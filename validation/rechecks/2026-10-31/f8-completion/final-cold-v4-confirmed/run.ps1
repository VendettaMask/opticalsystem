#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceRoot,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$source = [IO.Path]::GetFullPath($OriginalInput)
if (@(Get-ChildItem -LiteralPath $root -Recurse -Filter input.json).Count) { throw 'Existing runs are never overwritten.' }
$protocol = Get-Content -LiteralPath (Join-Path $root 'protocol.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run.ps1')
$protocol.seeds | ForEach-Object -Parallel {
    $seed=$_; $evidence=$using:root; $original=$using:source
    & dotnet (Join-Path $evidence 'runtime\OpticalSystemDesign.InitialStructure.Benchmarks.dll') $original (Join-Path $evidence "candidate\seed$seed") $seed candidate 4 true
    if($LASTEXITCODE -ne 0) { throw "candidate/$seed exited $LASTEXITCODE" }
} -ThrottleLimit 2
