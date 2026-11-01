param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/paraxial-xy-native-20261031')
$ErrorActionPreference='Stop';$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null;$jobs=@()
foreach($kind in 'infinite','finite','strong'){foreach($medium in 'air','glass'){
 $id="xy-$kind-$medium";$template=if($kind -eq 'infinite'){'infinite-mode1'}else{'finite-mode1'}
 $source=[IO.File]::ReadAllText((Join-Path $Repository "artifacts/validation/paraxial-phase-native-20261031-02/$template.ZMX"))
 $source=$source.Replace('TYPE PARAXIAL','TYPE PARAX_XY').Replace('PARM 1 100','PARM 1 .015').Replace('PARM 2 1','PARM 2 .03')
 if($medium -eq 'glass'){$source=$source.Replace(' DISZ 5'," GLAS N-BK7`n DISZ 5").Replace(' PARM 1'," GLAS N-BK7`n PARM 1")}
 if($kind -eq 'strong'){$source=$source.Replace('DISZ 200','DISZ .5362').Replace('DISZ 5','DISZ .03').Replace('ENPD 10','ENPD .2').Replace('YFLN 0 5','YFLN 0 .02').Replace('PARM 1 .015','PARM 1 0').Replace('PARM 2 .03','PARM 2 7.971')}
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
  @{code=$code;int1=$surf;int2=1;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
 }}}}})
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 14|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
}}
$jobs|ConvertTo-Json -Depth 10|Set-Content "$root/jobs.json" -Encoding utf8
