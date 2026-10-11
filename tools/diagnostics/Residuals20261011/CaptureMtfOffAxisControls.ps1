param([Parameter(Mandatory)][string]$OutputDirectory, [string]$NativeHostDirectory)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
if (!$NativeHostDirectory) { $NativeHostDirectory = Join-Path $repository 'artifacts/validation/huygens-offaxis-20261011/host' }
$nativeHost = Join-Path $NativeHostDirectory 'NativeEnergyControl.exe'
$source = Join-Path $repository 'validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8') { throw 'Source identity differs.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot 'source.ZMX')
$psf = Join-Path $repository 'validation/zemax/2026-r1/huygens-offaxis-2026-10-11/psf/field3-uniform-linear/request.json'
$scan = Join-Path $repository 'validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/huygens-mtf-vs-field-c1/captured-settings.json'
foreach ($name in @('positive-chief','positive-centroid','negative-centroid','scan-plus','scan-minus','scan-three','mfe-positive','mfe-negative')) {
    if ($name -like 'scan-*') {
        $request = (Get-Content -LiteralPath $scan -Raw | ConvertFrom-Json -AsHashtable).request
        $request.zemaxSettings.ScanType = if ($name -eq 'scan-minus') { 'Minus_Y' } else { 'Plus_Y' }
        if ($name -eq 'scan-three') {
            $request.zemaxSettings.FieldDensity = 3
            $request.workbenchSettings.FieldDensity = '3'
            $request.workbenchSettings.FieldPointCount = '3'
        }
    } else {
        $request = Get-Content -LiteralPath $psf -Raw | ConvertFrom-Json -AsHashtable
        $request.zemaxSettings.UseCentroid = $name -like '*centroid'
        if ($name -like 'positive-*' -or $name -eq 'mfe-positive') { $request.diagnosticFieldCoordinates = @(@{field=3;x=0;y=15}) }
    }
    $request.adapter = 'huygens-methods'
    if ($name -like 'mfe-*') { $request.adapter = 'diagnostic-huygens-mtf' }
    $request.diagnosticHuygensMethods = @('Auto','Planar','Spherical')
    $request.input = Join-Path $outputRoot 'source.ZMX'
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
$files = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
