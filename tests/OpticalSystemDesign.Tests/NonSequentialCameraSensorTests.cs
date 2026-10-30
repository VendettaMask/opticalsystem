using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialCameraSensorTests
{
    private const double Hc = 6.62607015e-34 * 299792458;
    private static CameraSensorSettings Clean => new(NoiseEnabled: false, DarkElectronsPerSecond: 0,
        PrnuPercent: 0, DsnuElectrons: 0, FullWellElectrons: 1e7, GainDnPerElectron: .01, BlackLevel: 0, BitDepth: 16);
    private static SensorSpectralResponse Qe => new([new(450, .1, .3, .5), new(650, .7, .4, .2)]);
    private static NonSequentialObjectDefinition Detector() => NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
    {
        LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
        Parameters = new DetectorRectangleParameters(.012, .012, 4, 4)
    };

    [Theory]
    [InlineData(BayerPattern.RGGB)] [InlineData(BayerPattern.BGGR)]
    [InlineData(BayerPattern.GRBG)] [InlineData(BayerPattern.GBRG)]
    public void BandIntegratedPowerUsesOneExposureAndOutputRasterCfaWithoutExtraWeights(BayerPattern pattern)
    {
        var detector = Detector();
        var blue = Enumerable.Range(1, 16).Select(i => i * 1e-14).ToArray();
        var red = Enumerable.Range(1, 16).Select(i => (17 - i) * 2e-14).ToArray();
        var frame = new NonSequentialDetectorFrame(detector.Id, "test", 4, 4,
            new Dictionary<int, IReadOnlyList<double>> { [1] = blue, [2] = red }, blue.Sum() + red.Sum());
        var wavelengths = new[] { new NonSequentialWavelength("blue", 450, .01, false), new("red", 650, 100, true) };
        var captured = NonSequentialCameraSensor.Capture(frame, detector, wavelengths);
        var sensor = Clean with { Pattern = pattern };
        var result = SpectralDetectorSensor.Simulate(captured, Qe, sensor, new());
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
        {
            var index = y * 4 + x; var input = (3 - y) * 4 + x;
            var channel = CameraSensorPipeline.ColorAt(pattern, y, x);
            var expected = .01 / Hc * (blue[input] * 450e-9 * new[] { .1, .3, .5 }[channel]
                + red[input] * 650e-9 * new[] { .7, .4, .2 }[channel]);
            Assert.Equal(expected, result.PhotoElectrons[index], 9);
        }
        var changedMetadata = SpectralDetectorSensor.Simulate(captured, Qe, sensor with
        { ReferenceIrradiance = 100, RedQe = 0, GreenQe = 0, BlueQe = 0 }, new());
        Assert.Equal(result.PhotoElectrons, changedMetadata.PhotoElectrons);
        var doubled = SpectralDetectorSensor.Simulate(captured, Qe, sensor with { ExposureMilliseconds = 20 }, new());
        for (var i = 0; i < 16; i++) Assert.Equal(result.PhotoElectrons[i] * 2, doubled.PhotoElectrons[i], 9);
    }

    [Fact]
    public void RawNonSequentialTraceFeedsTheExistingSensorPipelineAndConservesPower()
    {
        var optic = Optic.CreateBlank(); var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.ReplaceWavelengths([new("550 nm", 550, 1, true)]);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with { Parameters = new SourceRayParameters(1e-13) });
        var detector = Detector(); document.Insert(1, detector);
        var trace = new NonSequentialDocumentTracer().Trace(document, optic.Materials);
        var frame = Assert.Single(trace.Detectors);
        var power = NonSequentialCameraSensor.Capture(frame, detector, document.Wavelengths);
        var response = new SensorSpectralResponse([new(400, .2, .5, .8), new(700, .2, .5, .8)]);
        var result = SpectralDetectorSensor.Simulate(power, response, Clean, new());
        // The even grid's local (0,0) falls in NSC row 2, col 2. Camera top-first raster maps it to row 1 (green).
        Assert.Equal(1e-13 * .01 * 550e-9 / Hc * .5, result.PhotoElectrons[1 * 4 + 2], 9);
        Assert.Equal(1, result.PhotoElectrons.Count(v => v > 0));
        Assert.Equal(frame.TotalPowerWatts, power.TotalPowerWatts);
        Assert.InRange(Math.Abs(trace.EnergyBalance.AccountedPowerWatts / 1e-13 - 1), 0, 1e-12);
        Assert.Equal(16, result.RawDn.Length); Assert.Equal(4, result.Developed.Width);
        CameraContinuousSpectrumTests.Record("nsc-camera-source-ray", new { power.TotalPowerWatts, ExpectedElectrons = 1e-13 * .01 * 550e-9 / Hc * .5,
            ActualElectrons = result.PhotoElectrons[6], trace.EnergyBalance, Scope = "Shared Core NSC ray -> raw W/pixel -> camera electrons/RAW/ISP; no 3D-scene or scatter certification" });
    }

    [Fact]
    public void CapturedPowerIsImmutableAndRepeatedDevelopmentDoesNotChangeTheInput()
    {
        var values = Enumerable.Repeat(1e-14, 16).ToArray();
        var input = new SpectralDetectorPower(4, 4, 3, 3, DetectorRowOrder.PositiveLocalYFirst, [new(450, values)]);
        values[0] = 1;
        Assert.Equal(1e-14, input.Bands[0].PixelBandPowerWatts[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)input.Bands[0].PixelBandPowerWatts)[0] = 1);
        var a = SpectralDetectorSensor.Simulate(input, Qe, new(), new());
        var b = SpectralDetectorSensor.Simulate(input, Qe, new(), new());
        Assert.Equal(a.RawDn, b.RawDn); Assert.Equal(a.PhotoElectrons, b.PhotoElectrons);
    }

    [Fact]
    public void DarkDetectorHasNoInventedOpticalSignalAndStillUsesDarkCurrent()
    {
        var detector = Detector();
        var frame = new NonSequentialDetectorFrame(detector.Id, "dark", 4, 4, new Dictionary<int, IReadOnlyList<double>>(), 0);
        var power = NonSequentialCameraSensor.Capture(frame, detector, [new("green", 550, 1, true)]);
        var result = SpectralDetectorSensor.Simulate(power, Qe, Clean with { DarkElectronsPerSecond = 100 }, new());
        Assert.All(result.PhotoElectrons, e => Assert.Equal(0, e));
        Assert.All(result.MeasuredElectrons, e => Assert.Equal(1, e));
    }

    [Fact]
    public void PixelPowerDoesNotGainAnExtraPixelAreaFactor()
    {
        SpectralDetectorPower Power(double pitch) => new(4, 4, pitch, pitch, DetectorRowOrder.PositiveLocalYFirst,
            [new(450, Enumerable.Repeat(1e-14, 16).ToArray())]);
        var a = SpectralDetectorSensor.IntegratePhotoElectrons(Power(3), Qe, Clean);
        var b = SpectralDetectorSensor.IntegratePhotoElectrons(Power(6), Qe, Clean with { PixelPitchMicrometers = 6 });
        Assert.Equal(a, b);
        Assert.Throws<ArgumentException>(() => SpectralDetectorSensor.IntegratePhotoElectrons(Power(6), Qe, Clean));
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidRadiometricPixelsAreRejected(double invalid)
    {
        var values = new double[16]; values[5] = invalid;
        Assert.Throws<ArgumentException>(() => new SpectralDetectorPower(4, 4, 3, 3, DetectorRowOrder.PositiveLocalYFirst, [new(450, values)]));
    }

    [Fact]
    public void MissingQeWrongDetectorUnknownWavelengthAndInconsistentPowerAreRejected()
    {
        var detector = Detector(); var pixels = Enumerable.Repeat(1e-14, 16).ToArray();
        var frame = new NonSequentialDetectorFrame(detector.Id, "test", 4, 4, new Dictionary<int, IReadOnlyList<double>> { [1] = pixels }, pixels.Sum());
        var wavelengths = new[] { new NonSequentialWavelength("green", 550, 1, true) };
        Assert.Throws<ArgumentException>(() => NonSequentialCameraSensor.Capture(frame with { DetectorId = Guid.NewGuid() }, detector, wavelengths));
        Assert.Throws<ArgumentException>(() => NonSequentialCameraSensor.Capture(frame with { TotalPowerWatts = 1 }, detector, wavelengths));
        Assert.Throws<ArgumentException>(() => NonSequentialCameraSensor.Capture(frame with
        { PowerByWavelength = new Dictionary<int, IReadOnlyList<double>> { [2] = pixels } }, detector, wavelengths));
        var power = NonSequentialCameraSensor.Capture(frame, detector, wavelengths);
        var missing = new SensorSpectralResponse([new(600, .2, .4, .6), new(700, .2, .4, .6)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => SpectralDetectorSensor.IntegratePhotoElectrons(power, missing, Clean));
    }

    [Fact]
    public void SpectralWorkAndCancellationAreBounded()
    {
        var values = new double[65536];
        Assert.Throws<ArgumentException>(() => new SpectralDetectorPower(256, 256, 3, 3, DetectorRowOrder.PositiveLocalYFirst,
            Enumerable.Repeat(new SpectralDetectorPowerBand(450, values), 129)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => new SpectralDetectorPower(4, 4, 3, 3, DetectorRowOrder.PositiveLocalYFirst, []));
    }
}
