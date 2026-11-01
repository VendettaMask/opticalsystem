using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

internal sealed record FftAxisProfiles(double[] Horizontal, double[] Vertical,
    double Spacing, AnalysisAxisUnit Unit, bool UseRayAiming, bool StopAimingFallbackUsed,
    int StoredPupilSize, double WorkingFNumber, double UnaberratedPeakRatio);

public static partial class DiffractionEngine
{
    // This bounds one-dimensional readouts, not a larger dense FFT allocation.
    // The original 2048-square source and aggregate allocation limits remain.
    internal const int MaximumAxisTransformSize = 32_768;
    internal const int MaximumBoundedFourierTransformSize = 524_288;

    internal static PupilGridSpecification CompactFftPupilGrid(PupilGridSpecification source)
    {
        if (source.SampleCount < 32 || source.SampleCount > MaximumBoundedFourierTransformSize / 2
            || (source.SampleCount & (source.SampleCount - 1)) != 0
            || !source.ZemaxCentered || source.SourceGrid is not null)
            throw new ArgumentOutOfRangeException(nameof(source));
        var required = 1;
        for (var i = 0; i < source.SampleCount; i++)
        {
            if (!double.IsFinite(source.Coordinate(i)) || !double.IsFinite(source.YCoordinate(i)))
                throw new ArgumentException("Nonfinite pupil grid", nameof(source));
            if (Math.Abs(source.Coordinate(i)) <= 1 || Math.Abs(source.YCoordinate(i)) <= 1)
                required = Math.Max(required, Math.Abs(i - source.SampleCount / 2) + 1);
        }
        var size = Math.Min(source.SampleCount, Math.Max(32, 2 * required));
        if (size > AnalysisResourceLimits.MaximumFftGridSize)
            throw new ArgumentOutOfRangeException(nameof(source), "Illuminated pupil exceeds the original source allocation limit");
        return source with { SampleCount = size, SourceGrid = source, SourceOffset = (source.SampleCount - size) / 2 };
    }

    internal static FftAxisProfiles ComputeFftAxisProfiles(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, int pupilSampling, int transformSize, Wavelength reference,
        double imageDelta = 0, PupilGridSpecification? explicitGrid = null)
    {
        if (transformSize < 2 * pupilSampling || transformSize > MaximumAxisTransformSize
            || (transformSize & (transformSize - 1)) != 0 || !double.IsFinite(imageDelta) || imageDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(transformSize));
        var aiming = optic.RayAimingEnabled;
        var fallback = false;
        var axes = (Tangential: double.PositiveInfinity, Sagittal: double.PositiveInfinity);
        if (!optic.ImageSpaceAfocal && !TryWorkingFNumbersAtPupilZone(optic, field, wave, aiming, false, 1, out axes))
        {
            axes = WorkingFNumbers(optic, field, wave, true); fallback = !aiming; aiming = true;
        }
        var fNumber = optic.ImageSpaceAfocal ? double.PositiveInfinity : CombineWorkingFNumbers(axes);
        var stretch = FftPupilStretch(optic, wave, pupilSampling, transformSize, true, fNumber, imageDelta);
        var source = explicitGrid ?? new PupilGridSpecification(pupilSampling, true, true, stretch);
        var spacingFNumber = fNumber;
        if (explicitGrid is null && !optic.ImageSpaceAfocal)
            (source, spacingFNumber) = FiniteScalarFourierGrid(source, axes, fNumber, imageDelta);
        if (explicitGrid is null && optic.ImageSpaceAfocal)
        {
            var aspect = ImageSpaceAnalysisSupport.AfocalFftPupilAspectStretch(optic, field, wave, aiming);
            source = source with { Stretch = stretch * aspect.X, StretchY = stretch * aspect.Y };
        }
        if (source.SampleCount != pupilSampling || source.Coordinate(0) > -1 || source.YCoordinate(0) > -1
            || source.Coordinate(pupilSampling - 1) < 1 || source.YCoordinate(pupilSampling - 1) < 1)
            throw new ArgumentException("The nominal Fourier grid must cover the complete physical pupil.");
        var compact = CompactFftPupilGrid(source);
        // The ordinary shared preparation retains the same coordinates, chief
        // reference, accepted mask, apodization and phase/amplitude interpolation.
        // Its two-pixel readout is discarded, and supplies no profile intensity.
        ComputeFftPsfCore(optic, field, wave, compact.SampleCount, CompactPupilTransformSize(compact.SampleCount),
            false, true, 0, null, null, true, false, aiming, reference, 0, true, out var pupil,
            explicitPupilGrid: compact, interpolateScalarPupilAmplitude: true, croppedOutputSize: 2);
        var (horizontal, vertical) = FourierAxisProfiles(pupil, transformSize);
        var amplitude = pupil.Cast<Complex>().Sum(v => v.Magnitude);
        if (!(amplitude > 0)) throw new AnalysisDataUnavailableException("FFT PSF Cross Section", "no illuminated pupil");
        var spacing = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.FftSampleSpacingMilliradians(optic, wave, pupilSampling, transformSize, true, stretch)
            : wave.Micrometers * spacingFNumber * (pupilSampling - 2) / (transformSize * stretch);
        return new(horizontal.Select(v => v.Magnitude * v.Magnitude / (amplitude * amplitude)).ToArray(),
            vertical.Select(v => v.Magnitude * v.Magnitude / (amplitude * amplitude)).ToArray(),
            spacing, optic.ImageSpaceAfocal ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer,
            aiming, fallback, compact.SampleCount, fNumber,
            amplitude * amplitude / Math.Pow(pupil.Cast<Complex>().Count(v => v.Magnitude > 0), 2));
    }

    private static int CompactPupilTransformSize(int sourceSize)
    {
        var size = 1; while (size < 2 * sourceSize) size *= 2;
        return size;
    }

    internal static (Complex[] Horizontal, Complex[] Vertical) FourierAxisProfiles(Complex[,] pupil, int transformSize)
    {
        var size = pupil.GetLength(0);
        if (size < 2 || size > AnalysisResourceLimits.MaximumFftGridSize || pupil.GetLength(1) != size
            || transformSize < size || transformSize > MaximumAxisTransformSize || (transformSize & (transformSize - 1)) != 0
            || (long)size * size + 4L * transformSize > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentOutOfRangeException(nameof(transformSize));
        var horizontal = new Complex[transformSize]; var vertical = new Complex[transformSize];
        var offset = (transformSize - size) / 2;
        for (var row = 0; row < size; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < size; column++)
            {
                horizontal[offset + column] += pupil[row, column];
                vertical[offset + row] += pupil[row, column];
            }
        }
        Fft(horizontal); Fft(vertical);
        // Match the native heatmap's (-N/2+1)..N/2 independent knots;
        // the shared profile presenter subsequently applies its spline contract.
        var center = (transformSize - 1) / 2;
        return (Shift(horizontal), Shift(vertical));
        Complex[] Shift(Complex[] values) => Enumerable.Range(0, transformSize)
            .Select(i => values[(i - center + transformSize) % transformSize]).ToArray();
    }
}
