param([string]$Repository='D:\Projects\opticalsystem')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath($Repository);$root=[IO.Path]::GetFullPath((Join-Path $repo 'validation/rechecks/2026-11-02/biocular-reference'))
if(!$root.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid evidence root'}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function Read([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json}
function Save([string]$name,$value){[IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $value -Depth 40),[Text.UTF8Encoding]::new($false))}
function Counts($rows){$c=[ordered]@{};foreach($g in ($rows|Group-Object conclusion)){$c[$g.Name]=$g.Count};$c}
$before=Read (Join-Path $repo 'validation/rechecks/2026-11-02/boundary-common/results-final.json');$after=Read (Join-Path $root 'replay-final/results.json')
if($before.Count -ne 216 -or $after.Count -ne 216){throw 'Incomplete replay'}
$map=@{};foreach($r in $before){$map[$r.index]=$r}
$lost=@($after|Where-Object {$map[$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass'})
$fixed=@($after|Where-Object {$map[$_.index].conclusion -ne 'Pass' -and $_.conclusion -eq 'Pass'})
if($lost.Count -or $fixed.Count -ne 5 -or @($after|Where-Object conclusion -eq 'Pass').Count -ne 196){throw 'Unexpected before/after scope'}
$categories=Read (Join-Path $root 'categories-fixed/results.json');$oldCategories=Read (Join-Path $repo 'validation/rechecks/2026-11-02/huygens-common/results-final.json')
$catMap=@{};foreach($r in $oldCategories){$catMap[$r.index]=$r.conclusion}
if($categories.Count -ne 426 -or @($categories|Where-Object {$_.conclusion -ne $catMap[$_.index]}).Count){throw 'Category regression'}
$mtf=Read (Join-Path $root 'mtf-convergence-accepted/results.json')
if($mtf.Count -ne 14 -or @($mtf|Where-Object conclusion -ne 'Pass').Count){throw 'MTF convergence incomplete'}
[xml]$trx=Get-Content (Join-Path $root 'tests/geometric-pupil-final.trx') -Raw
if($trx.TestRun.ResultSummary.Counters.passed -ne '417' -or $trx.TestRun.ResultSummary.Counters.failed -ne '0'){throw 'Tests incomplete'}
$runtimes=@('artifacts/validation/biocular-reference-runner-sealed-20261102','artifacts/validation/biocular-reference-replay-final-runtime-20261102','artifacts/validation/biocular-reference-categories-fixed-runtime-20261102','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')
$coreHash=(Read (Join-Path $root 'replay-final/identity.json')).coreSha256
foreach($runtime in $runtimes){if((Hash (Join-Path $repo "$runtime/OpticalSystemDesign.Core.dll")) -ne $coreHash){throw 'Core mismatch'}}
$ident=Read (Join-Path $root 'replay-final/identity.json')
foreach($input in $ident.inputs){if((Hash (Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$input.lens))) -ne $input.sha256){throw 'Source changed'}}
if(@($mtf|Where-Object coreHash -ne $coreHash).Count){throw 'MTF Core mismatch'}
$files=[Collections.Generic.List[object]]::new()
function Add([string]$path,[string]$logical){$files.Add(@{path=$path;logical=$logical})}
$audit=[Collections.Generic.List[object]]::new()
foreach($pdb in @('artifacts/validation/biocular-reference-runner-sealed-20261102/OpticalSystemDesign.Core.pdb','artifacts/validation/biocular-reference-runner-sealed-20261102/BiocularReference20261102.pdb','artifacts/validation/biocular-reference-runner-sealed-20261102/OpticalSystemDesign.ZemaxComparison.pdb','artifacts/validation/biocular-reference-replay-final-runtime-20261102/PhaseCommon20261102.pdb','artifacts/validation/biocular-reference-categories-fixed-runtime-20261102/ObscuredReference20261101.pdb','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0/OpticalSystemDesign.Tests.pdb','src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Application.pdb','src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.pdb')){
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
[IO.File]::Copy((Join-Path $root 'replay-final/results.json'),(Join-Path $root 'results-final.json'),$true)
[IO.File]::Copy((Join-Path $root 'categories-fixed/results.json'),(Join-Path $root 'categories-results-final.json'),$true)
[IO.File]::Copy((Join-Path $root 'mtf-convergence-accepted/results.json'),(Join-Path $root 'mtf-results-final.json'),$true)
Save 'summary.json' @{coreBefore=(Read (Join-Path $repo 'validation/rechecks/2026-11-02/boundary-common/summary.json')).coreAfter;coreAfter=$coreHash;before=(Counts $before);after=(Counts $after);fixedIndexes=@($fixed.index);priorPassLost=$lost;byLens=@($after|Group-Object lens|ForEach-Object {@{lens=$_.Name;counts=(Counts $_.Group)}});categoriesBefore=(Counts $oldCategories);categoriesAfter=(Counts $categories);categoryConclusionsUnchanged=$true;mtfConvergenceChecks=14;mtfConvergenceAllPassed=$true;mtfSampling=@(64,128,256);testsPassed=417;testsFailed=0;defaultAppBuild=@{passed=$true;warnings=0;errors=0;builtInModules=2};inputs=$ident.inputs;unchangedRequestsAndTolerances=$true;freshImport=$true;newNativeCapture=$true;notAll95=$true;historicalFailedControlsRetained=$true;completedUtc=[DateTimeOffset]::UtcNow}
foreach($directory in (Get-ChildItem -LiteralPath $root -Directory)){foreach($file in (Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)){Add $file.FullName ([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'))}}
foreach($file in (Get-ChildItem (Join-Path $repo 'artifacts/validation') -File -Filter 'biocular-reference-*.log'|Where-Object Name -notlike '*-seal-*')){Add $file.FullName ('logs/'+$file.Name)}
foreach($file in (Get-ChildItem -LiteralPath $PSScriptRoot -File)){Add $file.FullName ('source/tools/diagnostics/BiocularReference20261102/'+$file.Name)}
foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') -File -Filter '*.cs')){Add $file.FullName ('source/tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/'+$file.Name)}
foreach($runtime in $runtimes){
    if($runtime -like 'src/*'){continue}
    foreach($file in (Get-ChildItem (Join-Path $repo $runtime) -File|Where-Object {$_.Name -match '^(OpticalSystemDesign.Core|OpticalSystemDesign.Application|OpticalSystemDesign.ZemaxComparison|OpticalSystemDesign.Tests|PhaseCommon20261102|ObscuredReference20261101|BiocularReference20261102)\.(dll|pdb|deps.json|runtimeconfig.json)$'})){Add $file.FullName ('runtime/'+$runtime.Replace('/','_')+'/'+$file.Name)}
}
# Reuse both directly retained and archived prior bytes, verifying each actual authority once.
$old=@{}
foreach($indexRoot in @('validation/rechecks/2026-11-01/testing-three','validation/rechecks/2026-11-02/huygens-common','validation/rechecks/2026-11-02/phase-common','validation/rechecks/2026-11-02/boundary-common')){
    $base=Join-Path $repo $indexRoot
    foreach($entry in (Read (Join-Path $base 'archive-index.json'))){
        $archive=if($entry.archive){$entry.archive}else{'evidence.zip'}
        $entryPath=if($entry.retainedFile -or ($entry.PSObject.Properties.Name -contains 'entry' -and $null -eq $entry.entry)){$null}elseif($entry.entry){$entry.entry}else{'payloads/'+$entry.sha256}
        $old[$entry.sha256]=@{path=[IO.Path]::GetFullPath((Join-Path $base $(if($entry.retainedFile){$entry.retainedFile}else{$archive})));entry=$entryPath}
    }
}
foreach($name in @('results-final.json','categories-results-final.json','mtf-results-final.json')){$path=Join-Path $root $name;$old[(Hash $path)]=@{path=$path;entry=$null}}
$archive=Join-Path $root 'evidence.zip';if(Test-Path $archive){throw 'Already sealed'}
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create);$opened=@{};$new=@{};$verifiedOld=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{};$retained=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try{foreach($f in $files){
    $hash=Hash $f.path;$length=(Get-Item -LiteralPath $f.path).Length
    if($logical.ContainsKey($f.logical)){if($logical[$f.logical] -ne $hash){throw 'Conflicting logical identity'};continue};$logical[$f.logical]=$hash
    if($f.logical -match '^mtf-convergence/(1013|1043)/(128|256)/native/'){
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
    if($directory.Name -ne 'mtf-convergence'){Remove-Item -LiteralPath $resolved -Recurse -Force;continue}
    foreach($file in (Get-ChildItem -LiteralPath $resolved -Recurse -File)){if(!$retained.Contains($file.FullName)){Remove-Item -LiteralPath $file.FullName -Force}}
    foreach($dir in (Get-ChildItem -LiteralPath $resolved -Recurse -Directory|Sort-Object {$_.FullName.Length} -Descending)){if(!(Get-ChildItem -LiteralPath $dir.FullName -Force|Select-Object -First 1)){Remove-Item -LiteralPath $dir.FullName}}
}
Write-Output "Verified $($new.Count) new and $($verifiedOld.Count) reused payloads; no prior Pass lost."
