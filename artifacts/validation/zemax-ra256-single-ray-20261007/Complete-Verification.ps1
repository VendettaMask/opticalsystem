$ErrorActionPreference='Stop'
$taskRepo='D:\Projects\opticalsystem'
$taskRoot=Join-Path $taskRepo 'artifacts/zemax-standard-samples/20261007'
$taskEvidence=Join-Path $taskRepo 'artifacts/validation/zemax-ra256-single-ray-20261007'
function Hash-TaskFile([string]$taskPath){(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant()}
function Read-TaskJson([string]$taskPath){Get-Content -LiteralPath $taskPath -Raw | ConvertFrom-Json -DateKind String -AsHashtable}
$taskTests=@(foreach($taskName in @('main-current-release.trx','tool-current-release.trx','main-targeted-release.trx','main-targeted-debug.trx','tool-targeted-debug-final.trx')){
    $taskPath=Join-Path $taskEvidence $taskName
    [xml]$taskTrx=Get-Content -LiteralPath $taskPath
    $taskCounter=$taskTrx.TestRun.ResultSummary.Counters
    @{file=$taskPath;sha256=(Hash-TaskFile $taskPath);total=[int]$taskCounter.total;passed=[int]$taskCounter.passed;failed=[int]$taskCounter.failed;executed=[int]$taskCounter.executed;skipped=[int]$taskCounter.notExecuted;
      failures=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object {@{name=$_.testName;message=$_.Output.ErrorInfo.Message}});
      newTests=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object {$_.testName -match 'SingleRayPathParityTests|AutomaticStopDiamDoesNotClipTheEstimatedRayFootprint|CapturedRa256SamplingTests|JsonFileStreamingTests'} | ForEach-Object {@{name=$_.testName;outcome=$_.outcome}})}
})
$taskMain=$taskTests | Where-Object {$_.file.EndsWith('main-current-release.trx')}
$taskTool=$taskTests | Where-Object {$_.file.EndsWith('tool-current-release.trx')}
if($taskMain.total -ne 4384 -or $taskMain.passed -ne 4383 -or $taskMain.failed -ne 1 -or $taskMain.newTests.Count -ne 20){throw 'Unexpected formal test baseline'}
if(@($taskMain.failures | Where-Object {$_.name -notmatch 'FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays'}).Count){throw 'Unreviewed formal test failure'}
if($taskTool.total -ne 180 -or $taskTool.passed -ne 178 -or $taskTool.failed -ne 2 -or $taskTool.newTests.Count -ne 16){throw 'Unexpected tool test baseline'}
$taskOldV=Read-TaskJson (Join-Path $taskRepo 'artifacts/validation/zemax-tessar-rms-20261007/verification-final.json')
$taskOldTool=$taskOldV.tests | Where-Object {$_.file.EndsWith('tool-final-current-release.trx')}
foreach($taskFailure in $taskTool.failures){
    $taskKnown=@($taskOldTool.failures | Where-Object {$_.name -eq $taskFailure.name -and $_.message -eq $taskFailure.message})
    if($taskKnown.Count -ne 1){throw 'Comparison-tool failure changed; inspect rather than classify as existing'}
}
foreach($taskTest in $taskTests){
    if($taskTest.skipped -ne 0 -or $taskTest.executed -ne $taskTest.total -or @($taskTest.newTests | Where-Object outcome -ne 'Passed').Count){throw 'Incomplete new test verification'}
    if($taskTest.file -match 'main-targeted' -and ($taskTest.passed -ne 153 -or $taskTest.failed -ne 0)){throw 'Incomplete formal targeted check'}
    if($taskTest.file.EndsWith('tool-targeted-debug-final.trx') -and ($taskTest.passed -ne 54 -or $taskTest.failed -ne 0)){throw 'Incomplete tool targeted Debug check'}
}
$taskAssemblies=@(foreach($taskRelative in @('src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll','src/OptilandWorkbench.Application/bin/Release/net10.0/OptilandWorkbench.Application.dll','tools/OptilandWorkbench.ZemaxComparison/bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll')){
    $taskPath=Join-Path $taskRepo $taskRelative
    @{path=$taskPath;sha256=(Hash-TaskFile $taskPath)}
})
$taskReplays=@{}
$taskRetainedCounts=@{}
foreach($taskName in @('old-snapshot-original132','fresh-import-original132','old-snapshot-controls17','fresh-import-controls17','old-snapshot-controls6','fresh-import-controls6')){
    $taskDirectory=Join-Path $taskRoot "ra256-single-ray-$taskName"
    $taskReplay=Read-TaskJson (Join-Path $taskDirectory 'summary.json')
    if(!$taskReplay.allRetainedEvidenceUnchanged -or $taskReplay.settingsChanged -or $taskReplay.tolerancesChanged -or $taskReplay.nativeRecaptured -or $taskReplay.frozenBaselineChanged){throw 'Native evidence/settings changed'}
    if($taskReplay.coreAssemblySha256 -ne $taskAssemblies[0].sha256 -or $taskReplay.applicationAssemblySha256 -ne $taskAssemblies[1].sha256 -or $taskReplay.comparisonAssemblySha256 -ne $taskAssemblies[2].sha256){throw 'Replay does not use current default assemblies'}
    $taskHashes=Read-TaskJson (Join-Path $taskDirectory 'immutable-evidence.json')
    foreach($taskItem in $taskHashes.GetEnumerator()){if((Hash-TaskFile $taskItem.Key) -ne $taskItem.Value){throw "Immutable evidence changed: $($taskItem.Key)"}}
    $taskRetainedCounts[$taskName]=$taskHashes.Count
    foreach($taskRow in $taskReplay.rows){
        if($taskRow.requestChanged -or $taskRow.tolerancesChanged -or $taskRow.nativeRecaptured){throw 'Original request scope changed'}
        if($taskRow.nativeEnvironment.major -ne 26 -or $taskRow.nativeEnvironment.minor -ne 1 -or !$taskRow.nativeEnvironment.validLicense -or $taskRow.nativeEnvironment.initializationErrors){throw 'Invalid native environment'}
        foreach($taskPair in @(@('currentWorkbenchRaw','currentWorkbenchRawSha256'),@('nativeRaw','nativeRawSha256'),@('snapshot','snapshotSha256'),@('executionSnapshot','executionSnapshotSha256'),@('source','sourceSha256'))){
            if($taskRow[$taskPair[1]] -and (Hash-TaskFile $taskRow[$taskPair[0]]) -ne $taskRow[$taskPair[1]]){throw "Replay output changed: $taskName/$($taskRow.lens)/$($taskRow.key)"}
        }
        $taskComparison=Read-TaskJson $taskRow.currentComparison
        if($taskComparison.conclusion -ne $taskRow.currentConclusion){throw 'Conclusion differs from actual comparison'}
    }
    if($taskName -match 'controls(17|6)$' -and ($taskReplay.total -ne [int]$Matches[1] -or $taskReplay.counts.Pass -ne $taskReplay.total)){throw 'Independent controls incomplete'}
    $taskReplays[$taskName]=$taskReplay
}
$taskOld=$taskReplays['old-snapshot-original132']
if($taskOld.total -ne 132 -or $taskOld.counts.Pass -ne 95 -or $taskOld.counts.Close -ne 12 -or $taskOld.counts.Difference -ne 16 -or $taskOld.counts.Incomparable -ne 8 -or $taskOld.counts.Error -ne 1){throw 'Unexpected unchanged-snapshot matrix'}
$taskPrevious=Read-TaskJson (Join-Path $taskRoot 'tessar-rms-repair-final-v2-original132/summary.json')
$taskRawUnchanged=0
$taskChanges=@()
foreach($taskRow in $taskOld.rows){
    $taskPrior=@($taskPrevious.rows | Where-Object {$_.batch -eq $taskRow.batch -and $_.lens -eq $taskRow.lens -and $_.key -eq $taskRow.key})
    if($taskPrior.Count -ne 1){throw 'Unmatched original matrix row'}
    $taskPrior=$taskPrior[0]
    if($taskPrior.currentConclusion -eq 'Pass' -and $taskRow.currentConclusion -ne 'Pass'){throw 'Previous Pass regressed'}
    if($taskRow.nativeRequestFingerprint -ne $taskPrior.nativeRequestFingerprint){throw 'Original native fingerprint changed'}
    if($taskPrior.currentWorkbenchRawSha256){
        if((Hash-TaskFile $taskPrior.currentWorkbenchRaw) -ne $taskPrior.currentWorkbenchRawSha256){throw 'Historical replay modified'}
        if($taskPrior.currentWorkbenchRawSha256 -eq $taskRow.currentWorkbenchRawSha256){$taskRawUnchanged++}
        else {$taskChanges+=@{lens=$taskRow.lens;key=$taskRow.key;before=$taskPrior.currentConclusion;after=$taskRow.currentConclusion}}
    }
}
if($taskRawUnchanged -ne 121 -or $taskChanges.Count -ne 10 -or @($taskChanges | Where-Object key -ne 'Single Ray Trace').Count){throw 'Unexpected path-fix scope'}
$taskFreshChanges=@()
$taskFreshRegressions=@()
foreach($taskRow in $taskReplays['fresh-import-original132'].rows){
    $taskPrior=@($taskOld.rows | Where-Object {$_.batch -eq $taskRow.batch -and $_.lens -eq $taskRow.lens -and $_.key -eq $taskRow.key})[0]
    if($taskPrior.currentConclusion -eq 'Pass' -and $taskRow.currentConclusion -ne 'Pass'){$taskFreshRegressions+=$taskRow}
    if($taskPrior.currentWorkbenchRawSha256 -ne $taskRow.currentWorkbenchRawSha256){$taskFreshChanges+=@{lens=$taskRow.lens;key=$taskRow.key;before=$taskPrior.currentConclusion;after=$taskRow.currentConclusion}}
}
if($taskFreshRegressions.Count){throw 'Fresh source import regressed an existing Pass'}
$taskAudits=@()
foreach($taskCase in @('tessar-ra256-remove-streamed','tessar-ra256-retain','cooke-40-degree-field-ra256-remove','double-gauss-28-degree-field-ra256-remove','relay-lens-ra256-remove','even-asphere-ra256-remove')){
    $taskDirectory=Join-Path $taskRoot "ra256-ray-audit/$taskCase"
    $taskBefore=Read-TaskJson (Join-Path $taskDirectory 'verification.json')
    $taskAfter=Read-TaskJson (Join-Path $taskDirectory 'verification-import-source.json')
    if($taskAfter.inputs -ne 51468 -or $taskAfter.nativeErrors -ne 0 -or $taskAfter.acceptanceDifferences -ne 0 -or $taskAfter.firstClipDifferences -ne 0 -or $taskAfter.physicalMisses -ne 0 -or $taskAfter.virtualMissingOrNativeErrors -ne 0 -or $taskAfter.maximumPhysicalCoordinateMillimeters -gt 2e-7){throw 'Incomplete corrected-source ray audit'}
    if($taskAfter.currentCoreAssemblySha256 -ne $taskAssemblies[0].sha256 -or $taskAfter.currentToolAssemblySha256 -ne $taskAssemblies[2].sha256){throw 'Ray audit uses obsolete assemblies'}
    foreach($taskFile in $taskAfter.files){if((Hash-TaskFile (Join-Path $taskDirectory $taskFile.path)) -ne $taskFile.sha256){throw 'Native ray evidence changed'}}
    if((Hash-TaskFile (Join-Path $taskDirectory 'current-import-snapshot.json')) -ne $taskAfter.currentImportSnapshotSha256){throw 'Fresh imported ray model changed'}
    $taskAudits+=@{case=$taskCase;directory=$taskDirectory;before=$taskBefore;after=$taskAfter}
}
$taskFixtureCounts=@{}
foreach($taskName in @('rms-field-sampling-2026-10-07','tessar-ray-audit-2026-10-07','ra256-field-sampling-2026-10-07','single-ray-path-2026-10-07')){
    $taskDirectory=Join-Path $taskRepo "validation/zemax/2026-r1/$taskName"
    $taskFixture=Read-TaskJson (Join-Path $taskDirectory 'manifest.json')
    foreach($taskFile in $taskFixture.files){if((Hash-TaskFile (Join-Path $taskDirectory $taskFile.path)) -ne $taskFile.sha256){throw 'Frozen fixture changed'}}
    $taskFixtureCounts[$taskName]=$taskFixture.files.Count
}
$taskPlan=Read-TaskJson (Join-Path $taskRepo 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json')
foreach($taskSample in $taskPlan.samples){if((Hash-TaskFile $taskSample.sourcePath) -ne $taskSample.sourceSha256){throw 'Official source changed'}}
$taskFrozenDiff=git -C $taskRepo status --porcelain -- validation/history artifacts/zemax/123456-zemax-2026-r1-baseline
if($LASTEXITCODE -ne 0 -or $taskFrozenDiff){throw 'Frozen authority changed'}
$taskRecord=@{
    date='2026-10-07';completedUtc=[DateTimeOffset]::UtcNow.ToString('O');builds=@{Debug='Default full solution: 0 warnings/errors';Release='Default full solution: 0 warnings/errors'};
    tests=$taskTests;assemblies=$taskAssemblies;oldSnapshotCounts=$taskOld.counts;freshImportCounts=$taskReplays['fresh-import-original132'].counts;
    previous85PassRegressions=0;oldSnapshotRawUnchanged=$taskRawUnchanged;oldSnapshotRawChanges=$taskChanges;freshImportPreviousPassRegressions=0;freshImportRawChanges=$taskFreshChanges;
    retainedEvidenceCounts=$taskRetainedCounts;fixtureFilesVerified=$taskFixtureCounts;sourceHashesVerified=$taskPlan.samples.Count;
    rayAuditInputs=($taskAudits.after.inputs | Measure-Object -Sum).Sum;rayAuditSurfaceResults=($taskAudits.after.surfaceRayResults | Measure-Object -Sum).Sum;rayAudits=$taskAudits;
    controls=@{oldSnapshotPass=23;freshImportPass=23;previous17AndNew6CountedSeparately=$true};
    original132NativeRecaptured=$false;original132SettingsChanged=$false;original132TolerancesChanged=$false;original132SnapshotRewritten=$false;frozenAuthorityChanged=$false;
    debugFullSuite='Not rerun: 2026-10-06 full Debug 4336/1/4337 remains historical; current formal targeted Debug 153/153 and tool targeted Debug 54/54.';
    laboratories='Not rerun: InitialStructure Release 258/2/260 and Coating Debug/Release 47/47 remain historical.';
    firstRa256Failure=Read-TaskJson (Join-Path $taskRoot 'ra256-ray-audit/tessar-ra256-remove/failure.json');
    earlierTargetedFailures='Initial Single Ray path Debug 23/3/26 retained: table has six-decimal precision; zero finite object/off-axis test was geometrically degenerate. Corrected tests preserve product/reference budgets. Initial fresh-import six-case run passed but its misspelled Tessar case selector skipped the additional fine residual assertion; final full tool suite uses the corrected selector. Initial unquoted PowerShell TRX argument gave six passing aperture tests plus a shell exit error; final 153-case checks and full suite supersede it.';
    releaseGatePassed=$false
}
$taskRecord.modelImportDifferences=Read-TaskJson (Join-Path $taskEvidence 'model-import-differences.json')
$taskRecord.original132ModelImportDifferences=Read-TaskJson (Join-Path $taskEvidence 'model-original132-import-differences.json')
if(!$taskRecord.modelImportDifferences.allOtherPropertiesUnchanged -or !$taskRecord.original132ModelImportDifferences.allOtherPropertiesUnchanged){throw 'Unexpected model import scope'}
if(Test-Path -LiteralPath (Join-Path $taskEvidence 'verification-final.json')){throw 'Do not overwrite completed verification'}
$taskRecord | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $taskEvidence 'verification-final.json') -Encoding utf8NoBOM
$taskTests | ForEach-Object {'{0}: {1}/{2}/{3}' -f (Split-Path $_.file -Leaf),$_.passed,$_.failed,$_.total}
