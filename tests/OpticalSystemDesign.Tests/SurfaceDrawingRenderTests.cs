using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SurfaceDrawingRenderTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(false, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Transparent, false)]
    [InlineData(true, true, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, true)]
    public async Task ZeroOpacityRemovesAllSurfaceAndElementPixelsAndResetRestoresThem(
        bool threeD, bool solid, OpticSceneRenderMode mode, bool cutaway)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(threeD ? SceneDimension.ThreeDimensional : SceneDimension.TwoDimensional);
        var empty = ClearGeometry(source);
        SceneDto Opacity(double value) => source with
        {
            TwoDimensional = source.TwoDimensional is { } two ? two with
            {
                Surfaces = two.Surfaces.Select(s => s with { Opacity = value }).ToArray(),
                LensElements = two.LensElements.Select(e => e with { Opacity = value }).ToArray(),
                LensEdges = two.LensEdges.Select(e => e with { Opacity = value }).ToArray()
            } : null,
            ThreeDimensional = source.ThreeDimensional is { } three ? three with
            {
                Surfaces = three.Surfaces.Select(s => s with { Opacity = value }).ToArray(),
                LensElements = three.LensElements.Select(e => e with { Opacity = value }).ToArray()
            } : null
        };
        await RenderCases(source, threeD, solid, mode, cutaway, (view, window) =>
        {
            var baseline = Pixels(view);
            Present(view, window, empty); var blank = Pixels(view);
            Assert.NotEqual(baseline, blank);
            Present(view, window, Opacity(0));
            Assert.Equal(blank, Pixels(view, $"zero-{threeD}-{solid}-{mode}-{cutaway}.png"));
            Present(view, window, Opacity(0.35));
            var partial = Pixels(view, $"opacity35-{threeD}-{solid}-{mode}-{cutaway}.png");
            Assert.NotEqual(blank, partial); Assert.NotEqual(baseline, partial);
            Present(view, window, source); Assert.Equal(baseline, Pixels(view));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HiddenSelectedSurfaceStaysHiddenWhileItsExplicitAxesAndOriginRemainVisible(bool threeD, bool solid)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(threeD ? SceneDimension.ThreeDimensional : SceneDimension.TwoDimensional);
        var empty = ClearGeometry(source);
        var z = threeD ? (source.ThreeDimensional!.ZMin + source.ThreeDimensional.ZMax) / 2
            : (source.TwoDimensional!.ZMin + source.TwoDimensional.ZMax) / 2;
        SceneDto Visible(bool hidden, bool axes, bool origin) => empty with
        {
            TwoDimensional = empty.TwoDimensional is { } two ? two with
            {
                Surfaces = [source.TwoDimensional!.Surfaces.Single(s => s.SurfaceNumber == 1) with
                {
                    HideSurface = hidden,
                    LocalFrame = new(new(z, 0), new(z, 0), new(z, 5), new(z + 5, 0), axes, origin)
                }]
            } : null,
            ThreeDimensional = empty.ThreeDimensional is { } three ? three with
            {
                Surfaces = [source.ThreeDimensional!.Surfaces.Single(s => s.SurfaceNumber == 1) with
                {
                    HideSurface = hidden,
                    LocalFrame = new(new(0, 0, z), new(5, 0, z), new(0, 5, z), new(0, 0, z + 5), axes, origin)
                }]
            } : null
        };
        await RenderCases(empty, threeD, solid, OpticSceneRenderMode.Wireframe, false, (view, window) =>
        {
            view.HighlightedSurfaceNumber = 1; Tick(window);
            var blank = Pixels(view);
            Present(view, window, Visible(false, false, false)); Assert.NotEqual(blank, Pixels(view));
            Present(view, window, Visible(true, false, false)); Assert.Equal(blank, Pixels(view));
            Present(view, window, Visible(true, true, false)); var axes = Pixels(view);
            Present(view, window, Visible(true, false, true)); var origin = Pixels(view);
            Present(view, window, Visible(true, true, true)); var both = Pixels(view, $"local-frame-{threeD}-{solid}.png");
            Assert.NotEqual(blank, axes); Assert.NotEqual(blank, origin);
            Assert.NotEqual(axes, both); Assert.NotEqual(origin, both);
            var transparent = Visible(true, true, true);
            transparent = transparent with
            {
                TwoDimensional = transparent.TwoDimensional is { } two ? two with
                { Surfaces = two.Surfaces.Select(s => s with { Opacity = 0 }).ToArray() } : null,
                ThreeDimensional = transparent.ThreeDimensional is { } three ? three with
                { Surfaces = three.Surfaces.Select(s => s with { Opacity = 0 }).ToArray() } : null
            };
            Present(view, window, transparent); Assert.Equal(both, Pixels(view));
        });
    }

    [Theory]
    [InlineData(false, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, true)]
    public async Task HideEdgesAffectsTheActualConnectionsAndSideWalls(bool threeD, bool solid,
        OpticSceneRenderMode mode, bool cutaway)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(threeD ? SceneDimension.ThreeDimensional : SceneDimension.TwoDimensional);
        var hidden = source with
        {
            TwoDimensional = source.TwoDimensional is { } two ? two with
            { Surfaces = two.Surfaces.Select(s => s with { HideEdges = true }).ToArray() } : null,
            ThreeDimensional = source.ThreeDimensional is { } three ? three with
            { Surfaces = three.Surfaces.Select(s => s with { HideEdges = true }).ToArray() } : null
        };
        await RenderCases(source, threeD, solid, mode, cutaway, (view, window) =>
        {
            var before = Pixels(view);
            Present(view, window, hidden);
            Assert.NotEqual(before, Pixels(view, $"hide-edges-{threeD}-{solid}-{mode}-{cutaway}.png"));
            Present(view, window, source); Assert.Equal(before, Pixels(view));
        });
    }

    private static SceneDto ClearGeometry(SceneDto scene) => scene with
    {
        TwoDimensional = scene.TwoDimensional is { } two ? two with { Surfaces = [], LensElements = [], LensEdges = [], Rays = [] } : null,
        ThreeDimensional = scene.ThreeDimensional is { } three ? three with { Surfaces = [], LensElements = [], Rays = [] } : null
    };

    [Theory]
    [InlineData(false, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, false, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, true, OpticSceneRenderMode.Wireframe, false)]
    [InlineData(true, true, OpticSceneRenderMode.Solid, true)]
    [InlineData(true, true, OpticSceneRenderMode.Transparent, true)]
    public async Task HideEdgesPreservesAnIndependentSurfaceOutlineAndImageReference(
        bool threeD, bool solid, OpticSceneRenderMode mode, bool imageSurface)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(threeD ? SceneDimension.ThreeDimensional : SceneDimension.TwoDimensional);
        var empty = ClearGeometry(source);
        var isolated = empty with
        {
            TwoDimensional = empty.TwoDimensional is { } two ? two with
            { Surfaces = [source.TwoDimensional!.Surfaces.Single(s => s.SurfaceNumber == 1) with { SurfaceColor = "#D62728" }] } : null,
            ThreeDimensional = empty.ThreeDimensional is { } three ? three with
            { Surfaces = [source.ThreeDimensional!.Surfaces.Single(s => imageSurface ? s.IsImageSurface : s.SurfaceNumber == 1) with { SurfaceColor = "#D62728" }] } : null
        };
        var hidden = isolated with
        {
            TwoDimensional = isolated.TwoDimensional is { } isolatedTwo ? isolatedTwo with
            { Surfaces = isolatedTwo.Surfaces.Select(s => s with { HideEdges = true }).ToArray() } : null,
            ThreeDimensional = isolated.ThreeDimensional is { } isolatedThree ? isolatedThree with
            { Surfaces = isolatedThree.Surfaces.Select(s => s with { HideEdges = true }).ToArray() } : null
        };
        await RenderCases(isolated, threeD, solid, mode, false, (view, window) =>
        {
            var before = Pixels(view);
            Present(view, window, empty); Assert.NotEqual(before, Pixels(view));
            Present(view, window, hidden); Assert.Equal(before, Pixels(view));
        });
    }

    [Theory]
    [InlineData(false, OpticSceneRenderMode.Solid)]
    [InlineData(true, OpticSceneRenderMode.Solid)]
    [InlineData(true, OpticSceneRenderMode.Transparent)]
    public async Task HideEdgesPreservesBothElementRimsWhenThereAreNoConnectingFaces(
        bool solid, OpticSceneRenderMode mode)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        var model = Assert.IsType<Scene3Dto>(source.ThreeDimensional);
        var numbers = model.LensElements.SelectMany(e => new[] { e.FrontSurfaceNumber, e.BackSurfaceNumber }).ToHashSet();
        var rimsOnly = source with
        {
            ThreeDimensional = model with
            {
                Surfaces = model.Surfaces.Where(s => numbers.Contains(s.SurfaceNumber)).ToArray(),
                LensElements = model.LensElements.Select(e => e with { FrontFaces = [], BackFaces = [], Opacity = 0 }).ToArray()
            }
        };
        var hiddenEdges = rimsOnly with
        {
            ThreeDimensional = rimsOnly.ThreeDimensional! with
            { Surfaces = rimsOnly.ThreeDimensional!.Surfaces.Select(s => s with { HideEdges = true }).ToArray() }
        };
        await RenderCases(rimsOnly, true, solid, mode, false, (view, window) =>
        {
            var before = Pixels(view);
            Present(view, window, ClearGeometry(rimsOnly)); Assert.NotEqual(before, Pixels(view));
            Present(view, window, hiddenEdges); Assert.Equal(before, Pixels(view));
        });
    }

    [Theory]
    [InlineData(false, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, OpticSceneRenderMode.Solid, false)]
    [InlineData(true, OpticSceneRenderMode.Transparent, false)]
    [InlineData(true, OpticSceneRenderMode.Solid, true)]
    public async Task HiddenElementFacesAndEdgesCannotBeRedrawnByCachedSolidMeshes(
        bool solid, OpticSceneRenderMode mode, bool cutaway)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var source = await app.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        var model = Assert.IsType<Scene3Dto>(source.ThreeDimensional);
        var hidden = source with
        {
            ThreeDimensional = model with
            { Surfaces = model.Surfaces.Select(s => s with { HideSurface = true, HideEdges = true }).ToArray() }
        };
        await RenderCases(source, true, solid, mode, cutaway, (view, window) =>
        {
            view.HighlightedSurfaceNumber = 1; Tick(window);
            var before = Pixels(view);
            Present(view, window, ClearGeometry(source)); var blank = Pixels(view);
            Assert.NotEqual(before, blank);
            Present(view, window, hidden); Assert.Equal(blank, Pixels(view));
        });
    }

    private static async Task RenderCases(SceneDto scene, bool threeD, bool solid, OpticSceneRenderMode mode,
        bool cutaway, Action<OpticSceneControl, Window> cases)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SurfaceDrawingRenderTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            var view = new OpticSceneControl
            {
                Scene = scene,
                ShowRays = false,
                RenderMode = mode,
                CutawayEnabled = cutaway,
                ViewMode = threeD ? OpticSceneViewMode.ThreeDimensional : OpticSceneViewMode.TwoDimensional,
                VisualStyle = solid ? OpticSceneVisualStyle.SolidModel : OpticSceneVisualStyle.OpticalLayout
            };
            var window = new Window { Width = 1000, Height = 660, Content = view };
            try { window.Show(); Tick(window); cases(view, window); }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Present(OpticSceneControl view, Window window, SceneDto scene)
    { view.Scene = scene; view.InvalidateVisual(); Tick(window); }

    private static void Tick(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }

    private static string Pixels(Control control, string? file = null)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(control.Bounds.Width),
            (int)Math.Ceiling(control.Bounds.Height)), new Vector(96, 96));
        image.Render(control);
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SURFACE_DRAWING_CAPTURE_DIR");
        if (directory is not null && file is not null)
        { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, file), stream.ToArray()); }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
