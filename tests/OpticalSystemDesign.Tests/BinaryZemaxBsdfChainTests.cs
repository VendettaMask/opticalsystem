using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class BinaryZemaxBsdfChainTests
{
    [Theory]
    [InlineData("binary")] [InlineData("zemax-r")] [InlineData("zemax-t")]
    public async Task ImportedFilesUseOnePhotonConversionAndFrozenRawIspReplay(string format)
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await MeasuredBsdfChainTests.Setup(app, files);
        var reflectionOnly = format == "zemax-r";
        if (reflectionOnly) app.NonSequential.UpdateObject(new(setup.Plane, true, true, NonSequentialObjectKind.PlaneRectangle, "Zemax BRDF", null, null, 0, 0, 0, 0, 0, 0, new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective, "AIR", "AIR")));
        var path = files.Path(format == "binary" ? "synthetic.brdf" : "synthetic.BSDF");
        if (format == "binary") File.WriteAllBytes(path, BinaryZemaxBsdfTests.BinarySpeos()); else File.WriteAllText(path, BinaryZemaxBsdfTests.Zemax(!reflectionOnly));
        var options = new BsdfImportOptions(BsdfImportSides.BothDeclaredIdentical, 550, true);
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, path, options: options);
        var trace = await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts + trace.DetectorPowerWatts + trace.EscapedPowerWatts + trace.TruncatedPowerWatts) / trace.SourcePowerWatts - 1), 0, 1e-12);
        var detector = reflectionOnly ? app.NonSequential.GetDocument().Objects.First(o => o.Kind == NonSequentialObjectKind.DetectorRectangle).Id : setup.TransmittedDetector;
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector, reflectionOnly ? "S2 & R2 & D3" : "B2 & D4");
        Assert.Equal("MeasuredBsdfGrid/3", input.SurfaceBsdfProtocol);
        var run = await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input)); const double hc = 6.62607015e-34 * 299792458; double max = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe);
            max = Math.Max(max, Math.Abs(expected - run.PhotoElectrons[y * 32 + x])); Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        Assert.True(run.MeanPhotoElectrons > 0); Assert.Contains(run.RawDn, x => x > 0); Assert.Contains(run.Developed.Rgb24, x => x > 0);
        var experiment = files.Path(format + ".camera-experiment.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, run), experiment);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_BINARY_ZEMAX_BSDF_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, format + Path.GetExtension(path)), true); File.Copy(experiment, Path.Combine(evidence, Path.GetFileName(experiment)), true); }
        File.Delete(path); await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        var opened = await app.CameraImaging.OpenExperimentAsync(experiment); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(model.MeasuredData!.DataSha256, replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.DataSha256);
        BinaryZemaxBsdfTests.Record("chain-" + format, new { model.MeasuredData.SourceFormat, model.MeasuredData.SourceSha256, model.MeasuredData.DataSha256, MaximumPhotonConversionError = max, input.BsdfStatistics, ExactFrozenRawIspReplay = true });
    }
    [Fact]
    public async Task PreviousFixedAxisExperimentRetainsItsOriginalProtocolAndFingerprint()
    {
        using var app = WorkbenchApplication.Create(); var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-speos-text-grid2-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/2", opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B); Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn); Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
}
