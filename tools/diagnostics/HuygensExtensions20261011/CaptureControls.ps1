param([Parameter(Mandatory)][string]$OutputDirectory, [string]$NativeHostDirectory, [switch]$ExtraScalarControls)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$source = Join-Path $repository 'validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX'
$sourceSha = '8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceSha) { throw 'Source identity differs.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot 'source.ZMX')
if (!$NativeHostDirectory) { $NativeHostDirectory = Join-Path $repository 'artifacts/validation/huygens-offaxis-20261011/host-pupil' }
$nativeHost = Join-Path $NativeHostDirectory 'NativeEnergyControl.exe'
$template = Join-Path $repository 'validation/zemax/2026-r1/huygens-offaxis-2026-10-11/psf/field2-uniform-linear/request.json'
$kinds = if ($ExtraScalarControls) { @('spherical64','spherical8','spherical-gaussian32') } else { @('spherical','polarized','polychromatic') }
foreach ($kind in $kinds) {
    foreach ($field in @(1,3)) {
        $types = if ($kind -eq 'spherical') { @('Linear','Real','Imaginary') } else { @('Linear') }
        foreach ($type in $types) {
            $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
            $request.input = Join-Path $outputRoot 'source.ZMX'
            $request.field = $field
            $request.zemaxSettings.Field = $field
            $request.zemaxSettings.Type = $type
            $request.imageDeltaMicrometers = if ($kind -like 'spherical*') { 4 } else { .25 }
            if ($kind -eq 'spherical64') {
                $request.pupilSampling = 64
                $request.zemaxSettings.PupilSampleSize = 'S_64x64'
                $request.imageDeltaMicrometers = 2
            }
            if ($kind -eq 'spherical8') { $request.imageDeltaMicrometers = 8 }
            if ($kind -eq 'spherical-gaussian32') { $request.diagnosticApodizationFactor = 32 }
            $request.zemaxSettings.ImageDelta = $request.imageDeltaMicrometers
            $request.polarization = $kind -eq 'polarized'
            $request.zemaxSettings.UsePolarization = $request.polarization
            if ($kind -eq 'polychromatic') {
                $request.wavelength = 0
                $request.zemaxSettings.Wavelength = 0
                $request.wavelengthScope = 'All'
            }
            $name = $kind + '-field' + $field + '-' + $type.ToLowerInvariant()
            $directory = Join-Path $outputRoot $name
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
                if (!$process.WaitForExit(120000)) { $process.Kill(); throw ('Capture timed out: ' + $name) }
                if ($process.ExitCode -ne 0) { throw ('Capture failed: ' + $name) }
            } finally { $process.Dispose() }
            Write-Output ('Captured ' + $name)
        }
    }
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceSha) { throw 'Source changed during capture.' }
$files = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
