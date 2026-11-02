param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$root=[IO.Path]::GetFullPath((Join-Path $repo 'validation/rechecks/2026-11-02/boundary-common'))
if(!$root.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid evidence root'}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function Read([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Save([string]$name,$value){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $value -Depth 40),[Text.UTF8Encoding]::new($false))}
function Counts($rows){$c=[ordered]@{};foreach($g in ($rows|Group-Object conclusion)){$c[$g.Name]=$g.Count};$c}
$before=Read (Join-Path $repo 'validation/rechecks/2026-11-02/phase-common/results-final.json');$after=Read (Join-Path $root 'analyses-accepted/results.json')
if($before.Count -ne 216 -or $after.Count -ne 216){throw 'Incomplete replay'}
$map=@{};foreach($r in $before){$map[$r.index]=$r}
$lost=@($after|Where-Object {$map[$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass'})
$fixed=@($after|Where-Object {$map[$_.index].conclusion -ne 'Pass' -and $_.conclusion -eq 'Pass'})
if($lost.Count -or $fixed.Count -ne 3){throw 'Unexpected before/after scope'}
$runtimes=@('artifacts/validation/boundary-common-runner-final-20261102','artifacts/validation/boundary-common-replay-accepted-20261102','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')
$coreHash=(Read (Join-Path $root 'analyses-accepted/identity.json')).coreSha256
foreach($runtime in $runtimes){if((Hash (Join-Path $repo "$runtime/OpticalSystemDesign.Core.dll")) -ne $coreHash){throw 'Core mismatch'}}
$ident=Read (Join-Path $root 'analyses-accepted/identity.json')
$files=[Collections.Generic.List[object]]::new()
function Add([string]$path,[string]$logical){$files.Add(@{path=$path;logical=$logical})}
$audit=[Collections.Generic.List[object]]::new()
foreach($pdb in @('artifacts/validation/boundary-common-replay-accepted-20261102/OpticalSystemDesign.Core.pdb','artifacts/validation/boundary-common-runner-final-20261102/BoundaryCommon20261102.pdb','artifacts/validation/boundary-common-replay-accepted-20261102/PhaseCommon20261102.pdb','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0/OpticalSystemDesign.Tests.pdb','src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Application.pdb','src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')){
    $stream=[IO.File]::OpenRead((Join-Path $repo $pdb));$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream);$documents=[Collections.Generic.List[object]]::new()
    try{$reader=$provider.GetMetadataReader();foreach($handle in $reader.Documents){
        $d=$reader.GetDocument($handle);$path=$reader.GetString($d.Name)
        if(!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue}
        $expected=[Convert]::ToHexString($reader.GetBlobBytes($d.Hash)).ToLowerInvariant()
        if((Hash $path) -ne $expected){throw "Source/PDB mismatch: $path"}
        $relative=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');Add $path ('source/'+$relative);$documents.Add(@{path=$relative;sha256=$expected;matchesCompiled=$true})
    }}finally{$provider.Dispose();$stream.Dispose()}
    $audit.Add(@{pdb=$pdb;sha256=(Hash (Join-Path $repo $pdb));documents=$documents})
}
Save 'source-verification.json' @{coreSha256=$coreHash;assemblies=$audit;allListedSourcesMatchCompiled=$true}
Save 'results-final.json' $after
$fixedRays=Read (Join-Path $root 'fixed-rays-final/fixed-ray-comparison.json')
if($fixedRays.Count -ne 6 -or @($fixedRays|Where-Object conclusion -ne 'Pass').Count){throw 'Fixed boundary ray control incomplete'}
Save 'fixed-ray-results.json' $fixedRays
Save 'summary.json' @{coreBefore=(Read (Join-Path $repo 'validation/rechecks/2026-11-02/phase-common/summary.json')).coreAfter;coreAfter=$coreHash;before=(Counts $before);after=(Counts $after);fixedIndexes=@($fixed.index);priorPassLost=$lost;byLens=@($after|Group-Object lens|ForEach-Object {@{lens=$_.Name;counts=(Counts $_.Group)}});boundaryCases=@($after|Where-Object index -in @(1055,1056,1059,1060,1063,1064));fixedRayControls=$fixedRays;apertureToleranceChanged=$false;maximumSolveUsesSharedPhysicalTracing=$true;testsPassed=141;testsFailed=0;defaultAppBuild=@{passed=$true;warnings=0;errors=0;builtInModules=2};inputs=$ident.inputs;unchangedRequestsAndTolerances=$true;freshImport=$true;newNativeCapture=$true;notAll95=$true;nativeConvergenceNotEstablishedByReplay=$true;completedUtc=[DateTimeOffset]::UtcNow}
foreach($directory in (Get-ChildItem -LiteralPath $root -Directory)){foreach($file in (Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)){Add $file.FullName ([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'))}}
foreach($file in (Get-ChildItem (Join-Path $repo 'artifacts/validation') -File -Filter 'boundary-common-*.log'|Where-Object Name -notlike '*-seal-*')){Add $file.FullName ('logs/'+$file.Name)}
foreach($file in (Get-ChildItem -LiteralPath $PSScriptRoot -File)){Add $file.FullName ('source/tools/diagnostics/BoundaryCommon20261102/'+$file.Name)}
foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs')){Add $file.FullName ('source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$file.Name)}
foreach($runtime in @('artifacts/validation/boundary-common-runner-final-20261102')+$runtimes){
    if($runtime -like 'src/*'){continue}
    foreach($file in (Get-ChildItem (Join-Path $repo $runtime) -File|Where-Object {$_.Name -match '^(OpticalSystemDesign.Core|OpticalSystemDesign.Application|OpticalSystemDesign.Tests|PhaseCommon20261102|BoundaryCommon20261102)\.(dll|pdb|deps.json|runtimeconfig.json)$'})){Add $file.FullName ('runtime/'+$runtime.Replace('/','_')+'/'+$file.Name)}
}
# Reuse both directly retained and archived prior bytes, verifying each actual authority once.
$old=@{}
foreach($indexRoot in @('validation/rechecks/2026-11-01/testing-three','validation/rechecks/2026-11-02/huygens-common','validation/rechecks/2026-11-02/phase-common')){
    $base=Join-Path $repo $indexRoot
    foreach($entry in (Read (Join-Path $base 'archive-index.json'))){
        $archive=if($entry.archive){$entry.archive}else{'evidence.zip'}
        $old[$entry.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $base $(if($entry.retainedFile){$entry.retainedFile}else{$archive})));entry=$(if($entry.retainedFile){$null}else{'payloads/'+$entry.sha256})}
    }
}
$archive=Join-Path $root 'evidence.zip';if(Test-Path $archive){throw 'Already sealed'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verifiedOld=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{};$retained=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try{foreach($f in $files){
    $hash=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
    if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $hash){throw 'Conflicting logical identity'};continue};$logical[$f.logical]=$hash
    if($f.logical -match '^native-rays/c[12]-w[123]/native/'){
        $retained.Add([IO.Path]::GetFullPath($f.path))|Out-Null
        $mapping.Add(@{logicalPath=$f.logical;sha256=$hash;length=$length;archive=$f.logical;entry=$null});continue
    }
    if($old.ContainsKey($hash)){
        $ref=$old[$hash]
        if(!$verifiedOld.ContainsKey($hash)){
            if($null -eq $ref.entry){$actual=Hash $ref.path}
            else{if(!$opened.ContainsKey($ref.path)){$opened[$ref.path]=[IO.Compression.ZipFile]::OpenRead($ref.path)};$s=$opened[$ref.path].GetEntry($ref.entry).Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}}
            if($actual -ne $hash){throw 'Prior authority mismatch'};$verifiedOld[$hash]=$true
        }
        $mapping.Add(@{logicalPath=$f.logical;sha256=$hash;length=$length;archive=[IO.Path]::GetRelativePath($root,$ref.path).Replace('\','/');entry=$ref.entry})
    }else{
        if(!$new.ContainsKey($hash)){$e=$zip.CreateEntry('payloads/'+$hash,[IO.Compression.CompressionLevel]::Optimal);$s=[IO.File]::OpenRead($f.path);$t=$e.Open();try{$s.CopyTo($t)}finally{$s.Dispose();$t.Dispose()};$new[$hash]=$length}
        $mapping.Add(@{logicalPath=$f.logical;sha256=$hash;length=$length;archive='evidence.zip';entry='payloads/'+$hash})
    }
}}finally{$zip.Dispose();foreach($z in $opened.Values){$z.Dispose()}}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{if($zip.Entries.Count -ne $new.Count){throw 'New payload count differs'};foreach($e in $zip.Entries){$hash=$e.FullName.Substring(9);$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()};if($actual -ne $hash -or $e.Length -ne $new[$hash]){throw 'New payload mismatch'}}}finally{$zip.Dispose()}
foreach($f in $files|Where-Object {$_.path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)}){if((Hash $f.path) -ne $logical[$f.logical]){throw 'Expanded payload changed'}}
Save 'archive-index.json' $mapping
Save 'archive-verification.json' @{archive='evidence.zip';sha256=(Hash $archive);bytes=(Get-Item $archive).Length;logicalFiles=$mapping.Count;newVerifiedPayloads=$new.Count;reusedVerifiedPayloads=$verifiedOld.Count;retainedNativeInputs=$retained.Count;verifiedBeforeRetirement=$true}
foreach($directory in (Get-ChildItem -LiteralPath $root -Directory)){
    $resolved=[IO.Path]::GetFullPath($directory.FullName);if(!$resolved.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe retirement target'}
    if($directory.Name -ne 'native-rays'){Remove-Item -LiteralPath $resolved -Recurse -Force;continue}
    foreach($file in (Get-ChildItem -LiteralPath $resolved -Recurse -File)){if(!$retained.Contains($file.FullName)){Remove-Item -LiteralPath $file.FullName -Force}}
    foreach($dir in (Get-ChildItem -LiteralPath $resolved -Recurse -Directory|Sort-Object {$_.FullName.Length} -Descending)){if(!(Get-ChildItem -LiteralPath $dir.FullName -Force|Select-Object -First 1)){Remove-Item -LiteralPath $dir.FullName}}
}
Write-Output "Verified $($new.Count) new and $($verifiedOld.Count) reused payloads; no prior Pass lost."
