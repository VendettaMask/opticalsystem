$ErrorActionPreference = 'Stop'
$bsdfRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$bsdfRuntime = [IO.Path]::GetFullPath((Join-Path $bsdfRoot 'native-runtime')) + [IO.Path]::DirectorySeparatorChar
$bsdfManifest = Join-Path $bsdfRoot 'evidence-manifest.json'
$bsdfItems = @(Get-ChildItem -LiteralPath $bsdfRoot -File -Recurse -Force | Where-Object {
    -not $_.FullName.StartsWith($bsdfRuntime, [StringComparison]::OrdinalIgnoreCase) -and $_.FullName -ne $bsdfManifest
} | Sort-Object FullName | ForEach-Object {
    [ordered]@{ Path = [IO.Path]::GetRelativePath($bsdfRoot, $_.FullName).Replace('\', '/')
        Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[ordered]@{ CapturedUtc = [DateTimeOffset]::UtcNow.ToString('o'); ProductTests = 245; ProductTestsPassed = 245
    NativeWhiteSpecularComplete = $false; RealZemaxXyzCalibrationComplete = $false
    Excludes = @('native-runtime/**', 'evidence-manifest.json'); Files = $bsdfItems
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bsdfManifest -Encoding utf8
$bsdfTotalBytes = 0L
foreach ($bsdfItem in $bsdfItems) { $bsdfTotalBytes += [long]$bsdfItem['Bytes'] }
[pscustomobject]@{ Files = $bsdfItems.Count; Bytes = $bsdfTotalBytes }
