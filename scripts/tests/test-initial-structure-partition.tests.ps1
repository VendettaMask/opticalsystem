#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/initial-structure-test-results.ps1')
$runner = Join-Path $PSScriptRoot '../test-initial-structure-partition.ps1'
$plans = @('contracts', 'survey-first', 'survey-second') | ForEach-Object {
    (& $runner -Partition $_ -PlanOnly) | ConvertFrom-Json
}
$expected = @($plans[0].allSurveyCases | Sort-Object)
$actual = @(@($plans[1].selectedSurveyCases) + @($plans[2].selectedSurveyCases) | Sort-Object)
if ($expected.Count -ne 12 -or ($expected -join '|') -cne ($actual -join '|') `
    -or ($actual | Sort-Object -Unique).Count -ne $actual.Count) {
    throw 'Every original survey case must be selected exactly once.'
}
if ($plans[0].selectedSurveyCases.Count -ne 0 -or $plans[0].filter -notlike 'FullyQualifiedName!~*') {
    throw 'The regular test partition must exclude only the separately executed survey.'
}
foreach ($plan in $plans | Select-Object -Skip 1) {
    if ($plan.selectedSurveyCases.Count -ne 6 -or $plan.filter -notlike '*DisplayName~*') {
        throw 'Both survey partitions must retain their six original cases.'
    }
}
[pscustomobject]@{ passed = 3; failed = 0; surveyCases = $actual.Count; droppedCases = 0 }

$hardware = 'OpticalSystemDesign.InitialStructure.Tests.AutoLensGpuTests.GeneratedMultiElementSeedEveryGpuHitMatchesCpu'
function Result([string]$Name, [string]$Outcome, [string]$Summary = 'Completed') {
    [xml]"<TestRun><Results><UnitTestResult testName='$Name' outcome='$Outcome'/></Results><ResultSummary outcome='$Summary'/></TestRun>"
}
Assert-InitialStructureTestResults (Result 'ordinary' 'Passed') 'contracts'
Assert-InitialStructureTestResults (Result $hardware 'NotExecuted') 'contracts'
$rejected = 0
foreach ($sample in @(
    @{ Trx = (Result 'ordinary' 'Failed'); Partition = 'contracts' },
    @{ Trx = (Result 'ordinary' 'NotExecuted'); Partition = 'contracts' },
    @{ Trx = (Result $hardware 'NotExecuted'); Partition = 'survey-first' },
    @{ Trx = (Result $hardware 'Failed'); Partition = 'contracts' },
    @{ Trx = (Result 'ordinary' 'Passed' 'Aborted'); Partition = 'contracts' },
    @{ Trx = ([xml]"<TestRun><Results/><ResultSummary outcome='Completed'/></TestRun>"); Partition = 'contracts' }
)) {
    try { Assert-InitialStructureTestResults @sample }
    catch { $rejected++; continue }
    throw 'Invalid, empty, aborted or unexpectedly skipped test results must fail.'
}
[pscustomobject]@{ outcomeChecks = 8; rejected = $rejected; failed = 0 }
