param([string]$Root='artifacts/validation/afocal-20261024/current-source',
      [string]$Inputs='artifacts/validation/afocal-20261024')
$ErrorActionPreference='Stop'
function ReadJson([string]$p){Get-Content -LiteralPath $p -Raw|ConvertFrom-Json -Depth 100}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([string]$p,$v){ConvertTo-Json -InputObject $v -Depth 100|Set-Content -LiteralPath $p -Encoding utf8}
function Require([bool]$condition,[string]$reason){if(!$condition){throw $reason}}
$repo=(Get-Location).Path
$fixtures='validation/zemax/2026-r1/afocal-repair-2026-10-24/captures'
$ledgerPath='docs/validation/AFOCAL_REPAIR_2026-10-24.json'
$archivePath='validation/rechecks/2026-10-24/afocal-repair-evidence.zip'
Require (!(Test-Path -LiteralPath $archivePath)) 'A fresh archive is required'
Require (!(Test-Path -LiteralPath $ledgerPath)) 'A fresh published acceptance ledger is required'

$core=Hash "$Root/runners/Afocal20261024/OpticalSystemDesign.Core.dll"
$binaryPaths=@(
    "$Root/runners/Objectives20261020/OpticalSystemDesign.Core.dll",
    "$Root/runners/SequentialCorpus20261019/OpticalSystemDesign.Core.dll",
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll')
$binaries=@(foreach($path in $binaryPaths){$h=Hash $path;Require ($h -ceq $core) "Core binary differs: $path";@{path=$path;sha256=$h}})
$sourceIntegrity=@(foreach($row in (ReadJson "$Root/core-source-before.json")){
    $actual=Hash $row.path;Require ($actual -ceq $row.sha256) "Core source changed during final acceptance: $($row.path)"
    @{path=$row.path;sha256=$actual;unchanged=$true}
})
WriteJson "$Root/core-source-after.json" $sourceIntegrity
foreach($configuration in 'Debug','Release'){
    $log=Get-Content -LiteralPath "$Root/build-default-$configuration-complete.log" -Raw
    Require ($log -match '已成功生成' -and $log -match '0 个警告' -and $log -match '0 个错误') "Default $configuration build incomplete"
}
$policyPath='tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
$policyHash=Hash $policyPath
Require ($policyHash -ceq '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5') 'Original policy changed'
$policy=ReadJson $policyPath;$scoped=ReadJson "$Inputs/scoped-policy.json"
foreach($analysis in $policy.analyses.PSObject.Properties){
    $expected=ConvertTo-Json -InputObject $analysis.Value.quantities -Depth 20 -Compress
    $actual=ConvertTo-Json -InputObject $scoped.analyses.($analysis.Name).quantities -Depth 20 -Compress
    Require ($actual -ceq $expected) "Focal quantity table changed: $($analysis.Name)"
}

$matrix=ReadJson "$Root/final-matrix/analysis-results.json"
$cutoff=ReadJson "$Root/final-cutoff/analysis-results.json"
foreach($kind in 'matrix','cutoff'){
    $summary=ReadJson "$Root/final-$kind/analysis-summary.json"
    Require ($summary.total -eq 150 -and $summary.counts.Pass -eq 30 -and $summary.counts.Incomparable -eq 120) "Unexpected $kind conclusions"
    Require (!$summary.counts.Difference -and !$summary.counts.Error -and !$summary.counts.Close) "Unresolved $kind comparison"
    $id=ReadJson "$Root/final-$kind/identity.json"
    Require ($id.assemblies[0].sha256 -ceq $core) "Stale $kind software identity"
    $reuse=ReadJson "$Root/final-$kind/native-reuse.json"
    Require ($reuse.count -eq 50 -and $reuse.pending -eq 0) "Incomplete $kind native reuse"
}
$rays=ReadJson "$Root/final-ray-audit/summary.json"
Require ($rays.Count -eq 15 -and @($rays|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Ray group failure'
foreach($row in $rays){Require (($row.coordinateFailures+$row.directionFailures+$row.acceptanceDifferences+$row.firstClipDifferences) -eq 0) 'Ray mask/point failure'}
$rayInputs=($rays.inputs|Measure-Object -Sum).Sum
$rayComparisons=($rays.physicalSurfaceComparisons|Measure-Object -Sum).Sum
Require ($rayInputs -eq 3240 -and $rayComparisons -eq 44496) 'Ray coverage changed'
$focal=ReadJson "$Root/final-focal-guards/summary.json"
Require ($focal.Count -eq 1159 -and @($focal|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Original focal regression'
foreach($row in $focal){
    Require ($row.requestExactlyEqual -and $row.referenceUnchanged -and !$row.error) 'Original focal request/reference mismatch'
    Require ((Hash $row.referencePath) -ceq $row.referenceHash) 'Focal reference changed after replay'
    $source='src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$row.key.Split('|')[0]
    Require ((Hash $source) -ceq $row.sourceSha256) 'Focal source changed after replay'
}
$focalId=ReadJson "$Root/final-focal-guards/identity.json"
Require ($focalId.coreSha256 -ceq $core -and $focalId.policyHash -ceq $policyHash) 'Focal replay identity mismatch'
$suppA=ReadJson "$Root/final-focal-supplemental-analyses/summary.json"
$suppP=ReadJson "$Root/final-focal-supplemental-points/summary.json"
Require ($suppA.Count -eq 22 -and $suppP.Count -eq 30) 'Supplemental coverage changed'
Require (@(($suppA+$suppP)|Where-Object conclusion -ne 'Pass').Count -eq 0) 'Supplemental focal regression'
foreach($name in 'analyses','points'){
    $id=ReadJson "$Root/final-focal-supplemental-$name/identity.json"
    Require ($id.coreSha256 -ceq $core -and $id.policySha256 -ceq $policyHash) 'Supplemental software/policy mismatch'
}
$previous=ReadJson 'validation/zemax/2026-r1/objectives-completion-2026-10-23/comparison-ledger.json'
Require ($previous.Count -eq 52) 'Previous supplemental ledger changed'
$suppIntegrity=@(foreach($row in $previous){
    $source='src/OpticalSystemDesign.App/Assets/SequentialExamples/'+$row.originalKey.Split('|')[0]
    Require ((Hash $source) -ceq $row.sourceSha256) 'Supplemental source changed'
    Require ((Hash $row.requestPath) -ceq $row.requestSha256) 'Supplemental frozen request changed'
    Require ((Hash $row.referencePath) -ceq $row.referenceSha256) 'Supplemental frozen reference changed'
    @{key=$row.originalKey;sourceSha256=$row.sourceSha256;requestSha256=$row.requestSha256;referenceSha256=$row.referenceSha256;unchanged=$true}
})
WriteJson "$Root/supplemental-integrity-after.json" $suppIntegrity

$tests=@(foreach($kind in 'core','comparison'){
    [xml]$trx=Get-Content -LiteralPath "$Root/tests/afocal-$kind-complete.trx" -Raw
    $c=$trx.TestRun.ResultSummary.Counters;$expected=if($kind -eq 'core'){71}else{50}
    Require ([int]$c.total -eq $expected -and [int]$c.passed -eq $expected -and [int]$c.failed -eq 0 -and [int]$c.notExecuted -eq 0) "Focused $kind tests did not pass"
    @{kind=$kind;passed=$expected;failed=0;skipped=0;trx="$Root/tests/afocal-$kind-complete.trx";sha256=Hash "$Root/tests/afocal-$kind-complete.trx"}
})
$opd=ReadJson "$fixtures/opd-manifest.json";$opdCount=0
Require ($opd.Count -eq 13) 'Explicit OPD coverage changed'
foreach($row in $opd){
    $path="$fixtures/opd/$($row.id)/point-reference.json"
    Require ((Hash $path) -ceq $row.sha256) 'Explicit OPD reference changed'
    $point=ReadJson $path;$opdCount+=$point.inputs.Count
    Require ($point.inputs.Count -eq 13) 'OPD point count changed'
    foreach($ray in $point.surfaces[-1].rays){Require ($ray.error -eq 0 -and $ray.vignette -eq 0) 'Invalid native OPD point'}
}
Require ($opdCount -eq 169) 'OPD coverage changed'
foreach($row in (ReadJson "$fixtures/manifest.json")+(ReadJson "$fixtures/fullband-manifest.json")){
    Require ((Hash "$fixtures/$($row.id)/data.json") -ceq $row.nativeSha256) 'Portable native array changed'
}
$native=@(foreach($folder in "$Inputs/matrix-corrected-contract","$Inputs/matrix-corrected-cutoff/native-pending"){
    foreach($job in (ReadJson "$folder/native-jobs.json")){
        $done=ReadJson "$($job.output)/batch-result.json";$envData=ReadJson "$($job.output)/environment.json"
        Require ($done.exitCode -eq 0 -and $envData.validLicense -and $envData.major -eq 26 -and $envData.minor -eq 1) 'Unsuccessful/unlicensed new native capture'
        @{id=$job.id;output=$job.output;completedUtc=$done.completedUtc;opticStudioVersion=$envData.opticStudioVersion;license=$envData.licenseStatus}
    }
})
Require ($native.Count -eq 55) 'Expected 50 corrected requests plus five physical-band MTF captures'
$sourceFiles=@(foreach($file in Get-ChildItem -LiteralPath "$Inputs/selected-sources" -Recurse -File){
    $relative=[IO.Path]::GetRelativePath((Resolve-Path "$Inputs/selected-sources").Path,$file.FullName).Replace('\','/')
    $h=Hash $file.FullName
    foreach($path in "artifacts/zemax-sequential-corpus/20261021/full-v1/sources/$relative","src/OpticalSystemDesign.App/Assets/SequentialExamples/$relative","$Root/final-matrix/sources/$relative"){
        Require ((Hash $path) -ceq $h) 'Afocal original source bytes changed'
    }
    @{path=$relative;sha256=$h;originalAndProductAndRunExact=$true}
})
Require ($sourceFiles.Count -eq 4) 'Four original sources required'
$pobs=@(foreach($name in 'PORRO_PRISM-LH.POB','PORRO_PRISM-RH.POB'){
    $nativePath=Join-Path ([Environment]::GetFolderPath('MyDocuments')) "Zemax/Objects/Polygon Objects/$name"
    $h=Hash $nativePath
    foreach($path in "$Inputs/$name","$fixtures/$name","src/OpticalSystemDesign.App/Assets/SequentialExamples/Objects/Polygon Objects/$name",
        "src/OpticalSystemDesign.App/bin/Debug/net10.0/Examples/Sequential/Objects/Polygon Objects/$name",
        "src/OpticalSystemDesign.App/bin/Release/net10.0/Examples/Sequential/Objects/Polygon Objects/$name"){
        Require ((Hash $path) -ceq $h) "POB deployment mismatch: $path"
    }
    @{name=$name;source=$nativePath;sha256=$h;productAndDefaultBuildOutputsExact=$true}
})
& tools/validation/GitFrozenIntegrity.ps1 -RepositoryRoot . -ManifestPath artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json -ExpectedAggregateSha256 3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10 -RelocationMapPath tools/validation/frozen-evidence-relocations.json -ExpectedRelocationMapSha256 6eefb25cbb1c35a120f73e8bbb761aac16759e7b8491b4606475caec8dfd4f6e -OutputPath "$Root/frozen-integrity.json" *> "$Root/frozen-integrity.log"
Require ($LASTEXITCODE -eq 0) 'Frozen integrity checker failed'
$frozen=ReadJson "$Root/frozen-integrity.json"
Require ($frozen.count -eq 2668 -and $frozen.matched -eq 2668 -and $frozen.unchanged) 'Frozen bytes changed'

$curvature=@($matrix|Where-Object {$_.run.key -eq 'Field Curvature and Distortion'}|ForEach-Object {
    @{lens=$_.lens;configuration=$_.configuration;conclusion=$_.run.conclusion;request=$_.run.request;metrics=$_.run.metrics}
})
$mtf=@($cutoff|Where-Object {$_.run.key -eq 'MTF'}|ForEach-Object {
    @{lens=$_.lens;configuration=$_.configuration;conclusion=$_.run.conclusion;maximumFrequency=$_.run.request.maximumFrequency;metrics=$_.run.metrics}
})
Require ($curvature.Count -eq 5 -and $mtf.Count -eq 5) 'Five configuration controls required'
$acceptance=[ordered]@{
    date='2026-10-24';status='Completed within declared afocal repair scope';generatedUtc=[DateTime]::UtcNow.ToString('o')
    sourceCommit=(& git rev-parse HEAD).Trim();coreSha256=$core;coreSourceCount=$sourceIntegrity.Count;coreSourcesUnchangedDuringAcceptance=$true;binaries=$binaries
    focal=@{originalRequests=1159;passed=1159;regressions=0;supplementalAnalyses=22;supplementalFanAndOpd=30;supplementalPassed=52;requestsReferencesSourcesAndPolicyUnchanged=$true;policySha256=$policyHash;summary="$Root/final-focal-guards/summary.json"}
    afocal=@{files=$sourceFiles;configurations=5;matrix=@{total=150;Pass=30;Incomparable=120;Difference=0;Error=0};newNativeCaptures=$native;rayGroups=15;rayInputs=$rayInputs;physicalSurfaceComparisons=$rayComparisons;coordinateFailures=0;directionFailures=0;maskDifferences=0;maxCoordinateMillimeters=($rays.coordMax|Measure-Object -Maximum).Maximum;maxDirectionCosine=($rays.dirMax|Measure-Object -Maximum).Maximum;opdGroups=13;opdPoints=$opdCount;fullbandMtf=$mtf;curvatureAndDistortion=$curvature;pobs=$pobs}
    curvatureContract=@{afocalPupilDelta=1e-4;rayPair='Symmetric';nativeOperandValues=20;nativeOperandPointGateDiopters=1e-6;originalOneSidedRequestsPreserved=$true;inferenceScope='Measured OpticStudio 2026 R1 and the five captured configurations, not a universal published private algorithm';focalSamplingUnchanged=$true;customSamplingStillHonored=$true}
    tests=$tests;distinctFocusedTests=121;defaultDebugBuild='Succeeded, zero warnings/errors';defaultReleaseBuild='Succeeded, zero warnings/errors';frozen=@{count=2668;unchanged=$true;aggregateSha256=$frozen.actualAggregateSha256}
    evidenceRoot=$Root;report='docs/AFOCAL_REPAIR_2026-10-24.md'
    limitations=@('120 catalog entries still lack a verified afocal comparison contract and are not counted as passes.','The separate 55 cycles/mrad exploration retains two Binocular Close results; physical cutoff controls do not overwrite it.','Bounded single-volume MM polygon groups do not certify internal scene paths, coherent polarization, lossless ZMX export, arbitrary NSC scenes, or each internal face against native data.','No full 95-file rerun, new independent Cooke/Tessar certification, code push, or installer publication.')
}
WriteJson "$Root/acceptance-body.json" $acceptance

# Archive only scoped evidence, never SDK binaries or the large rederived focal PSF grids.
$payload=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
function AddFile([string]$p){
    $full=(Resolve-Path -LiteralPath $p).Path;$relative=[IO.Path]::GetRelativePath($repo,$full).Replace('\','/')
    Require (!$relative.StartsWith('../')) 'Archive input escapes repository'
    if(!$payload.ContainsKey($relative)){$payload.Add($relative,$full)}
}
function AddTree([string]$p,[scriptblock]$filter={param($f)$true}){
    foreach($file in Get-ChildItem -LiteralPath $p -Recurse -File){
        if($file.Extension -in @('.exe','.dll','.pdb') -or $file.Name -eq '.prepare.lock'){continue}
        if(& $filter $file){AddFile $file.FullName}
    }
}
foreach($name in 'final-matrix','final-cutoff','final-ray-audit','tests'){AddTree "$Root/$name"}
foreach($name in 'final-focal-guards','final-focal-supplemental-analyses','final-focal-supplemental-points'){
    AddTree "$Root/$name" {param($f)$f.Name -in @('summary.json','identity.json','comparison.json','ray-comparison.json','ray-failures.json','native-launch-comparison.json') -or ($f.Extension -eq '.json' -and $f.FullName -match '\\comparisons\\')}
}
foreach($file in Get-ChildItem -LiteralPath $Root -File){AddFile $file.FullName}
AddTree $fixtures
$busyHistoricalLogs=@()
foreach($file in Get-ChildItem -LiteralPath $Inputs -File|Where-Object {$_.Extension -in @('.log','.json','.txt','.POB')}){
    try{$null=Hash $file.FullName;AddFile $file.FullName}
    catch{
        if($file.Extension -ne '.log'){throw}
        $busyHistoricalLogs+=@{path=[IO.Path]::GetRelativePath($repo,$file.FullName).Replace('\','/');reason='Historical log still in use; excluded from this portable package, original retained in workspace';error=$_.Exception.Message}
    }
}
WriteJson "$Root/archive-exclusions.json" $busyHistoricalLogs
AddFile "$Root/archive-exclusions.json"
$acceptance.historicalLogArchiveExclusions=$busyHistoricalLogs
WriteJson "$Root/acceptance-body.json" $acceptance
foreach($name in 'matrix-verified','matrix-fullband'){
    foreach($file in 'analysis-results.json','analysis-summary.json','identity.json'){AddFile "$Inputs/$name/$file"}
}
foreach($name in 'native-triangle-control-v2','binocular-parabasal-v2','binocular-parabasal-v3'){AddTree "$Inputs/$name"}
foreach($file in Get-ChildItem -LiteralPath "$Inputs/facet-confirm-v1/00" -File){AddFile $file.FullName}
AddTree "$Inputs/focal-supplemental-manifest"
AddFile "$Inputs/matrix-stage3/host/source-identities.json"
AddFile "$Inputs/matrix-stage3/host/Host.cs"
AddFile "$Inputs/matrix-corrected-cutoff/native-pending/host/source-identities.json"
AddFile "$Inputs/matrix-corrected-cutoff/native-pending/host/Host.cs"
$owned=@(
    'src/OpticalSystemDesign.Core/Domain/ReferenceOpdMode.cs','src/OpticalSystemDesign.Core/Optic.cs',
    'src/OpticalSystemDesign.Core/Serialization/OpticSnapshot.cs','src/OpticalSystemDesign.Core/Serialization/ComponentSnapshot.cs',
    'src/OpticalSystemDesign.Core/FileIO/ZemaxZmxReader.cs','src/OpticalSystemDesign.Core/FileIO/ZemaxZmxReader.PolygonGroup.cs','src/OpticalSystemDesign.Core/FileIO/CommercialFormatIO.cs',
    'src/OpticalSystemDesign.Core/Geometries/PolygonGroupGeometry.cs','src/OpticalSystemDesign.Core/Domain/OpticalSurface.StateTracing.cs',
    'src/OpticalSystemDesign.Core/Capabilities/OpticCapabilityPreflight.cs','src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs',
    'src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs','src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs',
    'src/OpticalSystemDesign.Core/Services/FieldCurvatureMetrics.cs','src/OpticalSystemDesign.Core/Analysis/Fields/DistortionAnalyses.cs','src/OpticalSystemDesign.Core/Analysis/Shared/AnalysisTrace.cs',
    'src/OpticalSystemDesign.Core/Optimization/MeritFunction.cs','src/OpticalSystemDesign.Application/Services/MeritOperandReferenceCatalog.cs',
    'src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs','src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs','src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.Helpers.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/AfocalAnalysisContracts.cs','tools/OpticalSystemDesign.ZemaxComparison/ExtendedAnalysisContracts.cs','tools/OpticalSystemDesign.ZemaxComparison/ComparisonRunner.cs',
    'tools/OpticalSystemDesign.ZemaxComparison/Configuration/ComparisonConfiguration.cs','tools/OpticalSystemDesign.ZemaxComparison/Normalization/PhysicalNormalization.cs','tools/OpticalSystemDesign.ZemaxComparison/Normalization/ExtendedResultNormalizer.cs','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost/Host.cs',
    'tools/diagnostics/SequentialCorpus20261019/Program.cs','tests/OpticalSystemDesign.Tests/AfocalEvaluationContractTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalRepairNativeTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalComparisonTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj',
    'docs/AFOCAL_REPAIR_2026-10-24.md','docs/CURRENT_STATUS.md','docs/OPEN_ISSUES_2026-10-08.md','docs/FILE_FORMATS_AND_PLUGINS.md','docs/NUMERICAL_PARITY.md','docs/ZEMAX_OPERAND_SUPPORT.md','docs/README.md','validation/zemax/2026-r1/afocal-repair-2026-10-24/README.md')
& git diff --check -- @owned *> "$Root/diff-check.log"
Require ($LASTEXITCODE -eq 0) 'Scoped whitespace check failed'
AddFile "$Root/diff-check.log"
foreach($file in $owned){AddFile $file}
AddTree src/OpticalSystemDesign.Core {param($f)$f.FullName -notmatch '\\(bin|obj)\\'}
AddTree tools/diagnostics/Afocal20261024 {param($f)$f.FullName -notmatch '\\(bin|obj)\\'}
$records=@(foreach($entry in ($payload.GetEnumerator()|Sort-Object Key)){
    @{path=$entry.Key;sha256=Hash $entry.Value;bytes=(Get-Item -LiteralPath $entry.Value).Length}
})
Add-Type -AssemblyName System.IO.Compression
$manifestText=ConvertTo-Json -InputObject $records -Depth 8
$stream=[IO.File]::Open((Join-Path $repo $archivePath),[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($record in $records){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$payload[$record.path],$record.path,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
    $entry=$zip.CreateEntry('payload-sha256.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
    try{$writer.Write($manifestText)}finally{$writer.Dispose()}
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $repo $archivePath))
try{
    Require ($zip.Entries.Count -eq $records.Count+1) 'Unexpected archive entry count'
    foreach($record in $records){
        $entry=$zip.GetEntry($record.path);$s=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$actual=[Convert]::ToHexString($sha.ComputeHash($s)).ToLowerInvariant()}finally{$s.Dispose();$sha.Dispose()}
        Require ($actual -ceq $record.sha256 -and $entry.Length -eq $record.bytes) "Archive readback failed: $($record.path)"
    }
    $reader=[IO.StreamReader]::new($zip.GetEntry('payload-sha256.json').Open())
    try{Require ($reader.ReadToEnd() -ceq $manifestText) 'Archive payload manifest readback failed'}finally{$reader.Dispose()}
}finally{$zip.Dispose()}
$archive=@{path=$archivePath;sha256=Hash $archivePath;bytes=(Get-Item -LiteralPath $archivePath).Length;payloadFiles=$records.Count;readbackVerified=$true;manifestPath="$archivePath.manifest.json"}
WriteJson "$archivePath.manifest.json" @{archive=$archive;files=$records}
$acceptance.archive=$archive
WriteJson $ledgerPath $acceptance
Write-Output "Acceptance confirmed: 30 afocal comparisons, 1159+52 focal guards, 121 focused tests, 2668 frozen files; archive $($archive.bytes) bytes."
