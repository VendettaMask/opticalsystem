using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraContinuousSpectrumTests
{
    internal static CameraSensorSettings Clean => new(NoiseEnabled: false, DarkElectronsPerSecond: 0,
        PrnuPercent: 0, DsnuElectrons: 0, FullWellElectrons: 1e7, GainDnPerElectron: .01, BlackLevel: 64, BitDepth: 16);
    internal static RgbImage White(int size = 16)
    {
        var pixels = new double[3, size, size];
        for (var c = 0; c < 3; c++) for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) pixels[c, y, x] = 1;
        return new(pixels);
    }
    internal static ScalarSpectrum Light(double value = .0001) => new(ScalarSpectrumKind.Illuminant,
        new[] { new ScalarSpectrumSample(400, value), new ScalarSpectrumSample(700, value) });
    internal static ScalarSpectrum Reflectance(double value = .5) => new(ScalarSpectrumKind.Reflectance,
        new[] { new ScalarSpectrumSample(400, value), new ScalarSpectrumSample(700, value) });
    internal static SensorSpectralResponse Qe => new(new[]
    { new SensorResponseSample(400, .2, .4, .6), new SensorResponseSample(700, .2, .4, .6) });
    private const double Hc = 6.62607015e-34 * 299792458;

    [Fact]
    public void FlatContinuousSpectrumMatchesIndependentIntegralWithCorrectPerNanometerUnits()
    {
        var plan = CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new());
        var result = CameraContinuousSpectrum.Integrate(White(), Clean, plan);
        // Integrate λ_nm analytically; density is per nm, only photon energy converts λ to meters.
        var factor = .0001 * .5 * 9e-12 * .01 * 1e-9 * (700d * 700 - 400d * 400) / (2 * Hc);
        var expected = new[] { .2 * factor, .4 * factor, .6 * factor };
        var actual = new[] { result.PhotoElectrons[0], result.PhotoElectrons[1], result.PhotoElectrons[17] };
        for (var c = 0; c < 3; c++) Assert.Equal(expected[c], actual[c], 9);
        Assert.Equal(.03, plan.IlluminantIrradiance, 12); Assert.Equal(.015, plan.ReflectedIrradiance, 12);
        Assert.Equal(9, plan.Nodes.Count); Assert.Equal(300, plan.Nodes.Sum(n => n.WeightNanometers), 10);
        Assert.Equal(.5, result.OpticalPreview[0, 4, 4], 12);
        var sensor = CameraSensorPipeline.SimulatePhotoElectrons(result.PhotoElectrons, 16, 16, Clean, new());
        Assert.Equal(0, sensor.RawNoiseRmseDn);
        Record("flat-integral", new
        {
            ExpectedRgbElectrons = expected,
            ActualRgbElectrons = actual,
            plan.IlluminantIrradiance,
            plan.ReflectedIrradiance,
            Nodes = plan.Nodes.Count,
            sensor.RawNoiseRmseDn
        });
    }

    [Fact]
    public void ProductOfThreeLinearCurvesMatchesIndependentPolynomialAntiderivative()
    {
        // x=(λ-400)/300. Supply unequal knots even though each input follows a global line.
        var light = new ScalarSpectrum(ScalarSpectrumKind.Illuminant, new[] { 400d, 510, 700 }
            .Select(w => new ScalarSpectrumSample(w, .0001 * (1 + (w - 400) / 300))));
        var rho = new ScalarSpectrum(ScalarSpectrumKind.Reflectance, new[] { 400d, 480, 650, 700 }
            .Select(w => new ScalarSpectrumSample(w, .2 + .4 * (w - 400) / 300)));
        var qe = new SensorSpectralResponse(new[] { 400d, 520, 700 }.Select(w =>
            new SensorResponseSample(w, .1 + .6 * (w - 400) / 300, .2, .3)));
        // (1+x)(.2+.4x)(.1+.6x)(400+300x) = 8+78x+214x²+216x³+72x⁴.
        var integral = 300 * .0001 * (8 + 78d / 2 + 214d / 3 + 216d / 4 + 72d / 5);
        var expected = 9e-12 * .01 * 1e-9 / Hc * integral;
        var coarse = CameraContinuousSpectrum.BuildPlan(light, rho, qe, new(MaximumIntervalNanometers: 300));
        var fine = CameraContinuousSpectrum.BuildPlan(light, rho, qe, new(MaximumIntervalNanometers: 10));
        var a = CameraContinuousSpectrum.Integrate(White(), Clean, coarse).PhotoElectrons[0];
        var b = CameraContinuousSpectrum.Integrate(White(), Clean, fine).PhotoElectrons[0];
        Assert.Equal(expected, a, 9); Assert.Equal(expected, b, 9);
        Record("linear-product-antiderivative", new { Expected = expected, Coarse = a, Fine = b, CoarseNodes = coarse.Nodes.Count, FineNodes = fine.Nodes.Count });
    }

    [Fact]
    public void NarrowPeakBetweenUniformGridPointsIsPreservedByMergedBreakpoints()
    {
        var light = new ScalarSpectrum(ScalarSpectrumKind.Illuminant, new[]
        { new ScalarSpectrumSample(400, 0), new(531.9, 0), new(532, .01), new(532.1, 0), new(700, 0) });
        var plan = CameraContinuousSpectrum.BuildPlan(light, Reflectance(1), Qe, new(MaximumIntervalNanometers: 100));
        var result = CameraContinuousSpectrum.Integrate(White(), Clean, plan);
        var expected = .01 * .1 * 532e-9 / Hc * .2 * 9e-12 * .01;
        Assert.InRange(Math.Abs(result.PhotoElectrons[0] / expected - 1), 0, 2e-12);
        Assert.Equal(.001, plan.IlluminantIrradiance, 12);
        Record("narrow-peak", new { Expected = expected, Actual = result.PhotoElectrons[0], plan.IlluminantIrradiance, Nodes = plan.Nodes.Count });
    }

    [Fact]
    public void NonlinearWavelengthTransferConvergesAgainstClosedFormIntegral()
    {
        const double k = .01;
        // ∫ λ exp(k(λ-400)) dλ = exp(k(λ-400)) (λ/k - 1/k²).
        var exactIntegral = Math.Exp(3) * (700 / k - 1 / (k * k)) - (400 / k - 1 / (k * k));
        var expected = exactIntegral * .0001 * .5 * .2 * 9e-12 * .01 * 1e-9 / Hc;
        var values = new List<double>();
        foreach (var step in new[] { 100d, 50, 25 })
        {
            var plan = CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new(MaximumIntervalNanometers: step));
            var result = CameraContinuousSpectrum.Integrate(White(), Clean, plan, (mask, wavelength) =>
            {
                var data = new double[1, mask.Height, mask.Width];
                for (var y = 0; y < mask.Height; y++) for (var x = 0; x < mask.Width; x++)
                    data[0, y, x] = mask[0, y, x] * Math.Exp(k * (wavelength - 400));
                return new(data);
            });
            values.Add(result.PhotoElectrons[0]);
        }
        var errors = values.Select(v => Math.Abs(v / expected - 1)).ToArray();
        Assert.True(errors[1] < errors[0] / 40); Assert.True(errors[2] < errors[1] / 40);
        Assert.InRange(errors[2], 0, 1e-9);
        Record("nonlinear-transfer-convergence", new { Expected = expected, Actual = values, RelativeErrors = errors });
    }

    [Fact]
    public void ExposureReflectanceAndLightScaleChargeWithoutPeakNormalization()
    {
        var plan = CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new());
        var a = CameraContinuousSpectrum.Integrate(White(), Clean, plan);
        var b = CameraContinuousSpectrum.Integrate(White(), Clean with { ExposureMilliseconds = 20, ReferenceIrradiance = 0, RedQe = 0 }, plan);
        var c = CameraContinuousSpectrum.Integrate(White(), Clean,
            CameraContinuousSpectrum.BuildPlan(Light(.00005), Reflectance(.25), Qe, new()));
        for (var i = 0; i < a.PhotoElectrons.Length; i++)
        { Assert.Equal(2 * a.PhotoElectrons[i], b.PhotoElectrons[i], 9); Assert.Equal(a.PhotoElectrons[i] / 4, c.PhotoElectrons[i], 9); }
        var dark = CameraContinuousSpectrum.Integrate(White(), Clean,
            CameraContinuousSpectrum.BuildPlan(Light(0), Reflectance(), Qe, new()));
        Assert.All(dark.PhotoElectrons, v => Assert.Equal(0, v)); Assert.All(dark.OpticalPreview.Values.Cast<double>(), v => Assert.Equal(0, v));
    }

    [Fact]
    public void IntegratedChargeUsesSameShotAndReadNoiseStatistics()
    {
        var settings = Clean with { NoiseEnabled = true, ReadNoiseElectrons = 3 };
        var image = CameraContinuousSpectrum.Integrate(White(256), settings,
            CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new()));
        var result = CameraSensorPipeline.SimulatePhotoElectrons(image.PhotoElectrons, 256, 256, settings, new());
        var expected = .0001 * .5 * .2 * 9e-12 * .01 * 1e-9 * (700d * 700 - 400d * 400) / (2 * Hc);
        var red = result.MeasuredElectrons.Where((_, i) => i / 256 % 2 == 0 && i % 256 % 2 == 0).ToArray();
        var mean = red.Average(); var variance = red.Sum(v => Math.Pow(v - mean, 2)) / (red.Length - 1);
        Assert.InRange(Math.Abs(mean - expected), 0, 6 * Math.Sqrt((expected + 9) / red.Length));
        Assert.InRange(variance / (expected + 9), .94, 1.06);
        Record("continuous-noise", new { ExpectedMean = expected, ActualMean = mean, ExpectedVariance = expected + 9, ActualVariance = variance, Samples = red.Length });
    }

    [Fact]
    public void InvalidUnitsCoverageDataBudgetAndCancellationAreExplicit()
    {
        Assert.Throws<ArgumentException>(() => CameraContinuousSpectrum.BuildPlan(Reflectance(), Light(), Qe, new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new(350, 700)));
        Assert.Throws<ArgumentException>(() => CameraContinuousSpectrum.BuildPlan(
            new(ScalarSpectrumKind.Illuminant, new[] { new ScalarSpectrumSample(200, .0001), new(20000, .0001) }),
            new(ScalarSpectrumKind.Reflectance, new[] { new ScalarSpectrumSample(200, .5), new(20000, .5) }),
            new(new[] { new SensorResponseSample(200, .2, .4, .6), new(20000, .2, .4, .6) }), new(200, 20000, 1)));
        Assert.Throws<ArgumentException>(() => new ScalarSpectrum(ScalarSpectrumKind.Reflectance,
            new[] { new ScalarSpectrumSample(400, 1.1), new(700, 0) }));
        Assert.Throws<ArgumentException>(() => new ScalarSpectrum(ScalarSpectrumKind.Illuminant,
            new[] { new ScalarSpectrumSample(400, double.NaN), new(700, 0) }));
        using var cts = new CancellationTokenSource();
        using var cancellation = ComputationCancellation.Push(cts.Token);
        var plan = CameraContinuousSpectrum.BuildPlan(Light(), Reflectance(), Qe, new());
        var calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => CameraContinuousSpectrum.Integrate(White(), Clean, plan, (mask, _) =>
        { calls++; cts.Cancel(); return mask; }));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void DenseOneNanometerCurvesKeepEveryKnotAndUseIdenticalIdealAndStreamedIntegrals()
    {
        var light = new ScalarSpectrum(ScalarSpectrumKind.Illuminant,
            Enumerable.Range(400, 301).Select(w => new ScalarSpectrumSample(w, .0001)));
        var plan = CameraContinuousSpectrum.BuildPlan(light, Reflectance(), Qe, new());
        Assert.Equal(300, plan.Intervals); Assert.Equal(900, plan.Nodes.Count);
        var image = White(512);
        var ideal = CameraContinuousSpectrum.Integrate(image, Clean, plan);
        var reference = CameraContinuousSpectrum.Integrate(White(), Clean, plan, (mask, _) => mask);
        Assert.Equal(reference.PhotoElectrons[0], ideal.PhotoElectrons[0], 9);
        var expected = .0001 * .5 * .2 * 9e-12 * .01 * 1e-9 * (700d * 700 - 400d * 400) / (2 * Hc);
        Assert.Equal(expected, ideal.PhotoElectrons[0], 9);
        Assert.Throws<ArgumentException>(() => CameraContinuousSpectrum.Integrate(image, Clean, plan, (mask, _) => mask));
        Record("dense-one-nanometer", new
        {
            Nodes = plan.Nodes.Count,
            ImagePixels = 512 * 512,
            Expected = expected,
            Ideal = ideal.PhotoElectrons[0],
            Streamed = reference.PhotoElectrons[0],
            EveryKnotRetained = plan.Intervals == 300
        });
    }

    internal static void Record(string name, object data)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
