param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/grid-rule-rays-native-20261101')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path;$root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh rule-ray control scope required'}
New-Item -ItemType Directory $root|Out-Null
$jobs=@();$models=@();$pupils=@(@(.25,0),@(.5,0),@(-.25,0),@(-.5,0))
foreach($n in 6,8){foreach($kind in 'x2','x4'){
 $id="$kind-n$n";$input=Join-Path $repo "artifacts/validation/incomparable-transverse-native-controls-20261101/transverse-$kind-n$n.ZMX"
 $sha=(Get-FileHash -LiteralPath $input).Hash.ToLowerInvariant()
 $ops=@(foreach($p in $pupils){foreach($surface in 1,2){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC','OPTH'){
  @{code=$code;int1=$surface;int2=1;data1=0;data2=0;data3=$p[0];data4=$p[1];data5=0;data6=0}
 }}})
 @{input=$input;inputSha256=$sha;zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|
  ConvertTo-Json -Depth 18|Set-Content -LiteralPath "$root/$id.request.json"
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$sha;budgetSeconds=120;queueBudgetSeconds=120
  frozenHost='artifacts/validation/incomparable-native-message-guard-build-20261101/ZemaxHost.exe';frozenHostSha256='8b323e6fa1e1e7eacc48f08d7a491cee968fcc0d10d8cf31857648bf05cc8fb4'}
 $models+=@{id=$id;input=$input;sourceSha256=$sha;gridSha256=(Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($input,'GGD'))).Hash.ToLowerInvariant();pupils=$pupils;nodes=$n;bodyLength=.02;native="$root/$id"}
}}
$models|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$root/models.json"
$jobs|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$root/jobs.json"
