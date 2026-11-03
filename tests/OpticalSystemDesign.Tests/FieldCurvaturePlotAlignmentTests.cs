using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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
public sealed class FieldCurvaturePlotAlignmentTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Light", 13, false)]
    [InlineData("Dark", 18, true)]
    [InlineData("Pixel", 13, true)]
    [InlineData("Isekai", 22, false)]
    public async Task MixedTitlesKeepPlotBordersAlignedAtDifferentFontsAndContainerSizes(
        string theme, double fontSize, bool multiline)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FieldCurvaturePlotAlignmentTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            var title = multiline ? "Field Curvature\n场曲 / extended title" : "Field Curvature";
            var series = new AnalysisSeriesDto("Image Plane Delta", "Field Angle", [new(0, 0), new(.2, 14)],
                XUnit: AnalysisAxisUnit.Millimeter, YUnit: AnalysisAxisUnit.Degree);
            var panes = new[]
            {
                new AnalysisPlotPaneDto("left", [series], new AnalysisPlotOptionsDto(Title: title)),
                new AnalysisPlotPaneDto("right", [series], new AnalysisPlotOptionsDto())
            };
            var root = Assert.IsAssignableFrom<Control>(typeof(AnalysisPanel)
                .GetMethod("BuildPanePlot", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [panes, 2, false, true]));
            var window = new Window { Content = root };
            try
            {
                window.Show();
                double? previousHeight = null;
                foreach (var size in new[] { (960, 620), (780, 420), (1400, 800) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Render(window);
                    var plots = root.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                    AssertAligned(plots, root);
                    Assert.All(plots, plot => Assert.Same(series, Assert.Single(plot.Series)));
                    Assert.Equal(title, Assert.Single(root.GetVisualDescendants().OfType<TextBlock>(),
                        text => !string.IsNullOrWhiteSpace(text.Text)).Text);
                    var height = PlotBounds(plots[0], root).Height;
                    if (previousHeight.HasValue) Assert.NotEqual(previousHeight.Value, height);
                    previousHeight = height;
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ActualDoubleGaussKeepsAlignedSourceAndIdenticalLiveAndReportExportsAcrossResize()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FieldCurvaturePlotAlignmentTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var original = app.Documents.GetRecoveryState();
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Field Curvature and Distortion",
                new Dictionary<string, string>()));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(result.View.PlotPanes[0].PlotOptions.Title));
            Assert.True(string.IsNullOrWhiteSpace(result.View.PlotPanes[1].PlotOptions.Title));
            var generatedAt = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8));
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), "Field Curvature and Distortion");
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), generatedAt);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                byte[]? referencePixels = null;
                Rect? referenceBounds = null;
                foreach (var size in new[] { (1600, 1000), (520, 360), (1900, 1150) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; Render(window);
                    var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                    var plots = sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                    AssertAligned(plots, sheet);
                    referenceBounds ??= PlotBounds(plots[0], sheet);
                    Assert.Equal(referenceBounds.Value, PlotBounds(plots[0], sheet));
                    for (var index = 0; index < plots.Length; index++)
                        Assert.Equal(result.View.PlotPanes[index].Series, plots[index].Series);
                    using var image = panel.CapturePlotImage();
                    Assert.Equal(new PixelSize(1920, 1080), image.PixelSize);
                    referencePixels ??= PixelHash(image);
                    Assert.Equal(referencePixels, PixelHash(image));
                    var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_PLOT_ALIGNMENT_CAPTURE_DIR");
                    if (!string.IsNullOrWhiteSpace(directory) && size.Item1 == 1600)
                    {
                        Directory.CreateDirectory(directory);
                        image.Save(Path.Combine(directory, "field-curvature-aligned.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                var report = Assert.IsType<ResponsiveAnalysisPlotHost>(AnalysisPanel.CreateReportPlot(
                    result, app.Documents.GetSnapshot(), generatedAt));
                window.Content = report; Render(window);
                var reportSheet = Assert.IsType<UniformDiagramPage>(report.Child);
                reportSheet.ValidateForExport();
                var reportPlots = reportSheet.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                AssertAligned(reportPlots, reportSheet);
                Assert.Equal(referenceBounds!.Value, PlotBounds(reportPlots[0], reportSheet));
                using var reportImage = ExportImageFrame.CaptureForReport(reportSheet, report);
                Assert.Equal(referencePixels, PixelHash(reportImage));
                Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertAligned(AnalysisPlotControl[] plots, Control root)
    {
        Assert.Equal(2, plots.Length);
        var left = PlotBounds(plots[0], root);
        var right = PlotBounds(plots[1], root);
        Assert.InRange(Math.Abs(left.Top - right.Top), 0, .01);
        Assert.InRange(Math.Abs(left.Bottom - right.Bottom), 0, .01);
        Assert.InRange(Math.Abs(left.Height - right.Height), 0, .01);
        Assert.InRange(Math.Abs(left.Width - right.Width), 0, .01);
        Assert.True(left.Right < right.Left);
    }

    private static Rect PlotBounds(AnalysisPlotControl plot, Control root)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) plot.Render(context);
        var bounds = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(plot)!;
        return new Rect(plot.TranslatePoint(bounds.TopLeft, root)!.Value, bounds.Size);
    }

    private static void Render(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static byte[] PixelHash(Bitmap bitmap)
    { return SHA256.HashData(ExportImageAssertions.FramePixels(bitmap)); }
}
