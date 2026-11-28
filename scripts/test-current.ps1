param(
    [ValidateSet('Main', 'Comparison')][string]$Suite = 'Main',
    [string]$Filter,
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [Parameter(ValueFromRemainingArguments)][string[]]$TestArguments
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'verify-acceptance-scope.ps1') -RepositoryRoot $root | Out-Host
$policy = Get-Content -LiteralPath (Join-Path $root 'validation/acceptance-policy.json') -Raw | ConvertFrom-Json
$scope = $policy.testFilter
if ($Filter) { $scope = "($scope)&($Filter)" }
$project = if ($Suite -eq 'Main') { 'tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj' }
    else { 'tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj' }
$arguments = @('test', (Join-Path $root $project), '-c', $Configuration, '--filter', $scope)
if ($NoBuild) { $arguments += '--no-build' }
& dotnet @arguments @TestArguments
exit $LASTEXITCODE
