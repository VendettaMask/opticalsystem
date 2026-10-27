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
    $taskAttempts = foreach ($taskName in @('recovery-first.trx', 'recovery-rank-probe.trx', 'recovery-corrected.trx', 'recovery-final.trx')) {
        [xml]$taskXml = Get-Content -LiteralPath (Join-Path $PSScriptRoot $taskName) -Raw
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{ Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskName }
        }
        $taskCounters = $taskXml.TestRun.ResultSummary.Counters
        @{ File = $taskName; Passed = [int]$taskCounters.passed; Failed = [int]$taskCounters.failed; Total = [int]$taskCounters.total }
    }
    if ($taskLatest.Count -ne 6 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Expected six latest passing results.' }
    $taskCases = foreach ($taskMode in @('False', 'True')) {
        $taskStopped = Get-Content -LiteralPath (Join-Path $PSScriptRoot "recovery-$taskMode-cancelled.json") -Raw | ConvertFrom-Json
        $taskResumed = (Get-Content -LiteralPath (Join-Path $PSScriptRoot "recovery-$taskMode-resumed.autonomous.json") -Raw | ConvertFrom-Json).checkpoint
        foreach ($taskUnit in @('FunctionEvaluations','GradientEvaluations','ValidationEvaluations','MatrixEvaluations','DifferenceEvaluations','JacobianBuilds','TracedRays','GeometryProbes')) {
            if ($taskStopped.Consumed.$taskUnit -ne $taskResumed.Consumed.$taskUnit) { throw "Repeated work after resume: $taskUnit" }
            $taskSum = ($taskResumed.Events | ForEach-Object { $_.Work.$taskUnit } | Measure-Object -Sum).Sum
            if ($taskSum -ne $taskResumed.Consumed.$taskUnit) { throw "Ledger mismatch: $taskUnit" }
        }
        $taskRecovery = @($taskStopped.Events | Where-Object Phase -eq 0)[0].Recovery
        if ($taskStopped.Pending -or $taskStopped.Reservation -or $taskRecovery.Steps.Count -ne 1 -or $taskStopped.PlanFingerprint -ne $taskResumed.PlanFingerprint) { throw 'Interrupted trial or plan mismatch.' }
        @{ Automatic = [bool]::Parse($taskMode); RecoveryWork = $taskRecovery.Work; SearchWork = $taskStopped.Consumed
            ResumedStatus = $taskResumed.Status; ResumeAdditionalTraces = 0; TrialReplayed = $false }
    }
    $taskPositive = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'front-stop-pupil-perturbation.json') -Raw | ConvertFrom-Json
    $taskNegative = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'front-stop-pupil-full-current.json') -Raw | ConvertFrom-Json
    $taskIncompatible = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'incompatible-pupil-rank.json') -Raw | ConvertFrom-Json
    if ($taskPositive.Termination -ne 0 -or $taskNegative.Termination -ne 4 -or $taskIncompatible.Termination -ne 1) { throw 'Optical control outcomes changed.' }
    $taskErrors = @($taskPositive.EndpointAError, $taskPositive.EndpointFocalCError, $taskPositive.EndpointPupilError)
    if (@($taskErrors | Where-Object { [Math]::Abs($_) -gt 1e-4 }).Count -ne 0) { throw 'Original recovery tolerance was not met.' }
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskSources = @(
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousSearchService.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ParaxialRestorer.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousDesktopSession.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
        'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.RecoveryProgress.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/ParaxialRecoveryRepairTests.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md', 'labs/InitialStructure/README.md'
    )
    $taskBinaries = foreach ($taskName in @('OpticalSystemDesign.App.dll','OpticalSystemDesign.Core.dll','OpticalSystemDesign.InitialStructure.Engine.dll','OpticalSystemDesign.InitialStructure.Contracts.dll','OpticalSystemDesign.InitialStructure.Persistence.dll')) {
        $taskDefault = Get-TaskFingerprint ('src/OpticalSystemDesign.App/bin/Release/net10.0/' + $taskName)
        $taskTest = Get-TaskFingerprint ('labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/' + $taskName)
        if ($taskDefault.Sha256 -ne $taskTest.Sha256) { throw "Default/test output mismatch: $taskName" }
        $taskDefault; $taskTest
    }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffExitCode = $LASTEXITCODE
    if ($taskDiffExitCode -ne 0) { throw 'Whitespace check failed.' }
    $taskConcurrent = @(git diff --name-only | Where-Object { $_ -match '^(src/OpticalSystemDesign.Core/|labs/InitialStructure/src/)' -and $_ -notin $taskSources })
    $taskSummary = [ordered]@{
        Task = 'Recovery progress, compatible redundant pupil constraints, and durable cancellation'; Date = '2026-10-27'
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = 6; LatestPassed = 6; LatestFailed = 0; NewTests = 3; RelatedExistingRegressions = 3
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = $SourceFormatExitCode; SourceFormatVerify = $FormatVerifyExitCode; Documentation = $DocumentationExitCode
            DiffWhitespace = $taskDiffExitCode; DefaultAppOutput = 'Release/net10.0'; DefaultAndTestBinariesMatch = $true }
        Scope = @{ PupilRecoveryProtocol = 'autonomous-paraxial-recovery-pupil-v4'; CoreOpticalCalculationsChangedByThisTask = $false
            LaboratoryRankGuardChanged = $true; ParameterPermissionsChanged = $false; OriginalTolerance = 1e-4
            CheckpointFieldsChanged = $false; LegacyNonPupilProtocolsChanged = $false; ConcurrentBackendChangesNotAuthoredByThisTask = $taskConcurrent }
        Cases = @($taskCases)
        SeparateControls = @{ PerturbationRecovered = $true; PerturbationWork = $taskPositive.Work; PerturbationErrors = $taskErrors
            FullDeletionRecovered = $false; FullDeletionWork = $taskNegative.Work; FullDeletionDiagnostics = $taskNegative.Diagnostics
            IncompatibleResidualRejected = $true; IncompatibleWork = $taskIncompatible.Work; AdditionalPhysicalRays = 0 }
        Validation = @{ Host = 'Avalonia Headless/Skia via SafeHeadlessUnitTestSession'; DotnetProcessorCount = 2; ParentCommandPriority = 'BelowNormal'; NativeDesktopInteraction = $false }
        Limitations = @('The small parent uses relaxed workflow targets; no successful reduced optical design is claimed',
            'Full deletion remains blocked by the original edge-thickness gate; the local perturbation control is separate',
            'No native UI acceptance, full repository suite, long training, final-output comparison, commit, push or release')
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ }); Binaries = @($taskBinaries)
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
    [pscustomobject]$taskSummary | Select-Object Task, UniqueTests, LatestPassed, Checks, SeparateControls | ConvertTo-Json -Depth 6
}
finally { Pop-Location }
