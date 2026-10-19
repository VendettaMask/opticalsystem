param([Parameter(Mandatory)][string]$RunDirectory, [Parameter(Mandatory)][string]$HostExecutable, [int]$ChunkSize = 50)
$ErrorActionPreference = 'Stop'
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$exe = (Resolve-Path -LiteralPath $HostExecutable).Path
$jobs = @(Get-Content -LiteralPath (Join-Path $run 'native-jobs.json') -Raw | ConvertFrom-Json)
if (@($jobs | Where-Object { Test-Path -LiteralPath (Join-Path $_.output 'batch-result.json') }).Count -ne 0) { throw 'Use a fresh native run.' }
$chunkRoot = Join-Path $run 'native-chunks'
New-Item -ItemType Directory -Path $chunkRoot | Out-Null
[pscustomobject]@{ executable=$exe; sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant(); chunkSize=$ChunkSize } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $chunkRoot 'host-identity.json') -Encoding utf8
$api = (Get-Content -LiteralPath $jobs[0].request -Raw | ConvertFrom-Json).zosApiPath
$env:PATH = $api + ';' + $env:PATH
for ($offset=0; $offset -lt $jobs.Count; $offset+=$ChunkSize) {
    $end = [Math]::Min($jobs.Count-1,$offset+$ChunkSize-1)
    $chunk = @($jobs[$offset..$end])
    $name = ($offset/$ChunkSize).ToString('000')
    $path = Join-Path $chunkRoot ($name+'.json')
    ConvertTo-Json -InputObject $chunk -Depth 6 | Set-Content -LiteralPath $path -Encoding utf8
    Write-Output "CHUNK $name jobs=$($chunk.Count)"
    & $exe $api $path 2>&1 | Tee-Object -FilePath (Join-Path $chunkRoot ($name+'.log'))
    $chunkExit = $LASTEXITCODE
    [pscustomobject]@{ chunk=$name; exitCode=$chunkExit; count=$chunk.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $chunkRoot ($name+'-process.json')) -Encoding utf8
    if (@($chunk | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.output 'batch-result.json')) }).Count -ne 0) { throw 'Unfinished native chunk; preserve outputs and inspect.' }
}
Write-Output "Completed $($jobs.Count) native request attempts in fresh application chunks."
