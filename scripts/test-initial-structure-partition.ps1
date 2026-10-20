#requires -Version 7.0
param(
    [ValidateSet('contracts', 'survey-first', 'survey-second')][string]$Partition = 'contracts',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory,
    [string]$HangTimeout = '35m',
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repository 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/FlatStartSearchTests.cs'
$method = 'FrozenSpecificationSurveyKeepsTargetsAndReportsActualFamilyOutcomes'
$source = [IO.File]::ReadAllText($sourcePath)
$methodOffset = $source.IndexOf('public async Task ' + $method, [StringComparison]::Ordinal)
if ($methodOffset -lt 0) { throw 'Cannot locate the complete frozen survey.' }
$theoryOffset = $source.LastIndexOf('[Theory]', $methodOffset, [StringComparison]::Ordinal)
if ($theoryOffset -lt 0) { throw 'Cannot locate the survey data.' }
$names = @([regex]::Matches($source.Substring($theoryOffset, $methodOffset - $theoryOffset), '\[InlineData\("([a-zA-Z0-9-]+\.json)"\)\]') |
    ForEach-Object { $_.Groups[1].Value })
if ($names.Count -eq 0 -or ($names | Sort-Object -Unique).Count -ne $names.Count) { throw 'Survey cases must be nonempty and unique.' }
$split = [int][Math]::Ceiling($names.Count / 2.0)
$selected = switch ($Partition) {
    'survey-first' { @($names | Select-Object -First $split) }
    'survey-second' { @($names | Select-Object -Skip $split) }
    default { @() }
}
$surveyFilter = 'FullyQualifiedName~FlatStartSearchTests.' + $method
$filter = if ($Partition -eq 'contracts') { 'FullyQualifiedName!~FlatStartSearchTests.' + $method }
    else { '(' + $surveyFilter + ')&(' + (($selected | ForEach-Object { 'DisplayName~' + $_ }) -join '|') + ')' }
$plan = [ordered]@{ partition = $Partition; filter = $filter; allSurveyCases = $names; selectedSurveyCases = @($selected) }
if ($PlanOnly) { $plan | ConvertTo-Json -Depth 4; return }
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { throw 'A dedicated results directory is required.' }

$results = [IO.Path]::GetFullPath($ResultsDirectory)
[void][IO.Directory]::CreateDirectory($results)
$logName = 'initial-' + $Partition + '.trx'
Push-Location $repository
try {
    & dotnet test 'labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj' `
        -c $Configuration --no-build --no-restore --filter $filter --logger ('trx;LogFileName=' + $logName) `
        --results-directory $results --blame-hang --blame-hang-timeout $HangTimeout /m:1 /nr:false
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) { exit $testExit }
    [xml]$trx = [IO.File]::ReadAllText((Join-Path $results $logName))
    $cases = @($trx.TestRun.Results.UnitTestResult)
    if ($trx.TestRun.ResultSummary.outcome -ne 'Completed' -or $cases.Count -eq 0 `
        -or @($cases | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
        throw 'The selected partition did not complete with every selected case passing.'
    }
    if ($Partition -ne 'contracts') {
        if ($cases.Count -ne $selected.Count) { throw 'Survey partition coverage changed or the filter selected no tests.' }
        foreach ($name in $selected) {
            if (@($cases | Where-Object { $_.testName.Contains('"' + $name + '"') }).Count -ne 1) {
                throw ('Missing or duplicate survey result: ' + $name)
            }
        }
    }
    elseif (@($cases | Where-Object { $_.testName.Contains($method) }).Count -ne 0) { throw 'The regular partition overlapped the survey.' }
}
finally { Pop-Location }
