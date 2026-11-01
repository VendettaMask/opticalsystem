#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceRoot,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot); $source = [IO.Path]::GetFullPath($OriginalInput)
if (@(Get-ChildItem -LiteralPath $root -Recurse -Filter input.json).Count) { throw 'Existing runs are never overwritten.' }
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run.ps1')
@('control','candidate') | ForEach-Object -Parallel {
    $arm = $_; $evidence = $using:root; $original = $using:source
    $version = if($arm -eq 'control') { 1 } else { 2 }
    foreach($seed in 42..46) {
        & dotnet (Join-Path $evidence 'runtime\OpticalSystemDesign.InitialStructure.Benchmarks.dll') $original (Join-Path $evidence "$arm\seed$seed") $seed $arm $version true
        if($LASTEXITCODE -ne 0) { throw "$arm/$seed exited $LASTEXITCODE" }
    }
} -ThrottleLimit 2
