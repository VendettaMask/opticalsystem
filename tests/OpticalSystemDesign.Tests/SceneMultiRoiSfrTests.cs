using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.Tests;

public sealed class SceneMultiRoiSfrTests
{
    [Theory]
    [InlineData(.087, false)] [InlineData(-.123, false)] [InlineData(.087, true)] [InlineData(-.123, true)]
    public void NonSquareOffsetTransposeAndPolarityPreserveProfilesAndDetectorGeometry(double slope, bool reverse)
    {
        const int width = 192, height = 144; var pixels = new double[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            pixels[y * width + x] = 2e-14 * (.1 + .8 / (1 + Math.Exp((reverse ? -1 : 1) * -(x - 87.2 - slope * (y - 70)) / 1.1)));
        var region = new SfrRegion(30, 12, 120, 112); var roi = new LinearSfrRoi("V", region);
        var v = LinearImageRoiSfr.Measure(pixels, width, height, 2.5, DetectorRowOrder.PositiveLocalYFirst, [roi]).Single();
        Assert.Equal(LinearSfrRoiStatus.Measured, v.Status); Assert.Null(v.Failure); Assert.NotNull(v.Geometry);
        var transposed = Enumerable.Range(0, pixels.Length).Select(i => pixels[(i % height) * width + i / height]).ToArray();
        var hRoi = new LinearSfrRoi("H", new(region.Y, region.X, region.Height, region.Width), LinearEdgeOrientation.NearHorizontal);
        var h = LinearImageRoiSfr.Measure(transposed, height, width, 2.5, DetectorRowOrder.PositiveLocalYFirst, [hRoi]).Single();
        Assert.Equal(LinearSfrRoiStatus.Measured, h.Status);
        Assert.Equal(JsonSerializer.Serialize(v.Edge!.Measurement), JsonSerializer.Serialize(h.Edge!.Measurement));
        Assert.Equal(v.Geometry!.Column, h.Geometry!.Row); Assert.Equal(v.Geometry.Row, h.Geometry.Column);
        Assert.Equal(v.Geometry.XMillimeters, -h.Geometry.YMillimeters, 14);
        Assert.Equal(v.Geometry.YMillimeters, -h.Geometry.XMillimeters, 14);
        Assert.Equal(v.Geometry.NormalX, -h.Geometry.NormalY); Assert.Equal(v.Geometry.NormalY, -h.Geometry.NormalX);
        Assert.Equal(v.Geometry.ProjectedSamples, h.Geometry.ProjectedSamples); Assert.InRange(v.Geometry.MinimumBinSamples, 1, 112);
        var negativeRows = LinearImageRoiSfr.Measure(pixels, width, height, 2.5, DetectorRowOrder.NegativeLocalYFirst, [roi]).Single();
        Assert.Equal(v.Geometry.YMillimeters, -negativeRows.Geometry!.YMillimeters);
        Assert.Equal(v.Geometry.NormalY, -negativeRows.Geometry.NormalY);
        var full = v.Edge.Measurement;
        Assert.All(full.Curve, p => Assert.Equal(p.CyclesPerPixel * 400, p.CyclesPerMillimeter));
        Assert.InRange(full.Contrast, 1e-14, 2e-14); Assert.InRange(full.Mtf50CyclesPerPixel!.Value, .07, .15);
        // Independent logistic LSF transform: a/sinh(a), a=2*pi^2*scale*f along the fitted normal.
        foreach (var point in full.Curve.Where(p => p.CyclesPerPixel is > 0 and <= .3))
        {
            var a = 2 * Math.PI * Math.PI * 1.1 / Math.Sqrt(1 + slope * slope) * point.CyclesPerPixel;
            Assert.InRange(Math.Abs(point.Response - a / Math.Sinh(a)), 0, .006);
        }
    }

    [Fact]
    public void BadRoisStayIndependentAndNeverGetFabricatedCurves()
    {
        const int n = 160; var pixels = new double[n * n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
            pixels[y * n + x] = x < 16 ? 0 : y < 130 ? .1 + .8 / (1 + Math.Exp(-(x - 78 - .087 * (y - 60)) / 1.2)) : .2;
        LinearSfrRoi[] rois = [new("good", new(24, 8, 112, 112)), new("outside", new(140, 140, 48, 48)),
            new("small", new(0, 0, 16, 48)), new("flat", new(0, 130, 100, 30)),
            new("wrong direction", new(24, 8, 112, 112), LinearEdgeOrientation.NearHorizontal), new("no light", new(0, 0, 32, 32))];
        // A separate zero plane gives a valid-sized no-light ROI.
        var progress = new List<int>(); var result = LinearImageRoiSfr.Measure(pixels, n, n, 3, DetectorRowOrder.PositiveLocalYFirst, rois, progress.Add);
        Assert.Equal(LinearSfrRoiStatus.Measured, result[0].Status);
        Assert.All(result.Skip(1).Take(4), r => { Assert.NotEqual(LinearSfrRoiStatus.Measured, r.Status); Assert.Null(r.Edge); Assert.Null(r.Geometry); Assert.NotEmpty(r.Failure!); });
        Assert.Equal(Enumerable.Range(1, rois.Length), progress);
        var zero = LinearImageRoiSfr.Measure(new double[n * n], n, n, 3, DetectorRowOrder.PositiveLocalYFirst, [new("zero", new(0, 0, 64, 64))]).Single();
        Assert.Equal(LinearSfrRoiStatus.NoLight, zero.Status); Assert.Null(zero.Edge);
        var flat = LinearImageRoiSfr.Measure(Enumerable.Repeat(.2, n * n).ToArray(), n, n, 3, DetectorRowOrder.PositiveLocalYFirst, [new("flat", new(0, 0, 64, 64))]).Single();
        Assert.Equal(LinearSfrRoiStatus.EdgeQualityFailed, flat.Status);
        var corner = new double[n * n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) corner[y * n + x] = ((x > 80 + .087 * y) ^ (y > 80 - .087 * x)) ? 1 : .1;
        Assert.Equal(LinearSfrRoiStatus.EdgeQualityFailed, LinearImageRoiSfr.Measure(corner, n, n, 3,
            DetectorRowOrder.PositiveLocalYFirst, [new("crossing", new(16, 16, 128, 128))]).Single().Status);
    }

    [Fact]
    public void BoundsUniqueNamesAndCancellationRemainExplicit()
    {
        var roi = new LinearSfrRoi("one", new(0, 0, 64, 64));
        Assert.Throws<ArgumentException>(() => LinearImageRoiSfr.ValidateRegions([roi, roi]));
        Assert.Throws<ArgumentException>(() => LinearImageRoiSfr.ValidateRegions(Enumerable.Range(0, 33).Select(i => roi with { Id = i.ToString() }).ToArray()));
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => LinearImageRoiSfr.Measure(new double[64 * 64], 64, 64, 3, DetectorRowOrder.PositiveLocalYFirst, [roi]));
    }

    private static double PixelResponse(double cycles) => Math.Abs(cycles) < 1e-12
        ? 1 : Math.Sin(Math.PI * cycles) / (Math.PI * cycles);

    internal static CameraSceneLinearBatchRequest CrossRequest() => new(new(2, 2, 2, 100), Width: 192, Height: 192,
        PixelPitchMicrometers: 10, PixelSamplesPerAxis: 2, RowsPerStrip: 17);
    internal static LinearSfrRoi[] CrossRois() => [new("V top", new(48, 0, 96, 80)),
        new("H left", new(0, 48, 80, 96), LinearEdgeOrientation.NearHorizontal), new("crossing", new(48, 48, 96, 96)),
        new("outside", new(160, 160, 64, 64))];

    [Fact]
    public void DistantOffAxisReferenceMapsBeyondTheNearFieldStepLimit()
    {
        var optic = OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(DoubleGaussSfrTests.StandardLens), ".zmx");
        var tilt = 5 * Math.PI / 180;
        var detectorEdge = new LinearSfrEdgeGeometry(255.1473, 423.5, -.0010580379060533289, -.504,
            Math.Cos(tilt), -Math.Sin(tilt), -5, 1, 1);
        var reference = SceneEdgeMtf.EvaluateSceneEdge(optic, new(14, 14, 2000, 100_000),
            [new(587.6, 1, 1, .1)], detectorEdge, 3, 1, pupilAxisSamples: 17);

        Assert.Equal(100_000, reference.Field.ObjectDistanceMillimeters);
        Assert.InRange(reference.Field.ObjectXMillimeters, 1.06, 1.07);
        Assert.InRange(reference.Field.ObjectYMillimeters, 507, 508);
        Assert.Equal(132.988419345, reference.Field.ImageDistanceFromFirstSurfaceMillimeters, 8);
        Assert.True(reference.AcceptedRays > 0);
    }

    [Fact]
    public async Task RealFullFrameMultiTaskMeasurementRoundTripsWithoutRetracingAndRejectsWrongSourceOrEditedCurves()
    {
        using var app = WorkbenchApplication.Create(); var fixture = await SceneLinearBatchTests.LoadFixture(app);
        var batchPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-linear.zip"); var reportPath = batchPath + ".scene-sfr-rois.json";
        try
        {
            var artifact = await app.CameraImaging.RunSceneLinearBatchAsync(app.CameraImaging.PrepareSceneLinearBatch(CrossRequest()), batchPath);
            var request = new CameraSceneRoiSfrRequest([0, 1, 2], CrossRois());
            var report = await app.CameraImaging.MeasureSceneLinearRoisAsync(artifact, request);
            Assert.Equal(3, report.Tasks.Count); Assert.Equal(12, report.Tasks.Sum(t => t.Regions.Count));
            foreach (var task in report.Tasks.Take(2))
            {
                Assert.All(task.Regions.Take(2), r => Assert.Equal(LinearSfrRoiStatus.Measured, r.Status));
                Assert.Equal(LinearSfrRoiStatus.EdgeQualityFailed, task.Regions[2].Status);
                Assert.Equal(LinearSfrRoiStatus.InvalidRegion, task.Regions[3].Status);
            }
            Assert.All(report.Tasks[2].Regions.Take(3), r => Assert.Equal(LinearSfrRoiStatus.NoLight, r.Status));
            var a = report.Tasks[0].Regions[0].Edge!.Measurement; var b = report.Tasks[1].Regions[0].Edge!.Measurement;
            Assert.Equal(a.Curve, b.Curve); Assert.Equal(a.Contrast, b.Contrast * 2, 12);
            await app.CameraImaging.SaveSceneLinearRoisAsync(report, reportPath);
            var sourceBytes = await File.ReadAllBytesAsync(batchPath);
            await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SaveSceneLinearRoisAsync(report, batchPath));
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(batchPath));
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens); var revision = app.Documents.GetSnapshot().Revision;
            var replay = await app.CameraImaging.OpenSceneLinearRoisAsync(artifact, reportPath);
            Assert.Equal(JsonSerializer.Serialize(report), JsonSerializer.Serialize(replay)); Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
            var comparison = await app.CameraImaging.CompareSceneLinearMtfAsync(artifact, request, 17);
            Assert.Equal(report.SourceManifestSha256, comparison.SourceManifestSha256);
            Assert.Equal(report.LensSha256, comparison.LensSha256);
            Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
            foreach (var task in comparison.Tasks.Take(2))
            {
                foreach (var region in task.Regions.Take(2))
                {
                    Assert.Null(region.ReferenceFailure);
                    var reference = Assert.IsType<SceneEdgeMtfReference>(region.Reference);
                    var geometry = region.Measurement.Geometry!;
                    Assert.Equal(100, reference.Field.ObjectDistanceMillimeters, 8);
                    Assert.Equal(100, reference.Field.ImageDistanceFromFirstSurfaceMillimeters, 8);
                    Assert.Equal(-geometry.XMillimeters, reference.Field.ObjectXMillimeters, 8);
                    Assert.Equal(-geometry.YMillimeters, reference.Field.ObjectYMillimeters, 8);
                    Assert.All(reference.Optical, point => Assert.Equal(1, point.Response, 10));
                    var expected = PixelResponse(.5 * geometry.NormalX) * PixelResponse(.5 * geometry.NormalY);
                    Assert.Equal(expected, reference.WithPixelAperture[^1].Response, 10);
                    Assert.Equal(50, reference.WithPixelAperture[^1].CyclesPerMillimeter, 10);
                }
                Assert.All(task.Regions.Skip(2), region => Assert.Null(region.Reference));
            }
            Assert.All(comparison.Tasks[2].Regions, region => Assert.Null(region.Reference));
            var retained = await File.ReadAllBytesAsync(reportPath); using var cts = new CancellationTokenSource(); cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SaveSceneLinearRoisAsync(report, reportPath, cts.Token));
            Assert.Equal(retained, await File.ReadAllBytesAsync(reportPath));
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSceneLinearRoisAsync(artifact with
                { Manifest = artifact.Manifest with { Png = artifact.Manifest.Png with { CommonMaximumWattsPerSquareMeter = 123 } } }, reportPath));
            // Even a report re-signed through the public writer must match a fresh measurement of its identified pixels.
            var changed = JsonNode.Parse(JsonSerializer.Serialize(report))!;
            changed["Tasks"]![0]!["Regions"]![0]!["Edge"]!["Measurement"]!["Curve"]![1]!["Response"] = .123;
            await app.CameraImaging.SaveSceneLinearRoisAsync(changed.Deserialize<CameraSceneRoiSfrReport>()!, reportPath);
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSceneLinearRoisAsync(artifact, reportPath));
            var malformed = JsonNode.Parse(await File.ReadAllTextAsync(reportPath))!;
            malformed["Report"]!["Request"]!["Regions"] = null;
            await File.WriteAllTextAsync(reportPath, malformed.ToJsonString());
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSceneLinearRoisAsync(artifact, reportPath));
            await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.MeasureSceneLinearRoisAsync(artifact, new([0, 0], CrossRois())));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.MeasureSceneLinearRoisAsync(artifact, request, cancellationToken: cts.Token));
        }
        finally { File.Delete(fixture); File.Delete(batchPath); File.Delete(reportPath); }
    }
}
