param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/grid-sag-native-20261031')
$ErrorActionPreference='Stop';$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null;$jobs=@()
$example=Get-Content (Join-Path $Repository 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Grid Sag Example.zmx')
$gridStart=($example|Select-String '^  GDAT 5 5 ').LineNumber-1
$random=$example[($gridStart+1)..($gridStart+25)] -join "`n"
foreach($kind in 'random','smooth','linear','estimated'){
 $id="grid-$kind";$mode=if($kind -eq 'linear'){1}else{0}
 $records=if($kind -in 'random','linear'){$random}else{
   $lines=for($row=0;$row -lt 5;$row++){for($col=0;$col -lt 5;$col++){
    $x=$col-2;$y=2-$row;$sag=.005*$x*$x+.003*$y*$y+.001*$x*$x*$y+.0002*$x*$y*$y
    $dx=.01*$x+.002*$x*$y+.0002*$y*$y;$dy=.006*$y+.001*$x*$x+.0004*$x*$y;$cross=.002*$x+.0004*$y
    if($kind -eq 'estimated'){$dx=0;$dy=0;$cross=0}
    " GARR $($row*5+$col) $sag $dx $dy $cross 0"
   }};$lines -join "`n"
 }
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
GCAT SCHOTT
ENPD 2
FTYP 0 0 2 1 0 0 0
XFLN 0 0
YFLN 0 2
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ INFINITY
SURF 1
 STOP
 TYPE STANDARD
 CURV 0
 DISZ 5
 DIAM 20 1 0 0 1 ""
SURF 2
 TYPE GRID_SAG
 CURV .02
 CONI -.4
 GLAS N-BK7
 PARM 0 $mode
 PARM 1 .001
 XDAT 1 0
 XDAT 2 100
 GDAT 5 5 1 1 0 0 $mode 0 0 1
 $records
 DISZ 10
 DIAM 20 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DIAM 50 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash $path).Hash.ToLowerInvariant()
 $ops=@(foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
  @{code=$code;int1=$surf;int2=1;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
 }}}}})
 foreach($x in -1.8,-1.2,0,.3,1.75){foreach($y in -1.8,-.6,0,.8,1.75){$ops+=@{code='SSAG';int1=2;int2=0;data1=$x;data2=$y;data3=0;data4=0;data5=0;data6=0}}}
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 12|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
}
$jobs|ConvertTo-Json -Depth 12|Set-Content "$root/jobs.json" -Encoding utf8
