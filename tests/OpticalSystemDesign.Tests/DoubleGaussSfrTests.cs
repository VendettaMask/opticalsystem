using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class DoubleGaussSfrTests
{
    internal const string StandardRelativePath = "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx";
    internal static string StandardLens => Path.Combine(SlantedEdgeSfrTests.RepositoryRoot, StandardRelativePath);

    [Fact]
    public void UnwrappedPhaseRejectsAliasingRatherThanJudgingWrappedComplexPhase()
    {
        WavefrontResult Wave(double step, double piston = 0) => new(
            Enumerable.Range(0, 16).Select(i => new WavefrontSample(-.75 + .5 * (i % 4), -.75 + .5 * (i / 4),
                0, 0, 0, piston + step * (i % 4), i == 0 ? 0 : 1)).ToArray(), 1, 1, 0)
            { PupilGrid = new(4, true, false, 1) };
        DiffractionEngine.ValidateLocalPupilSampling(Wave(.2, 1000));
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ValidateLocalPupilSampling(Wave(13)));
        Assert.Contains("0.5", exception.Message); Assert.Contains("13", exception.Message);
    }

    [Theory]
    [InlineData(2)] [InlineData(-5)] [InlineData(10)]
    public void ProjectedConvolutionMatchesIndependentClippedPixelAreaForAsymmetricPsf(double angle)
    {
        var random = new Random(731); var values = new double[33, 33];
        for (var y = 0; y < 33; y++) for (var x = 0; x < 33; x++) values[y, x] = random.NextDouble() * (x + 1);
        var mass = values.Cast<double>().Sum(); var a = angle * Math.PI / 180; var nx = Math.Cos(a); var ny = -Math.Sin(a);
        var settings = new SlantedEdgeSettings(EdgeAngleDegrees: angle, PupilSampling: 32, ImageSamplesPerPixel: 4, PsfRadiusPixels: 4);
        var frame = MonochromeSlantedEdge.RenderPsf(new(values, 32, 33, 4, .75), settings);
        foreach (var y in new[] { 0, 11, 37, 65, 95 }) foreach (var x in new[] { 0, 41, 45, 49, 53, 57, 95 })
        {
            double expected = 0;
            for (var row = 0; row < 33; row++) for (var column = 0; column < 33; column++)
                expected += values[row, column] / mass * ClippedArea(x + .5 - 48 - .2 - (column - 16) / 4d,
                    y + .5 - 48 - (row - 16) / 4d, nx, ny);
            Assert.InRange(Math.Abs(frame.LinearPixels[y * 96 + x] - (.2 + .6 * expected)), 0, 2e-11);
        }
    }

    [Fact]
    public void StandardDoubleGaussWindowExpansionConvergesWithoutChangingItsInfiniteConjugate()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(StandardLens), ".zmx");
        var json = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var before = JsonSerializer.Serialize(optic.ToSnapshot(), json);
        Assert.True(double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness));
        var oldWindow = MonochromeSlantedEdge.Run(optic, new(PsfRadiusPixels: 8));
        Assert.False(oldWindow.SamplingConverged);
        // V2's exact .017676 value belongs to its archived point-cell protocol.
        // V3 must still reject the small window against the unchanged .01 gate.
        Assert.Contains(oldWindow.Sampling, c => c.Axis == "PsfWindow" && !c.WithinCriteria && c.MaximumMtfDifference > oldWindow.DifferenceThreshold);
        var run = MonochromeSlantedEdge.Run(optic, new());
        Assert.True(run.SamplingConverged); Assert.All(run.Sampling, c => Assert.True(c.WithinCriteria, c.Message));
        Assert.Equal(32, run.Baseline.Settings.PsfRadiusPixels);
        Assert.InRange(run.Baseline.Measurement.Mtf50CyclesPerPixel!.Value, .100, .102);
        Assert.InRange(run.Baseline.MaximumSfrReferenceDifference, 0, .005);
        Assert.InRange(run.Baseline.RelativePsfReadoutIntegral, .99, 1);
        Assert.Equal(before, JsonSerializer.Serialize(optic.ToSnapshot(), json));
    }

    [Theory]
    [InlineData("Double Gauss 28 degree field_2.zmx", 500)]
    [InlineData("double-gauss-50mm.zmx", double.PositiveInfinity)]
    public void OriginalSeverelyDefocusedFilesRejectUndersampledPhaseAt128And512(string file, double objectDistance)
    {
        var path = Path.Combine(SlantedEdgeSfrTests.RepositoryRoot, "local-data/lens-library/originals/user-zmx/project/samples/lenses", file);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        Assert.Equal(objectDistance, optic.SurfaceGroup.Items[0].Thickness);
        foreach (var n in new[] { 128, 512 })
        {
            var error = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeLocalIncoherentPsf(optic, (0, 0),
                new() { Micrometers = .55 }, n, 129, .375));
            Assert.Contains("相位欠采样", error.Message);
        }
    }

    [Fact]
    public async Task ApplicationExportsConvergedStandardDoubleGaussAndPreservesLegacyViewOnlyIdentity()
    {
        using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(StandardLens);
        var before = app.Documents.GetSnapshot();
        var result = await app.CameraImaging.SimulateSlantedEdgeAsync(new());
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.True(result.SamplingConverged);
        Assert.Equal("MonochromeLocalFourierEdge/3", result.Algorithm);
        var dir = Environment.GetEnvironmentVariable("OPTICAL_DOUBLE_GAUSS_SFR_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            await app.CameraImaging.ExportSlantedEdgeAsync(result, Path.Combine(dir, "double-gauss-standard.sfr-experiment.zip"));
        }
        var replay = await app.CameraImaging.RecomputeSlantedEdgeAsync(result);
        Assert.Equal(result.Baseline.LinearPixels, replay.Baseline.LinearPixels); Assert.Equal(result.Baseline.Curve, replay.Baseline.Curve);
        // Protocol test fixture; deliberately retagged data is not historical optical evidence.
        var legacy = result with { Algorithm = CameraSfrResult.PreviousAlgorithm };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfr-experiment.zip");
        try
        {
            await app.CameraImaging.ExportSlantedEdgeAsync(legacy, path);
            var opened = await app.CameraImaging.OpenSlantedEdgeAsync(path);
            Assert.Equal(legacy.Algorithm, opened.Algorithm); Assert.Equal(legacy.Baseline.LinearPixels, opened.Baseline.LinearPixels);
            await Assert.ThrowsAsync<NotSupportedException>(() => app.CameraImaging.RecomputeSlantedEdgeAsync(opened));
        }
        finally { File.Delete(path); }
    }

    // Independent polygon clipping against the bright half-plane; its area is the
    // physical square-pixel integral, without calling the renderer's coverage formula.
    private static double ClippedArea(double cx, double cy, double nx, double ny)
    {
        var points = new[] { (X: cx - .5, Y: cy - .5), (X: cx + .5, Y: cy - .5), (X: cx + .5, Y: cy + .5), (X: cx - .5, Y: cy + .5) };
        var clipped = new List<(double X, double Y)>();
        for (var i = 0; i < 4; i++)
        {
            var p = points[i]; var q = points[(i + 1) % 4]; var dp = nx * p.X + ny * p.Y; var dq = nx * q.X + ny * q.Y;
            if (dp >= 0) clipped.Add(p);
            if (dp >= 0 != dq >= 0) { var t = dp / (dp - dq); clipped.Add((p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y))); }
        }
        double area = 0;
        for (var i = 0; i < clipped.Count; i++) { var p = clipped[i]; var q = clipped[(i + 1) % clipped.Count]; area += p.X * q.Y - q.X * p.Y; }
        return Math.Abs(area) / 2;
    }
}
