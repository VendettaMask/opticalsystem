using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class DiagramAutomaticAspectTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    public async Task OneRealPsfKeepsAxesDataAndZoomAcrossAutomaticAndManualLayouts(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAutomaticAspectTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings();
            Assert.Equal("Auto", settings.DiagramAspectRatio);
            ImageExportResolution.Configure(settings); DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(SequentialExampleCatalog.Examples.Single(e =>
                e.RelativePath == "Objectives/Double Gauss 28 degree field.zmx").FilePath);
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "FFT PSF Cross Section",
                new Dictionary<string, string> { ["Sampling"] = "64 x 64" }));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var source = app.Documents.GetRecoveryState(); var data = JsonSerializer.Serialize(result.View);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "FFT PSF Cross Section");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new(2026, 11, 4, 20, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1900, Height = 1100, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var host = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var page = Assert.IsType<UniformDiagramPage>(host.Child);
                var plot = Assert.Single(page.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var series = plot.Series; var options = plot.PlotOptions;
                var rendered = typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", Private)!;
                var zoomField = typeof(AnalysisPlotControl).GetField("_viewport", Private)!;
                var axes = rendered.GetValue(plot); Assert.NotNull(axes);
                byte[]? standard = null;
                foreach (var width in new[] { 1900, 800, 1900, 800 })
                {
                    window.Width = width; Tick(window);
                    Assert.Equal(width == 1900 ? 1600 : 1200, page.Width);
                    Assert.Equal(axes, rendered.GetValue(plot)); Assert.Null(zoomField.GetValue(plot));
                    AssertStable(); AssertFits();
                    using var image = panel.CapturePlotImage(1);
                    Save(image, $"psf-{theme}-auto-{(page.Width == 1600 ? "16-9" : "4-3")}.png");
                    if (page.Width == 1200)
                    {
                        var pixels = Hash(image);
                        if (standard is not null) Assert.Equal(standard, pixels);
                        standard = pixels;
                    }
                }
                plot.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Add });
                plot.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
                Tick(window); var zoom = zoomField.GetValue(plot); Assert.NotNull(zoom);
                var zoomedAxes = rendered.GetValue(plot);
                foreach (var (mode, paneWidth, sheetWidth) in new[]
                {
                    ("Auto", 1900, 1600), ("Auto", 800, 1200),
                    ("16:9", 800, 1600), ("16:9", 1900, 1600),
                    ("4:3", 1900, 1200), ("4:3", 800, 1200),
                    ("Auto", 1900, 1600), ("Auto", 800, 1200)
                })
                {
                    settings.DiagramAspectRatio = mode; ImageExportResolution.Configure(settings);
                    window.Width = paneWidth; Tick(window);
                    Assert.Equal(sheetWidth, page.Width); Assert.Equal(zoom, zoomField.GetValue(plot));
                    Assert.Equal(zoomedAxes, rendered.GetValue(plot)); AssertStable(); AssertFits();
                }
                host.FitToViewport = false; Tick(window);
                window.Width = 1900; Tick(window); Assert.Equal(1600, page.Width);
                window.Width = 800; Tick(window); Assert.Equal(1200, page.Width);
                Assert.False(host.FitToViewport); Assert.Equal(1, host.ContentScale);
                Assert.Equal(zoom, zoomField.GetValue(plot)); AssertStable();

                void AssertStable()
                {
                    Assert.Same(page, host.Child); Assert.Same(plot, Assert.Single(page.GetVisualDescendants().OfType<AnalysisPlotControl>()));
                    Assert.Same(series, plot.Series); Assert.Same(options, plot.PlotOptions);
                    Assert.Equal(data, JsonSerializer.Serialize(result.View)); Assert.Equal(source, app.Documents.GetRecoveryState());
                    Assert.Equal(160, page.Caption.Bounds.Height); Assert.False(page.HasOverflow);
                }
                void AssertFits()
                {
                    var bounds = new Rect(page.Bounds.Size).TransformToAABB(page.TransformToVisual(host)!.Value);
                    Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1,
                        $"{theme}: {bounds} exceeds {host.Viewport}");
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TwoPanesSelectIndependentlyAndSmallDividerJitterDoesNotToggleTheSheet()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAutomaticAspectTests));
        await session.Dispatch(() =>
        {
            ImageExportResolution.Configure(new());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Lumen");
            var first = new UniformDiagramPage(new Border(), new Border());
            var second = new UniformDiagramPage(new Border(), new Border());
            var left = new ResponsiveAnalysisPlotHost { Child = first };
            var right = new ResponsiveAnalysisPlotHost { Child = second }; Grid.SetColumn(right, 1);
            var split = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,*"), Children = { left, right } };
            var window = new Window { Width = 2300, Height = 900, Content = split };
            try
            {
                window.Show(); Tick(window);
                Assert.Equal(1600, first.Width); Assert.Equal(1200, second.Width);
                split.ColumnDefinitions = new ColumnDefinitions("*,3*"); Tick(window);
                Assert.Equal(1200, first.Width); Assert.Equal(1600, second.Width);
                Assert.Equal("Auto", DiagramSheetLayout.AspectRatio);
                split.Children.Remove(left); window.Content = left; window.Height = 800; window.Width = 1100; Tick(window);
                Assert.Equal(1200, first.Width);
                foreach (var width in new[] { 1220, 1230, 1240, 1225 })
                {
                    window.Width = width; Tick(window); Assert.Equal(1200, first.Width);
                }
                window.Width = 1500; Tick(window); Assert.Equal(1600, first.Width);
                foreach (var width in new[] { 1230, 1220, 1215, 1225 })
                {
                    window.Width = width; Tick(window); Assert.Equal(1600, first.Width);
                }
                window.Width = 1100; Tick(window); Assert.Equal(1200, first.Width);
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SquarePlotAndHiddenLegendKeepTheirAxesWhileItsOuterPageChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAutomaticAspectTests));
        await session.Dispatch(() =>
        {
            var settings = new AppSettings(); ImageExportResolution.Configure(settings);
            using var app = WorkbenchApplication.Create();
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "MTF");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            var view = new AnalysisViewDto("MTF", [], "", [new("X", "Y", [new(0, 1), new(1, 0)], Name: "Field 1, T")],
                new(DefaultSquareViewport: true, EqualAspect: true, ShowLegend: true), [], 1, PresentationKind: AnalysisPresentationKind.Mtf);
            panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1900, Height = 1100, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var page = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                var plot = Assert.Single(page.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var legend = Assert.Single(page.GetVisualDescendants().OfType<CheckBox>());
                var original = plot.Series;
                var field = typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", Private)!;
                var axes = field.GetValue(plot); legend.IsChecked = false; Tick(window);
                foreach (var width in new[] { 800, 1900, 800 })
                {
                    window.Width = width; Tick(window);
                    Assert.Equal(width == 1900 ? 1600 : 1200, page.Width);
                    Assert.InRange(Math.Abs(plot.Bounds.Width - plot.Bounds.Height), 0, 1);
                    Assert.False(legend.IsChecked); Assert.Empty(plot.Series); Assert.Equal(axes, field.GetValue(plot));
                }
                legend.IsChecked = true; Tick(window); Assert.Equal(original, plot.Series);
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional)]
    [InlineData(SceneDimension.ThreeDimensional)]
    public async Task LayoutCameraAndSceneSurviveAutomaticAspectChanges(SceneDimension dimension)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAutomaticAspectTests));
        await session.Dispatch(async () =>
        {
            ImageExportResolution.Configure(new());
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension);
            var window = new Window { Width = 1900, Height = 1100, Content = viewer };
            try
            {
                window.Show();
                for (var pass = 0; pass < 150; pass++)
                {
                    await Task.Delay(10); Tick(window);
                    if (viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) break;
                }
                var page = Assert.Single(viewer.GetVisualDescendants().OfType<UniformDiagramPage>());
                var scene = Assert.Single(viewer.GetVisualDescendants().OfType<OpticSceneControl>());
                Assert.NotNull(scene.Scene); var source = scene.Scene;
                if (dimension == SceneDimension.ThreeDimensional) scene.SetViewPreset(OpticSceneViewPreset.Front);
                Tick(window); var camera = scene.OrientationAxesForRendering();
                foreach (var width in new[] { 1900, 800, 1900 })
                {
                    window.Width = width; Tick(window);
                    Assert.Equal(width == 1900 ? 1600 : 1200, page.Width);
                    Assert.Same(source, scene.Scene); Assert.Equal(camera, scene.OrientationAxesForRendering());
                    using var image = viewer.CaptureViewerImage(1);
                    Assert.Equal((int)page.Width + 2, image.PixelSize.Width);
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static byte[] Hash(Bitmap bitmap)
    {
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { handle.Free(); }
        return SHA256.HashData(bytes);
    }
    private static void Save(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_AUTO_ASPECT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
