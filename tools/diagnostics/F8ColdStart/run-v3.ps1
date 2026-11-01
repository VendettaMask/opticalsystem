#requires -Version 7.0
param([Parameter(Mandatory)][string]$EvidenceRoot,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot); $source = [IO.Path]::GetFullPath($OriginalInput)
if (@(Get-ChildItem -LiteralPath $root -Recurse -Filter input.json).Count) { throw 'Existing runs are never overwritten.' }
$protocol = Get-Content -LiteralPath (Join-Path $root 'protocol.json') -Raw | ConvertFrom-Json
$items = foreach($arm in 'candidate','control') {
    foreach($seed in $protocol.seeds) {
        [pscustomobject]@{ Arm=$arm; Seed=$seed; Version=$protocol.armPolicies.$arm.version; Explore=$protocol.armPolicies.$arm.exploreBackFocus }
    }
}
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run.ps1')
$items | ForEach-Object -Parallel {
    $item=$_; $evidence=$using:root; $original=$using:source
    & dotnet (Join-Path $evidence 'runtime\OpticalSystemDesign.InitialStructure.Benchmarks.dll') $original (Join-Path $evidence "$($item.Arm)\seed$($item.Seed)") $item.Seed $item.Arm $item.Version $item.Explore.ToString()
    if($LASTEXITCODE -ne 0) { throw "$($item.Arm)/$($item.Seed) exited $LASTEXITCODE" }
} -ThrottleLimit 2
