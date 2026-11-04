using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

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
                    AssertContained(host); Assert.Equal(new Size(1200, 900), host.Child!.Bounds.Size);
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
                SelectMode(control, "适应窗口"); Render(window); AssertContained(host);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertContained(ResponsiveAnalysisPlotHost host)
    {
        Assert.True(host.FitToViewport);
        var bounds = new Rect(host.Child!.Bounds.Size).TransformToAABB(host.Child.TransformToVisual(host)!.Value);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1, bounds.ToString());
        Assert.True(bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1,
            $"Sheet {bounds} exceeds viewport {host.Viewport}");
        Assert.InRange(Math.Abs(bounds.Width / bounds.Height - 4d / 3), 0, .001);
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
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_DIAGRAM_FIT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame(); bitmap!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
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
