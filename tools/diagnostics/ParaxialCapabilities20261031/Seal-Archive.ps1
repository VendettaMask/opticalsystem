param([string]$Repository='D:/Projects/opticalsystem')
$ErrorActionPreference='Stop';$repo=(Resolve-Path -LiteralPath $Repository).Path
$study=Join-Path $repo 'artifacts/validation/model-capabilities-stage2-20261031'
$report=Join-Path $repo 'validation/rechecks/2026-10-31/model-capabilities-stage2'
$runtime=Join-Path $repo 'artifacts/validation/model-capabilities-stage2-runner-20261031'
$summary=Get-Content "$report/summary.json" -Raw|ConvertFrom-Json
function Hash([string]$p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
if((Hash "$runtime/OpticalSystemDesign.Core.dll") -ne $summary.coreSha256){throw 'Frozen Core changed'}
if(Test-Path -LiteralPath "$report/verification.json"){throw 'Already sealed'}
if(-not(Test-Path -LiteralPath "$report/archive-manifest.failed-disk-full.json")){
 Move-Item -LiteralPath "$report/archive-manifest.json" -Destination "$report/archive-manifest.failed-disk-full.json"
}
$entries=[Collections.Generic.List[object]]::new();$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Payload([string]$path,[string]$name){if(-not $names.Add($name)){throw 'Duplicate entry'};$script:entries.Add([pscustomobject]@{source=$path;name=$name;bytes=(Get-Item -LiteralPath $path).Length;sha256=(Hash $path)})}
foreach($pair in @(@($study,'study'),@($runtime,'runtime'),@($report,'report'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -Recurse -File|Where-Object Name -ne 'archive-manifest.json'){
 Add-Payload $f.FullName ($pair[1]+'/'+[IO.Path]::GetRelativePath($pair[0],$f.FullName).Replace('\','/'))
}}
foreach($folder in 'paraxial-phase-native-20261031','paraxial-phase-native-20261031-02','grating-phase-native-20261031','binary-hologram-native-20261031','irregular-native-20261031','diffractive-firstorder-native-20261031'){
 $base=Join-Path $repo "artifacts/validation/$folder";foreach($f in Get-ChildItem -LiteralPath $base -Recurse -File|Where-Object {$_.Extension -in '.json','.zmx'}){
  Add-Payload $f.FullName ('native/'+$folder+'/'+[IO.Path]::GetRelativePath($base,$f.FullName).Replace('\','/'))
 }
}
foreach($relative in (& rg --files src/OpticalSystemDesign.Core tools/diagnostics/ParaxialCapabilities20261031)){
 Add-Payload (Join-Path $repo $relative) ('source/'+$relative.Replace('\','/'))
}
foreach($relative in 'docs/MODEL_CAPABILITIES_STAGE2_2026-10-31.md','tests/OpticalSystemDesign.Tests/ParaxialLensModelTests.cs','tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj','tests/OpticalSystemDesign.Tests/TestResults/model-capabilities-stage2-20261031.trx','artifacts/validation/model-capabilities-stage2-tests-20261031.log','artifacts/validation/model-capabilities-stage2-app-build-20261031.log','artifacts/validation/model-capabilities-stage2-space-cleanup-20261031.json','artifacts/validation/model-capabilities-stage2-foreign-runtime-cleanup-20261031.json'){
 Add-Payload (Join-Path $repo $relative) ('proof/'+$relative)
}
foreach($name in 'paraxial-phase-native-v1.json','grating-phase-native-v1.json','diffractive-phase-native-v1.json','diffractive-firstorder-native-v1.json','irregular-native-v1.json'){
 Add-Payload (Join-Path $repo "tests/OpticalSystemDesign.Tests/Fixtures/$name") ('proof/fixtures/'+$name)
}
$manifest=@($entries|ForEach-Object {[pscustomobject]@{name=$_.name;bytes=$_.bytes;sha256=$_.sha256}})
$manifest|ConvertTo-Json -Depth 6|Set-Content "$report/archive-manifest.json" -Encoding utf8
$archive=Join-Path $repo 'validation/repairs/2026-10-31/model-capabilities-stage2-evidence.zip'
if(Test-Path -LiteralPath $archive){throw 'Archive exists'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($e in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$e.source,$e.name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,"$report/archive-manifest.json",'archive-manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null}finally{$zip.Dispose()}
$verified=0;$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{foreach($e in $manifest){$entry=$zip.GetEntry($e.name);if($null -eq $entry -or $entry.Length -ne $e.bytes){throw 'Entry size mismatch'};$stream=$entry.Open();try{$hash=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()};if($hash -ne $e.sha256){throw 'Entry hash mismatch'};$verified++}}finally{$zip.Dispose()}
[pscustomobject]@{archive='validation/repairs/2026-10-31/model-capabilities-stage2-evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length
 verifiedPayloads=$verified;allNonManifestPayloadsReadBack=$true;manifestSha256=(Hash "$report/archive-manifest.json");coreSha256=$summary.coreSha256
 firstAttempt='DiskFull_NoValidArchiveProduced';recovery='Removed only task-owned duplicate build outputs and unused foreign native runtimes; retained Windows x64 frozen runner'
 priorArchiveIntegrity='prior-archive-integrity.json'}|ConvertTo-Json -Depth 8|Set-Content "$report/verification.json" -Encoding utf8
Get-Content "$report/verification.json"
