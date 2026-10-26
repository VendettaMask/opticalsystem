#requires -Version 7.0
function Assert-InitialStructureTestResults {
    param([xml]$Trx, [string]$Partition)
    $cases = @($Trx.TestRun.Results.UnitTestResult)
    if ($Trx.TestRun.ResultSummary.outcome -ne 'Completed' -or $cases.Count -eq 0) {
        throw 'The selected partition did not complete or selected no tests.'
    }
    # Hosted runners have no CUDA device. Only these explicitly opt-in
    # hardware checks may be reported as skipped in the contracts partition.
    $hardware = @(
        'GeneratedMultiElementSeedEveryGpuHitMatchesCpu',
        'BenchmarkCompleteMultiStartSearchWithTheSameBudgetAndSchedule',
        'RealGpuSolvePersistsDeviceWorkAndRetainsIndependentPhysicalAcceptance',
        'BenchmarkWarmedCompleteSolveWithoutTimingKernelInitialization',
        'BenchmarkSamePhysicalDesignOnCpuAndCuda'
    ) | ForEach-Object { 'OpticalSystemDesign.InitialStructure.Tests.AutoLensGpuTests.' + $_ }
    foreach ($case in $cases) {
        if ($case.outcome -eq 'Passed') { continue }
        if ($Partition -eq 'contracts' -and $case.outcome -eq 'NotExecuted' -and $case.testName -cin $hardware) { continue }
        throw ('Unexpected test outcome: ' + $case.testName + ' = ' + $case.outcome)
    }
}
