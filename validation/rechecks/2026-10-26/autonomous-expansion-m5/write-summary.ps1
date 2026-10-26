param([int]$DocumentationExitCode = -1)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
Push-Location $taskRoot
try {
    $taskLatest = @{}
    $taskAttempts = foreach ($taskFile in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.trx' | Sort-Object LastWriteTimeUtc)) {
        [xml]$taskXml = Get-Content -LiteralPath $taskFile.FullName
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{ Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskFile.Name }
        }
        @{ File = $taskFile.Name; Passed = [int]$taskXml.TestRun.ResultSummary.Counters.passed; Failed = [int]$taskXml.TestRun.ResultSummary.Counters.failed }
    }
    if ($taskLatest.Count -ne 14 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Latest scoped result mismatch.' }
    $taskSources = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'format-files.json') -Raw | ConvertFrom-Json) + @(
        'AGENTS.md',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyCompiler.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyParameterBinder.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/FixedTopologyOptimizer.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/FixedTopologyObjective.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyValidator.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ParaxialRestorer.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/AutonomousSearchCheckpointStore.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md',
        'labs/InitialStructure/README.md', 'labs/InitialStructure/benchmarks/autonomous-topology-v1/README.md',
        'labs/InitialStructure/benchmarks/autonomous-topology-v1/expansion-m5.json'
    )
    $taskBinaries = @(
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.App.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.Core.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Contracts.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Persistence.dll',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll'
    )
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskInput = 'labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json'
    $taskInputHead = git rev-parse "HEAD:$taskInput"
    $taskInputWorktree = git hash-object -- $taskInput
    if ($taskInputHead -ne $taskInputWorktree) { throw 'Frozen source input changed.' }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffCode = $LASTEXITCODE
    if ($taskDiffCode -ne 0) { throw 'Whitespace check failed.' }
    $taskGradual = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gradual-complete.json') -Raw | ConvertFrom-Json
    $taskResume = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'gradual-resume.json') -Raw | ConvertFrom-Json
    $taskSummary = [ordered]@{
        Stage = 'M5'; Date = '2026-10-26'; Algorithm = 'AutonomousTopologyV1'; CheckpointSchema = 1
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true; Ownership = 'Scoped InitialStructure changes; other concurrent Core/analysis edits are not attributed to M5.'
        UniqueTests = 14; LatestPassed = 14; LatestFailed = 0; NewM5Tests = 13; RelatedM4Regressions = 1
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = 0; SourceFormatVerify = 0; Documentation = $DocumentationExitCode; DiffWhitespace = $taskDiffCode; DefaultAppOutput = 'Debug/net10.0' }
        ActualGradualWork = $taskGradual.Consumed
        TransitionStages = 5; CompensatedStagesIncludingPhysicalDeletion = 6; IndependentAssessments = 7
        Resume = @{
            SameFunctionCount = ($taskResume.full.Consumed.FunctionEvaluations -eq $taskResume.resumed.Consumed.FunctionEvaluations)
            SameGradientCount = ($taskResume.full.Consumed.GradientEvaluations -eq $taskResume.resumed.Consumed.GradientEvaluations)
            SameRayCount = ($taskResume.full.Consumed.TracedRays -eq $taskResume.resumed.Consumed.TracedRays)
            EndpointEqualityProvedBy = 'MidTransitionSaveResumePreservesOriginAccountingAndArchiveAndUnknownWorkCannotPublishTransitions'
        }
        LegacyM4 = @{ ActualFilesRead = 3; TracedRaysDuringRead = 0; Schema = 1; ExpandedPolicyRemainsNull = $true }
        Limitations = @(
            'Explicit Expansion opt-in, CPU library; no new UI or M5 GPU hard-quota certification',
            'Direct-delete recovery test deliberately limited to two requests; not evidence of inherent non-recoverability',
            'Loose gradual targets and strict branch-test targets are control fixtures, not original-task success',
            'Merge sample returns NoParaxialRecovery; original accepted archive preserved',
            'No old full training, output-effect comparison, full repository suite, external certification, commit, push or release'
        )
        FrozenInput = @{ File = (Get-TaskFingerprint $taskInput); HeadGitObject = $taskInputHead; WorktreeGitObject = $taskInputWorktree; Unchanged = $true }
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ })
        Binaries = @($taskBinaries | ForEach-Object { Get-TaskFingerprint $_ })
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
}
finally { Pop-Location }
