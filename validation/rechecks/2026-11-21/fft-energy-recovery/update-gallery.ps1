$ErrorActionPreference = 'Stop'
$energyScope = $PSScriptRoot
$energyPicture = Join-Path $energyScope 'workbench\diffraction-encircled-energy.png'
$energyPictureHash = (Get-FileHash -LiteralPath $energyPicture).Hash.ToLowerInvariant()
$energyCapture = (Get-Content (Join-Path $energyScope 'workbench\manifest.json') -Raw | ConvertFrom-Json).Results |
    Where-Object Key -eq 'Diffraction Encircled Energy' | Select-Object -Last 1
$energyReport = (Get-Content (Join-Path $energyScope 'recovered\Automatic.json') -Raw | ConvertFrom-Json).data.Values.FourierSamplingValidation
if ($energyCapture.Outcome -ne 'Success' -or -not $energyReport.Validated) { throw 'Final capture or numerical validation did not succeed' }
$energyNote = '软件侧已按需从 64×64 加密至 2048×2048，显示全部 3 个视场和衍射极限；波长选择与原 0–20 µm 范围保持。相邻两档最大相对 RMS 变化 0.0844%，另经 4096 档独立复查，最大相对 RMS 差 0.0470%。右侧保留原低采样截图及“取样太低，数据不准确”提示，不作为已收敛的数值参考。'
$energyGalleryRoot = Join-Path (Split-Path $energyScope -Parent) 'all-output-visual-comparison'
$energyGalleryReceipts = @()
foreach ($energyGallery in @($energyGalleryRoot, (Join-Path $energyGalleryRoot 'representative-six'))) {
    $energyData = Get-Content (Join-Path $energyGallery 'gallery-data.json') -Raw | ConvertFrom-Json
    $energyLens = $energyData.lenses | Where-Object name -eq 'double-gauss'
    if ((Get-FileHash -LiteralPath $energyLens.source).Hash.ToLowerInvariant() -ne $energyLens.sha256) { throw 'Source lens has changed' }
    $energyEntry = $energyLens.results.'Diffraction Encircled Energy'
    $energyCurrent = [System.IO.Path]::GetFullPath((Join-Path $energyGallery $energyEntry.current))
    if (-not $energyCurrent.StartsWith($energyGallery + '\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Image target escapes gallery' }
    $energyPreviousHash = (Get-FileHash -LiteralPath $energyCurrent).Hash.ToLowerInvariant()
    if ($energyPreviousHash -eq $energyPictureHash) {
        $energyPreviousHash = '2cb118c309aa355db346fb8f591059deae8ca7abc3351219fe177dcdbd3e74e1'
    }
    $energyArchived = Join-Path $energyScope ('original-diagnostics\' + $energyPreviousHash + '.png')
    if (-not (Test-Path -LiteralPath $energyArchived)) { Copy-Item -LiteralPath $energyCurrent -Destination $energyArchived }
    if ((Get-FileHash -LiteralPath $energyArchived).Hash.ToLowerInvariant() -ne $energyPreviousHash) { throw 'Original image archive hash mismatch' }
    $energyNative = Join-Path $energyGallery $energyEntry.native
    $energyNativeHash = (Get-FileHash -LiteralPath $energyNative).Hash.ToLowerInvariant()
    if ((Get-FileHash -LiteralPath $energyCurrent).Hash.ToLowerInvariant() -ne $energyPictureHash) {
        Remove-Item -LiteralPath $energyCurrent
        try { New-Item -ItemType HardLink -Path $energyCurrent -Value $energyPicture | Out-Null }
        catch { Copy-Item -LiteralPath $energyPicture -Destination $energyCurrent }
    }
    $energyManifestFile = Join-Path $energyGallery 'double-gauss\workbench\manifest.json'
    $energyManifest = Get-Content $energyManifestFile -Raw | ConvertFrom-Json
    $energyGalleryName = if ($energyGallery.EndsWith('representative-six')) { 'representative-six' } else { 'original-three' }
    $energyOldManifest = Join-Path $energyScope ('original-diagnostics\' + $energyGalleryName + '-manifest-entry.json')
    if (-not (Test-Path -LiteralPath $energyOldManifest)) {
        @($energyManifest.Results | Where-Object Key -eq 'Diffraction Encircled Energy') | ConvertTo-Json -Depth 20 |
            Set-Content $energyOldManifest -Encoding utf8NoBOM
    }
    foreach ($energyOldResult in $energyManifest.Results | Where-Object Key -eq 'Diffraction Encircled Energy') {
        $energyUpdates = @{ Status = 'captured'; Outcome = 'Success'; Reason = $null; Error = $null;
            Seconds = $energyCapture.Seconds; Note = $energyNote; FourierSamplingValidation = $energyReport }
        foreach ($energyUpdate in $energyUpdates.GetEnumerator()) {
            $energyOldResult | Add-Member -NotePropertyName $energyUpdate.Key -NotePropertyValue $energyUpdate.Value -Force
        }
    }
    $energyManifest | ConvertTo-Json -Depth 40 | Set-Content $energyManifestFile -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $energyScope 'workbench\diffraction-encircled-energy-settings.json') -Destination (Join-Path $energyGallery 'double-gauss\workbench\diffraction-encircled-energy-settings.json')
    $energyGalleryReceipts += [pscustomobject]@{ gallery = $energyGallery; previousImageSha256 = $energyPreviousHash;
        newImageSha256 = (Get-FileHash -LiteralPath $energyCurrent).Hash.ToLowerInvariant(); nativeSha256Before = $energyNativeHash;
        nativeSha256After = (Get-FileHash -LiteralPath $energyNative).Hash.ToLowerInvariant(); sourceUnchanged = $true; canonicalImage = $energyPicture }
}
$energyGalleryReceipts | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $energyScope 'gallery-update.json') -Encoding utf8NoBOM
foreach ($energyGallery in @($energyGalleryRoot, (Join-Path $energyGalleryRoot 'representative-six'))) {
    python (Join-Path $energyGalleryRoot 'capture\build-gallery.py') $energyGallery
    if ($LASTEXITCODE -ne 0) { throw 'Gallery build failed' }
}
