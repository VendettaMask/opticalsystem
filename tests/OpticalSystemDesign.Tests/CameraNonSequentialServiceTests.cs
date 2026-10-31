using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraNonSequentialServiceTests
{
    internal static CameraResponseCurve Response => new("control QE", "analytical", [new(400, .2, .5, .8), new(700, .2, .5, .8)]);
    internal static CameraSensorParameters Clean => new(NoiseEnabled: false, DarkElectronsPerSecond: 0,
        PrnuPercent: 0, DsnuElectrons: 0, GainDnPerElectron: .1, BlackLevel: 0, FullWellElectrons: 1e7, BitDepth: 16);
    internal static CameraImagingRequest Request(CameraNscDetectorInput input) => new(Source: CameraSource.NonSequentialDetector,
        Width: input.Detector.PixelsX, Height: input.Detector.PixelsY, Optics: CameraOptics.DetectorInput,
        Sensor: Clean, SpectralResponse: Response, SpectralModel: CameraSpectralModel.DetectorBands, NscDetector: input);

    internal static async Task<(Guid Detector, int GlassNumber)> Setup(WorkbenchApplication app, bool glass = false)
    {
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        foreach (var o in app.NonSequential.GetDocument().Objects.ToArray()) app.NonSequential.DeleteObject(o.Id);
        app.NonSequential.UpdateWavelengths([new(1, "green", 550, .01, true), new(2, "blue", 450, 100, false)]);
        void Add(NonSequentialObjectKind kind, double z, NonSequentialObjectParameters parameters)
        {
            var id = app.NonSequential.AddObject(kind);
            app.NonSequential.UpdateObject(new(id, true, true, kind, kind.ToString(), null, null, 0, 0, z, 0, 0, 0, parameters));
        }
        Add(NonSequentialObjectKind.SourceRay, 0, new SourceRayParameters(1e-13, 1, new(-.0045, -.0045, 0), new(0, 0, 1)));
        Add(NonSequentialObjectKind.SourceRay, 0, new SourceRayParameters(2e-13, 2, new(.0045, .0045, 0), new(0, 0, 1)));
        if (glass) Add(NonSequentialObjectKind.Box, 5, new BoxParameters(1, 1, 1, "N-BK7", NonSequentialSurfaceBehavior.Refractive));
        Add(NonSequentialObjectKind.DetectorRectangle, 10, new DetectorRectangleParameters(.012, .012, 4, 4, true, true));
        var doc = app.NonSequential.GetDocument(); var detector = doc.Objects.Single(o => o.Kind == NonSequentialObjectKind.DetectorRectangle).Id;
        await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        return (detector, glass ? doc.Objects.Single(o => o.Kind == NonSequentialObjectKind.Box).ObjectNumber : 0);
    }

    [Fact]
    public async Task CurrentAnalysisFeedsAbsolutePowerAndOnePhotonConversionWithCorrectRowsAndCfa()
    {
        using var app = WorkbenchApplication.Create(); var setup = await Setup(app);
        var session = app.NonSequentialAnalysis.GetCurrentSession()!;
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector);
        var run = await app.CameraImaging.SimulateAsync(Request(input));
        Assert.Equal(session.Id, input.SessionId); Assert.Equal(session.SceneHash, input.SceneHash);
        Assert.Equal(session.SourceRevision, input.SourceRevision); Assert.Equal(.01, input.Wavelengths[0].Weight);
        Assert.InRange(Math.Abs(input.Bands.Sum(b => b.PixelPowerWatts.Sum()) / 3e-13 - 1), 0, 1e-12);
        const double hc = 6.62607015e-34 * 299792458;
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
        {
            var channel = (y % 2, x % 2) switch { (0, 0) => 0, (1, 1) => 2, _ => 1 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(3 - y) * 4 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * new[] { .2, .5, .8 }[channel]);
            Assert.Equal(expected, run.PhotoElectrons[y * 4 + x], 10);
        }
        var doubled = await app.CameraImaging.RecomputeAsync(run, run.Request with { Sensor = Clean with { ExposureMilliseconds = 20 } });
        for (var i = 0; i < 16; i++) Assert.Equal(run.PhotoElectrons[i] * 2, doubled.PhotoElectrons[i], 10);
        var metadataOnly = await app.CameraImaging.RecomputeAsync(run, run.Request with { Sensor = Clean with { ReferenceIrradiance = 100, RedQe = 0, GreenQe = 0, BlueQe = 0 } });
        Assert.Equal(run.PhotoElectrons, metadataOnly.PhotoElectrons);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)input.Bands[0].PixelPowerWatts)[0] = 1);
        Assert.Equal(0, run.OpticalFallbackCount); Assert.Equal(CameraExperiment.NonSequentialEngineVersion, run.Inputs!.EngineVersion);
    }

    [Fact]
    public async Task PrimaryAndGhostFiltersPartitionTheSameOpticalPowerWithoutAddingThePrimaryTwice()
    {
        using var app = WorkbenchApplication.Create(); var setup = await Setup(app, true);
        var all = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector, "");
        var primary = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector, $"!R{setup.GlassNumber}");
        var ghost = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector, $"R{setup.GlassNumber}");
        Assert.True(ghost.Bands.Sum(b => b.PixelPowerWatts.Sum()) > 0);
        var total = await app.CameraImaging.SimulateAsync(Request(all));
        var a = await app.CameraImaging.SimulateAsync(Request(primary)); var b = await app.CameraImaging.SimulateAsync(Request(ghost));
        for (var i = 0; i < 16; i++) Assert.Equal(a.PhotoElectrons[i] + b.PhotoElectrons[i], total.PhotoElectrons[i], 9);
        Assert.True(total.MeanPhotoElectrons > a.MeanPhotoElectrons);
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("nsc.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, total, a), path);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_NSC_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "nsc-main-ghost.camera-experiment.zip"), true);
            await File.WriteAllTextAsync(Path.Combine(evidence, "verification.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                TotalPowerWatts = all.Bands.Sum(b => b.PixelPowerWatts.Sum()),
                PrimaryPowerWatts = primary.Bands.Sum(b => b.PixelPowerWatts.Sum()),
                GhostPowerWatts = ghost.Bands.Sum(b => b.PixelPowerWatts.Sum()),
                MaximumElectronPartitionError = total.PhotoElectrons.Select((v, i) => Math.Abs(v - a.PhotoElectrons[i] - b.PhotoElectrons[i])).Max(),
                Source = "Analytical glass-box NSC control; not measured data or commercial certification"
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        var opened = await app.CameraImaging.OpenExperimentAsync(path);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(total.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(total.RawDn, replay.RawDn);
        Assert.Equal(total.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(primary.PathFilter, opened.A!.Request.NscDetector!.PathFilter);
        await app.CameraImaging.ExportAsync(replay, path, opened.A);
        if (!string.IsNullOrWhiteSpace(evidence)) File.Copy(path, Path.Combine(evidence, "nsc-comparison.zip"), true);
        using var zip = ZipFile.OpenRead(path); Assert.NotNull(zip.GetEntry("B/nsc-detector.json"));
        Assert.NotNull(zip.GetEntry("B/detector-power.csv")); Assert.NotNull(zip.GetEntry("A/sensor.csv"));
    }

    [Fact]
    public async Task StaleSessionsMissingQeWrongPitchResamplingAndCancellationAreRejected()
    {
        using var app = WorkbenchApplication.Create(); var setup = await Setup(app);
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(Request(input) with { SpectralResponse = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(Request(input) with { Sensor = Clean with { PixelPitchMicrometers = 6 } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(Request(input) with { PixelSampling = new(2) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(Request(input) with { Width = 8 }));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector, cancellationToken: cts.Token));
        app.NonSequential.UpdateWavelengths([new(1, "changed", 600, 1, true), new(2, "blue", 450, 100, false)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector));
    }

    [Fact]
    public async Task EmptySelectedPathsStayDarkAndDoNotInventAThreeBandImage()
    {
        using var app = WorkbenchApplication.Create(); var setup = await Setup(app);
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.Detector, "!D3");
        var run = await app.CameraImaging.SimulateAsync(Request(input));
        Assert.Empty(input.Bands); Assert.All(run.PhotoElectrons, v => Assert.Equal(0, v));
        Assert.All(run.RawDn, v => Assert.Equal(0, v));
    }

    [Theory]
    [InlineData("camera-scene-v7-frozen.zip")]
    [InlineData("camera-scene-v8-frozen.zip")]
    [InlineData("camera-sampling-v1-frozen.zip")]
    public async Task HistoricalExperimentsAndSamplingReportFingerprintsRemainReadable(string fixture)
    {
        using var app = WorkbenchApplication.Create();
        var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.Null(opened.B.Request.NscDetector);
        Assert.NotEqual(CameraExperiment.NonSequentialEngineVersion, opened.B.Inputs!.EngineVersion);
        if (fixture.Contains("sampling")) Assert.NotNull(opened.SamplingReport);
    }
}
