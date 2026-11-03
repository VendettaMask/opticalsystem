using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
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
public sealed class ScientificColorMapTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => LaboratoryPresentationTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Viridis")]
    [InlineData("RedBlue")]
    [InlineData("Rainbow")]
    [InlineData("Inferno")]
    [InlineData("Gray")]
    public void SettingsPersistAndOldOrUnknownSettingsUseViridis(string map)
    {
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var settings = new AppSettings { AnalysisColorMap = map, FontSize = 18, Theme = "Dark" };
        settings.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal(map, loaded.AnalysisColorMap);
        Assert.Equal(18, loaded.FontSize); Assert.Equal("Dark", loaded.Theme);
        var old = JsonSerializer.Deserialize<AppSettings>("{\"FontSize\":16}")!;
        Assert.Equal("Viridis", old.AnalysisColorMap);
        old.AnalysisColorMap = "unknown"; old.NormalizeDisplaySettings();
        Assert.Equal("Viridis", old.AnalysisColorMap);
        old.AnalysisColorMap = map; old.ResetDisplaySettings();
        Assert.Equal("Viridis", old.AnalysisColorMap);
    }

    [Fact]
    public void PaletteEndpointsInterpolationAndInvalidValuesAreDefined()
    {
        Assert.Equal(Color.FromRgb(68, 1, 84), ScientificColorMaps.ColorFor(ScientificColorMap.Viridis, 0));
        Assert.Equal(Color.FromRgb(33, 145, 140), ScientificColorMaps.ColorFor(ScientificColorMap.Viridis, .5));
        Assert.Equal(Color.FromRgb(253, 231, 37), ScientificColorMaps.ColorFor(ScientificColorMap.Viridis, 1));
        Assert.Equal(Colors.White, ScientificColorMaps.ColorFor(ScientificColorMap.RedBlue, .5));
        Assert.Equal(Color.FromRgb(33, 102, 172), ScientificColorMaps.ColorFor(ScientificColorMap.RedBlue, 0));
        Assert.Equal(Color.FromRgb(178, 24, 43), ScientificColorMaps.ColorFor(ScientificColorMap.RedBlue, 1));
        foreach (var map in Enum.GetValues<ScientificColorMap>())
        {
            Assert.Equal(ScientificColorMaps.ColorFor(map, 0), ScientificColorMaps.ColorFor(map, -10));
            Assert.Equal(ScientificColorMaps.ColorFor(map, 1), ScientificColorMaps.ColorFor(map, 10));
            Assert.Equal(Colors.Transparent, ScientificColorMaps.ColorFor(map, double.NaN));
            Assert.Equal(Colors.Transparent, ScientificColorMaps.ColorFor(map, double.PositiveInfinity));
            Assert.NotEqual(ScientificColorMaps.ColorFor(map, .25), ScientificColorMaps.ColorFor(map, .75));
        }
    }

    [Theory]
    [InlineData("zh-CN", "Light", 13)]
    [InlineData("en-US", "Dark", 18)]
    [InlineData("ja-JP", "Light", 22)]
    public async Task SettingsOfferPreviewSaveRollbackAndResetWithoutChangingOtherPreferences(string language, string theme, double size)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ScientificColorMapTests));
        await session.Dispatch(() =>
        {
            using var directory = new ReportTestDirectory();
            var previousPath = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var settings = new AppSettings { FontSize = size, Theme = theme };
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(language);
            DisplayTypography.Configure(settings);
            ScientificColorMaps.Configure(new());
            var window = new DisplaySettingsWindow(settings) { Width = 560, Height = 830 };
            try
            {
                var blocker = Path.Combine(directory.Path, "blocked"); File.WriteAllText(blocker, "file, not directory");
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", blocker);
                window.Show(); Tick(window);
                var choice = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "AnalysisColorMap");
                Assert.Equal(ScientificColorMap.Viridis, choice.SelectedItem);
                choice.BringIntoView(); choice.Focus(); choice.IsDropDownOpen = true; Tick(window);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                Assert.Equal(ScientificColorMap.RedBlue, choice.SelectedItem);
                Assert.Equal(ScientificColorMap.Viridis, ScientificColorMaps.Current); // Dialog preview is local.
                var save = (Button)typeof(DisplaySettingsWindow).GetField("_save", Private)!.GetValue(window)!;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True(window.IsVisible);
                Assert.Equal("Viridis", settings.AnalysisColorMap); // Failed save restores the entire selection.
                Assert.Equal(theme, settings.Theme); Assert.Equal(size, settings.FontSize);
                typeof(DisplaySettingsWindow).GetMethod("ResetControls", Private)!.Invoke(window, null);
                Assert.Equal(ScientificColorMap.Viridis, choice.SelectedItem);
                // Keep the user's other settings when applying only a colormap.
                window.Close();
                window = new DisplaySettingsWindow(settings) { Width = 560, Height = 830 };
                window.Show(); Tick(window);
                choice = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "AnalysisColorMap");
                choice.SelectedItem = ScientificColorMap.Rainbow; choice.BringIntoView(); Tick(window);
                using var settingsImage = window.CaptureRenderedFrame()!;
                Capture(settingsImage, $"settings-{language}-{theme}.png");
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory.Path);
                save = (Button)typeof(DisplaySettingsWindow).GetField("_save", Private)!.GetValue(window)!;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var loaded = AppSettings.Load(Path.Combine(directory.Path, "settings.json"));
                Assert.Equal("Rainbow", loaded.AnalysisColorMap);
                Assert.Equal(theme, loaded.Theme); Assert.Equal(size, loaded.FontSize);
            }
            finally
            {
                window.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousPath);
                UiLocalization.Apply(UiLocalization.Chinese); DisplayTypography.Configure(new()); ScientificColorMaps.Configure(new());
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(ScientificColorMap.Viridis)]
    [InlineData(ScientificColorMap.RedBlue)]
    [InlineData(ScientificColorMap.Rainbow)]
    public async Task Existing2DAnd3DPlotsRepaintTogetherAndRetainDataAndView(ScientificColorMap selected)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ScientificColorMapTests));
        await session.Dispatch(() =>
        {
            var series = Field();
            var planar = new AnalysisPlotControl { Series = [series], PlotOptions = new(ShowLegend: false) };
            var surface = new WavefrontSurfaceControl { Series = series };
            var grid = new Grid { ColumnDefinitions = new("*,*") };
            grid.Children.Add(planar); Grid.SetColumn(surface, 1); grid.Children.Add(surface);
            var window = new Window { Width = 1200, Height = 660, Content = grid };
            try
            {
                ScientificColorMaps.Configure(new() { AnalysisColorMap = "Inferno" });
                window.Show(); Tick(window);
                surface.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Add, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                var camera = (surface.ViewYawDegrees, surface.ViewPitchDegrees, surface.ViewZoom);
                var viewport = typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", Private)!.GetValue(planar);
                var hidden = new AnalysisSeriesDto[] { series }; planar.SetVisibleSeries(hidden);
                ScientificColorMaps.Configure(new() { AnalysisColorMap = selected.ToString() }); Tick(window);
                Assert.Same(hidden, planar.Series); Assert.Same(series, surface.Series);
                Assert.Equal(camera, (surface.ViewYawDegrees, surface.ViewPitchDegrees, surface.ViewZoom));
                Assert.Equal(viewport, typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", Private)!.GetValue(planar));
                foreach (var value in new[] { 0, .25, .5, .75, 1 })
                {
                    Assert.Equal(ScientificColorMaps.ColorFor(selected, value), planar.ScalarColor(series, value));
                    Assert.Equal(planar.ScalarColor(series, value), surface.PlotColor(value));
                    Assert.Equal(surface.PlotColor(value), new FoucaultPlotControl { DisplayAs = "伪彩色" }.DisplayColor(value));
                }
                using var image = window.CaptureRenderedFrame()!;
                using var bitmap = Decode(image);
                // Each plot's colorbar contains the selected low colour. Both sides must change.
                var color = ScientificColorMaps.ColorFor(selected, 0);
                Assert.True(Count(bitmap, color, 0, bitmap.Width / 2) > 3);
                Assert.True(Count(bitmap, color, bitmap.Width / 2, bitmap.Width) > 3);
                Capture(image, $"paired-{selected}.png");
            }
            finally { window.Close(); ScientificColorMaps.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LinesScatterColoredLinesAndRgbRasterRemainPixelIdentical()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ScientificColorMapTests));
        await session.Dispatch(() =>
        {
            var grid = new Grid { ColumnDefinitions = new("*,*"), RowDefinitions = new("*,*") };
            var kinds = new[] { AnalysisSeriesKind.Line, AnalysisSeriesKind.Scatter, AnalysisSeriesKind.ColoredLine, AnalysisSeriesKind.Raster };
            for (var i = 0; i < kinds.Length; i++)
            {
                var plot = new AnalysisPlotControl { Series = [Field() with { Kind = kinds[i] }], PlotOptions = new(ShowLegend: false) };
                Grid.SetRow(plot, i / 2); Grid.SetColumn(plot, i % 2); grid.Children.Add(plot);
            }
            var window = new Window { Width = 900, Height = 700, Content = grid };
            try
            {
                ScientificColorMaps.Configure(new()); window.Show(); Tick(window);
                using var baseline = window.CaptureRenderedFrame()!; var expected = Encoded(baseline);
                foreach (var map in Enum.GetValues<ScientificColorMap>())
                {
                    ScientificColorMaps.Configure(new() { AnalysisColorMap = map.ToString() }); Tick(window);
                    using var actual = window.CaptureRenderedFrame()!;
                    Assert.Equal(expected, Encoded(actual));
                    Assert.Equal(Color.FromRgb(128, 128, 128), new FoucaultPlotControl().DisplayColor(.5));
                    var gray = new WavefrontSurfaceControl { ColorMapOverride = AnalysisColorMap.InverseGray };
                    Assert.Equal(AnalysisPlotControl.ColorFor(AnalysisColorMap.InverseGray, .25), gray.PlotColor(.25));
                    var local = new AnalysisPlotControl { UseSeriesColorMap = true };
                    Assert.Equal(AnalysisPlotControl.ColorFor(AnalysisColorMap.Jet, .25), local.ScalarColor(Field(), .25));
                }
            }
            finally { window.Close(); ScientificColorMaps.Configure(new()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("表面", ScientificColorMap.Viridis)]
    [InlineData("二维热力图", ScientificColorMap.Viridis)]
    [InlineData("二维热力图", ScientificColorMap.RedBlue)]
    [InlineData("二维热力图", ScientificColorMap.Rainbow)]
    public async Task RealWavefrontLivePngAndReportShareOnePaletteAndSource(string display, ScientificColorMap map)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ScientificColorMapTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Wavefront Map", new Dictionary<string, string> { ["Sampling"] = "32 x 32", ["DisplayAs"] = display }));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            var view = result.View;
            var before = app.Documents.GetSnapshot();
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new(), "Wavefront Map");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            var time = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
            ScientificColorMaps.Configure(new() { AnalysisColorMap = map.ToString() });
            panel.PresentResult(view, before, time);
            var window = new Window { Width = 1200, Height = 940, Content = panel };
            using var directory = new ReportTestDirectory();
            try
            {
                window.Show(); Tick(window);
                var plot = Assert.Single(panel.GetVisualDescendants().OfType<WavefrontSurfaceControl>());
                Assert.Equal(display, plot.DisplayAs);
                Assert.Same(view.Series[0], plot.Series);
                using var png = panel.CapturePlotImage();
                Assert.Equal(new PixelSize(1920, 1080), png.PixelSize);
                Capture(png, $"wavefront-{(display == "表面" ? "3d" : "2d")}-{map}.png");
                var host = new Border(); window.Content = host; Tick(window);
                var files = await new ReportImageRenderer(host).WriteAsync(
                    new(result, null, before, time, view.Name), directory.Path, "palette", CancellationToken.None);
                using var report = new Bitmap(Path.Combine(directory.Path, Assert.Single(files)));
                Assert.Equal(ExportImageAssertions.FrameBounds(png).Size, report.PixelSize);
                Assert.Equal(ExportImageAssertions.FramePixels(png), ExportImageAssertions.FramePixels(report));
                Assert.Equal(before, app.Documents.GetSnapshot());
            }
            finally { window.Close(); ScientificColorMaps.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MainWindowAppliesOnlyPaletteWithoutRerunningOpenAnalysis()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ScientificColorMapTests));
        await session.Dispatch(async () =>
        {
            using var directory = new ReportTestDirectory();
            var previousPath = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory.Path);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var recovery = app.Documents.GetRecoveryState();
            var settings = new AppSettings();
            var window = new MainWindow(settings, app, new WorkspaceSessionStore(Path.Combine(directory.Path, "workspace")));
            var ready = false; window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var i = 0; i < 200 && !ready; i++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var panels = (PanelManager)typeof(MainWindow).GetField("_panels", Private)!.GetValue(window)!;
                panels.ShowAnalysis("Wavefront Map");
                for (var i = 0; i < 200 && !window.GetVisualDescendants().OfType<WavefrontSurfaceControl>().Any(); i++)
                { await Task.Delay(10); Tick(window); }
                var original = Assert.Single(window.GetVisualDescendants().OfType<WavefrontSurfaceControl>());
                var series = original.Series;
                original.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null); Tick(window);
                var camera = (original.ViewYawDegrees, original.ViewPitchDegrees, original.ViewZoom);
                var apply = (Task)typeof(MainWindow).GetMethod("ShowDisplaySettingsAsync", Private)!.Invoke(window, null)!;
                Tick(window);
                var dialog = Assert.Single(window.OwnedWindows.OfType<DisplaySettingsWindow>());
                var choice = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "AnalysisColorMap");
                choice.SelectedItem = ScientificColorMap.RedBlue;
                dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportHeight").Value = 2160;
                dialog.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportDpi").Value = 300;
                ((Button)typeof(DisplaySettingsWindow).GetField("_save", Private)!.GetValue(dialog)!)
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await apply; Tick(window); await Task.Delay(50); Tick(window);
                Assert.Same(original, Assert.Single(window.GetVisualDescendants().OfType<WavefrontSurfaceControl>()));
                Assert.Same(series, original.Series);
                Assert.Equal(camera, (original.ViewYawDegrees, original.ViewPitchDegrees, original.ViewZoom));
                Assert.Equal(ScientificColorMap.RedBlue, ScientificColorMaps.Current);
                Assert.Equal(2160, ImageExportResolution.PixelHeight); Assert.Equal(300, ImageExportResolution.Dpi);
                Assert.Equal("RedBlue", AppSettings.Load(Path.Combine(directory.Path, "settings.json")).AnalysisColorMap);
                Assert.Equal(recovery, app.Documents.GetRecoveryState());
            }
            finally
            {
                window.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previousPath);
                ScientificColorMaps.Configure(new());
                ImageExportResolution.Configure(new());
            }
        }, CancellationToken.None);
    }

    private static AnalysisSeriesDto Field() => new("X", "Y",
        Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new AnalysisPointDto(x / 8.0, y / 8.0, Value: (x * x + y * y) / 128.0, Red: .2, Green: .5, Blue: .8))).ToArray(),
        AnalysisSeriesKind.Heatmap, ValueLabel: "Value", ColorMap: AnalysisColorMap.Jet, ValueMinimum: 0, ValueMaximum: 1);

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static byte[] Encoded(Bitmap image) { using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray(); }
    private static SkiaSharp.SKBitmap Decode(Bitmap image) => SkiaSharp.SKBitmap.Decode(Encoded(image));
    private static int Count(SkiaSharp.SKBitmap image, Color color, int start, int end)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++) for (var x = start; x < end; x++)
        {
            var p = image.GetPixel(x, y);
            if (p.Red == color.R && p.Green == color.G && p.Blue == color.B) count++;
        }
        return count;
    }
    private static void Capture(Bitmap image, string file)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_COLORMAP_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path); image.Save(Path.Combine(path, file), PngBitmapEncoderOptions.Default);
    }
}
