#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
if (-not $output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Verification output must be a fresh directory inside the repository.'
}
if (Test-Path -LiteralPath $output) { throw 'Previous verification output must not be overwritten.' }
[void][IO.Directory]::CreateDirectory($output)
$rows = [Collections.Generic.List[object]]::new()
function Assert-Check([string]$Name, [bool]$Passed, [object]$Detail) {
    $rows.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    [IO.File]::WriteAllText((Join-Path $output 'checks.json'), ($rows.ToArray() | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    if (-not $Passed) { throw "Package verification failed: $Name" }
}

$manifest = Join-Path $source 'release.files.sha256'
$count = 0
foreach ($line in [IO.File]::ReadAllLines($manifest)) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'Invalid manifest row.' }
    $expected = $Matches[1]; $relative = $Matches[2]
    $path = [IO.Path]::GetFullPath((Join-Path $source $relative))
    if (-not $path.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escapes package.' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected) { throw "Manifest mismatch: $relative" }
    $count++
}
Assert-Check 'complete-payload-manifest' ($count -gt 0) $count
$independent = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Name -match '^OptilandWorkbench\.(InitialStructure|CoatingDesign)\.App\.(exe|dll)$' })
Assert-Check 'no-independent-module-applications' ($independent.Count -eq 0) $independent.Count
foreach ($notice in @('TFStudio-LICENSE.txt', 'tmmcore-LICENSE.txt', 'MATERIAL-NOTICE.txt')) {
    Assert-Check "license-$notice" (Test-Path -LiteralPath (Join-Path $source "Licenses\$notice")) $notice
}

# Test only a disposable copy. The completed release and the user's installation stay untouched.
$copy = Join-Path $output 'package copy'
Copy-Item -LiteralPath $source -Destination $copy -Recurse
$exe = Join-Path $copy 'OptilandWorkbench.App.exe'
$report = Join-Path $output 'native-product.json'
$process = Start-Process -FilePath $exe -ArgumentList @('--verify-installation', ('"{0}"' -f $report)) -WorkingDirectory $output -WindowStyle Hidden -Wait -PassThru
Assert-Check 'self-contained-executable' ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $report)) $process.ExitCode
$native = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
Assert-Check 'both-modules-in-main-process' ($native.Success -and $native.ModulesInProcess -and
    ($native.Modules -join ',') -eq 'InitialStructure,CoatingDesign' -and $native.ModuleLibraries.Count -eq 5 -and -not $native.UserDataWritten) $native

foreach ($library in $native.ModuleLibraries) {
    $path = [IO.Path]::GetFullPath((Join-Path $copy ($library + '.dll')))
    if (-not $path.StartsWith($copy + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid module target.' }
    $held = $path + '.verification-held'
    Move-Item -LiteralPath $path -Destination $held
    try {
        $missingReport = Join-Path $output ($library + '-missing.json')
        $errorLog = Join-Path $output ($library + '-missing.stderr.txt')
        $process = Start-Process -FilePath $exe -ArgumentList @('--verify-installation', ('"{0}"' -f $missingReport)) -WorkingDirectory $output -WindowStyle Hidden -Wait -PassThru -RedirectStandardError $errorLog
        Assert-Check "missing-$library-rejected" ($process.ExitCode -eq 75 -and -not (Test-Path -LiteralPath $missingReport)) $process.ExitCode
    } finally { Move-Item -LiteralPath $held -Destination $path }
}
$summary = [ordered]@{ Passed = @($rows | Where-Object Passed).Count; Failed = @($rows | Where-Object { -not $_.Passed }).Count;
    ManifestFiles = $count; Source = $source; NativeGuiVerified = $false; UserInstallationModified = $false }
[IO.File]::WriteAllText((Join-Path $output 'summary.json'), ($summary | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$summary
