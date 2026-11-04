using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisFrameDpiTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static AppBuilder BuildAvaloniaApp() => LaboratoryPresentationTests.BuildAvaloniaApp();

    [Fact]
    public void OldSettingsDefaultTo1080AndIndependent150DpiAndRoundTrip()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.Equal(1080, old.ImageExportPixelHeight); Assert.Equal(150, old.ImageExportDpi);
        Assert.Equal(1920, old.ImageExportPixelWidth); Assert.True(old.ImageExportPreserveAspectRatio);
        Assert.Equal(2560, JsonSerializer.Deserialize<AppSettings>("{\"ImageExportPixelHeight\":1440}")!.ImageExportPixelWidth);
        using var directory = new ReportTestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        old.ImageExportDpi = 300; old.ImageExportPixelHeight = 2160; old.ImageExportPixelWidth = 3840;
        old.ImageExportPreserveAspectRatio = false; old.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.Equal(300, loaded.ImageExportDpi); Assert.Equal(2160, loaded.ImageExportPixelHeight);
        Assert.Equal(3840, loaded.ImageExportPixelWidth); Assert.False(loaded.ImageExportPreserveAspectRatio);
        loaded.ImageExportDpi = double.NaN; loaded.ImageExportPixelHeight = int.MaxValue;
        loaded.NormalizeDisplaySettings(); Assert.Equal(150, loaded.ImageExportDpi); Assert.Equal(1080, loaded.ImageExportPixelHeight);
        loaded.ImageExportDpi = 600; loaded.ImageExportPixelHeight = 1440; loaded.ResetDisplaySettings();
        Assert.Equal(150, loaded.ImageExportDpi); Assert.Equal(1080, loaded.ImageExportPixelHeight);
    }

    [Theory]
    [InlineData("zh-CN", "Light")]
    [InlineData("en-US", "Dark")]
    [InlineData("ja-JP", "Light")]
    public async Task SettingsKeepPresetsAndDpiIndependentAndSaveAtomically(string language, string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisFrameDpiTests));
        await session.Dispatch(() =>
        {
            using var directory = new ReportTestDirectory();
            var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            var settings = new AppSettings { Theme = theme };
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            UiLocalization.InstallGlobalHooks(); UiLocalization.Apply(language);
            var window = new DisplaySettingsWindow(settings) { Height = 840 };
            try
            {
                window.Show(); Tick(window);
                var preset = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ImageExportPreset");
                var dpi = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportDpi");
                var height = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportHeight");
                var width = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "ImageExportWidth");
                var aspect = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ImageExportPreserveAspectRatio");
                var dpiPreset = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ImageExportDpiPreset");
                Assert.Equal(0, preset.SelectedIndex); Assert.Equal(150, dpi.Value); Assert.Equal(1080, height.Value);
                Assert.Equal(1920, width.Value); Assert.True(aspect.IsChecked); Assert.Equal(1, dpiPreset.SelectedIndex);
                var summary = (TextBlock)typeof(DisplaySettingsWindow).GetField("_imageExportPixels", Private)!.GetValue(window)!;
                Assert.Equal("1920 × 1080 px · 150 DPI", summary.Text);
                Assert.Same(summary, Assert.IsType<StackPanel>(summary.Parent).Children.Last());
                var scroll = summary.GetVisualAncestors().OfType<ScrollViewer>().First();
                scroll.Offset = new Vector(0, scroll.Extent.Height); Tick(window);
                using (var defaultImage = window.CaptureRenderedFrame()!) Capture(defaultImage, $"settings-default-{language}-{theme}.png");
                dpi.Value = 300; Assert.Equal(0, preset.SelectedIndex); Assert.Equal(1080, height.Value);
                preset.SelectedIndex = 2; Assert.Equal(2160, height.Value); Assert.Equal(300, dpi.Value);
                Assert.Equal(3840, width.Value);
                height.Value = 1200; Assert.Equal(preset.ItemCount - 1, preset.SelectedIndex); Assert.Equal(300, dpi.Value);
                preset.SelectedIndex = 1; Assert.Equal(1440, height.Value);
                Assert.Equal(2560, width.Value);
                dpiPreset.SelectedIndex = 3; Assert.Equal(600, dpi.Value); Assert.Equal(1, preset.SelectedIndex);
                dpi.Value = 175; Assert.Equal(4, dpiPreset.SelectedIndex); aspect.IsChecked = false;
                var blocker = Path.Combine(directory.Path, "blocked"); File.WriteAllText(blocker, "not a directory");
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", blocker);
                var save = (Button)typeof(DisplaySettingsWindow).GetField("_save", Private)!.GetValue(window)!;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick(window);
                Assert.True(window.IsVisible); Assert.Equal(1080, settings.ImageExportPixelHeight); Assert.Equal(150, settings.ImageExportDpi);
                Assert.Equal(1920, settings.ImageExportPixelWidth); Assert.True(settings.ImageExportPreserveAspectRatio);
                ((TextBlock)typeof(DisplaySettingsWindow).GetField("_imageExportPixels", Private)!.GetValue(window)!).BringIntoView(); Tick(window);
                using var preview = window.CaptureRenderedFrame()!; Capture(preview, $"settings-{language}-{theme}.png");
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", directory.Path);
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var restored = AppSettings.Load(Path.Combine(directory.Path, "settings.json"));
                Assert.Equal(1440, restored.ImageExportPixelHeight); Assert.Equal(175, restored.ImageExportDpi);
                Assert.Equal(2560, restored.ImageExportPixelWidth); Assert.False(restored.ImageExportPreserveAspectRatio);
            }
            finally { window.Close(); Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous); UiLocalization.Apply(UiLocalization.Chinese); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1080, 1920, 96)]
    [InlineData(1440, 2560, 300)]
    [InlineData(2160, 3840, 600)]
    public async Task PresetsPreserveCompleteSourceAndWriteRealLosslessPngDpi(int height, int width, int dpi)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisFrameDpiTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new(), "Wavefront Map");
            typeof(AnalysisPanel).GetField("_initialRunRequested", Private)!.SetValue(panel, true);
            var view = View(); var document = app.Documents.GetSnapshot();
            var time = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
            panel.PresentResult(view, document, time);
            var window = new Window { Width = 920, Height = 720, Content = panel };
            using var directory = new ReportTestDirectory();
            try
            {
                ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportPixelHeight = height, ImageExportDpi = dpi });
                window.Show(); Tick(window);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.IsType<AnalysisImageFrame>(sheet.Parent);
                var sourceBounds = sheet.Bounds; var parent = sheet.Parent;
                var surface = Assert.Single(sheet.GetVisualDescendants().OfType<WavefrontSurfaceControl>());
                var camera = (surface.ViewYawDegrees, surface.ViewPitchDegrees, surface.ViewZoom);
                using var image = panel.CapturePlotImage();
                Assert.Equal(new PixelSize(width, height), image.PixelSize);
                ExportImageAssertions.HasCompleteBorder(image);
                var frameBounds = ExportImageAssertions.FrameBounds(image);
                Assert.Equal((image.PixelSize.Width - frameBounds.Width) / 2, frameBounds.X);
                Assert.Equal(0, frameBounds.Y); Assert.Equal(height, frameBounds.Height);
                Assert.Equal(sourceBounds, sheet.Bounds); Assert.Same(parent, sheet.Parent);
                Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                Assert.Same(view.Series[0], surface.Series);
                Assert.Equal(camera, (surface.ViewYawDegrees, surface.ViewPitchDegrees, surface.ViewZoom));
                var path = Path.Combine(directory.Path, "analysis.png"); AnalysisPanel.SavePlotImage(image, path);
                var bytes = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
                AssertPngDpi(bytes, dpi);
                using var baseline = new MemoryStream(); image.Save(baseline, PngBitmapEncoderOptions.Default);
                using var decoded = SKBitmap.Decode(bytes); using var original = SKBitmap.Decode(baseline.ToArray());
                Assert.True(decoded.GetPixelSpan().SequenceEqual(original.GetPixelSpan()));
                Capture(image, $"wavefront-{height}.png");
                var result = new AnalysisResultDto(Guid.NewGuid(), 1, 0, view, new("Wavefront Map", "test", "test"), document);
                var host = new Border(); window.Content = host;
                var files = await new ReportImageRenderer(host).WriteAsync(new(result, null, document, time, view.Name), directory.Path, "report", CancellationToken.None);
                var reportBytes = File.ReadAllBytes(Path.Combine(directory.Path, Assert.Single(files))); AssertPngDpi(reportBytes, dpi);
                using var report = new Bitmap(new MemoryStream(reportBytes));
                Assert.Equal(frameBounds.Size, report.PixelSize);
                Assert.Equal(ExportImageAssertions.FramePixels(image), ExportImageAssertions.FramePixels(report));
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PrintDpiChangesOnlyMetadataAndJpegPathsCannotOverwriteFiles()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisFrameDpiTests));
        await session.Dispatch(() =>
        {
            var content = new Border { Width = 1200, Height = 900, Background = new SolidColorBrush(Color.FromRgb(12, 149, 231)) };
            var window = new Window { Width = 1200, Height = 900, Content = content };
            using var directory = new ReportTestDirectory();
            try
            {
                window.Show(); Tick(window);
                var encoded = new List<byte[]>();
                foreach (var dpi in new[] { 96, 150, 300, 600 })
                {
                    ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportDpi = dpi });
                    using var bitmap = ExportImageFrame.CaptureConfigured(content);
                    ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportDpi = dpi == 600 ? 96 : 600 });
                    using var stream = new MemoryStream(); PngImageWriter.Write(bitmap, stream);
                    var png = stream.ToArray(); AssertPngDpi(png, dpi);
                    using var decoded = SKBitmap.Decode(png); encoded.Add(decoded.GetPixelSpan().ToArray());
                    var path = Path.Combine(directory.Path, "existing.jpeg"); File.WriteAllBytes(path, [1, 2, 3]);
                    Assert.Throws<InvalidDataException>(() => AnalysisPanel.SavePlotImage(bitmap, path));
                    Assert.Throws<InvalidDataException>(() => ViewerPanel.SaveViewerImage(bitmap, path));
                    Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
                }
                Assert.All(encoded.Skip(1), bytes => Assert.Equal(encoded[0], bytes));
                ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportPixelHeight = 8192, ImageExportPixelWidth = 8192 });
                Assert.Throws<InvalidOperationException>(() => ExportImageFrame.CaptureConfigured(content));
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FixedCanvasPadsCenteredOrFillsOnlyWhenTheUserUnlocksIt()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisFrameDpiTests));
        await session.Dispatch(() =>
        {
            var source = new Border { Width = 1200, Height = 900, Background = Brushes.Crimson };
            var window = new Window { Width = 1200, Height = 900, Content = source };
            try
            {
                window.Show(); Tick(window);
                ImageExportResolution.Configure(new());
                using var padded = ExportImageFrame.CaptureConfigured(source);
                Assert.Equal(new PixelSize(1920, 1080), padded.PixelSize);
                Assert.Equal(new PixelRect(240, 0, 1440, 1080), ExportImageAssertions.FrameBounds(padded));
                ExportImageAssertions.HasCompleteBorder(padded);
                ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportPreserveAspectRatio = false });
                using var filled = ExportImageFrame.CaptureConfigured(source);
                Assert.Equal(new PixelRect(0, 0, 1920, 1080), ExportImageAssertions.FrameBounds(filled));
                ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard, ImageExportPixelWidth = 1080, ImageExportPixelHeight = 1920 });
                using var portrait = ExportImageFrame.CaptureConfigured(source);
                Assert.Equal(new PixelSize(1080, 1920), portrait.PixelSize);
                Assert.Equal(new PixelRect(0, 554, 1080, 811), ExportImageAssertions.FrameBounds(portrait));
                Assert.Equal(new Size(1200, 900), source.Bounds.Size);
            }
            finally { window.Close(); ImageExportResolution.Configure(new()); }
        }, CancellationToken.None);
    }

    private static AnalysisViewDto View()
    {
        var points = Enumerable.Range(-8, 17).SelectMany(y => Enumerable.Range(-8, 17).Select(x =>
            new AnalysisPointDto(x / 8d, y / 8d, Value: (x * x + y * y) / 128d))).ToArray();
        return new("波前图", [new("显示为", "表面")], "", [new("X", "Y", points, AnalysisSeriesKind.Heatmap)], new(), [], 1,
            PresentationKind: AnalysisPresentationKind.WavefrontMap);
    }
    private static void AssertPngDpi(byte[] png, double dpi)
    {
        var found = 0; var idatSeen = false;
        for (var offset = 8; offset < png.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") idatSeen = true;
            if (type == "pHYs")
            {
                Assert.False(idatSeen); found++; Assert.Equal(9, length); Assert.Equal(1, png[offset + 16]);
                var x = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8, 4));
                var y = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 12, 4));
                Assert.Equal(x, y); Assert.InRange(Math.Abs(x * .0254 - dpi), 0, .013);
            }
            offset += length + 12;
        }
        Assert.Equal(1, found);
    }
    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Capture(Bitmap bitmap, string file)
    {
        var path = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_FRAME_DPI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path); using var stream = File.Create(Path.Combine(path, file)); PngImageWriter.Write(bitmap, stream);
    }
}
