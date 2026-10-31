using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CameraBsdfChainTests
{
    internal static async Task<Guid> Setup(WorkbenchApplication app)
    {
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        foreach (var o in app.NonSequential.GetDocument().Objects.ToArray()) app.NonSequential.DeleteObject(o.Id);
        app.NonSequential.UpdateWavelengths([new(1, "green", 550, 100, true)]);
        Guid Add(NonSequentialObjectKind kind, double z, NonSequentialObjectParameters p, double tilt = 0, NonSequentialSurfaceBsdf? bsdf = null)
        {
            var id = app.NonSequential.AddObject(kind);
            app.NonSequential.UpdateObject(new(id, true, true, kind, kind.ToString(), null, null, 0, 0, z, 0, tilt, 0, p, bsdf));
            return id;
        }
        Add(NonSequentialObjectKind.SourcePoint, .032, new SourcePointParameters(1e-11, 1, 0, 20, 8192), 180);
        Add(NonSequentialObjectKind.PlaneRectangle, 0, new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective, "AIR", "AIR"),
            bsdf: new(NonSequentialBsdfKind.LambertianReflection, .5));
        var detector = Add(NonSequentialObjectKind.DetectorRectangle, .064, new DetectorRectangleParameters(.128, .128, 32, 32, true, true));
        await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        return detector;
    }

    [Fact]
    public async Task ScatteredWattsFeedOnePhotonConversionRawIspAndIndependentFrozenRecompute()
    {
        using var app = WorkbenchApplication.Create(); var detector = await Setup(app);
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector, "S2 & D3");
        Assert.Equal("LambertianReflection/1", input.SurfaceBsdfProtocol);
        Assert.Equal(.5, Assert.Single(input.SurfaceBsdfs!).Model.Reflectance);
        Assert.Equal(8192, input.BsdfStatistics!.SampleCount);
        Assert.InRange(Math.Abs(input.BsdfStatistics.MeanCosine - 2d / 3), 0, 6 * Math.Sqrt(1d / (18 * 8192)));
        var request = CameraNonSequentialServiceTests.Request(input) with
        { Sensor = CameraNonSequentialServiceTests.Clean with { PixelPitchMicrometers = 4 } };
        var run = await app.CameraImaging.SimulateAsync(request);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(request with { NscDetector = input with { SurfaceBsdfProtocol = "unknown/1" } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(request with { NscDetector = input with { BsdfStatistics = input.BsdfStatistics with { SampleCount = 0 } } }));
        var power = input.Bands.Sum(b => b.PixelPowerWatts.Sum()); Assert.True(power > 0);
        const double hc = 6.62607015e-34 * 299792458;
        double maxError = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe);
            maxError = Math.Max(maxError, Math.Abs(expected - run.PhotoElectrons[y * 32 + x]));
            Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        Assert.Contains(run.RawDn, v => v > 0); Assert.Contains(run.Developed.Rgb24, v => v > 0);
        var doubled = await app.CameraImaging.RecomputeAsync(run, request with { Sensor = request.Sensor! with { ExposureMilliseconds = 20 } });
        for (var i = 0; i < 1024; i++) Assert.Equal(2 * run.PhotoElectrons[i], doubled.PhotoElectrons[i], 10);
        var dark = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector, "!S2 & D3");
        var darkRun = await app.CameraImaging.SimulateAsync(request with { NscDetector = dark });
        Assert.All(darkRun.PhotoElectrons, value => Assert.Equal(0, value));
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("lambertian.camera-experiment.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, doubled), path);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_BSDF_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, Path.GetFileName(path)), true); }
        await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        var reopened = await app.CameraImaging.OpenExperimentAsync(path);
        var replay = await app.CameraImaging.RecomputeAsync(reopened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn);
        Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(.5, Assert.Single(replay.Request.NscDetector!.SurfaceBsdfs!).Model.Reflectance);
        Assert.Equal(input.BsdfStatistics, replay.Request.NscDetector.BsdfStatistics);
        NonSequentialBsdfTests.Record("camera-chain", new { PowerWatts = power, MaximumPhotonConversionError = maxError,
            PhotoElectrons = run.PhotoElectrons.Sum(), ReplayedExactly = true, Width = 32, Height = 32, PixelPitchMicrometers = 4,
            OpticalModel = input.SurfaceBsdfProtocol, Statistics = input.BsdfStatistics,
            Source = "Analytical Lambertian control; not measured or commercial reference data" });
    }

    [Fact]
    public async Task TraceOnlyAccumulatesWholeTraceSamplingStatisticsWithoutChangingFrozenInputs()
    {
        using var app = WorkbenchApplication.Create(); var detector = await Setup(app);
        var first = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector);
        var next = await app.NonSequentialAnalysis.TraceAsync(new(Command: NonSequentialTraceCommand.TraceOnly,
            SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.Equal(8192, next.BsdfStatistics!.SampleCount);
        var second = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector);
        Assert.Equal(16384, second.BsdfStatistics!.SampleCount); Assert.Equal(8192, first.BsdfStatistics!.SampleCount);
        Assert.InRange(Math.Abs(second.BsdfStatistics.ReflectedPowerWatts / second.BsdfStatistics.IncomingPowerWatts - .5), 0, 1e-12);
        Assert.Equal(2, second.TracePassCount);
    }

    [Fact]
    public async Task PreBsdfV9ExperimentRetainsFingerprintAndExactRawIspRecompute()
    {
        using var app = WorkbenchApplication.Create();
        var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-nsc-v9-frozen.zip"));
        Assert.Null(opened.B.Request.NscDetector!.SurfaceBsdfs); Assert.Null(opened.B.Request.NscDetector.SurfaceBsdfProtocol);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn);
        Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
}
