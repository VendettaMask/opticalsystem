param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path)
$ErrorActionPreference = 'Stop'
$evidence = Join-Path $RepositoryRoot 'validation/rechecks/2026-10-31/review-fixes'
$rows = Get-Content (Join-Path $evidence 'replay/reproductions.json') -Raw | ConvertFrom-Json
$mirrors = @($rows | Where-Object Case -eq 'folded-mirror-quick-focus')
if ($mirrors.Count -ne 2) { throw 'Missing mirror controls.' }
foreach ($row in $mirrors) {
    if ($row.After -ge 0 -or [Math]::Abs($row.After - $row.Before - $row.PredictedBestShift) -gt 1e-12 -or
        $null -eq $row.ActualRms -or $row.ActualRms -ge .01 -or
        [Math]::Abs($row.ActualRms - $row.Result.RmsSpotRadius) -gt 1e-12) { throw 'Signed focus or actual RMS mismatch.' }
}
$numeric = $rows | Where-Object Case -eq 'camera-invalid-numeric-text'
if ($numeric.ResultProduced -or $null -ne $numeric.UsedExposureMilliseconds -or $numeric.TextAfterRun -ne 'not-a-number') {
    throw 'Invalid numeric text was not rejected.'
}
$languages = ($rows | Where-Object Case -eq 'camera-language-coverage').Results
if ($languages.Count -ne 2 -or ($languages | Where-Object Language -eq 'en-US').SaveExperiment -ne 'Save Complete Experiment' -or
    ($languages | Where-Object Language -eq 'ja-JP').SaveExperiment -ne '完全な実験を保存') { throw 'Experiment translations mismatch.' }
$latest = @{}
foreach ($name in @('fix-regressions.trx', 'fix-verified.trx', 'fix-final.trx', 'fix-isolated-final.trx')) {
    [xml]$trx = Get-Content (Join-Path $evidence "tests/$name") -Raw
    foreach ($result in $trx.TestRun.Results.UnitTestResult) { $latest[$result.testName] = $result.outcome }
}
if ($latest.Count -ne 54 -or @($latest.Values | Where-Object { $_ -ne 'Passed' }).Count) { throw 'Latest focused test coverage mismatch.' }
$assemblies = @{}
foreach ($name in @('OpticalSystemDesign.Core', 'OpticalSystemDesign.Application', 'OpticalSystemDesign.App')) {
    $tested = Join-Path $evidence "isolated/OpticalSystemDesign.Tests/bin/Release/net10.0/$name.dll"
    $replayed = Join-Path $evidence "isolated/CodeReview20261031/bin/Release/net10.0/$name.dll"
    $hash = (Get-FileHash -LiteralPath $tested -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath $replayed -Algorithm SHA256).Hash) { throw "Test/replay assembly mismatch: $name" }
    $assemblies[$name] = $hash
}
$build = Get-Content (Join-Path $evidence 'default-app-build.log') -Raw
if ($build -notmatch '已成功生成|Build succeeded' -or $build -match 'error (CS|MSB|NETSDK)\d+') { throw 'Default App build did not succeed.' }
$verified = [ordered]@{ Status='passed'; DistinctLatestTestsPassed=$latest.Count; FinalIsolatedTests=16;
    DefaultReleaseBuildPassed=$true; OriginalReproductionsFixed=$true; TestedAndReplayedAssemblies=$assemblies;
    Scope='Three review fixes; 49 default-output checks plus 16 final isolated checks, deduplicated. Not a whole-repository or native GUI certification.' }
[System.IO.File]::WriteAllText((Join-Path $evidence 'verification.json'), ($verified | ConvertTo-Json -Depth 4) + [Environment]::NewLine)
$verified | ConvertTo-Json -Depth 4
