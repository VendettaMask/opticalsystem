#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '',
    [string]$InnoSetupCompiler = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\installers'
} elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$buildName = 'OpticalSystemDesign-{0}-{1}-{2}' -f $Runtime, (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$buildDirectory = Join-Path $OutputRoot $buildName
$stagingDirectory = "$buildDirectory.partial"
if (-not $PSCmdlet.ShouldProcess($buildDirectory, 'Build a self-contained EXE with a Chinese installation wizard')) {
    return
}

$compiler = & (Join-Path $PSScriptRoot 'get-inno-setup.ps1') -CompilerPath $InnoSetupCompiler
[void][IO.Directory]::CreateDirectory($stagingDirectory)
Write-Host '[1/2] Publishing the current application and resources...'
$package = & (Join-Path $PSScriptRoot 'publish-windows.ps1') -Runtime $Runtime `
    -OutputRoot (Join-Path $stagingDirectory 'payload') -PassThru -CompactName
if ($null -eq $package -or -not (Test-Path -LiteralPath $package.Directory -PathType Container)) {
    throw 'The application publish did not return a complete package.'
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package.Directory 'OptilandWorkbench.App.dll'))
$appVersion = '{0}.{1}.{2}' -f $version.FileMajorPart, $version.FileMinorPart, $version.FileBuildPart
$outputName = "OpticalSystemDesign-$appVersion-$Runtime-Setup"
$setupPath = Join-Path $stagingDirectory "$outputName.exe"
$script = Join-Path $repositoryRoot 'packaging\windows\OpticalSystemDesign.iss'
Write-Host '[2/2] Compiling the installation wizard...'
& $compiler '/Qp' "/DPublishDir=$($package.Directory)" "/DAppVersion=$appVersion" "/DTargetRuntime=$Runtime" `
    "/O$stagingDirectory" "/F$outputName" $script | Out-Host
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer compilation failed (exit $LASTEXITCODE). Output retained at: $stagingDirectory"
}
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setupPath.sha256", "$hash  $outputName.exe`r`n", [Text.Encoding]::ASCII)
[IO.Directory]::Move($stagingDirectory, $buildDirectory)
Write-Host ''
Write-Host 'Installer completed. Distribute this Setup EXE; it contains all required application files:'
Write-Host (Join-Path $buildDirectory "$outputName.exe")
Write-Host 'The installer is unsigned. Building it does not install or start the application.'
