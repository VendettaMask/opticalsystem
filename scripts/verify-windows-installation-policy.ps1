#requires -Version 5.1
[CmdletBinding()]
param([string]$InnoSetupCompiler = '', [string]$OutputDirectory = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$nonce = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$productAppId = "OpticalSystemDesign-UpgradeValidation-Policy-$nonce"
$fixtureName = "OpticalSystemDesign Policy $nonce"
if (-not $OutputDirectory) { $OutputDirectory = "artifacts\installation-policy-$nonce" }
$output = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
if (-not $output.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $output)) { throw 'Use a fresh evidence directory inside this workspace.' }
[void][IO.Directory]::CreateDirectory($output)
function Assert-OwnedPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Not a test-owned path: $resolved"
    }
    return $resolved
}
$compiler = & (Join-Path $PSScriptRoot 'get-inno-setup.ps1') -CompilerPath $InnoSetupCompiler
$registryPath = "Software\Microsoft\Windows\CurrentVersion\Uninstall\${productAppId}_is1"
$registryProviderPath = "HKCU:\$registryPath"
if (Test-Path -LiteralPath $registryProviderPath) { throw 'The fresh test identity unexpectedly exists.' }
$checks = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [bool]$Passed, [object]$Detail = '') {
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    [IO.File]::WriteAllText((Join-Path $output 'checks.json'), ($checks.ToArray() | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    if (-not $Passed) { throw "Installation policy failed: $Name ($Detail). Evidence retained at $output" }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Read-Registration { Get-ItemProperty -LiteralPath $registryProviderPath }
function Set-TestRegistration([string]$Name, [string]$Value) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath, $true)
    try { $key.SetValue($Name, $Value, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
}
function Run-Setup([string]$Executable, [string]$Label, [string]$Directory, [string]$Group, [string[]]$Extra = @()) {
    [void](Assert-OwnedPath $Directory)
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/LANG=english',
        ('/DIR="{0}"' -f $Directory), ('/GROUP="{0}"' -f $Group), ('/LOG="{0}"' -f (Join-Path $output "$Label.log"))) + $Extra
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { throw 'Test setup exceeded 60 seconds; no process was force-terminated.' }
    return $process.ExitCode
}
function Mode-Logged([string]$Label, [string]$Mode) {
    [IO.File]::ReadAllText((Join-Path $output "$Label.log")).Contains("INSTALL-POLICY: Mode=$Mode;")
}
function Uninstall-Fixture([string]$Directory, [string]$Label) {
    [void](Assert-OwnedPath $Directory)
    $uninstaller = @(Get-ChildItem -LiteralPath $Directory -Filter 'unins*.exe')
    Check "$Label-one-uninstaller" ($uninstaller.Count -eq 1) $uninstaller.Count
    Check "$Label-exit" ((Run-Setup $uninstaller[0].FullName $Label $Directory $productAppId) -eq 0)
    Check "$Label-registration-removed" (-not (Test-Path -LiteralPath $registryProviderPath))
}
function Shortcut-Target([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    try { $shell.CreateShortcut($Path).TargetPath }
    finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}

# Tiny fixtures exercise the actual production installer and its include files.
# They intentionally cannot launch the optical application and are never release packages.
$payload = Join-Path $output 'fixture payload'
[void][IO.Directory]::CreateDirectory((Join-Path $payload 'Assets\Brand'))
foreach ($name in @('OpticalSystemDesign.App.exe', 'OpticalSystemDesign.Application.dll', 'README.txt',
    'OpticalSystemDesign.InitialStructure.Contracts.dll', 'OpticalSystemDesign.InitialStructure.Engine.dll',
    'OpticalSystemDesign.InitialStructure.Persistence.dll', 'OpticalSystemDesign.CoatingDesign.Engine.dll', 'OpticalSystemDesign.Modules.dll')) {
    [IO.File]::WriteAllText((Join-Path $payload $name), "Non-launchable isolated setup-policy fixture: $name", [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\OpticalSystemDesign.App\Assets\Brand\AppIcon.ico') -Destination (Join-Path $payload 'Assets\Brand\AppIcon.ico')
$manifest = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Hash $_.FullName).ToLowerInvariant(), $_.FullName.Substring($payload.Length + 1)
})
[IO.File]::WriteAllLines((Join-Path $payload 'release.files.sha256'), $manifest, [Text.UTF8Encoding]::new($false))
$manifestHash = (Hash (Join-Path $payload 'release.files.sha256')).ToLowerInvariant()
$install = Join-Path $output 'installed original'
$alternate = Join-Path $output 'must not exist'
$group = "$productAppId\Original"
$otherGroup = "$productAppId\Must not exist"
$programs = [Environment]::GetFolderPath('Programs')
$shortcut = Join-Path $programs "$group\$fixtureName.lnk"
$otherShortcuts = Join-Path $programs $otherGroup
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) "$fixtureName.lnk"
Check 'test-shortcuts-not-preexisting' (-not (Test-Path -LiteralPath $desktop) -and -not (Test-Path -LiteralPath (Join-Path $programs $productAppId)))

$packages = @{}
foreach ($variant in @(@('base', '1.0.1'), @('repacked', '1.0.1'), @('newer', '1.0.2'), @('older', '0.9.9'))) {
    $releaseId = "$($variant[1])-win-x64-policy-$($variant[0])"
    $arguments = @('/Qp', "/DPublishDir=$payload", "/DAppVersion=$($variant[1])", '/DTargetRuntime=win-x64',
        "/DProductAppId=$productAppId", "/DAppName=$fixtureName", "/DReleaseId=$releaseId", "/DManifestHash=$manifestHash",
        "/O$output", "/F$($variant[0])")
    # Only the failure-injection variant enables hooks; ordinary flows compile
    # and execute the same no-hook code path as production packages.
    if ($variant[0] -eq 'repacked') { $arguments += '/DValidationHooks=1' }
    & $compiler @arguments (Join-Path $repositoryRoot 'packaging\windows\OpticalSystemDesign.iss') |
        Out-File -LiteralPath (Join-Path $output "compile-$($variant[0]).log") -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Native installer compilation failed: $($variant[0])" }
    $packages[$variant[0]] = [pscustomobject]@{ Path = Join-Path $output "$($variant[0]).exe"; Release = $releaseId }
}
& $compiler '/Qp' "/DPublishDir=$payload" "/DProductAppId=$productAppId" "/DAppName=$fixtureName" "/DFixtureDirectory=$install" "/O$output" '/Fflat' `
    (Join-Path $PSScriptRoot 'tests\fixtures\installation-policy-legacy.iss') |
    Out-File -LiteralPath (Join-Path $output 'compile-flat.log') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Flat-layout fixture compilation failed.' }

try {
Check 'fresh-install' ((Run-Setup $packages.base.Path 'fresh' $install $group) -eq 0)
Check 'fresh-mode-install' (Mode-Logged 'fresh' 'Install')
$state = Join-Path $install 'release-state.ini'
$stateHash = Hash $state
$project = Join-Path $install 'user-kept-project.staropt'
$settings = Join-Path $output 'user-kept-settings.json'
[IO.File]::WriteAllText($project, 'test-owned user project: preserve exact bytes')
[IO.File]::WriteAllText($settings, '{"Theme":"Dark","Language":"ja-JP"}')
$projectHash = Hash $project; $settingsHash = Hash $settings
Check 'fresh-shortcut' (Test-Path -LiteralPath $shortcut)
$shortcutHash = Hash $shortcut
Check 'same-version-repair' ((Run-Setup $packages.base.Path 'repair-alternate' $alternate $otherGroup @('/NOICONS', '/TASKS=desktopicon')) -eq 0)
Check 'same-version-mode-repair' (Mode-Logged 'repair-alternate' 'Repair')
Check 'alternate-directory-not-created' (-not (Test-Path -LiteralPath $alternate))
Check 'alternate-shortcuts-not-created' (-not (Test-Path -LiteralPath $otherShortcuts) -and -not (Test-Path -LiteralPath $desktop))
Check 'original-shortcut-retained' (Test-Path -LiteralPath $shortcut)
Check 'same-package-state-unchanged' ((Hash $state) -eq $stateHash)
Check 'original-directory-registration' ((Read-Registration).InstallLocation.TrimEnd('\') -eq $install)
$shortcutHash = Hash $shortcut
Check 'failed-maintenance-restored' ((Run-Setup $packages.repacked.Path 'fail-activation' $alternate $otherGroup @('/TESTFAILAFTERACTIVATION=1')) -eq 70)
Check 'failed-maintenance-state-and-shortcut-preserved' ((Hash $state) -eq $stateHash -and (Hash $shortcut) -eq $shortcutHash) `
    @{ State = Hash $state; ExpectedState = $stateHash; Shortcut = Hash $shortcut; ExpectedShortcut = $shortcutHash }
Check 'same-version-repacked-repair' ((Run-Setup $packages.repacked.Path 'repacked' $alternate $otherGroup) -eq 0 -and (Mode-Logged 'repacked' 'Repair'))
Check 'old-payload-preserved' (Test-Path -LiteralPath (Join-Path $install "versions\$($packages.base.Release)\OpticalSystemDesign.App.exe"))
Check 'newer-version-upgrade' ((Run-Setup $packages.newer.Path 'newer' $alternate $otherGroup) -eq 0 -and (Mode-Logged 'newer' 'Upgrade'))
$currentExe = Join-Path $install "versions\$($packages.newer.Release)\OpticalSystemDesign.App.exe"
Check 'shortcut-updates-in-place' ((Shortcut-Target $shortcut) -eq $currentExe)
Check 'only-one-test-registration' (@(Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { $_.PSChildName -like "*$nonce*" }).Count -eq 1)
$stateHash = Hash $state
Check 'downgrade-alternate-directory-blocked' ((Run-Setup $packages.older.Path 'downgrade' $alternate $otherGroup) -ne 0)
Check 'downgrade-does-not-change-active-state' ((Hash $state) -eq $stateHash -and (Read-Registration).DisplayVersion -eq '1.0.2')
Check 'downgrade-cannot-create-alternate-directory' (-not (Test-Path -LiteralPath $alternate))
Set-TestRegistration 'DisplayVersion' '0.9.0'
try { Check 'state-also-blocks-downgrade' ((Run-Setup $packages.older.Path 'state-downgrade' $alternate $otherGroup) -ne 0 -and (Hash $state) -eq $stateHash) }
finally { Set-TestRegistration 'DisplayVersion' '1.0.2' }
Set-TestRegistration 'DisplayVersion' 'invalid'
try { Check 'invalid-version-fails-closed' ((Run-Setup $packages.newer.Path 'invalid-version' $alternate $otherGroup) -ne 0) }
finally { Set-TestRegistration 'DisplayVersion' '1.0.2' }
Set-TestRegistration 'InstallLocation' $alternate
try { Check 'conflicting-path-records-fail-closed' ((Run-Setup $packages.newer.Path 'conflicting-records' $alternate $otherGroup) -ne 0) }
finally { Set-TestRegistration 'InstallLocation' ($install + '\') }
$module = Assert-OwnedPath (Join-Path (Split-Path -Parent $currentExe) 'OpticalSystemDesign.InitialStructure.Engine.dll')
$held = Assert-OwnedPath (Join-Path $output 'held-module.dll')
$moduleHash = Hash $module
Move-Item -LiteralPath $module -Destination $held
try {
    Check 'missing-module-repaired-in-original-location' ((Run-Setup $packages.newer.Path 'missing-module' $alternate $otherGroup) -eq 0 -and (Hash $module) -eq $moduleHash)
} finally { if (-not (Test-Path -LiteralPath $module)) { Copy-Item -LiteralPath $held -Destination $module } }
Check 'user-data-byte-preserved' ((Hash $project) -eq $projectHash -and (Hash $settings) -eq $settingsHash)
Uninstall-Fixture $install 'uninstall-versioned'
Check 'user-data-survives-test-uninstall' ((Hash $project) -eq $projectHash -and (Hash $settings) -eq $settingsHash)

$noicons = Join-Path $output 'no icons install'
Check 'fresh-noicons-install' ((Run-Setup $packages.base.Path 'noicons-fresh' $noicons $group @('/NOICONS', '/TASKS=desktopicon')) -eq 0)
Check 'noicons-original-shortcuts' (-not (Test-Path -LiteralPath $shortcut) -and (Test-Path -LiteralPath $desktop))
Check 'noicons-maintenance' ((Run-Setup $packages.base.Path 'noicons-repair' $alternate $otherGroup @('/MERGETASKS=!desktopicon')) -eq 0)
Check 'noicons-maintenance-preserved' (-not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath $otherShortcuts) -and
    (Test-Path -LiteralPath $desktop) -and (Shortcut-Target $desktop) -eq (Join-Path $noicons "versions\$($packages.base.Release)\OpticalSystemDesign.App.exe"))
Uninstall-Fixture $noicons 'uninstall-noicons'
Check 'test-desktop-removed' (-not (Test-Path -LiteralPath $desktop))

$flat = Join-Path $output 'flat original'
Check 'flat-fixture-install' ((Run-Setup (Join-Path $output 'flat.exe') 'flat-install' $flat $group) -eq 0)
$flatExe = Join-Path $flat 'OpticalSystemDesign.App.exe'; $flatHash = Hash $flatExe
Check 'flat-downgrade-blocked' ((Run-Setup $packages.older.Path 'flat-downgrade' $alternate $otherGroup) -ne 0)
Check 'flat-registration-preserved' ((Read-Registration).DisplayVersion -eq '1.0.0' -and -not (Test-Path -LiteralPath $alternate))
Check 'flat-upgrade-in-place' ((Run-Setup $packages.base.Path 'flat-upgrade' $alternate $otherGroup) -eq 0 -and (Mode-Logged 'flat-upgrade' 'Upgrade'))
Check 'flat-original-files-retained' ((Hash $flatExe) -eq $flatHash)
Check 'flat-shortcut-updated-in-place' ((Shortcut-Target $shortcut) -eq (Join-Path $flat "versions\$($packages.base.Release)\OpticalSystemDesign.App.exe"))
Uninstall-Fixture $flat 'uninstall-flat'
Check 'test-shortcut-removed' (-not (Test-Path -LiteralPath $shortcut))
Check 'all-alternate-locations-unused' (-not (Test-Path -LiteralPath $alternate) -and -not (Test-Path -LiteralPath $otherShortcuts))
$inputs = @(Get-ChildItem -LiteralPath $output -Filter '*.exe' | ForEach-Object { [ordered]@{ Name = $_.Name; Sha256 = Hash $_.FullName } })
$sources = @('OpticalSystemDesign.iss', 'InstallationRecovery.iss', 'InstallationPolicy.iss') | ForEach-Object {
    [ordered]@{ Path = "packaging/windows/$_"; Sha256 = Hash (Join-Path $repositoryRoot "packaging\windows\$_") }
}
$summary = [ordered]@{ Date = '2026-10-22'; ProductAppId = $productAppId; TestFixtureOnly = $true;
    ProductionInstallationModified = $false; GuiVerified = $false; MachineWideInstallExecuted = $false;
    Arm64HardwareVerified = $false; HarnessSha256 = Hash $PSCommandPath;
    Passed = $checks.Count; Failed = 0; Sources = @($sources); Inputs = $inputs; Checks = $checks.ToArray() }
[IO.File]::WriteAllText((Join-Path $output 'summary.json'), ($summary | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Write-Output "Installation policy passed: $($checks.Count) checks. Evidence: $output"
} finally {
    # Best-effort normal uninstall after a failed assertion, only for this fresh identity.
    # Keep all files/logs and test-created user documents; never recursively remove a root.
    if (Test-Path -LiteralPath $registryProviderPath) {
        $registeredRoot = [string](Read-Registration).InstallLocation
        [void](Assert-OwnedPath $registeredRoot)
        $cleanupUninstaller = @(Get-ChildItem -LiteralPath $registeredRoot -Filter 'unins*.exe')
        if ($cleanupUninstaller.Count -eq 1) {
            $cleanupExit = Run-Setup $cleanupUninstaller[0].FullName 'cleanup-failed-fixture' $registeredRoot $productAppId
            Write-Output "Isolated fixture cleanup exit: $cleanupExit. User-data fixtures and evidence retained."
        }
    }
}
