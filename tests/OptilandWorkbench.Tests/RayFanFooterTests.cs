using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class RayFanFooterTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_RAY_FAN_FOOTER_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture)
        {
            builder.UseSkia();
        }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Ray Fan", "PlotScaleMicrometers", "µm", "ray-fan-footer.png")]
    [InlineData("Optical Path Difference", "GraphScale", "Waves", "opd-fan-footer.png")]
    public async Task RealFanFooterShowsOnlyDateScaleWavelengthsAndSurfaceBelowTitle(
        string analysisName, string scaleKey, string unit, string captureFile)
    {
        var optic = Optic.CreateCookeTriplet();
        var runtime = new WorkbenchRuntime(optic);
        var raw = runtime.BuildAnalysisView(analysisName, new Dictionary<string, string>
        {
            [scaleKey] = "5",
            ["NumberOfRays"] = "10",
            ["WavelengthNumber"] = "所有",
            ["SurfaceNumber"] = "像面"
        });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper")!;
        var view = Assert.IsType<AnalysisViewDto>(mapper.GetMethod(
            "ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { raw })) with
        {
            PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(analysisName)
        };
        var originalRows = view.Rows;
        var originalReport = view.ReportText;
        var originalPanes = view.PlotPanes;
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RayFanFooterTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var footer = BuildFooter(view, optic.SurfaceGroup.Items.Count);
            var window = new Window { Width = 1000, Height = 160, Content = footer };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var left = Left(footer);
                Assert.Equal(3, left.Children.Count);
                Assert.Equal(view.Name, Assert.IsType<TextBlock>(left.Children[0]).Text);
                Assert.Equal("2026/9/4", Assert.IsType<TextBlock>(left.Children[1]).Text);
                var summary = Assert.IsType<StackPanel>(left.Children[2]);
                Assert.Equal($"最大缩放比例：± 5.000 {unit}", Assert.IsType<TextBlock>(summary.Children[0]).Text);
                Assert.Equal("面：像面", Assert.IsType<TextBlock>(summary.Children[2]).Text);
                var legend = Assert.IsType<WrapPanel>(summary.Children[1]);
                var series = view.PlotPanes[0].Series;
                Assert.Equal(series.Count, legend.Children.Count);
                for (var index = 0; index < series.Count; index++)
                {
                    var swatch = Assert.IsType<Border>(legend.Children[index]);
                    var label = Assert.IsType<TextBlock>(swatch.Child);
                    Assert.Matches(@"^0\.\d{3}$", label.Text!);
                    Assert.Equal(AnalysisPlotControl.SeriesColor(series[index]),
                        Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
                    Assert.Same(label.Foreground, swatch.BorderBrush);
                    Assert.Equal(new Thickness(0, 0, 0, 1), swatch.BorderThickness);
                }
                foreach (var label in left.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.DoesNotContain("采样", label.Text ?? string.Empty);
                    Assert.DoesNotContain("其余", label.Text ?? string.Empty);
                    Assert.DoesNotContain("视场数", label.Text ?? string.Empty);
                    var position = label.TranslatePoint(default, footer)!.Value;
                    Assert.InRange(position.Y + label.Bounds.Height, 0, footer.Bounds.Height);
                }
                Assert.Same(originalRows, view.Rows);
                Assert.Equal(originalReport, view.ReportText);
                Assert.Same(originalPanes, view.PlotPanes);
                Assert.Contains(view.Rows, row => row.Metric == "采样点数");

                var directory = Environment.GetEnvironmentVariable("OPTILAND_RAY_FAN_FOOTER_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    bitmap.Save(Path.Combine(directory, captureFile), PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(AnalysisAxisUnit.Millimeter, 0.005, "5.000 µm", AnalysisPresentationKind.RayFan)]
    [InlineData(AnalysisAxisUnit.Micrometer, 5, "5.000 µm", AnalysisPresentationKind.RayFan)]
    [InlineData(AnalysisAxisUnit.Milliradian, 5, "5.000 mrad", AnalysisPresentationKind.RayFan)]
    [InlineData(AnalysisAxisUnit.Wave, 0.75, "0.750 Waves", AnalysisPresentationKind.OpticalPathDifference)]
    [InlineData(AnalysisAxisUnit.Wave, 2.5, "2.500 Waves", AnalysisPresentationKind.OpticalPathDifference)]
    public async Task ScaleUsesTypedUnitsAndSurfaceFollowsTheSelectedSurface(
        AnalysisAxisUnit unit, double limit, string expected, AnalysisPresentationKind kind)
    {
        var series = new AnalysisSeriesDto("", "deliberately wrong (mm)", Array.Empty<AnalysisPointDto>(),
            Name: "0.5876 µm", YUnit: unit);
        var view = new AnalysisViewDto("renamed", new[] { new AnalysisRowDto("表面序号", "5") }, "full report",
            new[] { series }, new AnalysisPlotOptionsDto(),
            new[] { new AnalysisPlotPaneDto("field", new[] { series },
                new AnalysisPlotOptionsDto(YMinimum: -limit, YMaximum: limit)) }, 1,
            PresentationKind: kind);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RayFanFooterTests));
        await session.Dispatch(() =>
        {
            var footer = BuildFooter(view, 17);
            var summary = Assert.IsType<StackPanel>(Left(footer).Children[2]);
            Assert.Equal($"最大缩放比例：± {expected}", Assert.IsType<TextBlock>(summary.Children[0]).Text);
            Assert.Equal("面：5", Assert.IsType<TextBlock>(summary.Children[2]).Text);
            Assert.Equal(132, Assert.IsType<Grid>(footer.Child).Height);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task MatchingTitleDoesNotGiveOtherAnalysesAnOpdFooter()
    {
        var view = new AnalysisViewDto("Optical Path Difference", new[] { new AnalysisRowDto("采样点数", "41") },
            "report", Array.Empty<AnalysisSeriesDto>(), new AnalysisPlotOptionsDto(),
            Array.Empty<AnalysisPlotPaneDto>(), 1, PresentationKind: AnalysisPresentationKind.Standard);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RayFanFooterTests));
        await session.Dispatch(() =>
        {
            var left = Left(BuildFooter(view, 17));
            Assert.Equal("采样点数: 41", Assert.IsType<TextBlock>(left.Children[2]).Text);
        }, CancellationToken.None);
    }

    private static Border BuildFooter(AnalysisViewDto view, int surfaceCount)
    {
        var document = new OpticalDocumentSnapshot("test", null, 0, "", false, false,
            0, 0, 0, 0, surfaceCount, 3, 3);
        var factory = typeof(AnalysisPanel).GetMethod("BuildAnalysisTitleBlock",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return Assert.IsType<Border>(factory.Invoke(null, new object[]
        {
            view, document, new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
        }));
    }

    private static StackPanel Left(Border footer) =>
        Assert.IsType<StackPanel>(Assert.IsType<Grid>(footer.Child).Children[0]);
}
