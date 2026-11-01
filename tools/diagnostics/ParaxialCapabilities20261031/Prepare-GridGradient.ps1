param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/grid-gradient-native-20261101')
$ErrorActionPreference='Stop';$root=Join-Path $Repository $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'}
New-Item -ItemType Directory -Path $root|Out-Null;$jobs=@();$culture=[Globalization.CultureInfo]::InvariantCulture
$custom=Join-Path $root 'dispersion-grid.ggd';$lines=@('schott 6 6 6 .2 .2 .2')
for($i=0;$i -lt 6;$i++){for($j=0;$j -lt 6;$j++){for($k=0;$k -lt 6;$k++){
 $x=-.55+.2*$i;$y=-.5+.2*$j;$z=.2*$k;$c1=2.25+.5*$x+.1*$z;$c2=.03+.01*$y
 $lines+=(@($x,$y,$z,.05,$c1,$c2,0,0,0,0,0,0,0,0)|ForEach-Object {$_.ToString('R',$culture)}) -join ' '
}}}
[IO.File]::WriteAllLines($custom,$lines)
$definitions=@(foreach($step in 1,.1,.02,.005){@{id="sample-$step";file='Sample.GGD';step=$step;thickness=3.8}})
$definitions+=@(@{id='dispersion-.02';file=$custom.Replace('\','/');step=.02;thickness=1},@{id='dispersion-.005';file=$custom.Replace('\','/');step=.005;thickness=1})
foreach($d in $definitions){
 $id=$d.id;$source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD .2
FTYP 0 0 2 2 0 0 0
XFLN 0 0
YFLN 0 1
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
 DISZ 1
 DIAM .2 1 0 0 1 ""
SURF 2
 TYPE GRINSU11 $($d.file)
 CURV 0
 PARM 1 $($d.step.ToString('R',$culture))
 DISZ $($d.thickness.ToString('R',$culture))
 DIAM .5 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DISZ 1
 DIAM .5 1 0 0 1 ""
SURF 4
 TYPE STANDARD
 CURV 0
 DIAM 2 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($wave in 1,2){foreach($hy in 0,1){foreach($px in 0,.4){foreach($py in -.8,0,.8){foreach($surf in 1,2,3){foreach($code in 'OPTH','REAX','REAY','REAZ','REAA','REAB','REAC'){
  @{code=$code;int1=$surf;int2=$wave;data1=0;data2=$hy;data3=$px;data4=$py;data5=0;data6=0}
 }}}}}})
 foreach($wave in 1,2){$ops+=@{code='INDX';int1=2;int2=$wave;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}}
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 14|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120}
}
@{installedSampleSha256=(Get-FileHash -LiteralPath 'C:/Users/19851/Documents/Zemax/Glasscat/Sample.GGD').Hash.ToLowerInvariant();customGridSha256=(Get-FileHash -LiteralPath $custom).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content "$root/grid-assets.json" -Encoding utf8
$jobs|ConvertTo-Json -Depth 10|Set-Content "$root/jobs.json" -Encoding utf8
