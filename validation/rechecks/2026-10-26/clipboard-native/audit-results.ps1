$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repository = [IO.Path]::GetFullPath((Join-Path $root '../../../..'))
$baseline = Get-Content -LiteralPath (Join-Path $root 'background-summary.json') -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath (Join-Path $root 'fixed/native-final/background-summary.json') -Raw | ConvertFrom-Json
$recheck = Get-Content -LiteralPath (Join-Path $root 'fixed/default-output-recheck/background-summary.json') -Raw | ConvertFrom-Json
[xml]$trx = Get-Content -LiteralPath (Join-Path $root 'fixed/clipboard-focused.trx') -Raw
$counters = $trx.TestRun.ResultSummary.Counters
$faults = @($baseline.Runs | Where-Object { $_.Label.StartsWith('fault-unhandled') })
$allPastes = @($native.Runs | ForEach-Object PasteObservations)
$fixedFailures = @($native.Runs | Where-Object Label -in @('fault-search', 'fault-global', 'fault-late', 'fault-search-control-v'))
$negative = @($native.Runs | Where-Object Label -eq 'unrelated-com')
$positive = @($native.Runs | Where-Object Label -ne 'unrelated-com')
$recovery = @($native.Runs | Where-Object Label -eq 'fault-recovery')
$live = @($native.Runs | Where-Object Label -eq 'live-readonly')
$identities = @($native.Runs | ForEach-Object Initialized | ForEach-Object ProductDllSha256 | Sort-Object -Unique)
$checks = [ordered]@{
    FocusedTests28Passed = [int]$counters.total -eq 28 -and [int]$counters.passed -eq 28 -and [int]$counters.failed -eq 0
    BaselineComCrashReproducedTwice = $faults.Count -eq 2 -and @($faults | Where-Object { $_.ExitCode -eq 0 -or $_.Unhandled[0].HResult -ne '8007000E' }).Count -eq 0
    Native13ExpectedOutcomes = $native.Runs.Count -eq 13 -and $native.AllExpectedOutcomesMatched
    ProtectedAndNormalProcessesExitZero = $positive.Count -eq 12 -and @($positive | Where-Object { $_.ExitCode -ne 0 -or !$_.Complete }).Count -eq 0
    FailedPastesPreserveQueryAndSelection = @($fixedFailures | ForEach-Object PasteObservations | Where-Object { !$_.TextAndSelectionUnchanged -or $_.Warnings -ne 1 }).Count -eq 0
    RecoveryThenSuccess = $recovery.Count -eq 1 -and $recovery[0].PasteObservations[0].TextAndSelectionUnchanged -and $recovery[0].PasteObservations[1].MatchesControlledText
    LiveClipboardFiveSuccessfulPastes = $live[0].PasteObservations.Count -eq 5 -and @($live[0].PasteObservations | Where-Object { !$_.HasText -or $_.Warnings -ne 0 }).Count -eq 0
    NativeUndoRedoPreserved = @($native.Runs | ForEach-Object UndoRedo | Where-Object { !$_.UndoRestored -or !$_.RedoRestored }).Count -eq 0
    UnrelatedComErrorNotSwallowed = $negative.Count -eq 1 -and $negative[0].ExitCode -ne 0 -and $negative[0].Unhandled[0].HResult -eq '8007000E'
    EveryNativeWindowRemainedHidden = @($native.Runs | ForEach-Object Initialized | Where-Object WindowVisible).Count -eq 0 -and @($allPastes | Where-Object WindowVisible).Count -eq 0
    MatrixLoadedOneAppIdentity = $identities.Count -eq 1
    DefaultOutputRecheckFourExpectedOutcomes = $recheck.Runs.Count -eq 4 -and $recheck.AllExpectedOutcomesMatched
}
$sources = @(
    'src/OpticalSystemDesign.App/App.cs',
    'src/OpticalSystemDesign.App/Controls/CommandSearchControl.cs',
    'src/OpticalSystemDesign.App/MainWindow.cs',
    'src/OpticalSystemDesign.App/Services/ClipboardExceptionBoundary.cs',
    'src/OpticalSystemDesign.App/Services/UiLocalization.cs',
    'tests/OpticalSystemDesign.Tests/ClipboardExceptionBoundaryTests.cs'
) | ForEach-Object { [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath (Join-Path $repository $_) -Algorithm SHA256).Hash } }
$result = [ordered]@{
    Date = '2026-10-26'; Checks = $checks
    AllChecksPassed = @($checks.Values | Where-Object { !$_ }).Count -eq 0
    ClipboardSequenceUnchangedInObservedAttempts = @($allPastes | Where-Object { $_.SequenceBefore -ne $_.SequenceAfter }).Count -eq 0
    NativeMatrixAppSha256 = $identities
    DefaultOutputRecheckAppSha256 = @($recheck.Runs | ForEach-Object Initialized | ForEach-Object ProductDllSha256 | Sort-Object -Unique)
    CurrentDefaultAppSha256 = (Get-FileHash -LiteralPath (Join-Path $repository 'src/OpticalSystemDesign.App/bin/Release/net10.0/OpticalSystemDesign.App.dll') -Algorithm SHA256).Hash
    Sources = $sources
    NoInstalledApplicationReplacement = $true
    NoForegroundAutomationDuringBackgroundRegression = $true
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'repair-verification.json') -Encoding utf8
$checks.GetEnumerator() | Format-Table -AutoSize
if (!$result.AllChecksPassed) { throw 'Clipboard repair verification failed.' }
