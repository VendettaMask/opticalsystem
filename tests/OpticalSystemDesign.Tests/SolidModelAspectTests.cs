using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SolidModelAspectTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task SolidModelRetainsThemeMaterialAndCoversOuterViewportGaps(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SolidModelAspectTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings { DiagramAspectRatio = "16:9" };
            ImageExportResolution.Configure(settings);
            DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(),
                SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel);
            var window = new Window { Width = 1900, Height = 1100, Content = viewer };
            try
            {
                window.Show(); await Ready(window, viewer);
                var scene = Assert.Single(viewer.GetVisualDescendants().OfType<OpticSceneControl>());
                var host = Assert.Single(viewer.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var sheet = Assert.IsType<UniformDiagramPage>(host.Child);
                var frame = Assert.Single(host.GetVisualDescendants().OfType<AnalysisImageFrame>());
                var pose = CameraState(scene);
                var source = scene.Scene;
                using var image = viewer.CaptureViewerImage(1);
                Save(image, $"solid-backdrop-{theme}-export.png");
                var inset = (int)ThemeDrawingSurface.FrameInset(viewer);
                if (theme != "Isekai")
                {
                    AssertDarkPixel(image, inset + 50, inset + 300);
                    AssertDarkPixel(image, image.PixelSize.Width - inset - 51, inset + 300);
                }

                foreach (var (width, height, fit) in new[]
                {
                    (1900, 1100, true), (900, 1200, true), (1900, 1100, false)
                })
                {
                    window.Width = width; window.Height = height;
                    host.FitToViewport = fit;
                    Tick(window);
                    using var backdrop = new RenderTargetBitmap(
                        new PixelSize((int)Math.Ceiling(host.Bounds.Width), (int)Math.Ceiling(host.Bounds.Height)),
                        new Vector(96, 96));
                    backdrop.Render(host);
                    // Use the arranged sheet bounds so themed frames stay outside the samples.
                    var sheetBounds = new Rect(sheet.Bounds.Size).TransformToAABB(sheet.TransformToVisual(host)!.Value);
                    var y = (int)sheetBounds.Center.Y;
                    if (theme != "Isekai")
                    {
                        AssertDarkPixel(backdrop, (int)(sheetBounds.Left + sheetBounds.Width / 16), y);
                        AssertDarkPixel(backdrop, (int)(sheetBounds.Right - sheetBounds.Width / 16), y);
                    }
                    var frameBounds = new Rect(frame.Bounds.Size).TransformToAABB(frame.TransformToVisual(host)!.Value);
                    if (frameBounds.Left > 8) AssertDarkPixel(backdrop, (int)(frameBounds.Left / 2), y);
                    if (frameBounds.Top > 8)
                        AssertDarkPixel(backdrop, backdrop.PixelSize.Width / 2, (int)(frameBounds.Top / 2));
                    if (fit) Save(backdrop, $"solid-backdrop-{theme}-{width}-viewport.png");
                    if (fit && width == 1900)
                    {
                        using var live = window.CaptureRenderedFrame();
                        if (live is not null) Save(live, $"solid-backdrop-{theme}-window.png");
                    }
                    Assert.Same(source, scene.Scene);
                    Assert.Equal(pose, CameraState(scene));
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static void AssertDarkPixel(Bitmap image, int x, int y)
    {
        var pixel = new byte[4];
        var pinned = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(x, y, 1, 1), pinned.AddrOfPinnedObject(), 4, 4); }
        finally { pinned.Free(); }
        Assert.Equal(255, pixel[3]);
        Assert.True(pixel.Take(3).All(channel => channel < 80), $"Light backdrop at ({x}, {y}): {string.Join(',', pixel)}");
    }

    [Theory]
    [InlineData("Light", OpticSceneRenderMode.Solid, false, true)]
    [InlineData("Lumen", OpticSceneRenderMode.Solid, false, true)]
    [InlineData("Isekai", OpticSceneRenderMode.Solid, false, true)]
    [InlineData("Light", OpticSceneRenderMode.Transparent, false, true)]
    [InlineData("Lumen", OpticSceneRenderMode.Wireframe, false, true)]
    [InlineData("Isekai", OpticSceneRenderMode.Transparent, true, true)]
    [InlineData("Light", OpticSceneRenderMode.Solid, true, false)]
    public async Task ViewportExpandsAcrossPageWhileKeepingGeometryCameraAndExportPixels(
        string theme, OpticSceneRenderMode renderMode, bool cutaway, bool raysVisible)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SolidModelAspectTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings(); ImageExportResolution.Configure(settings); DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(),
                SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel);
            var window = new Window { Width = 1900, Height = 1100, Content = viewer };
            try
            {
                window.Show();
                await Ready(window, viewer);
                var scene = Assert.Single(viewer.GetVisualDescendants().OfType<OpticSceneControl>());
                var host = Assert.Single(viewer.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var sheet = Assert.IsType<UniformDiagramPage>(host.Child);
                Assert.Equal("Auto", DiagramSheetLayout.AspectRatio);
                Assert.Equal(ExpectedWidth(host, "Auto"), sheet.Width);
                Assert.Empty(sheet.GetVisualDescendants().OfType<CompanyLogoControl>());
                var source = scene.Scene; Assert.NotNull(source?.ThreeDimensional);
                var model = source!.ThreeDimensional!; var rays = model.Rays;
                var sourceJson = JsonSerializer.Serialize(source);
                var document = app.Documents.GetRecoveryState(); var revision = app.Events.Revision;
                viewer.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "Viewer3DRenderMode").SelectedIndex = renderMode switch
                { OpticSceneRenderMode.Transparent => 1, OpticSceneRenderMode.Wireframe => 2, _ => 0 };
                viewer.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "Viewer3DCutaway").IsChecked = cutaway;
                viewer.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "Viewer3DShowRays").IsChecked = raysVisible;
                scene.SetViewPreset(OpticSceneViewPreset.Right);
                SendKey(scene, Key.Add); SendKey(scene, Key.Right); SendKey(scene, Key.Down, KeyModifiers.Shift);
                Tick(window);
                var pose = CameraState(scene); Assert.NotEqual(1, pose.Zoom); Assert.NotEqual(default, pose.Pan);
                var materials = scene.GlassMaterials.ToArray();
                foreach (var (mode, width) in new[]
                {
                    ("Auto", 1900), ("Auto", 900), ("Auto", 1900),
                    ("16:9", 900), ("16:9", 1900),
                    ("4:3", 1900), ("4:3", 900), ("Auto", 1900)
                })
                {
                    settings.DiagramAspectRatio = mode; ImageExportResolution.Configure(settings);
                    window.Width = width; Tick(window);
                    var expectedWidth = ExpectedWidth(host, mode);
                    Assert.Equal(expectedWidth, sheet.Width); Assert.False(sheet.HasOverflow);
                    Assert.Same(source, scene.Scene); Assert.Same(model, scene.Scene!.ThreeDimensional);
                    Assert.Same(rays, scene.Scene.ThreeDimensional!.Rays);
                    Assert.Equal(sourceJson, JsonSerializer.Serialize(scene.Scene));
                    Assert.Equal(document, app.Documents.GetRecoveryState()); Assert.Equal(revision, app.Events.Revision);
                    Assert.Equal(pose, CameraState(scene));
                    Assert.InRange(scene.Bounds.Width, sheet.Width - 64, sheet.Width);
                    Assert.Equal(renderMode, scene.RenderMode); Assert.Equal(cutaway, scene.CutawayEnabled);
                    Assert.Equal(raysVisible, scene.ShowRays); Assert.Equal(materials, scene.GlassMaterials);
                    using var reference = ScenePixels(scene);
                    var completeRows = (int)Math.Floor(scene.Bounds.Height);
                    var opaqueReference = Hash(reference, new PixelRect(0, 0, reference.PixelSize.Width, completeRows));
                    if (mode == "Auto")
                    {
                        var modelBounds = new Rect(scene.Bounds.Size).TransformToAABB(scene.TransformToVisual(host)!.Value);
                        var insetInWindow = ThemeDrawingSurface.FrameInset(viewer) * host.ContentScale;
                        Assert.InRange(modelBounds.Left, 0, insetInWindow + 4);
                        Assert.InRange(host.Viewport.Width - modelBounds.Right, 0, insetInWindow + 4);
                    }
                    var bounds = new Rect(sheet.Bounds.Size).TransformToAABB(sheet.TransformToVisual(host)!.Value);
                    Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1);
                    using var image = viewer.CaptureViewerImage(1);
                    var inset = (int)ThemeDrawingSurface.FrameInset(viewer);
                    Assert.Equal((int)expectedWidth + 2 * inset, image.PixelSize.Width);
                    var modelRect = new PixelRect((image.PixelSize.Width - reference.PixelSize.Width) / 2,
                        inset, reference.PixelSize.Width, completeRows);
                    var exportedPixels = Hash(image, modelRect);
                    if (!opaqueReference.SequenceEqual(exportedPixels))
                    {
                        Save(image, $"failure-{theme}-{renderMode}-{cutaway}-{raysVisible}-export.png");
                        Save(reference, $"failure-{theme}-{renderMode}-{cutaway}-{raysVisible}-source.png");
                        Console.WriteLine($"{theme}/{renderMode}: scene {scene.Bounds}, source {reference.PixelSize}, image {image.PixelSize}, model {modelRect}");
                    }
                    Assert.Equal(opaqueReference, exportedPixels);
                    Assert.Equal(pose, CameraState(scene));
                    Assert.InRange(scene.Bounds.Width, sheet.Width - 64, sheet.Width);
                    if (mode == "Auto")
                    {
                        Save(image, $"model-{theme}-{renderMode}-{cutaway}-{raysVisible}-{(width == 1900 ? "wide" : "narrow")}.png");
                        using var live = window.CaptureRenderedFrame();
                        if (live is not null) Save(live, $"model-{theme}-{renderMode}-{cutaway}-{raysVisible}-{(width == 1900 ? "wide" : "narrow")}-window.png");
                    }
                }
                host.FitToViewport = false; Tick(window);
                window.Width = 900; window.Height = 600; Tick(window);
                Assert.Equal(1, host.ContentScale); Assert.Equal(ExpectedWidth(host, "Auto"), sheet.Width);
                Assert.Equal(pose, CameraState(scene));
                Assert.InRange(scene.Bounds.Width, sheet.Width - 64, sheet.Width);
                Assert.True(host.Extent.Height > host.Viewport.Height);
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeControlsStayReadableAndExportsExcludeSettingsAndFocus()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SolidModelAspectTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new()); DisplayTypography.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(),
                SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel);
            var window = new Window { Width = 800, Height = 1000, Content = viewer };
            try
            {
                window.Show(); await Ready(window, viewer);
                var scene = Assert.Single(viewer.GetVisualDescendants().OfType<OpticSceneControl>());
                var source = scene.Scene;
                var pose = CameraState(scene);
                var controls = viewer.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SolidModelViewPresets");
                Assert.InRange(controls.TransformToVisual(viewer)!.Value.M11, .999, 1.001);
                var legend = viewer.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Viewer3DGlassLegend");
                Assert.True(legend.IsVisible);
                Assert.True(legend.TranslatePoint(default, scene)!.Value.Y >= scene.Bounds.Height);
                var summary = (Border)typeof(ViewerPanel).GetField("_summaryBar", Private)!.GetValue(viewer)!;
                Assert.InRange(summary.Bounds.Height, 1, 159);
                using var original = viewer.CaptureViewerImage(1); var pixels = Hash(original);
                scene.Focus(); Tick(window);
                using var focused = viewer.CaptureViewerImage(1); Assert.Equal(pixels, Hash(focused));
                var toggle = viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Content is LocalIconLabel);
                toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Tick(window);
                using var settings = viewer.CaptureViewerImage(1); Assert.Equal(pixels, Hash(settings));
                window.Width = 1900; Tick(window);
                Assert.InRange(controls.TransformToVisual(viewer)!.Value.M11, .999, 1.001);
                Assert.Same(source, scene.Scene); Assert.Equal(pose, CameraState(scene));
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static double ExpectedWidth(ResponsiveAnalysisPlotHost host, string mode)
    {
        if (mode != "Auto") return mode == "16:9" ? 1600 : 1200;
        var frame = host.GetVisualDescendants().OfType<AnalysisImageFrame>().Single();
        var inset = frame.DrawingInset;
        return Math.Round(Math.Clamp(900 * (host.Viewport.Width - 2 * inset) / (host.Viewport.Height - 2 * inset), 320, 7200));
    }

    private sealed record Camera(double Yaw, double Pitch, double Zoom, Vector Pan);
    private static Camera CameraState(OpticSceneControl scene)
    {
        var viewport = (SceneViewport)typeof(OpticSceneControl).GetField("_viewport", Private)!.GetValue(scene)!;
        return new((double)typeof(OpticSceneControl).GetField("_yaw", Private)!.GetValue(scene)!,
            (double)typeof(OpticSceneControl).GetField("_pitch", Private)!.GetValue(scene)!, viewport.Zoom, viewport.Pan);
    }
    private sealed class ModelPixels(OpticSceneControl scene) : Control
    {
        public override void Render(DrawingContext context) => scene.RenderImageContent(context);
    }
    private static RenderTargetBitmap ScenePixels(OpticSceneControl scene)
    {
        var canvas = new ModelPixels(scene) { Width = scene.Bounds.Width, Height = scene.Bounds.Height };
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(canvas.DesiredSize));
        var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height)), new Vector(96, 96));
        image.Render(canvas); return image;
    }
    private static void SendKey(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static async Task Ready(Window window, ViewerPanel viewer)
    {
        for (var i = 0; i < 200; i++)
        {
            await Task.Delay(10); Tick(window);
            if (viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) return;
        }
        throw new TimeoutException("No completed solid-model scene.");
    }
    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static byte[] Hash(Bitmap bitmap, PixelRect? region = null)
    {
        var bounds = region ?? new PixelRect(bitmap.PixelSize);
        var bytes = new byte[bounds.Width * bounds.Height * 4];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(bounds, pinned.AddrOfPinnedObject(), bytes.Length, bounds.Width * 4); }
        finally { pinned.Free(); }
        return SHA256.HashData(bytes);
    }
    private static void Save(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SOLID_ASPECT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
