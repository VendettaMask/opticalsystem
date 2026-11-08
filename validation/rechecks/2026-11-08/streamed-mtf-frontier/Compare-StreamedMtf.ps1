[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Output,
    [Parameter(Mandatory)] [ValidateSet(445, 1095)] [int] $FrozenIndex,
    [string] $ProductResult
)

$ErrorActionPreference = 'Stop'
$frozen = Join-Path $Repository ('artifacts/validation/hf1107/full/analyses/{0:D4}/request.json' -f $FrozenIndex)
$job = Get-Content -LiteralPath $frozen -Raw | ConvertFrom-Json -Depth 60
$settings = $job.request.workbenchSettings
$snapshotHash = (Get-FileHash -LiteralPath $job.snapshotPath).Hash.ToLowerInvariant()
$originalSourceHash = if ($FrozenIndex -eq 445) {
    '2f85aa23896797969b375d24a4d5e43a103aa39e498b07dca22d8c6adc247b0f'
} else {
    'c22e0ae7f6655dba582da1a61b9fafab7b93fc6a396c388692249e73848d58da'
}
$native = @{}
$nativeModel = $null

foreach ($n in 256, 512, 1024, 2048) {
    if ($FrozenIndex -eq 445 -and $n -eq 256) { continue }
    $root = if ($n -ge 1024) {
        'artifacts/validation/streamed-mtf-frontier-20261108/native'
    } elseif ($FrozenIndex -eq 445) {
        'validation/rechecks/2026-11-07/residual-reference-followup/fft-mtf-payload/native'
    } else {
        'artifacts/validation/ic2/h/n'
    }
    $folder = Join-Path $Repository "$root/$FrozenIndex-$n"
    if (!(Test-Path -LiteralPath "$folder/batch-result.json")) { continue }
    $files = [ordered]@{}
    $json = @{}
    $names = @('data', 'captured-settings', 'result-diagnostics', 'batch-result')
    if ($FrozenIndex -ne 445 -or $n -ne 512) { $names += 'model' }
    foreach ($name in $names) {
        $path = "$folder/$name.json"
        $files[$name] = (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
        $json[$name] = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 80
    }
    $captured = $json['captured-settings']
    $diag = $json['result-diagnostics']
    if ($json['batch-result'].exitCode -ne 0 -or $diag.seriesCount -ne 1 -or $diag.gridCount -ne 0 -or
        $diag.messages.Count -ne 0 -or $captured.properties.SampleSize -cne "S_$($n)x$n" -or
        $captured.properties.MaximumFrequency -ne 50 -or $captured.properties.Type -cne 'Modulation' -or
        $captured.properties.UsePolarization -or $captured.properties.ShowDiffractionLimit -or
        $captured.selectors.Field -ne $job.request.field -or $captured.selectors.Wavelength -ne $job.request.wavelength -or
        $captured.selectors.Surface -ne 0 -or
        $captured.request.configuration -ne 1 -or $captured.request.useRayAiming -or
        $captured.request.apodization -cne $job.request.apodization -or
        (Get-FileHash -LiteralPath $captured.request.input).Hash -ine $originalSourceHash) {
        throw "Unexpected native capture settings or diagnostic at $n"
    }
    if ($files.Contains('model')) {
        if ($nativeModel -and $files.model -cne $nativeModel) { throw "Native model differs at $n" }
        $nativeModel = $files.model
    }
    $data = $json.data
    $series = $data.dataSeries[0]
    if ($data.dataSeries.Count -ne 1 -or $series.x.Count -ne 300 -or $series.y.Count -ne 300 -or
        $series.seriesLabels.Count -ne 2 -or $series.seriesLabels[0] -cne '子午' -or
        $series.seriesLabels[1] -cne '弧矢' -or @($series.y | Where-Object Count -ne 2).Count -ne 0) {
        throw "Unexpected native axes at $n"
    }
    $native[$n] = [pscustomobject]@{
        n = $n
        series = @(
            [pscustomobject]@{ x = $series.x; y = @($series.y | ForEach-Object { $_[0] }) }
            [pscustomobject]@{ x = $series.x; y = @($series.y | ForEach-Object { $_[1] }) }
        )
        folder = "$root/$FrozenIndex-$n"
        hashes = $files
    }
}

function Compare-Curves($candidate, $reference) {
    @(for ($d = 0; $d -lt 2; $d++) {
        $peak = 0.0
        $squared = 0.0
        $largest = 0.0
        $pointwise = $true
        for ($i = 0; $i -lt 300; $i++) {
            $x = 50.0 * $i / 299
            $cx = [double]$candidate[$d].x[$i]
            $rx = [double]$reference[$d].x[$i]
            $value = [double]$candidate[$d].y[$i]
            $truth = [double]$reference[$d].y[$i]
            if (![double]::IsFinite($cx) -or ![double]::IsFinite($rx) -or
                [Math]::Abs($cx - $x) -gt 1e-12 -or [Math]::Abs($rx - $x) -gt 1e-12 -or
                ![double]::IsFinite($value) -or ![double]::IsFinite($truth)) {
                throw "Invalid physical frequency or modulation at direction $d knot $i"
            }
            $err = [Math]::Abs($value - $truth)
            $squared += $err * $err
            $largest = [Math]::Max($largest, $err)
            $peak = [Math]::Max($peak, [Math]::Abs($truth))
            $pointwise = $pointwise -and ($err -le .001 + .001 * [Math]::Abs($truth))
        }
        $nrmse = [Math]::Sqrt($squared / 300) / [Math]::Max($peak, .001)
        [ordered]@{
            direction = @('Tangential', 'Sagittal')[$d]
            coordinates = 300
            coverage = 1
            maximumAbsoluteError = $largest
            nrmse = $nrmse
            pointwisePass = $pointwise
            pass = $pointwise -or $nrmse -le .01
        }
    })
}

$pairwise = @(foreach ($n in 512, 1024, 2048) {
    $low = [int]($n / 2)
    if ($native.ContainsKey($low) -and $native.ContainsKey($n)) {
        $comparison = @(Compare-Curves $native[$low].series $native[$n].series)
        [pscustomobject]@{
            lowerSampling = $low
            higherSampling = $n
            pass = @($comparison | Where-Object { -not $_.pass }).Count -eq 0
            comparison = $comparison
        }
    }
})
$highestPair = $pairwise | Select-Object -Last 1
$receipt = [ordered]@{
    frozenIndex = $FrozenIndex
    sourceLensSha256 = $originalSourceHash
    sourceSnapshotSha256 = $snapshotHash
    frozenRequestSha256 = (Get-FileHash -LiteralPath $frozen).Hash.ToLowerInvariant()
    physicalAxis = [ordered]@{ quantity = 'SpatialFrequency'; unit = 'CyclesPerMillimeter'; start = 0; end = 50 }
    originalTolerance = [ordered]@{ absolute = .001; relative = .001; nrmse = .01; closeNrmse = .03; minimumCoverage = .95 }
    native = @($native.Values | Sort-Object n | Select-Object n,folder,hashes)
    nativePairwise = $pairwise
    nativeConverged = $highestPair -and $highestPair.pass
}

if ($ProductResult) {
    $product = Get-Content -LiteralPath $ProductResult -Raw | ConvertFrom-Json -Depth 80
    if ($product.sourceSnapshotSha256 -cne $snapshotHash) { throw 'The product optical source differs' }
    foreach ($setting in $settings.PSObject.Properties) {
        if ($product.parameters.($setting.Name) -cne $setting.Value) {
            throw "The product setting $($setting.Name) differs"
        }
    }
    $certificate = $product.Values.FourierSamplingValidation
    $receipt.product = [ordered]@{
        outcome = $product.Outcome
        outcomeReason = $product.OutcomeReason
        rawSha256 = (Get-FileHash -LiteralPath $ProductResult).Hash.ToLowerInvariant()
        coreSha256 = $product.coreSha256
        applicationSha256 = $product.applicationSha256
        wallSeconds = $product.wallSeconds
        certificate = $certificate
        diagnostics = $product.Values.StreamedFourierPupil
    }
    if ($product.Outcome -ceq 'Success') {
        $resolved = @($certificate.Steps | Where-Object PhaseResolved)
        if (!$certificate.Validated -or $certificate.Mode -cne 'Adaptive' -or $certificate.RequestedPupilSampling -ne 64 -or
            $certificate.RelativeRmsAccuracy -ne .001 -or $resolved.Count -lt 2 -or
            $resolved[-1].PupilSampling -ne $certificate.ReturnedPupilSampling -or $product.series.Count -ne 2) {
            throw 'Unexpected product convergence certificate'
        }
        if ($certificate.Protocol -ceq 'FourierPhysicalDomainConvergence/v3') {
            $streamed = @($certificate.Steps | Where-Object UsesStreamedPhysicalPupil)
            if ($streamed.Count -ne 2 -or @($streamed | Where-Object {
                -not $_.PhaseResolved -or $_.MaximumPupilPhaseStepWaves -le 0 -or $_.MaximumPupilPhaseStepWaves -gt .5
            }).Count -ne 0) { throw 'Unexpected streamed phase certificate' }
        }
        for ($d = 0; $d -lt 2; $d++) {
            $curve = $product.series[$d]
            if ($curve.Name -cne @('Field 1, T', 'Field 1, S')[$d] -or $curve.XQuantity -cne 'SpatialFrequency' -or
                $curve.XUnit -cne 'CyclesPerMillimeter' -or $curve.YQuantity -cne 'Modulation' -or
                $curve.YUnit -cne 'Dimensionless' -or $curve.x.Count -ne 300 -or $curve.y.Count -ne 300) {
                throw "Unexpected product axes at direction $d"
            }
        }
        if ($highestPair) {
            $referenceSampling = $highestPair.higherSampling
            $cross = @(Compare-Curves $product.series $native[$referenceSampling].series)
            $receipt.product.nativeReferenceSampling = $referenceSampling
            $receipt.product.cross = $cross
            $receipt.product.independentCrossPass = $receipt.nativeConverged -and @($cross | Where-Object { -not $_.pass }).Count -eq 0
        }
    }
}
$receipt | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $Output
