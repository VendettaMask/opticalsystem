param([string]$Root = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
function Require($condition, [string]$message) { if (!$condition) { throw $message } }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$tests = @(foreach ($case in @(
    @{file='afocal.trx'; expected=167; scope='Afocal regressions, native contracts and independent physics'},
    @{file='focal-native.trx'; expected=55; scope='Focused focal native-reference regressions'},
    @{file='pupil-focal.trx'; expected=5; scope='Existing focal pupil analytical regressions'}
)) {
    $path = Join-Path $Root ('tests/' + $case.file)
    [xml]$trx = Get-Content -LiteralPath $path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $results = @($trx.TestRun.Results.UnitTestResult)
    Require ([int]$counters.total -eq $case.expected -and [int]$counters.passed -eq $case.expected -and
        [int]$counters.failed -eq 0 -and [int]$counters.notExecuted -eq 0 -and
        $results.Count -eq $case.expected) ('Incomplete test run: ' + $case.file)
    foreach ($result in $results) {
        Require ($result.outcome -ceq 'Passed') ('Non-passing test: ' + $result.testName)
        Require ($identities.Add([string]$result.testId)) ('Repeated test identity: ' + $result.testName)
    }
    [pscustomobject]@{file=('tests/' + $case.file); sha256=(Hash $path); scope=$case.scope;
        total=$results.Count; passed=[int]$counters.passed; failed=[int]$counters.failed;
        started=$trx.TestRun.Times.start; finished=$trx.TestRun.Times.finish}
})
Require ($identities.Count -eq 227) 'Unexpected distinct test count'
[xml]$afocal = Get-Content -LiteralPath (Join-Path $Root 'tests/afocal.trx') -Raw
$gridRows = @(foreach ($result in $afocal.TestRun.Results.UnitTestResult) {
    if ($result.testName -notlike '*FormalNormalizerRetainsOriginalGridOutcomes*') { continue }
    $log = [string]$result.Output.StdOut
    Require ($log -match '(?m)^([^\r\n]+): original=(Pass|Incomparable), maxAngularCoordinateError=\(([^,]+), ([^)]+)\) mrad') 'Missing original PSF outcome'
    [pscustomobject]@{id=$Matches[1]; conclusion=$Matches[2];
        maximumXErrorMrad=[double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture);
        maximumYErrorMrad=[double]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture)}
})
Require ($gridRows.Count -eq 20) 'Incomplete PSF contract coverage'
$automatic = @($gridRows | Where-Object { !$_.id.StartsWith('fixed-') })
$fixed = @($gridRows | Where-Object { $_.id.StartsWith('fixed-') })
Require ($automatic.Count -eq 10 -and @($automatic | Where-Object conclusion -eq Pass).Count -eq 4 -and
    @($automatic | Where-Object conclusion -eq Incomparable).Count -eq 6) 'Automatic-grid outcomes changed'
Require ($fixed.Count -eq 10 -and @($fixed | Where-Object conclusion -ne Pass).Count -eq 0) 'Fixed-grid regression'
$physics = @(foreach ($result in $afocal.TestRun.Results.UnitTestResult) {
    if ($result.testName -notlike '*AfocalIndependentPhysicsTests*') { continue }
    foreach ($line in ([string]$result.Output.StdOut -split '\r?\n')) {
        if (!$line.StartsWith('{')) { continue }
        $record = $line | ConvertFrom-Json
        if ($record.kind -in @('ideal-disk', 'production-huygens-airy', 'padding', 'rotation90')) { $record }
    }
})
Require ($physics.Count -eq 7) 'Missing independent physics records'
$frozen = Get-Content -LiteralPath (Join-Path $Root 'frozen-head-final.json') -Raw | ConvertFrom-Json
Require ($frozen.unchanged -and $frozen.count -eq 2668 -and $frozen.matched -eq 2668 -and
    $frozen.workingTree.unchanged) 'Frozen HEAD/working-copy evidence changed'
$coreFiles = @(
    'src/OpticalSystemDesign.Core/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tests/OpticalSystemDesign.ZemaxComparison.Tests/bin/Release/net10.0/OpticalSystemDesign.Core.dll',
    'tools/diagnostics/AfocalPsfContract20261028/bin/Release/net10.0/OpticalSystemDesign.Core.dll'
)
$binaries = @($coreFiles | ForEach-Object { [pscustomobject]@{path=$_; sha256=(Hash (Join-Path $repository $_))} })
Require (@($binaries.sha256 | Sort-Object -Unique).Count -eq 1) 'Mixed Core binaries'
$policyPath = 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
$policyHash = Hash (Join-Path $repository $policyPath)
Require ($policyHash -ceq '30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5') 'Original policy changed'
$report = [ordered]@{
    checkedAtUtc=[DateTime]::UtcNow.ToString('o'); sourceCommit=$frozen.commit;
    tests=$tests; distinctPassed=$identities.Count; binaries=$binaries;
    comparisonPolicySha256=$policyHash; psfGridChecks=$gridRows; independentPhysics=$physics;
    frozen=@{count=$frozen.count; unchanged=$frozen.unchanged; workingUnchanged=$frozen.workingTree.unchanged;
        aggregateSha256=$frozen.actualAggregateSha256; reportSha256=(Hash (Join-Path $Root 'frozen-head-final.json'))};
    scope=@{nativeRecapture=$false; complete150Recomputed=$false; full1159Plus52Recomputed=$false;
        productCodeChanged=$false; tolerancesChanged=$false;
        limitation='Passing tests include expected Incomparable outcomes and finite-data diagnostics; they are not 227 optical comparisons.'};
    incompleteAttempts=@(
        @{log='afocal-startup-incomplete.log'; reason='Restricted process did not start; cancelled before a TRX existed. Excluded from passing count.'},
        @{log='frozen-integrity.log'; reason='Absolute relocation-map argument rejected; repeated using the required repository-relative path. Excluded from integrity certification.'}
    )
}
$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $Root 'verification.json') -Encoding utf8
[pscustomobject]$report | Select-Object sourceCommit, distinctPassed, frozen | ConvertTo-Json -Depth 4
