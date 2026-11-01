param([string]$Repository='D:/Projects/opticalsystem',
 [string]$Study='artifacts/validation/incomparable-transverse-native-controls-20261101',
 [ValidateSet('x2','x4','y2','y4','xy')][string[]]$Kinds=@('x2','x4','y2','y4','xy'))
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path;$root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh transverse control scope required'}
New-Item -ItemType Directory -Path $root|Out-Null
$culture=[Globalization.CultureInfo]::InvariantCulture;$jobs=@();$models=@()
foreach($n in 6,8){foreach($kind in $Kinds){
 $id="transverse-$kind-n$n";$grid=Join-Path $root "$id.ggd";$spacingZ=.02/($n-1)
 $lines=[Collections.Generic.List[string]]::new()
 $lines.Add("schott $n $n $n .25 .25 $($spacingZ.ToString('R',$culture))")
 for($ix=0;$ix -lt $n;$ix++){for($iy=0;$iy -lt $n;$iy++){for($iz=0;$iz -lt $n;$iz++){
  $x=.25*($ix-($n-1)/2.);$y=.25*($iy-($n-1)/2.);$z=$iz*$spacingZ
  $term=switch($kind){'x2'{.06*$x*$x};'x4'{.1*[Math]::Pow($x,4)};'y2'{.06*$y*$y};'y4'{.1*[Math]::Pow($y,4)};'xy'{.03*$x*$y}}
  $index=1.5+$term+.015*$z
  $lines.Add((@($x,$y,$z,$index,0,0,0,0,0,0,0,0,0,0)|ForEach-Object{$_.ToString('R',$culture)}) -join ' ')
 }}}
 [IO.File]::WriteAllLines($grid,$lines)
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD .4
FTYP 0 0 1 1 0 0 0
XFLN 0
YFLN 0
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ 1
SURF 1
 STOP
 TYPE GRINSU11 $($grid.Replace('\','/'))
 CURV 0
 PARM 1 .00005
 DISZ .02
 DIAM .45 1 0 0 1 ""
SURF 2
 TYPE STANDARD
 CURV 0
 DISZ .5
 DIAM .45 1 0 0 1 ""
SURF 3
 TYPE STANDARD
 CURV 0
 DISZ 0
 DIAM .45 1 0 0 1 ""
SURF 4
 TYPE STANDARD
 CURV 0
 DIAM .45 1 0 0 1 ""
"@
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source)
 $sha=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($surface in 1,2){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC','OPTH'){
  @{code=$code;int1=$surface;int2=1;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}
 }})
 $ops+=@{code='INDX';int1=1;int2=1;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}
 @{input=$path;inputSha256=$sha;zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio'
  configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls'
  captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|ConvertTo-Json -Depth 16|
  Set-Content -LiteralPath "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$sha;budgetSeconds=120;queueBudgetSeconds=10800
  frozenHost='artifacts/validation/incomparable-native-message-guard-build-20261101/ZemaxHost.exe'
  frozenHostSha256='8b323e6fa1e1e7eacc48f08d7a491cee968fcc0d10d8cf31857648bf05cc8fb4'}
 $models+=@{id=$id;input=$path;sourceSha256=$sha;gridSha256=(Get-FileHash -LiteralPath $grid).Hash.ToLowerInvariant()
  nodes=$n;kind=$kind;diagnosticOnly=$true;bodyLength=.02;maximumStep=.00005
  purpose='Independent symmetric single transverse polynomial; straight chief isolates index interpolation from bent-ray geometry. Axial .015*z retained.'}
}}
$jobs|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$root/jobs.json" -Encoding utf8
$models|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$root/models.json" -Encoding utf8
