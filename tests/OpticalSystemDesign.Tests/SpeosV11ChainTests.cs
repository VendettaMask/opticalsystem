using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class SpeosV11ChainTests
{
    [Fact]
    public async Task OriginalProjectedProtocolThreeExperimentReplaysWithoutMigration()
    {
        using var app = WorkbenchApplication.Create(); var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-projected-grid3-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/3", opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B); Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn); Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
    [Theory]
    [InlineData("v11-binary")] [InlineData("speos-white")] [InlineData("xyz-r")] [InlineData("xyz-t")]
    public async Task ImportedFilesUseOnePhotonConversionAndFrozenRawIspReplay(string format)
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await MeasuredBsdfChainTests.Setup(app, files);
        var reflectionOnly = format is "v11-binary" or "xyz-r";
        if (reflectionOnly) app.NonSequential.UpdateObject(new(setup.Plane, true, true, NonSequentialObjectKind.PlaneRectangle, "Zemax BRDF", null, null, 0, 0, 0, 0, 0, 0, new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective, "AIR", "AIR")));
        var path = files.Path(format == "v11-binary" ? "synthetic.anisotropicbsdf" : "synthetic.BSDF");
        var calibration = files.Path("xyz-calibration.json"); File.WriteAllText(calibration, SpeosV11XyzTests.Calibration());
        if (format == "v11-binary") File.WriteAllBytes(path, SpeosV11BinaryTests.Binary(true, true));
        else File.WriteAllText(path, format == "speos-white" ? SpeosV11XyzTests.SpeosText(both: true) : SpeosV11XyzTests.XyzText(!reflectionOnly));
        var options = new BsdfImportOptions(BsdfImportSides.BothDeclaredIdentical, 550, true, ZemaxXyzCalibrationPath: format.StartsWith("xyz") ? calibration : null,
            SpeosPeak: format == "speos-white" ? new(14, .01, 550) : null);
        app.NonSequential.UpdateWavelengths([new(1, "blue", 400, 7, false), new(2, "green", 550, 100, true), new(3, "red", 700, 13, false)]);

        foreach (var number in new[] { 2, 3 }) { var id = app.NonSequential.AddObject(NonSequentialObjectKind.SourcePoint); app.NonSequential.UpdateObject(new(id, true, true, NonSequentialObjectKind.SourcePoint, "spectral source", null, null, 0, 0, .032, 0, 180, 0, new SourcePointParameters(1e-11, number, 0, 20, 4096))); }
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, path, options: options);
        var trace = await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts + trace.DetectorPowerWatts + trace.EscapedPowerWatts + trace.TruncatedPowerWatts) / trace.SourcePowerWatts - 1), 0, 1e-12);
        var detector = reflectionOnly ? app.NonSequential.GetDocument().Objects.First(o => o.Kind == NonSequentialObjectKind.DetectorRectangle).Id : setup.TransmittedDetector;
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector, reflectionOnly ? "S2 & R2 & D3" : "B2 & D4");
        Assert.Equal("MeasuredBsdfGrid/4", input.SurfaceBsdfProtocol); Assert.Equal(3, input.Bands.Count);
        var run = await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input)); const double hc = 6.62607015e-34 * 299792458; double max = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe);
            max = Math.Max(max, Math.Abs(expected - run.PhotoElectrons[y * 32 + x])); Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        Assert.True(run.MeanPhotoElectrons > 0); Assert.Contains(run.RawDn, x => x > 0); Assert.Contains(run.Developed.Rgb24, x => x > 0);
        var experiment = files.Path(format + ".camera-experiment.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, run), experiment);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_SPEOS_V11_XYZ_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, format + Path.GetExtension(path)), true); File.Copy(experiment, Path.Combine(evidence, Path.GetFileName(experiment)), true); }
        var project = files.Path(format + ".staropt"); await app.Documents.SaveAsync(project);
        File.Delete(path); File.Delete(calibration); await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        await app.Documents.OpenAsync(project);
        var fromProject = app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData!;
        Assert.Equal(model.MeasuredData!.DataSha256, fromProject.DataSha256);
        app.Documents.NewBlank(); var opened = await app.CameraImaging.OpenExperimentAsync(experiment); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(model.MeasuredData!.DataSha256, replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.DataSha256);
        SpeosV11XyzTests.Record("chain-" + format, new { model.MeasuredData.SourceFormat, model.MeasuredData.SourceSha256, model.MeasuredData.DataSha256, MaximumPhotonConversionError = max, ProjectVersion = 12, input.BsdfStatistics, ExactFrozenRawIspReplay = true });
    }
}
