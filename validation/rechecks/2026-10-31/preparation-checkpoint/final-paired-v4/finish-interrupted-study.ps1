#requires -Version 7.0
param([Parameter(Mandatory)][string]$Study,[Parameter(Mandatory)][string]$OriginalInput)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Study)
$inputPath=[IO.Path]::GetFullPath($OriginalInput)
$protocol=Get-Content -LiteralPath (Join-Path $root 'protocol.json') -Raw|ConvertFrom-Json
foreach($file in (Get-Content -LiteralPath (Join-Path $root 'runtime-sha256.json') -Raw|ConvertFrom-Json)) {
    if((Get-FileHash -LiteralPath (Join-Path $root $file.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256){throw "Frozen runtime changed: $($file.path)"}
}
$runner=Join-Path $root 'runtime/PreparationResume20261031.dll'
$interrupted=Join-Path $root 'interrupted-on-chat-stop'
$reentry=Join-Path $root 'reentry-2026-11-01.json'
if(Test-Path -LiteralPath $reentry){throw 'Reentry already declared; never overwrite it.'}
$rows=[Collections.Generic.List[object]]::new()
foreach($seed in 46,42,43,44,45){foreach($arm in 'continuous','paused'){
    $folder=Join-Path $root "$arm/seed$seed"
    $segment=Join-Path $folder $(if($arm -eq 'paused'){'resumed-segment.json'}else{'segment.json'})
    if(Test-Path -LiteralPath $segment){
        $result=Get-Content -LiteralPath $segment -Raw|ConvertFrom-Json
        if($result.status -ne 'completed' -or !$result.qualified){throw "Existing completed segment is not qualified: $folder"}
        $rows.Add([pscustomobject]@{Seed=$seed;Arm=$arm;Action='Retain completed original result'});continue
    }
    if(Test-Path -LiteralPath $folder){
        $snapshot=(Get-Content -LiteralPath (Join-Path $folder 'run.preparation.json') -Raw|ConvertFrom-Json -Depth 100).checkpoint
        New-Item -ItemType Directory -Path $interrupted -Force|Out-Null
        $destination=[IO.Path]::GetFullPath((Join-Path $interrupted "$arm-seed$seed"))
        $source=[IO.Path]::GetFullPath($folder)
        foreach($path in $source,$destination){if(!$path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive path escaped study.'}}
        if(Test-Path -LiteralPath $destination){throw 'Interrupted evidence already exists.'}
        Move-Item -LiteralPath $source -Destination $destination
        $log=Join-Path $root "$arm-seed$seed.log"
        if(Test-Path -LiteralPath $log){Move-Item -LiteralPath $log -Destination (Join-Path $interrupted "$arm-seed$seed.log")}
        $rows.Add([pscustomobject]@{Seed=$seed;Arm=$arm;Action='Archive externally interrupted attempt; replace with a fresh separately budgeted attempt';
            SavedConsumed=$snapshot.consumed;UnsettledReservation=$snapshot.pending.reservation;ActualAfterLastSave='Unknown';Archive=$destination})
    } else {$rows.Add([pscustomobject]@{Seed=$seed;Arm=$arm;Action='Not previously started'})}
}}
[pscustomobject]@{Reason='Previous chat interruption terminated the driver; completed cases retained, incomplete attempt archived without recycling its budget';
    DeclaredAt=(Get-Date -Format o);SameFrozenRuntime=$true;SameOriginalCapsAndGates=$true;Workers=1;Cases=$rows}|
    ConvertTo-Json -Depth 12|Set-Content -LiteralPath $reentry -Encoding utf8
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $root 'finish-interrupted-study.ps1')
foreach($seed in 46,42,43,44,45){foreach($arm in 'continuous','paused'){
    $folder=Join-Path $root "$arm/seed$seed"
    if(Test-Path -LiteralPath $folder){continue}
    $stop=if($arm -eq 'continuous'){'continuous'}else{$protocol.stopPoints."$seed"}
    $log=Join-Path $root "$arm-seed$seed.log"
    & dotnet $runner run $inputPath $folder $seed $stop *> $log
    if($LASTEXITCODE -ne 0){throw "$arm/$seed failed."}
    if($arm -eq 'paused'){
        & dotnet $runner resume (Join-Path $folder 'run.preparation.json') (Join-Path $folder 'resumed-segment.json') *>> $log
        if($LASTEXITCODE -ne 0){throw "resume/$seed failed."}
    }
}}
foreach($seed in $protocol.seeds){
    & dotnet $runner compare (Join-Path $root "continuous/seed$seed/run.preparation.json") (Join-Path $root "paused/seed$seed/run.preparation.json") (Join-Path $root "compare$seed.json")
    if($LASTEXITCODE -ne 0){throw "compare/$seed failed."}
}
