using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Tests;

public sealed class HuygensImplementationRepairTests
{
    [Fact]
    public void FocalPropagationPreservesIndexOverWavelengthAndOpticalPhase()
        => CheckIndexScaling(afocal: false);

    [Fact]
    public void AfocalPropagationPreservesIndexOverWavelengthAndOpticalPhase()
        => CheckIndexScaling(afocal: true);

    [Fact]
    public void PolychromaticImageUsesTheSameWavelengthWeightsAsTheSharedPsf()
    {
        var optic = FreezeIndices(Optic.CreateCookeTriplet());
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 450, Weight = 1, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 900, Weight = 1, IsPrimary = false });
        CheckImageAndShared(optic, (0, 0), 1);
    }

    [Fact]
    public void OffAxisPolychromaticImageKeepsLateralColorOnOnePhysicalGrid()
    {
        var optic = Optic.CreateCookeTriplet();
        var chiefs = optic.Wavelengths.Select(w => optic.TraceGenericFinalSample(
            0, 1, 0, 0, w.Micrometers)!.Position.Y).ToArray();
        Assert.True(chiefs.Max() - chiefs.Min() > 1e-4);
        CheckImageAndShared(optic, (0, 1), optic.Fields.Count);
    }

    [Fact]
    public void GeneralHuygensMtfTransformsTheCombinedImageBeforeTakingMagnitude()
    {
        var optic = Optic.CreateCookeTriplet();
        var waves = optic.Wavelengths.ToArray();
        var expected = DiffractionEngine.ComputePsfMtf(new PsfResult(
            IndependentCombination(optic, (0, 1), waves, waves.First(w => w.IsPrimary)),
            16, 16, 1, .5));
        var actual = new HuygensMtfAnalysis(optic, 16, 16, .0005,
            fields: [(0, 1)], wavelengthNumber: 0, zemaxCompatible: false).GenerateData();
        Assert.Equal(2, actual.PlotSeries.Count);
        foreach (var (series, values) in new[]
                 { (actual.PlotSeries[0], expected.Tangential), (actual.PlotSeries[1], expected.Sagittal) })
        {
            Assert.Equal(expected.Frequency.Count, series.Points.Count);
            for (var i = 0; i < values.Count; i++)
            {
                Assert.Equal(expected.Frequency[i], series.Points[i].X, 10);
                Assert.InRange(Math.Abs(values[i] - series.Points[i].Y), 0, 1e-11);
            }
        }
    }

    [Fact]
    public void CentroidCenteredPolychromaticImageUsesOneWeightedPhysicalGrid()
    {
        var optic = Optic.CreateCookeTriplet();
        var waves = optic.Wavelengths.ToArray();
        var primary = waves.First(w => w.IsPrimary);
        var offset = DiffractionEngine.HuygensGeometricCenterOffset(optic, (0, 1), waves,
            primary, 16, optic.RayAimingEnabled);
        var expected = IndependentCombination(optic, (0, 1), waves, primary, offset);
        var data = Image(optic, optic.Fields.Count, useCentroid: true);
        CheckImage(data, expected);
        Assert.Equal(offset.Y * 1000,
            Convert.ToDouble(data.Values["GeometricCenterOffsetYMicrometers"]), 10);
    }

    [Fact]
    public void SpectralScalingAndZeroWeightColorsDoNotChangeNormalizedImage()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Wavelengths[0].Weight = 2;
        optic.Wavelengths[1].Weight = 3;
        optic.Wavelengths[2].Weight = 0;
        var baseline = Image(optic, 1);
        foreach (var wave in optic.Wavelengths) wave.Weight *= 1e200;
        var scaled = Image(optic, 1);
        var weights = optic.Wavelengths.Select(w => w.Weight).ToArray();
        CheckImageAndShared(optic, (0, 0), 1);
        Assert.Equal(weights, optic.Wavelengths.Select(w => w.Weight));
        var a = Assert.Single(baseline.PlotSeries).Points;
        var b = Assert.Single(scaled.PlotSeries).Points;
        Assert.InRange(a.Zip(b).Max(p => Math.Abs(p.First.Value!.Value - p.Second.Value!.Value)), 0, 1e-12);
        // An explicitly selected single color is meaningful even when its
        // configured spectral weight is zero; this is not a zero-energy mixture.
        var single = new HuygensPsfAnalysis(optic, 16, 16, .0005,
            wavelengthNumber: 3, fieldNumber: 1).GenerateData();
        Assert.True(Convert.ToDouble(single.Values["PeakStrehlRatio"]) > 0);
    }

    [Fact]
    public void AxialReferenceSphereAndDiffractionAreInvariantToTransverseTranslation()
        => CheckTranslation((0, 0), [new(10, 0, 0), new(0, 10, 0)]);

    [Fact]
    public void OffAxisReferenceSphereAndDiffractionAreInvariantToTransverseTranslation()
        => CheckTranslation((0, 1), [new(.1, 0, 0), new(0, .1, 0)]);

    private static void CheckIndexScaling(bool afocal)
    {
        var original = FreezeIndices(Optic.CreateCookeTriplet());
        original.ImageSpaceAfocal = afocal;
        original.Wavelengths.Clear();
        original.Wavelengths.Add(new Wavelength { Nanometers = 587.6, IsPrimary = true });
        var wave = original.Wavelengths[0];
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(original, (0, 0), wave, 16);
        var pitch = afocal ? .1 : .0005;
        var psf = DiffractionEngine.ComputeHuygensPsf(original, (0, 0), wave, 16, 16, pitch);
        foreach (var scale in new[] { 1.5, 2.0 })
        {
            var scaled = Optic.FromSnapshot(original.ToSnapshot());
            scaled.InvalidateRayTraceCache();
            foreach (var surface in scaled.SurfaceGroup.Items)
                surface.MaterialAfter = new ConstantIndexMaterial("Scaled " + surface.Number,
                    surface.MaterialAfter.RefractiveIndex(587.6) * scale);
            scaled.Wavelengths[0].Nanometers *= scale;
            var scaledWave = scaled.Wavelengths[0];
            var actualWavefront = WavefrontEngine.GenerateChiefRayUniform(scaled, (0, 0), scaledWave, 16);
            Assert.Equal(scale, actualWavefront.ImageRefractiveIndex);
            Assert.Equal(wavefront.Samples.Count, actualWavefront.Samples.Count);
            foreach (var pair in wavefront.Samples.Zip(actualWavefront.Samples))
            {
                Assert.Equal(pair.First.Intensity > 0, pair.Second.Intensity > 0);
                if (pair.First.Intensity <= 0) continue;
                Assert.InRange(Math.Abs(pair.First.OpdWaves - pair.Second.OpdWaves), 0, 1e-9);
                Assert.InRange((Position(pair.First) - Position(pair.Second)).Length, 0, 1e-10);
            }
            var actual = DiffractionEngine.ComputeHuygensPsf(scaled, (0, 0), scaledWave, 16, 16, pitch);
            Assert.Equal(psf.SampleSpacingMicrometers, actual.SampleSpacingMicrometers);
            Assert.InRange(MaxDifference(psf.Values, actual.Values), 0, 1e-7);
        }
    }

    private static void CheckImageAndShared(Optic optic, (double Hx, double Hy) field, int fieldNumber)
    {
        var waves = optic.Wavelengths.ToArray();
        var primary = waves.FirstOrDefault(w => w.IsPrimary) ?? waves[0];
        var expected = IndependentCombination(optic, field, waves, primary);
        CheckImage(Image(optic, fieldNumber), expected);
        // Native display conventions must not select a different physical
        // spectral mixture. Both MTF presentations start with this same PSF.
        foreach (var nativePresentation in new[] { false, true })
        {
            var shared = MtfMethodEvaluator.ComputeHuygensPolychromaticPsf(optic, field, waves,
                new MtfComputationSettings(PupilSampling: 16, ImageSize: 16,
                    PixelPitchMillimeters: .0005, UseZemaxHuygensSemantics: nativePresentation));
            var peak = shared.Values.Cast<double>().Max();
            var expectedPeak = expected.Cast<double>().Max();
            var normalized = new double[16, 16];
            for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) normalized[y, x] = shared.Values[y, x] / peak;
            Assert.InRange(MaxDifference(normalized, Scale(expected, 1 / expectedPeak)), 0, 1e-11);
        }
    }

    private static double[,] IndependentCombination(Optic optic, (double Hx, double Hy) field,
        Wavelength[] waves, Wavelength primary, (double X, double Y)? offset = null)
    {
        // Independent spectral bookkeeping in a test only. Every monochromatic
        // field still comes from the formal Core, not another optical engine.
        var scale = waves.Max(w => w.Weight);
        var shortest = waves.Min(w => w.Nanometers);
        var values = new double[16, 16];
        var ideal = 0.0;
        foreach (var wave in waves)
        {
            var weight = (scale > 0 ? wave.Weight / scale : 1) * Math.Pow(shortest / wave.Nanometers, 2);
            ideal += weight;
            var psf = DiffractionEngine.ComputeHuygensPsf(optic, field, wave, 16, 16, .0005,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: primary, imageCenterOffset: offset);
            for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) values[y, x] += weight * psf.Values[y, x];
        }
        return Scale(values, 1 / (100 * ideal));
    }

    private static void CheckTranslation((double Hx, double Hy) field, Vector3D[] shifts)
    {
        var source = Optic.CreateCookeTriplet();
        source.RayAimingEnabled = true;
        var wave = source.Wavelengths[0];
        var baseline = WavefrontEngine.GenerateChiefRayUniform(source, field, wave, 16, aimAtStop: true);
        var psf = DiffractionEngine.ComputeHuygensPsf(source, field, wave, 16, 16, .0005, aimAtStop: true);
        var fft = DiffractionEngine.ComputeFftPsf(source, field, wave, 16, 32, aimAtStop: true);
        var original = source.TraceGenericFinalSample(field.Hx, field.Hy, 0, 0, wave.Micrometers, aimAtStop: true)!;
        foreach (var shift in shifts)
        {
            var optic = Optic.FromSnapshot(source.ToSnapshot());
            optic.InvalidateRayTraceCache();
            foreach (var surface in optic.SurfaceGroup.Items)
                surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + shift };
            var translated = optic.TraceGenericFinalSample(field.Hx, field.Hy, 0, 0, wave.Micrometers, aimAtStop: true)!;
            Assert.InRange((translated.Position - original.Position - shift).Length, 0, 1e-10);
            Assert.InRange((translated.Direction - original.Direction).Length, 0, 1e-12);
            Assert.Equal(original.CumulativeOpticalPathLength, translated.CumulativeOpticalPathLength, 10);
            var actual = WavefrontEngine.GenerateChiefRayUniform(optic, field, optic.Wavelengths[0], 16, aimAtStop: true);
            Assert.Equal(baseline.Radius, actual.Radius, 10);
            foreach (var pair in baseline.Samples.Zip(actual.Samples))
            {
                Assert.Equal(pair.First.Intensity > 0, pair.Second.Intensity > 0);
                if (pair.First.Intensity <= 0) continue;
                Assert.InRange(Math.Abs(pair.First.OpdWaves - pair.Second.OpdWaves), 0, 1e-9);
                Assert.InRange((Position(pair.Second) - Position(pair.First) - shift).Length, 0, 1e-9);
            }
            Assert.InRange(MaxDifference(psf.Values, DiffractionEngine.ComputeHuygensPsf(
                optic, field, optic.Wavelengths[0], 16, 16, .0005, aimAtStop: true).Values), 0, 1e-7);
            Assert.InRange(MaxDifference(fft.Values, DiffractionEngine.ComputeFftPsf(
                optic, field, optic.Wavelengths[0], 16, 32, aimAtStop: true).Values), 0, 1e-7);
        }
    }

    private static Optic FreezeIndices(Optic optic)
    {
        optic.InvalidateRayTraceCache();
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.MaterialAfter = new ConstantIndexMaterial("Frozen " + surface.Number,
                surface.MaterialAfter.RefractiveIndex(587.6));
        return optic;
    }

    private static AnalysisData Image(Optic optic, int field, bool useCentroid = false)
        => new HuygensPsfAnalysis(optic, 16, 16, .0005, wavelengthNumber: 0,
            fieldNumber: field, useCentroid: useCentroid).GenerateData();

    private static void CheckImage(AnalysisData data, double[,] expected)
    {
        var points = Assert.Single(data.PlotSeries).Points;
        Assert.Equal(256, points.Count);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            Assert.InRange(Math.Abs(points[y * 16 + x].Value!.Value - expected[y, x]), 0, 1e-11);
        Assert.Equal(expected.Cast<double>().Max(), Convert.ToDouble(data.Values["PeakStrehlRatio"]), 11);
    }

    private static Vector3D Position(WavefrontSample s) => new(s.PupilX, s.PupilY, s.PupilZ);
    private static double[,] Scale(double[,] values, double factor)
    {
        var result = new double[values.GetLength(0), values.GetLength(1)];
        for (var y = 0; y < result.GetLength(0); y++)
        for (var x = 0; x < result.GetLength(1); x++) result[y, x] = values[y, x] * factor;
        return result;
    }
    private static double MaxDifference(double[,] a, double[,] b)
        => a.Cast<double>().Zip(b.Cast<double>()).Max(p => Math.Abs(p.First - p.Second));
}
