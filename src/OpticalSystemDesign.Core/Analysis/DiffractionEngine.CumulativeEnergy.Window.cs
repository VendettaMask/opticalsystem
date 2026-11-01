using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;
using System.Numerics;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static FftCumulativeEnergyGrid ComputeBoundedCumulativeEnergy(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, int sampling, string referenceKind, string type, double maximumDistance, bool ignoreOpd)
    {
        var readout = PrepareBoundedCumulativeEnergy(optic, field, wave, sampling, referenceKind, type, maximumDistance, ignoreOpd, false, 1);
        var width = readout.Values.GetLength(1); var height = readout.Values.GetLength(0);
        var samples = new EnergySample[checked(width * height)];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var value = readout.Values[y, x];
            samples[y * width + x] = new((readout.FirstX + x) * readout.Pitch, (readout.FirstY + y) * readout.Pitch,
                value.Real * value.Real + value.Imaginary * value.Imaginary);
        }
        return new(samples, readout.Center, type, readout.Total, maximumDistance);
    }

    internal static FftCumulativeKnotGrid ComputeBoundedCumulativeEnergyKnots(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, int sampling, string referenceKind, string type, double maximumDistance, bool ignoreOpd,
        int additionalImageDecimation = 1)
    {
        var readout = PrepareBoundedCumulativeEnergy(optic, field, wave, sampling, referenceKind, type, maximumDistance,
            ignoreOpd, true, additionalImageDecimation);
        return new(readout.Values, readout.FirstX, readout.FirstY, readout.Pitch, readout.Center,
            readout.Total, readout.BinWidth, maximumDistance, sampling, type, readout.LogicalSize);
    }

    private sealed record BoundedEnergyReadout(Complex[,] Values, int FirstX, int FirstY, double Pitch,
        double BinWidth, (double X, double Y) Center, double Total, int LogicalSize);

    private static BoundedEnergyReadout PrepareBoundedCumulativeEnergy(Optic optic, (double Hx, double Hy) field,
        Wavelength wave, int sampling, string referenceKind, string type, double maximumDistance, bool ignoreOpd,
        bool allowImageReduction, int additionalImageDecimation)
    {
        if (optic.ImageSpaceAfocal || sampling < 256 || maximumDistance <= 0 || !double.IsFinite(maximumDistance)
            || type.StartsWith("X", StringComparison.OrdinalIgnoreCase) || type.StartsWith("Y", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisDataUnavailableException("Diffraction Encircled Energy",
                "bounded high-sampling cumulative energy requires a finite monochromatic radial/square grid and an explicit maximum distance");
        var reference = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        if (CumulativeEnergyCone(optic, field, reference) is not { } scale
            || CumulativeEnergyCone(optic, field, wave) is not { } cone)
            throw new AnalysisDataUnavailableException("Diffraction Encircled Energy", "no finite full marginal cone");
        var nativePitch = ExpandedCumulativeEnergyPitch(wave.Micrometers, scale.Rms, sampling);
        var source = DenseMonochromaticCumulativePupil(optic, field, wave, sampling, nativePitch, scale.Rms, cone)
            with { UseCachedFftStopMap = true };
        var grid = CompactFftPupilGrid(source);
        var logical = checked(2 * sampling); var pitch = nativePitch;
        if (additionalImageDecimation < 1 || (additionalImageDecimation & (additionalImageDecimation - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(additionalImageDecimation));
        if (allowImageReduction)
        {
            while (!FitsReadout())
            {
                if (logical < 4 * grid.SampleCount)
                    throw new ArgumentOutOfRangeException(nameof(maximumDistance), "No complete bounded image quadrature fits the resource budget");
                logical /= 2; pitch *= 2;
            }
            logical /= additionalImageDecimation; pitch *= additionalImageDecimation;
        }
        if (logical < 2 * grid.SampleCount || !FitsReadout())
            throw new ArgumentOutOfRangeException(nameof(maximumDistance), "Bounded image quadrature exceeds the original aggregate allocation budget");
        var sphere = CumulativeEnergyReferenceSphere(optic, field, reference);
        var metadata = ComputeFftPsfCore(optic, field, wave, grid.SampleCount, CompactPupilTransformSize(grid.SampleCount),
            false, true, 0, null, null, true, ignoreOpd, optic.RayAimingEnabled,
            reference, 0, true, out var pupil, sphere, grid, croppedOutputSize: 2);
        var moments = FourierImageMoments(pupil, logical);
        var center = referenceKind.Equals("centroid", StringComparison.OrdinalIgnoreCase)
            ? (X: moments.X * pitch, Y: moments.Y * pitch) : (X: 0d, Y: 0d);
        if (referenceKind.Equals("vertex", StringComparison.OrdinalIgnoreCase))
        {
            var waveNumber = optic.Wavelengths.ToList().IndexOf(reference) + 1;
            var chief = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number,
                waveNumber, field.Hx, field.Hy, [new Raytrace.PupilSample(0, 0, 1)],
                requireUnvignetted: true, includeSurfaceTransmission: false, aimAtStop: metadata.UseRayAiming)[0];
            center = (-chief.Position.X * 1000, -chief.Position.Y * 1000);
        }
        var firstX = (int)Math.Floor((center.X - maximumDistance) / pitch - .5);
        var firstY = (int)Math.Floor((center.Y - maximumDistance) / pitch - .5);
        var width = checked((int)Math.Ceiling((center.X + maximumDistance) / pitch + .5) - firstX + 1);
        var height = checked((int)Math.Ceiling((center.Y + maximumDistance) / pitch + .5) - firstY + 1);
        var image = FourierTransformWindow(pupil, logical, firstX, firstY, width, height,
            additionalButterflies: FourierImageMomentButterflies(pupil));
        return new(image, firstX, firstY, pitch, nativePitch, center, moments.Total, logical);

        bool FitsReadout()
        {
            if (logical < 2 * grid.SampleCount) return false;
            var side = Math.Ceiling(2 * maximumDistance / pitch) + 4;
            return side <= AnalysisResourceLimits.MaximumFftGridSize && side >= 2
                && grid.SampleCount * (double)grid.SampleCount + grid.SampleCount * side + side * side
                    <= AnalysisResourceLimits.MaximumAggregateGridCells;
        }
    }
}
