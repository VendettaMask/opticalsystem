$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\Projects\opticalsystem'
$taskEvidence = $PSScriptRoot
$taskReports = @('product-release', 'product-debug', 'comparison-release', 'initial-structure-release', 'coating-release', 'coating-debug')
$taskSuites = foreach ($taskReport in $taskReports) {
    $taskPath = Join-Path $taskEvidence ($taskReport + '.trx')
    [xml]$taskXml = Get-Content -LiteralPath $taskPath -Raw
    $taskNs = [System.Xml.XmlNamespaceManager]::new($taskXml.NameTable)
    $taskNs.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $taskCounters = $taskXml.SelectSingleNode('//t:Counters', $taskNs)
    $taskResults = $taskXml.SelectNodes('//t:UnitTestResult', $taskNs)
    $taskFailures = @($taskResults | Where-Object { $_.outcome -eq 'Failed' } | ForEach-Object {
        @{ name = $_.testName; message = $_.Output.ErrorInfo.Message }
    })
    $taskNew = @($taskResults | Where-Object {
        ($_.testName -like '*CommercialReliabilityTests*') -or
        ($_.testName -like '*UiAuditRegressionTests.HeadlessSessionWaitsForAsyncCallbacks*') -or
        ($_.testName -like '*UiAuditRegressionTests.HeadlessSessionPropagatesAsyncCallbackFailures*')
    })
    @{ name = $taskReport; total = [int]$taskCounters.total; passed = [int]$taskCounters.passed;
       failed = [int]$taskCounters.failed; notExecuted = [int]$taskCounters.notExecuted;
       failures = $taskFailures; addedPassed = @($taskNew | Where-Object { $_.outcome -eq 'Passed' }).Count;
       addedTotal = $taskNew.Count; trx = $taskPath }
}
$taskSources = @('src/OptilandWorkbench.Core/Analysis/AnalysisResourceLimits.cs',
    'src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs',
    'src/OptilandWorkbench.Core/NonSequential/NonSequentialRayDatabase.cs',
    'src/OptilandWorkbench.Core/Services/IlluminationMetrics.cs',
    'src/OptilandWorkbench.Core/Analysis/Extended/ImageAndPolarizationAnalyses.cs',
    'src/OptilandWorkbench.Core/Analysis/Extended/ExtendedSceneImageAnalyses.cs',
    'src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.cs',
    'tests/OptilandWorkbench.Tests/CommercialReliabilityTests.cs',
    'tests/OptilandWorkbench.Tests/InterferogramFooterTests.cs',
    'tests/OptilandWorkbench.Tests/OpticCapabilityPreflightTests.cs',
    'tests/OptilandWorkbench.Tests/WavefrontSurfaceRenderTests.cs',
    'tests/OptilandWorkbench.Tests/UiAuditRegressionTests.cs') | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $taskRoot $_) -Algorithm SHA256).Hash }
    }
$taskBaseline = '949623b70224df249185d3b5d458288f3107d049'
$taskVerification = @{ date = '2026-10-06'; baseline = $taskBaseline;
    builds = @{ formalDebugWarnings = 0; formalDebugErrors = 0; formalReleaseWarnings = 0; formalReleaseErrors = 0 };
    suites = @($taskSuites); sourceFiles = @($taskSources);
    numericalThresholdsChanged = $false; frozenReferencesChanged = $false }
[System.IO.File]::WriteAllText((Join-Path $taskEvidence 'verification.json'),
    ($taskVerification | ConvertTo-Json -Depth 10), [System.Text.UTF8Encoding]::new($false))
$taskSuites | Select-Object name, passed, failed, total, addedPassed, addedTotal | Format-Table -AutoSize
