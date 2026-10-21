$ErrorActionPreference = 'Stop'
$root = (Resolve-Path '.').Path
$target = Join-Path $root 'validation/zemax/2026-r1/objectives-energy32-repair-2026-10-21'
if (Test-Path -LiteralPath $target) { throw 'Fresh fixture directory required.' }
New-Item -ItemType Directory -Path $target | Out-Null
$items = Get-Content 'artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json' -Raw | ConvertFrom-Json
$jobs = (Get-Content 'artifacts/validation/objectives32-continuation-20261021/native-bits-request.json' -Raw | ConvertFrom-Json).jobs
$cases = @(foreach ($job in $jobs) {
    $item = $items | Where-Object { $_.lens -eq $job.lens -and $_.run.key -eq 'Diffraction Encircled Energy' }
    if (@($item).Count -ne 1) { throw "Ambiguous case $($job.id)" }
    $folder = Join-Path $target $job.id
    New-Item -ItemType Directory -Path $folder | Out-Null
    foreach ($name in @('data.json', 'request.json', 'captured-settings.json', 'environment.json', 'model.json', 'unit-normalization.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $item.path "native/$name"))) { continue }
        Copy-Item -LiteralPath (Join-Path $item.path "native/$name") -Destination $folder
    }
    Copy-Item -LiteralPath "artifacts/validation/objectives32-continuation-20261021/native-active-parameters-v2/capture/$($job.id)/refractive-indices.json" -Destination $folder
    [ordered]@{ id=$job.id; lens=$job.lens; sourceSha256=(Get-FileHash (Join-Path 'src/OpticalSystemDesign.App/Assets/SequentialExamples' $job.lens)).Hash.ToLowerInvariant(); original32Conclusion= $(if ($job.id -in @('apochromat2','apochromat4','doublet-using-mil-number-glasses')) {'Difference'} else {'Pass'}); originalCapture=$item.path.Substring($root.Length+1).Replace('\','/')+'/native'; currentConclusion='Pass' }
})
$manifest = [ordered]@{
    schemaVersion=1; date='2026-10-21'; status='original32-repaired'; supersedesUnresolved32Status='objectives-energy128-acceptance-2026-10-21/acceptance.json'; originalComparisonPolicySha256=(Get-FileHash 'tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json').Hash.ToLowerInvariant(); pupilSampling=32; imageSampling=64; numPoints=401; changedTolerance=$false; replacedOriginalNativeBytes=$false; cases=$cases
}
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $target 'cases.json') -Encoding utf8NoBOM
$files = @(Get-ChildItem -LiteralPath $target -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=$_.FullName.Substring($target.Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
[ordered]@{schemaVersion=1; description='Exact copies of original native 32 captures, plus independent GetIndex controls; originals preserved.'; files=$files} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding utf8NoBOM
Write-Output "Frozen $($cases.Count) cases, $($files.Count) files."
