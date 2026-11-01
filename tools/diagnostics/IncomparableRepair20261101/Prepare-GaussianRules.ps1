param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/incomparable-gaussian-rules-20261101')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path;$root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh study required'};New-Item -ItemType Directory -Path $root|Out-Null
$culture=[Globalization.CultureInfo]::InvariantCulture;$jobs=@();$models=@()
foreach($d in @(@{id='n12-g02';n=1.2;g=.2},@{id='n15-g02';n=1.5;g=.2},@{id='n20-g02';n=2.;g=.2},@{id='n15-g01';n=1.5;g=.1},@{id='n15-g0';n=1.5;g=0},@{id='axial';n=1.5;g=0})){
 $id=$d.id;$grid=Join-Path $root "$id.ggd";$lines=@('schott 6 6 6 .2 .2 .2')
 for($i=0;$i -lt 6;$i++){for($j=0;$j -lt 6;$j++){for($k=0;$k -lt 6;$k++){
  $x=-.5+.2*$i;$y=-.5+.2*$j;$z=.2*$k;$n=$d.n+$d.g*$y;if($id -eq 'axial'){$n+=2*$z+.2*$z*$z}
  $lines+=(@($x,$y,$z,$n,0,0,0,0,0,0,0,0,0,0)|ForEach-Object {$_.ToString('R',$culture)}) -join ' '
 }}};[IO.File]::WriteAllLines($grid,$lines)
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD .2
FTYP 0 0 2 2 0 0 0
XFLN 0 0
YFLN 0 1
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ INFINITY
SURF 1
 TYPE STANDARD
 CURV 0
 DISZ 1
 DIAM .5 1 0 0 1 ""
SURF 2
 STOP
 TYPE GRINSU11 $($grid.Replace('\','/'))
 CURV 0
 PARM 1 .001
 DISZ 1
 DIAM .5 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DISZ 0
 DIAM .5 1 0 0 1 ""
SURF 4
 TYPE STANDARD
 CURV 0
 DIAM .5 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source);$hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($hy in 0,1){foreach($py in -1,0,1){foreach($surf in 2,3){foreach($code in 'PARY','PATY'){@{code=$code;int1=$surf;int2=1;data1=0;data2=$hy;data3=0;data4=$py;data5=0;data6=0}}}}})
 foreach($code in 'EFFL','EXPP','EXPD','EPDI'){$ops+=@{code=$code;int1=0;int2=0;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}}
 @{zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';input=$path;inputSha256=$hash;configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 15|Set-Content "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$hash;budgetSeconds=120};$models+=@{id=$id;n=$d.n;g=$d.g;axial=$id -eq 'axial';sourceSha256=$hash;gridSha256=(Get-FileHash -LiteralPath $grid).Hash.ToLowerInvariant()}
}
$jobs|ConvertTo-Json -Depth 8|Set-Content "$root/jobs.json" -Encoding utf8;$models|ConvertTo-Json -Depth 8|Set-Content "$root/models.json" -Encoding utf8
