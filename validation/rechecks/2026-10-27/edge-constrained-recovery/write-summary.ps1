param(
    [Parameter(Mandatory)][int]$SourceFormatExitCode,
    [Parameter(Mandatory)][int]$FormatVerifyExitCode,
    [Parameter(Mandatory)][int]$DocumentationExitCode
)
$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path (Join-Path $PSScriptRoot '../../../..'))
try {
    if ($SourceFormatExitCode -ne 0 -or $FormatVerifyExitCode -ne 0 -or $DocumentationExitCode -ne 0) { throw 'A required check failed.' }
    function Read-Evidence([string]$taskName) { Get-Content -LiteralPath (Join-Path $PSScriptRoot $taskName) -Raw | ConvertFrom-Json }
    function Get-TaskFingerprint([string]$taskPath) {
        @{ Path = $taskPath; Bytes = (Get-Item -LiteralPath $taskPath).Length; Sha256 = (Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $taskLatest = @{}
    $taskAttempts = foreach ($taskName in @('step-projection.trx','joint-projection.trx','integrated.trx','final-controls.trx','optional-pupil.trx')) {
        [xml]$taskXml = Get-Content -LiteralPath (Join-Path $PSScriptRoot $taskName) -Raw
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            $taskLatest[$taskResult.testName] = @{ Name = $taskResult.testName; Outcome = $taskResult.outcome; Evidence = $taskName }
        }
        $taskCounters = $taskXml.TestRun.ResultSummary.Counters
        @{ File = $taskName; Total = [int]$taskCounters.total; Passed = [int]$taskCounters.passed; Failed = [int]$taskCounters.failed }
    }
    if ($taskLatest.Count -ne 5 -or @($taskLatest.Values | Where-Object Outcome -ne 'Passed').Count -ne 0) { throw 'Expected five different latest passing results.' }
    $taskRun = Read-Evidence 'integrated-result.json'; $taskRecovery = $taskRun.result
    $taskGeometry = Read-Evidence 'integrated-dense-geometry.json'
    $taskStrict = Read-Evidence 'integrated-strict-product-assessment.json'
    $taskFixture = Read-Evidence 'integrated-independent-assessment.json'
    $taskSearch = Read-Evidence 'integrated-autonomous-search.json'
    $taskControls = Read-Evidence 'optional-pupil-tangents-and-budgets.json'
    $taskOld = Get-Content -LiteralPath 'validation/rechecks/2026-10-27/autonomous-recovery-progress/front-stop-pupil-full-current.json' -Raw | ConvertFrom-Json
    if (!$taskRun.options.UseGeometryConstraintTangents -or $taskRun.options.MaximumFunctionEvaluations -ne 80 -or $taskRun.options.MaximumAcceptedSteps -ne 30 -or $taskRecovery.Variables.Count -ne 8) { throw 'Frozen allowances or variable count changed.' }
    foreach ($taskProperty in $taskRun.plan.Options.Recovery.PSObject.Properties) {
        if (($taskProperty.Value | ConvertTo-Json -Compress -Depth 15) -ne ($taskRun.options.($taskProperty.Name) | ConvertTo-Json -Compress -Depth 15)) {
            throw "Original recovery option changed: $($taskProperty.Name)"
        }
    }
    if (($taskRecovery.Variables | ConvertTo-Json -Compress -Depth 15) -ne ($taskOld.Variables | ConvertTo-Json -Compress -Depth 15) -or
        ($taskRecovery.ChildInitialCoordinates | ConvertTo-Json -Compress) -ne ($taskOld.ChildInitialCoordinates | ConvertTo-Json -Compress)) { throw 'Variables or edited-child origin changed.' }
    for ($taskIndex = 0; $taskIndex -lt $taskRecovery.Variables.Count; $taskIndex++) {
        $taskVariable = $taskRecovery.Variables[$taskIndex]; $taskCoordinate = $taskRecovery.EndpointCoordinates[$taskIndex]
        if ($taskCoordinate -lt $taskVariable.LowerBound / $taskVariable.Scale -or $taskCoordinate -gt $taskVariable.UpperBound / $taskVariable.Scale) { throw 'Endpoint exceeded an original variable bound.' }
    }
    if ($taskRecovery.Termination -ne 0 -or $taskRecovery.Work.FunctionEvaluations -gt 80 -or $taskRecovery.Steps.Count -gt 30 -or
        @($taskGeometry | Where-Object Margin -lt 0).Count -ne 0) { throw 'Recovery or original dense geometry gate failed.' }
    if ($taskStrict.assessment.MeetsTargets -or !$taskFixture.assessment.MeetsTargets -or
        $taskStrict.assessment.Targets.MaximumRmsRadiusMillimeters -ne .05 -or $taskStrict.assessment.Targets.MaximumSpotRadiusMillimeters -ne .15) { throw 'Strict product acceptance was misrepresented.' }
    if ($taskControls.invalid.Work.FunctionEvaluations -ne 0 -or $taskControls.cancelled.Work.FunctionEvaluations -ne 0 -or
        $taskControls.bounded.Work.FunctionEvaluations -gt 3 -or $taskControls.noPupil.Termination -ne 0) { throw 'Policy, cancellation or budget control failed.' }
    foreach ($taskUnit in @('FunctionEvaluations','GradientEvaluations','ValidationEvaluations','MatrixEvaluations','DifferenceEvaluations','JacobianBuilds','TracedRays','GeometryProbes')) {
        if (($taskSearch.Events | ForEach-Object { $_.Work.$taskUnit } | Measure-Object -Sum).Sum -ne $taskSearch.Consumed.$taskUnit) { throw "Search ledger mismatch: $taskUnit" }
    }
    $taskSources = @(
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologyRestorationModels.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ParaxialRestorer.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/Optimization/BoundedTrustRegionLeastSquares.cs',
        'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/Optimization/LinearFeasibilityProjection.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.cs',
        'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
        'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/ParaxialGeometryRecoveryTests.cs',
        'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopTests.GeometryRecovery.cs',
        'docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md','docs/CURRENT_STATUS.md','labs/InitialStructure/README.md'
    )
    $taskBinaries = foreach ($taskName in @('OpticalSystemDesign.App.dll','OpticalSystemDesign.Core.dll','OpticalSystemDesign.InitialStructure.Engine.dll','OpticalSystemDesign.InitialStructure.Contracts.dll','OpticalSystemDesign.InitialStructure.Persistence.dll')) {
        $taskDefault = Get-TaskFingerprint ('src/OpticalSystemDesign.App/bin/Release/net10.0/' + $taskName)
        $taskTest = Get-TaskFingerprint ('labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/bin/Release/net10.0/' + $taskName)
        if ($taskDefault.Sha256 -ne $taskTest.Sha256) { throw "Default/test output mismatch: $taskName" }
        $taskDefault; $taskTest
    }
    git diff --check *> (Join-Path $PSScriptRoot 'diff-check.log'); $taskDiffCode = $LASTEXITCODE
    if ($taskDiffCode -ne 0) { throw 'Whitespace check failed.' }
    $taskOutside = @(git diff --name-only | Where-Object { $_ -match '^(src/OpticalSystemDesign.Core/|labs/InitialStructure/src/)' -and $_ -notin $taskSources })
    $taskSummary = [ordered]@{
        Task = 'Edge-constrained paraxial recovery using shared Core manufacturing tangents'; Date = '2026-10-27'
        BaseCommit = (git rev-parse HEAD); WorktreeIsUncommitted = $true
        UniqueTests = 5; LatestPassed = 5; LatestFailed = 0; NewTests = 4; ExistingRegression = 1
        Attempts = @($taskAttempts); LatestTests = @($taskLatest.Values | Sort-Object Name)
        Checks = @{ SourceFormat = $SourceFormatExitCode; SourceFormatVerify = $FormatVerifyExitCode; Documentation = $DocumentationExitCode
            DiffWhitespace = $taskDiffCode; DefaultAppOutput = 'Release/net10.0'; DefaultAndTestBinariesMatch = $true
            InitialFormatApproval = 'Timed out; permitted single retry completed' }
        Scope = @{ PureCSharp = $true; SharedCoreOptics = $true; CoreOpticalFormulaChangedByThisTurn = $false
            NewOptionalRecoveryField = 'UseGeometryConstraintTangents'; RecoveryProtocol = 'autonomous-paraxial-recovery-geometry-v1'
            OriginalVariables = 8; OriginalFunctionCap = 80; OriginalAcceptedStepCap = 30; AxialWindowMillimeters = .25
            LegacyNullIdentityPreserved = $true; CheckpointSchemaVersion = 1; WorktreeBackendChangesOutsideThisTurn = $taskOutside }
        IsolatedRecovery = @{ Work = $taskRecovery.Work; AcceptedSteps = $taskRecovery.Steps.Count
            Errors = @($taskRecovery.EndpointAError,$taskRecovery.EndpointFocalCError,$taskRecovery.EndpointPupilError)
            DenseGeometryRows = $taskGeometry.Count; AllDenseGeometryGatesMet = $true
            MinimumEdgeMillimeters = ($taskGeometry | Where-Object Id -like '*:edge:*' | Measure-Object Value -Minimum).Minimum
            MaximumCoreTangentDifference = ($taskControls.differences | ForEach-Object { [Math]::Abs($_.Automatic - $_.Difference) } | Measure-Object -Maximum).Maximum }
        IndependentAssessments = @{ FixtureTargetsMet = $true; StrictProductTargetsMet = $false; StrictViolations = $taskStrict.assessment.Violations
            FixtureRays = $taskFixture.PhysicalRays; StrictRays = $taskStrict.PhysicalRays }
        SeparateSearchWork = $taskSearch.Consumed
        Validation = @{ Host = 'CPU and Avalonia Headless/Skia'; DotnetProcessorCount = 2; ParentCommandPriority = 'BelowNormal'; NativeDesktopInteraction = $false }
        Limitations = @('One axial workflow fixture, not general deletion success or final design qualification',
            'Strict original product RMS and maximum-radius gates still fail; fixture acceptance is reported separately',
            'No full repository suite, native GUI acceptance, long training, commit, push or release')
        FrozenInputs = @('validation/rechecks/2026-10-27/autonomous-recovery-progress/recovery-True-resumed.autonomous.json',
            'validation/rechecks/2026-10-27/autonomous-recovery-progress/front-stop-pupil-full-current.json') | ForEach-Object { Get-TaskFingerprint $_ }
        Sources = @($taskSources | ForEach-Object { Get-TaskFingerprint $_ }); Binaries = @($taskBinaries)
        Evidence = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -ne 'summary.json' | ForEach-Object { Get-TaskFingerprint $_.FullName })
    }
    $taskSummary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'summary.json') -Encoding utf8NoBOM
    [pscustomobject]$taskSummary | Select-Object Task,UniqueTests,LatestPassed,Checks,IsolatedRecovery,IndependentAssessments | ConvertTo-Json -Depth 6
}
finally { Pop-Location }
