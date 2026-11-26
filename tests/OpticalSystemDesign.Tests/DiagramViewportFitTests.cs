using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
public sealed class DiagramViewportFitTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Fact]
    public async Task AnalysisFitsBothWindowSizesAndModeSurvivesRefreshWithoutChangingExport()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramViewportFitTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard });
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), "FFT PSF Cross Section");
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "FFT PSF Cross Section",
                new Dictionary<string, string> { ["Sampling"] = "64 x 64" }));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var view = result.View;
            var generatedAt = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(8));
            panel.PresentResult(view, app.Documents.GetSnapshot(), generatedAt);
            var window = new Window { Width = 1600, Height = 1000, Content = panel };
            try
            {
                window.Show(); Render(window);
                var page = panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                var sheet = Assert.IsType<UniformDiagramPage>(page.Child);
                var source = app.Documents.GetRecoveryState();
                var series = sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Single().Series;
                var control = panel.GetVisualDescendants().OfType<DiagramViewportModeButton>().Single();
                using var reference = panel.CapturePlotImage(); var pixels = PixelHash(reference);
                foreach (var size in new[] { (1600, 1000), (520, 360), (1900, 1150), (800, 600) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Render(window);
                    AssertContained(page); Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                    Assert.Same(series, sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Single().Series);
                    Assert.Equal(source, app.Documents.GetRecoveryState());
                    using var image = panel.CapturePlotImage();
                    Assert.Equal(new PixelSize(1920, 1080), image.PixelSize);
                    Assert.Equal(pixels, PixelHash(image));
                    Capture(window, $"analysis-fit-{size.Item1}x{size.Item2}");
                }
                SelectMode(control, "原始大小（100%）"); Render(window);
                Assert.False(page.FitToViewport); Assert.Equal(1, page.ContentScale);
                Assert.True(page.Extent.Width > page.Viewport.Width);
                panel.PresentResult(view, app.Documents.GetSnapshot(), generatedAt); Render(window);
                page = panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                Assert.False(page.FitToViewport); Assert.False(control.IsFitToViewport);
                SelectMode(control, "适应窗口"); Render(window); AssertContained(page);
                using var restored = panel.CapturePlotImage();
                Assert.Equal(pixels, PixelHash(restored));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional)]
    [InlineData(SceneDimension.ThreeDimensional)]
    public async Task LayoutUsesTheSameFitHostAndKeepsCameraAndExportAcrossResize(SceneDimension dimension)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramViewportFitTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard });
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension);
            var window = new Window { Width = 1500, Height = 1050, Content = viewer };
            try
            {
                window.Show();
                for (var i = 0; i < 150; i++)
                {
                    await Task.Delay(10); Render(window);
                    if (viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) break;
                }
                var host = viewer.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                var scene = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                Assert.NotNull(scene.Scene); var originalScene = scene.Scene;
                if (dimension == SceneDimension.ThreeDimensional) { scene.SetViewPreset(OpticSceneViewPreset.Front); Render(window); }
                var camera = scene.OrientationAxesForRendering();
                using var reference = viewer.CaptureViewerImage(); var pixels = PixelHash(reference);
                foreach (var size in new[] { (1500, 1050), (600, 440), (1900, 1200), (900, 650) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Render(window);
                    AssertContained(host, dimension == SceneDimension.TwoDimensional);
                    if (dimension == SceneDimension.ThreeDimensional) Assert.Equal(new Size(1200, 900), host.Child!.Bounds.Size);
                    var export = viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage");
                    Assert.InRange(export.TransformToVisual(viewer)!.Value.M11, .999, 1.001);
                    Assert.Same(originalScene, scene.Scene); Assert.Equal(camera, scene.OrientationAxesForRendering());
                    using var image = viewer.CaptureViewerImage();
                    Assert.Equal(new PixelSize(1920, 1080), image.PixelSize);
                    Assert.Equal(pixels, PixelHash(image));
                    Capture(window, $"layout-{dimension}-{size.Item1}x{size.Item2}");
                }
                var control = viewer.GetVisualDescendants().OfType<DiagramViewportModeButton>().Single();
                SelectMode(control, "原始大小（100%）"); Render(window);
                Assert.Equal(1, host.ContentScale); Assert.True(host.Extent.Width > host.Viewport.Width);
                SelectMode(control, "适应窗口"); Render(window); AssertContained(host, dimension == SceneDimension.TwoDimensional);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task BlueNotchUsesTheWholePaneWithoutChangingPrintAspectSceneOrUserView(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramViewportFitTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard });
            DisplayTypography.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var example = SequentialExampleCatalog.Examples.Single(e => e.RelativePath.Contains("blue notch filter", StringComparison.OrdinalIgnoreCase));
            await app.Documents.OpenAsync(example.FilePath);
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), SceneDimension.TwoDimensional);
            var window = new Window { Width = 1000, Height = 450, Content = viewer };
            try
            {
                window.Show();
                for (var pass = 0; pass < 300; pass++)
                {
                    await Task.Delay(10); Render(window);
                    if (viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) break;
                }
                var scene = viewer.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                var host = viewer.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                var mode = viewer.GetVisualDescendants().OfType<DiagramViewportModeButton>().Single();
                var source = scene.Scene; Assert.NotNull(source);
                var document = app.Documents.GetRecoveryState();
                scene.RayColorMode = OpticSceneRayColorMode.Wavelength;
                Render(window);
                using var reference = viewer.CaptureViewerImage(1); var pixels = PixelHash(reference);
                CaptureImage(reference, $"blue-notch-{theme}-print-4-3");
                foreach (var size in new[] { (1000, 450), (1600, 470), (650, 900), (1000, 450) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Render(window);
                    Capture(window, $"blue-notch-{theme}-{size.Item1}x{size.Item2}");
                    AssertContained(host, fillsViewport: true);
                    Assert.Same(source, scene.Scene); Assert.Equal(document, app.Documents.GetRecoveryState());
                    using var image = viewer.CaptureViewerImage(1);
                    Assert.Equal(pixels, PixelHash(image));
                    // Parchment intentionally has an irregular transparent edge.
                    if (ThemeDrawingSurface.Material(scene) is null) ExportImageAssertions.HasCompleteBorder(image);
                    if (theme == "Light" && size.Item1 >= 1000)
                        AssertRayWidth(window, scene, .72);
                }
                scene.Focus();
                window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); Render(window);
                var viewport = (SceneViewport)typeof(OpticSceneControl).GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var view = (viewport.Zoom, viewport.Pan);
                Assert.True(view.Zoom > 1); Assert.NotEqual(default, view.Pan);
                using var moved = viewer.CaptureViewerImage(1); var movedPixels = PixelHash(moved);
                Assert.NotEqual(pixels, movedPixels);
                SelectMode(mode, "原始大小（100%）"); Render(window);
                Assert.Equal(new Size(1200, 900), host.Child!.Bounds.Size); Assert.Equal(1, host.ContentScale);
                SelectMode(mode, "适应窗口"); window.Width = 1500; Render(window);
                AssertContained(host, fillsViewport: true);
                Assert.Equal(view, (viewport.Zoom, viewport.Pan));
                using var after = viewer.CaptureViewerImage(1); Assert.Equal(movedPixels, PixelHash(after));
                ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Wide }); Render(window);
                using var wide = viewer.CaptureViewerImage(1);
                CaptureImage(wide, $"blue-notch-{theme}-print-16-9");
                Assert.True(wide.PixelSize.Width > after.PixelSize.Width);
                Assert.Equal(after.PixelSize.Height, wide.PixelSize.Height);
                Assert.Equal(view, (viewport.Zoom, viewport.Pan));
            }
            finally { window.Close(); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static void AssertRayWidth(Window window, OpticSceneControl scene, double minimumFraction)
    {
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        var area = new Rect(scene.Bounds.Size).TransformToAABB(scene.TransformToVisual(window)!.Value);
        var bytes = new byte[image.PixelSize.Width * image.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(image.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, image.PixelSize.Width * 4); }
        finally { handle.Free(); }
        var left = image.PixelSize.Width; var right = -1;
        for (var y = (int)Math.Max(area.Top + 65, 0); y < Math.Min(area.Bottom, image.PixelSize.Height); y++)
            for (var x = (int)Math.Max(area.Left, 0); x < Math.Min(area.Right, image.PixelSize.Width); x++)
            {
                var p = (y * image.PixelSize.Width + x) * 4;
                if (Math.Max(bytes[p], Math.Max(bytes[p + 1], bytes[p + 2]))
                    - Math.Min(bytes[p], Math.Min(bytes[p + 1], bytes[p + 2])) > 40)
                { left = Math.Min(left, x); right = Math.Max(right, x); }
            }
        Assert.True((right - left) / area.Width >= minimumFraction,
            $"Coloured ray width {right - left} does not fill {minimumFraction:P0} of drawing viewport {area.Width}.");
    }

    private static void AssertContained(ResponsiveAnalysisPlotHost host, bool fillsViewport = false)
    {
        Assert.True(host.FitToViewport);
        var bounds = new Rect(host.Child!.Bounds.Size).TransformToAABB(host.Child.TransformToVisual(host)!.Value);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1, bounds.ToString());
        Assert.True(bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1,
            $"Sheet {bounds} exceeds viewport {host.Viewport}");
        if (fillsViewport)
        {
            // Decorative perimeter belongs to the fitted page, outside the drawing.
            var frame = host.GetVisualDescendants().OfType<AnalysisImageFrame>().Single();
            var framedBounds = new Rect(frame.Bounds.Size).TransformToAABB(frame.TransformToVisual(host)!.Value);
            Assert.True(framedBounds.Width >= host.Viewport.Width * .985, $"Drawing frame {framedBounds} leaves horizontal gutters in {host.Viewport}.");
            Assert.True(framedBounds.Height >= host.Viewport.Height * .985, $"Drawing frame {framedBounds} leaves vertical gutters in {host.Viewport}.");
        }
        else Assert.InRange(Math.Abs(bounds.Width / bounds.Height - 4d / 3), 0, .001);
        Assert.InRange(Math.Abs(bounds.Center.X - host.Viewport.Width / 2), 0, 1);
        Assert.InRange(Math.Abs(bounds.Center.Y - host.Viewport.Height / 2), 0, 1);
    }
    private static void SelectMode(DiagramViewportModeButton button, string label) =>
        Assert.IsType<MenuFlyout>(button.Flyout).Items.OfType<MenuItem>()
            .Single(i => Avalonia.Automation.AutomationProperties.GetName(i) == label)
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static void Render(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static void Capture(Window window, string name)
    {
        using var bitmap = window.CaptureRenderedFrame();
        if (bitmap is not null) CaptureImage(bitmap, name);
    }
    private static void CaptureImage(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_DIAGRAM_FIT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
    private static byte[] PixelHash(Bitmap bitmap)
    {
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { handle.Free(); }
        return SHA256.HashData(bytes);
    }
}
