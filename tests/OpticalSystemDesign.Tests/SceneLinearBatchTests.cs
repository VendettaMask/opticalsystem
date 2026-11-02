using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Serialization;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class SceneLinearBatchTests
{
    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(16)]
    public void StripsRetainFullSensorCoordinatesPupilBoundsAndFullyVignettedRows(int rows)
    {
        var lens = SceneSlantedEdgeTests.FiniteConjugateLens();
        lens.SurfaceGroup.Items[^1].PhysicalAperture = new CircularAperture(.06);
        var chart = EmissiveCheckerboard.Create(new(4, 4, 2, 100, CenterYMillimeters: 1), [new(486, 2, .1), new(656, 1, .05)], 100);
        var settings = new DirectCameraRenderSettings(32, 40, 10, new(2), 2, 8);
        var full = DirectCameraRenderer.Render(lens, chart, settings); var values = new[] { new double[32 * 40], new double[32 * 40] };
        var starts = new List<int>();
        var batch = DirectCameraRenderer.RenderBatch(lens, [chart], settings, rows, strip =>
        {
            starts.Add(strip.FirstRow);
            Assert.Equal(full.PupilPlaneRadiusMillimeters, strip.PupilPlaneRadiusMillimeters);
            for (var b = 0; b < 2; b++) for (var i = 0; i < strip.Bands[b].PixelBandPowerWatts.Count; i++) values[b][strip.FirstRow * 32 + i] = strip.Bands[b].PixelBandPowerWatts[i];
        });
        for (var b = 0; b < 2; b++) Assert.Equal(full.DetectorPower.Bands[b].PixelBandPowerWatts, values[b]);
        Assert.Equal(0, starts[0]); Assert.Equal(full.LaunchedRays, batch.Sources[0].LaunchedRays);
        Assert.Equal(full.SurfaceInteractions, batch.Sources[0].SurfaceInteractions); Assert.Equal(0, values[0][0]); Assert.True(values[0].Max() > 0);
    }

    [Fact]
    public void GlobalBudgetCannotBeResetPerSourceOrStripAndOriginalRasterIsReportedWithoutCropping()
    {
        var plan = DirectCameraRenderer.PlanBatch(new(96, 96, 3, new(2), 8, 24), [1, 1, 1], 12);
        Assert.False(plan.WithinBudget); Assert.Equal(3 * 7077888, plan.Rays);
        Assert.True(DirectCameraRenderer.PlanBatch(new(96, 96, 3, new(2), 8, 24), [1], 12).WithinBudget);
        var original = DirectCameraRenderer.PlanBatch(new(5000, 3000, .5, new(1), 1, 4), [1, 1, 1], 12);
        Assert.False(original.WithinBudget); Assert.Equal(15000000, original.Pixels); Assert.Equal(180000000, original.Rays);
        Assert.Contains("未缩图", original.Failure);
        var surface = DirectCameraRenderer.PlanBatch(new(256, 256, 3, new(1), 1, 4), [1], 256);
        Assert.True(surface.WithinBudget);
        Assert.False(DirectCameraRenderer.PlanBatch(new(256, 256, 3, new(1), 1, 4), [1, 1], 256).WithinBudget);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void FiniteSourceShapeIntegratesRadianceAndKnownSpectralMomentsWithoutPhotonWeights(int order)
    {
        var q = SceneEmissionSpectrum.Integrate([new(400, 0), new(499, 0), new(500, 100), new(501, 0), new(800, 0)], 3, .6, order);
        Assert.Equal(1, q.ShapeAreaNanometers, 14); Assert.Equal(100, q.InputShapeMaximum);
        Assert.Equal(1, q.Nodes.Sum(n => n.SpectralFraction), 13);
        Assert.Equal(3, q.Nodes.Sum(n => n.BrightRadiance), 13); Assert.Equal(.6, q.Nodes.Sum(n => n.DarkRadiance), 13);
        Assert.Equal(500, q.Nodes.Sum(n => n.SpectralFraction * n.WavelengthNanometers), 10);
        Assert.Equal(1 / 6d, q.Nodes.Sum(n => n.SpectralFraction * Math.Pow(n.WavelengthNanometers - 500, 2)), 12);
        Assert.All(q.Nodes, n => Assert.InRange(n.WavelengthNanometers, 499, 501));
    }

    [Fact]
    public void Gray16UsesOnePhysicalLinearScaleAndConformsToAnIndependentPngDecoder()
    {
        var values = Enumerable.Range(0, 1024).Select(i => i / 1023d).ToArray();
        var bytes = LinearGray16Png.Encode(values, 32, 32, 1, CancellationToken.None);
        Assert.Equal(16, bytes[24]); Assert.Equal(0, bytes[25]);
        using var decoded = SKBitmap.Decode(bytes); Assert.NotNull(decoded); Assert.Equal(32, decoded.Width);
        Assert.Equal((byte)0, decoded.GetPixel(0, 0).Red); Assert.Equal((byte)255, decoded.GetPixel(31, 31).Red);
        for (var i = 0; i < 1024; i++) Assert.InRange(Math.Abs(decoded.GetPixel(i % 32, i / 32).Red / 255d - values[i]), 0, 1 / 255d);
        LinearGray16Png.Validate(bytes, values, 32, 32, 1, CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => LinearGray16Png.Encode(values, 32, 32, .5, CancellationToken.None));
        bytes[^5] ^= 1; Assert.Throws<InvalidDataException>(() => LinearGray16Png.Validate(bytes, values, 32, 32, 1, CancellationToken.None));
    }

    [Fact]
    public async Task LensTasksFreezeWeightsKeepZeroRowsAndRoundTripAllFloatPlanesAcrossStripSizesAndCultures()
    {
        using var app = WorkbenchApplication.Create(); var fixture = await LoadFixture(app);
        var first = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip"); var second = first + ".second.zip";
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            var request = SmallRequest() with { RowsPerStrip = 3, UseLensWeightsAsSourceMultipliers = true };
            var a = app.CameraImaging.PrepareSceneLinearBatch(request);
            var b = app.CameraImaging.PrepareSceneLinearBatch(request with { RowsPerStrip = 7 });
            Assert.Equal(3, a.Tasks.Count); Assert.Equal(2, a.Tasks[0].Source.LensWeight); Assert.Equal(0, a.Tasks[2].Nodes[0].BrightRadiance);
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var generated = await app.CameraImaging.RunSceneLinearBatchAsync(a, first);
            var repeated = await app.CameraImaging.RunSceneLinearBatchAsync(b, second);
            Assert.Equal(generated.Manifest.Files.Select(f => f.Sha256), repeated.Manifest.Files.Select(f => f.Sha256));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var opened = await app.CameraImaging.OpenSceneLinearBatchAsync(first);
            Assert.Equal(a.LensSha256, opened.Manifest.Plan.LensSha256);
            var image0 = await app.CameraImaging.ReadSceneLinearTaskAsync(opened, 0);
            var image1 = await app.CameraImaging.ReadSceneLinearTaskAsync(opened, 1);
            var image2 = await app.CameraImaging.ReadSceneLinearTaskAsync(opened, 2);
            for (var i = 0; i < 32 * 40; i++)
            {
                Assert.Equal(image1.PowerWatts[i] * 2, image0.PowerWatts[i]); Assert.Equal(0, image2.PowerWatts[i]);
                Assert.Equal(image0.PowerWatts[i] / opened.Manifest.CollectionAreaSquareMeters, image0.IrradianceWattsPerSquareMeter[i]);
            }
            Assert.Equal(a.Work.Rays, opened.Manifest.CompletedRays); Assert.Equal(0, opened.Manifest.Png.ClippedPixels);
            using var zip = ZipFile.OpenRead(first); var imageBytes = Read(zip, opened.Manifest.Outputs[0].PngPath);
            Assert.DoesNotContain("gAMA", System.Text.Encoding.ASCII.GetString(imageBytes));
        }
        finally { CultureInfo.CurrentCulture = oldCulture; File.Delete(fixture); File.Delete(first); File.Delete(second); }
    }

    [Fact]
    public async Task ArticleShapeTasksKeepAllNodesAndCancelOrEncodingFailurePreservesTheExistingDestination()
    {
        using var app = WorkbenchApplication.Create(); var fixture = await LoadFixture(app);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip");
        try
        {
            var plan = app.CameraImaging.PrepareSceneLinearBatch(SmallRequest() with { Mode = SceneLinearBatchMode.ArticleSourceShapes });
            Assert.Equal(3, plan.Tasks.Count); Assert.All(plan.Tasks, t => { Assert.Equal(8, t.Nodes.Count); Assert.Equal(2.5, t.ShapeAreaNanometers); });
            var generated = await app.CameraImaging.RunSceneLinearBatchAsync(plan, path);
            var reopened = await app.CameraImaging.OpenSceneLinearBatchAsync(path);
            Assert.Equal(24, reopened.Manifest.Plan.Work.SpectralNodes); Assert.Equal(30 * 32 * 40, reopened.Manifest.Plan.Float64Values);
            Assert.Equal(30, reopened.Manifest.Files.Count(f => f.Path.EndsWith(".f64le", StringComparison.Ordinal)));
            var retained = await File.ReadAllBytesAsync(path);
            using var cts = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.RunSceneLinearBatchAsync(plan, path,
                new Callback<CameraSceneLinearBatchProgress>(p => { if (p.CompletedRays > 0) cts.Cancel(); }), cts.Token));
            Assert.Equal(retained, await File.ReadAllBytesAsync(path));
            var clips = app.CameraImaging.PrepareSceneLinearBatch(SmallRequest() with { PngMaximumIrradiance = 1e-30 });
            await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.RunSceneLinearBatchAsync(clips, path));
            Assert.Equal(retained, await File.ReadAllBytesAsync(path));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var descriptor = generated.Manifest.Files[0]; var entry = zip.GetEntry(descriptor.Path)!; var bytes = Read(zip, descriptor.Path); entry.Delete(); bytes[0] ^= 8;
                using var writer = zip.CreateEntry(descriptor.Path).Open(); writer.Write(bytes);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSceneLinearBatchAsync(path));
        }
        finally { File.Delete(fixture); File.Delete(path); }
    }

    private static CameraSceneLinearBatchRequest SmallRequest() => new(new(4, 4, 2, 100, CenterYMillimeters: 1), Width: 32, Height: 40,
        PixelPitchMicrometers: 10, RowsPerStrip: 7);
    internal static async Task<string> LoadFixture(IWorkbenchApplication app)
    {
        var lens = SceneSlantedEdgeTests.FiniteConjugateLens(); lens.Wavelengths.Clear();
        lens.Wavelengths.Add(new() { Label = "F", Nanometers = 486.1327, Weight = 2, IsPrimary = false });
        lens.Wavelengths.Add(new() { Label = "d", Nanometers = 587.5618, Weight = 1, IsPrimary = true });
        lens.Wavelengths.Add(new() { Label = "C", Nanometers = 656.2725, Weight = 0, IsPrimary = false });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".optic.json"); await OpticJsonStore.SaveAsync(lens, path); await app.Documents.OpenAsync(path); return path;
    }
    private static byte[] Read(ZipArchive zip, string path) { using var stream = zip.GetEntry(path)!.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
    private sealed class Callback<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
