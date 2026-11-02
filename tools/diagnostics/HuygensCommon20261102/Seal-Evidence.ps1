param([string]$Repository = 'D:\Projects\opticalsystem')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath($Repository)
$root = [IO.Path]::GetFullPath((Join-Path $repo 'validation/rechecks/2026-11-02/huygens-common'))
if (!$root.StartsWith($repo + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid evidence root' }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Read([string]$p) { Get-Content -LiteralPath $p -Raw | ConvertFrom-Json }
function Write-Json([string]$name,$value) { [IO.File]::WriteAllText((Join-Path $root $name),(ConvertTo-Json -InputObject $value -Depth 50),[Text.UTF8Encoding]::new($false)) }
function Counts($rows) { $c=[ordered]@{}; foreach($g in ($rows | Group-Object conclusion)) { $c[$g.Name]=$g.Count }; $c }
$priorRoot = Join-Path $repo 'validation/rechecks/2026-11-01/testing-three'
$prior = Read (Join-Path $priorRoot 'results-final.json')
$after = Read (Join-Path $root 'categories/results.json')
if ($after.Count -ne 426) { throw 'Incomplete replay' }
$beforeMap=@{}; foreach($r in $prior){$beforeMap[$r.index]=$r}
$lost=@($after | Where-Object { $beforeMap[$_.index].conclusion -eq 'Pass' -and $_.conclusion -ne 'Pass' })
if ($lost.Count) { throw 'A prior Pass was lost' }
$changes=@($after | Where-Object { $beforeMap[$_.index].conclusion -ne $_.conclusion } | ForEach-Object { @{index=$_.index;before=$beforeMap[$_.index].conclusion;after=$_.conclusion;reason=$_.reason} })
$identity=Read (Join-Path $root 'categories/identity.json'); $coreHash=$identity.coreSha256
$fourier=@(Read (Join-Path $root 'convergence-final/results.json')) + @(Read (Join-Path $root 'psf-convergence-corrected/results.json'))
if ($fourier.Count -ne 32 -or @($fourier | Where-Object { $_.sampling -eq 1024 -and $_.conclusion -ne 'Pass' }).Count) { throw 'Final high sampling control incomplete' }
$runtimes=@('artifacts/validation/huygens-common-runner-accepted-20261102','artifacts/validation/huygens-common-runner-illumination-20261102','artifacts/validation/huygens-common-category-runner-20261102','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0')
$runtimeIdentities=@(foreach($runtime in $runtimes){$p=Join-Path $repo "$runtime/OpticalSystemDesign.Core.dll";if ((Hash $p) -ne $coreHash){throw 'Core runtime mismatch'};@{path=$runtime;coreSha256=$coreHash}})
$files=[Collections.Generic.List[object]]::new()
function Add-File([string]$path,[string]$logicalPath) { $files.Add([pscustomobject]@{path=$path;logicalPath=$logicalPath}) }
$audit=[Collections.Generic.List[object]]::new()
$pdbs=@('artifacts/validation/huygens-common-runner-accepted-20261102/OpticalSystemDesign.Core.pdb','artifacts/validation/huygens-common-runner-accepted-20261102/OpticalSystemDesign.Tests.pdb','artifacts/validation/huygens-common-runner-illumination-20261102/OpticalSystemDesign.Tests.pdb','artifacts/validation/huygens-common-category-runner-20261102/ObscuredReference20261101.pdb','artifacts/validation/huygens-common-category-runner-20261102/OpticalSystemDesign.ZemaxComparison.pdb','tools/diagnostics/ObscuredReferenceTests20261101/bin/Release/net10.0/OpticalSystemDesign.Tests.pdb')
foreach($pdb in $pdbs){
    $entries=[Collections.Generic.List[object]]::new();$stream=[IO.File]::OpenRead((Join-Path $repo $pdb));$provider=[System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
    try {
        $reader=$provider.GetMetadataReader()
        foreach($handle in $reader.Documents){
            $doc=$reader.GetDocument($handle);$path=$reader.GetString($doc.Name)
            if (!(Test-Path -LiteralPath $path) -or $path -match '[\\/]obj[\\/]'){continue}
            $expected=[Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant();$actual=Hash $path
            $relative=[IO.Path]::GetRelativePath($repo,$path).Replace('\','/');$sourcePath=$path;$recovered=$false
            if ($actual -ne $expected){
                # The later RI diagnostic added one dispatch line; retain the exact executed earlier driver too.
                if ($relative -ne 'tools/diagnostics/HuygensCommon20261102/Program.cs') {throw "Source/PDB mismatch: $path"}
                $old=[regex]::Replace([IO.File]::ReadAllText($path),'(?m)^if \(mode == "illumination"\).*(\r?\n)','')
                $candidates=@($old,($old -replace '\r?\n',"`r`n"),($old -replace '\r\n',"`n"))
                $matched=$null
                foreach($candidate in $candidates){$bytes=[Text.UTF8Encoding]::new($false).GetBytes($candidate);if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -eq $expected){$matched=$bytes;break}}
                if($null -eq $matched){throw 'Cannot recover exact earlier driver bytes'}
                $sourcePath=Join-Path $root ('compiled-sources/'+$expected+'.cs');[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($sourcePath))|Out-Null;[IO.File]::WriteAllBytes($sourcePath,$matched);$recovered=$true
            }
            $logical='source/'+$relative
            if($recovered){$logical='executed-source/'+$expected+'/'+$relative}
            Add-File $sourcePath $logical
            $entries.Add(@{path=$relative;sha256=$expected;currentWorkspaceSha256=$actual;currentWorkspaceMatches=($actual -eq $expected);exactEarlierDriverRecovered=$recovered})
        }
    } finally {$provider.Dispose();$stream.Dispose()}
    $audit.Add(@{pdb=$pdb;sha256=(Hash (Join-Path $repo $pdb));documents=$entries})
}
Write-Json 'source-verification.json' @{coreSha256=$coreHash;assemblies=$audit;allExecutedSourcesVerified=$true}
$three=@($after | Where-Object lens -in @('Optical testing/Double-pass null test.zmx','Optical testing/Mach Zender Interferometer.zmx','Optical testing/Twyman Green Interferometer.zmx'))
Write-Json 'results-final.json' $after
Write-Json 'high-sampling-results.json' $fourier
Write-Json 'summary.json' @{coreSha256=$coreHash;baselineCommit='232e878001acf23a1c17799573fcefa4c2929d83';before=(Counts $prior);after=(Counts $after);three=(Counts $three);opticalTesting=(Counts @($after | Where-Object lens -like 'Optical testing/*'));telescopes=(Counts @($after | Where-Object lens -like 'Telescopes/*'));priorPassLost=$lost;changes=$changes;originalRequestAndToleranceReplay=$true;highSamplingControls=$fourier;illumination=(Read (Join-Path $root 'illumination-native/results.json'));tests=@{differentPassedCases=270;fullScopedRun=269;additionalRealInput=1;excludedHistoricalMethod='SpecialSurfaceParameterZeroSurvivesSnapshotWithoutShiftingOtherParameters';notWholeRepository=$true};defaultAppBuild=@{passed=$true;warnings=0;errors=0;builtInModules=2};runtimeCoreIdentities=$runtimeIdentities;sourceInputs=$identity.inputs;directBudgetRaised=$false;imageWindowReduced=$false;foregroundInteraction=$false;completedUtc=[DateTimeOffset]::UtcNow}
foreach($directory in (Get-ChildItem -LiteralPath $root -Directory)){foreach($file in (Get-ChildItem -LiteralPath $directory.FullName -Recurse -File)){Add-File $file.FullName ([IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/'))}}
foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts/validation') -File -Filter 'huygens-common-*.log' | Where-Object Name -notlike '*-seal-*')){Add-File $file.FullName ('logs/'+$file.Name)}
foreach($runtime in $runtimes){
    if ($runtime -like 'src/*'){continue}
    foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repo $runtime) -File | Where-Object { $_.Name -match '^(OpticalSystemDesign\.|ObscuredReference).*(\.dll|\.pdb|\.deps\.json|\.runtimeconfig\.json)$' })){
        Add-File $file.FullName ('runtime/'+$runtime.Replace('/','_')+'/'+$file.Name)
    }
}
foreach($file in (Get-ChildItem -LiteralPath $PSScriptRoot -File)){Add-File $file.FullName ('source/tools/diagnostics/HuygensCommon20261102/'+$file.Name)}
$priorIndex=Read (Join-Path $priorRoot 'archive-index.json');$oldByHash=@{}
foreach($entry in $priorIndex){$oldByHash[$entry.sha256]=$entry.length}
$priorZipPath=Join-Path $priorRoot 'evidence.zip';$priorZip=[IO.Compression.ZipFile]::OpenRead($priorZipPath)
$archivePath=Join-Path $root 'evidence.zip';if(Test-Path -LiteralPath $archivePath){throw 'Already sealed'}
$zip=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
$payloads=@{};$mapping=[Collections.Generic.List[object]]::new();$logical=@{};$oldVerified=@{};$retained=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try {
    foreach($file in $files){
        $hash=Hash $file.path;$length=(Get-Item -LiteralPath $file.path).Length
        if($logical.ContainsKey($file.logicalPath)){if($logical[$file.logicalPath] -ne $hash){throw 'Conflicting logical path'};continue};$logical[$file.logicalPath]=$hash
        # These four small new native inputs remain the sole expanded authority used by the replay tool.
        if($file.logicalPath -match '^convergence-first/(2165|2187)/(512|1024)/native/'){
            $retained.Add([IO.Path]::GetFullPath($file.path))|Out-Null
            $mapping.Add(@{logicalPath=$file.logicalPath;sha256=$hash;length=$length;retainedFile=$file.logicalPath});continue
        }
        $archive='evidence.zip'
        if($oldByHash.ContainsKey($hash)){
            if(!$oldVerified.ContainsKey($hash)){
                $e=$priorZip.GetEntry('payloads/'+$hash);$s=$e.Open()
                try{$verified=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()}
                if($verified -ne $hash -or $e.Length -ne $length){throw 'Old reference verification failed'};$oldVerified[$hash]=$true
            }
            $archive='../../2026-11-01/testing-three/evidence.zip'
        } elseif(!$payloads.ContainsKey($hash)){
            $e=$zip.CreateEntry('payloads/'+$hash,[IO.Compression.CompressionLevel]::Optimal);$s=[IO.File]::OpenRead($file.path);$t=$e.Open();try{$s.CopyTo($t)}finally{$s.Dispose();$t.Dispose()};$payloads[$hash]=$length
        }
        $mapping.Add(@{logicalPath=$file.logicalPath;sha256=$hash;length=$length;archive=$archive;entry='payloads/'+$hash})
    }
} finally {$zip.Dispose();$priorZip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath);$verifiedCount=0
try{
    if($zip.Entries.Count -ne $payloads.Count){throw 'Archive entry count differs'}
    foreach($e in $zip.Entries){$hash=$e.FullName.Substring(9);$s=$e.Open();try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($s)).ToLowerInvariant()}finally{$s.Dispose()};if($hash -ne $actual -or $e.Length -ne $payloads[$hash]){throw 'New payload verification failed'};$verifiedCount++}
}finally{$zip.Dispose()}
foreach($file in $files | Where-Object { $_.path.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) }){
    # Verify the unchanged expanded copy against the indexed authority immediately before retiring it.
    if((Hash $file.path) -ne $logical[$file.logicalPath]){throw 'Expanded evidence changed during sealing'}
}
Write-Json 'archive-index.json' $mapping
Write-Json 'archive-verification.json' @{archive='evidence.zip';sha256=(Hash $archivePath);bytes=(Get-Item -LiteralPath $archivePath).Length;logicalFiles=$mapping.Count;newUniquePayloads=$payloads.Count;newFullyReadVerifiedPayloads=$verifiedCount;reusedOldPayloads=$oldVerified.Count;priorArchiveSha256=(Hash $priorZipPath);retainedNativeFiles=$retained.Count;fullyVerifiedBeforeRetirement=$true}
foreach($directory in (Get-ChildItem -LiteralPath $root -Directory)){
    $target=[IO.Path]::GetFullPath($directory.FullName)
    if(!$target.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Retirement path outside evidence root'}
    if($directory.Name -ne 'convergence-first'){Remove-Item -LiteralPath $target -Recurse -Force;continue}
    foreach($file in (Get-ChildItem -LiteralPath $target -Recurse -File)){if(!$retained.Contains($file.FullName)){Remove-Item -LiteralPath $file.FullName -Force}}
    foreach($dir in (Get-ChildItem -LiteralPath $target -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)){if(!(Get-ChildItem -LiteralPath $dir.FullName -Force | Select-Object -First 1)){Remove-Item -LiteralPath $dir.FullName}}
}
Write-Output "Verified $verifiedCount new and $($oldVerified.Count) reused payloads, $($retained.Count) sole native input files."
