param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$artifact=Join-Path $repo 'artifacts/validation';$root=Join-Path $repo 'validation/rechecks/2026-11-04/analysis-surface-reference'
function Read($p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Hash($p){(Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()}
function Save($name,$v){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $v -Depth 80),[Text.UTF8Encoding]::new($false))}
$summary=Read "$root/summary.json";$core=$summary.coreAfter
if($summary.distinctTests -ne 679 -or $summary.defaultNumericalArraysIdentical -ne 4 -or $summary.imagePairVerdictChanges -ne 0 -or $summary.crossAnalysisPriorPassLost -ne 0){throw 'Incomplete acceptance'}
$archive=Join-Path $root 'evidence.zip';if(Test-Path $archive){throw 'Already sealed'}
$files=[Collections.Generic.List[object]]::new()
function Add($path,$logical){$files.Add(@{path=[IO.Path]::GetFullPath($path);logical=$logical})}
function AddTree($path,$prefix){foreach($f in Get-ChildItem -LiteralPath $path -Recurse -File){$rel=[IO.Path]::GetRelativePath($path,$f.FullName).Replace('\','/');if($rel -match '(^|/)(bin|obj)/' -or $f.Extension -in @('.png','.svg','.pdf','.dll','.pdb','.exe')){continue};Add $f.FullName "$prefix/$rel"}}
$runtime=Join-Path $artifact 'analysis-definition-runtime-20261104'
$test=Join-Path $repo 'tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0';$mainTest=Join-Path $repo 'tests/OpticalSystemDesign.Tests/bin/Release/net10.0';$app=Join-Path $repo 'src/OpticalSystemDesign.App/bin/Release/net10.0'
$bindings=[Collections.Generic.List[object]]::new()
foreach($pdb in @("$runtime/OpticalSystemDesign.Core.pdb","$runtime/OpticalSystemDesign.Application.pdb","$runtime/OpticalSystemDesign.ZemaxComparison.pdb","$runtime/AnalysisDefinitionRepair20261104.pdb","$runtime/OpticalSystemDesign.Tests.pdb","$runtime/ResidualClosure20261103.pdb","$test/OpticalSystemDesign.Tests.pdb")){
 $stream=[IO.File]::OpenRead($pdb);$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream);$documents=[Collections.Generic.List[object]]::new()
 try{$reader=$provider.GetMetadataReader();foreach($handle in $reader.Documents){$d=$reader.GetDocument($handle);$path=$reader.GetString($d.Name);if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue};$sha=[Convert]::ToHexString($reader.GetBlobBytes($d.Hash)).ToLowerInvariant();if((Hash $path) -ne $sha){throw "Compiled source changed: $path"};$rel=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');Add $path "source/$rel";$documents.Add(@{path=$rel;sha256=$sha})}}finally{$provider.Dispose();$stream.Dispose()}
 $bindings.Add(@{pdb=[IO.Path]::GetRelativePath($repo,$pdb).Replace('\','/');sha256=(Hash $pdb);documents=$documents})
}
foreach($path in @($runtime,$test,$app)){if((Hash "$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
Save 'source-verification.json' @{coreSha256=$core;allListedSourcesMatchCompiled=$true;bindings=$bindings;sourceRevision=(Read "$root/source-revision.json");defaultAppCoreMatches=$true;appSha256=(Hash "$app/OpticalSystemDesign.App.dll")}
$studies=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'analysis-definition-*' -and $_.Name -notmatch 'runtime'})
foreach($study in $studies){AddTree $study.FullName ('runs/'+$study.Name);foreach($hostBinary in Get-ChildItem -LiteralPath $study.FullName -Recurse -File -Filter ZemaxHost.exe){Add $hostBinary.FullName ('native-hosts/'+$study.Name+'/'+[IO.Path]::GetRelativePath($study.FullName,$hostBinary.FullName).Replace('\','/'))}}
foreach($log in Get-ChildItem -LiteralPath $artifact -File|Where-Object {$_.Name -like 'analysis-definition-*' -and $_.Extension -in @('.log','.json') -and $_.Name -notmatch 'seal'}){Add $log.FullName ('logs/'+$log.Name)}
foreach($pair in @(@($runtime,'analysis'),@($test,'scoped-tests'))){foreach($f in Get-ChildItem -LiteralPath $pair[0] -File|Where-Object {$_.Name -match '^(OpticalSystemDesign.Core|OpticalSystemDesign.Application|OpticalSystemDesign.ZemaxComparison|AnalysisDefinitionRepair20261104|ObscuredReference20261101|MediumReference20261103|ResidualClosure20261103|OpticalSystemDesign.Tests)\.(dll|pdb|deps.json|runtimeconfig.json)$'}){Add $f.FullName ('runtime/'+$pair[1]+'/'+$f.Name)}}
$retireRuntimes=@(Get-ChildItem -LiteralPath $artifact -Directory|Where-Object {$_.Name -like 'analysis-definition-*runtime-20261104' -and $_.Name -ne 'analysis-definition-runtime-20261104'})
foreach($directory in $retireRuntimes){foreach($f in Get-ChildItem -LiteralPath $directory.FullName -Recurse -File){Add $f.FullName ('intermediate-runtime/'+$directory.Name+'/'+[IO.Path]::GetRelativePath($directory.FullName,$f.FullName).Replace('\','/'))}}
foreach($f in Get-ChildItem -LiteralPath $PSScriptRoot -File){Add $f.FullName ('source/tools/diagnostics/AnalysisDefinitionRepair20261104/'+$f.Name)}
foreach($f in Get-ChildItem (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs'){Add $f.FullName ('source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$f.Name)}
foreach($path in @($runtime,$test,$app)){if((Hash "$path/OpticalSystemDesign.Core.dll") -ne $core){throw 'Final Core differs'}}
$old=@{}
foreach($base in @('validation/rechecks/2026-10-31/all-example-accuracy','validation/rechecks/2026-11-01/testing-three','validation/rechecks/2026-11-02/huygens-common','validation/rechecks/2026-11-02/phase-common','validation/rechecks/2026-11-02/boundary-common','validation/rechecks/2026-11-02/biocular-reference','validation/rechecks/2026-11-03/aperture-footprint','validation/rechecks/2026-11-03/all-example-accuracy','validation/rechecks/2026-11-03/medium-reference','validation/rechecks/2026-11-03/residual-closure','validation/rechecks/2026-11-03/definition-reference','validation/rechecks/2026-11-04/all-example-accuracy','validation/rechecks/2026-11-04/fft-sampling-policy','validation/rechecks/2026-11-04/image-simulation-pickup','validation/rechecks/2026-11-04/zemax-definition-review')){
 $folder=Join-Path $repo $base
 if(Test-Path "$folder/archive-index.json"){foreach($e in (Read "$folder/archive-index.json")){$container=if($e.archive){$e.archive}else{'evidence.zip'};$entry=if($e.retainedFile -or ($e.PSObject.Properties.Name -contains 'entry' -and $null -eq $e.entry)){$null}elseif($e.entry){$e.entry}else{'payloads/'+$e.sha256};$path=if($e.retainedFile){$e.retainedFile}else{$container};$old[$e.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $folder $path));entry=$entry}}}
 else{foreach($e in (Read "$folder/manifest.json").entries){$old[$e.sha256]=@{path=(Join-Path $folder 'evidence-current.zip');entry=$e.path}}}
}
$referenceFolder=Join-Path $repo 'validation/rechecks/2026-11-01/example-incomparable-repair'
foreach($e in (Read "$referenceFolder/manifest.json").entries){$old[$e.sha256]=@{path=(Join-Path $referenceFolder 'accepted-evidence.zip');entry=$e.path}}
foreach($f in Get-ChildItem -LiteralPath $root -File|Where-Object Extension -in @('.json','.csv','.md','.log')){$old[(Hash $f.FullName)]=@{path=$f.FullName;entry=$null}}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verified=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{}
try{foreach($f in $files){
 $sha=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
 if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $sha){throw 'Conflicting source'};continue};$logical[$f.logical]=$sha
 if($old.ContainsKey($sha)){
  $ref=$old[$sha]
  if(!$verified.ContainsKey($sha)){
   if($null -eq $ref.entry){$actual=Hash $ref.path}else{if(!$opened.ContainsKey($ref.path)){$opened[$ref.path]=[IO.Compression.ZipFile]::OpenRead($ref.path)};$s=$opened[$ref.path].GetEntry($ref.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}}
   if($actual -ne $sha){throw 'Prior authority hash mismatch'};$verified[$sha]=$true
  }
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive=[IO.Path]::GetRelativePath($root,$ref.path).Replace('\','/');entry=$ref.entry})
 }else{
  if(!$new.ContainsKey($sha)){$e=$zip.CreateEntry('payloads/'+$sha,[IO.Compression.CompressionLevel]::Optimal);$a=[IO.File]::OpenRead($f.path);$b=$e.Open();try{$a.CopyTo($b)}finally{$a.Dispose();$b.Dispose()};$new[$sha]=$length}
  $mapping.Add(@{logicalPath=$f.logical;sha256=$sha;length=$length;archive='evidence.zip';entry='payloads/'+$sha})
 }
}}finally{$zip.Dispose();foreach($z in $opened.Values){$z.Dispose()}}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{if($zip.Entries.Count -ne $new.Count){throw 'Payload count differs'};foreach($e in $zip.Entries){$sha=$e.FullName.Substring(9);$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()};if($actual -ne $sha -or $e.Length -ne $new[$sha]){throw 'New payload mismatch'}}}finally{$zip.Dispose()}
foreach($f in $files){if((Hash $f.path) -ne $logical[$f.logical]){throw 'Run changed during seal'}}
Save 'archive-index.json' $mapping
Save 'archive-verification.json' @{archive='evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;logicalFiles=$mapping.Count;newVerifiedPayloads=$new.Count;reusedVerifiedPayloads=$verified.Count;allReadBackBeforeRetirement=$true;finalRuntimeRetained=$true}
foreach($directory in @($studies+$retireRuntimes)){$path=[IO.Path]::GetFullPath($directory.FullName);$parent=[IO.Path]::GetFullPath($artifact)+[IO.Path]::DirectorySeparatorChar;if(!$path.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or $directory.Name -notmatch '^analysis-definition-' -or $directory.Name -eq 'analysis-definition-runtime-20261104'){throw 'Unsafe retirement target'};Remove-Item -LiteralPath $path -Recurse -Force}
Write-Output "Verified $($new.Count) new / $($verified.Count) reused payloads; $($mapping.Count) logical files."
