#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$OutputRoot = '',

    [switch]$PassThru,

    [switch]$CompactName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\OptilandWorkbench.App\OptilandWorkbench.App.csproj'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\windows'
} elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$packageName = 'OpticalSystemDesign-{0}-{1}-{2}' -f $Runtime, (Get-Date -Format 'yyyyMMdd-HHmmss'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
if ($CompactName) {
    # Installer staging already has a unique, descriptive parent directory.
    # Keep source paths short enough for Inno Setup 6's Win32 path limit.
    $packageName = 'app-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
}
$packageDirectory = Join-Path $OutputRoot $packageName
$stagingDirectory = "$packageDirectory.partial"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Application project not found: $project"
}
if (-not $PSCmdlet.ShouldProcess($packageDirectory, 'Publish a self-contained Release EXE and its resources')) {
    return
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is missing. Install the SDK version required by global.json, then retry.'
}

Push-Location -LiteralPath $repositoryRoot
try {
    Write-Host '[1/3] Checking locked dependencies and restoring the Windows runtime...'
    & dotnet restore $project --locked-mode --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency restore failed (exit $LASTEXITCODE). Check the SDK, NuGet connectivity and package cache."
    }
    # Committed locks are platform-neutral. RID-specific locks belong in each
    # project's obj directory, never overwrite the source-controlled lock files.
    & dotnet restore $project --runtime $Runtime -p:SelfContained=true --nologo `
        -p:RestoreLockedMode=false "-p:NuGetLockFilePath=obj/packages.$Runtime.lock.json" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Windows runtime restore failed (exit $LASTEXITCODE). No release was created."
    }

    # Each attempt gets a new directory. Never clean or overwrite a previous release.
    [void][System.IO.Directory]::CreateDirectory($stagingDirectory)
    Write-Host '[2/3] Publishing the self-contained Windows application...'
    & dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
        --no-restore --nologo --output $stagingDirectory `
        -p:UseAppHost=true -p:WindowsPackage=true -p:PublishSingleFile=false `
        -p:PublishTrimmed=false -p:PublishAot=false -p:DebugType=None -p:DebugSymbols=false | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed (exit $LASTEXITCODE). Incomplete output is retained at: $stagingDirectory"
    }

    Write-Host '[3/3] Checking the EXE, runtime and bundled resources...'
    $requiredFiles = @(
        'OptilandWorkbench.App.exe',
        'OptilandWorkbench.App.dll',
        'OptilandWorkbench.App.runtimeconfig.json',
        'coreclr.dll',
        'hostfxr.dll',
        'libSkiaSharp.dll',
        'LensLibrary\index.json',
        'Assets\Brand\AppIcon.ico',
        'Assets\Fonts\OFL-LICENSE.txt',
        'Assets\Icons\GAME-ICONS-LICENSE.txt',
        'Assets\Icons\FARM-FRESH-ICONS-LICENSE.txt'
    )
    foreach ($relativePath in $requiredFiles) {
        $filePath = Join-Path $stagingDirectory $relativePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or (Get-Item -LiteralPath $filePath).Length -eq 0) {
            throw "Published package is incomplete: $relativePath. Output retained at: $stagingDirectory"
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory 'LensLibrary\StockCatalogs') -PathType Container)) {
        throw "Manufacturer stock catalogs are missing. Output retained at: $stagingDirectory"
    }

    # This license is tracked but is not currently copied by the application project.
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\OptilandWorkbench.App\Assets\Icons\LUCIDE-LICENSE.txt') `
        -Destination (Join-Path $stagingDirectory 'Assets\Icons\LUCIDE-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\windows\README.txt') `
        -Destination (Join-Path $stagingDirectory 'README.txt')

    # Directory.Move refuses an existing destination instead of merging releases.
    [System.IO.Directory]::Move($stagingDirectory, $packageDirectory)
    Write-Host ''
    Write-Host 'Packaging completed. Copy the ENTIRE folder to the destination computer:'
    Write-Host $packageDirectory
    Write-Host 'Run this EXE (no separate .NET installation required):'
    Write-Host (Join-Path $packageDirectory 'OptilandWorkbench.App.exe')
    if ($PassThru) {
        [pscustomobject]@{ Directory = $packageDirectory; Runtime = $Runtime }
    }
} finally {
    Pop-Location
}
