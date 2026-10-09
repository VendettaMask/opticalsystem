param([string]$FinalFormalDirectory = 'formal-release-final-v3')
$ErrorActionPreference = 'Stop'
$evidenceDirectory = 'artifacts/validation/fft-sampling-reference-repair-20261009'
function Read-Trx([string]$path) {
    [xml]$document = Get-Content -LiteralPath $path -Raw
    $counter = $document.SelectSingleNode("//*[local-name()='Counters']")
    $summary = $document.SelectSingleNode("//*[local-name()='ResultSummary']")
    $results = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    [pscustomobject]@{
        Path = $path
        Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        Outcome = $summary.GetAttribute('outcome')
        Total = [int]$counter.GetAttribute('total')
        Passed = [int]$counter.GetAttribute('passed')
        Failed = [int]$counter.GetAttribute('failed')
        Skipped = [int]$counter.GetAttribute('notExecuted')
        Results = $results
    }
}
function Trx-Summary($run) {
    $run | Select-Object Path,Sha256,Outcome,Total,Passed,Failed,Skipped
}
function Identity-Map($run) {
    $map = [System.Collections.Generic.Dictionary[string,int]]::new([System.StringComparer]::Ordinal)
    foreach ($result in $run.Results) {
        $key = $result.GetAttribute('testId') + [char]31 + $result.GetAttribute('testName')
        if ($map.ContainsKey($key)) { $map[$key]++ } else { $map[$key] = 1 }
    }
    return ,$map
}
$previousFormal = Read-Trx 'artifacts/validation/ray-launch-coordinate-repair-20261009/formal-release-final/formal-release-final.trx'
$formal = Read-Trx "$evidenceDirectory/$FinalFormalDirectory/formal-release-final.trx"
$previousComparison = Read-Trx 'artifacts/validation/ray-launch-coordinate-repair-20261009/comparison-release-final/comparison-release-final.trx'
$comparison = Read-Trx "$evidenceDirectory/comparison-release-final/comparison-release-final.trx"
$oldIdentities = Identity-Map $previousFormal
$identities = Identity-Map $formal
$removed = @($oldIdentities.Keys | Where-Object { !$identities.ContainsKey($_) -or $identities[$_] -ne $oldIdentities[$_] })
$added = @($identities.Keys | Where-Object { !$oldIdentities.ContainsKey($_) })
$oldComparisonIdentities = Identity-Map $previousComparison
$comparisonIdentities = Identity-Map $comparison
$comparisonIdentityDifference = @($oldComparisonIdentities.Keys | Where-Object {
    !$comparisonIdentities.ContainsKey($_) -or $comparisonIdentities[$_] -ne $oldComparisonIdentities[$_]
}).Count + @($comparisonIdentities.Keys | Where-Object { !$oldComparisonIdentities.ContainsKey($_) }).Count
function Failure-Details($run) {
    @($run.Results | Where-Object { $_.GetAttribute('outcome') -eq 'Failed' } | ForEach-Object {
        [pscustomobject]@{
            TestId = $_.GetAttribute('testId'); Name = $_.GetAttribute('testName')
            Error = [string]$_.Output.ErrorInfo.Message; Stdout = [string]$_.Output.StdOut
        }
    } | Sort-Object Name)
}
$oldFailures = Failure-Details $previousComparison
$failures = Failure-Details $comparison
$failuresUnchanged = (ConvertTo-Json -InputObject $oldFailures -Depth 5 -Compress) -ceq
    (ConvertTo-Json -InputObject $failures -Depth 5 -Compress)
$tracked = @(git ls-files)
$frozen = @($tracked | Where-Object {
    $_ -like 'validation/*' -or $_ -like 'artifacts/zemax/*' -or
    $_ -like 'tests/OptilandWorkbench.Tests/Fixtures/*' -or $_ -like '*comparison-settings.json'
})
$entries = [System.Text.StringBuilder]::new()
foreach ($path in $frozen) {
    [void]$entries.AppendLine($path + '=' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
}
$frozenHash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData(
    [System.Text.Encoding]::UTF8.GetBytes($entries.ToString()))).ToLowerInvariant()
$hashPaths = @(
    'src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs',
    'src/OptilandWorkbench.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs',
    'src/OptilandWorkbench.Core/Analysis/Radiometry/EncircledEnergyVariants.cs',
    'src/OptilandWorkbench.Core/Services/DiffractionEnergyMetrics.cs',
    'tests/OptilandWorkbench.Tests/FftSamplingReferenceRepairTests.cs',
    'tests/OptilandWorkbench.Tests/AnalysisNumericalRepairTests.cs',
    'src/OptilandWorkbench.Core/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.Core/bin/Debug/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.Tests/bin/Debug/net10.0/OptilandWorkbench.Core.dll',
    'tests/OptilandWorkbench.ZemaxComparison.Tests/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.App/bin/Release/net10.0/OptilandWorkbench.Core.dll',
    'src/OptilandWorkbench.App/bin/Debug/net10.0/OptilandWorkbench.Core.dll'
)
$hashes = @($hashPaths | ForEach-Object {
    [pscustomobject]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$otherRuns = @(
    "$evidenceDirectory/before/before.trx", "$evidenceDirectory/after/after-targeted.trx",
    "$evidenceDirectory/formal-release-final/formal-release-final.trx",
    "$evidenceDirectory/formal-release-final-v2/formal-release-final.trx",
    "$evidenceDirectory/debug-targeted-final/debug-targeted-final.trx",
    "$evidenceDirectory/debug-targeted-final-v2/debug-targeted-final.trx"
)
[pscustomobject]@{
    Date = '2026-10-09'; Scope = 'Formal FFT sampling and energy reference repair; not native residual closure'
    RepositoryHead = (git rev-parse HEAD); GitState = 'Uncommitted; no push or new native capture'
    Builds = @{ Debug = @{ ExitCode=0; Warnings=0; Errors=0; DefaultOutput=$true };
        Release = @{ ExitCode=0; Warnings=0; Errors=0; DefaultOutput=$true } }
    FormalRelease = (Trx-Summary $formal); ComparisonRelease = (Trx-Summary $comparison)
    AdditionalTrxs = @($otherRuns | ForEach-Object { Trx-Summary (Read-Trx $_) })
    IdentityRetention = @{ Definition='testId + full testName + exact occurrence multiplicity';
        PreviousCount=$previousFormal.Total; CurrentCount=$formal.Total; RemovedOrMultiplicityChanged=$removed.Count;
        AddedCount=$added.Count; AddedNames=@($added | ForEach-Object { ($_ -split [char]31)[1] } | Sort-Object);
        AllCurrentPassed=($formal.Passed -eq $formal.Total -and $formal.Outcome -ne 'Aborted') }
    ComparisonRetention = @{ IdentityDifference=$comparisonIdentityDifference; FailureDetailsUnchanged=$failuresUnchanged;
        PreviousTotal=$previousComparison.Total; CurrentTotal=$comparison.Total; Failures=$failures }
    FrozenIntegrity = @{ Count=$frozen.Count; Sha256=$frozenHash;
        MatchesPrevious=($frozenHash -eq '7c1fc6e327ac3a0d0da17adb04d1a88a90999af25522136110cd0041a92b3ce0') }
    SourceAndBinaryHashes = $hashes
    DiagnosticBefore = 'artifacts/validation/web-implementation-audit-20261009/results.json'
    DiagnosticAfter = "$evidenceDirectory/diagnostic-after.json"
    ChangedExistingTestContract = 'MS-L7 0.5 micrometer coordinate-only control now rejects incomplete pupil; 0.25 native assertions and fixtures unchanged'
    ExcludedRuns = @('First Release full run deliberately aborted after obsolete 0.5 micrometer contract failure',
        'Second Release full run deliberately aborted after background task setup deadline failure; isolated unchanged cancellation test passed, final full run does not overlap heavy checks',
        'First Debug targeted run deliberately aborted to release locked default test output; not a complete pass')
    Unverified = @('Full Debug', 'New native 2D PSF and vertex/polychromatic captures',
        'Native N01/N02 residual closure', 'Laboratories, installers and manual desktop smoke test',
        'Arbitrary rotated/decentered/folded pupil and tilted image projection')
} | ConvertTo-Json -Depth 10
