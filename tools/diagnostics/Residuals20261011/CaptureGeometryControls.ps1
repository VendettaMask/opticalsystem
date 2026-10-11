param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$template = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/gaussian/512-real/request.json'
$nativeHost = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/host/NativeEnergyControl.exe'
$controls = @(
    @{name='chief-only';diagnosticApodizationFactor=10000},
    @{name='image-shift';diagnosticApodizationFactor=10000;diagnosticImageThicknessDelta=3},
    @{name='half-chief';diagnosticApodizationFactor=10000;diagnosticApertureScale=.5},
    @{name='cooke-chief';diagnosticApodizationFactor=10000;source='validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/cooke-32/source.ZMX'},
    @{name='tessar-chief';diagnosticApodizationFactor=10000;source='validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/tessar-32/source.ZMX'},
    @{name='half-aperture';diagnosticApertureScale=.5},
    @{name='on-axis-only';diagnosticOnAxisOnly=$true},
    @{name='single-wave';diagnosticSingleWavelength=$true},
    @{name='cooke';source='validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/cooke-32/source.ZMX'},
    @{name='tessar';source='validation/zemax/2026-r1/fft-pupil-phase-2026-10-09/tessar-32/source.ZMX'}
)
foreach ($control in $controls) {
    $types = if ($control.source) { @('Real','Imaginary','Linear') } else { @('Real','Imaginary') }
    foreach ($type in $types) {
        $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in $control.Keys | Where-Object { $_ -like 'diagnostic*' }) { $request[$key] = $control[$key] }
        if ($control.source) { $request.input = Join-Path $repository $control.source }
        $request.zemaxSettings.Type = $type
        $directory = Join-Path $outputRoot ($control.name + '-' + $type.ToLowerInvariant())
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
        Write-Output ('Captured ' + $control.name + '/' + $type)
    }
}
$files = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
