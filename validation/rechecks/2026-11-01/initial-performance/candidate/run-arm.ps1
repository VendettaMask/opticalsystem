#requires -Version 7.0
param([Parameter(Mandatory)][string]$Arm,[Parameter(Mandatory)][string]$OriginalInput,[string]$Results)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Arm)
$resultsRoot=if($Results){[IO.Path]::GetFullPath($Results)}else{$root}
if($Results){
    if(Test-Path -LiteralPath $resultsRoot){throw 'Never overwrite temporary trial results.'}
    New-Item -ItemType Directory -Path $resultsRoot -ErrorAction Stop|Out-Null
    [pscustomobject]@{Results=$resultsRoot;Reason='Task scratch on C drive; final completed evidence copied back to repository';DeclaredAt=(Get-Date -Format o)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'scratch-location.json') -Encoding utf8
}
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'run-arm.ps1')
foreach($seed in 46,42,43,44,45){
    & dotnet (Join-Path $root 'runtime/InitialPerformance20261101.dll') run ([IO.Path]::GetFullPath($OriginalInput)) (Join-Path $resultsRoot "seed$seed") $seed *> (Join-Path $root "seed$seed.log")
    if($LASTEXITCODE -ne 0){throw "seed$seed failed"}
}
