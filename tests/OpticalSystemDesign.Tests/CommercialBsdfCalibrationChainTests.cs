using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfCalibrationChainTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task IndependentlyValidatedSyntheticFitReachesRawIspWithFrozenEvidence(bool transmission)
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var scene = await MeasuredBsdfChainTests.Setup(app, files);
        if (!transmission) app.NonSequential.UpdateObject(new(scene.Plane, true, true, NonSequentialObjectKind.PlaneRectangle, "calibration control", null, null, 0, 0, 0, 0, 0, 0, new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective, "AIR", "AIR")));
        var xyz = files.Path("input.BSDF"); var calibration = files.Path("fitted.json"); File.WriteAllText(xyz, SpeosV11XyzTests.XyzText(transmission)); File.WriteAllText(calibration, CommercialBsdfCalibrationTests.CalibrationJson());
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(scene.Plane, xyz, options: new(BsdfImportSides.BothDeclaredIdentical, ZemaxXyzCalibrationPath: calibration));
        var trace = await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs((trace.AbsorbedPowerWatts + trace.DetectorPowerWatts + trace.EscapedPowerWatts + trace.TruncatedPowerWatts) / trace.SourcePowerWatts - 1), 0, 1e-12);
        Assert.InRange(Math.Abs(trace.AbsorbedPowerWatts / trace.SourcePowerWatts - .7), 0, 1e-12);
        var detector = transmission ? scene.TransmittedDetector : app.NonSequential.GetDocument().Objects.First(o => o.Kind == NonSequentialObjectKind.DetectorRectangle).Id;
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(detector, transmission ? "B2 & D4" : "S2 & R2 & D3");
        var count = input.BsdfStatistics!.SampleCount; Assert.Equal(8192, count); Assert.InRange(Math.Abs(input.BsdfStatistics.MeanCosine - 2d / 3), 0, 6 * Math.Sqrt(1d / (18 * count)));
        var run = await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input)); Assert.True(run.MeanPhotoElectrons > 0); const double hc = 6.62607015e-34 * 299792458; double maximum = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe); maximum = Math.Max(maximum, Math.Abs(expected - run.PhotoElectrons[y * 32 + x])); Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        var experiment = files.Path("fit.camera-experiment.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, run), experiment);
        var project = files.Path("fit.staropt"); await app.Documents.SaveAsync(project); File.Delete(xyz); File.Delete(calibration); await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        await app.Documents.OpenAsync(project); Assert.Equal(model.MeasuredData!.DataSha256, app.NonSequential.GetDocument().Objects.Single(o => o.Id == scene.Plane).SurfaceBsdf!.MeasuredData!.DataSha256); app.Documents.NewBlank();
        var opened = await app.CameraImaging.OpenExperimentAsync(experiment); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        var evidence = replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.ZemaxXyz!.Calibration.FitEvidence!;
        Assert.Equal(OpticalSystemDesign.Core.Scattering.XyzCalibrationDataKind.SyntheticControl, evidence.Provenance.Kind); Assert.All(evidence.Bands, b => Assert.True(b.Passed));
        var dir = Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_CALIBRATION_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(dir)) { Directory.CreateDirectory(dir); File.Copy(experiment, Path.Combine(dir, "fitted-chain-" + transmission + ".camera-experiment.zip"), true); File.WriteAllText(Path.Combine(dir, "fitted-chain-" + transmission + ".json"), System.Text.Json.JsonSerializer.Serialize(new { Kind = "Synthetic fitted control; not measured/commercial certification", MaximumPhotonConversionError = maximum, input.BsdfStatistics, ExactReplay = true, evidence }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
    }
    [Fact]
    public async Task PreviousGridFourXyzCalibrationKeepsItsOriginalFingerprintAndReplay()
    {
        using var app = WorkbenchApplication.Create(); var old = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-xyz-grid4-frozen.zip")); var replay = await app.CameraImaging.RecomputeAsync(old.B);
        Assert.Null(replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.ZemaxXyz!.Calibration.FitEvidence);
        Assert.Equal(old.B.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.DataSha256, replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.DataSha256); Assert.Equal(old.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(old.B.RawDn, replay.RawDn); Assert.Equal(old.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
}
