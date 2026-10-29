using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
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

    [Theory]
    [InlineData(UiLocalization.Chinese, DesignReportFormat.Word, 0, ".docx")]
    [InlineData(UiLocalization.English, DesignReportFormat.Pdf, 1, ".pdf")]
    [InlineData(UiLocalization.Japanese, DesignReportFormat.Markdown, 2, ".md")]
    [InlineData(UiLocalization.Chinese, DesignReportFormat.Html, 3, ".html")]
    public async Task ReportRibbonOpensOneIdleSettingsWindowAndExportsOnlyOnStart(string language, DesignReportFormat format, int index, string extension)
    {
        using var directory = new ReportTestDirectory();
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MarkdownReportImageTests));
        await session.Dispatch(async () =>
        {
            var previous = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY");
            Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(directory.Path, "settings"));
            using var application = WorkbenchApplication.Create("cooke");
            var settings = new AppSettings
            {
                Language = language,
                ReportExport = new() { AnalysisKeys = [], Include2DLayout = false, Include3DLayout = false }
            };
            var main = new global::OpticalSystemDesign.App.MainWindow(settings, application,
                new WorkspaceSessionStore(Path.Combine(directory.Path, "workspace")));
            try
            {
                await application.Documents.SaveAsync(Path.Combine(directory.Path, "source.staropt"));
                main.Show(); Dispatcher.UIThread.RunJobs();
                var ribbon = main.GetVisualDescendants().OfType<TabControl>().Single(item => item.Classes.Contains("ribbon-tabs"));
                ribbon.SelectedItem = ribbon.Items.Cast<TabItem>().Single(item => AutomationProperties.GetName(item) == UiLocalization.Text("数据库"));
                main.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var buttons = main.GetVisualDescendants().OfType<Button>().ToArray();
                var entry = Assert.Single(buttons, item => AutomationProperties.GetAutomationId(item) == "ribbon-export-markdown-report");
                Assert.Equal(UiLocalization.Text("导出设计报告…"), AutomationProperties.GetName(entry));
                Assert.DoesNotContain(buttons, item => AutomationProperties.GetAutomationId(item) == "ribbon-configure-markdown-report");

                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var window = Assert.Single(main.OwnedWindows.OfType<MarkdownReportWindow>());
                Assert.Same(main, window.Owner);
                var controls = window.GetVisualDescendants().ToArray();
                var start = controls.OfType<Button>().Single(item => item.Name == "ExportMarkdownReport");
                var output = controls.OfType<TextBox>().Single(item => item.Name == "ReportOutputDirectory");
                var progress = controls.OfType<ProgressBar>().Single(item => item.Name == "ReportExportProgress");
                var formats = controls.OfType<ComboBox>().Single(item => item.Name == "ReportExportFormat");
                Assert.Equal(new[] { "Word (.docx)", "PDF (.pdf)", "Markdown (.md)", "HTML (.html)" }, formats.Items.Cast<string>());
                Assert.Equal(2, formats.SelectedIndex);
                Assert.Equal(UiLocalization.Text("导出格式"), AutomationProperties.GetName(formats));
                Assert.Equal(UiLocalization.Text("开始导出"), start.Content);
                Assert.True(start.IsEnabled); Assert.False(progress.IsVisible);
                Assert.Empty(Directory.GetFiles(directory.Path, "*.md"));

                var chosen = Path.Combine(directory.Path, "chosen-output");
                output.Text = chosen;
                formats.SelectedIndex = index;
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(window, Assert.Single(main.OwnedWindows.OfType<MarkdownReportWindow>()));
                Assert.Equal(chosen, output.Text);
                Assert.Equal(index, formats.SelectedIndex);
                Assert.True(start.IsEnabled); Assert.False(progress.IsVisible);
                Assert.False(Directory.Exists(chosen));

                start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(start.IsEnabled);
                Assert.False(formats.IsEnabled);
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(window, Assert.Single(main.OwnedWindows.OfType<MarkdownReportWindow>()));
                for (var i = 0; i < 1000 && !start.IsEnabled; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                Assert.True(start.IsEnabled); Assert.False(progress.IsVisible);
                Assert.Single(Directory.GetFiles(chosen, "*" + extension));
                Assert.Empty(Directory.GetFiles(directory.Path, "*" + extension));
                Assert.Equal(format, settings.ReportExport.Format);
                window.Close();
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var reopened = Assert.Single(main.OwnedWindows.OfType<MarkdownReportWindow>());
                Assert.Equal(index, reopened.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "ReportExportFormat").SelectedIndex);
                Assert.True(reopened.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportMarkdownReport").IsEnabled);
            }
            finally
            {
                foreach (var window in main.OwnedWindows.ToArray()) window.Close();
                main.Close();
                Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", previous);
                UiLocalization.Apply(UiLocalization.Chinese);
            }
        }, CancellationToken.None);
    }

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

    [Theory]
    [InlineData(DesignReportFormat.Markdown)]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Html)]
    public async Task FullReportRendersRealLayoutsPlotsAndPaginatedTextWithVisiblePixels(DesignReportFormat format)
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
                var options = new MarkdownReportSettings { Format = format, AnalysisKeys = [.. MarkdownReportSettings.CommonAnalyses, "Seidel Coefficients", "Wavefront Map"] };
                var renderer = new ReportImageRenderer(host);
                var images = new List<(string Name, byte[] Bytes)>();
                var result = await Task.Run(() => MarkdownReportExporter.ExportAsync(snapshot, options, directory.Path,
                    async (request, output, stem, token) =>
                    {
                        var files = await renderer.WriteAsync(request, output, stem, token);
                        foreach (var file in files)
                        {
                            var bytes = await File.ReadAllBytesAsync(Path.Combine(output, file), token);
                            if (request.Scene is not null)
                            {
                                // The self-contained report image must retain its complete title block,
                                // including the company mark in the right-hand region beneath the scene.
                                using var bitmap = SkiaSharp.SKBitmap.Decode(bytes);
                                Assert.True(bitmap.Height >= 650 + 132);
                                Assert.True(Enumerable.Range(660, 40).SelectMany(y => Enumerable.Range(850, 330).Select(x => bitmap.GetPixel(x, y)))
                                    .Count(color => color.Alpha > 0 && (color.Red < 180 || color.Green < 180 || color.Blue < 180)) > 100);
                            }
                            images.Add((file, bytes));
                        }
                        return files;
                    }));
                Assert.Equal(0, result.Failed);
                Assert.Equal(8, result.Succeeded);
                Assert.True(images.Count >= 8);
                foreach (var image in images)
                {
                    using var bitmap = new Bitmap(new MemoryStream(image.Bytes));
                    ExportImageAssertions.HasCompleteBorder(bitmap);
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
                    evidence = Path.Combine(evidence, format.ToString());
                    Directory.CreateDirectory(evidence);
                    foreach (var image in images)
                    {
                        var target = Path.Combine(evidence, Path.GetFileNameWithoutExtension(result.ReportPath) + "_assets", image.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.WriteAllBytes(target, image.Bytes);
                    }
                    File.Copy(result.ReportPath, Path.Combine(evidence, Path.GetFileName(result.ReportPath)), true);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ConfigurationWindowExcludesPrescriptionAndShowsProgressAndDefaultDirectory()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MarkdownReportImageTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create("cooke");
            application.MultiConfiguration.Add();
            var settings = new AppSettings
            {
                AnalysisSettings = new() { ["Spot Diagram"] = new() { ["RayDensity"] = "3" } },
                ReportExport = new() { AnalysisKeys = [.. MarkdownReportSettings.CommonAnalyses, "Zernike", "Zernike 系数", "Prescription Report", "处方报告"] }
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
                Assert.Equal(application.Analyses.AnalysisNames.Count(key => MarkdownReportExporter.IsSupportedAnalysis(application.Analyses.CanonicalKey(key))), checks.Length);
                Assert.DoesNotContain(checks, check => AutomationProperties.GetAutomationId(check) == "report-analysis-Prescription Report");
                Assert.Contains("Prescription Report", application.Analyses.AnalysisNames.Select(application.Analyses.CanonicalKey));
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
