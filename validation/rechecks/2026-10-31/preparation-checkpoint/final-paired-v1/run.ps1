#requires -Version 7.0
param([Parameter(Mandatory)][string]$Study,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Study)
$inputPath=[IO.Path]::GetFullPath($OriginalInput)
$protocol=Get-Content -LiteralPath (Join-Path $root 'protocol.json') -Raw | ConvertFrom-Json
$jobs=foreach($seed in 46,42,43,44,45) {
    [pscustomobject]@{Seed=$seed;Arm='continuous';Stop='continuous'}
    [pscustomobject]@{Seed=$seed;Arm='paused';Stop=$protocol.stopPoints."$seed"}
}
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run.ps1')
$jobs | ForEach-Object -Parallel {
    $item=$_; $evidence=$using:root; $original=$using:inputPath
    $runner=Join-Path $evidence 'runtime/PreparationResume20261031.dll'
    $destination=Join-Path $evidence "$($item.Arm)/seed$($item.Seed)"
    & dotnet $runner run $original $destination $item.Seed $item.Stop
    if($LASTEXITCODE -ne 0){throw "$($item.Arm)/$($item.Seed) failed."}
    if($item.Arm -eq 'paused') {
        & dotnet $runner resume (Join-Path $destination 'run.preparation.json') (Join-Path $destination 'resumed-segment.json')
        if($LASTEXITCODE -ne 0){throw "resume/$($item.Seed) failed."}
    }
} -ThrottleLimit 2
foreach($seed in $protocol.seeds) {
    & dotnet (Join-Path $root 'runtime/PreparationResume20261031.dll') compare (Join-Path $root "continuous/seed$seed/run.preparation.json") (Join-Path $root "paused/seed$seed/run.preparation.json") (Join-Path $root "compare$seed.json")
    if($LASTEXITCODE -ne 0){throw "compare/$seed failed."}
}
