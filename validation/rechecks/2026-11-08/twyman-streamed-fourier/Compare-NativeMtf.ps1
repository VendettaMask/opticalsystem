[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Output,
    [string] $ProductResult
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $Repository 'src/OpticalSystemDesign.App/Assets/SequentialExamples/Optical testing/Twyman Green Interferometer.zmx'
$payloads = Get-Content -LiteralPath "$PSScriptRoot/native-mtf-payloads.json" -Raw | ConvertFrom-Json
function Read-VerifiedPayload([string] $LogicalPath) {
    $entry = $payloads | Where-Object logicalPath -CEQ $LogicalPath
    if (!$entry) { throw "Unknown native payload $LogicalPath" }
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot $entry.archive))
    try {
        $reader = $zip.GetEntry($entry.member).Open()
        $bytes = [IO.MemoryStream]::new()
        try {
            $reader.CopyTo($bytes)
            $raw = $bytes.ToArray()
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw)).ToLowerInvariant()
            if ($hash -cne $entry.sha256) { throw "Native payload hash differs for $LogicalPath" }
            [pscustomobject]@{ hash = $hash; data = [Text.Encoding]::UTF8.GetString($raw) | ConvertFrom-Json -Depth 80 }
        }
        finally { $reader.Dispose(); $bytes.Dispose() }
    }
    finally { $zip.Dispose() }
}
$caps = @(foreach ($n in 1024, 2048) {
    $nativePayload = Read-VerifiedPayload "2159-$n/data.json"
    $settingsPayload = Read-VerifiedPayload "2159-$n/captured-settings.json"
    $modelPayload = Read-VerifiedPayload "2159-$n/model.json"
    $diagnosticPayload = Read-VerifiedPayload "2159-$n/result-diagnostics.json"
    $batchPayload = Read-VerifiedPayload "2159-$n/batch-result.json"
    $native = $nativePayload.data
    $settings = $settingsPayload.data
    $diagnostics = $diagnosticPayload.data
    $series = $native.dataSeries[0]
    if ($native.dataSeries.Count -ne 1 -or $series.x.Count -ne 300 -or $series.y.Count -ne 300 -or
        $series.seriesLabels.Count -ne 2 -or $diagnostics.messages.Count -ne 0 -or
        $settings.properties.SampleSize -cne "S_$($n)x$n" -or $settings.properties.MaximumFrequency -ne 50 -or
        $settings.selectors.Field -ne 1 -or $settings.selectors.Wavelength -ne 1 -or $settings.selectors.Surface -ne 0 -or
        $settings.properties.UsePolarization -or $settings.properties.Type -cne 'Modulation' -or
        $batchPayload.data.exitCode -ne 0) { throw "Unexpected capture for $n" }
    [pscustomobject]@{
        series = $series
        request = $settings.request
        receipt = [ordered] @{
            pupilSampling = $n
            archiveManifest = 'native-mtf-payloads.json'
            files = [ordered] @{
                data = $nativePayload.hash
                settings = $settingsPayload.hash
                model = $modelPayload.hash
                diagnostics = $diagnosticPayload.hash
                batch = $batchPayload.hash
            }
        }
    }
})
if ($caps[0].receipt.files.model -cne $caps[1].receipt.files.model -or
    $caps[0].request.apodization -cne $caps[1].request.apodization) {
    throw 'Native model or apodization identity differs'
}
$metrics = @(for ($direction = 0; $direction -lt 2; $direction++) {
    $peak = 0.0
    $squares = 0.0
    $largestError = 0.0
    $pointwise = $true
    for ($i = 0; $i -lt 300; $i++) {
        $first = $caps[0].series
        $second = $caps[1].series
        $frequency = 50 * $i / 299.0
        if ([Math]::Abs($first.x[$i] - $frequency) -gt 1e-12 -or
            [Math]::Abs($second.x[$i] - $frequency) -gt 1e-12) {
            throw 'MTF physical coordinate differs'
        }
        $ref = [double]$second.y[$i][$direction]
        $errorValue = [Math]::Abs([double]$first.y[$i][$direction] - $ref)
        if (-not [double]::IsFinite($errorValue)) { throw 'Nonfinite MTF' }
        $peak = [Math]::Max($peak, [Math]::Abs($ref))
        $largestError = [Math]::Max($largestError, $errorValue)
        $squares += $errorValue * $errorValue
        $pointwise = $pointwise -and ($errorValue -le .001 + .001 * [Math]::Abs($ref))
    }
    $nrmse = [Math]::Sqrt($squares / 300) / [Math]::Max($peak, .001)
    [ordered]@{
        direction = @('Tangential', 'Sagittal')[$direction]
        coordinates = 300
        coverage = 1
        maximumAbsoluteError = $largestError
        nrmse = $nrmse
        pointwisePass = $pointwise
        pass = $pointwise -or $nrmse -le .01
    }
})
$receipt = [ordered]@{
    frozenRequest = 2159
    status = 'NativeConverged; product independent sampling still open'
    sourceLensSha256 = (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
    physicalAxis = [ordered] @{ quantity = 'SpatialFrequency'; unit = 'CyclesPerMillimeter'; start = 0; end = 50 }
    originalTolerance = [ordered] @{ absolute = .001; relative = .001; nrmse = .01; closeNrmse = .03; minimumCoverage = .95 }
    settings = [ordered] @{ field = 1; wavelength = 1; modulation = $true; polarized = $false; apodization = $caps[0].request.apodization }
    nativeStages = $caps.receipt
    convergence = $metrics
}
if ($metrics | Where-Object { -not $_.pass }) { throw 'Native MTF did not independently converge' }
if ($ProductResult) {
    $product = Get-Content -LiteralPath $ProductResult -Raw | ConvertFrom-Json -Depth 80
    $settings = $product.parameters
    if ($product.sourceSnapshotSha256 -cne 'e0a8e954c24b6d12150cae65f55d66898c617110b963c022a43fff0b34e32cf2' -or
        $settings.Sampling -cne '64' -or $settings.MaximumFrequency -cne '50' -or
        $settings.FieldNumber -cne '1' -or $settings.WavelengthNumber -cne '1' -or
        $settings.SurfaceNumber -cne '-1' -or $settings.Type -cne '调制' -or
        [bool]::Parse($settings.UsePolarization) -or [bool]::Parse($settings.UseRayAiming) -or
        [bool]::Parse($settings.ShowDiffractionLimit)) { throw 'The original product settings or source snapshot differ' }
    $certificate = $product.Values.FourierSamplingValidation
    $streamedSteps = @($certificate.Steps | Where-Object UsesStreamedPhysicalPupil)
    if ($product.Outcome -cne 'Success' -or -not $certificate.Validated -or
        $certificate.Protocol -cne 'FourierPhysicalDomainConvergence/v3' -or
        $certificate.Mode -cne 'Adaptive' -or $certificate.RequestedPupilSampling -ne 64 -or
        $certificate.RelativeRmsAccuracy -ne .001 -or $product.series.Count -ne 2 -or
        $streamedSteps.Count -ne 2 -or $streamedSteps[0].PupilSampling -ge $streamedSteps[1].PupilSampling -or
        $certificate.ReturnedPupilSampling -ne $streamedSteps[1].PupilSampling -or
        @($streamedSteps | Where-Object {
            -not $_.PhaseResolved -or $_.MaximumPupilPhaseStepWaves -le 0 -or $_.MaximumPupilPhaseStepWaves -gt .5
        }).Count -ne 0) { throw 'Unexpected streamed product result or convergence certificate' }
    $checks = @(for ($direction = 0; $direction -lt 2; $direction++) {
        $label = @('Tangential', 'Sagittal')[$direction]
        $curve = $product.series[$direction]
        if ($curve.Name -cne "Field 1, $($label.Substring(0, 1))" -or $curve.XQuantity -cne 'SpatialFrequency' -or
            $curve.XUnit -cne 'CyclesPerMillimeter' -or $curve.YQuantity -cne 'Modulation' -or
            $curve.YUnit -cne 'Dimensionless' -or $curve.x.Count -ne 300 -or $curve.y.Count -ne 300) {
            throw "Unexpected product direction, quantity, units or knot count for $label"
        }
        $peak = 0.0
        $squares = 0.0
        $largestError = 0.0
        $pointwise = $true
        for ($i = 0; $i -lt 300; $i++) {
            $native = $caps[1].series
            if ([Math]::Abs([double]$curve.x[$i] - [double]$native.x[$i]) -gt 1e-12) {
                throw "Product frequency coordinate differs at $label knot $i"
            }
            $ref = [double]$native.y[$i][$direction]
            $errorValue = [Math]::Abs([double]$curve.y[$i] - $ref)
            if (-not [double]::IsFinite($errorValue)) { throw "Nonfinite MTF at $label knot $i" }
            $peak = [Math]::Max($peak, [Math]::Abs($ref))
            $squares += $errorValue * $errorValue
            $largestError = [Math]::Max($largestError, $errorValue)
            $pointwise = $pointwise -and ($errorValue -le .001 + .001 * [Math]::Abs($ref))
        }
        $nrmse = [Math]::Sqrt($squares / 300) / [Math]::Max($peak, .001)
        [ordered]@{
            direction = $label
            coordinates = 300
            coverage = 1
            maximumAbsoluteError = $largestError
            nrmse = $nrmse
            pointwisePass = $pointwise
            pass = $pointwise -or $nrmse -le .01
        }
    })
    $passes = @($checks | Where-Object { -not $_.pass }).Count -eq 0
    $receipt.status = if ($passes) { 'ConvergedProductAndNativeCrossPass' } else { 'NativeConvergedProductCrossFail' }
    $rawDataHash = (Get-FileHash -LiteralPath $ProductResult).Hash.ToLowerInvariant()
    $receipt.product = [ordered]@{
        rawDataSha256 = $rawDataHash
        archive = 'window-numeric.zip'
        member = "sha256/$rawDataHash"
        sourceSnapshotSha256 = $product.sourceSnapshotSha256
        sourceSnapshotArchive = '../../2026-11-01/testing-three/evidence.zip'
        sourceSnapshotMember = "payloads/$($product.sourceSnapshotSha256)"
        coreSha256 = $product.coreSha256
        applicationSha256 = $product.applicationSha256
        wallSeconds = $product.wallSeconds
        certificate = $certificate
        diagnostics = $product.Values.StreamedFourierPupil
        versusNative2048 = $checks
    }
}
$receipt | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Output
if ($ProductResult -and -not $passes) { throw 'Streamed product MTF failed its native cross-check' }
