param([ValidateSet('Reference','Evidence')][string]$Mode,
 [string]$RepositoryRoot=(Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference='Stop'
$root=Join-Path $RepositoryRoot 'artifacts/validation/riflescope-mtf-field-20261031'
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Json($v,$p){$v|ConvertTo-Json -Depth 100|Set-Content -LiteralPath $p -Encoding utf8NoBOM}
function CopyFile($from,$to){New-Item -ItemType Directory (Split-Path $to) -Force|Out-Null;Copy-Item -LiteralPath $from -Destination $to}
function Archive($stage,$destination,$name){
 New-Item -ItemType Directory $destination -Force|Out-Null;$path=Join-Path $destination $name
 if(Test-Path -LiteralPath $path){throw "Immutable archive exists: $path"}
 [IO.Compression.ZipFile]::CreateFromDirectory($stage,$path,[IO.Compression.CompressionLevel]::Optimal,$false)
 $entries=@();$zip=[IO.Compression.ZipFile]::OpenRead($path)
 try{foreach($e in $zip.Entries|Sort-Object FullName){$s=$e.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};$entries+=@{path=$e.FullName;sha256=$sha;bytes=$e.Length}}}finally{$zip.Dispose()}
 Json @{sha256=(Hash $path);bytes=(Get-Item $path).Length;entries=$entries} "$destination/manifest.json"
 $zip=[IO.Compression.ZipFile]::OpenRead($path)
 try{foreach($r in $entries){$e=$zip.GetEntry($r.path);$s=$e.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($s))}finally{$s.Dispose()};if($sha -ne $r.sha256 -or $e.Length -ne $r.bytes){throw 'Archive readback mismatch'}}}finally{$zip.Dispose()}
 Json @{sha256=(Hash $path);allMatched=$true;verifiedEntries=$entries.Count} "$destination/verification.json"
 Write-Output "$name : $(Hash $path); $($entries.Count) entries; $((Get-Item $path).Length) bytes"
}
if($Mode -eq 'Reference'){
 $stage="$root/reference-stage";if(Test-Path $stage){throw 'Fresh reference stage required'}
 New-Item -ItemType Directory $stage|Out-Null
 $case=(Get-Content "$root/cases.json" -Raw|ConvertFrom-Json)[0]
 if((Hash $case.source) -ne $case.sourceSha256 -or (Hash $case.snapshot) -ne $case.snapshotSha256){throw 'Input changed'}
 CopyFile $case.source "$stage/inputs/Afocal_Riflescope.ZMX";CopyFile $case.snapshot "$stage/inputs/riflescope.optic.json"
 foreach($cohort in @('native','native-high','native-huygens-split')){
  $execution=Get-Content "$root/$cohort/execution.json" -Raw|ConvertFrom-Json
  CopyFile "$root/$cohort/execution.json" "$stage/$cohort/execution.json"
  foreach($job in $execution | Where-Object {$_.result.ExitCode -eq 0 -and $_.sourceUnchanged}){
   foreach($file in Get-ChildItem "$root/$cohort/$($job.id)" -File){CopyFile $file.FullName "$stage/$cohort/$($job.id)/$($file.Name)"}
   CopyFile "$root/$cohort/$($job.id).request.json" "$stage/$cohort/$($job.id).request.json"
  }
 }
 foreach($file in @('native-reference-index.json','native-single-reference-index.json')){CopyFile "$root/$file" "$stage/$file"}
 Json @{referenceBasis='FourierConvergedDirectNativeAndHuygensFiniteSamplingRegression';fourierNativeSampling=@(256,512);
  huygensNativeSampling=@(64,128,256);huygensConvergenceAccepted=$false;psfSampling=32;sourceSha256=$case.sourceSha256;snapshotSha256=$case.snapshotSha256;
  frequenciesCyclesPerMilliradian=@(.1,.2,.4,.8,1.6,3.2);fieldDensity=10;scan='+Y';wavelength=0;removeVignetting=$true;polarization=$false;
  nativeAngleUnit='ArcMinute';productAngleUnit='Milliradian';policySha256=(Hash "$RepositoryRoot/tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
  directBuffers=$true;referenceInterpolationUsed=$false;historicalFailuresPreserved=$true} "$stage/contract.json"
 Archive $stage "$RepositoryRoot/validation/zemax/2026-r1/riflescope-mtf-field-2026-10-31" 'reference.zip';return
}
if($Mode -eq 'Evidence'){
 $stage="$root/evidence-stage";if(Test-Path $stage){throw 'Fresh evidence stage required'}
 New-Item -ItemType Directory $stage|Out-Null
 foreach($file in Get-ChildItem $root -File -Recurse){$relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/');if($relative.StartsWith('evidence-stage/') -or $relative.StartsWith('reference-stage/')){continue};CopyFile $file.FullName "$stage/study/$relative"}
 foreach($folder in Get-ChildItem "$RepositoryRoot/artifacts/validation" -Directory -Filter 'riflescope-mtf-field-runner-20261031-*'){
  foreach($file in Get-ChildItem $folder.FullName -File){CopyFile $file.FullName "$stage/runners/$($folder.Name)/$($file.Name)"}
 }
 $test="$RepositoryRoot/artifacts/validation/riflescope-mtf-field-tests-20261031"
 foreach($file in Get-ChildItem $test -File){CopyFile $file.FullName "$stage/runtime/$($file.Name)"}
 foreach($file in Get-ChildItem "$test/TestResults" -File){CopyFile $file.FullName "$stage/tests/$($file.Name)"}
 foreach($file in Get-ChildItem "$test/Fixtures/riflescope-mtf-field" -File){CopyFile $file.FullName "$stage/runtime/Fixtures/riflescope-mtf-field/$($file.Name)"}
 foreach($file in Get-ChildItem "$RepositoryRoot/artifacts/validation" -File -Filter 'riflescope-*-20261031.log'){CopyFile $file.FullName "$stage/logs/$($file.Name)"}
 $sources=@('src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs','src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs',
  'src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs','src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.AfocalQuadrature.cs',
  'tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalHuygensImageProjectionTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/RiflescopeMtfFieldTests.cs',
  'tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json',
  'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/NativeHuygensFieldSpectra.cs',
  'tools/validation/Seal-RiflescopeMtfField.ps1','docs/RIFLESCOPE_MTF_FIELD_2026-10-31.md')
 $sources+=Get-ChildItem "$RepositoryRoot/tools/diagnostics/RiflescopeMtfField20261031" -File|ForEach-Object {[IO.Path]::GetRelativePath($RepositoryRoot,$_.FullName)}
 foreach($relative in $sources){CopyFile "$RepositoryRoot/$relative" "$stage/source/$relative"}
 Archive $stage "$RepositoryRoot/validation/rechecks/2026-10-31/riflescope-mtf-field" 'evidence-current.zip'
}
