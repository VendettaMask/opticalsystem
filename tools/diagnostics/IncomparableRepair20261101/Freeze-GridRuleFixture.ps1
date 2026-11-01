param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
$destination=Join-Path $repo 'tests/OpticalSystemDesign.Tests/Fixtures/grid-gradient-phase-native-v1.json'
if(Test-Path -LiteralPath $destination){throw 'Never overwrite a frozen native fixture'}
$materials=@{};$cases=@();$curves=@()
function Add-Material([string]$sourcePath){
 $source=[IO.File]::ReadAllText($sourcePath)
 $name=[regex]::Match($source,'TYPE GRINSU11 ([^\r\n]+)').Groups[1].Value.Trim()
 $path=if(Test-Path -LiteralPath $name){$name}elseif($name -eq 'Sample.GGD'){Join-Path $repo 'src/OpticalSystemDesign.Core/Materials/Data/sample-grid-gradient.ggd'}else{throw "Missing grid $name"}
 $sha=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
 if(!$materials.ContainsKey($sha)){$materials[$sha]=@{sha256=$sha;bytesBase64=if($name -eq 'Sample.GGD'){$null}else{[Convert]::ToBase64String([IO.File]::ReadAllBytes($path))};bundledSample=($name -eq 'Sample.GGD')}}
 return $sha
}
foreach($folder in 'grid-rule-rays-native-20261101','grid-rule-holdout-native-20261101'){
 $root=Join-Path $repo "artifacts/validation/$folder"
 foreach($m in (Get-Content -LiteralPath "$root/models.json" -Raw|ConvertFrom-Json)){
  $data=Join-Path $m.native 'model-controls.json'
  $cases+=@{id=$m.id;entranceSurface=1;source=[IO.File]::ReadAllText($m.input);sourceSha256=(Get-FileHash -LiteralPath $m.input).Hash.ToLowerInvariant();grid=Add-Material $m.input
   nativePath=[IO.Path]::GetRelativePath($repo,$data).Replace('\','/');nativeSha256=(Get-FileHash -LiteralPath $data).Hash.ToLowerInvariant();rows=(Get-Content -LiteralPath $data -Raw|ConvertFrom-Json).rows}
 }
}
$axial=Join-Path $repo 'artifacts/validation/incomparable-axial-path-native-retry-20261101'
foreach($id in 'axial-n6-l1-h5E-05','axial-n8-l1-h5E-05'){
 $request=Get-Content -LiteralPath "$axial/$id.request.json" -Raw|ConvertFrom-Json;$data=Join-Path $axial "$id/model-controls.json"
 $cases+=@{id=$id;entranceSurface=2;source=[IO.File]::ReadAllText($request.input);sourceSha256=(Get-FileHash -LiteralPath $request.input).Hash.ToLowerInvariant();grid=Add-Material $request.input
  nativePath=[IO.Path]::GetRelativePath($repo,$data).Replace('\','/');nativeSha256=(Get-FileHash -LiteralPath $data).Hash.ToLowerInvariant();rows=(Get-Content -LiteralPath $data -Raw|ConvertFrom-Json).rows}
}
$root=Join-Path $repo 'artifacts/validation/grid-window-rounding-native-20261101';$input=Join-Path $root 'boundary-x4-n8.ZMX';$data=Join-Path $root 'entry-boundary/model-controls.json'
$cases+=@{id='exact-signed-boundary';entranceSurface=1;source=[IO.File]::ReadAllText($input);sourceSha256=(Get-FileHash -LiteralPath $input).Hash.ToLowerInvariant();grid=Add-Material $input
 nativePath=[IO.Path]::GetRelativePath($repo,$data).Replace('\','/');nativeSha256=(Get-FileHash -LiteralPath $data).Hash.ToLowerInvariant();rows=(Get-Content -LiteralPath $data -Raw|ConvertFrom-Json).rows}
foreach($m in (Get-Content -LiteralPath "$repo/artifacts/validation/grid-floor-query-index-20261101/models.json" -Raw|ConvertFrom-Json)){
 $data=Join-Path $m.native 'data.json';$s=(Get-Content -LiteralPath $data -Raw|ConvertFrom-Json).dataSeries[0]
 $curves+=@{id=$m.id;grid=Add-Material $m.input;axis=$m.axis;x=$m.x;y=$m.y;z=$m.z;coordinates=$s.x;indexValues=@($s.y|ForEach-Object {$_[0]})
  nativePath=[IO.Path]::GetRelativePath($repo,$data).Replace('\','/');nativeSha256=(Get-FileHash -LiteralPath $data).Hash.ToLowerInvariant()}
}
@{schema='GridGradientPhaseNativeV1';authority='OpticStudio 2026 R1 direct ZOS-API captures';rayGate=@{absolute=1e-8;relative=1e-6};queryGate=1e-12
 materials=$materials;cases=$cases;curves=$curves;note='Pure C# shared Core inputs. No native residual or chosen window supplies runtime evaluation. Original native boundary rounding is retained.'}|
 ConvertTo-Json -Depth 25|Set-Content -LiteralPath $destination
