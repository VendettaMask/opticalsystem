param([switch]$Remaining)
$ErrorActionPreference = 'Stop'
$root = 'D:/Projects/psf1107'
$repo = 'D:/Projects/opticalsystem'
$baseline = "$repo/artifacts/validation/pq1107/full"
$rows = Get-Content "$baseline/rows.json" -Raw | ConvertFrom-Json
$models = Get-Content "$baseline/models.json" -Raw | ConvertFrom-Json
$ledger = Import-Csv "$repo/validation/rechecks/2026-11-07/incomparable-coverage/by-request.csv"
New-Item -ItemType Directory -Force -Path $root,"$root/requests","$root/core" | Out-Null
$ids = @(309,555,787,1203,1291,1365,2431,2971)
$suffix = ''
if ($Remaining) {
    $ids = @($ledger | Where-Object { $_.evidenceScope -eq 'NoSameContractHigherReferenceFound' -and $_.analysis -eq 'PSF' -and [int]$_.index -notin $ids } | ForEach-Object { [int]$_.index })
    if ($ids.Count -ne 26) { throw "Expected 26 remaining PSFs; found $($ids.Count)." }
    $suffix = '-remaining'
}
$nativeJobs = @()
$coreJobs = @()
$manifest = foreach ($id in $ids) {
    $record = $ledger | Where-Object index -eq $id
    $row = $rows[$id - 1]
    $model = $models[$row.lensIndex - 1]
    if ($record.evidenceScope -ne 'NoSameContractHigherReferenceFound' -or $record.analysis -ne 'PSF' -or
        $row.index -ne $id -or $row.key -ne 'PSF' -or $model.lens -ne $row.lens -or $model.lens -match '123456') {
        throw 'Expected one of the still pending menu PSFs'
    }
    $requestPath = "$($row.legacyPath)/native/request.json"
    $frozen = Get-Content $requestPath -Raw | ConvertFrom-Json -AsHashtable
    if ($frozen.imageSampling -ne 128 -or $frozen.imageDeltaMicrometers -ne 0.25 -or $frozen.pupilSampling -ne 64 -or
        (Get-FileHash -LiteralPath $frozen.input).Hash -ne $model.sourceSha256) { throw 'Native physical identity differs' }
    foreach ($sampling in 256,512) {
        $native = Get-Content $requestPath -Raw | ConvertFrom-Json -AsHashtable
        $native.pupilSampling = $sampling
        $nativePath = "$root/requests/$id-$sampling.json"
        $native | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $nativePath -Encoding utf8
        $nativeJobs += @{id="$id-$sampling";request=$nativePath;output="$root/$id-$sampling"}
    }
    $job = Get-Content $row.job -Raw | ConvertFrom-Json -AsHashtable
    $job.outputDirectory = "$root/core/$id"
    $job.request.workbenchSettings.FourierSamplingMode = 'Adaptive'
    $job.request.workbenchSettings.FourierMaximumSampling = '2048'
    $job.request.workbenchSettings.FourierRelativeAccuracy = '.001'
    $jobPath = "$root/requests/$id-adaptive.json"
    $job | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $jobPath -Encoding utf8
    $coreJobs += @{id=$id;job=$jobPath}
    @{id=$id; lens=$row.lens; settings=$row.settings; request=(Get-Content $row.job -Raw | ConvertFrom-Json -AsHashtable).request;
        originalNativeDirectory="$($row.legacyPath)/native"; inputSha256=$model.sourceSha256; originalRequestFingerprint=$record.requestFingerprint}
}
ConvertTo-Json -InputObject $nativeJobs -Depth 8 | Set-Content "$root/native-jobs$suffix.json" -Encoding utf8
ConvertTo-Json -InputObject $coreJobs -Depth 8 | Set-Content "$root/core-jobs$suffix.json" -Encoding utf8
ConvertTo-Json -InputObject @($manifest) -Depth 16 | Set-Content "$root/rows$suffix.json" -Encoding utf8
