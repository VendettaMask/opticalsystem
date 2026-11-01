#requires -Version 7.0
param([Parameter(Mandatory)][string]$Arm,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Arm)
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run-arm.ps1')
foreach($seed in 46,42,43,44,45){
    & dotnet (Join-Path $root 'runtime/InitialPerformance20261101.dll') run ([IO.Path]::GetFullPath($OriginalInput)) (Join-Path $root "seed$seed") $seed *> (Join-Path $root "seed$seed.log")
    if($LASTEXITCODE -ne 0){throw "seed$seed failed"}
}
