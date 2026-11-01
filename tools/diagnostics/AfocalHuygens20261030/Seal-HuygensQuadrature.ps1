param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation';$destination=Join-Path $root 'validation/rechecks/2026-10-30/afocal-huygens'
function ReadJson([string]$p){Get-Content -Raw -LiteralPath $p|ConvertFrom-Json}
function Require([bool]$value,[string]$message){if(-not $value){throw $message}}
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()}
function Relative([string]$p){[IO.Path]::GetRelativePath($root,$p).Replace('\','/')}
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not(Test-Path -LiteralPath $archivePath)) 'Never overwrite sealed evidence.'
$dependencies=@(
    @{path='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip';sha256='235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1'},
    @{path='validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip';sha256='8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1'},
    @{path='validation/rechecks/2026-10-30/afocal-auto-grid/evidence.zip';sha256='b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f'},
    @{path='validation/rechecks/2026-10-30/riflescope-pupil/evidence.zip';sha256='f4e6fef82110d8b6bde01a6d85ea72dbf7613e480991751e79b1f5a423bace4a'},
    @{path='validation/rechecks/2026-10-30/centroid-energy/evidence.zip';sha256='a7c6cded1d3d98546a1aad0aa2bc42290d3fcd5f0c3688a8382f107e6d82d997'},
    @{path='validation/rechecks/2026-10-30/afocal-ri/evidence.zip';sha256='89aeb7953c4d03e2948a3db5b48e090fa9a1ee190e395c12ff029200524f471c'})
foreach($d in $dependencies){Require ((Hash (Join-Path $root $d.path)) -ceq $d.sha256) 'Frozen dependency changed.'}
$before=@(ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-before/summary.json'))
$after=@(ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-final/summary.json'))
Require ($before.Count -eq 3 -and $after.Count -eq 3) 'Wrong independent disk sampling scope.'
for($i=0;$i -lt 3;$i++){
    $a=$after[$i];$b=$before[$i]
    Require ($a.n -eq $b.n -and $a.coreAiryMaximumAbsolute -eq $b.coreAiryMaximumAbsolute -and $a.nativeAiryMaximumAbsolute -eq $b.nativeAiryMaximumAbsolute -and $a.nativeMaximumAbsoluteDifference -eq $b.nativeMaximumAbsoluteDifference) 'Native protocol changed.'
    Require ($a.gaussianAiryMaximumAbsolute -le 1e-10 -and $a.nativeMaximumAbsoluteDifference -le 1e-12 -and $a.PupilQuadratureNodeCount -eq $a.n*$a.n -and -not $a.gaussianModeCountedAsNativePass) 'Continuum or native protection failed.'
}
$wide=@(ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-final/wide-window.json'))
Require ($wide.Count -eq 4 -and -not $wide[0].meetsStrictContinuumGate -and -not $wide[1].meetsStrictContinuumGate -and $wide[2].meetsStrictContinuumGate -and $wide[3].meetsStrictContinuumGate) 'Under-sampling failures were hidden.'
$execution=@(ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-native-02/execution.json'))
Require ($execution.Count -eq 3 -and @($execution|Where-Object {$_.result.exitCode -ne 0 -or -not $_.sourceUnchanged}).Count -eq 0) 'Native diagnostic execution failed.'
$source=Join-Path $artifacts 'afocal-huygens-20261030-native/disk.ZMX'
Require ((Hash $source) -ceq '67d33177298a0d23398a882de2e68c7f4ddaa681185b6025698aaeb1c708ffe3') 'Independent disk source changed.'
foreach($n in 32,64,128){
    $folder=Join-Path $artifacts "afocal-huygens-20261030-native-02/disk-n$n"
    $settings=ReadJson (Join-Path $folder 'captured-settings.json');$model=ReadJson (Join-Path $folder 'model.json')
    Require ($model.aperture.AFocalImageSpace -and $settings.properties.PupilSampleSize -ceq "S_$($n)x$n" -and $settings.properties.ImageSampleSize -ceq 'S_32x32' -and $settings.properties.ImageDelta -eq .02 -and -not $settings.properties.Normalize -and -not $settings.properties.UsePolarization -and -not $settings.properties.UseCentroid -and $settings.selectors.Wavelength -eq 1 -and $settings.selectors.Field -eq 1) 'Native physical settings differ.'
    Require (-not $settings.request.formalAcceptanceRequest -and -not $settings.request.captureScreenshots -and $settings.request.inputSha256 -ceq (Hash $source)) 'Invalid native diagnostic scope.'
}
$oldIndex=ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-index-baseline.json')
$preFixIndex=ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-index-current.json')
$fixedIndex=ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-index-fixed.json')
Require ($oldIndex.coreSha256 -ceq '21d9a68915ecc34556e9c4f5d88f04e9f9b425a1fdeac6edf71b172c7458ff89') 'Wrong pre-task binary baseline.'
for($i=0;$i -lt 3;$i++){
    Require ($oldIndex.rows[$i].maximumDifference -eq $preFixIndex.rows[$i].maximumDifference) 'Preexisting index failure was not independently reproduced.'
    Require ($fixedIndex.rows[$i].maximumDifference -le 1e-7) 'Original index-scaling gate failed.'
}
$tests=@();$distinct=[Collections.Generic.HashSet[string]]::new()
foreach($p in 'afocal-huygens-20261030-tests-comparison-final-03/comparison.trx','afocal-huygens-20261030-tests-core-final-03/core.trx'){
    [xml]$doc=Get-Content -Raw -LiteralPath (Join-Path $artifacts $p);$c=$doc.TestRun.ResultSummary.Counters
    Require ($c.failed -eq 0 -and $c.notExecuted -eq 0 -and $c.total -eq $c.passed) 'Final tests failed or skipped.'
    $definitions=@{};foreach($t in $doc.TestRun.TestDefinitions.UnitTest){$definitions[[string]$t.id]=[string]$t.TestMethod.className}
    foreach($t in $doc.TestRun.Results.UnitTestResult){Require ($t.outcome -ceq 'Passed' -and $distinct.Add($definitions[[string]$t.testId]+':'+$t.testName)) 'Duplicate or nonpassing test identity.'}
    $tests+=[pscustomobject]@{path=('artifacts/validation/'+$p);sha256=(Hash (Join-Path $artifacts $p));passed=[int]$c.passed;failed=[int]$c.failed;skipped=[int]$c.notExecuted}
}
Require ($distinct.Count -eq 410 -and $tests[0].passed -eq 124 -and $tests[1].passed -eq 286) 'Wrong distinct scope.'
$copies=@();foreach($p in 'src/OpticalSystemDesign.Core/bin/Release/net10.0','src/OpticalSystemDesign.App/bin/Release/net10.0','tests/OpticalSystemDesign.Tests/bin/Release/net10.0','tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0','tools/OpticalSystemDesign.ZemaxComparison/bin/Release/net10.0','tools/diagnostics/AfocalHuygens20261030/bin/Release/net10.0'){
    $relativePath=$p+'/OpticalSystemDesign.Core.dll';$copies+=[pscustomobject]@{path=$relativePath;sha256=(Hash (Join-Path $root $relativePath))}
}
Require (@($copies.sha256|Sort-Object -Unique).Count -eq 1 -and $fixedIndex.coreSha256 -ceq $copies[0].sha256) 'Final shared Core identities differ.'
$identity=ReadJson (Join-Path $artifacts 'afocal-huygens-20261030-final/identity.json')
Require ($identity.coreSha256 -ceq $copies[0].sha256 -and $identity.sourceSha256 -ceq (Hash $source)) 'Study identity differs.'
foreach($p in 'afocal-huygens-20261030-app-build.log','afocal-huygens-20261030-tool-build.log'){
    $log=Get-Content -Raw -LiteralPath (Join-Path $artifacts $p);Require ($log -match '0 个警告' -and $log -match '0 个错误') 'Default build failed or warned.'
}
git -C $root -c core.fsmonitor=false diff --quiet -- tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json
Require ($LASTEXITCODE -eq 0) 'Comparison policy changed.'
$sources=@();$paths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@(git -C $root -c core.fsmonitor=false ls-files --others --exclude-standard -- src/OpticalSystemDesign.Core)+@('src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.cs','src/OpticalSystemDesign.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs')
foreach($p in $paths|Sort-Object -Unique){$sources+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}}
$assessment=[ordered]@{date='2026-10-30';status='explicit_gaussian_afocal_huygens_and_object_medium_pupil_distance_repaired';strictContinuumGate=1e-10;originalIndexScalingGate=1e-7;defaultNativeFiniteCellChanged=$false;nativeControlsCountedAsFormalPasses=0;allFiniteSamplingIssuesClosed=$false;nativeSamplingBiasRetainedByProtocol=$true;originalNativeInputsReferencesGatesAndHistoricalLedgerChanged=$false;dependencies=$dependencies;sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);before=$before;after=$after;wideWindowCounterexamples=$wide;nativeExecution=$execution;oldBinaryIndexScaling=$oldIndex;preFixIndexScaling=$preFixIndex;fixedIndexScaling=$fixedIndex;coreCopies=$copies;workingTreeSources=$sources;testRuns=$tests;latestDistinctTestsPassed=$distinct.Count;earlierRunsSupersededNotAdded=$true;foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json';$assessment|ConvertTo-Json -Depth 50|Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new();$extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.zmx','.md','.ps1','.csproj','.trx','.csv')
foreach($item in Get-ChildItem -LiteralPath $artifacts -Filter 'afocal-huygens-20261030*'){
    if($item.PSIsContainer -and $item.Name.EndsWith('-build')){continue}
    $candidates=if($item.PSIsContainer){@(Get-ChildItem -LiteralPath $item.FullName -Recurse -File)}else{@($item)}
    foreach($f in $candidates){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
}
foreach($folder in 'tools/diagnostics/AfocalHuygens20261030','tools/diagnostics/AfocalHuygensIndex20261030','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost'){
    foreach($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File){if($f.Extension.ToLowerInvariant() -in $extensions){$files.Add($f)}}
}
foreach($p in @('docs/AFOCAL_HUYGENS_QUADRATURE_2026-10-30.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalHuygensQuadratureTests.cs','tests/OpticalSystemDesign.Tests/OpticCapabilityPreflightTests.cs','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json')+$sources.path){$files.Add((Get-Item -LiteralPath (Join-Path $root $p)))}
$files.Add((Get-Item -LiteralPath $assessmentPath))
$entries=[Collections.Generic.List[object]]::new();$canonical=@{}
# Hash all source files before opening the archive, so an active log fails cleanly.
foreach($f in $files|Sort-Object FullName -Unique){$logical=Relative $f.FullName;Require (-not $logical.StartsWith('../')) 'Evidence escaped repository.';$sha=Hash $f.FullName;if(-not $canonical.ContainsKey($sha)){$canonical[$sha]=$logical};$entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})}
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($e in $entries|Where-Object {$_.path -ceq $_.member}){
        $entry=$zip.CreateEntry($e.member,[IO.Compression.CompressionLevel]::Optimal);$inputStream=[IO.File]::OpenRead((Join-Path $root $e.path));$outputStream=$entry.Open();try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}
    }
    $entry=$zip.CreateEntry('entry-map.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10))}finally{$writer.Dispose()}
}finally{$zip.Dispose();$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{foreach($e in $entries){$member=$zip.GetEntry($e.member);$inputStream=$member.Open();try{$sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream))}finally{$inputStream.Dispose()};Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence mismatch: '+$e.path)}}finally{$zip.Dispose()}
$seal=[ordered]@{date='2026-10-30';status=$assessment.status;latestDistinctTestsPassed=$distinct.Count;newQuadratureTestsPassed=30;strictContinuumGate=1e-10;nativeDiagnosticControls=3;nativeControlsCountedAsFormalPasses=0;allFiniteSamplingIssuesClosed=$false;defaultNativeProtocolChanged=$false;coreAssemblySha256=$copies[0].sha256;coreCopiesIdentical=6;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal|ConvertTo-Json -Depth 12
