param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$template = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/gaussian-fft-dee2/dee-4/request.json'
$nativeHost = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/host/NativeEnergyControl.exe'
$controls = @(
    @{name='sampling64';sampling=64},
    @{name='sampling128';sampling=128},
    @{name='half-aperture';key='diagnosticApertureScale';value=.5},
    @{name='on-axis-only';key='diagnosticOnAxisOnly';value=$true},
    @{name='single-wave';key='diagnosticSingleWavelength';value=$true},
    @{name='image-gap3';key='diagnosticImageThicknessDelta';value=3}
)
foreach ($control in $controls) {
    $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
    $request.Remove('diagnosticApodizationFactor')
    if ($control.ContainsKey('sampling')) {
        $request.zemaxSettings.SampleSize = 'S_' + $control.sampling + 'x' + $control.sampling
        $request.pupilSampling = $control.sampling
    }
    if ($control.ContainsKey('key')) { $request[$control.key] = $control.value }
    $directory = Join-Path $outputRoot $control.name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $requestFile = Join-Path $directory 'request.json'
    $request | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $requestFile -Encoding utf8
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $nativeHost
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add($requestFile)
    $start.ArgumentList.Add($directory)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(120000)) { $process.Kill(); throw ('Capture timed out: ' + $directory) }
        if ($process.ExitCode -ne 0) { throw ('Capture failed: ' + $directory) }
    } finally { $process.Dispose() }
    Write-Output ('Captured ' + $control.name)
}
$files = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
