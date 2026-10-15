#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SetupPath,
      [Parameter(Mandatory)][string]$ReleaseMetadata,
      [Parameter(Mandatory)][string]$LegacySetupPath,
      [string]$DowngradeSetupPath = '',
      [string]$RecoverySetupPath = '',
      [string]$PartialCopySetupPath = '',
      [switch]$VerifyCleanup,
      [string]$OutputDirectory = '.tmp\windows-upgrade-validation')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$metadata = Get-Content -Raw -LiteralPath $ReleaseMetadata | ConvertFrom-Json
if (-not $metadata.TestPackage -or $metadata.ProductAppId -notlike 'OpticalSystemDesign-UpgradeValidation-*') {
    throw 'Validation requires an isolated unsigned test package. The production installation must never be used.'
}
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $output.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Validation output must be inside this workspace.'
}
if (Test-Path -LiteralPath $output) { throw 'Use a fresh validation directory; previous evidence must not be overwritten.' }
[void][IO.Directory]::CreateDirectory($output)
$install = Join-Path $output 'installed app'
$group = $metadata.ProductAppId
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($metadata.ProductAppId)_is1"
if (Test-Path -LiteralPath $uninstallKey) { throw 'The validation AppId already has an installation. Use a fresh AppId.' }
$rows = [Collections.Generic.List[object]]::new()

function Assert-Check {
    param([string]$Name, [bool]$Passed, [object]$Detail)
    $rows.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    [IO.File]::WriteAllText((Join-Path $output 'checks.json'), ($rows.ToArray() | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    if (-not $Passed) { throw "Installation validation failed: $Name ($Detail)" }
}
function Run-Setup {
    param([string]$Executable, [string]$Label, [string[]]$Extra = @())
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',
        ('/DIR="{0}"' -f $install), ('/GROUP="{0}"' -f $group),
        ('/LOG="{0}"' -f (Join-Path $output "$Label.log"))) + $Extra
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
function File-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Read-Association {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\$group.Project\shell\open\command")
    try { return $key.GetValue('') } finally { $key.Dispose() }
}
function Run-Product {
    param([string]$Executable, [string]$Label, [string[]]$Extra = @())
    $report = Join-Path $output "$Label.json"
    $arguments = @('--verify-installation', ('"{0}"' -f $report), '--project', ('"{0}"' -f $project),
        '--settings', ('"{0}"' -f $settings)) + $Extra
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Assert-Check "$Label-process" ($process.ExitCode -eq 0) $process.ExitCode
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    Assert-Check "$Label-native-product" ($result.Success -and $result.ProjectOpened -and $result.SettingsLoaded -and -not $result.UserDataWritten) $result
    if ($metadata.PSObject.Properties.Name -contains 'RequiredModules' -and
        $Executable.Contains([IO.Path]::DirectorySeparatorChar + $metadata.ReleaseId + [IO.Path]::DirectorySeparatorChar)) {
        Assert-Check "$Label-mandatory-modules" ($result.ModulesInProcess -and
            ($result.Modules -join ',') -eq ($metadata.RequiredModules -join ',') -and $result.ModuleLibraries.Count -eq 5) $result.ModuleLibraries
        foreach ($library in $result.ModuleLibraries) {
            Assert-Check "$Label-$library-installed" (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $Executable) ($library + '.dll'))) $library
        }
    }
}

Assert-Check 'legacy-install' ((Run-Setup $LegacySetupPath 'legacy-install') -eq 0) '1.0.0 flat layout'
$legacyExe = Join-Path $install 'OpticalSystemDesign.App.exe'
Assert-Check 'legacy-payload-present' (Test-Path -LiteralPath $legacyExe) $legacyExe
$legacyHash = File-Hash $legacyExe
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$group\Optical System Design.lnk"
$shortcutHash = File-Hash $shortcut
# This isolated ProgId is created by this harness, never the user's .staropt key.
$associationPath = "Software\Classes\$group.Project"
$association = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$associationPath\shell\open\command")
$legacyCommand = '"{0}" "%1"' -f $legacyExe
$association.SetValue('', $legacyCommand)
$association.Dispose()
$project = Join-Path $install 'user-kept-project.staropt'
$settings = Join-Path $output 'old-settings.json'
# A real bundled STAROPT project plus legacy settings, outside installer-owned payload.
$sourceProject = Get-ChildItem -LiteralPath (Join-Path $install 'LensLibrary\projects') -Filter '*.staropt' | Select-Object -First 1
Copy-Item -LiteralPath $sourceProject.FullName -Destination $project
[IO.File]::WriteAllText($settings, '{"Theme":"Dark","DecimalPlaces":5,"LeftPaneWidth":400}', [Text.UTF8Encoding]::new($false))
$projectHash = File-Hash $project
$settingsHash = File-Hash $settings
Run-Product $legacyExe 'legacy-native-read'
$legacyLock = [IO.File]::Open($legacyExe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try {
    Assert-Check 'legacy-exclusive-file-lock-blocked' ((Run-Setup $SetupPath 'legacy-lock-blocked') -ne 0) 'Compatibility guard for older binaries without a presence marker'
} finally { $legacyLock.Dispose() }

Assert-Check 'upgrade-before-activation-failure' ((Run-Setup $SetupPath 'fail-before' @('/TESTFAILBEFOREACTIVATION=1')) -eq 70) 'Explicit failure exit 70 before switching release'
Assert-Check 'legacy-preserved-after-failure' ((File-Hash $legacyExe) -eq $legacyHash) $legacyHash
Assert-Check 'failed-upgrade-has-no-active-state' (-not (Test-Path -LiteralPath (Join-Path $install 'release-state.ini'))) 'Old flat layout retained'

Assert-Check 'upgrade-after-activation-failure' ((Run-Setup $SetupPath 'fail-after' @('/TESTFAILAFTERACTIVATION=1')) -eq 70) 'Explicit failure exit 70 after switching from legacy layout'
Assert-Check 'activation-restored-to-legacy' (-not (Test-Path -LiteralPath (Join-Path $install 'release-state.ini'))) 'Old flat activation restored'
Assert-Check 'legacy-byte-preserved-after-activation-failure' ((File-Hash $legacyExe) -eq $legacyHash) $legacyHash
Assert-Check 'legacy-shortcut-restored' ((File-Hash $shortcut) -eq $shortcutHash) $shortcutHash
Assert-Check 'legacy-association-restored' ((Read-Association) -eq $legacyCommand) (Read-Association)
Assert-Check 'legacy-display-version-restored' ((Get-ItemPropertyValue -LiteralPath $uninstallKey -Name DisplayVersion) -eq '1.0.0') '1.0.0'
Run-Product $legacyExe 'recovered-legacy-native-read'

# Inno rolls back failed owned copies. If any verified files were retained,
# same-package retry validates them before reuse instead of overwriting them.
Assert-Check 'real-version-upgrade' ((Run-Setup $SetupPath 'upgrade') -eq 0) '1.0.0 -> 1.0.1'
$currentExe = Join-Path $install "versions\$($metadata.ReleaseId)\OpticalSystemDesign.App.exe"
Assert-Check 'new-payload-present' (Test-Path -LiteralPath $currentExe) $currentExe
Run-Product $currentExe 'upgraded-native-read'
$newCommand = '"{0}" "%1"' -f $currentExe
Assert-Check 'association-upgraded' ((Read-Association) -eq $newCommand) (Read-Association)
$shell = New-Object -ComObject WScript.Shell
try { $shortcutTarget = $shell.CreateShortcut($shortcut).TargetPath }
finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
Assert-Check 'shortcut-upgraded' ($shortcutTarget -eq $currentExe) $shortcutTarget
Assert-Check 'legacy-payload-retained' ((File-Hash $legacyExe) -eq $legacyHash) $legacyHash
$state = Join-Path $install 'release-state.ini'
$stateHash = File-Hash $state
$upgradedShortcutHash = File-Hash $shortcut
if ($RecoverySetupPath) {
    Assert-Check 'versioned-activation-failure' ((Run-Setup $RecoverySetupPath 'versioned-fail-after' @('/TESTFAILAFTERACTIVATION=1')) -eq 70) 'Different release id, failure after replacing active state'
    Assert-Check 'versioned-state-restored' ((File-Hash $state) -eq $stateHash) $stateHash
    Assert-Check 'versioned-shortcut-restored' ((File-Hash $shortcut) -eq $upgradedShortcutHash) $upgradedShortcutHash
    Assert-Check 'versioned-association-restored' ((Read-Association) -eq $newCommand) (Read-Association)
}

Assert-Check 'same-package-repeat' ((Run-Setup $SetupPath 'repeat') -eq 0) 'Validated immutable release reused'
# Repeating an identical package must not replace its recovery pointer with itself.
Assert-Check 'same-package-state-stable' ((File-Hash $state) -eq $stateHash) $stateHash
if ($DowngradeSetupPath) {
    Assert-Check 'downgrade-blocked' ((Run-Setup $DowngradeSetupPath 'downgrade') -ne 0) '1.0.1 -> 1.0.0 rejected before copying'
    Assert-Check 'downgrade-state-preserved' ((File-Hash $state) -eq $stateHash) $stateHash
}

$readme = Join-Path (Split-Path -Parent $currentExe) 'README.txt'
$readmeBytes = [IO.File]::ReadAllBytes($readme)
try {
    [IO.File]::AppendAllText($readme, 'controlled corruption')
    Assert-Check 'corrupted-payload-repaired' ((Run-Setup $SetupPath 'corrupted-payload') -eq 0) 'Same-package repair replaces manifest-owned damaged files'
    Assert-Check 'corrupted-file-restored-exactly' ((File-Hash $readme) -eq (Get-FileHash -InputStream ([IO.MemoryStream]::new($readmeBytes)) -Algorithm SHA256).Hash) 'Original packaged bytes restored'
    Assert-Check 'corrupted-payload-state-preserved' ((File-Hash $state) -eq $stateHash) $stateHash
} finally { [IO.File]::WriteAllBytes($readme, $readmeBytes) }

$missingFile = Join-Path (Split-Path -Parent $currentExe) 'OpticalSystemDesign.Application.dll'
$missingHash = File-Hash $missingFile
Remove-Item -LiteralPath $missingFile -Force
Assert-Check 'missing-payload-repaired' ((Run-Setup $SetupPath 'missing-payload') -eq 0) 'Same-package repair restores missing installer-owned DLL'
Assert-Check 'missing-file-restored-exactly' ((File-Hash $missingFile) -eq $missingHash) $missingHash

if ($metadata.PSObject.Properties.Name -contains 'RequiredModules') {
    foreach ($module in @('InitialStructure.Contracts', 'InitialStructure.Engine', 'InitialStructure.Persistence', 'CoatingDesign.Engine', 'Modules')) {
        $moduleFile = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $currentExe) "OpticalSystemDesign.$module.dll"))
        if (-not $moduleFile.StartsWith($install + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test-owned module target.' }
        $moduleHash = File-Hash $moduleFile
        $held = Join-Path $output ("OpticalSystemDesign.$module.dll.held")
        Move-Item -LiteralPath $moduleFile -Destination $held
        try {
            Assert-Check "missing-$module-repaired" ((Run-Setup $SetupPath "missing-$module") -eq 0) 'Mandatory module restored by the one product installer'
            Assert-Check "missing-$module-restored-exactly" ((File-Hash $moduleFile) -eq $moduleHash) $moduleHash
        } finally {
            if (-not (Test-Path -LiteralPath $moduleFile)) { Copy-Item -LiteralPath $held -Destination $moduleFile }
        }
    }
    Run-Product $currentExe 'modules-repaired-native-read'
}

function Interrupt-TestSetup {
    param([string]$Executable, [string]$Label, [string]$PauseParameter)
    $marker = Join-Path $output "$Label.pid"
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',
        ('/DIR="{0}"' -f $install), ('/GROUP="{0}"' -f $group),
        ('/LOG="{0}"' -f (Join-Path $output "$Label.log")), ('/{0}="{1}"' -f $PauseParameter,$marker))
    $startedAt = [DateTime]::Now
    $wrapper = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert-Check "$Label-ready" (Test-Path -LiteralPath $marker) $marker
    $testProcessId = [int](Get-Content -Raw -LiteralPath $marker)
    $ownedProcess = Get-Process -Id $testProcessId -ErrorAction Stop
    # Only the exact PID published by this fresh isolated test package is killed.
    Assert-Check "$Label-owned-process" ($ownedProcess.StartTime -ge $startedAt.AddSeconds(-2) -and
        $ownedProcess.Path -like '*\is-*.tmp\*.tmp') $ownedProcess.Path
    Stop-Process -Id $testProcessId -Force
    $ownedProcess.WaitForExit()
    [void]$wrapper.WaitForExit(10000)
    Assert-Check "$Label-durable-journal" (Test-Path -LiteralPath (Join-Path $install '.install-recovery\pending.ini')) 'Hard kill bypassed cleanup'
}

Interrupt-TestSetup $SetupPath 'killed-before-copy' 'TESTPAUSEBEFORECOPY'
Assert-Check 'retry-recovers-before-copy-kill' ((Run-Setup $SetupPath 'recover-before-copy') -eq 0) 'Installer replays journal before starting a new transaction'
Assert-Check 'before-copy-state-preserved' ((File-Hash $state) -eq $stateHash) $stateHash
if ($RecoverySetupPath) {
    $beforeActivationShortcutHash = File-Hash $shortcut
    Interrupt-TestSetup $RecoverySetupPath 'killed-after-activation' 'TESTPAUSEAFTERACTIVATION'
    Run-Product $currentExe 'startup-replays-interrupted-activation'
    Assert-Check 'startup-restores-old-state-exactly' ((File-Hash $state) -eq $stateHash) $stateHash
    Assert-Check 'startup-restores-old-shortcut' ((File-Hash $shortcut) -eq $beforeActivationShortcutHash) $beforeActivationShortcutHash
    Assert-Check 'startup-restores-old-association' ((Read-Association) -eq $newCommand) (Read-Association)
    Assert-Check 'startup-recovery-journal-retired' (-not (Test-Path -LiteralPath (Join-Path $install '.install-recovery\pending.ini'))) 'Recovery completed'
    Interrupt-TestSetup $RecoverySetupPath 'killed-after-activation-retry' 'TESTPAUSEAFTERACTIVATION'
    Assert-Check 'retry-recovers-after-activation-kill' ((Run-Setup $SetupPath 'recover-after-activation') -eq 0) 'Setup recovers independently of application startup'
    Assert-Check 'retry-state-preserved' ((File-Hash $state) -eq $stateHash) $stateHash
}
if ($PartialCopySetupPath) {
    Interrupt-TestSetup $PartialCopySetupPath 'killed-during-copy' 'TESTPAUSEAFTERPARTIALCOPY'
    Assert-Check 'partial-copy-keeps-active-state' ((File-Hash $state) -eq $stateHash) $stateHash
    Run-Product $currentExe 'startup-recovers-partial-copy'
    Assert-Check 'partial-copy-startup-state-preserved' ((File-Hash $state) -eq $stateHash) $stateHash
    Assert-Check 'partial-copy-retry-completes' ((Run-Setup $SetupPath 'recover-partial-copy') -eq 0) 'Incomplete inactive payload does not replace the active version'
}

$setupGate = [Threading.Mutex]::new($false, "Local\$group-Application-Setup")
try {
    $blockedReport = Join-Path $output 'startup-blocked.json'
    $blocked = Start-Process -FilePath $currentExe -ArgumentList @('--verify-installation', ('"{0}"' -f $blockedReport)) -WindowStyle Hidden -Wait -PassThru
    Assert-Check 'startup-during-setup-blocked' ($blocked.ExitCode -eq 75 -and -not (Test-Path -LiteralPath $blockedReport)) $blocked.ExitCode
    $uninstaller = Get-ChildItem -LiteralPath $install -Filter 'unins*.exe' | Select-Object -First 1
    Assert-Check 'uninstall-during-setup-blocked' ((Run-Setup $uninstaller.FullName 'setup-uninstall-blocked') -ne 0) 'Setup gate prevents concurrent uninstall'
} finally { $setupGate.Dispose() }

if ($VerifyCleanup) {
    # These handles intentionally have no session-local mutex. They exercise the
    # same directory protocol used across login sessions, not an actual second login.
    $fileGate = [IO.File]::Open((Join-Path $install '.setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $report = Join-Path $output 'file-gate-blocked.json'
        $blocked = Start-Process -FilePath $currentExe -ArgumentList @('--verify-installation', ('"{0}"' -f $report)) -WindowStyle Hidden -Wait -PassThru
        Assert-Check 'file-setup-gate-blocks-startup' ($blocked.ExitCode -eq 75 -and -not (Test-Path -LiteralPath $report)) $blocked.ExitCode
        Assert-Check 'file-setup-gate-blocks-setup' ((Run-Setup $SetupPath 'file-setup-blocked') -ne 0) 'No Local mutex held'
        Assert-Check 'file-setup-gate-blocks-uninstall' ((Run-Setup $uninstaller.FullName 'file-setup-uninstall-blocked') -ne 0) 'No Local mutex held'
    } finally { $fileGate.Dispose() }
    $fileGate = [IO.File]::Open((Join-Path $install '.application.lock'), 'OpenOrCreate', 'ReadWrite', 'ReadWrite')
    try {
        Assert-Check 'file-app-gate-blocks-setup' ((Run-Setup $SetupPath 'file-app-blocked') -ne 0) 'No Local mutex held'
        Assert-Check 'file-app-gate-blocks-uninstall' ((Run-Setup $uninstaller.FullName 'file-app-uninstall-blocked') -ne 0) 'No Local mutex held'
    } finally { $fileGate.Dispose() }
}

$runningReport = Join-Path $output 'running.json'
$running = Start-Process -FilePath $currentExe -ArgumentList @('--verify-installation', ('"{0}"' -f $runningReport),
    '--hold-milliseconds', '15000') -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $runningReport) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert-Check 'running-product-ready' (Test-Path -LiteralPath $runningReport) $running.Id
    Assert-Check 'running-upgrade-blocked' ((Run-Setup $SetupPath 'running-blocked') -ne 0) 'Running process not terminated'
    Assert-Check 'running-uninstall-blocked' ((Run-Setup $uninstaller.FullName 'running-uninstall-blocked') -ne 0) 'Running process not terminated'
    Assert-Check 'running-product-not-killed' (-not $running.HasExited) $running.Id
} finally { $running.WaitForExit() }

Run-Product $currentExe 'recovered-native-read'
Assert-Check 'project-byte-preserved' ((File-Hash $project) -eq $projectHash) $projectHash
Assert-Check 'settings-byte-preserved' ((File-Hash $settings) -eq $settingsHash) $settingsHash

if ($VerifyCleanup) {
    if (-not $RecoverySetupPath -or -not $PartialCopySetupPath) { throw 'Cleanup validation requires three different release IDs.' }
    $firstExe = $currentExe
    $firstDirectory = Split-Path -Parent $firstExe
    $receipt = Join-Path $install ".installed-releases\$($metadata.ReleaseId).ini"
    Assert-Check 'committed-release-has-cleanup-receipt' (Test-Path -LiteralPath $receipt) $receipt
    Assert-Check 'cleanup-second-release-install' ((Run-Setup $RecoverySetupPath 'cleanup-second') -eq 0) 'First release becomes previous'
    $active = ([IO.File]::ReadAllLines($state) | Where-Object { $_ -like 'Active=*' }).Substring(7)
    $secondExe = Join-Path $install "versions\$active\OpticalSystemDesign.App.exe"
    Run-Product $secondExe 'cleanup-keeps-previous'
    Assert-Check 'previous-release-preserved' (Test-Path -LiteralPath $firstExe) $firstExe
    $extra = Join-Path $firstDirectory 'user-extra.staropt'
    [IO.File]::WriteAllText($extra, 'test-owned extra user data')
    Assert-Check 'cleanup-third-release-install' ((Run-Setup $PartialCopySetupPath 'cleanup-third') -eq 0) 'Second release becomes previous'
    $active = ([IO.File]::ReadAllLines($state) | Where-Object { $_ -like 'Active=*' }).Substring(7)
    $currentExe = Join-Path $install "versions\$active\OpticalSystemDesign.App.exe"
    Run-Product $currentExe 'cleanup-keeps-user-files'
    Assert-Check 'user-extra-preserves-whole-old-release' ((Test-Path -LiteralPath $firstExe) -and [IO.File]::ReadAllText($extra) -eq 'test-owned extra user data') $extra
    # Remove only this harness-created fixture, inside the validated test root.
    Remove-Item -LiteralPath $extra
    Run-Product $currentExe 'cleanup-old-release'
    Assert-Check 'old-owned-payload-removed' (-not (Test-Path -LiteralPath $firstDirectory)) $firstDirectory
    Assert-Check 'old-receipt-retired' (-not (Test-Path -LiteralPath $receipt)) $receipt
    Assert-Check 'active-and-previous-retained' ((Test-Path -LiteralPath $currentExe) -and (Test-Path -LiteralPath $secondExe)) 'Two newest releases'
    Assert-Check 'cleanup-preserves-project-and-settings' ((File-Hash $project) -eq $projectHash -and (File-Hash $settings) -eq $settingsHash) 'Exact original bytes'
}

$uninstaller = Get-ChildItem -LiteralPath $install -Filter 'unins*.exe' | Select-Object -First 1
Assert-Check 'uninstall' ((Run-Setup $uninstaller.FullName 'uninstall') -eq 0) 'Owned installation only'
Assert-Check 'new-executable-removed' (-not (Test-Path -LiteralPath $currentExe)) $currentExe
Assert-Check 'registration-removed' (-not (Test-Path -LiteralPath $uninstallKey)) $uninstallKey
Assert-Check 'user-project-survives-uninstall' ((File-Hash $project) -eq $projectHash) $projectHash
Assert-Check 'user-settings-survive-uninstall' ((File-Hash $settings) -eq $settingsHash) $settingsHash
# Only the exact test-owned isolated association is removed; no production key.
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($associationPath, $false)
$inputs = @($SetupPath, $LegacySetupPath, $DowngradeSetupPath, $RecoverySetupPath, $PartialCopySetupPath, $ReleaseMetadata) |
    Where-Object { $_ } | ForEach-Object { [ordered]@{ Path = $_; Sha256 = File-Hash $_ } }
[IO.File]::WriteAllText((Join-Path $output 'summary.json'), ([ordered]@{ Passed = $rows.Count; Failed = 0;
    ProductAppId = $metadata.ProductAppId; ReleaseId = $metadata.ReleaseId; Runtime = $metadata.Runtime;
    Signed = $false; GuiVerified = $false; Arm64HardwareVerified = $false; Inputs = @($inputs);
    Checks = $rows.ToArray() } | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "Installation validation passed: $($rows.Count) checks. Evidence: $output"
