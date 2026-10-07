$ErrorActionPreference = 'Stop'
$repoRoot = 'D:\Projects\opticalsystem'
$evidenceRoot = Join-Path $repoRoot 'artifacts/validation/zemax-standard-sample-opd-20261006'
$testResults = @()
foreach ($file in @('main-release.trx','main-debug.trx','opd-targeted-final.trx','debug-timeouts-isolated.trx','comparison-release.trx','initial-structure-release.trx','coating-debug.trx','coating-release.trx')) {
    $path = Join-Path $evidenceRoot $file
    [xml]$trx = Get-Content -LiteralPath $path
    $counter = $trx.TestRun.ResultSummary.Counters
    $testResults += [ordered]@{
        file=$path; sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        total=[int]$counter.total; executed=[int]$counter.executed; passed=[int]$counter.passed
        failed=[int]$counter.failed; skipped=[int]$counter.notExecuted
        failures=@($trx.TestRun.Results.UnitTestResult | Where-Object outcome -ne 'Passed' | ForEach-Object {
            [ordered]@{name=$_.testName; outcome=$_.outcome; message=$_.Output.ErrorInfo.Message}
        })
        newOpdTests=@($trx.TestRun.Results.UnitTestResult | Where-Object testName -like '*StandardSampleOpdParityTests*' | ForEach-Object {
            [ordered]@{name=$_.testName; outcome=$_.outcome}
        })
    }
}
foreach ($configuration in @('main-release.trx','main-debug.trx')) {
    $test = $testResults | Where-Object { $_.file.EndsWith($configuration) }
    if ($test.total -ne 4307 -or $test.executed -ne 4307 -or $test.skipped -ne 0 -or
        $test.newOpdTests.Count -ne 12 -or @($test.newOpdTests | Where-Object outcome -ne 'Passed').Count) {
        throw "Incomplete formal verification: $configuration"
    }
}
$replayPath = Join-Path $repoRoot 'artifacts/zemax-standard-samples/20261006/opd-core-repair-v2/summary.json'
$replay = Get-Content -LiteralPath $replayPath -Raw | ConvertFrom-Json
if ($replay.total -ne 132 -or $replay.originalPassRegressions -ne 0 -or $replay.counts.Pass -ne 82) {
    throw 'Unexpected replay scope or numerical regressions'
}
$integrityPath = Join-Path $repoRoot 'artifacts/zemax-standard-samples/20261006/opd-core-repair-v2/immutable-evidence.json'
$integrity = Get-Content -LiteralPath $integrityPath -Raw | ConvertFrom-Json
foreach ($entry in $integrity.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath $entry.Name -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) {
        throw "Retained evidence changed: $($entry.Name)"
    }
}
$planPath = Join-Path $repoRoot 'docs/validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json'
$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
foreach ($sample in $plan.samples) {
    if ((Get-FileHash -LiteralPath $sample.sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sample.sourceSha256) {
        throw "Original standard sample changed: $($sample.sourcePath)"
    }
}
$assemblies = @()
foreach ($file in @('src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.Application/bin/Release/net10.0/OptilandWorkbench.Application.dll',
    'tools/OptilandWorkbench.ZemaxComparison/bin/Release/net10.0/OptilandWorkbench.ZemaxComparison.dll',
    'artifacts/zemax-standard-samples/20261006/core-replay/bin/Release/net10.0/Replay.dll')) {
    $path = Join-Path $repoRoot $file
    $assemblies += [ordered]@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
}
if ($assemblies[0].sha256 -ne $replay.coreAssemblySha256 -or
    $assemblies[1].sha256 -ne $replay.applicationAssemblySha256 -or
    $assemblies[2].sha256 -ne $replay.comparisonAssemblySha256) {
    throw 'Default formal Release assemblies differ from replay assemblies'
}
$record = [ordered]@{
    date='2026-10-06'; completedUtc=[DateTimeOffset]::UtcNow.ToString('O')
    productBuilds=[ordered]@{Debug='Default solution output, zero warnings/errors';Release='Default solution output, zero warnings/errors'}
    tests=$testResults; assemblies=$assemblies
    replaySummary=$replayPath; replaySummarySha256=(Get-FileHash -LiteralPath $replayPath -Algorithm SHA256).Hash.ToLowerInvariant()
    numericalCounts=$replay.counts; selectedComparisons=132; originalPassRegressions=0
    nativeVersions=@($replay.rows.nativeEnvironment | Select-Object major,minor,opticStudioVersion,licenseStatus,validLicense,initializationErrors -Unique)
    allNativeEnvironmentsValid=@($replay.rows | Where-Object { $_.nativeEnvironment.major -ne 26 -or $_.nativeEnvironment.minor -ne 1 -or $_.nativeEnvironment.opticStudioVersion -ne 260127 -or -not $_.nativeEnvironment.validLicense -or $_.nativeEnvironment.initializationErrors }).Count -eq 0
    retainedEvidenceFiles=@($integrity.PSObject.Properties).Count; retainedEvidenceUnchanged=$true
    standardSourceHashesVerified=$plan.samples.Count; tolerancesChanged=$false; settingsChanged=$false
    nativeRecaptured=$false; frozenBaselineChanged=$false; historicalAuxiliaryDataChanged=$false
    rejectedAuxiliaryRun='artifacts/zemax-standard-samples/20261006/opd-core-repair/INVALID_RUN.json'
    intermediateRuns=@('opd-targeted.trx: initial test construction; not final verification')
    releaseGatePassed=$false
}
$record | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'verification-final.json') -Encoding utf8
$testResults | ForEach-Object { "$(Split-Path $_.file -Leaf): $($_.passed)/$($_.failed)/$($_.total)" }
