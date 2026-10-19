param([Parameter(Mandatory)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$run = (Resolve-Path -LiteralPath $RunDirectory).Path
$prefix = $run.TrimEnd('\') + '\'
if (-not (Test-Path -LiteralPath (Join-Path $run 'workbench-results.json'))) { throw 'Wait until the original Workbench stage completes before retrying.' }
$items = @(Get-Content -LiteralPath (Join-Path $run 'items.json') -Raw | ConvertFrom-Json)
$retry = @($items | Where-Object {
    if (-not $_.workbenchJob) { return $false }
    $log = Join-Path (Split-Path -Parent $_.workbenchJob) 'process.log'
    if (-not (Test-Path -LiteralPath $log)) { return $false }
    (Get-Content -LiteralPath $log -Raw) -match '0x800705AF|Failed to create CoreCLR|Failed to load System.Private.CoreLib|System.OutOfMemoryException|页面文件太小|内存不足'
})
if ($retry.Count -eq 0) { Write-Output 'No memory-startup infrastructure failures to retry.'; exit 0 }
$attemptRoot = Join-Path $run ('attempts/workbench-memory-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ'))
New-Item -ItemType Directory -Path $attemptRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $run 'workbench-results.json') -Destination (Join-Path $attemptRoot 'original-workbench-results.json')
$records = @()
for ($index = 0; $index -lt $retry.Count; $index++) {
    $item = $retry[$index]
    $source = [IO.Path]::GetFullPath((Split-Path -Parent $item.workbenchJob))
    $target = [IO.Path]::GetFullPath((Join-Path $attemptRoot ($index.ToString('D4'))))
    if (-not $source.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Attempt paths escape the validated run.' }
    $hash = (Get-FileHash -LiteralPath $item.workbenchJob -Algorithm SHA256).Hash
    [IO.Directory]::Move($source, $target)
    New-Item -ItemType Directory -Path $source | Out-Null
    Copy-Item -LiteralPath (Join-Path $target 'request.json') -Destination $item.workbenchJob
    if ((Get-FileHash -LiteralPath $item.workbenchJob -Algorithm SHA256).Hash -ne $hash) { throw 'Retry request changed.' }
    $records += [pscustomobject]@{ id=$item.run.directory; output=$source; archived=$target; requestSha256=$hash.ToLowerInvariant() }
}
$records | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $attemptRoot 'attempts.json') -Encoding utf8
ConvertTo-Json -InputObject $retry -Depth 35 | Set-Content -LiteralPath (Join-Path $attemptRoot 'items.json') -Encoding utf8
$tool = Join-Path $repo 'tools/diagnostics/SequentialCorpus20261019/bin/Release/net10.0/SequentialCorpus20261019.dll'
& dotnet $tool workbench $repo $attemptRoot 2>&1 | Tee-Object -FilePath (Join-Path $attemptRoot 'retry.log')
if ($LASTEXITCODE -ne 0) { throw 'Workbench retry stage failed; original and retry outputs retained.' }
$final = foreach ($item in $items | Where-Object workbenchJob) {
    $result = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $item.workbenchJob) 'worker-result.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{ directory=$item.run.directory; exitCode=$result.exitCode; timedOut=$result.timedOut }
}
$final | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'workbench-results.json') -Encoding utf8
Write-Output "Retried $($retry.Count) memory-startup failures; first attempts retained in $attemptRoot."
