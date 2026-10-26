using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MarkdownReportImageTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Fact]
    public async Task SharedProgressSupportsItemCountsAndResetsToIndeterminateForOtherOperations()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MarkdownReportImageTests));
        await session.Dispatch(() =>
        {
            using var status = new OperationStatusBar("ReportExportProgress");
            status.Start("Export", () => { });
            status.ReportProgress(3, 8, "3/8");
            var progress = Assert.Single(status.Children.OfType<ProgressBar>());
            Assert.Equal(3, progress.Value); Assert.Equal(8, progress.Maximum);
            Assert.False(progress.IsIndeterminate); Assert.True(progress.IsVisible);
            status.MarkSynced("Complete"); Assert.False(progress.IsVisible);
            status.Start("Analyze", null);
            Assert.True(progress.IsIndeterminate); Assert.False(progress.IsVisible);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FullReportRendersRealLayoutsPlotsAndPaginatedTextWithVisiblePixels()
    {
        using var directory = new ReportTestDirectory();
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MarkdownReportImageTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            var host = new Border { Width = 1200, Height = 900, Opacity = 0 };
            var window = new Window { Width = 900, Height = 700, Content = host };
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                using var snapshot = application.CaptureReportSnapshot();
                var options = new MarkdownReportSettings { AnalysisKeys = [.. MarkdownReportSettings.CommonAnalyses, "Seidel Coefficients", "Wavefront Map"] };
                var renderer = new ReportImageRenderer(host);
                var result = await Task.Run(() => MarkdownReportExporter.ExportAsync(snapshot, options, directory.Path, renderer.WriteAsync));
                Assert.Equal(0, result.Failed);
                Assert.Equal(8, result.Succeeded);
                var images = Directory.GetFiles(directory.Path, "*.png", SearchOption.AllDirectories);
                Assert.True(images.Length >= 8);
                foreach (var image in images)
                {
                    using var bitmap = new Bitmap(image);
                    Assert.InRange(bitmap.PixelSize.Width, 100, 32768);
                    Assert.InRange(bitmap.PixelSize.Height, 100, 32768);
                    var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
                    var pinned = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
                    finally { pinned.Free(); }
                    // Transparent/empty images and uniform background captures both fail.
                    Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0) > 1000);
                    Assert.True(Enumerable.Range(0, pixels.Length / 4).Select(i => (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2])).Distinct().Take(20).Count() >= 20);
                }
                var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    foreach (var image in images)
                    {
                        var target = Path.Combine(evidence, Path.GetRelativePath(directory.Path, image));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(image, target, true);
                    }
                    File.Copy(result.MarkdownPath, Path.Combine(evidence, "report.md"), true);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ConfigurationWindowListsEveryAnalysisAndShowsProgressAndDefaultDirectory()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MarkdownReportImageTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            application.MultiConfiguration.Add();
            var settings = new AppSettings
            {
                AnalysisSettings = new() { ["Spot Diagram"] = new() { ["RayDensity"] = "3" } },
                ReportExport = new() { AnalysisKeys = [.. MarkdownReportSettings.CommonAnalyses, "Zernike", "Zernike 系数"] }
            };
            var window = new MarkdownReportWindow(application, settings);
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                var controls = window.GetVisualDescendants().ToArray();
                Assert.Equal(MarkdownReportExporter.SuggestedDirectory(null), controls.OfType<TextBox>().Single(item => item.Name == "ReportOutputDirectory").Text);
                Assert.Single(controls.OfType<ProgressBar>(), item => item.Name == "ReportExportProgress");
                Assert.True(controls.OfType<Button>().Single(item => item.Name == "ExportMarkdownReport").IsEnabled);
                var checks = controls.OfType<CheckBox>().Where(item => AutomationProperties.GetAutomationId(item)?.StartsWith("report-analysis-") == true).ToArray();
                Assert.Equal(application.Analyses.AnalysisNames.Count, checks.Length);
                Assert.DoesNotContain(checks, check => AutomationProperties.GetAutomationId(check) is "report-analysis-Zernike" or "report-analysis-Zernike 系数");
                foreach (var key in new[] { "Zernike Fringe", "Zernike Standard", "Zernike Annular" })
                    Assert.Contains(checks, check => AutomationProperties.GetAutomationId(check) == "report-analysis-" + key);
                Assert.Equal(4, checks.Count(item => item.IsChecked == true));
                Assert.Equal(3, controls.OfType<NumericUpDown>().Single(item => AutomationProperties.GetName(item) == "光线密度").Value);
                var configuration = controls.OfType<ComboBox>().Single(item => AutomationProperties.GetName(item) == "结构");
                configuration.SelectedIndex = configuration.ItemCount - 1;
                Dispatcher.UIThread.RunJobs();
                Assert.NotSame(configuration, window.GetVisualDescendants().OfType<ComboBox>().Single(item => AutomationProperties.GetName(item) == "结构"));
                foreach (var button in controls.OfType<Button>().Where(item => AutomationProperties.GetAutomationId(item)?.StartsWith("report-settings-") == true))
                {
                    var scroll = button.GetVisualAncestors().OfType<ScrollViewer>().First();
                    Assert.True(button.TranslatePoint(new Point(button.Bounds.Width, 0), scroll)!.Value.X <= scroll.Viewport.Width);
                }
                using var image = new RenderTargetBitmap(new PixelSize(980, 740), new Vector(96, 96));
                image.Render(window);
                var evidence = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(evidence))
                {
                    Directory.CreateDirectory(evidence);
                    image.Save(Path.Combine(evidence, "export-window.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
