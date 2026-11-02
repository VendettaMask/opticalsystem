using System.IO.Compression;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.NonSequential;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class SceneSlantedEdgeTests
{
    [Theory]
    [InlineData(14, 14, 0, 0, 5)] [InlineData(5, 3, 17, -11, 23)]
    public void EveryEmissiveCellMatchesSeparateCoplanarFacesIncludingOddCountsAndPose(int columns, int rows, double rx, double ry, double rz)
    {
        var settings = new EmissiveCheckerboardSettings(columns, rows, 2, 100, 1, -2, rx, ry, rz);
        var bands = new[] { new EmissiveCheckerboardBand(486, 2, .125, .6, .02), new EmissiveCheckerboardBand(656, 7, 0, .8, .04) };
        var chart = EmissiveCheckerboard.Create(settings, bands, 5);
        var pose = new CoordinateSystem(new(1, -2, -95), rx, ry, rz);
        for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++)
        {
            // Independent finite faces, each uniformly emissive, provide the partition authority.
            var local = new Vector3D((x + .5 - columns / 2d) * 2, (y + .5 - rows / 2d) * 2, 0);
            var point = pose.ToGlobalPoint(local); var normal = pose.ToGlobalDirection(new(0, 0, 1));
            var surface = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
            { LocalCoordinateSystem = new(point, rx, ry, rz), Parameters = new PlaneRectangleParameters(2, 2) };
            var dark = (x + y) % 2 != 0;
            var separate = new DirectCameraScene(new("one face", bands.Select((b, i) => new NonSequentialWavelength($"Band {i + 1}", b.WavelengthNanometers, 1, i == 0)).ToArray(), [surface]),
                [new(surface.Id, bands.Select(b => dark ? b.DarkReflectance : b.BrightReflectance).ToArray(), bands.Select(b => dark ? b.DarkRadiance : b.BrightRadiance).ToArray())], []);
            var ray = new OpticalSystemDesign.Core.Rays.RealRay(point + normal * 10, -normal, 486);
            var hit = chart.Intersect(ray)!; var independentHit = separate.Intersect(ray)!;
            for (var b = 0; b < 2; b++) Assert.Equal(separate.Evaluate(independentHit, normal, b), chart.Evaluate(hit, normal, b));
        }
        Assert.Single(chart.ObjectIds); // Includes the 196-cell case without dropping faces at the 64-object limit.
        var outside = pose.ToGlobalPoint(new((columns / 2d + .01) * 2, 0, 0));
        Assert.Null(chart.Intersect(new(outside + pose.ToGlobalDirection(new(0, 0, 10)), pose.ToGlobalDirection(new(0, 0, -1)), 486)));
        // A single projected boundary has both physical one-sided limits.
        var boundary = -columns + 2d;
        foreach (var offset in new[] { -1e-7, 1e-7 })
        {
            var point = pose.ToGlobalPoint(new(boundary + offset, -rows + 1, 0)); var normal = pose.ToGlobalDirection(new(0, 0, 1));
            var hit = chart.Intersect(new(point + normal * 10, -normal, 486))!;
            Assert.Equal(offset < 0 ? 2 : .125, chart.Evaluate(hit, normal, 0).RadianceWattsPerSquareMeterSteradian);
        }
    }

    [Fact]
    public void OldReflectedCheckerKeepsUniformEmissionAndNewEmissionArraysAreFrozen()
    {
        var surface = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -10)), Parameters = new PlaneRectangleParameters(10, 10) };
        var geometry = new NonSequentialDocument("legacy checker", [new("green", 550, 1, true)], [surface]);
        var emission = new double[] { 2 }; var dark = new double[] { .5 };
        var old = new DirectCameraScene(geometry, [new(surface.Id, [.8], emission, [.1], 1)], []);
        var current = new DirectCameraScene(geometry, [new(surface.Id, [.8], emission, [.1], 1, DarkEmittedRadiance: dark)], []);
        emission[0] = 100; dark[0] = 100;
        var hit = old.Intersect(new(new(-.5, .5, 0), new(0, 0, -1), 550))!;
        Assert.Equal(2, old.Evaluate(hit, new(0, 0, 1), 0).RadianceWattsPerSquareMeterSteradian);
        Assert.Equal(.5, current.Evaluate(hit, new(0, 0, 1), 0).RadianceWattsPerSquareMeterSteradian);
        Assert.Equal(.1, current.Evaluate(hit, new(0, 0, 1), 0).Reflectance);
        Assert.Equal(0, current.Evaluate(hit, new(0, 0, -1), 0).RadianceWattsPerSquareMeterSteradian);
    }

    [Fact]
    public void PhysicalAreaAndRowOrderConvertOnceAndKeepAbsoluteBandRatios()
    {
        var power = new SpectralDetectorPower(4, 4, 2, 4, DetectorRowOrder.NegativeLocalYFirst,
            [new(486, Enumerable.Repeat(1e-14, 16).ToArray()), new(656, Enumerable.Repeat(3e-14, 16).ToArray())], .25);
        var image = new LinearDetectorImage(power);
        Assert.InRange(Math.Abs(2e-12 - image.CollectionAreaSquareMeters), 0, 1e-26);
        Assert.Equal(.005, image.Bands[0].WattsPerSquareMeter[0], 15);
        Assert.Equal(3, image.Bands[1].WattsPerSquareMeter[0] / image.Bands[0].WattsPerSquareMeter[0], 14);
        Assert.Equal((-.003, -.006), image.PixelCenter(0, 0));
        Assert.Equal((.003, .006), image.PixelCenter(3, 3));
    }

    [Fact]
    public void ImageOnlyMeasurementIsScaleInvariantAndHorizontalTransposePreservesFrequency()
    {
        const int n = 128; var pixels = new double[n * n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) pixels[y * n + x] = .1 + .8 / (1 + Math.Exp(-(x - 63.2 - .087 * (y - 63.5)) / 1.1));
        var roi = new SfrRegion(8, 8, 112, 112);
        var basis = LinearImageSfr.Measure(pixels, n, n, roi, 3);
        var tiny = LinearImageSfr.Measure(pixels.Select(v => v * 1e-15).ToArray(), n, n, roi, 3);
        var transposed = Enumerable.Range(0, n * n).Select(i => pixels[(i % n) * n + i / n]).ToArray();
        var horizontal = LinearImageSfr.Measure(transposed, n, n, roi, 3, LinearEdgeOrientation.NearHorizontal);
        Assert.Equal(basis.Measurement.Curve, horizontal.Measurement.Curve);
        Assert.InRange(basis.Measurement.Curve.Zip(tiny.Measurement.Curve).Max(p => Math.Abs(p.First.Response - p.Second.Response)), 0, 2e-13);
        Assert.InRange(Math.Abs(basis.Measurement.Contrast * 1e-15 - tiny.Measurement.Contrast), 0, 1e-27);
        Assert.Equal(basis.Measurement.EdgeIntercept, horizontal.Measurement.EdgeIntercept, 14);
    }

    internal static Optic FiniteConjugateLens()
    {
        var lens = new Optic(); lens.Fields.Add(new()); lens.Wavelengths.Add(new() { Nanometers = 550, IsPrimary = true });
        lens.SurfaceGroup.Replace([new OpticalSurface { Thickness = 100 },
            new OpticalSurface { IsStop = true, SemiDiameter = 2, Thickness = 100, InteractionModel = new ThinLensInteractionModel(50) }, new OpticalSurface()]);
        return lens;
    }

    [Fact]
    public void ActualObjectChartInvertsAtKnownConjugateAndRadianceScalesWithoutChangingPrescription()
    {
        var lens = FiniteConjugateLens(); var before = lens.ToSnapshot();
        var settings = new EmissiveCheckerboardSettings(2, 2, 2, 100, CenterYMillimeters: 1, RotationZDegrees: 0);
        var chart = EmissiveCheckerboard.Create(settings, [new(550, 1, .1)], lens.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z);
        var doubled = EmissiveCheckerboard.Create(settings, [new(550, 2, .2)], lens.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z);
        var image = DirectCameraRenderer.Render(lens, chart, new(16, 16, 10, new(2), 4, 16));
        var twice = DirectCameraRenderer.Render(lens, doubled, new(16, 16, 10, new(2), 4, 16));
        for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
        {
            var value = image.DetectorPower.Bands[0].PixelBandPowerWatts[y * 16 + x];
            Assert.InRange(Math.Abs(2 * value - twice.DetectorPower.Bands[0].PixelBandPowerWatts[y * 16 + x]), 0, 1e-23);
            Assert.Equal(x >= 8, value > image.DetectorPower.Bands[0].PixelBandPowerWatts[y * 16] * 5);
        }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(lens.ToSnapshot()));
    }

    [Fact]
    public async Task CurrentLensPrimaryBandLinearFrameSaveAndFrozenReplayAreIndependentOfLiveDocument()
    {
        using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
        var before = app.Documents.GetSnapshot();
        var request = new CameraSceneSfrRequest(new(CellSizeMillimeters: 200, CenterYMillimeters: 100), [new(550, 2, .2)],
            Width: 64, Height: 64, PixelSamplesPerAxis: 2, CaptureLensPrimaryWavelength: true,
            Edge: new(new(0, 0, 64, 64), HalfWindowPixels: 16));
        var result = await app.CameraImaging.SimulateSceneSlantedEdgeAsync(request);
        Assert.Equal(587.6, result.Request.Bands![0].WavelengthNanometers, 6);
        Assert.Equal(2, result.Request.Bands[0].BrightRadiance); Assert.False(result.Request.CaptureLensPrimaryWavelength);
        Assert.Equal(before.Revision, app.Documents.GetSnapshot().Revision); Assert.Equal(64 * 64 * 4 * 2 * 8, result.Frame.LaunchedRays);
        Assert.True(result.Frame.ToLinearImage().Power.TotalPowerWatts > 0);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scene-sfr.zip");
        try
        {
            await app.CameraImaging.ExportSceneSlantedEdgeAsync(result, path);
            var reopened = await app.CameraImaging.OpenSceneSlantedEdgeAsync(path);
            await app.Documents.OpenAsync(SlantedEdgeSfrTests.ReferenceLens);
            var replay = await app.CameraImaging.RecomputeSceneSlantedEdgeAsync(reopened);
            Assert.Equal(result.LensSha256, replay.LensSha256); Assert.Equal(result.SourceDocument, replay.SourceDocument);
            Assert.Equal(result.Frame.Bands[0].PixelPowerWatts, replay.Frame.Bands[0].PixelPowerWatts);
            Assert.Equal(result.EdgeFailure, replay.EdgeFailure); Assert.Equal(result.Edge?.Measurement.Curve, replay.Edge?.Measurement.Curve);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("sha256.txt")!; entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("sha256.txt").Open()); writer.Write("bad");
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.OpenSceneSlantedEdgeAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidRoiIsARecordedFailureAndBudgetCancellationAndUnsupportedPoseFailExplicitly()
    {
        using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
        var result = await app.CameraImaging.SimulateSceneSlantedEdgeAsync(new(new(CellSizeMillimeters: 200), Width: 32, Height: 32,
            Edge: new(new(0, 0, 32, 32), HalfWindowPixels: 8)));
        Assert.Null(result.Edge); Assert.NotNull(result.EdgeFailure);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => app.CameraImaging.SimulateSceneSlantedEdgeAsync(new(Width: 1024, Height: 1024)));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SimulateSceneSlantedEdgeAsync(new(Width: 256, Height: 256, PixelSamplesPerAxis: 4, PupilRings: 8, PupilArms: 32)));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateSceneSlantedEdgeAsync(new(new(DistanceMillimeters: 2, RotationXDegrees: 90))));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.CameraImaging.SimulateSceneSlantedEdgeAsync(new(), cts.Token));
        app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.CameraImaging.SimulateSceneSlantedEdgeAsync(new()));
    }
}
