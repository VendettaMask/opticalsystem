using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Materials;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class CameraScene3DTests
{
    [Theory]
    [InlineData(2, 1)] [InlineData(2, .36)] [InlineData(2, .04)]
    [InlineData(4, 1)] [InlineData(4, .36)] [InlineData(4, .04)]
    public void ActualActiveAreaQuadratureMovesNodesAndIntegratesIndependentPolynomial(int n, double fill)
    {
        var nodes = CameraPixelAperture.Quadrature(new(n, fill));
        Assert.Equal(1, nodes.Sum(p => p.Weight), 14);
        Assert.All(nodes, p => Assert.InRange(Math.Abs(p.XOffsetPixels), 0, Math.Sqrt(fill) / 2));
        Assert.Equal(fill / 12, nodes.Sum(p => p.Weight * p.XOffsetPixels * p.XOffsetPixels), 14);
        Assert.Equal(fill * fill / 144, nodes.Sum(p => p.Weight * p.XOffsetPixels * p.XOffsetPixels * p.YOffsetPixels * p.YOffsetPixels), 14);
    }

    [Fact]
    public void TotalQeAndActiveAreaQeDoNotDuplicateFillLossOrCreateUnphysicalGain()
    {
        SpectralDetectorPower Power(double f) => new(4, 4, 3, 3, DetectorRowOrder.PositiveLocalYFirst,
            [new(550, Enumerable.Repeat(1e-14 * f, 16).ToArray())], f);
        var response = new SensorSpectralResponse([new(450, .2, .2, .2), new(650, .2, .2, .2)]);
        var whole = SpectralDetectorSensor.IntegratePhotoElectrons(Power(1), response, new());
        var total = SpectralDetectorSensor.IntegratePhotoElectrons(Power(.25), response, new());
        var active = SpectralDetectorSensor.IntegratePhotoElectrons(Power(.25), response, new(), QuantumEfficiencyAreaReference.CollectionArea);
        for (var i = 0; i < 16; i++) { Assert.Equal(whole[i], total[i], 11); Assert.Equal(whole[i] * .25, active[i], 11); }
        Assert.Throws<ArgumentException>(() => SpectralDetectorSensor.IntegratePhotoElectrons(Power(.1), response, new()));
    }

    private static NonSequentialObjectDefinition Plane(double z, double x = 0, double size = 100) =>
        NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(x, 0, z)), Parameters = new PlaneRectangleParameters(size, size) };
    private static DirectCameraScene Scene(NonSequentialObjectDefinition[] objects, DirectPointLight[] lights, double emission = 0) =>
        new(new("Analytic", [new("green", 550, 1, true)], objects), objects.Select(o => new DirectSceneMaterial(o.Id, [.5], [emission])), lights);

    [Fact]
    public void LambertianRadianceUsesInverseSquareAndSharedGeometryForOpaqueShadows()
    {
        var plane = Plane(-1000); var scene = Scene([plane], [new(new(0, 0, 0), [4 * Math.PI])]);
        var hit = scene.Intersect(new(new(0, 0, 0), new(0, 0, -1), 550))!;
        Assert.Equal(1000, hit.DistanceMillimeters, 12);
        Assert.Null(scene.Intersect(new(new(0, 0, 0), new(0, 0, -1), 550), 999));
        var lit = scene.Evaluate(hit, new(0, 0, 1), 0);
        Assert.Equal(.5 / Math.PI, lit.RadianceWattsPerSquareMeterSteradian, 12);
        var distant = Scene([plane], [new(new(0, 0, 1000), [4 * Math.PI])]);
        Assert.Equal(lit.RadianceWattsPerSquareMeterSteradian / 4, distant.Evaluate(hit, new(0, 0, 1), 0).RadianceWattsPerSquareMeterSteradian, 12);
        var blocked = Scene([plane, Plane(-500, 0, 10)], [new(new(0, 0, 0), [4 * Math.PI])]);
        var shadow = blocked.Evaluate(hit, new(0, 0, 1), 0);
        Assert.Equal(0, shadow.RadianceWattsPerSquareMeterSteradian); Assert.Equal(1, shadow.BlockedLights);
        Assert.Equal(0, scene.Evaluate(hit, new(0, 0, -1), 0).RadianceWattsPerSquareMeterSteradian);
    }

    private static Optic ApertureLens()
    {
        var optic = new Optic(); optic.Fields.Add(new()); optic.Wavelengths.Add(new() { Nanometers = 550 });
        optic.SurfaceGroup.Replace([new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 2, Thickness = 10 }, new OpticalSurface()]);
        return optic;
    }

    [Fact]
    public void AbsoluteImageIrradianceMatchesIndependentDiskSolidAngleAndCancellationIsBounded()
    {
        var lens = ApertureLens(); var scene = Scene([Plane(-100)], [], 2);
        var result = DirectCameraRenderer.Render(lens, scene, new(4, 4, .1, new(4), 8, 32));
        var actual = result.DetectorPower.Bands[0].PixelBandPowerWatts[5] / 1e-14;
        var expected = 2 * Math.PI * 4 / (100 + 4); // L*pi*a²/(d²+a²), on-axis aperture integral.
        Assert.InRange(Math.Abs(actual / expected - 1), 0, 2e-9); // 0.1 um pixels have finite off-axis extent.
        Assert.Equal(result.LaunchedRays, result.PassedLensRays); Assert.Equal(result.PassedLensRays, result.SceneHitRays);
        Assert.Throws<ArgumentException>(() => DirectCameraRenderer.Render(lens, scene, new(256, 256, 3, new(4), 8, 32)));
        using var cts = new CancellationTokenSource(); cts.Cancel(); using var scope = ComputationCancellation.Push(cts.Token);
        Assert.Throws<OperationCanceledException>(() => DirectCameraRenderer.Render(lens, scene, new(4, 4, 3)));
    }

    [Fact]
    public void ReversedThinLensFormsAnInvertedImageFromTrueSceneDepth()
    {
        var lens = ApertureLens(); lens.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        lens.SurfaceGroup.Items[1].Thickness = 100; lens.SurfaceGroup.Renumber();
        var scene = Scene([Plane(-100, -2, 4)], [], 1);
        var image = DirectCameraRenderer.Render(lens, scene, new(4, 4, 100, new(2), 4, 16));
        // s=s'=100 mm, f=50 mm: every pupil ray maps x_sensor to -x_object.
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
            Assert.Equal(x >= 2, image.DetectorPower.Bands[0].PixelBandPowerWatts[y * 4 + x] > 0);
    }

    [Fact]
    public void NonuniformIlluminationChangesItsSpatialIntegralWhenFillChanges()
    {
        var scene = Scene([Plane(-10)], [new(new(0, 0, -9), [1e-5])]); var lens = ApertureLens();
        double Average(double fill) => DirectCameraRenderer.Render(lens, scene, new(4, 4, 100, new(2, fill), 8, 32))
            .DetectorPower.Bands[0].PixelBandPowerWatts[5] / fill;
        var full = Average(1); var small = Average(.04);
        Assert.True(Math.Abs(full / small - 1) > 1e-7, $"Spatial integration remained degenerate: {full:G17}, {small:G17}");
    }

    [Fact]
    public void BareParallelGlassInterfacesUseIndependentNormalIncidenceFresnelPower()
    {
        var optic = new Optic(); optic.Materials.Register(new ConstantIndexMaterial("Control glass", 1.5));
        optic.Fields.Add(new()); optic.Wavelengths.Add(new() { Nanometers = 550 });
        optic.SurfaceGroup.ImportLegacySurfaces([new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Material = "Control glass", Thickness = 1, SemiDiameter = 2, IsStop = true },
            new OpticalSurface { Thickness = 1000, SemiDiameter = 2 }, new OpticalSurface()], optic.Materials);
        var scene = Scene([Plane(-100)], [], 1);
        var withLoss = DirectCameraRenderer.Render(optic, scene, new(4, 4, .1, PupilRings: 4, PupilArms: 16));
        var without = DirectCameraRenderer.Render(optic, scene, new(4, 4, .1, PupilRings: 4, PupilArms: 16, BareInterfaceFresnel: false));
        var transmission = withLoss.DetectorPower.Bands[0].PixelBandPowerWatts[5] / without.DetectorPower.Bands[0].PixelBandPowerWatts[5];
        var reflectance = Math.Pow((1.5 - 1) / (1.5 + 1), 2);
        Assert.InRange(Math.Abs(transmission - Math.Pow(1 - reflectance, 2)), 0, 1e-9);
    }

    internal static CameraImagingRequest Request(ICameraImagingService service, int width = 8, int height = 8) =>
        new(Source: CameraSource.Scene3D, Width: width, Height: height, Aberration: CameraAberration.Geometric,
            Sensor: new(NoiseEnabled: false), SpectralModel: CameraSpectralModel.SceneBands,
            PixelSampling: new(2), Scene3D: service.CreateExampleScene3D());

    [Fact]
    public async Task ActualLensSceneToRawIspExportsAndReplaysAfterWorkspaceReplacement()
    {
        using var app = WorkbenchApplication.Create("cooke"); var before = app.Documents.GetSnapshot();
        var run = await app.CameraImaging.SimulateAsync(Request(app.CameraImaging));
        Assert.Equal(before, app.Documents.GetSnapshot()); Assert.True(run.MeanPhotoElectrons > 0,
            $"Launched {run.Scene3D?.LaunchedRays}, passed {run.Scene3D?.PassedLensRays}, hits {run.Scene3D?.SceneHitRays}, shadow {run.Scene3D?.ShadowedLightSamples}/{run.Scene3D?.ShadowQueries}");
        Assert.NotNull(run.Scene3D); Assert.True(run.Scene3D.TotalPowerWatts > 0); Assert.Equal(0, run.OpticalFallbackCount);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            var b = await app.CameraImaging.RecomputeAsync(run, run.Request with { Sensor = run.Request.Sensor! with { ExposureMilliseconds = 20 } });
            for (var i = 0; i < run.PhotoElectrons.Count; i++) Assert.Equal(run.PhotoElectrons[i] * 2, b.PhotoElectrons[i], 9);
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, run), path);
            var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidence)) { Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "scene3d-frozen-AB.camera-experiment.zip"), true); }
            app.Documents.NewBlank(); app.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var opened = await app.CameraImaging.OpenExperimentAsync(path);
            var replay = await app.CameraImaging.RecomputeAsync(opened.B);
            Assert.Equal(b.RawDn, replay.RawDn); Assert.Equal(b.PhotoElectrons, replay.PhotoElectrons);
            Assert.Equal(b.Developed.Rgb24, replay.Developed.Rgb24);
            Assert.Equal(b.Scene3D!.TotalPowerWatts, replay.Scene3D!.TotalPowerWatts);
            await app.CameraImaging.ExportAsync(replay, path);
            using var zip = ZipFile.OpenRead(path); Assert.NotNull(zip.GetEntry("B/detector-power.csv")); Assert.NotNull(zip.GetEntry("B/scene3d.json"));
            CameraContinuousSpectrumTests.Record("scene3d-actual-lens", new { run.Scene3D, run.MeanPhotoElectrons, ReplayedRaw = true, CommercialCertification = false });
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(128, 96, 2)] [InlineData(64, 48, 4)]
    public async Task OriginalCameraSizeAndFourByFourAreaSamplingStayWithinTheDeclaredRayBudget(int width, int height, int sampling)
    {
        using var app = WorkbenchApplication.Create("cooke"); var watch = System.Diagnostics.Stopwatch.StartNew();
        var run = await app.CameraImaging.SimulateAsync(Request(app.CameraImaging, width, height) with { PixelSampling = new(sampling) });
        Assert.True(run.MeanPhotoElectrons > 0); Assert.Equal(width * height, run.RawDn.Count);
        Assert.Equal((long)width * height * sampling * sampling * 3 * 16, run.Scene3D!.LaunchedRays);
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence) && width == 128)
        {
            Directory.CreateDirectory(evidence);
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run), Path.Combine(evidence, "scene3d-128.camera-experiment.zip"));
            await app.CameraImaging.ExportAsync(run, Path.Combine(evidence, "scene3d-128-export.zip"));
        }
        CameraContinuousSpectrumTests.Record($"scene3d-size-{width}-{height}-{sampling}", new { width, height, sampling,
            run.Scene3D.LaunchedRays, run.Scene3D.TotalPowerWatts, run.MeanPhotoElectrons, ElapsedSeconds = watch.Elapsed.TotalSeconds });
    }

    [Fact]
    public async Task PowerLinearitySceneImmutabilityImportFailureAndNewModeGuardsAreExplicit()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var request = Request(app.CameraImaging); var run = await app.CameraImaging.SimulateAsync(request);
        var doubleLight = request.Scene3D! with { Lights = request.Scene3D!.Lights.Select(l => l with
            { BandPowerWatts = l.BandPowerWatts.Select(v => v * 2).ToArray() }).ToArray() };
        var bright = await app.CameraImaging.SimulateAsync(request with { Scene3D = doubleLight });
        for (var i = 0; i < run.PhotoElectrons.Count; i++) Assert.Equal(run.PhotoElectrons[i] * 2, bright.PhotoElectrons[i], 9);
        var dark = await app.CameraImaging.SimulateAsync(request with { Scene3D = request.Scene3D! with { Lights = [] } });
        Assert.All(dark.PhotoElectrons, v => Assert.Equal(0, v));
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.RecomputeAsync(run with
        { Request = run.Request with { Aberration = CameraAberration.Diffraction }, Inputs = run.Inputs! with { EngineVersion = CameraExperiment.DirectSceneEngineVersion } }));
        await Assert.ThrowsAsync<ArgumentException>(() => app.CameraImaging.SimulateAsync(new(Width: 8, Height: 8, Optics: CameraOptics.Ideal, PixelSampling: new(2, .25))));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await app.CameraImaging.SaveScene3DAsync(request.Scene3D!, path);
            var scene = await app.CameraImaging.ImportScene3DAsync(path); Assert.NotNull(scene.Origin);
            var imported = await app.CameraImaging.SimulateAsync(request with { Scene3D = scene });
            Assert.Equal(run.RawDn, imported.RawDn); Assert.Equal(run.Scene3D!.TotalPowerWatts, imported.Scene3D!.TotalPowerWatts);
            var bundled = await app.CameraImaging.ImportScene3DAsync(Path.Combine(CameraSensorFileTests.Examples, "direct-light.camera-scene3d.json"));
            var example = await app.CameraImaging.SimulateAsync(request with { Scene3D = bundled }); Assert.Equal(run.RawDn, example.RawDn);
            await File.WriteAllTextAsync(path, "{}"); await Assert.ThrowsAnyAsync<Exception>(() => app.CameraImaging.ImportScene3DAsync(path));
            Assert.Equal(300, run.Request.Scene3D!.Lights[0].BandPowerWatts[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FrozenHistoricalFilesWithoutNewOptionalPropertiesRetainTheirCalculationVersion()
    {
        using var app = WorkbenchApplication.Create();
        var run = await app.CameraImaging.SimulateAsync(new(Width: 4, Height: 4, Optics: CameraOptics.Ideal));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            foreach (var version in new[] { CameraExperiment.LegacyEngineVersion, CameraExperiment.PixelAreaEngineVersion })
            {
                var old = run with { Inputs = run.Inputs! with { EngineVersion = version } };
                await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, old), path);
                byte[] bytes;
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
                {
                    JsonNode node;
                    using (var reader = new StreamReader(zip.GetEntry("experiment.json")!.Open())) node = JsonNode.Parse(reader.ReadToEnd())!;
                    var r = node["B"]!["Request"]!.AsObject(); r.Remove("Scene3D"); r.Remove("Render3D");
                    if (version == CameraExperiment.LegacyEngineVersion) r.Remove("PixelSampling");
                    bytes = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
                    zip.GetEntry("experiment.json")!.Delete(); using (var s = zip.CreateEntry("experiment.json").Open()) s.Write(bytes);
                    zip.GetEntry("sha256.txt")!.Delete(); using var h = new StreamWriter(zip.CreateEntry("sha256.txt").Open()); h.Write(Convert.ToHexStringLower(SHA256.HashData(bytes)));
                }
                var opened = await app.CameraImaging.OpenExperimentAsync(path); var replay = await app.CameraImaging.RecomputeAsync(opened.B);
                Assert.Equal(version, replay.Inputs!.EngineVersion); Assert.Equal(run.RawDn, replay.RawDn);
            }
        }
        finally { File.Delete(path); }
    }
}
