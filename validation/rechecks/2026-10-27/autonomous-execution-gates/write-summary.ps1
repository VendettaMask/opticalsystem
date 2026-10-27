param(
    [Parameter(Mandatory)][int]$SourceFormatExitCode,
    [Parameter(Mandatory)][int]$FormatVerifyExitCode,
    [Parameter(Mandatory)][int]$DocumentationExitCode
)
$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path (Join-Path $PSScriptRoot '../../../..'))
try {
    if ($SourceFormatExitCode -ne 0 -or $FormatVerifyExitCode -ne 0 -or $DocumentationExitCode -ne 0) { throw 'A required check failed.' }
    $taskLatest = @{}
    $taskAttempts = foreach ($taskName in @('historical-first.trx', 'execution-corrected.trx')) {
        [xml]$taskXml = Get-Content -LiteralPath (Join-Path $PSScriptRoot $taskName) -Raw
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{ Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskName }
        }
        $taskCounters = $taskXml.TestRun.ResultSummary.Counters
        @{ File = $taskName; Total = [int]$taskCounters.total; Passed = [int]$taskCounters.passed; Failed = [int]$taskCounters.failed }
    }
    if ($taskLatest.Count -ne 4 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Expected four latest passing results.' }
    $taskStopped = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'compensation-cancelled.json') -Raw | ConvertFrom-Json
    $taskFinal = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'compensation-final.autonomous.json') -Raw | ConvertFrom-Json).checkpoint
    foreach ($taskUnit in @('FunctionEvaluations', 'GradientEvaluations', 'TracedRays')) {
        $taskSum = ($taskFinal.Events | ForEach-Object { $_.Work.$taskUnit } | Measure-Object -Sum).Sum
        if ($taskSum -ne $taskFinal.Consumed.$taskUnit) { throw "Ledger mismatch: $taskUnit" }
    }
    if ($taskFinal.AcceptedArchive.Count -ne 0 -or $taskFinal.Consumed.MatrixEvaluations -ne 0) { throw 'Unexpected accepted design or recovery work.' }
    if ($taskStopped.Pending.Continuation.Steps.Count -ne 1 -or $taskFinal.PlanFingerprint -ne $taskStopped.PlanFingerprint) { throw 'Continuation or plan mismatch.' }
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskSources = @(
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousSearchService.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousDesktopSession.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
        'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.Execution.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md', 'labs/InitialStructure/README.md'
    )
    $taskBinaries = foreach ($taskName in @('OpticalSystemDesign.App.dll', 'OpticalSystemDesign.Core.dll', 'OpticalSystemDesign.InitialStructure.Engine.dll', 'OpticalSystemDesign.InitialStructure.Contracts.dll', 'OpticalSystemDesign.InitialStructure.Persistence.dll')) {
        $taskApp = Get-TaskFingerprint ('src/OpticalSystemDesign.App/bin/Release/net10.0/' + $taskName)
        $taskTest = Get-TaskFingerprint ('labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/' + $taskName)
        if ($taskApp.Sha256 -ne $taskTest.Sha256) { throw "Default/test output mismatch: $taskName" }
        $taskApp; $taskTest
    }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffExitCode = $LASTEXITCODE
    if ($taskDiffExitCode -ne 0) { throw 'Whitespace check failed.' }
    $taskOtherBackend = @(git diff --name-only | Where-Object { $_ -match '^(src/OpticalSystemDesign.Core/|labs/InitialStructure/src/)' -and $_ -ne $taskSources[0] })
    $taskSummary = [ordered]@{
        Task = 'Autonomous execution eligibility and durable compensation continuation'; Date = '2026-10-27'
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = $taskLatest.Count; LatestPassed = 4; LatestFailed = 0; NewTests = 3; ExistingRegression = 1
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = $SourceFormatExitCode; SourceFormatVerify = $FormatVerifyExitCode
            Documentation = $DocumentationExitCode; DiffWhitespace = $taskDiffExitCode
            DefaultAppOutput = 'Release/net10.0'; DefaultAndTestBinariesMatch = $true }
        Scope = @{ ExistingEngineEligibilityRuleExtractedAndReused = $true; SupportedVersionsChanged = $false
            CoreCalculationsChangedByThisTask = $false; NumericalOptimizerChanged = $false; CheckpointFormatChanged = $false
            ConcurrentBackendChangesNotAuthoredByThisTask = $taskOtherBackend }
        Validation = @{ Host = 'Avalonia Headless/Skia via SafeHeadlessUnitTestSession'; DotnetProcessorCount = 2
            ParentCommandPriority = 'BelowNormal'; NativeDesktopInteraction = $false; HistoricalReadOnlyAdditionalTraces = 0
            RejectedSessionCreatesRunDirectory = $false; FixedTopology = $true; RecoveryDispatched = $false
            CancelledWork = $taskStopped.Consumed; FinalWork = $taskFinal.Consumed
            FinalStatus = $taskFinal.Status; AcceptedDesigns = $taskFinal.AcceptedArchive.Count }
        Limitations = @('The short fixed-topology run validates workflow and accounting, not successful optical design',
            'Recovery flags are persisted but no recovery matrix is dispatched by this fixed-topology case',
            'No long training, new optimization-effect comparison, full repository suite, native interaction, commit, push or release')
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ }); Binaries = @($taskBinaries)
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
    [pscustomobject]$taskSummary | Select-Object Task, UniqueTests, LatestPassed, Checks, Validation | ConvertTo-Json -Depth 6
}
finally { Pop-Location }
