using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Services;
using NonSequentialObjectKind = OpticalSystemDesign.Core.NonSequential.NonSequentialObjectKind;
using PlaneRectangleParameters = OpticalSystemDesign.Core.NonSequential.PlaneRectangleParameters;

namespace OpticalSystemDesign.Tests;

public sealed class CameraSamplingTests
{
    [Fact]
    public async Task FrozenV8EveryOriginalBandElectronRawAndIspValueReplaysAndRegionalBaselineMatches()
    {
        using var app = WorkbenchApplication.Create();
        var saved = await app.CameraImaging.OpenExperimentAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "camera-scene-v8-frozen.zip"));
        Assert.Null(saved.SamplingReport);
        foreach (var run in new[] { saved.B, saved.A! })
        {
            var replay = await app.CameraImaging.RecomputeAsync(run);
            Assert.Equal(run.PhotoElectrons, replay.PhotoElectrons); Assert.Equal(run.RawDn, replay.RawDn);
            Assert.Equal(run.ReferenceRawDn, replay.ReferenceRawDn); Assert.Equal(run.Developed.Rgb24, replay.Developed.Rgb24);
            Assert.Equal(run.Inputs!.LensSnapshotJson, replay.Inputs!.LensSnapshotJson);
            for (var b = 0; b < 3; b++) Assert.Equal(run.Scene3D!.Bands[b].PixelPowerWatts, replay.Scene3D!.Bands[b].PixelPowerWatts);
        }
        var report = await app.CameraImaging.CheckSamplingAsync(saved.B, new(1, 3), CameraSamplingAxis.Pixel);
        Assert.Equal(0, report.SavedElectronDifference.MaximumAbsolute);
        Assert.All(report.SavedBandDifferences, d => Assert.Equal(0, d.MaximumAbsolute));
        Assert.Equal(CameraSamplingComparisonStatus.Compared, report.Comparisons[0].Status);
    }

    [Fact]
    public void PreparedFieldIsImmutableAndBitwiseIdenticalAtFixedPhysicalCoordinates()
    {
        var cells = Enumerable.Range(0, 15).Select(i => new ScalarHuygensCell(new((i % 5 - 2) * .1, (i / 5 - 1) * .1, 0),
            new Vector3D(.001, -.002, 1).NormalizeDirectionForTest(), new(.1, 0, 0), new(0, .1, 0), .01, .2 + i * .1, 10000 + i * .000001)).ToArray();
        foreach (var wavelength in new[] { 450d, 550, 650 })
        {
            var prepared = ScalarHuygensIntegral.Prepare(cells, wavelength);
            for (var i = 0; i < 32; i++)
            {
                var point = new Vector3D(i * .0002 - .003, i * .0001, 100);
                Assert.Equal(BitConverter.DoubleToInt64Bits(ScalarHuygensIntegral.IrradianceWattsPerSquareMillimeter(cells, point, wavelength)),
                    BitConverter.DoubleToInt64Bits(prepared.IrradianceWattsPerSquareMillimeter(point)));
            }
        }
        var frozen = ScalarHuygensIntegral.Prepare(cells, 550); var before = frozen.IrradianceWattsPerSquareMillimeter(new(0, 0, 100));
        cells[0] = cells[0] with { Amplitude = 100 }; Assert.Equal(before, frozen.IrradianceWattsPerSquareMillimeter(new(0, 0, 100)));
        Assert.Equal(0, ScalarHuygensIntegral.Prepare([], 550).IrradianceWattsPerSquareMillimeter(new(0, 0, 100)));
    }

    internal static Optic Lens()
    {
        var lens = new Optic(); lens.Fields.Add(new()); lens.Wavelengths.Add(new() { Nanometers = 550 });
        lens.SurfaceGroup.Replace([new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 100, InteractionModel = new ThinLensInteractionModel(50) }, new OpticalSurface()]);
        return lens;
    }

    [Fact]
    public void RegionalRenderRetainsFullSensorCoordinatesInsteadOfRecenteringTheOptics()
    {
        var lens = Lens();
        var plane = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, -100)), Parameters = new PlaneRectangleParameters(.05, .05) };
        var scene = new DirectCameraScene(new("coordinate control", [new("green", 550, 1, true)], [plane]), [new(plane.Id, [.5], [.01])], []);
        var detector = new DirectCameraRenderSettings(10, 12, 3, new(2)); var settings = new SceneDiffractionSettings(8, 1, 3, 8);
        var all = DiffractionCameraRenderer.Render(lens, scene, detector, settings);
        var regional = DiffractionCameraRenderer.RenderRegion(lens, scene, detector, settings, new(1, 3, 4, 4));
        Assert.Equal(10, regional.FullWidth); Assert.Equal(12, regional.FullHeight);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
            Assert.Equal(all.Image.DetectorPower.Bands[0].PixelBandPowerWatts[(y + 3) * 10 + x + 1],
                regional.Result.Image.DetectorPower.Bands[0].PixelBandPowerWatts[y * 4 + x]);
        Assert.Throws<ArgumentException>(() => DiffractionCameraRenderer.RenderRegion(lens, scene, detector, settings, new(8, 8, 4, 4)));
    }

    internal static async Task<CameraImagingResult> Source(ICameraImagingService service, int width = 10, int height = 12,
        CameraBayerPattern pattern = CameraBayerPattern.RGGB, int subdivision = 1)
    {
        var bootstrap = await service.SimulateAsync(new(Width: width, Height: height, Optics: CameraOptics.Ideal));
        var json = JsonSerializer.Serialize(Lens().ToSnapshot(), new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        var scene = new Camera3DScene(Camera3DScene.FormatIdentifier, 1, "独立棋盘求积控制", "Analytical prescribed thin lens, not measured", [
            new("checker", Camera3DShape.Rectangle, 0, 0, -100, 100, 80, 1, [.8, .8, .8],
                DarkReflectance: [.04, .04, .04], CheckerSize: .004)
        ], [new("point", 10, 20, -50, [.5, .5, .5])]);
        var request = new CameraImagingRequest(Source: CameraSource.Scene3D, Width: width, Height: height,
            Aberration: CameraAberration.Diffraction, Sensor: new(NoiseEnabled: false, Pattern: pattern), Isp: new(),
            SpectralModel: CameraSpectralModel.SceneBands, Scene3D: scene, Render3D: new(Diffraction: new(8, subdivision, 3, 8)));
        return await service.RecomputeAsync(bootstrap with { Inputs = bootstrap.Inputs! with { LensSnapshotJson = json } }, request);
    }

    [Theory]
    [InlineData(CameraBayerPattern.RGGB)] [InlineData(CameraBayerPattern.BGGR)]
    [InlineData(CameraBayerPattern.GRBG)] [InlineData(CameraBayerPattern.GBRG)]
    public async Task OddRegionOriginsRetainOriginalCfaAndEverySavedBaselineCharge(CameraBayerPattern pattern)
    {
        using var app = WorkbenchApplication.Create(); var run = await Source(app.CameraImaging, pattern: pattern);
        var report = await app.CameraImaging.CheckSamplingAsync(run, new(1, 3, 4, 4), CameraSamplingAxis.Pixel);
        Assert.All(report.SavedBandDifferences, d => Assert.Equal(0, d.MaximumAbsolute)); Assert.Equal(0, report.SavedElectronDifference.MaximumAbsolute);
        Assert.Equal(CameraSamplingComparisonStatus.Compared, Assert.Single(report.Comparisons).Status);
        for (var y = 0; y < 4; y++) for (var x = 0; x < 4; x++)
        {
            Assert.Equal(run.PhotoElectrons[(y + 3) * run.Request.Width + x + 1], report.Baseline.PhotoElectrons[y * 4 + x]);
            Assert.Equal(run.ReferenceRawDn[(y + 3) * run.Request.Width + x + 1], report.Baseline.NoiselessRawDn[y * 4 + x]);
        }
    }

    [Fact]
    public async Task IndependentAxesAccumulateSaveReplayAndRejectReportOrSourceTampering()
    {
        using var app = WorkbenchApplication.Create(); var run = await Source(app.CameraImaging, 8, 8); var region = new CameraSamplingRegion(2, 2, 4, 4);
        CameraSamplingReport? report = null;
        foreach (var axis in Enum.GetValues<CameraSamplingAxis>()) report = await app.CameraImaging.CheckSamplingAsync(run, region, axis, report);
        Assert.Equal(5, report!.Comparisons.Count);
        Assert.All(report.Comparisons, c => Assert.Equal(CameraSamplingComparisonStatus.Compared, c.Status));
        var scene = report.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Scene);
        Assert.True(scene.ElectronDifference!.MaximumAbsolute > 0);
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("sampling.zip");
        await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, SamplingReport: report), path);
        var opened = await app.CameraImaging.OpenExperimentAsync(path); Assert.NotNull(opened.SamplingReport);
        await app.CameraImaging.ExportSamplingAsync(opened.B, opened.SamplingReport, files.Path("sampling.json"));
        app.Documents.NewBlank();
        var replay = await app.CameraImaging.CheckSamplingAsync(opened.B, region, CameraSamplingAxis.Pixel, opened.SamplingReport);
        Assert.Equal(5, replay.Comparisons.Count); Assert.Equal(report.SourceRunSha256, replay.SourceRunSha256);
        Assert.Equal(report.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel).ElectronDifference,
            replay.Comparisons.Single(c => c.Axis == CameraSamplingAxis.Pixel).ElectronDifference);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSamplingAsync(run with { SourceRevision = run.SourceRevision + 1 }, report, files.Path("invalid.json")));
        var bad = report with { SourceRunSha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, run, SamplingReport: bad), files.Path("bad.zip")));
        var metrics = scene.ElectronDifference with { MaximumAbsolute = scene.ElectronDifference.MaximumAbsolute + 1 };
        bad = report with { Comparisons = report.Comparisons.Select(c => c.Axis == CameraSamplingAxis.Scene ? c with { ElectronDifference = metrics } : c).ToArray() };
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.ExportSamplingAsync(run, bad, files.Path("bad.json")));
        var evidence = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(evidence))
        {
            Directory.CreateDirectory(evidence); File.Copy(path, Path.Combine(evidence, "sampling-check.camera-experiment.zip"), true);
            await app.CameraImaging.ExportSamplingAsync(run, report, Path.Combine(evidence, "sampling-check.json"));
        }
    }

    [Fact]
    public async Task ResourceFailuresAndMaximumSamplingAreExplicitAndCancellationPreservesTheSource()
    {
        using var app = WorkbenchApplication.Create(); var run = await Source(app.CameraImaging, 8, 8, subdivision: 4);
        var report = await app.CameraImaging.CheckSamplingAsync(run, new(2, 2), CameraSamplingAxis.Scene);
        Assert.Equal(CameraSamplingComparisonStatus.AtLimit, Assert.Single(report.Comparisons).Status);
        Assert.Null(report.Comparisons[0].Refined); Assert.Null(report.Comparisons[0].ElectronDifference);
        await Assert.ThrowsAsync<InvalidDataException>(() => app.CameraImaging.CheckSamplingAsync(run, new(7, 7), CameraSamplingAxis.Pixel));
        using var cts = new CancellationTokenSource(); cts.Cancel(); var raw = run.RawDn.ToArray();
        await Assert.ThrowsAsync<OperationCanceledException>(() => app.CameraImaging.CheckSamplingAsync(run, new(2, 2), CameraSamplingAxis.Pixel, cancellationToken: cts.Token));
        Assert.Equal(raw, run.RawDn);
        // A valid base window can be refined past its fixed guard; report the refusal, never retry with changed settings.
        var large = await app.CameraImaging.RecomputeAsync(run, run.Request with { Render3D = new(Diffraction: new(8, 1, 8, 9)) });
        report = await app.CameraImaging.CheckSamplingAsync(large, new(2, 2), CameraSamplingAxis.Window);
        Assert.Equal(CameraSamplingComparisonStatus.Unavailable, report.Comparisons[0].Status); Assert.Null(report.Comparisons[0].Refined);
    }
}

internal static class CameraSamplingVectorTestExtensions
{
    internal static Vector3D NormalizeDirectionForTest(this Vector3D v) => v / v.Length;
}
