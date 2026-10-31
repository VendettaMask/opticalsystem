param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $RepositoryRoot).Path
$artifacts=Join-Path $root 'artifacts/validation'
$destination=Join-Path $root 'validation/rechecks/2026-10-30/riflescope-pupil'
function ReadJson([string]$p) { Get-Content -Raw -LiteralPath $p | ConvertFrom-Json }
function Require([bool]$condition,[string]$message) { if (-not $condition) { throw $message } }
function Hash([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant() }
function Relative([string]$p) { [IO.Path]::GetRelativePath($root,$p).Replace('\','/') }
$archivePath=Join-Path $destination 'evidence.zip'
Require (-not (Test-Path -LiteralPath $archivePath)) 'Never overwrite a sealed evidence archive.'
$dependencies=@(
    @{path='validation/rechecks/2026-10-27/afocal-fft-energy/evidence-current.zip';sha256='235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1'},
    @{path='validation/rechecks/2026-10-30/fft-seam-repair/evidence.zip';sha256='8a9a21f077515b016dd90c7cff890abccc7c038058cf0094ace38970523a2bd1'},
    @{path='validation/rechecks/2026-10-30/afocal-auto-grid/evidence.zip';sha256='b7340ea55499260feef6080a9e265c68318c37f500cec66df5a2e83b8a39586f'})
foreach ($d in $dependencies) { Require ((Hash (Join-Path $root $d.path)) -ceq $d.sha256) 'An immutable dependency changed.' }
$expectationPath=Join-Path $root 'validation/rechecks/2026-10-30/afocal-auto-grid/current-grid-expectations.json'
$expectations=ReadJson $expectationPath
$manifestPath=Join-Path $root $expectations.originalManifest
Require ((Hash $manifestPath) -ceq '8b19e5b6e4094f5aa0ff0442e7e0de4a179d30fd296609f8bb1cd1df61da1d07') 'Original request manifest changed.'
$manifest=ReadJson $manifestPath
$before=@(ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-final/summary.json'))
$after=@(ReadJson (Join-Path $artifacts 'riflescope-pupil-20261030-grids-final/summary.json'))
Require ($before.Count -eq 20 -and $after.Count -eq 20 -and @($before | Where-Object failure).Count -eq 2) 'Wrong original grid set.'
$newlyComparable=@(); $comparedValues=0
for ($i=0;$i -lt 20;$i++) {
    $a=$after[$i]; $b=$before[$i]
    Require ($a.id -ceq $b.id -and $a.fingerprint -ceq $b.fingerprint -and $a.snapshotSha256 -ceq $b.snapshotSha256) 'Original request or snapshot changed.'
    Require (-not $a.failure -and $null -ne $a.violations -and $a.violations -eq 0 -and $a.metric.conclusion -ceq 'Pass' -and $a.xGateRatio -le 1 -and $a.yGateRatio -le 1) 'An original grid fails the unchanged gates.'
    Require (-not $a.inferredDiameterIsProductInput) 'Inferred scale used as product input.'
    $expected=@($expectations.cases | Where-Object id -CEQ $a.id)
    $original=@($manifest.cases | Where-Object id -CEQ $a.id)
    Require ($expected.Count -eq 1 -and $original.Count -eq 1 -and $expected[0].requestFingerprint -ceq $a.fingerprint -and $expected[0].originalConclusion -ceq $original[0].originalConclusion -and $expected[0].currentConclusion -ceq 'Pass' -and $expected[0].pointwiseRequired) 'Current expectation lost its original identity.'
    $comparedValues+=$a.metric.count
    if ($b.failure) { $newlyComparable+=$a }
}
Require (($newlyComparable.id.Substring(0,4) -join ',') -ceq '0007,0008' -and ($newlyComparable.metric | Measure-Object count -Sum).Sum -eq 17408 -and $comparedValues -eq 250880) 'Wrong closure scope.'
$profiles=@(ReadJson (Join-Path $artifacts 'riflescope-pupil-20261030-profiles-final/summary.json'))
$oldProfiles=@(ReadJson (Join-Path $artifacts 'afocal-auto-grid-20261030-profiles/summary.json'))
Require ($profiles.Count -eq 5) 'Wrong original profile count.'
for ($i=0;$i -lt 5;$i++) { Require ($profiles[$i].id -ceq $oldProfiles[$i].id -and $profiles[$i].fingerprint -ceq $oldProfiles[$i].fingerprint -and $profiles[$i].violations -eq 0) 'Original profile identity changed or regressed.' }
$testRuns=@(); $testIdentities=[Collections.Generic.HashSet[string]]::new(); $controlResults=@()
$controlsPath=Join-Path $destination 'controls.json'; $controls=ReadJson $controlsPath
foreach ($spec in @(@{file='parabasal-final.trx';count=121;assembly='ZemaxComparison'},@{file='parabasal-core.trx';count=35;assembly='Core'})) {
    $p=Join-Path $artifacts ('riflescope-pupil-20261030-tests/'+$spec.file)
    [xml]$trx=Get-Content -Raw -LiteralPath $p
    $results=@($trx.TestRun.Results.UnitTestResult)
    Require ($results.Count -eq $spec.count -and @($results | Where-Object outcome -NE 'Passed').Count -eq 0) 'Incomplete or failed final tests.'
    foreach ($result in $results) {
        [void]$testIdentities.Add($spec.assembly+':'+$result.testName)
        if ($result.testName -like '*AfocalParabasalScaleTests.FiniteScaleAndItsLaunches*') {
            $value=$result.Output.StdOut.Trim() | ConvertFrom-Json
            $native=@($controls.cases | Where-Object id -CEQ $value.id)
            Require ($native.Count -eq 1) 'Missing native control.'
            $diameterError=[Math]::Abs($value.D-$native[0].nativeExitDiameter)
            $positionError=[Math]::Abs($value.Z-$native[0].nativeExitPosition)
            $launchError=($value.launchErrorsMillimeters | Measure-Object -Maximum).Maximum
            Require ($diameterError -le 1e-9 -and $positionError -le 1e-9 -and $launchError -le 1e-11) 'Independent finite-scale control fails.'
            foreach ($kind in 'Operand','Launch') { $nativePath=$native[0].('native'+$kind+'Path'); $nativeHash=$native[0].('native'+$kind+'Sha256'); Require ((Hash (Join-Path $root $nativePath)) -ceq $nativeHash) 'Native control fixture byte identity differs.' }
            $controlResults += [pscustomobject]@{id=$value.id;coreDiameter=$value.D;nativeDiameter=$native[0].nativeExitDiameter;diameterErrorMillimeters=$diameterError;corePosition=$value.Z;nativePosition=$native[0].nativeExitPosition;positionErrorMillimeters=$positionError;maximumLaunchErrorMillimeters=$launchError}
        }
    }
    $testRuns += [pscustomobject]@{path=(Relative $p);passed=$results.Count;failed=0;skipped=0;sha256=(Hash $p)}
}
Require ($testIdentities.Count -eq 156 -and $controlResults.Count -eq 5) 'Wrong latest test/control count.'
[xml]$failedTrx=Get-Content -Raw (Join-Path $artifacts 'riflescope-pupil-20261030-tests/parabasal-controls.trx')
Require (@($failedTrx.TestRun.Results.UnitTestResult | Where-Object outcome -EQ 'Failed').Count -eq 2) 'Initial mismatched control failures were lost.'
$copies=@()
foreach ($prefix in 'src/OpticalSystemDesign.Core','src/OpticalSystemDesign.App','tests/OpticalSystemDesign.Tests','tests/OpticalSystemDesign.ZemaxComparison.Tests','tools/OpticalSystemDesign.ZemaxComparison','tools/diagnostics/FftProfileNative20261029') {
    $p="$prefix/bin/Release/net10.0/OpticalSystemDesign.Core.dll"; $copies+=[pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))}
}
Require (@($copies.sha256 | Select-Object -Unique).Count -eq 1) 'Final Core copies differ.'
$identity=ReadJson (Join-Path $artifacts 'riflescope-pupil-20261030-grids-final/identity.json')
Require ($identity.coreSha256 -ceq $copies[0].sha256 -and -not $identity.originalRequestsAndGatesChanged) 'Final grid used a different Core.'
$unchanged=@('tools/OpticalSystemDesign.ZemaxComparison/Metrics/ComparisonMetrics.cs','tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json','src/OpticalSystemDesign.Core/Raytrace/RayGenerator.cs','src/OpticalSystemDesign.Core/Raytrace/RayGenerator.StopAiming.cs','src/OpticalSystemDesign.Core/Raytrace/RayGenerator.FftAiming.cs','src/OpticalSystemDesign.Core/Services/Paraxial.cs',$expectations.originalManifest)
git -C $root -c core.fsmonitor=false diff --exit-code -- @unchanged | Out-Null
Require ($LASTEXITCODE -eq 0) 'Original gates, precise solver or analytic paraxial source changed.'
$executions=@(); $baseline=ReadJson (Join-Path $artifacts 'riflescope-pupil-20261030-native-03/original/active-model.json')
foreach ($cohort in '02','03','04','05') {
    $capture=Join-Path $artifacts ('riflescope-pupil-20261030-native-'+$cohort)
    $run=@(ReadJson (Join-Path $capture 'execution.json'))
    Require ($run.Count -eq 3) 'Incomplete native cohort.'
    foreach ($e in $run) {
        Require ($e.result.ExitCode -eq 0 -and -not $e.result.TimedOut -and $e.sourceUnchanged) 'Native diagnostic failed or source changed.'
        $request=ReadJson (Join-Path $capture ($e.id+'.request.json'))
        $environment=ReadJson (Join-Path $capture ($e.id+'/environment.json'))
        Require (-not $request.captureScreenshots -and -not $request.formalAcceptanceRequest -and $environment.validLicense -and $environment.major -eq 26 -and $environment.minor -eq 1) 'Native environment or background-only contract differs.'
        Require ((Hash $request.input) -ceq '36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b') 'Original native input changed.'
        if ($cohort -eq '05') {
            $active=ReadJson (Join-Path $capture ($e.id+'/active-model.json'))
            Require ($request.freezeThicknessSolves) 'Held-out final control did not freeze thickness.'
            for ($i=1;$i -lt $active.surfaces.Count-1;$i++) { Require ($active.surfaces[$i].Thickness -eq $baseline.surfaces[$i].Thickness -and $active.surfaces[$i].Radius -eq $baseline.surfaces[$i].Radius) 'Final control changed physical prescription.' }
        }
        $executions += [pscustomobject]@{cohort=$cohort;id=$e.id;exitCode=0;sourceUnchanged=$true;formalOpticalPass=$false;samePrescriptionForIndependentControl=($cohort -eq '05');unfrozenPrimarySolvesNotPairedWithCore=($cohort -eq '04')}
    }
}
Require ($executions.Count -eq 12) 'Wrong native diagnostic count.'
$failedBuild=ReadJson (Join-Path $artifacts 'riflescope-pupil-20261030-native-01/build.json')
Require ($failedBuild.ExitCode -ne 0 -and -not (Test-Path (Join-Path $artifacts 'riflescope-pupil-20261030-native-01/execution.json'))) 'Initial API path compilation failure was lost.'
$coreSources=@(); $sourcePaths=@(git -C $root -c core.fsmonitor=false diff --name-only -- src/OpticalSystemDesign.Core)+@('src/OpticalSystemDesign.Core/Raytrace/RayGenerator.AfocalScale.cs','src/OpticalSystemDesign.Core/Services/AfocalParabasalPupilScale.cs')
foreach ($p in $sourcePaths | Sort-Object -Unique) { $coreSources += [pscustomobject]@{path=$p;sha256=(Hash (Join-Path $root $p))} }
$assessment=[ordered]@{date='2026-10-30';status='original_two_riflescope_automatic_grids_closed';requestedIssueClosed=$true;allAfocalIssuesClosed=$false;remainingIncomparableOriginalPsfGrids=0;newlyComparedValues=17408;allOriginalPsfGridValuesCompared=$comparedValues;pointwiseViolations=0;original335ProfileViolationsRemaining=0;originalPrescriptionSnapshotRequestsReferenceSamplingAndGatesChanged=$false;dependencies=$dependencies;sourceBaseCommit=(git -C $root -c core.fsmonitor=false rev-parse HEAD);coreCopies=$copies;coreWorkingTreeSources=$coreSources;gridBefore=$before;gridAfter=$after;originalProfiles=$profiles;finiteScaleControls=$controlResults;latestDistinctTestsPassed=156;testRuns=$testRuns;earlierControlFailureRunAndPrior133NotAdded=$true;validNativeDiagnosticExecutions=$executions;nativeControlsCountedAsOpticalPasses=0;nativeCompileFailures=1;initialPrimaryControlThicknessMismatchPreserved=$true;nativePrivateGeneralCacheFullyReproduced=$false;preciseRayAndParaxialSourceUnchanged=$true;runtimeReadsNativeCoordinatesOrFittedScale=$false;productScope='three positive finite-scale probes in centered infinite angle-field Standard/Plane systems';foregroundDesktopUsed=$false;full170Or95CorpusRerun=$false;otherOpenIssuesClosed=$false;committedOrPushed=$false}
$assessmentPath=Join-Path $destination 'assessment.json'
$assessment | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $assessmentPath -Encoding utf8
$files=[Collections.Generic.List[IO.FileInfo]]::new(); $extensions=@('.json','.txt','.log','.cfg','.config','.cs','.patch','.pob','.zmx','.md','.ps1','.csproj','.trx','.csv','.tsv')
foreach ($item in Get-ChildItem -LiteralPath $artifacts -Filter 'riflescope-pupil-20261030*') {
    $candidates=if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File) } else { @($item) }
    foreach ($f in $candidates) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } }
}
foreach ($folder in 'tools/diagnostics/FftProfileNative20261029','tools/OpticalSystemDesign.ZemaxComparison/ZemaxHost') { foreach ($f in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File) { if ($f.Extension.ToLowerInvariant() -in $extensions) { $files.Add($f) } } }
foreach ($p in @('docs/AFOCAL_RIFLESCOPE_PUPIL_REPAIR_2026-10-30.md','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalParabasalScaleTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/AfocalPsfContractTests.cs','tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj')+$coreSources.path+$unchanged) { $files.Add((Get-Item -LiteralPath (Join-Path $root $p))) }
foreach ($p in $assessmentPath,$controlsPath,$expectationPath) { $files.Add((Get-Item -LiteralPath $p)) }
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::Open($archivePath,[IO.FileMode]::CreateNew); $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
$canonical=@{}; $entries=[Collections.Generic.List[object]]::new()
try {
    foreach ($f in $files | Sort-Object FullName -Unique) {
        $logical=Relative $f.FullName; Require (-not $logical.StartsWith('../')) 'Evidence path escaped the repository.'; $sha=Hash $f.FullName
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
$seal=[ordered]@{date='2026-10-30';status=$assessment.status;requestedIssueClosed=$true;allAfocalIssuesClosed=$false;originalPsfGridsPassed=20;remainingIncomparableOriginalPsfGrids=0;pointwiseViolations=0;original335ProfileViolationsRemaining=0;latestDistinctTestsPassed=156;nativeValidDiagnosticControls=12;nativeCompileFailures=1;nativeControlsCountedAsOpticalPasses=0;coreAssemblySha256=$copies[0].sha256;archiveSha256=(Hash $archivePath);archiveBytes=(Get-Item -LiteralPath $archivePath).Length;logicalEntries=$entries.Count;uniqueMembers=$canonical.Count;allEntryHashesVerified=$true;proprietaryAssembliesIncluded=$false;foregroundDesktopUsed=$false}
$seal | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $destination 'verification.json') -Encoding utf8
$seal | ConvertTo-Json -Depth 10
