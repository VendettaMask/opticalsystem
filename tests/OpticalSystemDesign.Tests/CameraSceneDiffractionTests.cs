using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSceneDiffractionTests
{
    [Theory]
    [InlineData(450)] [InlineData(550)] [InlineData(650)]
    public void AbsoluteSquarePupilMatchesIndependentFraunhoferPeakAndFirstZero(double wavelength)
    {
        const int n = 24; const double z = 1000; const double width = 1;
        var cells = new List<ScalarHuygensCell>();
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
        {
            var point = new Vector3D((x + .5 - n / 2d) * width / n, (y + .5 - n / 2d) * width / n, 0);
            var delta = new Vector3D(0, 0, z) - point;
            cells.Add(new(point, delta / delta.Length, new(width / n, 0, 0), new(0, width / n, 0),
                width * width / (n * n), 1, -delta.Length));
        }
        var peak = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(cells, new(0, 0, z), wavelength);
        var expected = Math.Pow(width * width / (wavelength * 1e-6 * z), 2);
        Assert.InRange(Math.Abs(peak / expected - 1), 0, 2e-6);
        var halfZero = wavelength * 1e-6 * z / width / 2;
        var half = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(cells, new(halfZero, 0, z), wavelength);
        Assert.InRange(Math.Abs(half / peak - 4 / (Math.PI * Math.PI)), 0, 2e-6);
        var zero = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(cells, new(halfZero * 2, 0, z), wavelength);
        Assert.InRange(zero / peak, 0, 1e-10);
        var halfPower = cells.Select(c => c with { Amplitude = Math.Sqrt(.5) }).ToArray();
        Assert.Equal(peak * .5, ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(halfPower, new(0, 0, z), wavelength), 10);
    }

    [Fact]
    public void PupilPhaseIsCoherentAndOutputCropDoesNotRenormalizeTheField()
    {
        var cell = new ScalarHuygensCell(new(0, 0, 0), new(0, 0, 1), new(.1, 0, 0), new(0, .1, 0), .01, 1, 0);
        var one = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell], new(0, 0, 100), 500);
        var two = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell, cell], new(0, 0, 100), 500);
        var canceled = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell, cell with { OpticalPathMillimeters = .00025 }], new(0, 0, 100), 500);
        Assert.Equal(4 * one, two, 12); Assert.InRange(canceled, 0, 1e-20);
        var before = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell], new(.01, 0, 100), 500);
        for (var i = -8; i <= 8; i++) _ = ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell], new(i * .01, 0, 100), 500);
        Assert.Equal(before, ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter([cell], new(.01, 0, 100), 500));
    }

    private static Optic Lens()
    {
        var optic = new Optic(); optic.Fields.Add(new()); optic.Wavelengths.Add(new() { Nanometers = 550 });
        optic.SurfaceGroup.Replace([new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 100, InteractionModel = new ThinLensInteractionModel(50) },
            new OpticalSurface()]);
        return optic;
    }

    private static DirectCameraScene Scene(double z = -100, double emission = 1, bool blocker = false)
    {
        var plane = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, z)), Parameters = new PlaneRectangleParameters(.1, .1) };
        var objects = new List<NonSequentialObjectDefinition> { plane };
        if (blocker) objects.Add(NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -50)), Parameters = new PlaneRectangleParameters(4, 4) });
        return new(new("Analytical finite conjugate", [new("green", 550, 1, true)], objects),
            objects.Select(o => new DirectSceneMaterial(o.Id, [0], [o.Id == plane.Id ? emission : 0])), []);
    }

    [Fact]
    public void SurfaceDiffractionUsesRealDepthOcclusionAbsolutePowerAndTheUnmodifiedPrescription()
    {
        var lens = Lens(); var before = lens.ToSnapshot(); var detector = new DirectCameraRenderSettings(8, 8, 3, new(2));
        var config = new SceneDiffractionSettings(12, 1, 8, 16);
        var a = DiffractionCameraRenderer.Render(lens, Scene(), detector, config);
        var b = DiffractionCameraRenderer.Render(lens, Scene(emission: 2), detector, config);
        Assert.True(a.Image.DetectorPower.TotalPowerWatts > 0);
        for (var i = 0; i < 64; i++)
        {
            var expected = a.Image.DetectorPower.Bands[0].PixelBandPowerWatts[i] * 2;
            Assert.InRange(Math.Abs(b.Image.DetectorPower.Bands[0].PixelBandPowerWatts[i] - expected), 0, Math.Max(1e-300, expected * 1e-12));
        }
        var outOfFocus = DiffractionCameraRenderer.Render(lens, Scene(-101), detector, config);
        Assert.NotEqual(a.Image.DetectorPower.Bands[0].PixelBandPowerWatts, outOfFocus.Image.DetectorPower.Bands[0].PixelBandPowerWatts);
        var shadow = DiffractionCameraRenderer.Render(lens, Scene(blocker: true), detector, config);
        Assert.Equal(0, shadow.Image.DetectorPower.TotalPowerWatts); Assert.True(shadow.Diffraction.OccludedPupilCells > 0);
        var json = new System.Text.Json.JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before, json), System.Text.Json.JsonSerializer.Serialize(lens.ToSnapshot(), json));
        Assert.True(a.Image.DetectorPower.TotalPowerWatts < a.Diffraction.CollectedPowerWatts);
        CameraContinuousSpectrumTests.Record("diffraction-surface-control", new { a.Diffraction, a.Image.DetectorPower.TotalPowerWatts,
            OutOfFocusPower = outOfFocus.Image.DetectorPower.TotalPowerWatts, ShadowPower = shadow.Image.DetectorPower.TotalPowerWatts });
    }

    internal static CameraImagingRequest Request(ICameraImagingService service) => CameraScene3DTests.Request(service, 8, 8) with
    {
        Aberration = CameraAberration.Diffraction, Render3D = new(Diffraction: new(8, 1, 3, 12))
    };

    [Fact]
    public void TracedFiniteConjugatePointQuadratureMatchesAiryShapeWithoutPeakOrEnergyFitting()
    {
        const double wavelength = 550; const double firstZero = 1.2196698912665045;
        var pitch = firstZero * wavelength * 1e-3 * 100 / 2 / 11; // um, root at eleven output pixels; s=s'=100, D=2 mm.
        var plane = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -100)), Parameters = new PlaneRectangleParameters(pitch * .001, pitch * .001) };
        var scene = new DirectCameraScene(new("one source quadrature cell", [new("green", wavelength, 1, true)], [plane]),
            [new(plane.Id, [0], [1])], []);
        var a = DiffractionCameraRenderer.Render(Lens(), scene, new(23, 23, pitch), new(24, 1, 12, 12));
        var b = DiffractionCameraRenderer.Render(Lens(), scene, new(23, 23, pitch), new(32, 1, 12, 12));
        var values = b.Image.DetectorPower.Bands[0].PixelBandPowerWatts;
        var center = values[11 * 23 + 11];
        Assert.InRange(values[11 * 23 + 22] / center, 0, 1e-5);
        var u = 2 * Math.PI * .01 * (5 * pitch * .001) / (wavelength * 1e-6);
        double term = u / 2, j1 = term;
        for (var k = 1; k < 30; k++) { term *= -u * u / (4 * k * (k + 1)); j1 += term; }
        Assert.InRange(Math.Abs(values[11 * 23 + 16] / center - Math.Pow(2 * j1 / u, 2)), 0, .002);
        Assert.InRange(Math.Abs(a.Image.DetectorPower.TotalPowerWatts / b.Image.DetectorPower.TotalPowerWatts - 1), 0, .005);
        Assert.InRange(b.Image.DetectorPower.TotalPowerWatts / b.Diffraction.CollectedPowerWatts, .75, 1.001);
        Assert.Equal(1, b.Diffraction.SurfaceSamples);
        CameraContinuousSpectrumTests.Record("traced-airy-shape", new { pitch, FirstZeroRatio = values[11 * 23 + 22] / center,
            EnergyFraction = b.Image.DetectorPower.TotalPowerWatts / b.Diffraction.CollectedPowerWatts,
            PupilConvergence = a.Image.DetectorPower.TotalPowerWatts / b.Image.DetectorPower.TotalPowerWatts - 1 });
    }

    [Fact]
    public async Task ThirtyTwoPixelSceneDiffractionDeliversAReopenableExperimentAndAbsolutePowerExport()
    {
        using var app = WorkbenchApplication.Create("cooke"); var request = Request(app.CameraImaging) with
        { Width = 32, Height = 24, PixelSampling = null, Render3D = new(Diffraction: new(12, 1, 4, 24)) };
        var watch = System.Diagnostics.Stopwatch.StartNew(); var result = await app.CameraImaging.SimulateAsync(request);
        Assert.True(result.MeanPhotoElectrons > 0); Assert.NotNull(result.Scene3D!.Diffraction);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, result), Path.Combine(directory, "scene-diffraction-32.camera-experiment.zip"));
            await app.CameraImaging.ExportAsync(result, Path.Combine(directory, "scene-diffraction-32-export.zip"));
        }
        CameraContinuousSpectrumTests.Record("diffraction-32", new { result.Scene3D, result.MeanPhotoElectrons, ElapsedSeconds = watch.Elapsed.TotalSeconds });
    }

    [Fact]
    public async Task ActualLensDiffractionFeedsSensorIspAndFrozenMixedABReplaysAfterWorkspaceReplacement()
    {
        using var app = WorkbenchApplication.Create("cooke"); var before = app.Documents.GetSnapshot();
        var a = await app.CameraImaging.SimulateAsync(CameraScene3DTests.Request(app.CameraImaging));
        var request = Request(app.CameraImaging);
        var b = await app.CameraImaging.SimulateAsync(request); Assert.NotNull(b.Scene3D?.Diffraction);
        Assert.True(b.MeanPhotoElectrons > 0); Assert.Equal(0, b.OpticalFallbackCount); Assert.Equal(before, app.Documents.GetSnapshot());
        Assert.NotEqual(a.PhotoElectrons, b.PhotoElectrons); Assert.Equal(64, b.RawDn.Count);
        var c = await app.CameraImaging.RecomputeAsync(b, b.Request with { Sensor = b.Request.Sensor! with { ExposureMilliseconds = 20 } });
        for (var i = 0; i < 64; i++) Assert.Equal(b.PhotoElectrons[i] * 2, c.PhotoElectrons[i], 9);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), path);
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "diffraction-mixed-AB.camera-experiment.zip"), true); }
            app.Documents.NewBlank(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var opened = await app.CameraImaging.OpenExperimentAsync(path);
            foreach (var run in new[] { opened.B, opened.A! })
            {
                var replay = await app.CameraImaging.RecomputeAsync(run);
                Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn);
                Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24); Assert.Equal(run.Inputs!.LensSnapshotJson, replay.Inputs!.LensSnapshotJson);
            }
            var isp = await app.CameraImaging.RecomputeAsync(b, b.Request with { Isp = new(Gamma: 1) });
            Assert.Equal(b.RawDn, isp.RawDn); Assert.NotEqual(b.Developed.Rgb24, isp.Developed.Rgb24);
            await app.CameraImaging.ExportAsync(b, path, a); using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("B/detector-power.csv"));
            using var metadata = new StreamReader(zip.GetEntry("B/metadata.json")!.Open()); Assert.Contains("Diffraction", await metadata.ReadToEndAsync());
            CameraContinuousSpectrumTests.Record("diffraction-actual-lens", new { b.Scene3D, b.MeanPhotoElectrons, b.OpticalModel, MixedABReplayed = true });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ResourceLimitsInvalidSamplingAndCancellationDoNotFallBackToGeometry()
    {
        Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.Render(Lens(), Scene(), new(128, 96, 3), new(64, 4, 32, 32)));
        Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.Validate(new(12, 1, 8, 2)));
        var guard = Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.Render(Lens(), Scene(-130), new(4, 4, 3), new(8, 1, 4, 4)));
        Assert.Contains("保护带", guard.Message);
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
        Assert.Throws<OperationCanceledException>(() => DiffractionCameraRenderer.Render(Lens(), Scene(), new(4, 4, 3), new()));
    }

    [Fact]
    public async Task FrozenV7OriginalMixedExposureFixtureRetainsItsVersionAndEveryStoredRawValue()
    {
        using var app = WorkbenchApplication.Create();
        var saved = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-scene-v7-frozen.zip"));
        foreach (var run in new[] { saved.B, saved.A! })
        {
            Assert.Equal(CameraExperiment.DirectSceneEngineVersion, run.Inputs!.EngineVersion);
            var replay = await app.CameraImaging.RecomputeAsync(run);
            Assert.Equal(run.Inputs.EngineVersion, replay.Inputs!.EngineVersion);
            Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn);
            Assert.Equal(run.ReferenceRawDn, replay.ReferenceRawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
            Assert.Equal(run.Scene3D!.TotalPowerWatts, replay.Scene3D!.TotalPowerWatts);
        }
    }

    [Fact]
    public void GrazingOneSidedSurfaceDoesNotRadiateThroughItsBackHemisphere()
    {
        var plane = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -100), RotationYDegrees: 89.8), Parameters = new PlaneRectangleParameters(1, .003) };
        DirectCameraScene Source(bool twoSided) => new(new("grazing source cell", [new("green", 550, 1, true)], [plane]),
            [new(plane.Id, [0], [1], TwoSided: twoSided)], []);
        var one = DiffractionCameraRenderer.Render(Lens(), Source(false), new(5, 5, 3), new(16, 1, 3, 4));
        var two = DiffractionCameraRenderer.Render(Lens(), Source(true), new(5, 5, 3), new(16, 1, 3, 4));
        Assert.True(one.Diffraction.CollectedPowerWatts > 0);
        Assert.True(two.Diffraction.CollectedPowerWatts > one.Diffraction.CollectedPowerWatts * 1.1);
        Assert.NotEqual(one.Image.DetectorPower.Bands[0].PixelBandPowerWatts, two.Image.DetectorPower.Bands[0].PixelBandPowerWatts);
    }
}
