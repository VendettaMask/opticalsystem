using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MtfPlotTitleTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(AnalysisPresentationKind.Mtf, false)]
    [InlineData(AnalysisPresentationKind.Mtf, true)]
    [InlineData(AnalysisPresentationKind.Standard, false)]
    [InlineData(AnalysisPresentationKind.Standard, true)]
    public async Task OnlyTypedMtfSinglePlotsSuppressDuplicateTitlesOnBothLegendPaths(AnalysisPresentationKind kind, bool selectable)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfPlotTitleTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create();
            var series = Series(selectable);
            var rawOptions = new AnalysisPlotOptionsDto(Title: "Original analysis heading", ShowLegend: true, XMinimum: 0, XMaximum: 100, YMinimum: 0, YMaximum: 1);
            // The visible label deliberately contains MTF even for the non-MTF control case.
            var view = new AnalysisViewDto("Custom MTF label", [], "Result", [series], rawOptions, [], 1, PresentationKind: kind);
            var root = AnalysisPanel.CreateResultPlot(view, app.Documents.GetSnapshot(), DateTimeOffset.Now)!;
            var window = new Window { Width = 1200, Height = 850, Content = root };
            try
            {
                window.Show(); Tick(window);
                var plot = Assert.Single(root.GetVisualDescendants().OfType<AnalysisPlotControl>());
                Assert.Equal(kind == AnalysisPresentationKind.Mtf ? "" : rawOptions.Title, plot.PlotOptions.Title);
                Assert.Equal(rawOptions.Title, view.PlotOptions.Title);
                Assert.Equal(rawOptions with { Title = plot.PlotOptions.Title, ShowLegend = plot.PlotOptions.ShowLegend }, plot.PlotOptions);
                Assert.Same(series, Assert.Single(plot.Series));
                Assert.Contains(root.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == view.Name);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MtfConfigurationPaneTitlesKeepTheirIdentification()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfPlotTitleTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create();
            var series = Series(false);
            var panes = new[] { "结构 1", "结构 2" }.Select(title =>
                new AnalysisPlotPaneDto(title, [series], new AnalysisPlotOptionsDto(Title: title))).ToArray();
            var view = new AnalysisViewDto("MTF", [], "Result", [], new(), panes, 2, PresentationKind: AnalysisPresentationKind.Mtf);
            var root = AnalysisPanel.CreateResultPlot(view, app.Documents.GetSnapshot(), DateTimeOffset.Now)!;
            var window = new Window { Width = 1200, Height = 850, Content = root };
            try
            {
                window.Show(); Tick(window);
                Assert.Equal(new[] { "结构 1", "结构 2" }, root.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(plot => plot.PlotOptions.Title));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("MTF")]
    [InlineData("Huygens MTF")]
    [InlineData("Geometric MTF")]
    public async Task ActualDoubleGaussMtfHasOneCaptionAndIdenticalLivePngAndReportDrawing(string analysis)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MtfPlotTitleTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var original = app.Documents.GetRecoveryState();
            // The FFT fixture must satisfy the shared pupil-phase sampling gate;
            // low-count Huygens/geometric inputs still suffice for UI coverage.
            var pupilSampling = analysis == "MTF" ? "256" : "32";
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, analysis, new Dictionary<string, string>
            {
                ["Sampling"] = pupilSampling, ["PupilSampling"] = pupilSampling, ["ImageSampling"] = "32", ["NumRays"] = "32",
                ["PlotPointCount"] = "32", ["WavelengthNumber"] = "1", ["FieldNumber"] = "0", ["MaximumFrequency"] = "100"
            }));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            Assert.Equal(AnalysisPresentationKind.Mtf, result.View.PresentationKind);
            var raw = result.View.PlotOptions;
            var generatedAt = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), analysis);
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), generatedAt);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                byte[]? pixels = null;
                double? top = null;
                foreach (var size in new[] { (1400, 1000), (650, 480) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Tick(window);
                    var plot = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
                    Assert.Empty(plot.PlotOptions.Title);
                    Assert.Equal(raw with { Title = "", ShowLegend = false }, plot.PlotOptions);
                    Assert.Equal(result.View.Series, plot.Series);
                    Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == result.View.Name);
                    var plotTop = PlotTop(plot);
                    top ??= plotTop; Assert.Equal(top.Value, plotTop);
                    using var image = panel.CapturePlotImage();
                    Assert.Equal(new PixelSize(1920, 1080), image.PixelSize);
                    pixels ??= PixelHash(image); Assert.Equal(pixels, PixelHash(image));
                    if (size.Item1 == 1400) Capture(image, analysis.Replace(' ', '-') + ".png");
                }
                var report = AnalysisPanel.CreateReportPlot(result, app.Documents.GetSnapshot(), generatedAt)!;
                window.Content = report; Tick(window);
                var reportPlot = Assert.Single(report.GetVisualDescendants().OfType<AnalysisPlotControl>());
                Assert.Empty(reportPlot.PlotOptions.Title);
                Assert.Equal(top!.Value, PlotTop(reportPlot));
                var sheet = Assert.IsType<UniformDiagramPage>(report.Child); sheet.ValidateForExport();
                using var reportImage = ExportImageFrame.CaptureForReport(sheet, report);
                Assert.Equal(pixels, PixelHash(reportImage));
                Assert.Equal(raw, result.View.PlotOptions);
                Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static AnalysisSeriesDto Series(bool selectable) => new("Frequency", "Modulation", [new(0, 1), new(100, 0)],
        Name: selectable ? "Series" : "", LegendKey: selectable ? "field:1:T" : "", LegendLabel: selectable ? "Field 1, T" : "",
        XQuantity: AnalysisAxisQuantity.SpatialFrequency, XUnit: AnalysisAxisUnit.CyclesPerMillimeter,
        YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless);

    private static double PlotTop(AnalysisPlotControl plot)
    {
        var drawing = new Avalonia.Media.DrawingGroup();
        using (var context = drawing.Open()) plot.Render(context);
        return ((Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!).Top;
    }

    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static byte[] PixelHash(Bitmap bitmap)
    { return SHA256.HashData(ExportImageAssertions.FramePixels(bitmap)); }

    private static void Capture(Bitmap image, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_MTF_TITLE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
