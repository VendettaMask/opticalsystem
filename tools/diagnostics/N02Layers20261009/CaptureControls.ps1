param(
    [Parameter(Mandatory)][string]$NativeRun,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$nativeRoot = (Resolve-Path -LiteralPath $NativeRun).Path
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'Fresh output directory required.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$diagnosticDll = Join-Path $PSScriptRoot 'bin\Release\net10.0\N02Layers20261009.dll'
$hostExe = Join-Path $nativeRoot 'host\ZemaxHost.exe'
$pupilExe = Join-Path $nativeRoot 'host\NativeEnergyControl.exe'
$baseRequest = Get-Content -LiteralPath (Join-Path $nativeRoot 'psf-32\request.json') -Raw | ConvertFrom-Json -AsHashtable
function Invoke-Native([string]$executable, [string]$requestFile, [string]$directory) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $executable; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.ArgumentList.Add($requestFile); $start.ArgumentList.Add($directory)
    # Wait on the host process, not a redirected-output pipeline whose child
    # license helpers can retain a pipe handle after the host exits.
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Native host timeout.' }
        if ($process.ExitCode -ne 0) { throw ('Native host failed: ' + $directory) }
    } finally { $process.Dispose() }
}
$cases = @(
    @{ Name='ms-l7-64'; Source=(Join-Path $nativeRoot 'input\source.ZMX'); Sampling=64 },
    @{ Name='cooke-32'; Source='C:\Users\19851\Documents\Zemax\Samples\Sequential\Objectives\Cooke 40 degree field.zmx'; Sampling=32 },
    @{ Name='tessar-32'; Source='C:\Users\19851\Documents\Zemax\Samples\Sequential\Objectives\Tessar lens using vignetting factors.zmx'; Sampling=32 }
)
foreach ($case in $cases) {
    $directory = Join-Path $outputRoot $case.Name
    New-Item -ItemType Directory -Path (Join-Path $directory 'input'),(Join-Path $directory 'probe') | Out-Null
    $sourceCopy = Join-Path $directory 'input\source.ZMX'
    Copy-Item -LiteralPath $case.Source -Destination $sourceCopy
    $sourceHash = (Get-FileHash -LiteralPath $case.Source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne (Get-FileHash -LiteralPath $sourceCopy -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Input copy hash differs.' }
    $probe = @{ zosApiPath=$baseRequest.zosApiPath; input=$sourceCopy; zemaxVersion='2026 R1'; adapter='probe'; configuration=1; captureScreenshots=$false }
    $probeFile = Join-Path $directory 'probe\request.json'
    $probe | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $probeFile -Encoding utf8
    Invoke-Native $hostExe $probeFile (Join-Path $directory 'probe')
    $model = Get-Content -LiteralPath (Join-Path $directory 'probe\model.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($model.fields[0].data.X -ne 0 -or $model.fields[0].data.Y -ne 0) { throw 'Expected an on-axis first field.' }
    $n = $case.Sampling
    & dotnet $diagnosticDll --inputs $n (Join-Path $directory 'inputs.json')
    if ($LASTEXITCODE -ne 0) { throw 'Formal pupil input generation failed.' }
    foreach ($type in @('Linear','Real','Imaginary')) {
        $request = Get-Content -LiteralPath (Join-Path $nativeRoot 'psf-32\request.json') -Raw | ConvertFrom-Json -AsHashtable
        $request.input = $sourceCopy
        $request.pupilSampling = $n; $request.imageSampling = 2*$n; $request.gridSize = 2*$n
        $request.surfaceCount = $model.surfaceCount
        $request.fieldCount = $model.fields.Count; $request.wavelengthCount = $model.wavelengths.Count
        $request.definedFields = @($model.fields | ForEach-Object { ,@($_.data.X,$_.data.Y) })
        $request.primaryWavelengthMicrometers = @($model.wavelengths | Where-Object { $_.data.IsPrimary })[0].data.Wavelength
        $request.reference = 'ChiefRay'
        $request.zemaxSettings.SampleSize = 'PsfS_' + $n + 'x' + $n
        $request.zemaxSettings.OutputSize = 'PsfS_' + (2*$n) + 'x' + (2*$n)
        $request.zemaxSettings.Type = $type
        $request.workbenchSettings = @{}
        $suffix = if ($type -eq 'Linear') { 'psf-' + $n } else { 'psf-' + $type.ToLowerInvariant() + '-' + $n }
        $capture = Join-Path $directory $suffix
        New-Item -ItemType Directory -Path $capture | Out-Null
        $requestFile = Join-Path $capture 'request.json'
        $request | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $requestFile -Encoding utf8
        Invoke-Native $hostExe $requestFile $capture
    }
    $pupil = Join-Path $directory ('pupil-' + $n)
    New-Item -ItemType Directory -Path $pupil | Out-Null
    $probe.adapter = 'pupil'
    $probe.rayAuditInputs = @(Get-Content -LiteralPath (Join-Path $directory 'inputs.json') -Raw | ConvertFrom-Json -AsHashtable)
    $pupilRequest = Join-Path $pupil 'request.json'
    $probe | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $pupilRequest -Encoding utf8
    Invoke-Native $pupilExe $pupilRequest $pupil
    & dotnet $diagnosticDll --complex-control $directory (Join-Path $directory 'diagnosis') $n
    if ($LASTEXITCODE -ne 0) { throw ('Complex-field diagnostic failed: ' + $case.Name) }
    if ($sourceHash -ne (Get-FileHash -LiteralPath $case.Source -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Source changed.' }
    @{ name=$case.Name; source=$case.Source; sourceSha256=$sourceHash; pupilSampling=$n; imageDelta='automatic'; wavelength=1; field=1; configuration=1 } |
        ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $directory 'identity.json') -Encoding utf8
}
