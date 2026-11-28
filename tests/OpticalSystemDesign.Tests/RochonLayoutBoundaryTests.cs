using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class RochonLayoutBoundaryTests
{
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();
    private static string Source => SequentialExampleCatalog.Examples.Single(e => e.RelativePath.EndsWith("/Rochon prism.zmx")).FilePath;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TwoPlanarCrystalVolumesFormThePrescribedSquareWithoutCoordinateWalls(int configuration)
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Source)).Configurations[configuration];
        var json = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var before = JsonSerializer.Serialize(optic.ToSnapshot(), json);
        var builder = new Layout2DBuilder(optic);
        var scene = builder.Build(options: new(RayCount: 3, WavelengthIndex: 1));
        Assert.Equal(new[] { (2, 4), (5, 7) }, scene.LensElements.Select(e => (e.FrontSurfaceNumber, e.BackSurfaceNumber)));
        Assert.All(scene.LensElements, e => Assert.Equal(3, e.Boundary.Count));
        Assert.All(scene.LensElements.SelectMany(e => e.Boundary), p =>
        {
            Assert.InRange(p.Z, 20 - 1e-9, 70 + 1e-9);
            Assert.InRange(p.Y, -25 - 1e-9, 25 + 1e-9);
        });
        Assert.Equal(2500, scene.LensElements.Sum(e => Area(e.Boundary)), 7);
        Assert.DoesNotContain(scene.Surfaces, s => s.SurfaceNumber is 3 or 6);
        foreach (var number in new[] { 4, 5 })
            Assert.All(scene.Surfaces.Single(s => s.SurfaceNumber == number).Points,
                p => Assert.Equal(45, p.Z + p.Y, 9));
        var scene3 = builder.Build3D(options: new(RayCount: 3, WavelengthIndex: 1));
        Assert.Equal(2, scene3.LensElements.Count);
        Assert.DoesNotContain(scene3.Surfaces, s => s.SurfaceNumber is 3 or 6);
        Assert.All(scene3.LensElements.SelectMany(e => e.FrontRim.Concat(e.BackRim)), p =>
        {
            Assert.InRange(p.X, -25 - 1e-9, 25 + 1e-9);
            Assert.InRange(p.Y, -25 - 1e-9, 25 + 1e-9);
            Assert.InRange(p.Z, 20 - 1e-9, 70 + 1e-9);
        });
        Assert.Equal(before, JsonSerializer.Serialize(optic.ToSnapshot(), json));
    }

    [Fact]
    public async Task PlanarVolumeMeshFollowsTheAssemblyPoseInsteadOfTheFilenameOrGlobalAxes()
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Source)).ActiveOptic;
        var frame = new CoordinateSystem(new(13, -7, 4), 11, 17, -23);
        foreach (var s in optic.SurfaceGroup.Items)
        {
            s.Label = "Custom assembly " + s.Number;
            s.CoordinateSystem = s.CoordinateSystem.MoveWithFrame(CoordinateSystem.Global, frame);
        }
        var scene = new Layout2DBuilder(optic).Build3D(options: new(RayCount: 0));
        Assert.Equal(2, scene.LensElements.Count);
        foreach (var p in scene.LensElements.SelectMany(e => e.FrontRim.Concat(e.BackRim)))
        {
            var original = frame.ToLocalPoint(new(p.X, p.Y, p.Z));
            Assert.InRange(original.X, -25 - 1e-9, 25 + 1e-9);
            Assert.InRange(original.Y, -25 - 1e-9, 25 + 1e-9);
            Assert.InRange(original.Z, 20 - 1e-9, 70 + 1e-9);
        }
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)]
    public async Task EnergyRaysMatchIndependentSnellAnglesAtBothCrystalInterfaces(int configuration, int waveIndex)
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Source)).Configurations[configuration];
        var wave = optic.Wavelengths[waveIndex];
        var crystal = Assert.IsType<UniaxialMaterial>(optic.SurfaceGroup.Items[5].MaterialAfter);
        var no = crystal.Ordinary.RefractiveIndex(wave.Nanometers);
        var ne = crystal.Extraordinary.RefractiveIndex(wave.Nanometers);
        var inside = crystal.TraceExtraordinary ? Math.PI / 4 - Math.Asin(no / ne * Math.Sin(Math.PI / 4)) : 0;
        var outside = Math.Asin((crystal.TraceExtraordinary ? ne : no) * Math.Sin(inside));
        foreach (var pupil in new[] { -1d, 0d, 1d })
        {
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, pupil, wave.Micrometers, false).Rays.Single();
            var exit = optic.SequentialRayTracer.TraceToSurface(ray, 7)!;
            Assert.Equal(Math.Sin(outside), exit.Direction.Y, 11);
            Assert.Equal(Math.Cos(outside), exit.Direction.Z, 11);
            var image = optic.SequentialRayTracer.TraceToSurface(ray, 9)!;
            Assert.Equal(100 * Math.Tan(outside), image.Position.Y, 9);
        }
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(SceneDimension.ThreeDimensional, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(SceneDimension.ThreeDimensional, OpticSceneRenderMode.Solid, false)]
    [InlineData(SceneDimension.ThreeDimensional, OpticSceneRenderMode.Solid, true)]
    [InlineData(SceneDimension.ThreeDimensional, OpticSceneRenderMode.Transparent, true)]
    public async Task ActualViewerRendersBothConfigurationsWithTheSamePhysicalBoxAndDeclaredRays(
        SceneDimension dimension, OpticSceneRenderMode mode, bool cutaway)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RochonLayoutBoundaryTests));
        var rendered = false;
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(Source);
            var active = app.MultiConfiguration.GetNavigationState().ActiveIndex;
            var dto = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(dimension, FieldIndex: 0, WavelengthIndex: 1,
                RayCount: 3, LowerPupil: -1, UpperPupil: 1, MarginalAndChiefOnly: true, IncludeAllConfigurations: true));
            Assert.Equal(active, app.MultiConfiguration.GetNavigationState().ActiveIndex);
            Assert.Equal(4, dimension == SceneDimension.TwoDimensional ? dto.TwoDimensional!.LensElements.Count : dto.ThreeDimensional!.LensElements.Count);
            var launchHeights = dimension == SceneDimension.TwoDimensional
                ? dto.TwoDimensional!.Rays.Select(r => r.Points[0].Y)
                : dto.ThreeDimensional!.Rays.Select(r => r.Points[0].Y);
            Assert.Equal(new[] { -5d, -5d, 0d, 0d, 5d, 5d }, launchHeights.Order());
            var control = new OpticSceneControl { Scene = dto, ShowRays = true, RayColorMode = OpticSceneRayColorMode.Configuration,
                RenderMode = mode, CutawayEnabled = cutaway,
                ViewMode = dimension == SceneDimension.TwoDimensional ? OpticSceneViewMode.TwoDimensional : OpticSceneViewMode.ThreeDimensional };
            control.SetViewPreset(OpticSceneViewPreset.Front);
            var window = new Window { Content = control, Width = 1200, Height = 750 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var bitmap = new RenderTargetBitmap(new PixelSize(1200, 750), new Vector(96, 96));
                bitmap.Render(window);
                var output = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ROCHON_LAYOUT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    bitmap.Save(Path.Combine(output, $"rochon-{dimension}-{mode}-{cutaway}.png"), PngBitmapEncoderOptions.Default);
                    File.WriteAllText(Path.Combine(output, "rochon-" + dimension + ".json"), JsonSerializer.Serialize(dto,
                        new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
                }
                rendered = true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        Assert.True(rendered);
    }

    private static double Area(IReadOnlyList<Layout2DPoint> p) => Math.Abs(p.Select((a, i) =>
        a.Z * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].Z * a.Y).Sum()) / 2;
}
