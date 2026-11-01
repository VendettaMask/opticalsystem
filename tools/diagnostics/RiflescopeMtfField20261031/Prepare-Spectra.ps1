$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../..").Path
$root=Join-Path $repo 'artifacts/validation/riflescope-mtf-field-20261031'
$out=Join-Path $root 'native-spectra'
if(Test-Path $out){throw 'Fresh spectra cohort required'}
New-Item -ItemType Directory $out | Out-Null
$base=Get-Content "$root/native-high/huygens-n128-single3p2.request.json" -Raw | ConvertFrom-Json -AsHashtable
$jobs=@()
foreach($spec in @(@('bootstrap64',64,64,0),@('p128-i64-d04',128,64,.04),@('p256-i64-d04',256,64,.04),@('p512-i64-d04',512,64,.04),@('p512-i128-d04',512,128,.04))){
 $r=($base | ConvertTo-Json -Depth 100) | ConvertFrom-Json -AsHashtable
 $id=$spec[0];$r.adapter='huygens-field-spectra';$r.analysisType='HuygensMtf';$r.field=2
 $r.pupilSampling=$spec[1];$r.imageSampling=$spec[2]
 $r.imageDeltaMicrometers=$spec[3]/([Math]::PI*1000/10800)
 $r.maximumFrequency=3.2*299/256*([Math]::PI*1000/10800)
 $r.diagnosticHuygensFields=@(0..9 | ForEach-Object { @{x=0;y=2.5*$_/9} })
 $r | ConvertTo-Json -Depth 100 | Set-Content "$out/$id.request.json" -Encoding utf8NoBOM
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$r.inputSha256;budgetSeconds=600}
}
$jobs | ConvertTo-Json -Depth 10 | Set-Content "$out/jobs.json" -Encoding utf8NoBOM
