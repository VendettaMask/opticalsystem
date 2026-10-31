using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

public sealed class CommercialBsdfChainTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CommercialReflectionTransmissionFreezeAndReplayTheCompleteRawIspChain(bool anisotropic)
    {
        using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
        var setup = await MeasuredBsdfChainTests.Setup(app, files); var source = files.Path(anisotropic ? "synthetic.anisotropicbsdf" : "synthetic.brdf");
        File.WriteAllText(source, CommercialBsdfTests.Speos(anisotropic));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, source));
        var model = await app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, source, options: new(BsdfImportSides.BothDeclaredIdentical));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NonSequential.ImportMeasuredBsdfAsync(setup.Plane, source, cancellation.Token, new(BsdfImportSides.BothDeclaredIdentical)));
        Assert.Equal(model.MeasuredData!.DataSha256, app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData!.DataSha256);
        var trace = await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
        Assert.InRange(Math.Abs(trace.SourcePowerWatts - trace.AbsorbedPowerWatts - trace.DetectorPowerWatts - trace.EscapedPowerWatts - trace.TruncatedPowerWatts) / trace.SourcePowerWatts, 0, 1e-12);
        var input = await app.CameraImaging.CaptureNonSequentialDetectorAsync(setup.TransmittedDetector, "B2 & T2 & !R2 & D4");
        Assert.Equal("MeasuredBsdfGrid/2", input.SurfaceBsdfProtocol); var frozen = Assert.Single(input.SurfaceBsdfs!);
        Assert.Equal(model.MeasuredData.DataSha256, frozen.Model.MeasuredData!.DataSha256);
        Assert.Equal(anisotropic, frozen.WorldMaterialAxisX is not null);
        Assert.Equal(anisotropic ? input.BsdfStatistics!.SampleCount : 0, input.BsdfStatistics!.MaterialAxisSampleCount);
        var run = await app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(input)); const double hc = 6.62607015e-34 * 299792458;
        if (anisotropic)
        {
            var forged = input with { SurfaceBsdfs = new[] { frozen with { WorldMaterialAxisX = null } } };
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateAsync(MeasuredBsdfChainTests.Request(forged)));
        }
        double maxError = 0;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var qe = (y % 2, x % 2) switch { (0, 0) => .2, (1, 1) => .8, _ => .5 };
            var expected = input.Bands.Sum(b => b.PixelPowerWatts[(31 - y) * 32 + x] * .01 * b.WavelengthNanometers * 1e-9 / hc * qe);
            maxError = Math.Max(maxError, Math.Abs(expected - run.PhotoElectrons[y * 32 + x])); Assert.Equal(expected, run.PhotoElectrons[y * 32 + x], 10);
        }
        Assert.Contains(run.RawDn, v => v > 0); Assert.Contains(run.Developed.Rgb24, v => v > 0);
        var path = files.Path((anisotropic ? "anisotropic" : "brdf") + ".camera-experiment.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, run), path);
        var dir = Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_BSDF_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(dir)) { Directory.CreateDirectory(dir); File.Copy(source, Path.Combine(dir, Path.GetFileName(source)), true); File.Copy(path, Path.Combine(dir, Path.GetFileName(path)), true); }
        File.Delete(source); await app.NonSequentialAnalysis.ClearDetectorsAsync(); app.Documents.NewBlank();
        var opened = await app.CameraImaging.OpenExperimentAsync(path); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
        Assert.Equal(model.MeasuredData.ImportNotes, replay.Request.NscDetector!.SurfaceBsdfs![0].Model.MeasuredData!.ImportNotes);
        CommercialBsdfTests.Record("camera-chain-" + anisotropic, new { model.MeasuredData.SourceFormat, model.MeasuredData.SourceSha256, model.MeasuredData.DataSha256, MaximumPhotonConversionError = maxError, input.BsdfStatistics, ExactFrozenRawIspReplay = true });
    }

    [Fact]
    public async Task PreviousMeasuredV9ExperimentRetainsItsOriginalFingerprintAndRawIsp()
    {
        using var app = WorkbenchApplication.Create(); var opened = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-measured-v9-frozen.zip"));
        Assert.Equal("MeasuredBsdfGrid/1", opened.B.Request.NscDetector!.SurfaceBsdfProtocol);
        var replay = await app.CameraImaging.RecomputeAsync(opened.B);
        Assert.Equal(opened.B.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(opened.B.RawDn, replay.RawDn); Assert.Equal(opened.B.Developed.Rgb24, replay.Developed.Rgb24);
    }
}
