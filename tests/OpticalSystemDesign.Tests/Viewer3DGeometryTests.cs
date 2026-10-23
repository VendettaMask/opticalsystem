using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Core;
using Vector3D = OpticalSystemDesign.Core.Backend.Vector3D;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Visualization;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class Viewer3DGeometryTests
{
    private const string CaptureDirectory = "OPTICAL_SYSTEM_DESIGN_VIEWER3D_REPAIR_CAPTURE_DIR";
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(10, 0, 0, 0, 0, 0)]
    [InlineData(0, -5, 0, 0, 0, 0)]
    [InlineData(10, -3, 7, 20, -30, 15)]
    public void MeshAndSurfacePrimitivesFollowTheSameRigidTransform(double x, double y, double z, double rx, double ry, double rz)
    {
        var optic = Zoom();
        var original = Build(optic);
        var frame = new CoordinateSystem(new Vector3D(x, y, z), rx, ry, rz);
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.CoordinateSystem = frame with { Origin = frame.ToGlobalPoint(surface.CoordinateSystem.Origin) };
        var transformed = Build(optic);
        void Compare(IEnumerable<Layout3DPoint> before, IEnumerable<Layout3DPoint> after)
        {
            var left = before.ToArray(); var right = after.ToArray();
            Assert.Equal(left.Length, right.Length);
            foreach (var (a, b) in left.Zip(right))
            {
                var expected = frame.ToGlobalPoint(new Vector3D(a.X, a.Y, a.Z));
                Assert.InRange(Math.Abs(expected.X - b.X), 0, 1e-10);
                Assert.InRange(Math.Abs(expected.Y - b.Y), 0, 1e-10);
                Assert.InRange(Math.Abs(expected.Z - b.Z), 0, 1e-10);
            }
        }
        foreach (var (before, after) in original.Surfaces.Zip(transformed.Surfaces)) Compare(before.Rim, after.Rim);
        foreach (var (before, after) in original.LensElements.Zip(transformed.LensElements))
        {
            Compare(before.FrontRim, after.FrontRim); Compare(before.BackRim, after.BackRim);
            Compare(before.FrontFaces.SelectMany(face => face.Points), after.FrontFaces.SelectMany(face => face.Points));
            Compare(before.BackFaces.SelectMany(face => face.Points), after.BackFaces.SelectMany(face => face.Points));
            Compare(before.MeridianBoundary, after.MeridianBoundary);
        }
    }

    [Fact]
    public void NonRotationalSurfaceUsesItsFullLocalSagAfterTiltAndDecenter()
    {
        var optic = Zoom();
        var pair = Build(optic).LensElements[0];
        var front = optic.SurfaceGroup.Items.Single(s => s.Number == pair.FrontSurfaceNumber);
        var back = optic.SurfaceGroup.Items.Single(s => s.Number == pair.BackSurfaceNumber);
        front.SemiDiameter = back.SemiDiameter = 5;
        front.MechanicalSemiDiameter = back.MechanicalSemiDiameter = 5;
        front.ChipZone = back.ChipZone = 0;
        front.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
            { [(2, 0)] = .02, [(0, 2)] = .005, [(1, 1)] = .003 });
        front.CoordinateSystem = new CoordinateSystem(front.CoordinateSystem.Origin + new Vector3D(2, -3, 0), 12, -20, 8);
        var element = Build(optic).LensElements[0];
        foreach (var point in element.FrontFaces.SelectMany(face => face.Points).Concat(element.FrontRim))
        {
            var local = front.CoordinateSystem.ToLocalPoint(new Vector3D(point.X, point.Y, point.Z));
            var expected = .02 * local.X * local.X + .005 * local.Y * local.Y + .003 * local.X * local.Y;
            Assert.InRange(Math.Abs(expected - local.Z), 0, 1e-10);
        }
        var xEdge = front.CoordinateSystem.ToLocalPoint(new Vector3D(element.FrontRim[0].X, element.FrontRim[0].Y, element.FrontRim[0].Z));
        Assert.Equal(.5, xEdge.Z, 10);
    }

    [Fact]
    public void MechanicalShouldersStayFlatInTheirOwnFrameAndAreIncludedInBounds()
    {
        var optic = Zoom();
        var first = Build(optic).LensElements[0];
        var front = optic.SurfaceGroup.Items.Single(s => s.Number == first.FrontSurfaceNumber);
        var back = optic.SurfaceGroup.Items.Single(s => s.Number == first.BackSurfaceNumber);
        front.SemiDiameter = back.SemiDiameter = 3;
        front.MechanicalSemiDiameter = back.MechanicalSemiDiameter = 30;
        front.ChipZone = back.ChipZone = 0;
        var scene = Build(optic);
        var element = scene.LensElements[0];
        foreach (var point in element.FrontRim)
        {
            var local = front.CoordinateSystem.ToLocalPoint(new Vector3D(point.X, point.Y, point.Z));
            var radius = Math.Sqrt(local.X * local.X + local.Y * local.Y);
            Assert.Equal(30, radius, 10);
            Assert.Equal(front.Geometry.Sag(local.X / 10, local.Y / 10), local.Z, 10);
        }
        Assert.True(scene.XExtent >= 30 && scene.YExtent >= 30);
    }

    [Theory]
    [InlineData(.5, true)]
    [InlineData(10, false)]
    [InlineData(-10, false)]
    public void CutawayCapsComeFromTheActualMeshPlaneIntersection(double centerX, bool intersects)
    {
        var front = new[] { new ScenePoint3Dto(centerX - 2, 1, 10), new(centerX + 2, 1, 10),
            new ScenePoint3Dto(centerX + 2, 5, 10), new(centerX - 2, 5, 10) };
        var back = front.Select(p => p with { Z = 14 }).ToArray();
        var element = new SceneLensElement3Dto(1, 2, "glass", 1.5,
            front.Append(front[0]).ToArray(), back.Append(back[0]).ToArray(),
            new[] { new SceneSurfaceFace3Dto(front) }, new[] { new SceneSurfaceFace3Dto(back) },
            // Deliberately unrelated to the cut plane: this must not become a cap.
            new[] { new ScenePoint3Dto(50, 50, 50), new(60, 60, 60), new(70, 70, 70) });
        var caps = OpticSceneControl.CutawayCapsForRendering(element);
        if (!intersects) { Assert.Empty(caps); return; }
        var cap = Assert.Single(caps);
        Assert.Equal(4, cap.Count);
        Assert.All(cap, p => Assert.Equal(0, p.X));
        Assert.Equal(new[] { (1.0, 10.0), (1.0, 14.0), (5.0, 10.0), (5.0, 14.0) },
            cap.Select(p => (p.Y, p.Z)).OrderBy(p => p.Y).ThenBy(p => p.Z));
    }

    [Theory]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Light", "zh-CN")]
    [InlineData(ViewerPresentationMode.SolidModel, "Light", "zh-CN")]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Dark", "en-US")]
    public async Task LayoutIsWireframeAndShadingControlsBelongToSolidModel(ViewerPresentationMode mode, string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(async () =>
        {
            var oldLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language); ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), SceneDimension.ThreeDimensional, mode);
            var window = new Window { Content = viewer, Width = 1100, Height = 740 };
            try
            {
                window.Show();
                var scene = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                for (var i = 0; i < 200 && scene.Scene is null; i++) { Tick(window); await Task.Delay(20); }
                Assert.NotNull(scene.Scene?.ThreeDimensional);
                var modes = viewer.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "Viewer3DRenderMode");
                var cutaway = viewer.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "Viewer3DCutaway");
                var solid = mode == ViewerPresentationMode.SolidModel;
                Assert.Equal(solid, modes.IsVisible); Assert.Equal(solid, cutaway.IsVisible);
                Assert.Equal(solid ? OpticSceneRenderMode.Solid : OpticSceneRenderMode.Wireframe, scene.RenderMode);
                Capture(window, $"viewer3d-{mode}-{theme}-{language}.png");
                if (solid)
                {
                    modes.SelectedIndex = 1; Tick(window);
                    Assert.Equal(OpticSceneRenderMode.Transparent, scene.RenderMode);
                    Capture(window, "viewer3d-solid-transparent.png");
                    cutaway.IsChecked = true; Tick(window);
                    Assert.True(scene.CutawayEnabled);
                    Capture(window, "viewer3d-solid-cutaway.png");
                    modes.SelectedIndex = 2; Tick(window);
                    Assert.Equal(OpticSceneRenderMode.Wireframe, scene.RenderMode);
                }
            }
            finally { window.Close(); UiLocalization.Apply(oldLanguage); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(15, 25, 10)]
    [InlineData(-20, -30, 25)]
    public void TiltedLensCapsRemainOnTheGlobalCutPlane(double rx, double ry, double rz)
    {
        var optic = Zoom();
        var first = Build(optic).LensElements[0];
        var pivot = optic.SurfaceGroup.Items.Single(s => s.Number == first.FrontSurfaceNumber).CoordinateSystem.Origin;
        var frame = new CoordinateSystem(new Vector3D(.5, 3, pivot.Z), rx, ry, rz);
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.CoordinateSystem = frame with { Origin = frame.ToGlobalPoint(surface.CoordinateSystem.Origin - pivot) };
        var element = WorkbenchMapper.ToScene3Dto(Build(optic)).LensElements[0];
        var caps = OpticSceneControl.CutawayCapsForRendering(element);
        Assert.NotEmpty(caps);
        Assert.All(caps, cap =>
        {
            Assert.True(cap.Count >= 3);
            Assert.All(cap, point => Assert.Equal(0, point.X, 10));
            Assert.True(cap.Max(p => p.Y) - cap.Min(p => p.Y) > .01);
            Assert.True(cap.Max(p => p.Z) - cap.Min(p => p.Z) > .01);
        });
    }

    private static Optic Zoom() => new ZemaxZmxImporter().ImportConfigurationSet(
        File.ReadAllText(ConfigurationMatrixAnalysisTests.ZoomPath())).Configurations[0];
    private static Layout3DScene Build(Optic optic) => new Layout2DBuilder(optic).Build3D(
        surfaceSamples: 17, rimSamples: 32, options: new LayoutBuildOptions(RayCount: 0));
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
