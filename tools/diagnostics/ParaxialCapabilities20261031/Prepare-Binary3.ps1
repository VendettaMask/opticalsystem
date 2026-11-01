param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/binary3-native-20261031')
$ErrorActionPreference='Stop';$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null;$jobs=@()
foreach($kind in 'air','glass','mirror'){foreach($breakSag in 0,1){
 $id="dual-$kind-$breakSag";$before=if($kind -eq 'glass'){'GLAS N-BK7'}else{''};$after=if($kind -eq 'mirror'){'GLAS MIRROR'}else{''}
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
GCAT SCHOTT
ENPD 4
FTYP 0 0 2 2 0 0 0
XFLN 0 0
YFLN 0 5
WAVM 1 .55 1
WAVM 2 .65 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ INFINITY
SURF 1
 STOP
 TYPE STANDARD
 CURV 0
 $before
 DISZ 5
 DIAM 20 1 0 0 1 ""
SURF 2
 TYPE BINARY_3
 CURV .025
 CONI -.4
 $after
 PARM 1 50
 PARM 2 -.7
 PARM 3 1.2
 PARM 4 3
 PARM 5 -1
 PARM 6 2
 PARM 8 $breakSag
 XDAT 1 3
 XDAT 2 .0001
 XDAT 3 -220
 XDAT 4 -.0002
 XDAT 5 80
 XDAT 6 -1e-7
 XDAT 7 15
 XDAT 8 2e-7
 XDAT 9 -8
 XDAT 10 1e-10
 XDAT 11 -.2
 XDAT 12 -1e-10
 XDAT 13 .4
 DISZ 0
 DIAM 20 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($wave in 1,2){foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
  @{code=$code;int1=$surf;int2=$wave;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
 }}}}}})
 foreach($x in 0,.5,1.19999999,1.2,1.20000001,2,3.1){$ops+=@{code='SSAG';int1=2;int2=0;data1=$x;data2=0;data3=0;data4=0;data5=0;data6=0}}
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 14|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
}}
$jobs|ConvertTo-Json -Depth 10|Set-Content "$root/jobs.json" -Encoding utf8
