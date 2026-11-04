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
public sealed class DiagramAspectRatioTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => DiagramViewportFitTests.BuildAvaloniaApp();

    [Fact]
    public void PreferenceDefaultsAndRoundTripsWithoutChangingOpticalSettings()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal("Auto", settings.DiagramAspectRatio);
        settings.DiagramAspectRatio = "4:3";
        settings.AnalysisSettings["FFT PSF Cross Section"] = new() { ["Sampling"] = "64 x 64" };
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        settings.Save(path);
        var saved = AppSettings.Load(path);
        Assert.Equal("4:3", saved.DiagramAspectRatio);
        Assert.Equal("64 x 64", saved.AnalysisSettings["FFT PSF Cross Section"]["Sampling"]);
        saved.DiagramAspectRatio = "invalid"; saved.NormalizeDisplaySettings();
        Assert.Equal("Auto", saved.DiagramAspectRatio);
        saved.DiagramAspectRatio = "16:9"; saved.ResetDisplaySettings();
        Assert.Equal("Auto", saved.DiagramAspectRatio);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    public async Task RealPsfSwitchKeepsResultControlsZoomAndPixelsOnReturn(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAspectRatioTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings { DiagramAspectRatio = DiagramSheetLayout.Standard };
            ImageExportResolution.Configure(settings); DisplayTypography.Configure(settings);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "FFT PSF Cross Section");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "FFT PSF Cross Section",
                new Dictionary<string, string> { ["Sampling"] = "64 x 64" }));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var serialized = JsonSerializer.Serialize(result.View);
            var source = app.Documents.GetRecoveryState();
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1500, Height = 1000, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                var plot = Assert.Single(sheet.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var series = plot.Series; var options = plot.PlotOptions;
                plot.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Add });
                Tick(window);
                var viewportField = typeof(AnalysisPlotControl).GetField("_viewport", Private)!;
                var zoom = viewportField.GetValue(plot); Assert.NotNull(zoom);
                using var original = panel.CapturePlotImage(1); var hash = Hash(original);
                foreach (var ratio in new[] { "16:9", "4:3", "16:9", "4:3" })
                {
                    settings.DiagramAspectRatio = ratio;
                    ImageExportResolution.Configure(settings); Tick(window);
                    var width = ratio == "16:9" ? 1600 : 1200;
                    Assert.Same(sheet, Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>()));
                    Assert.Same(plot, Assert.Single(sheet.GetVisualDescendants().OfType<AnalysisPlotControl>()));
                    Assert.Equal(new Size(width, 900), sheet.Bounds.Size);
                    Assert.Equal(160, sheet.Caption.Bounds.Height); Assert.False(sheet.HasOverflow);
                    Assert.Same(series, plot.Series); Assert.Same(options, plot.PlotOptions);
                    Assert.Equal(zoom, viewportField.GetValue(plot));
                    Assert.Equal(serialized, JsonSerializer.Serialize(result.View));
                    Assert.Equal(source, app.Documents.GetRecoveryState());
                    using var image = panel.CapturePlotImage(1);
                    var border = (int)ThemeDrawingSurface.FrameInset(sheet);
                    Assert.Equal(new PixelSize(width + 2 * border, 900 + 2 * border), image.PixelSize);
                    if (ratio == "4:3") Assert.Equal(hash, Hash(image));
                    using var report = ExportImageFrame.CaptureForReport(sheet);
                    Assert.InRange(Math.Abs((double)report.PixelSize.Width / report.PixelSize.Height - (width + 2d) / 902), 0, .002);
                    using var configured = panel.CapturePlotImage(); Assert.Equal(new PixelSize(1920, 1080), configured.PixelSize);
                    Save(image, $"psf-{theme}-{ratio.Replace(':', '-')}.png");
                    Save(report, $"psf-{theme}-{ratio.Replace(':', '-')}-report.png");
                    var host = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                    foreach (var size in new[] { (520, 400), (1500, 1000) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2; Tick(window);
                        var bounds = new Rect(sheet.Bounds.Size).TransformToAABB(sheet.TransformToVisual(host)!.Value);
                        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= host.Viewport.Width + 1 && bounds.Bottom <= host.Viewport.Height + 1,
                            $"{theme}/{ratio}: {bounds} in {host.Viewport}");
                    }
                }
                plot.ResetView();
                foreach (var ratio in new[] { "16:9", "4:3" })
                {
                    settings.DiagramAspectRatio = ratio; ImageExportResolution.Configure(settings); Tick(window);
                    using var image = panel.CapturePlotImage(1); Save(image, $"psf-{theme}-{ratio.Replace(':', '-')}-overview.png");
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SquarePlotsAndLegendVisibilitySurviveBothLayouts()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAspectRatioTests));
        await session.Dispatch(() =>
        {
            var settings = new AppSettings { DiagramAspectRatio = DiagramSheetLayout.Standard }; ImageExportResolution.Configure(settings);
            using var app = WorkbenchApplication.Create();
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, "MTF");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            var view = new AnalysisViewDto("MTF", [], "", [new("X", "Y", [new(0, 1), new(1, 0)], Name: "Field 1, T")],
                new(DefaultSquareViewport: true, ShowLegend: true), [], 1, PresentationKind: AnalysisPresentationKind.Mtf);
            panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1400, Height = 1000, Content = panel };
            try
            {
                window.Show(); Tick(window);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                var plot = Assert.Single(sheet.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var legend = Assert.Single(sheet.GetVisualDescendants().OfType<CheckBox>());
                var series = plot.Series;
                legend.IsChecked = false; Tick(window); Assert.Empty(plot.Series);
                foreach (var ratio in new[] { "16:9", "4:3" })
                {
                    settings.DiagramAspectRatio = ratio; ImageExportResolution.Configure(settings); Tick(window);
                    Assert.False(legend.IsChecked); Assert.Empty(plot.Series); Assert.False(sheet.HasOverflow);
                    Assert.InRange(Math.Abs(plot.Bounds.Width - plot.Bounds.Height), 0, 1);
                    using var image = panel.CapturePlotImage(1); Save(image, $"square-hidden-{ratio.Replace(':', '-')}.png");
                }
                legend.IsChecked = true; Tick(window); Assert.Equal(series, plot.Series);
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional)]
    [InlineData(SceneDimension.ThreeDimensional)]
    public async Task LayoutExportUsesSelectedWidthAndKeepsSceneAndCamera(SceneDimension dimension)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAspectRatioTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings { DiagramAspectRatio = DiagramSheetLayout.Standard }; ImageExportResolution.Configure(settings);
            using var app = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension);
            var window = new Window { Width = 1500, Height = 1000, Content = viewer };
            try
            {
                window.Show();
                for (var i = 0; i < 150; i++)
                {
                    await Task.Delay(10); Tick(window);
                    if (viewer.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) break;
                }
                var sheet = Assert.Single(viewer.GetVisualDescendants().OfType<UniformDiagramPage>());
                var scene = Assert.Single(viewer.GetVisualDescendants().OfType<OpticSceneControl>());
                var source = scene.Scene; Assert.NotNull(source);
                var camera = scene.OrientationAxesForRendering();
                using var before = viewer.CaptureViewerImage(1); var hash = Hash(before);
                foreach (var ratio in new[] { "16:9", "4:3" })
                {
                    settings.DiagramAspectRatio = ratio; ImageExportResolution.Configure(settings); Tick(window);
                    Assert.Same(source, scene.Scene); Assert.Equal(camera, scene.OrientationAxesForRendering());
                    Assert.False(sheet.HasOverflow);
                    using var image = viewer.CaptureViewerImage(1);
                    Assert.Equal(new PixelSize(ratio == "16:9" ? 1602 : 1202, 902), image.PixelSize);
                    if (ratio == "4:3") Assert.Equal(hash, Hash(image));
                    Save(image, $"layout-{dimension}-{ratio.Replace(':', '-')}.png");
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReportBuildersUseSelectedAspectForAnalysisAndLayoutWithoutChangingInputs()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAspectRatioTests));
        await session.Dispatch(async () =>
        {
            var settings = new AppSettings(); ImageExportResolution.Configure(settings);
            using var directory = new ReportTestDirectory();
            using var app = WorkbenchApplication.Create("cooke");
            var document = app.Documents.GetSnapshot(); var source = app.Documents.GetRecoveryState();
            var points = Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
                new AnalysisPointDto(x / 8d, y / 8d, Value: (x * x + y * y) / 128d))).ToArray();
            var view = new AnalysisViewDto("波前图", [new("显示为", "表面")], "",
                [new("X", "Y", points, AnalysisSeriesKind.Heatmap)], new(), [], 1,
                PresentationKind: AnalysisPresentationKind.WavefrontMap);
            var result = new AnalysisResultDto(Guid.NewGuid(), 1, 0, view, new("Wavefront Map", "layout fixture", "layout fixture"), document);
            var scene = await app.Visualization.BuildSceneAsync(SceneDimension.TwoDimensional);
            var serialized = JsonSerializer.Serialize(new { view, scene });
            var time = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(8));
            var host = new Border(); var window = new Window { Width = 1800, Height = 1100, Content = host };
            try
            {
                window.Show();
                foreach (var ratio in new[] { "4:3", "16:9", "Auto" })
                {
                    settings.DiagramAspectRatio = ratio; ImageExportResolution.Configure(settings);
                    foreach (var request in new[]
                    {
                        new ReportImageRequest(result, null, document, time, "wavefront", ForPrint: true),
                        new ReportImageRequest(null, scene, document, time, "layout", ForPrint: true)
                    })
                    {
                        var name = $"builder-{request.Title}-{ratio.Replace(':', '-')}";
                        var file = Assert.Single(await new ReportImageRenderer(host).WriteAsync(request, directory.Path, name, CancellationToken.None));
                        using var bitmap = new Bitmap(Path.Combine(directory.Path, file));
                        Assert.Equal(ImageExportResolution.SheetPixels(1080), bitmap.PixelSize);
                        Save(bitmap, name + ".png");
                    }
                    Assert.Equal(serialized, JsonSerializer.Serialize(new { view, scene }));
                    Assert.Equal(source, app.Documents.GetRecoveryState());
                }
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SettingsPreviewDoesNotApplyUntilSaveAndFailedSaveRestoresPreference()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(DiagramAspectRatioTests));
        await session.Dispatch(() =>
        {
            using var directory = new ReportTestDirectory();
            var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var settings = new AppSettings(); ImageExportResolution.Configure(settings);
            var window = new DisplaySettingsWindow(settings);
            try
            {
                window.Show(); Tick(window);
                var ratio = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "DiagramAspectRatio");
                var width = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportWidth");
                Assert.Equal("Auto", ratio.SelectedItem);
                Assert.Equal(new[] { "Auto", "16:9", "4:3" }, ratio.ItemsSource!.Cast<string>());
                ratio.SelectedItem = "16:9"; Assert.Equal(1920, width.Value);
                Assert.Equal("Auto", DiagramSheetLayout.AspectRatio);
                var blocker = Path.Combine(directory.Path, "blocked"); File.WriteAllText(blocker, "file");
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", blocker);
                var save = (Button)typeof(DisplaySettingsWindow).GetField("_save", Private)!.GetValue(window)!;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Auto", settings.DiagramAspectRatio); Assert.True(window.IsVisible);
                ratio.SelectedItem = "4:3"; Assert.Equal(1440, width.Value);
                ratio.SelectedItem = "Auto"; Assert.Equal(1440, width.Value);
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory.Path);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Auto", AppSettings.Load(Path.Combine(directory.Path, "settings.json")).DiagramAspectRatio);
            }
            finally { window.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous); }
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
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ASPECT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
