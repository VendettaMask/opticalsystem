using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSensorPipelineTests
{
    private static readonly CameraIspSettings LinearIsp = new(Gamma: 1);
    private static CameraSensorSettings Clean => new(NoiseEnabled: false, DarkElectronsPerSecond: 0,
        PrnuPercent: 0, DsnuElectrons: 0, FullWellElectrons: 1e7, GainDnPerElectron: 0.001, BlackLevel: 0, BitDepth: 16);
    private static RgbImage Constant(int size = 16, double value = 1)
    {
        var data = new double[3, size, size];
        for (var c = 0; c < 3; c++) for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) data[c, y, x] = value;
        return new(data);
    }

    [Fact]
    public void PhotoelectronsMatchIndependentPhotonEnergyAreaExposureAndQe()
    {
        var settings = Clean; var result = CameraSensorPipeline.Simulate(Constant(), settings, LinearIsp);
        var wavelength = new[] { 650e-9, 550e-9, 450e-9 }; var qe = new[] { 0.5, 0.6, 0.45 };
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
        {
            var channel = y % 2 == 0 && x % 2 == 0 ? 0 : y % 2 == 1 && x % 2 == 1 ? 2 : 1;
            var energy = 6.62607015e-34 * 299792458 / wavelength[channel];
            var expected = 0.02 * 9e-12 * 0.01 / energy * qe[channel];
            Assert.Equal(expected, result.PhotoElectrons[y * 16 + x], 8);
        }
        Assert.Equal(0, result.RawNoiseRmseDn);
        Assert.Equal(result.RawDn, result.ReferenceRawDn);
        Assert.Equal(result.Developed.Values.Cast<double>(), result.ReferenceDeveloped.Values.Cast<double>());
        Evidence("photon-formula", new
        {
            result.MeanPhotoElectrons,
            Pixel00 = result.PhotoElectrons[0],
            Pixel01 = result.PhotoElectrons[1],
            Pixel11 = result.PhotoElectrons[17],
            result.RawNoiseRmseDn
        });
    }

    [Fact]
    public void ExposureAndAreaScaleElectronsWhileGainChangesOnlyDigitalNumbers()
    {
        var settings = Clean with { GainDnPerElectron = 0.1 };
        var a = CameraSensorPipeline.Simulate(Constant(), settings, LinearIsp);
        var exposure = CameraSensorPipeline.Simulate(Constant(), settings with { ExposureMilliseconds = 20 }, LinearIsp);
        var area = CameraSensorPipeline.Simulate(Constant(), settings with { PixelPitchMicrometers = 6 }, LinearIsp);
        var gain = CameraSensorPipeline.Simulate(Constant(), settings with { GainDnPerElectron = 0.2 }, LinearIsp);
        for (var i = 0; i < a.RawDn.Length; i++)
        {
            Assert.Equal(a.PhotoElectrons[i] * 2, exposure.PhotoElectrons[i], 8);
            Assert.Equal(a.PhotoElectrons[i] * 4, area.PhotoElectrons[i], 8);
            Assert.Equal(a.PhotoElectrons[i], gain.PhotoElectrons[i]);
            Assert.InRange(Math.Abs(a.RawDn[i] * 2 - gain.RawDn[i]), 0, 1);
        }
    }

    [Theory]
    [InlineData(BayerPattern.RGGB)]
    [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)]
    [InlineData(BayerPattern.GBRG)]
    public void DemosaicPreservesKnownConstantChannelLevelsIncludingBorders(BayerPattern pattern)
    {
        var settings = new CameraSensorSettings(Pattern: pattern); var levels = new[] { 320, 940, 1880 };
        var raw = new ushort[16 * 16];
        var tile = pattern switch
        {
            BayerPattern.RGGB => new[] { 0, 1, 1, 2 },
            BayerPattern.BGGR => new[] { 2, 1, 1, 0 },
            BayerPattern.GRBG => new[] { 1, 0, 2, 1 },
            _ => new[] { 1, 2, 0, 1 }
        };
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            raw[y * 16 + x] = (ushort)(settings.BlackLevel + levels[tile[(y % 2) * 2 + x % 2]]);
        var rgb = CameraSensorPipeline.Demosaic(raw, 16, 16, settings);
        for (var c = 0; c < 3; c++) for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            Assert.Equal(levels[c] / 4031d, rgb[c, y, x], 12);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(100)]
    [InlineData(5000)]
    public void PoissonAndReadNoiseMatchIndependentFlatFieldMeanAndVariance(double expected)
    {
        var settings = Clean with { NoiseEnabled = true, ReadNoiseElectrons = 3 };
        var data = Constant(256).Values; var qe = new[] { 0.5, 0.6, 0.45 };
        for (var c = 0; c < 3; c++)
        {
            var unitElectrons = 0.02 * 9e-12 * 0.01 * (650 - 100 * c) * 1e-9 / (6.62607015e-34 * 299792458) * qe[c];
            for (var y = 0; y < 256; y++) for (var x = 0; x < 256; x++) data[c, y, x] = expected / unitElectrons;
        }
        var result = CameraSensorPipeline.Simulate(new(data), settings, LinearIsp);
        var mean = result.MeasuredElectrons.Average();
        var variance = result.MeasuredElectrons.Sum(x => Math.Pow(x - mean, 2)) / (result.MeasuredElectrons.Length - 1);
        Assert.InRange(Math.Abs(mean - expected), 0, 6 * Math.Sqrt((expected + 9) / result.MeasuredElectrons.Length));
        Assert.InRange(variance / (expected + 9), 0.96, 1.04);
        Evidence("flat-field-" + expected, new
        {
            ExpectedMean = expected,
            ActualMean = mean,
            ExpectedVariance = expected + 9,
            ActualVariance = variance,
            Pixels = result.MeasuredElectrons.Length
        });
    }

    [Fact]
    public void DarkReadoutRetainsNegativeFluctuationsBelowBlackAndRepeatsWithSeed()
    {
        var settings = Clean with { NoiseEnabled = true, ReadNoiseElectrons = 8, BlackLevel = 100, GainDnPerElectron = 1 };
        var first = CameraSensorPipeline.Simulate(Constant(128, 0), settings, LinearIsp);
        var same = CameraSensorPipeline.Simulate(Constant(128, 0), settings, LinearIsp);
        Assert.Equal(first.RawDn, same.RawDn);
        Assert.Contains(first.RawDn, x => x < 100); Assert.Contains(first.RawDn, x => x > 100);
        Assert.InRange(first.MeasuredElectrons.Average(), -0.3, 0.3);
        var different = CameraSensorPipeline.Simulate(Constant(128, 0), settings with { Seed = 43 }, LinearIsp);
        Assert.False(first.RawDn.SequenceEqual(different.RawDn));
    }

    [Fact]
    public void FullWellAndAdcSaturateIndependentlyAndNeverWrap()
    {
        var well = CameraSensorPipeline.Simulate(Constant(), Clean with { FullWellElectrons = 10, GainDnPerElectron = 1 }, LinearIsp);
        Assert.All(well.MeasuredElectrons, x => Assert.Equal(10, x));
        Assert.Equal(256, well.SaturatedPixels);
        var adc = CameraSensorPipeline.Simulate(Constant(), Clean with { BitDepth = 8, BlackLevel = 10, GainDnPerElectron = 10 }, LinearIsp);
        Assert.All(adc.RawDn, x => Assert.Equal(255, x)); Assert.Equal(256, adc.SaturatedPixels);
    }

    [Fact]
    public void IspGainGammaAndBypassAreObservableWithoutChangingInput()
    {
        var image = Constant(value: 0.25);
        var gamma = CameraSensorPipeline.Develop(image, new(Gamma: 2));
        var gain = CameraSensorPipeline.Develop(image, new(RedGain: 2, Gamma: 1));
        var bypass = CameraSensorPipeline.Develop(image, new(Enabled: false, RedGain: 2));
        Assert.Equal(0.5, gamma[0, 0, 0]); Assert.Equal(0.5, gain[0, 0, 0]);
        Assert.Equal(0.25, gain[1, 0, 0]); Assert.Equal(0.25, bypass[0, 0, 0]); Assert.Equal(0.25, image[0, 0, 0]);
    }

    [Fact]
    public void NonuniformityDarkCurrentAndSpatialFiltersHaveMeasuredEffects()
    {
        var sensor = Clean with { NoiseEnabled = true, ReadNoiseElectrons = 0, GainDnPerElectron = 1 };
        var source = Constant(128, 0.1);
        var a = CameraSensorPipeline.Simulate(source, sensor, LinearIsp);
        var b = CameraSensorPipeline.Simulate(source, sensor with { PrnuPercent = 20, DsnuElectrons = 50 }, LinearIsp);
        Assert.True(b.RawNoiseRmseDn > a.RawNoiseRmseDn * 2);
        var dark = CameraSensorPipeline.Simulate(Constant(value: 0), Clean with { DarkElectronsPerSecond = 1000 }, LinearIsp);
        Assert.All(dark.MeasuredElectrons, x => Assert.Equal(10, x));
        var filtered = CameraSensorPipeline.Develop(b.LinearRgb, LinearIsp with { Denoise = 1 });
        var sharpened = CameraSensorPipeline.Develop(b.LinearRgb, LinearIsp with { Sharpen = 1 });
        Assert.False(filtered.Values.Cast<double>().SequenceEqual(b.Developed.Values.Cast<double>()));
        Assert.False(sharpened.Values.Cast<double>().SequenceEqual(b.Developed.Values.Cast<double>()));
    }

    [Fact]
    public void InvalidInputsAndCancelledWorkAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraSensorPipeline.ValidateDimensions(1024, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraSensorPipeline.Simulate(Constant(), Clean with { RedQe = double.NaN }, LinearIsp));
        Assert.Throws<ArgumentException>(() => CameraSensorPipeline.Simulate(Constant(), Clean with { BlackLevel = 4095, BitDepth = 12 }, LinearIsp));
        var corrupt = Constant().Values; corrupt[2, 0, 0] = double.NaN;
        Assert.Throws<ArgumentException>(() => CameraSensorPipeline.Simulate(new(corrupt), Clean, LinearIsp));
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
        Assert.Throws<OperationCanceledException>(() => CameraSensorPipeline.Simulate(Constant(), Clean, LinearIsp));
    }

    private static void Evidence(string name, object data)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, name + ".json"), System.Text.Json.JsonSerializer.Serialize(data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
