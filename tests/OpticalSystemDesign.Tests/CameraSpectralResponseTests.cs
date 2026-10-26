using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSpectralResponseTests
{
    internal static SensorSpectralResponse Curve(double scale = 1) => new(new[]
    {
        new SensorResponseSample(400, 0, .1 * scale, .8 * scale),
        new SensorResponseSample(500, .1 * scale, .5 * scale, .3 * scale),
        new SensorResponseSample(600, .5 * scale, .4 * scale, .1 * scale),
        new SensorResponseSample(700, .6 * scale, .1 * scale, 0)
    });
    private static CameraSensorSettings Clean => new(NoiseEnabled: false, DarkElectronsPerSecond: 0,
        PrnuPercent: 0, DsnuElectrons: 0, FullWellElectrons: 1e7, GainDnPerElectron: .001, BlackLevel: 0, BitDepth: 16);
    private static RgbImage Image(int size = 16, double red = 1, double green = 1, double blue = 1)
    {
        var pixels = new double[3, size, size]; var values = new[] { red, green, blue };
        for (var c = 0; c < 3; c++) for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) pixels[c, y, x] = values[c];
        return new(pixels);
    }

    [Fact]
    public void InterpolationUsesAbsoluteValuesAndRejectsUnmeasuredWavelengths()
    {
        var curve = Curve();
        Assert.Equal(.8, curve.Evaluate(2, 400)); Assert.Equal(.6, curve.Evaluate(0, 700));
        Assert.Equal(.2, curve.Evaluate(0, 525), 12); Assert.Equal(.475, curve.Evaluate(1, 525), 12);
        Assert.Equal(.55, curve.Evaluate(2, 450), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(0, 399));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(0, 701));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.Evaluate(0, double.NaN));
    }

    [Fact]
    public void AllThreeBandsContributeThroughEachCfaResponseWithIndependentPhotonReference()
    {
        // Oracle is an explicit hand-interpolated 3x3 table, not a call to the runtime evaluator.
        var qe = new[,] { { .55, .30, .05 }, { .25, .45, .30 }, { .05, .20, .55 } };
        var input = new[] { .3, .6, .1 }; var wavelengths = new[] { 650e-9, 550e-9, 450e-9 };
        var expected = new double[3];
        for (var c = 0; c < 3; c++) for (var band = 0; band < 3; band++)
            expected[c] += .02 * 9e-12 * .01 * input[band] / (6.62607015e-34 * 299792458 / wavelengths[band]) * qe[c, band];
        var sensor = Clean with { RedQe = 0, GreenQe = 0, BlueQe = 0 };
        var result = CameraSensorPipeline.Simulate(Image(red: .3, green: .6, blue: .1), sensor, new(), Curve());
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
        {
            var c = y % 2 == 0 && x % 2 == 0 ? 0 : y % 2 == 1 && x % 2 == 1 ? 2 : 1;
            Assert.Equal(expected[c], result.PhotoElectrons[y * 16 + x], 9);
        }
        Assert.Equal(0, result.RawNoiseRmseDn);
        Record("spectral-photon-reference", new
        {
            ExpectedRgbElectrons = expected,
            ActualRgbElectrons = new[] { result.PhotoElectrons[0], result.PhotoElectrons[1], result.PhotoElectrons[17] },
            result.RawNoiseRmseDn,
            Model = "Three discrete input bands, overlapping absolute CFA QE"
        });
    }

    [Fact]
    public void DiagonalCurveMatchesLegacyNarrowBandsAndResponseScalingScalesCharge()
    {
        var diagonal = new SensorSpectralResponse(new[]
        { new SensorResponseSample(450, 0, 0, .45), new SensorResponseSample(550, 0, .6, 0), new SensorResponseSample(650, .5, 0, 0) });
        var old = CameraSensorPipeline.Simulate(Image(), Clean, new());
        var equivalent = CameraSensorPipeline.Simulate(Image(), Clean, new(), diagonal);
        for (var i = 0; i < old.PhotoElectrons.Length; i++) Assert.Equal(old.PhotoElectrons[i], equivalent.PhotoElectrons[i], 9);
        Assert.Equal(old.RawDn, equivalent.RawDn);
        var full = CameraSensorPipeline.Simulate(Image(), Clean, new(), Curve());
        var half = CameraSensorPipeline.Simulate(Image(), Clean, new(), Curve(.5));
        for (var i = 0; i < full.PhotoElectrons.Length; i++) Assert.Equal(full.PhotoElectrons[i] / 2, half.PhotoElectrons[i], 9);
    }

    [Fact]
    public void OverlappingResponseNoiseAgreesWithPredictedShotPlusReadVariance()
    {
        var settings = Clean with { NoiseEnabled = true, ReadNoiseElectrons = 3 };
        var result = CameraSensorPipeline.Simulate(Image(256), settings, new(), Curve());
        // R response: .55*650 + .30*550 + .05*450 = 545 nm weighted photon energy factor.
        var expected = .02 * 9e-12 * .01 * 545e-9 / (6.62607015e-34 * 299792458);
        var red = result.MeasuredElectrons.Where((_, i) => i / 256 % 2 == 0 && i % 256 % 2 == 0).ToArray();
        var mean = red.Average(); var variance = red.Sum(x => Math.Pow(x - mean, 2)) / (red.Length - 1);
        Assert.InRange(Math.Abs(mean - expected), 0, 6 * Math.Sqrt((expected + 9) / red.Length));
        Assert.InRange(variance / (expected + 9), .94, 1.06);
        Record("spectral-noise-reference", new
        {
            ExpectedMean = expected,
            ActualMean = mean,
            ExpectedVariance = expected + 9,
            ActualVariance = variance,
            Samples = red.Length
        });
    }

    [Fact]
    public void CurvesAreFrozenAndInvalidSamplesAreRejected()
    {
        var input = new[] { new SensorResponseSample(450, .1, .2, .3), new SensorResponseSample(650, .4, .5, .6) };
        var frozen = new SensorSpectralResponse(input); input[0] = input[0] with { RedQe = 1 };
        Assert.Equal(.1, frozen.Evaluate(0, 450));
        Assert.Throws<ArgumentException>(() => new SensorSpectralResponse(new[] { input[1], input[0] }));
        Assert.Throws<ArgumentException>(() => new SensorSpectralResponse(new[] { input[0], input[0] }));
        Assert.Throws<ArgumentException>(() => new SensorSpectralResponse(new[] { input[0], input[1] with { BlueQe = 1.1 } }));
        Assert.Throws<ArgumentException>(() => new SensorSpectralResponse(new[] { input[0], input[1] with { BlueQe = double.NaN } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraSensorPipeline.Simulate(Image(), Clean, new(),
            new SensorSpectralResponse(new[] { new SensorResponseSample(500, 0, 0, 0), input[1] })));
    }

    private static void Record(string name, object data)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
