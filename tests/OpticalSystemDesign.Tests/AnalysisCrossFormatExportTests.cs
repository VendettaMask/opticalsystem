using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisCrossFormatExportTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(DesignReportFormat.Word)]
    [InlineData(DesignReportFormat.Pdf)]
    [InlineData(DesignReportFormat.Markdown)]
    [InlineData(DesignReportFormat.Html)]
    public async Task GaussRepairsReachEveryReportUsingTheSameCompleteDrawingAsOrdinaryPng(DesignReportFormat format)
    {
        using var directory = new ReportTestDirectory();
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisCrossFormatExportTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(SequentialExampleCatalog.Examples.Single(example =>
                example.RelativePath == "Objectives/Double Gauss 28 degree field.zmx").FilePath);
            var before = app.Documents.GetRecoveryState();
            using var snapshot = app.CaptureReportSnapshot();
            var host = new Border { Width = 1200, Height = 1000 };
            var window = new Window { Width = 1300, Height = 1100, Content = host };
            var renderer = new ReportImageRenderer(host);
            var drawings = new Dictionary<string, byte[]>();
            var imageFailures = new List<string>();
            window.Show();
            try
            {
                var report = await MarkdownReportExporter.ExportAsync(snapshot, new()
                {
                    Format = format,
                    AnalysisKeys = ["Through Focus", "Incoherent Irradiance"],
                    Include2DLayout = false,
                    Include3DLayout = false
                }, directory.Path, async (request, output, stem, token) =>
                {
                    try
                    {
                        var result = Assert.IsType<AnalysisResultDto>(request.Analysis);
                        Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
                        var irradiance = result.View.PresentationKind != AnalysisPresentationKind.ThroughFocusSpot;
                        Assert.Equal(irradiance ? 9 : 15, result.View.PlotPanes.Count);
                        if (irradiance)
                        {
                            Assert.Contains(result.View.Rows, row => row.Metric == "探测器窗口来源" && row.Value.Contains("未添加物理孔径"));
                            Assert.All(result.View.PlotPanes, pane => Assert.Contains(pane.Series.SelectMany(series => series.Points), point => point.Value is > 0));
                        }
                        var ordinary = await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), result.View.Name);
                            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
                            panel.PresentResult(result.View, request.Document, request.CapturedAt);
                            host.Child = panel;
                            for (var pass = 0; pass < 3; pass++)
                            {
                                window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); await Task.Delay(16, token);
                            }
                            var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                            Assert.False(sheet.HasOverflow);
                            using var image = panel.CapturePlotImage();
                            Assert.Equal(new PixelSize(2404, 1804), image.PixelSize);
                            ExportImageAssertions.HasCompleteBorder(image);
                            using var png = new MemoryStream();
                            image.Save(png, PngBitmapEncoderOptions.Default);
                            host.Child = null;
                            return png.ToArray();
                        });
                        var file = Assert.Single(await renderer.WriteAsync(request, output, stem, token));
                        var bytes = await File.ReadAllBytesAsync(Path.Combine(output, file), token);
                        using var actual = SKBitmap.Decode(bytes);
                        using var expected = SKBitmap.Decode(ordinary);
                        Assert.Equal(2404, actual.Width); Assert.Equal(1804, actual.Height);
                        var identical = actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan());
                        if (!identical && Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CROSS_FORMAT_CAPTURE_DIR") is { } diagnostic)
                        {
                            Directory.CreateDirectory(diagnostic);
                            await File.WriteAllBytesAsync(Path.Combine(diagnostic, format + "-" + stem + "-ordinary.png"), ordinary, token);
                            await File.WriteAllBytesAsync(Path.Combine(diagnostic, format + "-" + stem + "-report.png"), bytes, token);
                        }
                        Assert.True(identical, "Report rendering must preserve the ordinary PNG's complete drawing pixels.");
                        drawings.Add(file, bytes);
                        return [file];
                    }
                    catch (Exception error) { imageFailures.Add(error.ToString()); throw; }
                });
                Assert.True(report.Succeeded == 2, string.Join("\n", imageFailures));
                Assert.Equal(2, report.Succeeded); Assert.Equal(0, report.Failed);
                Assert.Equal(2, drawings.Count);
                Assert.Equal(before, app.Documents.GetRecoveryState());
                await VerifyPackage(report.ReportPath, format, drawings);
                if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CROSS_FORMAT_CAPTURE_DIR") is { } capture)
                {
                    var target = Path.Combine(capture, format.ToString());
                    Directory.CreateDirectory(target);
                    File.Copy(report.ReportPath, Path.Combine(target, Path.GetFileName(report.ReportPath)), true);
                    var assets = Path.Combine(target, Path.GetFileNameWithoutExtension(report.ReportPath) + "_assets");
                    Directory.CreateDirectory(assets);
                    foreach (var drawing in drawings) await File.WriteAllBytesAsync(Path.Combine(assets, drawing.Key), drawing.Value);
                }
            }
            finally { host.Child = null; window.Close(); }
        }, CancellationToken.None);
    }

    private static async Task VerifyPackage(string path, DesignReportFormat format, Dictionary<string, byte[]> drawings)
    {
        if (format == DesignReportFormat.Word)
        {
            using var zip = ZipFile.OpenRead(path);
            var embedded = new List<string>();
            foreach (var entry in zip.Entries.Where(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal)))
            {
                using var stream = entry.Open(); using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                embedded.Add(Convert.ToHexString(SHA256.HashData(memory.ToArray())));
            }
            Assert.Equal(2, embedded.Count);
            Assert.All(drawings.Values, png => Assert.Contains(Convert.ToHexString(SHA256.HashData(png)), embedded));
        }
        else if (format == DesignReportFormat.Html)
        {
            var html = await File.ReadAllTextAsync(path);
            Assert.All(drawings.Values, png => Assert.Contains("data:image/png;base64," + Convert.ToBase64String(png), html));
        }
        else if (format == DesignReportFormat.Markdown)
        {
            var text = await File.ReadAllTextAsync(path);
            var assets = Path.ChangeExtension(path, null) + "_assets";
            foreach (var drawing in drawings)
            {
                Assert.Contains(drawing.Key, text);
                Assert.Equal(drawing.Value, await File.ReadAllBytesAsync(Path.Combine(assets, drawing.Key)));
            }
        }
        else
        {
            var pdf = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path));
            Assert.Contains("/Width 2404", pdf); Assert.Contains("/Height 1804", pdf);
            Assert.DoesNotContain("/DCTDecode", pdf);
        }
    }
}
