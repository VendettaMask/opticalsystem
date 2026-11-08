[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $CoreResults,
    [Parameter(Mandatory)] [string] $NativeResults,
    [Parameter(Mandatory)] [string] $ComparisonSettings,
    [Parameter(Mandatory)] [string] $Output,
    [string] $ProductResults
)

$ErrorActionPreference = 'Stop'
$core = Get-Content -LiteralPath $CoreResults -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $NativeResults -Raw | ConvertFrom-Json
$settings = (Get-Content -LiteralPath $ComparisonSettings -Raw | ConvertFrom-Json).analyses
$psfGate = $settings.'Huygens PSF Cross Section'.quantities.Irradiance
$mtfGate = $settings.'Huygens Through Focus MTF'.quantities.Modulation

function Measure-Curve([double[]] $Sample, [double[]] $Reference, $Gate) {
    if ($Sample.Count -ne $Reference.Count -or $Sample.Count -lt 2) { throw 'Unequal or empty coordinates' }
    $peak = 0.0
    $maximumError = 0.0
    $squares = 0.0
    $pointwisePass = $true
    for ($i = 0; $i -lt $Sample.Count; $i++) {
        if (-not [double]::IsFinite($Sample[$i]) -or -not [double]::IsFinite($Reference[$i])) {
            throw 'Nonfinite curve value'
        }
        $magnitude = [Math]::Abs($Reference[$i])
        $errorValue = [Math]::Abs($Sample[$i] - $Reference[$i])
        $peak = [Math]::Max($peak, $magnitude)
        $maximumError = [Math]::Max($maximumError, $errorValue)
        $squares += $errorValue * $errorValue
        $pointwisePass = $pointwisePass -and ($errorValue -le $Gate.absolute + $Gate.relative * $magnitude)
    }
    $nrmse = [Math]::Sqrt($squares / $Sample.Count) / [Math]::Max($peak, $Gate.absolute)
    [ordered] @{
        points = $Sample.Count
        coverage = 1.0
        maximumAbsoluteError = $maximumError
        referencePeak = $peak
        maximumErrorRelativeToReferencePeak = $maximumError / $peak
        nrmse = $nrmse
        pointwisePass = $pointwisePass
        pass = $pointwisePass -or $nrmse -le $Gate.nrmse
    }
}

function Get-FocusCurve($Trial, [string] $Component) {
    [double[]] $values = $Trial.results.$Component
    if ($values.Count -ne 5) { throw 'Expected five equally spaced focus calculations' }
    # Natural cubic spline on the five physical knots, displayed at 101 positions.
    $a = [double[]]::new(5)
    $b = [double[]]::new(5)
    $d = [double[]]::new(5)
    for ($i = 1; $i -lt 4; $i++) {
        $pivot = 4 - $a[$i - 1]
        $a[$i] = 1 / $pivot
        $b[$i] = (6 * ($values[$i + 1] - 2 * $values[$i] + $values[$i - 1]) - $b[$i - 1]) / $pivot
    }
    for ($i = 3; $i -gt 0; $i--) { $d[$i] = $b[$i] - $a[$i] * $d[$i + 1] }
    for ($i = 0; $i -le 100; $i++) {
        $position = $i / 25.0
        $lower = [Math]::Min(3, [Math]::Floor($position))
        $t = $position - $lower
        $s = 1 - $t
        $s * $values[$lower] + $t * $values[$lower + 1] +
            (($s * $s * $s - $s) * $d[$lower] + ($t * $t * $t - $t) * $d[$lower + 1]) / 6
    }
}

function Compare-Trial([string] $Label, $Sample, $Reference) {
    $psf = Measure-Curve $Sample.results[2].line $Reference.results[2].line $psfGate
    $mtfT = Measure-Curve (Get-FocusCurve $Sample 'mtfT') (Get-FocusCurve $Reference 'mtfT') $mtfGate
    $mtfS = Measure-Curve (Get-FocusCurve $Sample 'mtfS') (Get-FocusCurve $Reference 'mtfS') $mtfGate
    [ordered] @{
        comparison = $Label
        section = $psf
        peakRelativeDifference = [Math]::Abs($Sample.results[2].peak / $Reference.results[2].peak - 1)
        additionalPeakRelativeCurvePass = $psf.maximumErrorRelativeToReferencePeak -le $psfGate.relative
        throughFocusTangential = $mtfT
        throughFocusSagittal = $mtfS
    }
}

$checks = @(
    Compare-Trial 'Core 65536 versus 131072' $core[-3] $core[-1]
    Compare-Trial 'Core 98292 versus 131072' $core[-2] $core[-1]
    Compare-Trial 'OpticStudio 98292 versus 131072' $native[-2] $native[-1]
    Compare-Trial 'Core 65536 versus OpticStudio 131072' $core[-3] $native[-1]
    Compare-Trial 'Core 131072 versus OpticStudio 131072' $core[-1] $native[-1]
)
$passed = @($checks | Where-Object { -not ($_.section.pass -and $_.additionalPeakRelativeCurvePass -and
    $_.throughFocusTangential.pass -and $_.throughFocusSagittal.pass) }).Count -eq 0
$productCheck = $null
if ($ProductResults) {
    $product = Get-Content -LiteralPath $ProductResults -Raw | ConvertFrom-Json
    $reference = $native[-1]
    if ($product.section.Count -ne 32 -or $product.plot.Count -ne 2) {
        throw 'Unexpected product section or through-focus shape'
    }
    for ($i = 0; $i -lt 32; $i++) {
        if ([Math]::Abs($product.section[$i].X - ($i - 16) * 0.25) -gt 1e-12) {
            throw 'Product section moved physical coordinates'
        }
    }
    foreach ($curve in $product.plot) {
        if ($curve.Count -ne 101) { throw 'Expected 101 through-focus display coordinates' }
        for ($i = 0; $i -le 100; $i++) {
            if ([Math]::Abs($curve[$i].X - (-0.01 + $i * 0.0002)) -gt 1e-12) {
                throw 'Product through-focus coordinates differ'
            }
        }
    }
    $section = Measure-Curve $product.section.Y $reference.results[2].line $psfGate
    $tangential = Measure-Curve $product.plot[0].Y (Get-FocusCurve $reference 'mtfT') $mtfGate
    $sagittal = Measure-Curve $product.plot[1].Y (Get-FocusCurve $reference 'mtfS') $mtfGate
    $productCheck = [ordered] @{
        coreSha256 = $product.coreSha256
        applicationSha256 = $product.applicationSha256
        section = $section
        productPeak = $product.imagePeak
        nativePeak = $reference.results[2].peak
        peakRelativeDifference = [Math]::Abs($product.imagePeak / $reference.results[2].peak - 1)
        throughFocusTangential = $tangential
        throughFocusSagittal = $sagittal
        pass = $section.pass -and $section.maximumErrorRelativeToReferencePeak -le $psfGate.relative `
            -and $tangential.pass -and $sagittal.pass
    }
    $passed = $passed -and $productCheck.pass
}
[ordered] @{
    scope = 'Axisymmetric planar continuum reconstruction, not the frozen finite-cell setting'
    wavelengthMicrometers = 0.55
    pixelPitchMicrometers = 0.25
    grid = 32
    sectionXMicrometers = @(0..31 | ForEach-Object { ($_ - 16) * 0.25 })
    focusMillimeters = @(-0.01, -0.005, 0, 0.005, 0.01)
    frequencyCyclesPerMillimeter = 50
    originalThresholds = @{ section = $psfGate; mtf = $mtfGate; minimumCoverage = 0.95 }
    checks = $checks
    productCheck = $productCheck
    continuumChecksPass = $passed
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Output
if (-not $passed) { throw 'A continuum comparison did not meet its declared gates' }
