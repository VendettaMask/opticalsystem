param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/incomparable-axial-path-native-20261101')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh diagnostic scope required'}
New-Item -ItemType Directory -Path $root|Out-Null
$culture=[Globalization.CultureInfo]::InvariantCulture
$jobs=@();$models=@()
foreach($spec in @(@{n=6;length=.2;step=.00005},@{n=6;length=.6;step=.00005},@{n=6;length=1.;step=.00005},@{n=6;length=1.;step=.0002},@{n=8;length=1.;step=.00005})){
 $n=$spec.n;$id="axial-n$n-l$($spec.length)-h$($spec.step)"
 $grid=Join-Path $root "axial-n$n.ggd"
 if(!(Test-Path -LiteralPath $grid)){
  $spacing=1./($n-1);$text=@("schott $n $n $n $($spacing.ToString('R',$culture)) $($spacing.ToString('R',$culture)) $($spacing.ToString('R',$culture))")
  for($x=0;$x -lt $n;$x++){for($y=0;$y -lt $n;$y++){for($z=0;$z -lt $n;$z++){
   $px=-.5+$x*$spacing;$py=-.5+$y*$spacing;$pz=$z*$spacing;$index=1.5+2*$pz+.2*$pz*$pz
   $text+=(@($px,$py,$pz,$index,0,0,0,0,0,0,0,0,0,0)|ForEach-Object{$_.ToString('R',$culture)}) -join ' '
  }}}
  [IO.File]::WriteAllLines($grid,$text)
 }
 $source=@"
VERS 200426 262 0
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD .2
FTYP 0 0 1 1 0 0 0
XFLN 0
YFLN 0
WAVM 1 .55 1
PWAV 1
SURF 0
 TYPE STANDARD
 CURV 0
 DISZ 2
SURF 1
 TYPE STANDARD
 CURV 0
 DISZ 1
 DIAM .5 1 0 0 1 ""
SURF 2
 STOP
 TYPE GRINSU11 $($grid.Replace('\','/'))
 CURV 0
 PARM 1 $($spec.step.ToString('R',$culture))
 DISZ $($spec.length.ToString('R',$culture))
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
 $path=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($path,$source)
 $sha=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 $ops=@(foreach($surface in 2,3){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC','OPTH'){
  @{code=$code;int1=$surface;int2=1;data1=0;data2=0;data3=0;data4=0;data5=0;data6=0}
 }})
 @{input=$path;inputSha256=$sha;zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';configuration=1;wavelength=1;zemaxVersion='2026 R1'
  adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops
 }|ConvertTo-Json -Depth 15|Set-Content -LiteralPath "$root/$id.request.json" -Encoding utf8
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$sha;budgetSeconds=120;queueBudgetSeconds=10800}
 $models+=@{id=$id;axisNodes=$n;bodyLength=$spec.length;maximumStep=$spec.step;sourceSha256=$sha;gridSha256=(Get-FileHash -LiteralPath $grid).Hash.ToLowerInvariant()
  purpose='Independent straight axial n(z)=1.5+2z+0.2z^2 nodal grid; native body OPL is OPTH(surface3)-OPTH(surface2). Not an acceptance substitution.'}
}
$jobs|ConvertTo-Json -AsArray -Depth 8|Set-Content -LiteralPath "$root/jobs.json" -Encoding utf8
$models|ConvertTo-Json -AsArray -Depth 8|Set-Content -LiteralPath "$root/models.json" -Encoding utf8
