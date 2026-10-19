param([Parameter(Mandatory)][string]$RunDirectory, [int]$ChunkSize = 50)
$ErrorActionPreference = 'Stop'
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$prefix = $run.TrimEnd('\') + '\'
$jobs = @(Get-Content -LiteralPath (Join-Path $run 'native-jobs.json') -Raw | ConvertFrom-Json)
$retry = @($jobs | Where-Object {
    $errorPath = Join-Path $_.output 'error.json'
    if (-not (Test-Path -LiteralPath $errorPath)) { return $false }
    $failure = Get-Content -LiteralPath $errorPath -Raw | ConvertFrom-Json
    $failure.type -like 'System.Runtime.Remoting.*'
})
if ($retry.Count -eq 0) { Write-Output 'No disconnected-host requests to retry.'; exit 0 }
$attemptRoot = Join-Path $run ('attempts/native-disconnected-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
New-Item -ItemType Directory -Path $attemptRoot | Out-Null
$records = @()
for ($index = 0; $index -lt $retry.Count; $index++) {
    $job = $retry[$index]
    $source = [IO.Path]::GetFullPath($job.output)
    $target = [IO.Path]::GetFullPath((Join-Path $attemptRoot ($index.ToString('D4'))))
    if (-not $source.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Attempt archive paths escape the validated run directory.' }
    if (-not (Test-Path -LiteralPath (Join-Path $source 'batch-result.json'))) { throw 'Only completed failed native requests may be archived.' }
    $hash = (Get-FileHash -LiteralPath $job.request -Algorithm SHA256).Hash
    [IO.Directory]::Move($source, $target)
    New-Item -ItemType Directory -Path $source | Out-Null
    Copy-Item -LiteralPath (Join-Path $target 'request.json') -Destination $job.request
    if ((Get-FileHash -LiteralPath $job.request -Algorithm SHA256).Hash -ne $hash) { throw 'Restored request differs from the archived first attempt.' }
    $records += [pscustomobject]@{ id=$job.id; output=$source; archived=$target; requestSha256=$hash.ToLowerInvariant() }
}
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $attemptRoot 'attempts.json') -Encoding utf8
$api = (Get-Content -LiteralPath $retry[0].request -Raw | ConvertFrom-Json).zosApiPath
$env:PATH = $api + ';' + $env:PATH
$exe = Join-Path $run 'host/NativeBatchHost.exe'
$chunkRoot = Join-Path $attemptRoot 'retry-chunks'
New-Item -ItemType Directory -Path $chunkRoot | Out-Null
for ($offset = 0; $offset -lt $retry.Count; $offset += $ChunkSize) {
    $end = [Math]::Min($retry.Count - 1, $offset + $ChunkSize - 1)
    $chunk = @($retry[$offset..$end])
    $name = ($offset / $ChunkSize).ToString('000')
    $jobPath = Join-Path $chunkRoot ($name + '.json')
    $chunk | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jobPath -Encoding utf8
    Write-Output "CHUNK $name jobs=$($chunk.Count) first=$($chunk[0].id)"
    & $exe $api $jobPath 2>&1 | Tee-Object -FilePath (Join-Path $chunkRoot ($name + '.log'))
    $chunkExit = $LASTEXITCODE
    [pscustomobject]@{ chunk=$name; exitCode=$chunkExit; count=$chunk.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $chunkRoot ($name + '-process.json')) -Encoding utf8
    $missing = @($chunk | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.output 'batch-result.json')) })
    if ($missing.Count -ne 0) { throw "Native chunk has $($missing.Count) unfinished requests; preserve outputs and investigate." }
}
Write-Output "Retried $($retry.Count) disconnected-host requests in fresh native applications; first attempts retained in $attemptRoot."
