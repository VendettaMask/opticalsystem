param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$NativeHostDirectory,
    [string]$ZosApiPath,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) { throw 'A fresh output directory is required.' }
$diagnosticDll = Join-Path $PSScriptRoot 'bin\Release\net10.0\Residuals20261011.dll'
if (!(Test-Path -LiteralPath $diagnosticDll)) { throw 'Build the Release diagnostic first.' }
if (!$PrepareOnly) {
    if (!$NativeHostDirectory) { throw 'Supply the existing licensed-machine host directory.' }
    $hostRoot = (Resolve-Path -LiteralPath $NativeHostDirectory).Path
    foreach ($name in @('ZemaxHost.exe', 'NativeEnergyControl.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $hostRoot $name))) { throw ('Missing native host: ' + $name) }
    }
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$source = Join-Path $repository 'validation\zemax\2026-r1\ms-l7-analysis-expansion-2026-09-06\source.ZMX'
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
if ($sourceHash -cne '8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8') { throw 'Frozen source identity differs.' }
$sourceCopy = Join-Path $outputRoot 'source.ZMX'
Copy-Item -LiteralPath $source -Destination $sourceCopy
& dotnet $diagnosticDll $repository (Join-Path $outputRoot 'inputs') inputs
if ($LASTEXITCODE -ne 0) { throw 'Pupil input generation failed.' }
$jobs = [Collections.Generic.List[object]]::new()
function Write-Request([hashtable]$request, [string]$name, [string]$hostName) {
    $directory = Join-Path $outputRoot $name
    New-Item -ItemType Directory -Path $directory | Out-Null
    $request.input = $sourceCopy
    if ($ZosApiPath) { $request.zosApiPath = $ZosApiPath }
    $requestFile = Join-Path $directory 'request.json'
    $request | ConvertTo-Json -Depth 60 | Set-Content -LiteralPath $requestFile -Encoding utf8
    $jobs.Add([pscustomobject]@{name=$name;host=$hostName;request=$requestFile;output=$directory})
}
foreach ($nodes in @('endpoints', 'zemax')) {
    $request = Get-Content -LiteralPath (Join-Path $repository 'validation\zemax\2026-r1\huygens-method-controls-2026-10-09\grid\request.json') -Raw | ConvertFrom-Json -AsHashtable
    $request.adapter = 'pupil'
    $request.rayAuditInputs = @(Get-Content -LiteralPath (Join-Path $outputRoot ('inputs\' + $nodes + '.json')) -Raw | ConvertFrom-Json -AsHashtable)
    # Retain all original nodes, then add an explicit chief for a zero-OPD check.
    if (!@($request.rayAuditInputs | Where-Object { $_.px -eq 0 -and $_.py -eq 0 }).Count) {
        $request.rayAuditInputs += @{hx=0;hy=0;px=0;py=0;fieldIndex=0;pupilIndex=-1}
    }
    Write-Request $request ('pupil-' + $nodes) 'NativeEnergyControl.exe'
}
foreach ($name in @('dee-range-2p5', 'dee-32', 'dee-range-10', 'dee-x-32')) {
    $request = Get-Content -LiteralPath (Join-Path $repository ('validation\zemax\2026-r1\n02-energy-layer-controls-2026-10-09\' + $name + '\request.json')) -Raw | ConvertFrom-Json -AsHashtable
    Write-Request $request $name 'ZemaxHost.exe'
}
foreach ($type in @('Linear', 'Real', 'Imaginary')) {
    $request = Get-Content -LiteralPath (Join-Path $repository 'validation\zemax\2026-r1\huygens-method-controls-2026-10-09\grid\request.json') -Raw | ConvertFrom-Json -AsHashtable
    $request.zemaxSettings.Type = $type
    $request.workbenchSettings = @{}
    # Keep the established scalar jobs ahead of the complex-type support check.
    # The helper captures Auto, Planar and Spherical and restores the setting.
    Write-Request $request ('huygens-' + $type.ToLowerInvariant()) 'NativeEnergyControl.exe'
}
$identity = [ordered]@{
    sourceSha256=$sourceHash
    repositoryCommit=(& git -C $repository rev-parse HEAD).Trim()
    status=if ($PrepareOnly) { 'PreparedOnly; no Zemax process started' } else { 'PendingNativeExecution' }
    jobs=$jobs.ToArray()
    limitations='DEE ideal 2D PSF/internal energy grid/denominator remain unavailable in the audited public API; this script does not fabricate them or modify the lens. Real/Imaginary enum support must be confirmed by native capture, not inferred from preparation.'
}
$identity | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-plan.json') -Encoding utf8
if ($PrepareOnly) {
    Write-Output ('Prepared ' + $jobs.Count + ' native jobs without launching Zemax: ' + $outputRoot)
    return
}
foreach ($job in $jobs) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $hostRoot $job.host
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add($job.request)
    $start.ArgumentList.Add($job.output)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(120000)) { $process.Kill(); throw ('Native host timeout: ' + $job.name) }
        if ($process.ExitCode -ne 0) { throw ('Native host failed: ' + $job.name + '; inspect error.json before continuing.') }
    } finally { $process.Dispose() }
    if ((Get-FileHash -LiteralPath $sourceCopy -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sourceHash) { throw 'Capture copy was modified.' }
}
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sourceHash) { throw 'Frozen source was modified.' }
$identity.status = 'NativeJobsCompleted; results require comparison before certification'
$identity | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-plan.json') -Encoding utf8
$files = Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath($outputRoot,$_.FullName).Replace('\','/');length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$files | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $outputRoot 'capture-file-hashes.json') -Encoding utf8
