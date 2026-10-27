param(
    [Parameter(Mandatory)][int]$SourceFormatExitCode,
    [Parameter(Mandatory)][int]$FormatVerifyExitCode,
    [Parameter(Mandatory)][int]$DocumentationExitCode
)
$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path (Join-Path $PSScriptRoot '../../../..'))
try {
    if ($SourceFormatExitCode -ne 0 -or $FormatVerifyExitCode -ne 0 -or $DocumentationExitCode -ne 0) {
        throw 'A required check did not pass.'
    }
    $taskLatest = @{}
    $taskAttempts = foreach ($taskName in @('controls-first.trx', 'controls-render-final.trx')) {
        [xml]$taskXml = Get-Content -LiteralPath (Join-Path $PSScriptRoot $taskName) -Raw
        $taskCounters = $taskXml.TestRun.ResultSummary.Counters
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{
                Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskName
            }
        }
        @{ File = $taskName; Passed = [int]$taskCounters.passed; Failed = [int]$taskCounters.failed; Total = [int]$taskCounters.total }
    }
    if ($taskLatest.Count -ne 10 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) {
        throw 'Expected ten different tests with latest results all passed.'
    }
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskSources = @(
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
        'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.RecoveryControls.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.Accounting.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/AutonomousSearchModels.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologyRestorationModels.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousDesignExamples.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md', 'labs/InitialStructure/README.md'
    )
    $taskDefaultOutput = 'src/OpticalSystemDesign.App/bin/Release/net10.0/'
    $taskTestOutput = 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/'
    $taskBinaryNames = @('OpticalSystemDesign.App.dll', 'OpticalSystemDesign.Core.dll', 'OpticalSystemDesign.InitialStructure.Engine.dll',
        'OpticalSystemDesign.InitialStructure.Contracts.dll', 'OpticalSystemDesign.InitialStructure.Persistence.dll')
    $taskBinaries = foreach ($taskBinaryName in $taskBinaryNames) {
        $taskDefault = Get-TaskFingerprint ($taskDefaultOutput + $taskBinaryName)
        $taskTest = Get-TaskFingerprint ($taskTestOutput + $taskBinaryName)
        if ($taskDefault.Sha256 -ne $taskTest.Sha256) { throw "Default/test output differs: $taskBinaryName" }
        $taskDefault; $taskTest
    }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffExitCode = $LASTEXITCODE
    if ($taskDiffExitCode -ne 0) { throw 'Whitespace check failed.' }
    $taskOtherBackendChanges = @(git diff --name-only | Where-Object { $_ -match '^(src/OpticalSystemDesign.Core/|labs/InitialStructure/src/)' })
    $taskSummary = [ordered]@{
        Task = 'Autonomous desktop recovery and compensation controls'; Date = '2026-10-27'
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = $taskLatest.Count; LatestPassed = $taskLatest.Count; LatestFailed = 0
        NewTests = 6; RelatedExistingRegressions = 4; Attempts = @($taskAttempts)
        LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = $SourceFormatExitCode; SourceFormatVerify = $FormatVerifyExitCode
            Documentation = $DocumentationExitCode; DiffWhitespace = $taskDiffExitCode
            DefaultAppOutput = 'Release/net10.0'; AppAndDomainBinariesMatchTestOutput = $true }
        Scope = @{ TaskModifiesCoreOrDomainLibrarySources = $false; NewPersistentFields = $false
            DefaultExampleRecoveryFlagsUpgraded = $false; TotalBudgetPolicyChanged = $false
            ConcurrentWorktreeBackendChangesNotAuthoredByThisTask = $taskOtherBackendChanges }
        Validation = @{ Host = 'Avalonia Headless/Skia via SafeHeadlessUnitTestSession'; DotnetProcessorCount = 2
            ParentCommandPriority = 'BelowNormal'; NativeDesktopInteraction = $false; AdditionalTracedRays = 0
            OptimizationOrSearchExecuted = $false; LatestUniqueResultsOnly = $true }
        Limitations = @('Scoped UI checks only; no new optical success or numerical certification claim',
            'No native interaction, full repository suite, long training, new optimization-effect comparison, commit, push or release',
            'Stage update caps do not increase total budgets or guarantee that all requested updates can run')
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ })
        Binaries = @($taskBinaries)
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
    [pscustomobject]$taskSummary | Select-Object Task, UniqueTests, LatestPassed, LatestFailed, Checks | ConvertTo-Json -Depth 5
}
finally { Pop-Location }
