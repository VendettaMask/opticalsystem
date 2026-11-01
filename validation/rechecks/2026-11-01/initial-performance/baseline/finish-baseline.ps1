#requires -Version 7.0
param([Parameter(Mandatory)][string]$Arm,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Arm)
foreach($file in (Get-Content -LiteralPath (Join-Path $root 'runtime-sha256.json') -Raw|ConvertFrom-Json)){
    if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash.ToLowerInvariant() -ne $file.sha256){throw 'Frozen runtime changed.'}
}
$source=[IO.Path]::GetFullPath((Join-Path $root 'seed42'))
$target=[IO.Path]::GetFullPath((Join-Path $root 'interrupted-disk-full/seed42'))
foreach($path in $source,$target){if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive path escaped study.'}}
if(Test-Path -LiteralPath $target){throw 'Never overwrite interrupted evidence.'}
$state=(Get-Content -LiteralPath (Join-Path $source 'run.preparation.json') -Raw|ConvertFrom-Json -Depth 100).checkpoint
New-Item -ItemType Directory -Path (Split-Path $target) -ErrorAction Stop|Out-Null
Move-Item -LiteralPath $source -Destination $target
Move-Item -LiteralPath (Join-Path $root 'seed42.log') -Destination (Join-Path (Split-Path $target) 'seed42.log')
[pscustomobject]@{DeclaredAt=(Get-Date -Format o);Reason='Disk full during atomic checkpoint write; source and prior complete record retained';
    SavedConsumed=$state.consumed;UnsettledReservation=$state.pending.reservation;ActualWorkAfterLastSave='Unknown';
    Replacement='Separately budgeted fresh seed42, same frozen runtime and original caps; completed seed46 retained';
    CacheCleanup='Ignored reproducible F8RealPipeline/bin,1250files checksum verified in a local temp ZIP; no source/input/sealed evidence removed'}|
    ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $root 'disk-reentry.json') -Encoding utf8
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'finish-baseline.ps1')
foreach($seed in 42,43,44,45){
    if((Get-PSDrive -Name D).Free -lt 134217728){throw 'Insufficient scratch space for another safe checkpoint; no new run started.'}
    & dotnet (Join-Path $root 'runtime/InitialPerformance20261101.dll') run ([IO.Path]::GetFullPath($OriginalInput)) (Join-Path $root "seed$seed") $seed *> (Join-Path $root "seed$seed.log")
    if($LASTEXITCODE -ne 0){throw "seed$seed failed"}
}
