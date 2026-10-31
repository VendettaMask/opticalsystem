param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts = Join-Path $root 'artifacts/validation'
$destination = Join-Path $root 'validation/rechecks/2026-10-30/fft-seam-repair'
function ReadJson([string]$p) { Get-Content -Raw -LiteralPath $p | ConvertFrom-Json }
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
$sourceArchive = 'validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip'
Require ((Hash (Join-Path $root $sourceArchive)) -eq '235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1') 'Frozen acceptance archive changed.'
$before = @(ReadJson (Join-Path $artifacts 'fft-seam-baseline-20261030/summary.json'))
$after = @(ReadJson (Join-Path $artifacts 'fft-seam-final-20261030/summary.json'))
Require ($before.Count -eq 5 -and $after.Count -eq 5 -and $before[3].violations -eq 335) 'Wrong original profile set.'
for ($i = 0; $i -lt 5; $i++) {
    Require ($before[$i].fingerprint -ceq $after[$i].fingerprint -and $before[$i].id -ceq $after[$i].id) 'Original request identity changed.'
    Require ($after[$i].violations -eq 0) 'An original FFT profile still fails.'
}
$ledger = ReadJson (Join-Path $root 'validation/rechecks/2026-10-29/final-core-170/afocal-comparison.json')
$pupils = @(); $rays = @()
foreach ($c in 1, 2) {
    $id = if ($c -eq 1) { '0063' } else { '0093' }
    $d = ReadJson (Join-Path $artifacts "fft-seam-final-20261030/diagnostics/$id-003-binocular_system-c$c-primary-f1-w2-fft-psf-cross-section.json")
    $core = [Collections.Generic.HashSet[string]]::new()
    foreach ($s in $d.diagnosis.raw) {
        if ($s.intensity -le 0) { continue }
        $x = 32 + [Math]::Round($s.normalizedPupilX * 15 / $d.diagnosis.x)
        $y = 32 + [Math]::Round($s.normalizedPupilY * 15 / $d.diagnosis.y)
        [void]$core.Add("$x,$y")
    }
    $native = [Collections.Generic.HashSet[string]]::new()
    foreach ($p in $d.diagnosis.pupil) { if ($p.amplitude -gt .5) { [void]$native.Add("$($p.x),$($p.y)") } }
    Require ($core.Count -eq 699 -and $core.SetEquals($native)) 'Final pupil support differs from frozen native support.'
    $pupils += [pscustomobject]@{configuration=$c;coreNodes=$core.Count;nativeNodes=$native.Count;identicalDiscreteSupport=$true;stretchX=$d.diagnosis.x;stretchY=$d.diagnosis.y;nativeMaskSuppliesProductInputs=$false;fullPhysicalLaunchMappingProven=$false}
    $r = @( (ReadJson (Join-Path $artifacts "fft-seam-final-20261030/rays-c$c.json")).rays | Where-Object { $null -ne $_.directionError } )
    $case = @($ledger.cases | Where-Object { $_.id -like "*-003-binocular_system-c$c-primary-f1-w2-single-ray-trace" })
    Require ($case.Count -eq 1) 'Original ray tolerance source is ambiguous.'
    $coordinateGate = $case[0].effectiveTolerances.Coordinate.absolute
    $directionGate = $case[0].effectiveTolerances.DirectionCosine.absolute
    Require ($coordinateGate -eq 1e-6 -and $directionGate -eq 1e-8) 'Original ray gates changed.'
    $maxPosition = ($r | Measure-Object positionError -Maximum).Maximum
    $maxDirection = ($r | Measure-Object directionError -Maximum).Maximum
    Require ($r.Count -eq 29 -and $maxPosition -le $coordinateGate -and $maxDirection -le $directionGate) 'A comparable seam probe exceeds the original absolute ray gate.'
    $rays += [pscustomobject]@{configuration=$c;comparableProbes=29;outsideNormalizedApiDomain=4;maximumPositionDifferenceMillimeters=$maxPosition;maximumDirectionDifference=$maxDirection;originalToleranceCase=$case[0].id;coordinateAbsoluteGate=$coordinateGate;directionAbsoluteGate=$directionGate;allComparableProbesWithinOriginalAbsoluteGates=$true;bitwiseNativeParityClaimed=$false}
}
$testRuns = @(); $identities = [Collections.Generic.HashSet[string]]::new()
foreach ($spec in @(@{file='afocal-main.trx';count=47;assembly='ZemaxComparison'},@{file='core-plane-main.trx';count=25;assembly='Core'},@{file='huygens-main.trx';count=10;assembly='ZemaxComparison'})) {
    $path = Join-Path $artifacts ('fft-seam-tags-20261030/tests/' + $spec.file)
    [xml]$trx = Get-Content -Raw -LiteralPath $path
    $results = @($trx.TestRun.Results.UnitTestResult)
    Require ($results.Count -eq $spec.count -and @($results | Where-Object outcome -ne 'Passed').Count -eq 0) ('Incomplete or failed tests: ' + $spec.file)
    foreach ($result in $results) { [void]$identities.Add($spec.assembly + ':' + $result.testName) }
    $testRuns += [pscustomobject]@{path=[IO.Path]::GetRelativePath($root,$path).Replace('\','/');passed=$results.Count;failed=0;sha256=(Hash $path)}
}
Require ($identities.Count -eq 82) 'Final distinct test count differs.'
$executions = @(); $temporaryFiles = @()
foreach ($cohort in 'fft-seam-tags-20261030','fft-seam-controls-20261030') {
    foreach ($e in (ReadJson (Join-Path $artifacts "$cohort/execution.json"))) {
        Require ($e.result.exitCode -eq 0 -and $e.sourceUnchanged -and -not $e.result.timedOut) 'Invalid native control receipt.'
        $folder = Join-Path $artifacts "$cohort/$($e.id)"
        $control = ReadJson (Join-Path $folder 'diagnostic-polygon.json')
        $environment = ReadJson (Join-Path $folder 'environment.json')
        Require ($environment.validLicense -and $environment.major -eq 26 -and $environment.minor -eq 1) 'Unexpected native environment.'
        $temporaryFiles += $control.temporaryName
        $executions += [pscustomobject]@{cohort=$cohort;id=$e.id;exitCode=0;sourceUnchanged=$true;formalAcceptanceRequest=$false}
    }
}
Require ($executions.Count -eq 16) 'Native control count differs.'
$installed = 'C:/Users/19851/Documents/Zemax/OBJECTS/Polygon Objects'
$assets = @()
foreach ($spec in @(@{name='PORRO_PRISM-LH.POB';sha='8ba3b4bfe0092319a97e6169de6987132c0f9e2cda99f0b6a19a70eecc3a74f4'},@{name='PORRO_PRISM-RH.POB';sha='c247ee42b9ec39fed6d0411e5f8b7c780ad3a6ff938bd6a642450d1e40dcef68'})) {
    Require ((Hash (Join-Path $installed $spec.name)) -ceq $spec.sha) 'Original installed polygon changed.'
    $assets += $spec
}
foreach ($name in $temporaryFiles) { Require (-not (Test-Path -LiteralPath (Join-Path $installed $name))) 'An owned temporary polygon remains.' }
$copies = @()
foreach ($prefix in 'src/OpticalSystemDesign.Core','src/OpticalSystemDesign.App','tests/OpticalSystemDesign.Tests','tests/OpticalSystemDesign.ZemaxComparison.Tests','tools/OpticalSystemDesign.ZemaxComparison','tools/diagnostics/FftProfileNative20261029') {
    $relative = "$prefix/bin/Release/net10.0/OpticalSystemDesign.Core.dll"
    $copies += [pscustomobject]@{path=$relative;sha256=(Hash (Join-Path $root $relative))}
}
Require (@($copies.sha256 | Select-Object -Unique).Count -eq 1) 'App, diagnostics and tests use different Core assemblies.'
$sources = @()
foreach ($p in 'src/OpticalSystemDesign.Core/Geometries/PolygonGroupGeometry.cs','src/OpticalSystemDesign.Core/Interactions/InteractionModels.cs','src/OpticalSystemDesign.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs') { $sources += [pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))} }
$assessment = [ordered]@{date='2026-10-30';status='original_fft_profile_pointwise_issue_closed';objectiveAchieved=$true;remainingOriginalProfileViolations=0;originalProfileViolationsBefore=335;originalToleranceSamplingReferenceChanged=$false;sourceArchive=$sourceArchive;sourceArchiveSha256=(Hash (Join-Path $root $sourceArchive));sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);productCoreWorkingTreeChangesAccepted=$true;coreSources=$sources;coreCopies=$copies;profilesBefore=$before;profilesAfter=$after;pupilSupport=$pupils;seamProbeDiagnostics=$rays;latestDistinctTestsPassed=82;testRuns=$testRuns;isolatedCandidate47SupersededAndNotAdded=$true;nativeControlsCompleted=16;nativeControlsAreOpticalPasses=$false;nativeExecutions=$executions;installedAssets=$assets;ownedTemporaryPolygonsRemaining=0;foregroundDesktopUsed=$false;nativeInternalTraversalFullyKnown=$false;gaussianEnvelopeDoesNotProvePhysicalLaunchMap=$true;full170Or95CorpusRerun=$false;otherOpenIssuesClosed=$false}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archivePath = Join-Path $destination 'evidence.zip'
Require (-not (Test-Path -LiteralPath $archivePath)) 'Do not overwrite a sealed evidence archive.'
$assessmentPath = Join-Path $destination 'assessment.json'
$assessment | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files = [Collections.Generic.List[IO.FileInfo]]::new()
$extensions = @('.json','.txt','.log','.cfg','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj','.trx')
foreach ($dir in Get-ChildItem -LiteralPath $artifacts -Directory -Filter 'fft-seam-*20261030') { foreach ($f in Get-ChildItem -LiteralPath $dir.FullName -Recurse -File) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } } }
foreach ($folder in 'tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') { foreach ($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } } }
foreach ($p in @('docs/AFOCAL_FFT_SEAM_REPAIR_2026-10-30.md','tests/OpticalSystemDesign.Tests/PolygonPlanePrecisionTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalFftProfilePointwiseTests.cs') + $sources.path) { $files.Add((Get-Item -LiteralPath (Join-Path $root $p))) }
foreach ($c in 1,2) { foreach ($p in @("afocal-native-resume-20261029-02/binocular-c$c-boundary-and-seam33.request.json","afocal-native-resume-20261029-02/binocular-c$c-boundary-and-seam33/image-geometry.json","afocal-native-resume-20261029-paths-02/binocular-c$c-nsc-paths-pupil/nsc-paths.json")) { $files.Add((Get-Item -LiteralPath (Join-Path $artifacts $p))) } }
$files.Add((Get-Item -LiteralPath $assessmentPath))
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($archivePath,[IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$canonical = @{}; $entries = [Collections.Generic.List[object]]::new()
try {
    foreach ($f in $files | Sort-Object FullName -Unique) {
        $logical = [IO.Path]::GetRelativePath($root,$f.FullName).Replace('\','/')
        Require (-not $logical.StartsWith('../')) 'Evidence path escaped the repository.'
        $sha = Hash $f.FullName
        if (-not $canonical.ContainsKey($sha)) {
            $canonical[$sha] = $logical
            $entry = $zip.CreateEntry($logical,[IO.Compression.CompressionLevel]::Optimal)
            $inputStream = [IO.File]::OpenRead($f.FullName); $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        }
        $entries.Add([pscustomobject]@{path=$logical;member=$canonical[$sha];length=$f.Length;sha256=$sha})
    }
    $entry = $zip.CreateEntry('entry-map.json'); $writer = [IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
    try { $writer.Write((ConvertTo-Json -InputObject @($entries) -Depth 10)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($e in $entries) {
        $member = $zip.GetEntry($e.member); $inputStream = $member.Open()
        try { $sha = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($inputStream)) } finally { $inputStream.Dispose() }
        Require ($sha -ceq $e.sha256 -and $member.Length -eq $e.length) ('Evidence byte mismatch: ' + $e.path)
    }
} finally { $zip.Dispose() }
$seal = [ordered]@{date='2026-10-30';status=$assessment.status;objectiveAchieved=$true;remainingOriginalProfileViolations=0;maximumIntensityDifference=$after[3].maximum;latestDistinctTestsPassed=82;nativeControlsCompleted=16;nativeInternalTraversalFullyKnown=$false;coreAssemblySha256=$copies[0].sha256;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal | ConvertTo-Json -Depth 10
