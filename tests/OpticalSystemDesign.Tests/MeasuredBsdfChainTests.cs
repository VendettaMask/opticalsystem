using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class MeasuredBsdfChainTests
{
    internal static async Task<(Guid Plane, Guid TransmittedDetector, string Path)> Setup(WorkbenchApplication app, CameraSensorFileTests.TemporaryFiles files)
    {
        await CameraBsdfChainTests.Setup(app);
        var plane = app.NonSequential.GetDocument().Objects.Single(o => o.Kind == NonSequentialObjectKind.PlaneRectangle);
        app.NonSequential.UpdateObject(new(plane.Id, true, true, plane.Kind, "Measured interface", null, null, 0, 0, 0, 0, 0, 0,
            new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Refractive, "AIR", "AIR")));
        var detector = app.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
        app.NonSequential.UpdateObject(new(detector, true, true, NonSequentialObjectKind.DetectorRectangle, "BTDF detector", null, null,
            0, 0, -.064, 0, 0, 0, new DetectorRectangleParameters(.128, .128, 32, 32, false, true)));
        var path = files.Path("control.bsdf.csv"); File.WriteAllText(path, MeasuredBsdfTests.Csv(MeasuredBsdfTests.Constant(.2, .5)));
        return (plane.Id, detector, path);
    }
    internal static CameraImagingRequest Request(CameraNscDetectorInput input) => CameraNonSequentialServiceTests.Request(input) with
    { Sensor = CameraNonSequentialServiceTests.Clean with { PixelPitchMicrometers = 4 } };

    [Fact]
    public async Task ImportedBtdfPowerHasOnePhotonConversionAndReplaysWithoutItsSourceFileSceneOrDatabase()
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await Setup(app, files);
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, setup.Path);
        Assert.NotNull(model.MeasuredData);
        var trace = await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs(trace.AbsorbedPowerWatts / trace.SourcePowerWatts - .3), 0, 1e-12);
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.TransmittedDetector, "B2 & S2 & T2 & !R2 & D4");
        Assert.Equal("MeasuredBsdfGrid/1", input.SurfaceBsdfProtocol);
        Assert.Equal(model.MeasuredData.SourceSha256, Assert.Single(input.SurfaceBsdfs!).Model.MeasuredData!.SourceSha256);
        Assert.True(input.BsdfStatistics!.TransmissionSampleCount > input.BsdfStatistics.ReflectionSampleCount);
        var request = Request(input); var run = await app.CameraImaging.SimulateAsync(request);
        const double hc = 6.62607015e-34 * 299792458; double max = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe);
            max = Math.Max(max, Math.Abs(expected - run.PhotoElectrons[y * 32 + x])); Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        Assert.Contains(run.RawDn, v => v > 0); Assert.Contains(run.Developed.Rgb24, v => v > 0);
        var dark = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.TransmittedDetector, "!S2 & D4");
        Assert.All((await app.CameraImaging.SimulateAsync(Request(dark))).PhotoElectrons, p => Assert.Equal(0, p));
        var doubled = await app.CameraImaging.RecomputeAsync(run, request with { Sensor = request.Sensor! with { ExposureMilliseconds = 20 } });
        for (var i = 0; i < 1024; i++) Assert.Equal(2 * run.PhotoElectrons[i], doubled.PhotoElectrons[i], 10);
        var path = files.Path("measured-btdf.camera-experiment.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, doubled), path);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_MEASURED_BSDF_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, Path.GetFileName(path)), true);
            File.Copy(setup.Path, Path.Combine(evidence, "synthetic-r02-t05.bsdf.csv"), true);
        }
        File.Delete(setup.Path); await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        var opened = await app.CameraImaging.OpenExperimentAsync(path); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(model.MeasuredData.DataSha256, replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.DataSha256);
        MeasuredBsdfTests.Record("camera-chain", new { PowerWatts = input.Bands.Sum(b => b.PixelPowerWatts.Sum()), MaximumPhotonConversionError = max,
            input.BsdfStatistics, ExactFrozenRawIspReplay = true, Source = "Synthetic analytical control, not a measured reference or certification" });
    }

    [Fact]
    public async Task ImportFailureCancellationImmutabilityAndNonLambertianPdfAreHandled()
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await Setup(app, files);
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, setup.Path);
        var bad = files.Path("bad.csv"); File.WriteAllText(bad, "unsupported");
        await Assert.ThrowsAsync<InvalidDataException>(() => app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, bad));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, setup.Path, cancel.Token));
        Assert.Equal(model.MeasuredData!.DataSha256, app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData!.DataSha256);
        Assert.Throws<NotSupportedException>(() => ((IList<MeasuredBsdfPointDto>)model.MeasuredData.Points)[0] = model.MeasuredData.Points[0]);
        File.WriteAllText(setup.Path, MeasuredBsdfTests.Csv(MeasuredBsdfTests.Data((_, _, mu, _) => (0, .75 * mu / Math.PI))));
        await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, setup.Path);
        await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.TransmittedDetector);
        Assert.True(input.BsdfStatistics!.MaximumPdf > 1 / Math.PI);
        Assert.True((await app.CameraImaging.SimulateAsync(Request(input))).MeanPhotoElectrons > 0);
    }

    [Theory]
    [InlineData("camera-lambertian-v9-frozen.zip")]
    [InlineData("camera-nsc-v9-frozen.zip")]
    public async Task PreviousV9ExperimentsRetainFingerprintsAndExactReplay(string name)
    {
        using var app = WorkbenchApplication.Create(); var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn);
        Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
}
