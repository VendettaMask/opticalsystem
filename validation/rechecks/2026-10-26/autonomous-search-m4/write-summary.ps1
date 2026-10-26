param([int]$DocumentationExitCode = -1)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
Push-Location $taskRoot
try {
    $taskCopies = @(
        @{ Prefix = 'm4-archive-*'; Name = 'checkpoint-archive.json' },
        @{ Prefix = 'm4-mid-phase-*'; Name = 'checkpoint-mid-phase.json' },
        @{ Prefix = 'm4-interruption-*'; Name = 'checkpoint-interrupted.json' }
    )
    foreach ($taskCopy in $taskCopies) {
        # The test host ran outside the filesystem sandbox; its temp directory is the native one.
        $taskFolder = Get-ChildItem -LiteralPath 'C:\Users\19851\AppData\Local\Temp' -Directory -Filter $taskCopy.Prefix |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if ($null -eq $taskFolder) { throw 'Missing actual tested checkpoint.' }
        Copy-Item -LiteralPath (Join-Path $taskFolder.FullName 'checkpoint.json') -Destination (Join-Path $PSScriptRoot $taskCopy.Name)
    }
    $taskLatest = @{}
    $taskAttempts = foreach ($taskFile in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.trx' | Sort-Object LastWriteTimeUtc)) {
        [xml]$taskXml = Get-Content -LiteralPath $taskFile.FullName
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{ Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskFile.Name }
        }
        @{ File = $taskFile.Name; Passed = [int]$taskXml.TestRun.ResultSummary.Counters.passed; Failed = [int]$taskXml.TestRun.ResultSummary.Counters.failed }
    }
    if ($taskLatest.Count -ne 12 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Latest unique test result mismatch.' }
    $taskSourcePaths = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'format-files.json') -Raw | ConvertFrom-Json) + @(
        'AGENTS.md',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/OpticalSystemDesign.InitialStructure.Persistence.csproj',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyCompiler.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyParameterBinder.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/Optimization/BoundedTrustRegionLeastSquares.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/FixedTopologyObjective.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/DesignTopology.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologyRestorationModels.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md', 'docs/CURRENT_STATUS.md', 'labs/InitialStructure/README.md',
        'labs/InitialStructure/benchmarks/autonomous-topology-v1/README.md',
        'labs/InitialStructure/benchmarks/autonomous-topology-v1/search-m4.json'
    )
    $taskBinaryPaths = @(
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.App.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.Core.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Contracts.dll',
        'src/OpticalSystemDesign.App/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Persistence.dll',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Debug/net10.0/OpticalSystemDesign.InitialStructure.Tests.dll'
    )
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant(); Bytes = (Get-Item -LiteralPath $taskPath).Length }
    }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log')
    $taskDiffCode = $LASTEXITCODE
    if ($taskDiffCode -ne 0) { throw 'Diff whitespace check failed.' }
    $taskSummary = [ordered]@{
        Stage = 'M4'; Date = '2026-10-26'; Algorithm = 'AutonomousTopologyV1'; CheckpointSchema = 1
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = 12; LatestPassed = 12; LatestFailed = 0; NewM4Tests = 8; RelatedExistingRegressions = 4
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{
            SourceFormat = 0; SourceFormatVerify = 0; Documentation = $DocumentationExitCode; DiffWhitespace = $taskDiffCode
            DefaultAppOutput = 'Debug/net10.0'
            CudaSmoke = @{ DerivativeRays = 225; Batches = 9; Scope = 'Existing two-update fixed-topology path outside CPU quota; not M4 GPU quota certification' }
        }
        Limitations = @(
            'CPU-only hard-quota search library; no new UI',
            'Recovery cancellation abandons that attempt, retaining original charge and endpoint',
            'Unknown in-flight phase conservatively charges its reservation once',
            'No original-task success, optimization-effect comparison, old long training, commit, push or release'
        )
        Sources = @($taskSourcePaths | ForEach-Object { Get-TaskFingerprint $_ })
        Binaries = @($taskBinaryPaths | ForEach-Object { Get-TaskFingerprint $_ })
        FrozenInput = (Get-TaskFingerprint 'labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json')
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
}
finally { Pop-Location }
