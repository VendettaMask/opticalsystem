param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts = Join-Path $root 'artifacts/validation'
$destination = Join-Path $root 'validation/rechecks/2026-10-30/afocal-auto-grid'
function ReadJson([string]$p) { Get-Content -Raw -LiteralPath $p | ConvertFrom-Json }
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Relative([string]$p) { [IO.Path]::GetRelativePath($root,$p).Replace('\','/') }
$archivePath = Join-Path $destination 'evidence.zip'
Require (-not (Test-Path -LiteralPath $archivePath)) 'Never overwrite a sealed evidence archive.'
$sourceArchive = 'validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip'
$seamArchive = 'validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip'
Require ((Hash (Join-Path $root $sourceArchive)) -ceq '235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1') 'Frozen source archive changed.'
Require ((Hash (Join-Path $root $seamArchive)) -ceq '8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1') 'Prior seam evidence changed.'
$expectationPath = Join-Path $destination 'current-grid-expectations.json'
$expectations = ReadJson $expectationPath
$manifestPath = Join-Path $root $expectations.originalManifest
Require ((Hash $manifestPath) -ceq $expectations.originalManifestSha256) 'Original PSF contract manifest changed.'
$manifest = ReadJson $manifestPath
$before = @(ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-before/summary.json'))
$after = @(ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-final/summary.json'))
Require ($before.Count -eq 20 -and $after.Count -eq 20 -and $expectations.cases.Count -eq 20) 'Wrong request count.'
$newlyComparable = @(); $comparedValues = 0
for ($i=0; $i -lt 20; $i++) {
    $b=$before[$i]; $a=$after[$i]
    Require ($b.id -ceq $a.id -and $b.fingerprint -ceq $a.fingerprint -and $b.snapshotSha256 -ceq $a.snapshotSha256) 'Request or frozen model identity changed.'
    $expected = @($expectations.cases | Where-Object id -CEQ $a.id)
    $original = @($manifest.cases | Where-Object id -CEQ $a.id)
    Require ($expected.Count -eq 1 -and $original.Count -eq 1) 'Ambiguous expectation.'
    Require ($expected[0].requestFingerprint -ceq $a.fingerprint -and $expected[0].originalConclusion -ceq $original[0].originalConclusion) 'Expectation lost its original identity.'
    Require (-not $a.inferredDiameterIsProductInput) 'An inferred scale supplied a product input.'
    if ($a.failure) {
        Require ($expected[0].currentConclusion -ceq 'Incomparable' -and $a.id -match '^000[78]-001-afocal-riflescope-' -and $null -eq $a.metric -and $null -eq $a.violations) 'Unexpected remaining grid failure.'
    } else {
        Require ($a.xGateRatio -le 1 -and $a.yGateRatio -le 1 -and $a.violations -eq 0 -and $a.metric.conclusion -ceq 'Pass') 'A claimed grid closure fails the original gates.'
        Require ($expected[0].currentConclusion -ceq 'Pass' -and $expected[0].pointwiseRequired) 'Current test expectation does not require pointwise success.'
        $comparedValues += $a.metric.count
        if ($b.failure) { $newlyComparable += $a }
    }
}
Require (@($before | Where-Object failure).Count -eq 6 -and @($after | Where-Object failure).Count -eq 2) 'Wrong before/after incomparable count.'
Require (($newlyComparable.id.Substring(0,4) -join ',') -ceq '0059,0060,0089,0090') 'Wrong newly comparable request identities.'
Require (($newlyComparable.metric | Measure-Object count -Sum).Sum -eq 34816 -and $comparedValues -eq 233472) 'Wrong number of compared values.'
$profiles = @(ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-profiles/summary.json'))
$priorProfiles = @(ReadJson (Join-Path $root 'validation/rechecks/2026-10-30/fft-seam-repair/assessment.json')).profilesAfter
Require ($profiles.Count -eq 5 -and $priorProfiles.Count -eq 5) 'Wrong original profile count.'
for ($i=0; $i -lt 5; $i++) { Require ($profiles[$i].id -ceq $priorProfiles[$i].id -and $profiles[$i].fingerprint -ceq $priorProfiles[$i].fingerprint -and $profiles[$i].violations -eq 0) 'An original profile regressed or changed identity.' }
$testRuns=@(); $testIdentities=[Collections.Generic.HashSet[string]]::new()
foreach ($spec in @(@{file='automatic-grid-final.trx';count=108;assembly='ZemaxComparison'},@{file='automatic-grid-core.trx';count=25;assembly='Core'})) {
    $path=Join-Path $artifacts ('afocal-auto-grid-20261030-tests/'+$spec.file)
    [xml]$trx=Get-Content -Raw -LiteralPath $path
    $results=@($trx.TestRun.Results.UnitTestResult)
    Require ($results.Count -eq $spec.count -and @($results | Where-Object outcome -NE 'Passed').Count -eq 0) 'Tests are incomplete or failed.'
    foreach ($result in $results) { [void]$testIdentities.Add($spec.assembly+':'+$result.testName) }
    $testRuns += [pscustomobject]@{path=(Relative $path);passed=$results.Count;failed=0;skipped=0;sha256=(Hash $path)}
}
Require ($testIdentities.Count -eq 133) 'Wrong latest distinct test count.'
$copies=@()
foreach ($prefix in 'src/OpticalSystemDesign.Core','src/OpticalSystemDesign.App','tests/OpticalSystemDesign.Tests','tests/OpticalSystemDesign.ZemaxComparison.Tests','tools/OpticalSystemDesign.ZemaxComparison','tools/diagnostics/FftProfileNative20261029') {
    $p="$prefix/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    $copies += [pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}
}
Require (@($copies.sha256 | Select-Object -Unique).Count -eq 1) 'Core binary copies differ.'
$identity=ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-final/identity.json')
Require ($identity.coreSha256 -ceq $copies[0].sha256 -and -not $identity.originalRequestsAndGatesChanged) 'Final diagnostic used a different Core.'
git -C $root -c core.fsmonitor=false diff --exit-code -- tools/OpticalSystemDesign.ZemaxComparison/Metrics/ComparisonMetrics.cs tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json $expectations.originalManifest | Out-Null
Require ($LASTEXITCODE -eq 0) 'Original acceptance implementation or settings changed.'
$native=@()
foreach ($spec in @(@{folder='afocal-auto-grid-20261030-native-riflescope';exit=2;role='failed_metadata_missing_originalKeys'},@{folder='afocal-auto-grid-20261030-native-riflescope-02';exit=0;role='valid_diagnostic_not_optical_acceptance'})) {
    $run=@(ReadJson (Join-Path $artifacts ($spec.folder+'/execution.json')))
    Require ($run.Count -eq 1 -and $run[0].result.ExitCode -eq $spec.exit -and $run[0].sourceUnchanged -and -not $run[0].result.TimedOut) 'Native attempt identity differs.'
    $native += [pscustomobject]@{folder=$spec.folder;exitCode=$spec.exit;role=$spec.role;sourceUnchanged=$true;formalOpticalPass=$false}
}
$packet=ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-native-riflescope-02/riflescope-nine-parabasal-rays/point-reference.json')
$environment=ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-native-riflescope-02/riflescope-nine-parabasal-rays/environment.json')
Require ($environment.validLicense -and $environment.major -eq 26 -and $environment.minor -eq 1 -and $packet.inputs.Count -eq 9 -and $packet.surfaces.Count -eq 18 -and -not $packet.savedInput) 'Native nine-ray control is invalid.'
Require ($packet.inputSha256 -ceq '36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b') 'Native source identity changed.'
foreach ($s in $packet.surfaces) { Require ($s.rays.Count -eq 9 -and @($s.rays | Where-Object { $_.error -ne 0 -or $_.vignette -ne 0 }).Count -eq 0) 'Invalid native ray.' }
$projected=ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-native-check-projected.json')
Require ($projected.surfaces.Count -eq 18 -and $projected.surfaces[-1].rays[1].projectedImageReference) 'Image reference planes were not explicitly reconciled.'
$sources=@()
foreach ($p in @(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)) { $sources += [pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))} }
$assessment=[ordered]@{date='2026-10-30';status='four_binocular_automatic_grids_closed_two_riflescope_grids_open';allAfocalIssuesClosed=$false;originalIncomparableCount=6;remainingIncomparableCount=2;newlyComparableRequests=4;newlyComparedValues=34816;totalComparedValues=$comparedValues;pointwiseViolationsAmongComparableGrids=0;original335ProfileViolationsRemaining=0;originalPrescriptionSnapshotRequestReferenceSamplingAndGatesChanged=$false;sourceArchive=$sourceArchive;sourceArchiveSha256=(Hash (Join-Path $root $sourceArchive));priorSeamArchive=$seamArchive;priorSeamArchiveSha256=(Hash (Join-Path $root $seamArchive));sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);coreCopies=$copies;coreWorkingTreeSources=$sources;gridBefore=$before;gridAfter=$after;originalProfiles=$profiles;latestDistinctTestsPassed=133;testRuns=$testRuns;earlier106AndPrior82NotAdded=$true;nativeAttempts=$native;firstSurfacePupilPositionDifferenceMillimeters=$projected.surfaces[0].rays[1].positionError;imagePupilPositionDifferenceAtCommonPlaneMillimeters=$projected.surfaces[-1].rays[1].positionError;oldUnprojectedDiagnosticNotSamePlaneError=$true;nativeInternalRulesFullyKnown=$false;inferredDiameterUsedAtRuntime=$false;foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;otherOpenIssuesClosed=$false;productChangesCommittedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json'
$assessment | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new()
$extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj','.trx','.csv','.tsv')
foreach ($item in Get-ChildItem -LiteralPath $artifacts -Filter 'afocal-auto-grid-20261030*') {
    $candidates=if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File) } else { @($item) }
    foreach ($f in $candidates) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } }
}
foreach ($folder in 'tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') { foreach ($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } } }
foreach ($p in @('docs/AFOCAL_AUTOMATIC_GRID_REPAIR_2026-10-30.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPsfContractTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalDiffractionPupilFrameTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftProfilePointwiseTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj','tests/OpticalSystemDesign.Tests/PolygonPlanePrecisionTests.cs','tools/OpticalSystemDesign.ZemaxComparison/Metrics/ComparisonMetrics.cs','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json',$expectations.originalManifest) + $sources.path) { $files.Add((Get-Item -LiteralPath (Join-Path $root $p))) }
foreach ($p in $assessmentPath,$expectationPath) { $files.Add((Get-Item -LiteralPath $p)) }
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$canonical=@{}; $entries=[Collections.Generic.List[object]]::new()
try {
    foreach ($f in $files | Sort-Object FullName -Unique) {
        $logical=Relative $f.FullName
        Require (-not $logical.StartsWith('../')) 'Evidence path escaped the repository.'
        $sha=Hash $f.FullName
        if (-not $canonical.ContainsKey($sha)) {
            $canonical[$sha]=$logical; $entry=$zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal)
            $inputStream=[IO.File]::OpenRead($f.FullName); $outputStream=$entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
        $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})
    }
    $entry=$zip.CreateEntry('entry-map.json'); $writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
    try { $writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($e in $entries) {
        $member=$zip.GetEntry($e.member); $inputStream=$member.Open()
        try { $sha=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream)) } finally { $inputStream.Dispose() }
        Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence byte mismatch: '+$e.path)
    }
} finally { $zip.Dispose() }
$seal=[ordered]@{date='2026-10-30';status=$assessment.status;allAfocalIssuesClosed=$false;remainingIncomparableGrids=2;original335ProfileViolationsRemaining=0;latestDistinctTestsPassed=133;nativeValidDiagnosticControls=1;nativeFailedAttempts=1;nativeControlsCountedAsOpticalPasses=0;coreAssemblySha256=$copies[0].sha256;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal | ConvertTo-Json -Depth 10
