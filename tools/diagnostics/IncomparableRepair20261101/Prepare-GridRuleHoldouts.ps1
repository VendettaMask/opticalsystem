param([string]$Repository='D:/Projects/opticalsystem',[string]$Study='artifacts/validation/grid-rule-holdout-native-20261101')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path;$root=Join-Path $repo $Study
if(Test-Path -LiteralPath $root){throw 'Fresh holdout scope required'};New-Item -ItemType Directory $root|Out-Null
$ci=[Globalization.CultureInfo]::InvariantCulture;$jobs=@();$models=@();$pupils=@(@(.25,0),@(-.25,0),@(.25,.2),@(-.25,-.2))
foreach($spec in @(@{id='long-x2';nx=6;ny=6;nz=6;dx=.25;dy=.25;dz=.12;ox=-.625;oy=-.625;length=.6},
 @{id='shifted-rectangular';nx=7;ny=8;nz=9;dx=.2;dy=.17;dz=.04;ox=-.5;oy=-.65;length=.2})){
 $id=$spec.id;$grid=Join-Path $root "$id.GGD";$lines=[Collections.Generic.List[string]]::new()
 $lines.Add("schott $($spec.nx) $($spec.ny) $($spec.nz) $($spec.dx.ToString('R',$ci)) $($spec.dy.ToString('R',$ci)) $($spec.dz.ToString('R',$ci))")
 for($ix=0;$ix -lt $spec.nx;$ix++){for($iy=0;$iy -lt $spec.ny;$iy++){for($iz=0;$iz -lt $spec.nz;$iz++){
  $x=$spec.ox+$ix*$spec.dx;$y=$spec.oy+$iy*$spec.dy;$z=$iz*$spec.dz
  $n=if($id -eq 'long-x2'){1.5+.06*$x*$x+.015*$z}else{1.5+.03*$x*$x+.02*[Math]::Pow($x,4)+.015*$y*$y+.01*$x*$y+.2*$z+.1*$z*$z}
  $lines.Add((@($x,$y,$z,$n,0,0,0,0,0,0,0,0,0,0)|ForEach-Object{$_.ToString('R',$ci)}) -join ' ')
 }}}
 [IO.File]::WriteAllLines($grid,$lines)
 $template=[IO.File]::ReadAllText((Join-Path $repo 'artifacts/validation/incomparable-transverse-native-controls-20261101/transverse-x2-n6.ZMX'))
 $template=[regex]::Replace($template,'TYPE GRINSU11 [^\r\n]+','TYPE GRINSU11 '+$grid.Replace('\','/'))
 $template=$template.Replace(' DISZ .02',' DISZ '+$spec.length.ToString('R',$ci)).Replace(' PARM 1 .00005',' PARM 1 .00001')
 $input=Join-Path $root "$id.ZMX";[IO.File]::WriteAllText($input,$template);$sha=(Get-FileHash -LiteralPath $input).Hash.ToLowerInvariant()
 $ops=@(foreach($p in $pupils){foreach($surface in 1,2){foreach($code in 'REAX','REAY','REAZ','REAA','REAB','REAC','OPTH'){
  @{code=$code;int1=$surface;int2=1;data1=0;data2=0;data3=$p[0];data4=$p[1];data5=0;data6=0}
 }}})
 @{input=$input;inputSha256=$sha;zosApiPath='D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio';configuration=1;wavelength=1;zemaxVersion='2026 R1';adapter='surface-model-controls';captureScreenshots=$false;formalAcceptanceRequest=$false;modelOperands=$ops}|
  ConvertTo-Json -Depth 18|Set-Content -LiteralPath "$root/$id.request.json"
 $jobs+=@{id=$id;request="$id.request.json";inputSha256=$sha;budgetSeconds=300;queueBudgetSeconds=120
  frozenHost='artifacts/validation/incomparable-native-message-guard-build-20261101/ZemaxHost.exe';frozenHostSha256='8b323e6fa1e1e7eacc48f08d7a491cee968fcc0d10d8cf31857648bf05cc8fb4'}
 $models+=@{id=$id;input=$input;sourceSha256=$sha;gridSha256=(Get-FileHash -LiteralPath $grid).Hash.ToLowerInvariant();pupils=$pupils;nodes=[Math]::Max($spec.nx,[Math]::Max($spec.ny,$spec.nz));bodyLength=$spec.length;maximumStep=.00001;native="$root/$id";specification=$spec;purpose='Independent long path or non-centered unequal grid; reject overfitted interpolation/integration hypotheses'}
}
$models|ConvertTo-Json -Depth 15|Set-Content -LiteralPath "$root/models.json";$jobs|ConvertTo-Json -Depth 12|Set-Content -LiteralPath "$root/jobs.json"
