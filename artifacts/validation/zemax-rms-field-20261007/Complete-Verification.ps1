$ErrorActionPreference = 'Stop'
$taskRepo = 'D:\Projects\opticalsystem'
$taskEvidence = Join-Path $taskRepo 'artifacts/validation/zemax-rms-field-20261007'
$taskRoot = Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261007'
function Hash-TaskFile([string]$taskPath) { (Get-FileHash -LiteralPath $taskPath).Hash.ToLowerInvariant() }
$taskTests = foreach ($taskName in @('main-release.trx','comparison-final-release.trx','rms-targeted-final-release.trx','rms-targeted-final-debug.trx','rms-tool-targeted.trx','ui-cleanup-isolated-release.trx')) {
    $taskPath = Join-Path $taskEvidence $taskName
    [xml]$taskTrx = Get-Content -LiteralPath $taskPath
    $taskCounter = $taskTrx.TestRun.ResultSummary.Counters
    @{file=$taskPath;sha256=(Hash-TaskFile $taskPath);total=[int]$taskCounter.total;passed=[int]$taskCounter.passed;failed=[int]$taskCounter.failed;executed=[int]$taskCounter.executed;skipped=[int]$taskCounter.notExecuted;
      failures=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object { @{name=$_.testName;message=$_.Output.ErrorInfo.Message} });
      newRmsTests=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'RectangularRmsParityTests|RmsFieldSamplingContractTests|CapturedRmsFieldSamplingTests' } | ForEach-Object { @{name=$_.testName;outcome=$_.outcome} })}
}
$taskMain = $taskTests | Where-Object { $_.file.EndsWith('main-release.trx') }
$taskTool = $taskTests | Where-Object { $_.file.EndsWith('comparison-final-release.trx') }
if ($taskMain.total -ne 4351 -or $taskMain.passed -ne 4349 -or $taskMain.failed -ne 2 -or $taskMain.newRmsTests.Count -ne 14) { throw 'Unexpected main test baseline' }
if (@($taskMain.failures | Where-Object { $_.name -notmatch 'FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays|PanelContentLayoutTests.DrawingReadsMaterialValuesAndCombinesThemWithEditedTolerances' }).Count) { throw 'Unreviewed full-suite failure' }
$taskIsolated = $taskTests | Where-Object { $_.file.EndsWith('ui-cleanup-isolated-release.trx') }
if ($taskIsolated.passed -ne 16 -or $taskIsolated.failed -ne 0) { throw 'UI cleanup isolation incomplete' }
if ($taskTool.total -ne 156 -or $taskTool.passed -ne 154 -or $taskTool.failed -ne 2 -or $taskTool.newRmsTests.Count -ne 30) { throw 'Unexpected tool test baseline' }
foreach ($taskTest in $taskTests) {
    if ($taskTest.skipped -ne 0 -or $taskTest.total -ne $taskTest.executed -or @($taskTest.newRmsTests | Where-Object outcome -ne 'Passed').Count) { throw 'Incomplete new RMS test scope' }
}
$taskSummaryPath = Join-Path $taskRoot 'rms-original132-recalculated/summary.json'
$taskSummary = Get-Content $taskSummaryPath -Raw | ConvertFrom-Json
$taskOldPath = Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261006/tessar-vignetting-repair-final/summary.json'
$taskOld = Get-Content $taskOldPath -Raw | ConvertFrom-Json
$taskRawVerified=0
foreach ($taskRow in $taskSummary.rows) {
    $taskPrior = @($taskOld.rows | Where-Object { $_.batch -eq $taskRow.batch -and $_.lens -eq $taskRow.lens -and $_.key -eq $taskRow.key })
    if ($taskPrior.Count -ne 1 -or $taskPrior[0].currentConclusion -ne $taskRow.currentConclusion) { throw 'Original matrix changed' }
    if ($taskPrior[0].currentWorkbenchRawSha256 -ne $taskRow.currentWorkbenchRawSha256) { throw 'Original raw result changed' }
    if ($taskPrior[0].currentWorkbenchRawSha256) {
        if ((Hash-TaskFile $taskPrior[0].currentWorkbenchRaw) -ne $taskPrior[0].currentWorkbenchRawSha256) { throw 'Previous stage raw output changed' }
        $taskRawVerified++
    }
}
if ($taskSummary.total -ne 132 -or $taskSummary.counts.Pass -ne 84 -or $taskRawVerified -ne 131) { throw 'Unexpected numerical scope' }
$taskControlPath = Join-Path $taskRoot 'rms-controls-recalculated/summary.json'
$taskControl = Get-Content $taskControlPath -Raw | ConvertFrom-Json
if ($taskControl.total -ne 17 -or $taskControl.counts.Pass -ne 14 -or $taskControl.counts.Difference -ne 3 -or $taskControl.changedConclusions -ne 4) { throw 'Unexpected independent controls' }
$taskRetainedCounts = @{}
foreach ($taskRelative in @('rms-original132-recalculated/immutable-evidence.json','rms-controls-recalculated/immutable-evidence.json')) {
    $taskHashes = Get-Content (Join-Path $taskRoot $taskRelative) -Raw | ConvertFrom-Json
    foreach ($taskItem in $taskHashes.PSObject.Properties) {
        if ((Hash-TaskFile $taskItem.Name) -ne $taskItem.Value) { throw "Changed immutable evidence: $($taskItem.Name)" }
    }
    $taskRetainedCounts[$taskRelative] = @($taskHashes.PSObject.Properties).Count
}
$taskPlan = Get-Content (Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json') -Raw | ConvertFrom-Json
foreach ($taskSample in $taskPlan.samples) { if ((Hash-TaskFile $taskSample.sourcePath) -ne $taskSample.sourceSha256) { throw 'Official source changed' } }
$taskFixtureRoot = Join-Path $taskRepo 'validation/zemax/2026-r1/rms-field-sampling-2026-10-07'
$taskFixture = Get-Content (Join-Path $taskFixtureRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($taskFile in $taskFixture.files) { if ((Hash-TaskFile (Join-Path $taskFixtureRoot $taskFile.path)) -ne $taskFile.sha256) { throw 'Fixture hash changed' } }
$taskAssemblies = foreach ($taskRelative in @('src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll','src/OptilandWorkbench.Application/bin/Release/net10.0/OptilandWorkbench.Application.dll','tools/OptilandWorkbench.ZemaxComparison/bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll')) {
    $taskPath = Join-Path $taskRepo $taskRelative
    @{path=$taskPath;sha256=(Hash-TaskFile $taskPath)}
}
foreach ($taskReplay in @($taskSummary,$taskControl)) {
    if ($taskReplay.coreAssemblySha256 -ne $taskAssemblies[0].sha256 -or $taskReplay.applicationAssemblySha256 -ne $taskAssemblies[1].sha256 -or $taskReplay.comparisonAssemblySha256 -ne $taskAssemblies[2].sha256) { throw 'Default assemblies differ from formal recalculation' }
}
$taskNativeValid = @($taskSummary.rows + $taskControl.rows | Where-Object { $_.nativeEnvironment.major -ne 26 -or $_.nativeEnvironment.minor -ne 1 -or $_.nativeEnvironment.opticStudioVersion -ne 260127 -or -not $_.nativeEnvironment.validLicense -or $_.nativeEnvironment.initializationErrors }).Count -eq 0
if (-not $taskNativeValid) { throw 'Invalid native version/license' }
$taskFrozenDiff = git -C $taskRepo status --porcelain -- validation/history artifacts/zemax/123456-zemax-2026-r1-baseline
if ($LASTEXITCODE -ne 0 -or $taskFrozenDiff) { throw 'Frozen baseline or historical auxiliary data changed' }
$taskRecord = @{date='2026-10-07';completedUtc=[DateTimeOffset]::UtcNow.ToString('O');builds=@{Debug='Default solution output; 0 warnings/errors';Release='Default solution output; 0 warnings/errors'};
    tests=@($taskTests);assemblies=@($taskAssemblies);numericalCounts=$taskSummary.counts;previous84PassRegressions=0;previousStageRawOutputsVerified=$taskRawVerified;previousStageRawChanged=0;
    replaySummary=$taskSummaryPath;replaySummarySha256=(Hash-TaskFile $taskSummaryPath);previousStageSummary=$taskOldPath;previousStageSummarySha256=(Hash-TaskFile $taskOldPath);
    nativeControlSummary=$taskControlPath;nativeControlSummarySha256=(Hash-TaskFile $taskControlPath);nativeControlCounts=$taskControl.counts;additionalNativeControlComparisons=17;
    retainedEvidenceCounts=$taskRetainedCounts;retainedEvidenceUnchanged=$true;standardSourceHashesVerified=$taskPlan.samples.Count;fixtureFilesVerified=$taskFixture.files.Count;allNativeEnvironmentsValid=$taskNativeValid;
    original132SettingsChanged=$false;original132TolerancesChanged=$false;original132NativeRecaptured=$false;frozenBaselineChanged=$false;historicalAuxiliaryDataChanged=$false;
    debugFullSuite='Not rerun this batch; prior 2026-10-06 full Debug 4336/1/4337 is historical. Current RMS targeted Debug 22/22 including all 14 new main cases.';
    laboratories='Not rerun; prior InitialStructure Release 258/2/260 and Coating Debug/Release 47/47 are historical.';
    fullReleaseUiCleanupFailure='PanelContentLayoutTests GbT13323_1991/cemented true: NullReferenceException in SafeHeadlessUnitTestSession.Dispose. Original full-suite failure retained. PanelContentLayout and WavefrontSurfaceRender isolation 16/16 passes; no test suppression or headless helper change in this RMS task, root cause remains unresolved.';
    intermediateRuns=@('comparison-release.trx: earlier contract-only stage 136/2/138, before 18 fixture tests','Probe.csproj first compile and rms-targeted-release command compile: diagnostic/test source errors fixed before final executions','Plot-Rms.py optional combined figure unavailable: no matplotlib; formal comparison PNGs retained and reviewed');releaseGatePassed=$false}
$taskRecord | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $taskEvidence 'verification-final.json') -Encoding utf8NoBOM
$taskTests | ForEach-Object { '{0}: {1}/{2}/{3}' -f (Split-Path $_.file -Leaf),$_.passed,$_.failed,$_.total }
