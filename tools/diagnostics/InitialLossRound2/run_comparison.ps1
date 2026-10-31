#requires -Version 7.0
param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$ComparisonRoot)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$comparison = [IO.Path]::GetFullPath($ComparisonRoot)
if (@(Get-ChildItem -LiteralPath $comparison -Filter '*.input.json' -Recurse).Count) { throw 'Existing runs are never overwritten.' }
$protocolPath = Join-Path $comparison 'experiment-protocol.json'
$protocol = Get-Content -Raw -LiteralPath $protocolPath | ConvertFrom-Json
$protocol.execution = 'Two bounded headless workers by original case; serial seeds and arms within each case; fresh Normal-priority process and one computation thread per run; common frozen Core/runtime. No wall-time speedup claim. No GUI, DeepSeek, commit or push.'
$protocol | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $protocolPath -Encoding utf8
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $comparison 'run_comparison.ps1')
Get-FileHash -LiteralPath (Join-Path $comparison 'run_comparison.ps1') -Algorithm SHA256 | Select-Object Path,Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $comparison 'driver-sha256.json') -Encoding utf8
@('competition-8-F8-full25', 'original-half21') | ForEach-Object -Parallel {
    $caseName = $_; $root = $using:repo; $evidence = $using:comparison
    $runner = Join-Path $evidence 'runtime\OpticalSystemDesign.InitialStructure.Benchmarks.dll'
    $inputs = Join-Path $root 'validation\rechecks\2026-10-31\initial-loss-compensation\control'
    foreach ($seed in 42..46) {
        foreach ($arm in @('legacy-final', 'joint-final')) {
            $output = Join-Path $evidence "$arm\seed$seed"
            & dotnet $runner $root $inputs $output $seed $arm $caseName
            if ($LASTEXITCODE -ne 0) { throw "$caseName / $arm / $seed exited $LASTEXITCODE" }
        }
    }
} -ThrottleLimit 2
