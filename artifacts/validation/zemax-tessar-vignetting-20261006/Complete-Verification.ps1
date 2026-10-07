$ErrorActionPreference = 'Stop'
$taskRepo = 'D:\Projects\opticalsystem'
$taskEvidence = Join-Path $taskRepo 'artifacts\validation\zemax-tessar-vignetting-20261006'
$taskReplayRoot = Join-Path $taskRepo 'artifacts\zemax-standard-samples\20261006'
function Hash-TaskFile([string]$taskPath) { (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$taskTests = @()
foreach ($taskName in @('main-release.trx','main-debug.trx','comparison-release.trx','tessar-targeted-final.trx','tool-targeted.trx')) {
    $taskPath = Join-Path $taskEvidence $taskName
    [xml]$taskTrx = Get-Content -LiteralPath $taskPath
    $taskCounter = $taskTrx.TestRun.ResultSummary.Counters
    $taskTests += [ordered]@{file=$taskPath; sha256=(Hash-TaskFile $taskPath);total=[int]$taskCounter.total;passed=[int]$taskCounter.passed;failed=[int]$taskCounter.failed;executed=[int]$taskCounter.executed;skipped=[int]$taskCounter.notExecuted;
        failures=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object { @{name=$_.testName;message=$_.Output.ErrorInfo.Message;outcome=$_.outcome} });
        newVignettingTests=@($taskTrx.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match 'TessarVignettingParityTests|VignettedWavefrontPhaseTests|VignettedFanContractTests' } | ForEach-Object { @{name=$_.testName;outcome=$_.outcome} })}
}
foreach ($taskTest in $taskTests | Where-Object { $_.file -match 'main-(release|debug)\.trx$' }) {
    if ($taskTest.total -ne 4337 -or $taskTest.executed -ne 4337 -or $taskTest.skipped -ne 0 -or $taskTest.newVignettingTests.Count -ne 30 -or @($taskTest.newVignettingTests | Where-Object outcome -ne 'Passed').Count) { throw 'Formal test scope incomplete' }
}
$taskSummaryPath = Join-Path $taskReplayRoot 'tessar-vignetting-repair-final\summary.json'
$taskSummary = Get-Content -LiteralPath $taskSummaryPath -Raw | ConvertFrom-Json
$taskPreviousPath = Join-Path $taskReplayRoot 'opd-core-repair-v2\summary.json'
$taskPrevious = Get-Content -LiteralPath $taskPreviousPath -Raw | ConvertFrom-Json
if ($taskSummary.total -ne 132 -or $taskSummary.counts.Pass -ne 84 -or $taskSummary.originalPassRegressions -ne 0) { throw 'Unexpected numerical matrix' }
$taskStageChanges=@(); $taskPreviousPassRegressions=@()
foreach ($taskRow in $taskSummary.rows) {
    $taskPrior = @($taskPrevious.rows | Where-Object { $_.batch -eq $taskRow.batch -and $_.lens -eq $taskRow.lens -and $_.key -eq $taskRow.key })
    if ($taskPrior.Count -ne 1) { throw 'Ambiguous previous-stage identity' }
    if ($taskPrior[0].currentConclusion -eq 'Pass' -and $taskRow.currentConclusion -ne 'Pass') { $taskPreviousPassRegressions += $taskRow }
    if ($taskPrior[0].currentConclusion -ne $taskRow.currentConclusion) { $taskStageChanges += @{batch=$taskRow.batch;lens=$taskRow.lens;key=$taskRow.key;previous=$taskPrior[0].currentConclusion;current=$taskRow.currentConclusion} }
}
if ($taskPreviousPassRegressions.Count -ne 0 -or $taskStageChanges.Count -ne 2) { throw 'Unexpected prior-stage regression' }
$taskPreviousRawCount = 0
foreach ($taskPreviousRow in $taskPrevious.rows) {
    if ($taskPreviousRow.currentWorkbenchRawSha256) {
        if ((Hash-TaskFile $taskPreviousRow.currentWorkbenchRaw) -ne $taskPreviousRow.currentWorkbenchRawSha256) { throw 'Previous-stage raw computation changed' }
        $taskPreviousRawCount++
    }
}
$taskControlPath = Join-Path $taskReplayRoot 'tessar-controls-recalculated\summary.json'
$taskControl = Get-Content -LiteralPath $taskControlPath -Raw | ConvertFrom-Json
if ($taskControl.total -ne 8 -or $taskControl.counts.Pass -ne 4 -or $taskControl.counts.Incomparable -ne 4) { throw 'Unexpected native control scope' }
$taskRetainedCounts = @{}
foreach ($taskIntegrity in @('opd-core-repair-v2\immutable-evidence.json','tessar-vignetting-repair-final\immutable-evidence.json','tessar-controls-recalculated\immutable-evidence.json')) {
    $taskFiles = Get-Content -LiteralPath (Join-Path $taskReplayRoot $taskIntegrity) -Raw | ConvertFrom-Json
    foreach ($taskFile in $taskFiles.PSObject.Properties) {
        if ((Hash-TaskFile $taskFile.Name) -ne $taskFile.Value) { throw ('Immutable evidence changed: ' + $taskFile.Name) }
    }
    $taskRetainedCounts[$taskIntegrity] = @($taskFiles.PSObject.Properties).Count
}
$taskPlan = Get-Content -LiteralPath (Join-Path $taskRepo 'docs\validation\ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json') -Raw | ConvertFrom-Json
foreach ($taskSample in $taskPlan.samples) { if ((Hash-TaskFile $taskSample.sourcePath) -ne $taskSample.sourceSha256) { throw 'Standard sample source changed' } }
$taskAssemblies = @()
foreach ($taskRelative in @('src\OptilandWorkbench.Core\bin\Release\net10.0\OptilandWorkbench.Core.dll','src\OptilandWorkbench.Application\bin\Release\net10.0\OptilandWorkbench.Application.dll','tools\OptilandWorkbench.ZemaxComparison\bin\Release\net10.0\OptilandWorkbench.ZemaxComparison.dll')) {
    $taskAssemblyPath = Join-Path $taskRepo $taskRelative
    $taskAssemblies += @{path=$taskAssemblyPath;sha256=(Hash-TaskFile $taskAssemblyPath)}
}
if ($taskAssemblies[0].sha256 -ne $taskSummary.coreAssemblySha256 -or $taskAssemblies[1].sha256 -ne $taskSummary.applicationAssemblySha256 -or $taskAssemblies[2].sha256 -ne $taskSummary.comparisonAssemblySha256) { throw 'Final default assemblies differ from replay' }
$taskFixtures = @{}
Get-ChildItem -LiteralPath (Join-Path $taskRepo 'validation\zemax\2026-r1\tessar-vignetting-2026-10-06') -File -Recurse | ForEach-Object { $taskFixtures[$_.FullName] = Hash-TaskFile $_.FullName }
$taskRecord = [ordered]@{date='2026-10-06';completedUtc=[DateTimeOffset]::UtcNow.ToString('O');
    builds=@{Debug='Default product solution output: zero warnings/errors';Release='Default product solution output: zero warnings/errors'};
    tests=$taskTests;assemblies=$taskAssemblies;numericalCounts=$taskSummary.counts;previousStageCounts=$taskPrevious.counts;previousStageChanges=$taskStageChanges;previous82PassRegressions=0;original76PassRegressions=0;
    replaySummary=$taskSummaryPath;replaySummarySha256=(Hash-TaskFile $taskSummaryPath);previousStageSummary=$taskPreviousPath;previousStageSummarySha256=(Hash-TaskFile $taskPreviousPath);previousStageRawOutputsVerified=$taskPreviousRawCount;nativeControlSummary=$taskControlPath;nativeControlSummarySha256=(Hash-TaskFile $taskControlPath);nativeControlCounts=$taskControl.counts;
    retainedEvidenceCounts=$taskRetainedCounts;retainedEvidenceUnchanged=$true;standardSourceHashesVerified=$taskPlan.samples.Count;fixtureFileHashes=$taskFixtures;
    allNativeEnvironmentsValid=@(($taskSummary.rows + $taskControl.rows) | Where-Object { $_.nativeEnvironment.major -ne 26 -or $_.nativeEnvironment.minor -ne 1 -or $_.nativeEnvironment.opticStudioVersion -ne 260127 -or -not $_.nativeEnvironment.validLicense -or $_.nativeEnvironment.initializationErrors }).Count -eq 0;
    original132SettingsChanged=$false;original132TolerancesChanged=$false;original132NativeRecaptured=$false;additionalNativeControlComparisons=8;frozenBaselineChanged=$false;historicalAuxiliaryDataChanged=$false;
    intermediateRuns=@('phase-targeted.trx and phase-fixture-diagnostic.trx: initial unequal-launch-plane test fixture, corrected without loosening assertion','tessar-targeted.trx: initial fixture column/key selection, corrected to strict active native column');
    laboratories='Not rerun in this wavefront/fan change; prior full scope retained at InitialStructure 258/2/260 and Coating Debug/Release 47/47';releaseGatePassed=$false}
if (-not $taskRecord.allNativeEnvironmentsValid) { throw 'Invalid native reference environment' }
$taskRecord | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath (Join-Path $taskEvidence 'verification-final.json') -Encoding utf8NoBOM
$taskTests | ForEach-Object { Write-Output ('{0}: {1}/{2}/{3}' -f (Split-Path $_.file -Leaf),$_.passed,$_.failed,$_.total) }
