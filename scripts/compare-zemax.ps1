[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputFile,
    [string]$OutputDirectory,
    [string]$Configuration,
    [string[]]$Analysis,
    [string]$OpticalConfiguration = '1',
    [string]$ZosApiPath,
    [int]$Timeout = 120,
    [ValidateSet('none', 'error', 'difference')][string]$FailOn = 'difference',
    [switch]$CaptureScreenshots,
    [switch]$KeepRaw,
    [switch]$Overwrite
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repositoryRoot 'tools/OptilandWorkbench.ZemaxComparison/OptilandWorkbench.ZemaxComparison.csproj'
if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw "ZMX input not found: $InputFile" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 10 SDK is required.' }

& dotnet restore $toolProject --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $toolProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$arguments = @('--input', (Resolve-Path -LiteralPath $InputFile).Path, '--configuration', $OpticalConfiguration,
    '--timeout', $Timeout.ToString([Globalization.CultureInfo]::InvariantCulture), '--fail-on', $FailOn)
if ($OutputDirectory) { $arguments += @('--output', $OutputDirectory) }
if ($Configuration) { $arguments += @('--config', $Configuration) }
if ($ZosApiPath) { $arguments += @('--zos-api-path', $ZosApiPath) }
foreach ($key in $Analysis) { $arguments += @('--analysis', $key) }
if (-not $Analysis) { $arguments += '--all' }
if ($CaptureScreenshots) { $arguments += '--capture-screenshots' }
if ($KeepRaw) { $arguments += '--keep-raw' }
if ($Overwrite) { $arguments += '--overwrite' }
& dotnet (Join-Path (Split-Path -Parent $toolProject) 'bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll') @arguments
exit $LASTEXITCODE
