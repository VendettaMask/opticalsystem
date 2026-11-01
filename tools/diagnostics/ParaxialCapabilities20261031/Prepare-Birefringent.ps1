param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/birefringent-native-20261031')
$ErrorActionPreference='Stop';$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null;$jobs=@()
foreach($mode in 0,1){foreach($kind in 'tilted','axial','glass'){
 $id="bire-$kind-$mode";$before=if($kind -eq 'glass'){'GLAS N-BK7'}else{''};$axis=if($kind -eq 'axial'){'0 0 1'}else{'.3 .4 .8660254037844386'};$a=$axis.Split(' ')
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
GCAT BIREFRINGENT SCHOTT
ENPD 2
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
 TYPE BIRE__IN
 CURV 0
 GLAS CALCITE
 PARM 1 $mode
 PARM 2 $($a[0])
 PARM 3 $($a[1])
 PARM 4 $($a[2])
 PARM 5 0
 PARM 6 0
 XDAT 1 0
 DISZ 5
 DIAM 20 1 0 0 1 ""
SURF 3
 TYPE BIRE_OUT
 CURV 0
 $before
 PARM 6 0
 DISZ 5
 DIAM 20 1 0 0 1 ""
SURF 4
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($wave in 1,2){foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2,3){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
  @{code=$code;int1=$surf;int2=$wave;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
 }}}}}})
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 14|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
}}
$jobs|ConvertTo-Json -Depth 10|Set-Content "$root/jobs.json" -Encoding utf8
