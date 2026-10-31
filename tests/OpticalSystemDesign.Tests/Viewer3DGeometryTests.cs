using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Visualization;
using Vector3D = OpticalSystemDesign.Core.Backend.Vector3D;

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

    [Fact]
    public void ImageSurfaceMetadataPreservesActualTiltSagAndSelectedSurfaceRange()
    {
        var optic = Zoom();
        var image = optic.SurfaceGroup.Items[^1];
        image.Label = "not a display-role selector";
        optic.SurfaceGroup.Items[1].Label = "Image";
        image.SemiDiameter = 4; image.ChipZone = 0;
        image.Geometry = new PolynomialGeometry(new Dictionary<(int, int), double>
        { [(2, 0)] = .02, [(0, 2)] = .01, [(1, 1)] = .003 });
        image.CoordinateSystem = new CoordinateSystem(image.CoordinateSystem.Origin + new Vector3D(3, -2, 1), 12, -20, 8);
        var snapshotOptions = new System.Text.Json.JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var before = System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), snapshotOptions);
        var core = Build(optic);
        var surface = Assert.Single(core.Surfaces, s => s.IsImageSurface);
        Assert.Equal(image.Number, surface.SurfaceNumber);
        Assert.True(surface.IsReferencePlane);
        foreach (var point in surface.Faces.SelectMany(face => face.Points).Concat(surface.Rim))
        {
            var local = image.CoordinateSystem.ToLocalPoint(new Vector3D(point.X, point.Y, point.Z));
            Assert.InRange(Math.Abs(local.Z - (.02 * local.X * local.X + .01 * local.Y * local.Y + .003 * local.X * local.Y)), 0, 1e-10);
        }
        var mapped = Assert.Single(WorkbenchMapper.ToScene3Dto(core).Surfaces, s => s.IsImageSurface);
        Assert.Equal(surface.SurfaceNumber, mapped.SurfaceNumber);
        Assert.Equal(surface.Rim.Select(p => (p.X, p.Y, p.Z)), mapped.Rim.Select(p => (p.X, p.Y, p.Z)));
        Assert.DoesNotContain(new Layout2DBuilder(optic).Build3D(options: new LayoutBuildOptions(
            LastSurface: image.Number - 1, RayCount: 0)).Surfaces, s => s.IsImageSurface);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), snapshotOptions));
    }

    [Viewer3DRenderTheory]
    [InlineData(OpticSceneRenderMode.Solid, false, "Light")]
    [InlineData(OpticSceneRenderMode.Transparent, false, "Light")]
    [InlineData(OpticSceneRenderMode.Wireframe, false, "Light")]
    [InlineData(OpticSceneRenderMode.Solid, true, "Dark")]
    [InlineData(OpticSceneRenderMode.Transparent, true, "Dark")]
    [InlineData(OpticSceneRenderMode.Wireframe, true, "Dark")]
    public async Task UnselectedImageSurfaceIsVisibleWithoutRaysAndStaysWholeInCutaway(
        OpticSceneRenderMode mode, bool cutaway, string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            var dto = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 3));
            var source = dto.ThreeDimensional!;
            var image = Assert.Single(source.Surfaces, s => s.IsImageSurface);
            Assert.True(image.Rim.Max(p => p.X) - image.Rim.Min(p => p.X) > .1);
            Assert.True(image.Rim.Max(p => p.Y) - image.Rim.Min(p => p.Y) > .1);
            var revision = app.Events.Revision;
            var control = new OpticSceneControl
            {
                Scene = dto,
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = OpticSceneVisualStyle.SolidModel,
                RenderMode = mode,
                ShowRays = false,
                CutawayEnabled = cutaway
            };
            var window = new Window { Content = control, Width = 800, Height = 560 };
            try
            {
                window.Show(); Tick(window);
                var visible = RenderPixels(control);
                Capture(window, $"image-surface-{mode}-{cutaway}-{theme}.png");
                var unmarked = dto with
                {
                    ThreeDimensional = source with
                    { Surfaces = source.Surfaces.Select(s => s with { IsImageSurface = false }).ToArray() }
                };
                control.Scene = unmarked; Tick(window);
                Assert.True(ChangedPixels(visible, RenderPixels(control), 800, new PixelRect(0, 0, 800, 560)) > 100,
                    "An unselected image surface must produce visible reference pixels even with rays hidden.");
                // The ordinary 3D Layout keeps its existing reference drawing.
                control.VisualStyle = OpticSceneVisualStyle.OpticalLayout; Tick(window);
                var layoutWithoutFlag = RenderPixels(control);
                control.Scene = dto; Tick(window);
                Assert.Equal(layoutWithoutFlag, RenderPixels(control));
                // Only the real image surface is isolated here; cutaway must not clip it.
                control.VisualStyle = OpticSceneVisualStyle.SolidModel;
                control.Scene = dto with { ThreeDimensional = source with { Surfaces = [image], LensElements = [], Rays = [] } };
                control.CutawayEnabled = false; Tick(window);
                var whole = RenderPixels(control);
                control.CutawayEnabled = true; Tick(window);
                Assert.Equal(whole, RenderPixels(control));
                Assert.Empty(control.GlassMaterials);
                Assert.Equal(revision, app.Events.Revision);
                Assert.Same(source, dto.ThreeDimensional);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Viewer3DRenderTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task BothViewersKeepOnlyTheLowerLeftLabelledTriad(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var dto = new SceneDto(0, SceneDimension.ThreeDimensional, null,
                new Scene3Dto([], [], [], 1, 1, 0, 10), app.Documents.GetSnapshot());
            var control = new OpticSceneControl
            {
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = OpticSceneVisualStyle.SolidModel,
                ShowScaleBar = false
            };
            var window = new Window { Content = control, Width = 600, Height = 400 };
            try
            {
                window.Show(); Tick(window);
                var background = RenderPixels(control);
                control.Scene = dto; Tick(window);
                var solid = RenderPixels(control);
                var right = new PixelRect(525, 325, 65, 65);
                var left = new PixelRect(5, 300, 100, 95);
                Assert.Equal(0, ChangedPixels(background, solid, 600, right));
                Assert.True(ChangedPixels(background, solid, 600, left) > 80);
                control.VisualStyle = OpticSceneVisualStyle.OpticalLayout;
                control.Scene = null; Tick(window);
                var layoutBackground = RenderPixels(control);
                control.Scene = dto; Tick(window);
                var layout = RenderPixels(control);
                Assert.Equal(0, ChangedPixels(layoutBackground, layout, 600, right));
                Assert.True(ChangedPixels(layoutBackground, layout, 600, left) > 80);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
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
    [InlineData(ViewerPresentationMode.SolidModel, "Dark", "en-US")]
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
                Tick(window); // The async scene update adds legend children before their next layout pass.
                var modes = viewer.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "Viewer3DRenderMode");
                var cutaway = viewer.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "Viewer3DCutaway");
                var solid = mode == ViewerPresentationMode.SolidModel;
                Assert.Equal(solid, modes.IsVisible); Assert.Equal(solid, cutaway.IsVisible);
                Assert.Equal(solid ? OpticSceneRenderMode.Solid : OpticSceneRenderMode.Wireframe, scene.RenderMode);
                var request = (VisualizationRequestDto)typeof(ViewerPanel)
                    .GetMethod("CreateRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, null)!;
                Assert.Equal(solid ? 21 : 7, request.RayCount);
                Assert.Equal(solid ? 0.7 : 1.25, scene.RayLineWidth);
                if (solid)
                {
                    var sparse = await app.Visualization.BuildSceneAsync(request with { RayCount = 7 });
                    Assert.True(scene.Scene!.ThreeDimensional!.Rays.Count > sparse.ThreeDimensional!.Rays.Count);
                    Assert.Equal(sparse.ThreeDimensional.LensElements.Select(e => (e.FrontSurfaceNumber, e.BackSurfaceNumber, e.Material)),
                        scene.Scene.ThreeDimensional.LensElements.Select(e => (e.FrontSurfaceNumber, e.BackSurfaceNumber, e.Material)));
                    var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(Path.Combine(directory, $"ray-count-{theme}-{language}.json"),
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                previousSampling = 7,
                                sampling = request.RayCount,
                                previousDisplayedRays = sparse.ThreeDimensional.Rays.Count,
                                displayedRays = scene.Scene.ThreeDimensional.Rays.Count,
                                widthDip = scene.RayLineWidth
                            }));
                    }
                }
                var legends = viewer.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Name == "Viewer3DGlassLegend").ToArray();
                if (solid)
                {
                    var legend = Assert.Single(legends);
                    Assert.True(legend.IsVisible);
                    Assert.NotEmpty(scene.GlassMaterials);
                    Assert.Equal(scene.Scene!.ThreeDimensional!.LensElements.Select(e => e.Material.Trim())
                        .Where(name => name.Length > 0 && !name.Equals("Air", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase),
                        legend.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                    var sceneBounds = new Rect(scene.Bounds.Size).TransformToAABB(scene.TransformToVisual(window)!.Value);
                    var legendBounds = new Rect(legend.Bounds.Size).TransformToAABB(legend.TransformToVisual(window)!.Value);
                    Assert.True(sceneBounds.Bottom <= legendBounds.Top);
                }
                else
                {
                    Assert.Empty(legends);
                    Assert.Empty(scene.GlassMaterials);
                    Assert.All(scene.Scene!.ThreeDimensional!.LensElements, e => Assert.Null(scene.GlassColorFor(e.Material)));
                }
                Capture(window, $"viewer3d-{mode}-{theme}-{language}.png");
                if (solid)
                {
                    modes.SelectedIndex = 1; Tick(window);
                    Assert.Equal(OpticSceneRenderMode.Transparent, scene.RenderMode);
                    Capture(window, $"viewer3d-solid-transparent-{theme}-{language}.png");
                    cutaway.IsChecked = true; Tick(window);
                    Assert.True(scene.CutawayEnabled);
                    Capture(window, $"viewer3d-solid-cutaway-{theme}-{language}.png");
                    modes.SelectedIndex = 2; Tick(window);
                    Assert.Equal(OpticSceneRenderMode.Wireframe, scene.RenderMode);
                    Assert.NotEmpty(scene.GlassMaterials);
                    Capture(window, $"viewer3d-solid-wireframe-{theme}-{language}.png");
                    window.Width = 400; window.Height = 460; Tick(window);
                    var legend = Assert.Single(legends);
                    Assert.True(legend.IsVisible);
                    var legendBounds = new Rect(legend.Bounds.Size).TransformToAABB(legend.TransformToVisual(window)!.Value);
                    Assert.InRange(legendBounds.Right, 0, 400);
                    Assert.InRange(legendBounds.Bottom, 0, 460);
                    foreach (var item in legend.GetVisualDescendants().OfType<TextBlock>())
                        Assert.True(item.Bounds.Width > 0);
                    Capture(window, $"viewer3d-solid-narrow-{theme}-{language}.png");
                }
                var lineWidth = (ComboBox)typeof(ViewerPanel)
                    .GetField("_lineWidthPicker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
                var widths = solid ? new[] { 0.7, 1.0, 1.5 } : new[] { 0.85, 1.25, 1.8 };
                for (var index = 0; index < widths.Length; index++)
                {
                    lineWidth.SelectedIndex = index;
                    Assert.Equal(widths[index], scene.RayLineWidth);
                }
                var rayCount = (NumericUpDown)typeof(ViewerPanel)
                    .GetField("_rayCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
                rayCount.Value = 9;
                typeof(ViewerPanel).GetMethod("ResetSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, null);
                Assert.Equal(solid ? 21 : 7, (int)rayCount.Value!.Value);
                Assert.Equal(solid ? 0.7 : 1.25, scene.RayLineWidth);
            }
            finally { window.Close(); UiLocalization.Apply(oldLanguage); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task GlassColorsFollowMaterialIdentityAndClearOnSceneOrStyleChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create();
            var source = WorkbenchMapper.ToScene3Dto(Build(Zoom()));
            var element = source.LensElements[0];
            var elements = new[]
            {
                element with { Material = "N-BK7", RefractiveIndex = 1.5 },
                element with { Material = " n-bk7 ", RefractiveIndex = 1.8 },
                element with { Material = "F2", RefractiveIndex = 1.5 },
                element with { Material = "Air" }, element with { Material = " " }
            };
            var dto = new SceneDto(0, SceneDimension.ThreeDimensional, null,
                source with { LensElements = elements }, app.Documents.GetSnapshot());
            var control = new OpticSceneControl
            {
                Scene = dto,
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = OpticSceneVisualStyle.SolidModel
            };
            Assert.Equal(2, control.GlassMaterials.Count);
            var bk7 = control.GlassColorFor("N-BK7");
            var f2 = control.GlassColorFor("F2");
            Assert.NotNull(bk7); Assert.NotEqual(bk7, f2);
            Assert.Equal(bk7, control.GlassColorFor(" n-bk7 "));
            Assert.Null(control.GlassColorFor("Air")); Assert.Null(control.GlassColorFor(" "));
            control.Scene = dto with { ThreeDimensional = source with { LensElements = elements.Reverse().ToArray() } };
            Assert.Equal(bk7, control.GlassColorFor("N-BK7")); Assert.Equal(f2, control.GlassColorFor("F2"));
            control.VisualStyle = OpticSceneVisualStyle.OpticalLayout;
            Assert.Empty(control.GlassMaterials); Assert.Null(control.GlassColorFor("F2"));
            control.VisualStyle = OpticSceneVisualStyle.SolidModel;
            Assert.Equal(f2, control.GlassColorFor("F2"));
            control.Scene = dto with
            {
                ThreeDimensional = source with
                {
                    LensElements = Enumerable.Range(0, 24).Select(index => element with { Material = $"Glass {index:00}" }).ToArray()
                }
            };
            Assert.Equal(24, control.GlassMaterials.Select(entry => entry.Color).Distinct().Count());
            Assert.DoesNotContain(control.GlassMaterials, entry => entry.Material == "F2");
            control.Scene = null;
            Assert.Empty(control.GlassMaterials); Assert.Null(control.GlassColorFor("F2"));
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

    [Theory]
    [InlineData(OpticSceneVisualStyle.SolidModel, "Light", 13)]
    [InlineData(OpticSceneVisualStyle.SolidModel, "Dark", 18)]
    [InlineData(OpticSceneVisualStyle.OpticalLayout, "Light", 13)]
    [InlineData(OpticSceneVisualStyle.OpticalLayout, "Dark", 18)]
    public async Task AxesTrackPresetsMouseAndKeyboardButIgnorePanZoomAndStretch(OpticSceneVisualStyle style, string theme, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            var dto = await app.Visualization.BuildSceneAsync(new VisualizationRequestDto(SceneDimension.ThreeDimensional, RayCount: 5));
            var control = new OpticSceneControl
            {
                Scene = dto,
                ViewMode = OpticSceneViewMode.ThreeDimensional,
                VisualStyle = style,
                RenderMode = style == OpticSceneVisualStyle.SolidModel
                    ? OpticSceneRenderMode.Solid : OpticSceneRenderMode.Wireframe,
                RayLineWidth = 0.7
            };
            var window = new Window { Width = 800, Height = 560, Content = control };
            try
            {
                window.Show(); Tick(window);
                var capturePixels = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
                var capturePrefix = $"axes-{style}-{theme}";
                byte[]? frontPixels = null;
                byte[]? backgroundPixels = null;
                if (capturePixels)
                {
                    control.Scene = null; control.InvalidateVisual(); Tick(window);
                    backgroundPixels = RenderPixels(control);
                    control.Scene = dto; control.InvalidateVisual(); Tick(window);
                }
                // Independent canonical screen directions: model Z is horizontal in Front,
                // X becomes horizontal in Left/Right, and Y points into the Top/Bottom view.
                var expected = new[]
                {
                    (OpticSceneViewPreset.Front, new[] { new Vector(0,0), new(0,-1), new(1,0) }),
                    (OpticSceneViewPreset.Back, new[] { new Vector(0,0), new(0,-1), new(-1,0) }),
                    (OpticSceneViewPreset.Left, new[] { new Vector(-1,0), new(0,-1), new(0,0) }),
                    (OpticSceneViewPreset.Right, new[] { new Vector(1,0), new(0,-1), new(0,0) }),
                    (OpticSceneViewPreset.Top, new[] { new Vector(0,1), new(0,0), new(1,0) }),
                    (OpticSceneViewPreset.Bottom, new[] { new Vector(0,-1), new(0,0), new(1,0) })
                };
                foreach (var (preset, directions) in expected)
                {
                    control.SetViewPreset(preset); Tick(window);
                    var axes = control.OrientationAxesForRendering();
                    Assert.Equal(new[] { "X", "Y", "Z" }, axes.Select(axis => axis.Label));
                    Assert.Equal(3, axes.Select(axis => axis.Color).Distinct().Count());
                    for (var i = 0; i < 3; i++)
                    {
                        Assert.InRange(Math.Abs(axes[i].Direction.X - directions[i].X), 0, 0.02);
                        Assert.InRange(Math.Abs(axes[i].Direction.Y - directions[i].Y), 0, 0.02);
                    }
                    if (capturePixels && preset == OpticSceneViewPreset.Front) frontPixels = RenderPixels(control);
                    if (capturePixels && preset == OpticSceneViewPreset.Back) AssertGizmoChanged(frontPixels!, RenderPixels(control));
                    Capture(window, $"{capturePrefix}-{preset}.png");
                }
                control.SetViewPreset(OpticSceneViewPreset.Isometric); Tick(window);
                Vector[] Directions() => control.OrientationAxesForRendering().Select(axis => axis.Direction).ToArray();
                var initial = Directions();
                var initialPixels = capturePixels ? RenderPixels(control) : null;
                Capture(window, $"{capturePrefix}-isometric.png");
                var pointer = new Point(400, 260);
                window.MouseDown(pointer, MouseButton.Left);
                window.MouseMove(pointer + new Vector(65, 28));
                window.MouseUp(pointer + new Vector(65, 28), MouseButton.Left); Tick(window);
                var dragged = Directions();
                Assert.False(initial.SequenceEqual(dragged));
                var draggedPixels = capturePixels ? RenderPixels(control) : null;
                if (capturePixels) AssertGizmoChanged(initialPixels!, draggedPixels!);
                Capture(window, $"{capturePrefix}-dragged.png");
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                var rotated = Directions();
                Assert.False(dragged.SequenceEqual(rotated));
                if (capturePixels) AssertGizmoChanged(draggedPixels!, RenderPixels(control));
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.None, null);
                window.MouseWheel(pointer, new Vector(0, 1));
                control.VerticalStretch = 2.5; Tick(window);
                Assert.Equal(rotated, Directions());
                Capture(window, $"{capturePrefix}-pan-zoom-stretch.png");
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                Assert.Equal(initial, Directions());
                Capture(window, $"{capturePrefix}-reset.png");
                Assert.Same(dto, control.Scene);
                Assert.Equal(dto.SourceRevision, app.Events.Revision);

                void AssertGizmoChanged(byte[] before, byte[] after)
                {
                    var pixelWidth = (int)control.Bounds.Width;
                    var pixelHeight = (int)control.Bounds.Height;
                    Assert.True(ChangedPixels(before, after, pixelWidth, new PixelRect(0, pixelHeight - 100, 100, 100)) > 5,
                        "The lower-left rendered axes must rotate with the camera.");
                    var corner = new PixelRect(pixelWidth - 76, pixelHeight - 76, 60, 60);
                    Assert.Equal(0, ChangedPixels(backgroundPixels!, after, pixelWidth, corner));
                }
            }
            finally
            {
                window.Close();
                DisplayTypography.Configure(new AppSettings());
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ViewerPresentationMode.SolidModel, "Light", "zh-CN", 1100, 13)]
    [InlineData(ViewerPresentationMode.SolidModel, "Dark", "en-US", 400, 13)]
    [InlineData(ViewerPresentationMode.SolidModel, "Light", "ja-JP", 400, 18)]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Light", "zh-CN", 1100, 13)]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Dark", "en-US", 400, 13)]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Light", "ja-JP", 400, 13)]
    [InlineData(ViewerPresentationMode.OpticalLayout, "Light", "ja-JP", 400, 18)]
    public async Task ViewButtonsShowLabelsAndTrackTheActualCamera(ViewerPresentationMode mode, string theme, string language, int width, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(Viewer3DGeometryTests));
        await session.Dispatch(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(),
                SceneDimension.ThreeDimensional, mode);
            var namePrefix = mode == ViewerPresentationMode.SolidModel ? "SolidModel" : "Viewer3D";
            var capturePrefix = $"view-buttons-{mode}-{theme}-{language}-{width}";
            var window = new Window { Width = width, Height = 700, FontSize = fontSize, Content = viewer };
            try
            {
                window.Show(); Tick(window);
                var scene = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                for (var i = 0; i < 200 && scene.Scene is null; i++) { Tick(window); await Task.Delay(20); }
                Assert.NotNull(scene.Scene); Tick(window);
                var originalScene = scene.Scene;
                var revision = app.Events.Revision;
                var toolbar = viewer.GetVisualDescendants().OfType<Border>().Single(item => item.Name == $"{namePrefix}ViewPresets");
                var buttons = toolbar.GetVisualDescendants().OfType<ToggleButton>().ToArray();
                Assert.Equal(7, buttons.Length);
                AssertSelected(OpticSceneViewPreset.Isometric);
                Capture(window, $"{capturePrefix}-initial.png");
                if (viewer.GetVisualDescendants().OfType<UniformDiagramPage>()
                    .SingleOrDefault() is { HasOverflow: true } overflowingSheet)
                {
                    // A large caption must report overflow and reject export;
                    // its warning intentionally covers the drawing controls.
                    Assert.True(fontSize > 13);
                    Assert.Equal(new Size(1200, 900), overflowingSheet.Bounds.Size);
                    Assert.Throws<InvalidOperationException>(() => viewer.CaptureViewerImage(1));
                    var warning = overflowingSheet.GetVisualDescendants().OfType<TextBlock>().Single(item =>
                        item.Text == UiLocalization.Text("当前内容超出统一图框，请减少同时显示的视场或子图，或减小文字后刷新。"));
                    Assert.True(warning.IsEffectivelyVisible);
                    Assert.All(buttons, button => Assert.Equal(fontSize,
                        button.GetVisualDescendants().OfType<TextBlock>().Single().FontSize));
                    Assert.Same(originalScene, scene.Scene);
                    Assert.Equal(revision, app.Events.Revision);
                    return true;
                }
                var captureDirectory = Environment.GetEnvironmentVariable(CaptureDirectory);
                if (!string.IsNullOrWhiteSpace(captureDirectory) && toolbar.Child is Control row)
                {
                    using var rowImage = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(row.Bounds.Width),
                        (int)Math.Ceiling(row.Bounds.Height)), new Vector(96, 96));
                    rowImage.Render(row);
                    rowImage.Save(Path.Combine(captureDirectory, $"{capturePrefix}-toolbar.png"), PngBitmapEncoderOptions.Default);
                }
                foreach (var preset in Enum.GetValues<OpticSceneViewPreset>())
                {
                    var button = buttons.Single(item => item.Name == $"{namePrefix}View{preset}");
                    var label = button.GetVisualDescendants().OfType<TextBlock>().Single();
                    Assert.False(string.IsNullOrWhiteSpace(label.Text));
                    Assert.Equal(fontSize, label.FontSize);
                    Assert.Equal(label.Text, AutomationProperties.GetName(button));
                    await Click(button); AssertSelected(preset);
                    await Click(button); AssertSelected(preset); // Re-clicking cannot leave the active view unchecked.
                    Assert.InRange(VisualBounds(button).Left, VisualBounds(toolbar).Left, VisualBounds(toolbar).Right);
                    Assert.InRange(VisualBounds(button).Right, VisualBounds(toolbar).Left, VisualBounds(toolbar).Right);
                    Assert.InRange(VisualBounds(button).Bottom, VisualBounds(toolbar).Top, VisualBounds(toolbar).Bottom);
                }
                // Exercise the canvas outside the floating navigation, which can
                // cover the center after a full caption reduces the viewport height.
                if (viewer.GetVisualDescendants().OfType<ScrollViewer>()
                    .SingleOrDefault(item => item.Name == "ViewerDiagramScroll") is { } diagramScroll)
                {
                    diagramScroll.Offset = default;
                    Tick(window);
                    var sheet = viewer.GetVisualDescendants().OfType<UniformDiagramPage>().Single();
                    Assert.Equal(UniformDiagramPage.SheetWidth, sheet.Bounds.Width);
                    Assert.Equal(UniformDiagramPage.SheetHeight, sheet.Bounds.Height);
                }
                var pointer = scene.TranslatePoint(new Point(40, scene.Bounds.Height * 0.4), window)!.Value;
                window.MouseDown(pointer, MouseButton.Left);
                window.MouseMove(pointer + new Vector(35, 15));
                window.MouseUp(pointer + new Vector(35, 15), MouseButton.Left); Tick(window);
                Assert.Null(scene.ActiveViewPreset);
                Assert.DoesNotContain(buttons, item => item.IsChecked == true);
                Capture(window, $"{capturePrefix}-free-rotation.png");
                var front = buttons.Single(item => item.Name == $"{namePrefix}ViewFront");
                BringIntoViewport(front);
                front.Focus();
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Tick(window);
                AssertSelected(OpticSceneViewPreset.Front);
                await Click(toolbar.GetVisualDescendants().OfType<Button>().Single(item => item.Name == $"{namePrefix}FitView"));
                AssertSelected(OpticSceneViewPreset.Front);
                scene.Focus();
                window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.None, null); Tick(window);
                AssertSelected(OpticSceneViewPreset.Front);
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                Assert.Null(scene.ActiveViewPreset);
                Assert.DoesNotContain(buttons, item => item.IsChecked == true);
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                AssertSelected(OpticSceneViewPreset.Isometric);
                Assert.Same(scene.Parent, toolbar.Parent);
                Assert.True(VisualBounds(scene).Top < VisualBounds(toolbar).Top);
                Assert.InRange(VisualBounds(toolbar).Left, VisualBounds(scene).Left + 72, VisualBounds(scene).Right);
                Assert.InRange(VisualBounds(toolbar).Right, VisualBounds(scene).Left, VisualBounds(scene).Right - 72);
                Assert.True(VisualBounds(toolbar).Bottom <= VisualBounds(scene).Bottom - 40);
                if (mode == ViewerPresentationMode.SolidModel)
                {
                    var legend = viewer.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "Viewer3DGlassLegend");
                    Assert.True(VisualBounds(toolbar).Bottom <= VisualBounds(legend).Top);
                    Assert.InRange(VisualBounds(legend).Bottom, 0, window.Height);
                }
                Assert.Same(originalScene, scene.Scene);
                Assert.Equal(revision, app.Events.Revision);
                Capture(window, $"{capturePrefix}-reset.png");

                Rect VisualBounds(Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                void BringIntoViewport(Control control)
                {
                    control.BringIntoView(); Tick(window);
                    if (viewer.GetVisualDescendants().OfType<ScrollViewer>()
                        .SingleOrDefault(item => item.Name == "ViewerDiagramScroll") is { } scroll)
                    {
                        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2,
                            control.Bounds.Height / 2), scroll)!.Value;
                        scroll.Offset = new Vector(
                            Math.Clamp(scroll.Offset.X + center.X - scroll.Viewport.Width / 2,
                                0, Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width)),
                            Math.Clamp(scroll.Offset.Y + center.Y - scroll.Viewport.Height / 2,
                                0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
                    }
                    Tick(window); Tick(window);
                }
                async Task Click(Control control)
                {
                    // Fixed sheets scroll in short/narrow panes. Click the visible
                    // control, retaining real hit testing rather than raising Click.
                    BringIntoViewport(control);
                    Point point = default;
                    Visual? hit = null;
                    // The render thread commits scrolled hit-test geometry after
                    // layout. Wait for that geometry, never retry a missed click.
                    for (var attempt = 0; attempt < 200; attempt++)
                    {
                        Tick(window);
                        point = control.TranslatePoint(new Point(control.Bounds.Width / 2,
                            control.Bounds.Height / 2), window)!.Value;
                        hit = window.InputHitTest(point) as Visual;
                        if (hit == control || hit?.GetVisualAncestors().Contains(control) == true) break;
                        await Task.Delay(10);
                    }
                    Assert.InRange(point.X, 0, window.ClientSize.Width);
                    Assert.InRange(point.Y, 0, window.ClientSize.Height);
                    Assert.True(hit == control || hit?.GetVisualAncestors().Contains(control) == true,
                        $"{control.Name} at {point} hit {hit?.GetType().Name}/{(hit as Control)?.Name}; bounds {VisualBounds(control)}; " +
                        $"ancestors {string.Join('>', hit?.GetVisualAncestors().OfType<Control>().Select(item => item.GetType().Name + '/' + item.Name) ?? [])}; " +
                        $"overflow {viewer.GetVisualDescendants().OfType<UniformDiagramPage>().SingleOrDefault()?.HasOverflow}");
                    window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Tick(window);
                }
                void AssertSelected(OpticSceneViewPreset expected)
                {
                    Assert.Equal(expected, scene.ActiveViewPreset);
                    Assert.Equal($"{namePrefix}View{expected}", Assert.Single(buttons, item => item.IsChecked == true).Name);
                }
            }
            finally
            {
                window.Close(); UiLocalization.Apply(previousLanguage); DisplayTypography.Configure(new AppSettings());
            }
            return true;
        }, CancellationToken.None);
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

    private static byte[] RenderPixels(Control control)
    {
        var size = new PixelSize((int)control.Bounds.Width, (int)control.Bounds.Height);
        using var image = new RenderTargetBitmap(size, new Vector(96, 96));
        image.Render(control);
        var stride = size.Width * 4;
        var bytes = new byte[stride * size.Height];
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try { image.CopyPixels(new PixelRect(size), pointer, bytes.Length, stride); Marshal.Copy(pointer, bytes, 0, bytes.Length); }
        finally { Marshal.FreeHGlobal(pointer); }
        return bytes;
    }

    private static int ChangedPixels(byte[] first, byte[] second, int width, PixelRect region)
    {
        Assert.Equal(first.Length, second.Length);
        var changed = 0;
        for (var y = region.Y; y < region.Bottom; y++)
            for (var x = region.X; x < region.Right; x++)
            {
                var offset = (y * width + x) * 4;
                if (!first.AsSpan(offset, 4).SequenceEqual(second.AsSpan(offset, 4))) changed++;
            }
        return changed;
    }

    private sealed class Viewer3DRenderTheoryAttribute : TheoryAttribute
    {
        public Viewer3DRenderTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory)))
                Skip = "Set the viewer3d capture directory to run independent Skia pixel checks.";
        }
    }
}
