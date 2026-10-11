param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$template = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/gaussian/512-real/request.json'
$nativeHost = Join-Path $repository 'artifacts/validation/residuals-20261011/followup/host/NativeEnergyControl.exe'
$controls = @(
    @{name='image64';pupil=32;image=64;delta=.25;wave=1},
    @{name='delta-half';pupil=32;image=32;delta=.125;wave=1},
    @{name='pupil64';pupil=64;image=32;delta=.25;wave=1},
    @{name='wave2';pupil=32;image=32;delta=.25;wave=2}
)
foreach ($control in $controls) {
    foreach ($type in @('Real','Imaginary')) {
        $request = Get-Content -LiteralPath $template -Raw | ConvertFrom-Json -AsHashtable
        $request.pupilSampling = $control.pupil
        $request.imageSampling = $control.image
        $request.imageDeltaMicrometers = $control.delta
        $request.wavelength = $control.wave
        $request.zemaxSettings.PupilSampleSize = 'S_' + $control.pupil + 'x' + $control.pupil
        $request.zemaxSettings.ImageSampleSize = 'S_' + $control.image + 'x' + $control.image
        $request.zemaxSettings.ImageDelta = $control.delta
        $request.zemaxSettings.Wavelength = $control.wave
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
