#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$repository = [IO.Path]::GetFullPath((Join-Path $taskRoot '../../../..'))
$names = @('portability-ui-focused', 'background-acceptance', 'identity-file-roundtrip')
$unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$runs = foreach ($name in $names) {
    [xml]$trx = [IO.File]::ReadAllText((Join-Path $taskRoot ($name + '.trx')))
    $cases = @($trx.TestRun.Results.UnitTestResult)
    if ($cases.Count -eq 0 -or @($cases | Where-Object outcome -ne 'Passed').Count -ne 0) {
        throw ('Incomplete or failed test results: ' + $name)
    }
    foreach ($case in $cases) { [void]$unique.Add($case.testName) }
    [ordered]@{ name = $name; passed = $cases.Count; total = [int]$trx.TestRun.ResultSummary.Counters.total;
        tests = @($cases.testName); sha256 = (Get-FileHash -LiteralPath (Join-Path $taskRoot ($name + '.trx'))).Hash.ToLowerInvariant() }
}
$sources = @(
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyValidator.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/HistoricalTopologyProtocols.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousSearchService.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/TopologyProtocolPortabilityTests.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/FlatStartDesktopTests.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/TopologySamplingPrecisionTests.cs',
    'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.cs',
    'src/OpticalSystemDesign.App/Laboratories/InitialStructure/MainWindow.Inputs.cs',
    'src/OpticalSystemDesign.App/Services/UiLocalization.cs')
$binaries = @(
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll',
    'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll')
function Hashes($paths) {
    foreach ($path in $paths) {
        [ordered]@{ path = $path; sha256 = (Get-FileHash -LiteralPath (Join-Path $repository $path)).Hash.ToLowerInvariant() }
    }
}
$fixture = 'validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/run.autonomous.json'
Push-Location $repository
try {
    $committedFixture = git rev-parse ('HEAD:' + $fixture)
    $workingFixture = git hash-object $fixture
    if ($committedFixture -ne $workingFixture) { throw 'The original historical checkpoint changed.' }
    $report = [ordered]@{
        sourceHead = (git rev-parse HEAD); uncommittedChanges = $true
        mode = 'background Avalonia.Headless/Skia and pure C# CPU tests; two processors, BelowNormal'
        testRuns = @($runs); uniqueTestCount = $unique.Count
        sourceHashes = @(Hashes $sources); binaryHashes = @(Hashes $binaries)
        originalCheckpoint = [ordered]@{ path = $fixture; unchangedFromHead = $true; gitBlob = $workingFixture }
        offscreenRenders = @(Hashes @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'headless') -Filter '*.png' |
            ForEach-Object { [IO.Path]::GetRelativePath($repository, $_.FullName).Replace('\', '/') }))
        nativeInteractiveAcceptance = 'incomplete; interrupted by user Escape; foreground validation prohibited'
        postFixLinuxMacExecution = 'not run locally; existing three-platform module CI includes tests'
        opticalAcceptanceLimits = 'unchanged'; coreNumericalChanges = $false
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRoot 'verification.json') -Encoding utf8
    [pscustomobject]@{ TestRuns = $runs.Count; UniquePassingTests = $unique.Count; OriginalCheckpointUnchanged = $true }
}
finally { Pop-Location }
