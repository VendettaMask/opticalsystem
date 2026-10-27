#requires -Version 7.0
param(
    [string]$ResultsDirectory = 'artifacts/cloud-verification',
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj'
[void][IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($ResultsDirectory))
# Keep every discovered case, but do not carry native Skia state from the editor
# cases into the complete Dock workspace. No assertion or sampling is changed.
$discovery = @(& dotnet test $project -c $Configuration --no-build --no-restore --list-tests --filter 'FullyQualifiedName~LensConfigurationNavigationTests' 2>&1)
if ($LASTEXITCODE -ne 0) { throw 'Lens navigation discovery failed.' }
$pattern = '^\s*(OpticalSystemDesign\.Tests\.LensConfigurationNavigationTests\.[A-Za-z0-9_]+)(?:\(|\s*$)'
$cases = @($discovery | ForEach-Object {
    if ([string]$_ -match $pattern) { [pscustomobject]@{ Method = $Matches[1]; Name = ([string]$_).Trim() } }
})
if ($cases.Count -eq 0) { throw 'No lens navigation cases were discovered.' }
$groups = @($cases | Group-Object Method | Sort-Object Name)
$records = @()
$index = 0
foreach ($group in $groups) {
    $index++
    $name = 'lens-navigation-{0:D2}.trx' -f $index
    & dotnet test $project -c $Configuration --no-build --no-restore --filter "FullyQualifiedName=$($group.Name)" --blame --logger "trx;LogFileName=$name" --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw "Lens navigation method failed or aborted: $($group.Name)" }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $ResultsDirectory $name) -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne $group.Count -or [int]$counters.passed -ne $group.Count -or
        [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0 -or
        $trx.TestRun.ResultSummary.outcome -ne 'Completed') {
        throw "Lens navigation coverage differs from discovery: $($group.Name)"
    }
    $records += [pscustomobject]@{ Method = $group.Name; Expected = $group.Count; Passed = [int]$counters.passed; Trx = $name }
}
$summary = [ordered]@{ DiscoveredCases = $cases.Count; IsolatedMethods = $groups.Count; Passed = ($records | Measure-Object Passed -Sum).Sum; Methods = $records }
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'lens-navigation-isolation.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 5
