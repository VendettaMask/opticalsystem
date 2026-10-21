param(
    [string]$Baseline = 'artifacts/zemax-sequential-corpus/20261021/full-v1',
    [string]$Output = 'validation/zemax/2026-r1/wide-schmidt-repair-2026-10-21',
    [string]$SchmidtMarginals = 'artifacts/validation/wide-schmidt-20261021/schmidt-native-v2-bits/capture/schmidt-marginals'
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Output) { throw 'Fresh output required.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in @('prior-pass-preservation.json', 'objectives-prior-pass-preservation.json')) {
    $audit = Get-Content (Join-Path (Split-Path $Baseline) $file) -Raw | ConvertFrom-Json -Depth 100
    foreach ($row in $audit.rows | Where-Object status -eq 'PreviouslyPassNowNotPass') { [void]$keys.Add($row.key) }
}
if ($keys.Count -ne 29) { throw 'The frozen regression union must contain exactly 29 requests.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$cases = @()
foreach ($item in (Get-Content (Join-Path $Baseline 'analysis-results.json') -Raw | ConvertFrom-Json -Depth 100)) {
    $r = $item.run.request
    if (!$r) { continue }
    $key = '{0}|{1}|{2}|{3}|{4}|{5}|{6}' -f $item.lens,$item.configuration,$item.scope,$r.canonicalAnalysisKey,$r.field,$r.wavelength,$r.pupilSampling
    if (!$keys.Contains($key)) { continue }
    $id = $item.run.directory
    $folder = Join-Path $Output $id
    New-Item -ItemType Directory -Path $folder | Out-Null
    $r | ConvertTo-Json -Depth 90 | Set-Content (Join-Path $folder 'request.json') -Encoding utf8
    foreach ($file in @('data.json','data.txt','captured-settings.json','request.json','model.json','unit-normalization.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $item.path "native/$file"))) {
            if ($file -eq 'unit-normalization.json') { continue }
            throw "Required native evidence missing: $file"
        }
        $target = if ($file -eq 'request.json') { 'native-request.json' } else { $file }
        Copy-Item -LiteralPath (Join-Path $item.path "native/$file") -Destination (Join-Path $folder $target)
    }
    $source = Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/' + $item.lens)
    $cases += [ordered]@{ id=$id; key=$key; lens=$item.lens; sourceSha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant(); baselineConclusion=$item.run.conclusion }
}
if ($cases.Count -ne 29) { throw 'Incomplete fixture selection.' }
$policy = Join-Path $repo 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json'
[ordered]@{ baseline=$Baseline; originalComparisonPolicySha256=(Get-FileHash $policy).Hash.ToLowerInvariant(); cases=$cases } | ConvertTo-Json -Depth 95 | Set-Content (Join-Path $Output 'cases.json') -Encoding utf8
$controls = Join-Path $Output 'schmidt-marginals'
New-Item -ItemType Directory -Path $controls | Out-Null
foreach ($file in @('ray-audit.json','ray-audit.json.bits.json')) {
    Copy-Item -LiteralPath (Join-Path $SchmidtMarginals $file) -Destination (Join-Path $controls $file)
}
$guard = Get-Content (Join-Path $Baseline 'analysis-results.json') -Raw | ConvertFrom-Json -Depth 100 |
    Where-Object { $_.lens -eq 'Telescopes/Unobscured Gregorian.zmx' -and $_.run.key -eq 'RMS vs Field' }
$guardFolder = Join-Path $Output 'gregorian-rms-guard'
New-Item -ItemType Directory -Path $guardFolder | Out-Null
$guard.run.request | ConvertTo-Json -Depth 90 | Set-Content (Join-Path $guardFolder 'request.json') -Encoding utf8
foreach ($file in @('data.json','data.txt')) { Copy-Item -LiteralPath (Join-Path $guard.path ('native/' + $file)) -Destination (Join-Path $guardFolder $file) }
[ordered]@{ lens=$guard.lens; sourceSha256=(Get-FileHash -LiteralPath (Join-Path $repo ('src/OpticalSystemDesign.App/Assets/SequentialExamples/' + $guard.lens))).Hash.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content (Join-Path $guardFolder 'source.json') -Encoding utf8
$files = @(Get-ChildItem -LiteralPath $Output -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=[IO.Path]::GetRelativePath((Join-Path $repo $Output),$_.FullName).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
[ordered]@{ files=$files } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $Output 'manifest.json') -Encoding utf8
Write-Output ('Prepared {0} immutable captured requests, {1} files.' -f $cases.Count,$files.Count)
