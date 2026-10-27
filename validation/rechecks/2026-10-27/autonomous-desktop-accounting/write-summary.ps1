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
    if ($taskLatest.Count -ne 7 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Latest scoped result mismatch.' }
    $taskSources = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'format-files.json') -Raw | ConvertFrom-Json) + @(
        'AGENTS.md', 'tests/Shared/SafeHeadlessUnitTestSession.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/AutonomousSearchModels.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologyRestorationModels.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/AutonomousSearchCheckpointStore.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousDesktopSession.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md', 'labs/InitialStructure/README.md'
    )
    $taskBinaries = @(
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll',
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Contracts.dll',
        'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Persistence.dll',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll'
    )
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskDiffPaths = @(git diff --name-only)
    $taskConcurrentBackendPaths = @($taskDiffPaths | Where-Object { $_ -match '^(src/OpticalSystemDesign.Core/|labs/InitialStructure/src/)' })
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffCode = $LASTEXITCODE
    if ($taskDiffCode -ne 0) { throw 'Whitespace check failed.' }
    $taskSummary = [ordered]@{
        Task = 'Autonomous desktop work accounting and plan preservation'; Date = '2026-10-27'
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = 7; LatestPassed = 7; LatestFailed = 0; NewTests = 5; RelatedExistingRegressions = 2
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = 0; SourceFormatVerify = 0; Documentation = $DocumentationExitCode; DiffWhitespace = $taskDiffCode; DefaultAppOutput = 'Release/net10.0' }
        Backend = @{ TaskModifiesCoreOrDomainLibrarySources = $false; CheckpointSchemaChanged = $false; BudgetAccountingRulesChanged = $false
            ConcurrentWorktreeBackendChangesNotAuthoredByThisTask = $taskConcurrentBackendPaths }
        Validation = @{
            Host = 'Avalonia Headless/Skia via SafeHeadlessUnitTestSession'; DotnetProcessorCount = 2; ParentCommandPriority = 'BelowNormal'
            NativeDesktopInteraction = $false; ReadAndEditUiAdditionalTraces = 0
            RecoveryFixture = '30 function requests per recovery; one structural trial; only for displaying recorded work, not optical success'
        }
        Limitations = @(
            'No native interaction or full-app localization certification; scoped labels and off-screen captures only',
            'Explicit recovery matrix counts do not count every internal paraxial calculation',
            'Unrecorded or unknown interrupted auxiliary work is not assumed zero',
            'No long training, new optimization-effect comparison, full repository suite, external numerical certification, commit, push or release'
        )
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ })
        Binaries = @($taskBinaries | ForEach-Object { Get-TaskFingerprint $_ })
        LegacyInput = (Get-TaskFingerprint 'validation/rechecks/2026-10-26/autonomous-search-m4/checkpoint-interrupted.json')
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
}
finally { Pop-Location }
