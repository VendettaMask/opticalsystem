param([Parameter(Mandatory)][string]$OutputDirectory, [string]$NativeHostDirectory)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$source = Join-Path $repository 'validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/source.ZMX'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8') { throw 'Source identity differs.' }
Copy-Item -LiteralPath $source -Destination (Join-Path $outputRoot 'source.ZMX')
$template = Join-Path $repository 'validation/zemax/2026-r1/huygens-integration-2026-10-11/ms-l7-gaussian512/request.json'
if (!$NativeHostDirectory) { $NativeHostDirectory = Join-Path $repository 'artifacts/validation/huygens-offaxis-20261011/host' }
$nativeHost = Join-Path $NativeHostDirectory 'NativeEnergyControl.exe'
function Capture($request, $name) {
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
foreach ($field in @(2,3)) {
    foreach ($gaussian in @($false,$true)) {
        foreach ($type in @('Linear','Real','Imaginary')) {
            $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
            $request.input = Join-Path $outputRoot 'source.ZMX'
            $request.field = $field
            $request.zemaxSettings.Field = $field
            $request.zemaxSettings.Type = $type
            $request.diagnosticHuygensMethods = @('Auto','Planar','Spherical')
            if ($gaussian) { $request.diagnosticHuygensMethods = @('Planar') }
            if (!$gaussian) { $request.Remove('diagnosticApodizationFactor') }
            $request.imageSampling = if ($gaussian) { 64 } else { 32 }
            $request.zemaxSettings.ImageSampleSize = 'S_' + $request.imageSampling + 'x' + $request.imageSampling
            $request.imageDeltaMicrometers = if ($gaussian) { 1 } else { .25 }
            $request.zemaxSettings.ImageDelta = $request.imageDeltaMicrometers
            $prefix = if ($gaussian) { 'gaussian' } else { 'uniform' }
            Capture $request ('field' + $field + '-' + $prefix + '-' + $type.ToLowerInvariant())
        }
    }
    $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
    $request.input = Join-Path $outputRoot 'source.ZMX'
    $request.Remove('diagnosticApodizationFactor')
    $request.adapter = 'pupil'
    $request.field = $field
    $request.zemaxSettings.Field = $field
    $hy = if ($field -eq 2) { -10.6/15 } else { -1 }
    $request.rayAuditInputs = @(for ($row=0;$row -lt 32;$row++) { for ($column=0;$column -lt 32;$column++) {
        $x=($column-16)/15; $y=($row-16)/15
        if ($x*$x+$y*$y -le (1-.5/15)*(1-.5/15)) { @{hx=0;hy=$hy;px=$x;py=$y} }
    } })
    Capture $request ('field' + $field + '-pupil')
}
$files = Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
