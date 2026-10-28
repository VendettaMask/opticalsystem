#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$evidenceRoot = $PSScriptRoot
$repository = [IO.Path]::GetFullPath((Join-Path $evidenceRoot '../../../..'))
$unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$runs = foreach ($name in @('seed-diversity-green', 'core-paraxial', 'seed-final-inputs', 'seed-summary-locales', 'seed-panel-visibility')) {
    $path = Join-Path $evidenceRoot ($name + '.trx')
    [xml]$trx = [IO.File]::ReadAllText($path)
    $cases = @($trx.TestRun.Results.UnitTestResult)
    if ($cases.Count -eq 0 -or @($cases | Where-Object outcome -ne 'Passed').Count -ne 0) { throw ('Failed/incomplete run: ' + $name) }
    foreach ($case in $cases) { [void]$unique.Add($case.testName) }
    [ordered]@{ name = $name; passed = $cases.Count; tests = @($cases.testName); sha256 = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() }
}
if ($unique.Count -ne 17) { throw ('Unexpected distinct test count: ' + $unique.Count) }
$preparationPath = Join-Path $evidenceRoot 'first-order-preparation.json'
$preparation = (Get-Content -LiteralPath $preparationPath -Raw | ConvertFrom-Json).result
$overTotal = $preparation.work.functionEvaluations -gt $preparation.options.maximumFunctionEvaluations
$overStart = @($preparation.attempts | Where-Object functionEvaluations -gt $preparation.options.maximumEvaluationsPerStart).Count -gt 0
$overRays = $preparation.work.tracedRays -gt $preparation.options.maximumTracedRays
if ($overTotal -or $overStart -or $overRays) { throw 'Preparation exceeds its recorded quota.' }
$sources = @(
    'src/OpticalSystemDesign.Core/Differentiation/DifferentiableParaxialMetrics.cs',
    'tests/OpticalSystemDesign.Tests/ParaxialTransferMatrixTests.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/AutonomousSearchModels.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/TopologySeedSearchModels.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousSearchService.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/ParaxialSeedSearch.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/TopologyDiversitySelection.cs',
    'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/ParaxialSeedSearchFiles.cs',
    'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.cs',
    'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Workflow.cs',
    'src/OpticalSystemDesign.App/Laboratories/InitialStructure/AutonomousTopologyWindow.Seeds.cs',
    'src/OpticalSystemDesign.App/Services/UiLocalization.AutonomousTopology.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/ParaxialSeedSearchTests.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/TopologyDiversityTests.cs',
    'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousDesktopSeedTests.cs')
function Hashes($paths) {
    foreach ($path in $paths) { [ordered]@{ path = $path; sha256 = (Get-FileHash -LiteralPath (Join-Path $repository $path)).Hash.ToLowerInvariant() } }
}
Push-Location $repository
try {
    [ordered]@{
        sourceHead = (git rev-parse HEAD); uncommittedChanges = $true
        mode = 'background CPU/Avalonia.Headless; two processors; BelowNormal; no desktop interaction'
        runs = @($runs); uniquePassingTests = $unique.Count; sourceHashes = @(Hashes $sources)
        binaryHashes = @(Hashes @('src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
            'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll',
            'labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/bin/Release/net10.0/OpticalSystemDesign.InitialStructure.Engine.dll'))
        preparation = [ordered]@{ protocol = $preparation.protocolFingerprint; starts = $preparation.attempts.Count;
            retained = $preparation.seeds.Count; work = $preparation.work; outcomes = @($preparation.attempts.outcome);
            reportSha256 = (Get-FileHash -LiteralPath $preparationPath).Hash.ToLowerInvariant() }
        preparedPlanSha256 = (Get-FileHash -LiteralPath (Join-Path $evidenceRoot 'prepared-first-order.topology-plan.json')).Hash.ToLowerInvariant()
        physicalAcceptanceLimits = 'unchanged; first-order preparation is not full physical certification'
        nativeInteractiveAcceptance = 'not run'; postFixLinuxMacExecution = 'not run locally'
        remainingRecommendations = @('bounded global/local population exploration', 'deletion contribution ranking', 'catalog neighborhood/allocation enhancement')
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'verification.json') -Encoding utf8
    [pscustomobject]@{ DistinctPassingTests = $unique.Count; PreparationStarts = $preparation.attempts.Count; Retained = $preparation.seeds.Count; TracedRays = $preparation.work.tracedRays }
}
finally { Pop-Location }
