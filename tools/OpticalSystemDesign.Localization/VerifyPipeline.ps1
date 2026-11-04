param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$localizationTool = Join-Path $PSScriptRoot 'bin/Release/net10.0/OpticalSystemDesign.Localization.dll'
if (-not (Test-Path -LiteralPath $localizationTool)) { throw 'Build the localization tool in Release first.' }
$localizationOutput = [IO.Path]::GetFullPath($OutputDirectory)
$localizationFixture = Join-Path $localizationOutput 'fixture'
$localizationApp = Join-Path $localizationFixture 'src/OpticalSystemDesign.App'
$localizationInputs = Join-Path $localizationFixture 'localization'
foreach ($localizationDirectory in @($localizationOutput, $localizationApp, (Join-Path $localizationApp 'Controls'), $localizationInputs)) {
    [IO.Directory]::CreateDirectory($localizationDirectory) | Out-Null
}
$localizationSeed = 'class UiLocalization { static object BuildTexts() { void Add(string source, string en, string ja) {} Add("保存", "Save", "保存"); Add("厚度 1.25 nm", "Thickness 1.25 nm", "膜厚 1.25 nm"); return new object(); } }'
$localizationView = 'class View { void Build() { var button = new Button { Content = "保存" }; } }'
$localizationSeedPath = Join-Path $localizationApp 'UiLocalization.cs'
$localizationViewPath = Join-Path $localizationApp 'View.cs'
$localizationTermsPath = Join-Path $localizationInputs 'optical-terms.tsv'
[IO.File]::WriteAllText($localizationSeedPath, $localizationSeed)
[IO.File]::WriteAllText($localizationViewPath, $localizationView)
[IO.File]::WriteAllText($localizationTermsPath, "辐照度`tIrradiance`t放射照度`n")
$localizationResults = [Collections.Generic.List[object]]::new()
function Invoke-LocalizationControl([string]$Name, [string]$Command, [bool]$ShouldPass, [string]$Diagnostic = '') {
    $localizationStart = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
    $localizationStart.UseShellExecute = $false
    $localizationStart.CreateNoWindow = $true
    $localizationStart.RedirectStandardOutput = $true
    $localizationStart.RedirectStandardError = $true
    foreach ($localizationArgument in @($localizationTool, $Command, $localizationFixture)) { $localizationStart.ArgumentList.Add($localizationArgument) }
    $localizationProcess = [Diagnostics.Process]::Start($localizationStart)
    $localizationStandard = $localizationProcess.StandardOutput.ReadToEndAsync()
    $localizationError = $localizationProcess.StandardError.ReadToEndAsync()
    if (-not $localizationProcess.WaitForExit(30000)) { $localizationProcess.Kill($true); throw 'Localization control timed out.' }
    $localizationText = $localizationStandard.GetAwaiter().GetResult() + $localizationError.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $localizationOutput ($Name + '.log')), $localizationText)
    $localizationValid = (($localizationProcess.ExitCode -eq 0) -eq $ShouldPass) -and (!$Diagnostic -or $localizationText.Contains($Diagnostic))
    $localizationResults.Add([pscustomobject]@{ Name=$Name; ExitCode=$localizationProcess.ExitCode; ExpectedSuccess=$ShouldPass; Passed=$localizationValid })
    $localizationProcess.Dispose()
    if (-not $localizationValid) { throw "Localization control failed: $Name" }
}
Invoke-LocalizationControl 'baseline-sync' 'sync' $true
Invoke-LocalizationControl 'baseline-check' 'check' $true
$localizationCatalog = Join-Path $localizationApp 'Assets/Localization/ui.catalog.json'
$localizationCatalogBytes = [IO.File]::ReadAllBytes($localizationCatalog)
[IO.File]::WriteAllText($localizationViewPath, $localizationView.Replace('"保存"', '"未翻译的新增控件"'))
Invoke-LocalizationControl 'missing-label' 'check' $false 'Untranslated UI'
Invoke-LocalizationControl 'missing-label-sync' 'sync' $false 'Untranslated UI'
if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$localizationCatalogBytes, [byte[]][IO.File]::ReadAllBytes($localizationCatalog))) { throw 'Failed sync overwrote the valid catalog.' }
[IO.File]::WriteAllText($localizationViewPath, $localizationView)
[IO.File]::WriteAllText($localizationCatalog, [Text.Encoding]::UTF8.GetString($localizationCatalogBytes).Replace('Save', 'Damaged'))
Invoke-LocalizationControl 'stale-catalog' 'check' $false 'Local catalog is stale'
[IO.File]::WriteAllBytes($localizationCatalog, $localizationCatalogBytes)
[IO.File]::WriteAllText($localizationSeedPath, $localizationSeed.Replace('Thickness 1.25 nm', 'Thickness 1.25 mm'))
Invoke-LocalizationControl 'changed-unit' 'sync' $false 'unit symbol mismatch'
[IO.File]::WriteAllText($localizationSeedPath, $localizationSeed)
[IO.File]::WriteAllText((Join-Path $localizationInputs 'ui-translations.tsv'), "辐照度`tIlluminance`t照度`n")
Invoke-LocalizationControl 'conflicting-term' 'check' $false 'Optical term override conflicts'
[IO.File]::WriteAllText((Join-Path $localizationInputs 'ui-translations.tsv'), '')
Invoke-LocalizationControl 'restored-check' 'check' $true
$localizationResults | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $localizationOutput 'results.json')
Write-Output "$($localizationResults.Count) localization command controls passed."
