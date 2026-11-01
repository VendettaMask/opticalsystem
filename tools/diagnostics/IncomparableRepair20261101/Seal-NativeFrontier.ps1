param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath $Repository).Path
$destination=Join-Path $repo 'validation/rechecks/2026-11-01/example-incomparable-repair'
$archive=Join-Path $destination 'native-frontier.zip'
foreach($name in 'native-frontier.zip','native-frontier-manifest.json','native-frontier-verification.json','native-frontier-scope.json'){
 if(Test-Path -LiteralPath (Join-Path $destination $name)){throw 'Never overwrite frozen evidence'}
}
$frontier=Join-Path $repo 'artifacts/validation/incomparable-latest-frontier-20261101'
$scope=Get-Content -LiteralPath "$frontier/scope.json" -Raw|ConvertFrom-Json
$coreHash=(Get-FileHash -LiteralPath "$repo/src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll").Hash.ToLowerInvariant()
if($coreHash -ne $scope.coreSha256 -or $scope.gatesChanged -or $scope.all27Closed){throw 'Unexpected product identity or scope'}
$mtf=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-final-mtf-2048-assessment-20261101/convergence-results.json" -Raw|ConvertFrom-Json
if($mtf.conclusion -ne 'Pass'){throw 'Native MTF convergence missing'}
$energy=Get-Content -LiteralPath "$repo/artifacts/validation/incomparable-energy-native-memory-control-20261101/execution.json" -Raw|ConvertFrom-Json
if($energy.Count -ne 2 -or $energy[1].result.ExitCode -eq 0){throw 'Expected energy failure evidence missing'}
$prior=@(foreach($pair in @(
 @{name='accepted-evidence.zip';hash='4c8bb49a9546f4bfcd076f0f46c7f2fc203b04450f047f1008a7d0c1f37408cd'},
 @{name='psf-evidence.zip';hash='87e0fc48419b7f37b11c152cd4a04b47897b37140dd7266890f94f2f562d8671'},
 @{name='bounded-supplement.zip';hash='59cbc409be950b7388c627d3af79b45fba10b79c074e1ccb7e0b9cb6c61b0866'})){
 $actual=(Get-FileHash -LiteralPath (Join-Path $destination $pair.name)).Hash.ToLowerInvariant()
 if($actual -ne $pair.hash){throw 'Earlier archive changed'}
 @{name=$pair.name;sha256=$actual;unchanged=$true}
})
Copy-Item -LiteralPath "$frontier/scope.json" -Destination "$destination/native-frontier-scope.json"
$files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-File([string]$path){
 $full=(Resolve-Path -LiteralPath $path).Path
 if(!$full.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence outside repository'}
 [void]$files.Add($full)
}
$roots=@(
 'incomparable-latest-frontier-20261101',
 'incomparable-current-accepted-assessment-20261101',
 'incomparable-grid-mtf-native-high-corrected-20261101',
 'incomparable-grid-mtf-native-high-corrected-step-assessment-20261101',
 'incomparable-final-mtf-2048-assessment-20261101',
 'incomparable-energy-native-memory-control-20261101',
 'incomparable-grid-pending-native-refinement-20261101',
 'incomparable-profile-native-fixed4096-20261101',
 'incomparable-profile-native-fixed4096-assessment-20261101',
 'incomparable-profile-native-step-confirmed-20261101',
 'incomparable-axial-path-native-retry-20261101',
 'incomparable-axial-path-native-20261101',
 'incomparable-grid-boundary-native-20261101',
 'incomparable-axial-nearzero-native-control-20261101',
 'incomparable-transverse-native-controls-20261101',
 'incomparable-transverse-cross-native-controls-20261101',
 'incomparable-transverse-axis-limit-native-20261101',
 'incomparable-transverse-opl-order-native-20261101',
 'incomparable-transverse-step-native-controls-20261101',
 'incomparable-small-native-transverse-lane-20261101',
 'incomparable-native-grin-index-profile-20261101',
 'incomparable-native-grin-index-profile-retry-20261101',
 'incomparable-native-grin-index-profile-fixed-coordinate-20261101',
 'incomparable-native-grin-index-profile-assessment-20261101',
 'incomparable-native-index-nearest-assessment-20261101',
 'incomparable-native-window-candidates-20261101',
 'incomparable-native-window-rule-holdout-20261101',
 'incomparable-native-window-rule-assessment-20261101',
 'incomparable-fft-native-phase-cause-20261101',
 'incomparable-nearest-interface-private-core-20261101',
 'incomparable-local-transverse-phase-private-core-20261101',
 'incomparable-local-floor-phase-private-core-20261101',
 'incomparable-local-xyz-phase-private-core-20261101',
 'incomparable-signed-window-phase-private-core-20261101',
 'incomparable-signed-window-mtf-private-core-20261101')
foreach($root in $roots){
 $folder=Join-Path $repo "artifacts/validation/$root"
 if(!(Test-Path -LiteralPath $folder)){throw "Missing study: $root"}
 $paths=& rg --files --hidden $folder
 if($LASTEXITCODE -ne 0){throw 'Evidence enumeration failed'}
 foreach($path in $paths){
  if($path -match '[\\/](runtime|runner|core-source|private-core|bin|obj)[\\/]'){continue}
  if([IO.Path]::GetExtension($path).ToLowerInvariant() -in '.json','.txt','.csv','.ggd','.zmx'){Add-File $path}
 }
 $modelsPath=Join-Path $folder 'models.json'
 if(Test-Path -LiteralPath $modelsPath){
  foreach($model in (Get-Content -LiteralPath $modelsPath -Raw|ConvertFrom-Json)){
   if($model.input -and (Test-Path -LiteralPath $model.input)){
    Add-File $model.input
    $grid=[IO.Path]::ChangeExtension($model.input,'GGD');if(Test-Path -LiteralPath $grid){Add-File $grid}
   }
  }
 }
}
foreach($variant in 'incomparable-nearest-interface-private-core-20261101','incomparable-local-transverse-phase-private-core-20261101',
 'incomparable-local-floor-phase-private-core-20261101','incomparable-local-xyz-phase-private-core-20261101','incomparable-signed-window-phase-private-core-20261101'){
 foreach($relative in 'runtime/OpticalSystemDesign.Core.dll','core-source/Materials/GridGradientIndexProfile.cs','core-source/Propagation/GradientIndexRayIntegrator.cs'){
  $path=Join-Path $repo "artifacts/validation/$variant/$relative";if(Test-Path -LiteralPath $path){Add-File $path}
 }
}
foreach($file in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add-File $file.FullName}
foreach($relative in 'docs/EXAMPLE_INCOMPARABLE_REPAIR_2026-11-01.md','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs',
 'src/OpticalSystemDesign.Core/Materials/Data/sample-grid-gradient.ggd',
 'artifacts/validation/incomparable-current-accepted-assessment-20261101/grid-resource-identity.json',
 'artifacts/validation/incomparable-native-message-guard-build-20261101/ZemaxHost.exe',
 'artifacts/validation/incomparable-native-message-guard-build-20261101/ZemaxHost.exe.config',
 'validation/rechecks/2026-11-01/example-incomparable-repair/native-frontier-scope.json') {Add-File (Join-Path $repo $relative)}
$manifest=@(foreach($file in @($files|Sort-Object)){
 @{entry=[IO.Path]::GetRelativePath($repo,$file).Replace('\','/');source=$file;length=(Get-Item -LiteralPath $file).Length
 sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant()}
})
Add-Type -AssemblyName System.IO.Compression
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $manifest){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.source,$file.entry,[IO.Compression.CompressionLevel]::Optimal)}}finally{$zip.Dispose()}
$readback=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($file in $manifest){$stream=$readback.GetEntry($file.entry).Open();try{
 if([Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) -ne $file.sha256){throw 'Archive hash mismatch'}
}finally{$stream.Dispose()}}}finally{$readback.Dispose()}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$destination/native-frontier-manifest.json"
@{archive=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();bytes=(Get-Item $archive).Length;entries=$manifest.Count
 allEntriesReadBackAndMatched=$true;closedRequests=$scope.closedRequestCount;openRequests=$scope.openIndices.Count;all27Closed=$false
 coreSha256=$coreHash;priorArchives=$prior;sealedUtc=[DateTimeOffset]::UtcNow}|ConvertTo-Json -Depth 8|
 Set-Content -LiteralPath "$destination/native-frontier-verification.json"
Get-Content -LiteralPath "$destination/native-frontier-verification.json"
