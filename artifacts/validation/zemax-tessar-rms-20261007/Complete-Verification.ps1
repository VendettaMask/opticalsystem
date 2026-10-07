$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskRoot=Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261007'
$taskEvidence=Join-Path $taskRepo 'artifacts/validation/zemax-tessar-rms-20261007'
function Hash-TaskFile([string]$taskPath) { (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$taskTests=@(foreach($taskName in @('main-current-release.trx','tool-final-current-release.trx','rms-grin-final-release.trx','rms-grin-final-debug.trx','tool-rms-audit-targeted.trx','concurrency-final-release.trx','concurrency-final-debug.trx')) {
    $taskPath=Join-Path $taskEvidence $taskName
    [xml]$taskTrx=Get-Content -LiteralPath $taskPath
    $taskCounter=$taskTrx.TestRun.ResultSummary.Counters
    @{file=$taskPath;sha256=(Hash-TaskFile $taskPath);total=[int]$taskCounter.total;passed=[int]$taskCounter.passed;failed=[int]$taskCounter.failed;executed=[int]$taskCounter.executed;skipped=[int]$taskCounter.notExecuted;
      failures=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object {@{name=$_.testName;message=$_.Output.ErrorInfo.Message}});
      newTests=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object {$_.testName -match 'GaussianPupilParityTests|GaussianPupilIntegralPreservesFormalContinuousGrinPropagation|RayAuditContractTests'} | ForEach-Object {@{name=$_.testName;outcome=$_.outcome}})}
})
$taskMain=$taskTests | Where-Object {$_.file.EndsWith('main-current-release.trx')}
$taskTool=$taskTests | Where-Object {$_.file.EndsWith('tool-final-current-release.trx')}
if($taskMain.total -ne 4364 -or $taskMain.passed -ne 4363 -or $taskMain.failed -ne 1 -or $taskMain.newTests.Count -ne 13){throw 'Unexpected formal test baseline'}
if(@($taskMain.failures | Where-Object {$_.name -notmatch 'FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays'}).Count){throw 'Unreviewed formal test failure'}
if($taskTool.total -ne 164 -or $taskTool.passed -ne 162 -or $taskTool.failed -ne 2 -or $taskTool.newTests.Count -ne 8){throw 'Unexpected tool test baseline'}
foreach($taskTest in $taskTests){
    if($taskTest.skipped -ne 0 -or $taskTest.executed -ne $taskTest.total -or @($taskTest.newTests | Where-Object outcome -ne 'Passed').Count){throw 'Incomplete new test verification'}
    if($taskTest.file -match 'rms-grin-final' -and ($taskTest.passed -ne 57 -or $taskTest.failed -ne 0)){throw 'Incomplete RMS/GRIN targeted check'}
    if($taskTest.file.EndsWith('tool-rms-audit-targeted.trx') -and ($taskTest.passed -ne 38 -or $taskTest.failed -ne 0)){throw 'Incomplete tool RMS/audit targeted check'}
    if($taskTest.file -match 'concurrency-final' -and ($taskTest.passed -ne 5 -or $taskTest.failed -ne 0)){throw 'Incomplete independent read concurrency check'}
}
$taskSummaryPath=Join-Path $taskRoot 'tessar-rms-repair-final-v2-original132/summary.json'
$taskControlPath=Join-Path $taskRoot 'tessar-rms-repair-final-v2-controls/summary.json'
$taskSummary=Get-Content $taskSummaryPath -Raw | ConvertFrom-Json
$taskControl=Get-Content $taskControlPath -Raw | ConvertFrom-Json
$taskPreviousPath=Join-Path $taskRoot 'rms-original132-recalculated/summary.json'
$taskPrevious=Get-Content $taskPreviousPath -Raw | ConvertFrom-Json
$taskRawUnchanged=0
$taskRawChanges=@()
$taskRegressions=@()
foreach($taskRow in $taskSummary.rows){
    $taskPrior=@($taskPrevious.rows | Where-Object {$_.batch -eq $taskRow.batch -and $_.lens -eq $taskRow.lens -and $_.key -eq $taskRow.key})
    if($taskPrior.Count -ne 1){throw 'Unmatched original matrix row'}
    $taskPrior=$taskPrior[0]
    if($taskPrior.currentConclusion -eq 'Pass' -and $taskRow.currentConclusion -ne 'Pass'){$taskRegressions+=$taskRow}
    if($taskPrior.currentWorkbenchRawSha256){
        if((Hash-TaskFile $taskPrior.currentWorkbenchRaw) -ne $taskPrior.currentWorkbenchRawSha256){throw 'Historical recalculation modified'}
        if($taskRow.currentWorkbenchRawSha256 -eq $taskPrior.currentWorkbenchRawSha256){$taskRawUnchanged++}
        else {$taskRawChanges+=@{lens=$taskRow.lens;key=$taskRow.key;before=$taskPrior.currentConclusion;after=$taskRow.currentConclusion}}
    }
    if($taskRow.nativeRequestFingerprint -ne $taskPrior.nativeRequestFingerprint){throw 'Original native request fingerprint changed'}
}
if($taskSummary.total -ne 132 -or $taskSummary.counts.Pass -ne 85 -or $taskSummary.counts.Close -ne 12 -or $taskSummary.counts.Difference -ne 26 -or $taskSummary.counts.Incomparable -ne 8 -or $taskSummary.counts.Error -ne 1 -or $taskRegressions.Count -ne 0 -or $taskRawUnchanged -ne 126 -or $taskRawChanges.Count -ne 5 -or @($taskRawChanges | Where-Object key -ne 'RMS vs Field').Count){throw 'Unexpected original matrix scope'}
if($taskControl.total -ne 17 -or $taskControl.counts.Pass -ne 17){throw 'Independent RMS controls incomplete'}
foreach($taskReplay in @($taskSummary,$taskControl)){
    if((Hash-TaskFile $taskReplay.originalSummary) -ne $taskReplay.originalSummarySha256){throw 'Original capture summary changed'}
    foreach($taskRow in $taskReplay.rows){
        if($taskRow.requestChanged -ne $false -or $taskRow.tolerancesChanged -ne $false -or $taskRow.nativeRecaptured -ne $false){throw 'Existing capture scope changed'}
        foreach($taskPair in @(@('currentWorkbenchRaw','currentWorkbenchRawSha256'),@('nativeRaw','nativeRawSha256'),@('snapshot','snapshotSha256'),@('source','sourceSha256'))){
            if($taskRow.($taskPair[1]) -and (Hash-TaskFile $taskRow.($taskPair[0])) -ne $taskRow.($taskPair[1])){throw "Final row evidence changed: $($taskRow.lens)/$($taskRow.key)/$($taskPair[0])"}
        }
        $taskComparison=Get-Content $taskRow.currentComparison -Raw | ConvertFrom-Json
        if($taskComparison.conclusion -ne $taskRow.currentConclusion){throw 'Published row conclusion differs from actual comparison'}
    }
}
$taskRetainedCounts=@{}
foreach($taskRelative in @('tessar-rms-repair-final-v2-original132/immutable-evidence.json','tessar-rms-repair-final-v2-controls/immutable-evidence.json')){
    $taskHashes=Get-Content (Join-Path $taskRoot $taskRelative) -Raw | ConvertFrom-Json
    foreach($taskItem in $taskHashes.PSObject.Properties){if((Hash-TaskFile $taskItem.Name) -ne $taskItem.Value){throw "Immutable evidence changed: $($taskItem.Name)"}}
    $taskRetainedCounts[$taskRelative]=@($taskHashes.PSObject.Properties).Count
}
$taskPlan=Get-Content (Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json') -Raw | ConvertFrom-Json
foreach($taskSample in $taskPlan.samples){if((Hash-TaskFile $taskSample.sourcePath) -ne $taskSample.sourceSha256){throw 'Official source changed'}}
$taskFixtureCounts=@{}
foreach($taskName in @('rms-field-sampling-2026-10-07','tessar-ray-audit-2026-10-07')){
    $taskFixtureRoot=Join-Path $taskRepo "validation/zemax/2026-r1/$taskName"
    $taskFixture=Get-Content (Join-Path $taskFixtureRoot 'manifest.json') -Raw | ConvertFrom-Json
    foreach($taskFile in $taskFixture.files){if((Hash-TaskFile (Join-Path $taskFixtureRoot $taskFile.path)) -ne $taskFile.sha256){throw 'Frozen fixture changed'}}
    $taskFixtureCounts[$taskName]=$taskFixture.files.Count
}
$taskAssemblies=@(foreach($taskRelative in @('src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll','src/OptilandWorkbench.Application/bin/Release/net10.0/OptilandWorkbench.Application.dll','tools/OptilandWorkbench.ZemaxComparison/bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll')){
    $taskPath=Join-Path $taskRepo $taskRelative
    @{path=$taskPath;sha256=(Hash-TaskFile $taskPath)}
})
foreach($taskReplay in @($taskSummary,$taskControl)){
    if($taskReplay.coreAssemblySha256 -ne $taskAssemblies[0].sha256 -or $taskReplay.applicationAssemblySha256 -ne $taskAssemblies[1].sha256 -or $taskReplay.comparisonAssemblySha256 -ne $taskAssemblies[2].sha256){throw 'Final default output differs from numerical replay'}
}
$taskNativeValid=@($taskSummary.rows+$taskControl.rows | Where-Object {$_.nativeEnvironment.major -ne 26 -or $_.nativeEnvironment.minor -ne 1 -or !$_.nativeEnvironment.validLicense -or $_.nativeEnvironment.initializationErrors}).Count -eq 0
if(!$taskNativeValid){throw 'Invalid native reference environment'}
$taskFinalAuditPath=Join-Path $taskRoot 'tessar-ray-audit/tessar-final-v2-gq12-remove/coordinate-verification.json'
$taskFinalAudit=Get-Content $taskFinalAuditPath -Raw | ConvertFrom-Json
if($taskFinalAudit.inputs -ne 576 -or $taskFinalAudit.surfaceRayResults -ne 5184 -or $taskFinalAudit.missingOrNativeError -ne 0 -or !$taskFinalAudit.matchesCurrentDefaultAssemblies -or $taskFinalAudit.maxXYMillimeters -gt 2e-10){throw 'Final GQ12 audit not verified'}
foreach($taskFile in $taskFinalAudit.files){if((Hash-TaskFile (Join-Path (Split-Path $taskFinalAuditPath) $taskFile.path)) -ne $taskFile.sha256){throw 'Final native audit evidence changed'}}
$taskFirstMainPath=Join-Path $taskEvidence 'main-final-release.trx'
[xml]$taskFirstMain=Get-Content $taskFirstMainPath
$taskFirstCounter=$taskFirstMain.TestRun.ResultSummary.Counters
$taskFirstMainRecord=@{file=$taskFirstMainPath;sha256=(Hash-TaskFile $taskFirstMainPath);total=[int]$taskFirstCounter.total;passed=[int]$taskFirstCounter.passed;failed=[int]$taskFirstCounter.failed;skipped=[int]$taskFirstCounter.notExecuted;
  failures=@($taskFirstMain.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object {@{name=$_.testName;message=$_.Output.ErrorInfo.Message}})}
if($taskFirstMainRecord.passed -ne 4362 -or $taskFirstMainRecord.failed -ne 2 -or $taskFirstMainRecord.total -ne 4364){throw 'First complete run history differs'}
$taskFrozenDiff=git -C $taskRepo status --porcelain -- validation/history artifacts/zemax/123456-zemax-2026-r1-baseline
if($LASTEXITCODE -ne 0 -or $taskFrozenDiff){throw 'Frozen authority changed'}
$taskRecord=@{date='2026-10-07';completedUtc=[DateTimeOffset]::UtcNow.ToString('O');builds=@{Debug='Default full solution: 0 warnings/errors';Release='Default full solution: 0 warnings/errors'};
  tests=$taskTests;assemblies=$taskAssemblies;numericalCounts=$taskSummary.counts;previous84PassRegressions=0;previousStageRawUnchanged=$taskRawUnchanged;previousStageRawChanges=$taskRawChanges;
  originalMatrixSummary=$taskSummaryPath;originalMatrixSummarySha256=(Hash-TaskFile $taskSummaryPath);previousStageSummary=$taskPreviousPath;previousStageSummarySha256=(Hash-TaskFile $taskPreviousPath);
  rmsControlSummary=$taskControlPath;rmsControlSummarySha256=(Hash-TaskFile $taskControlPath);rmsControlCounts=$taskControl.counts;retainedEvidenceCounts=$taskRetainedCounts;fixtureFilesVerified=$taskFixtureCounts;
  sourceHashesVerified=$taskPlan.samples.Count;nativeEnvironmentsValid=$taskNativeValid;finalGq12Audit=@{file=$taskFinalAuditPath;sha256=(Hash-TaskFile $taskFinalAuditPath);verification=$taskFinalAudit};firstCompleteReleaseBeforeDedicatedReadThread=$taskFirstMainRecord;original132NativeRecaptured=$false;original132SettingsChanged=$false;original132TolerancesChanged=$false;frozenAuthorityChanged=$false;
  debugFullSuite='Not rerun: 2026-10-06 full Debug 4336/1/4337 remains historical; current RMS/GRIN targeted Debug 57/57.';
  laboratories='Not rerun: InitialStructure Release 258/2/260 and Coating Debug/Release 47/47 remain historical.';
  intermediateRuns='Earlier RMS targeted runs retained failed new assertions: finite Relay per-ray maximum measured 1.085e-7 mm, budget 2e-7 mm; retained-vignetting RA128 maximum residual 0.0087227 micrometer, fixed assertion .01 micrometer. Original comparison tolerances unchanged. Earlier full tool runs 154/2/156 and 162/2/164 retained; final-current result is authoritative.';
  previousUiCleanupFailure='Prior full Release UI Dispose NullReferenceException and isolated 16/16 result remain historical; this task does not modify or suppress the cleanup helper.';releaseGatePassed=$false}
$taskRecord | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $taskEvidence 'verification-final.json') -Encoding utf8NoBOM
$taskTests | ForEach-Object {'{0}: {1}/{2}/{3}' -f (Split-Path $_.file -Leaf),$_.passed,$_.failed,$_.total}
