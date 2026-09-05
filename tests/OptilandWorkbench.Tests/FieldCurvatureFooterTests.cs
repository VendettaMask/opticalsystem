using System.Globalization;
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
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class FieldCurvatureFooterTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_FIELD_CURVATURE_FOOTER_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture)
        {
            builder.UseSkia();
        }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task CombinedFooterShowsTwoCompactSummariesFromTheActualLensResult()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX")), ".zmx");
        var raw = new WorkbenchRuntime(optic).BuildAnalysisView("Field Curvature and Distortion");
        var mapper = typeof(WorkbenchApplication).Assembly.GetType(
            "OptilandWorkbench.Application.Services.WorkbenchMapper")!;
        var view = Assert.IsType<AnalysisViewDto>(mapper.GetMethod(
            "ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { raw })) with
        {
            PresentationKind = WorkbenchAnalysisCatalog.PresentationKind("Field Curvature and Distortion")
        };
        var originalRows = view.Rows;
        var originalPanes = view.PlotPanes;
        var originalReport = view.ReportText;
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FieldCurvatureFooterTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var footer = BuildFooter(view);
            var window = new Window { Width = 1200, Height = 160, Content = footer };
            try
            {
                window.Show();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var outer = Assert.IsType<Grid>(footer.Child);
                Assert.Equal(132, outer.Height);
                Assert.Equal(2, outer.ColumnDefinitions.Count);
                var grid = Summary(footer);
                Assert.Equal(2, grid.ColumnDefinitions.Count);
                var curvature = Body(grid, 0);
                var distortion = Body(grid, 1);
                Assert.Equal(5, curvature.Children.Count);
                Assert.Equal(3, distortion.Children.Count);
                Assert.Equal("2026/9/4", Text(curvature, 0));
                Assert.Equal("最大视场是 15.000 毫米。", Text(curvature, 1));
                Assert.Equal(Text(curvature, 1), Text(distortion, 1));
                Assert.Equal($"弧矢场曲 = {Metric("场曲.最大弧矢场曲 (mm)")} 毫米", Text(curvature, 2));
                Assert.Equal($"子午场曲 = {Metric("场曲.最大子午场曲 (mm)")} 毫米", Text(curvature, 3));
                Assert.Equal("图例对应于波长", Text(curvature, 4));
                Assert.Matches(@"^最大畸变 = \d+\.\d{4}%$", Text(distortion, 2));
                foreach (var label in new[] { curvature, distortion }.SelectMany(body => body.Children.OfType<TextBlock>()))
                {
                    Assert.DoesNotContain("最大像面偏移", label.Text!);
                    Assert.DoesNotContain("畸变模型", label.Text!);
                    Assert.DoesNotContain("其余", label.Text!);
                    Assert.DoesNotContain("—", label.Text!);
                    var position = label.TranslatePoint(default, footer)!.Value;
                    Assert.InRange(position.Y + label.Bounds.Height, 0, footer.Bounds.Height);
                    Assert.True(label.Bounds.Width <= Assert.IsAssignableFrom<Control>(label.Parent).Bounds.Width);
                }
                Assert.Same(originalRows, view.Rows);
                Assert.Same(originalPanes, view.PlotPanes);
                Assert.Equal(originalReport, view.ReportText);

                var directory = Environment.GetEnvironmentVariable("OPTILAND_FIELD_CURVATURE_FOOTER_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    bitmap.Save(Path.Combine(directory, "field-curvature-footer.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        string Metric(string key) => double.Parse(view.Rows.Single(row => row.Metric == key).Value,
            CultureInfo.InvariantCulture).ToString("0.0000", CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData(600, "Light")]
    [InlineData(1200, "Light")]
    [InlineData(2048, "Light")]
    [InlineData(1200, "Dark")]
    [InlineData(1200, "Isekai")]
    [InlineData(1200, "Pixel")]
    public async Task ProductAndDocumentRegionKeepsTheSameBoundsAcrossAnalysisKinds(double width, string theme)
    {
        var view = new AnalysisViewDto("test", Array.Empty<AnalysisRowDto>(), "",
            Array.Empty<AnalysisSeriesDto>(), new AnalysisPlotOptionsDto(),
            Array.Empty<AnalysisPlotPaneDto>(), 1);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FieldCurvatureFooterTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            var window = new Window { Width = width, Height = 160 };
            Rect? expectedBounds = null;
            Size? expectedLogoSize = null;
            (string?, double, FontFamily, FontWeight, IBrush?)[]? expectedTextStyle = null;
            IImage? expectedLogo = null;
            try
            {
                window.Show();
                foreach (var kind in new[]
                {
                    AnalysisPresentationKind.Standard,
                    AnalysisPresentationKind.RayFan,
                    AnalysisPresentationKind.OpticalPathDifference,
                    AnalysisPresentationKind.Interferogram,
                    AnalysisPresentationKind.AngleVsImageHeight,
                    AnalysisPresentationKind.FieldCurvature,
                    AnalysisPresentationKind.FieldCurvatureAndDistortion
                })
                {
                    var footer = BuildFooter(view with { PresentationKind = kind });
                    window.Content = footer;
                    window.UpdateLayout();
                    var grid = Assert.IsType<Grid>(footer.Child);
                    var right = grid.Children.OfType<Border>().Single(child =>
                        Grid.GetColumn(child) == grid.ColumnDefinitions.Count - 1
                        && Grid.GetColumnSpan(child) == 1);
                    expectedBounds ??= right.Bounds;
                    Assert.Equal(expectedBounds.Value, right.Bounds);
                    Assert.Equal(AnalysisPanel.AnalysisFooterHeight, right.Bounds.Height);
                    Assert.InRange(right.Bounds.Width, grid.Bounds.Width / 3 - 1, grid.Bounds.Width / 3 + 1);
                    Assert.DoesNotContain(right.GetVisualDescendants(), child => child is Viewbox);
                    var details = Assert.IsType<Grid>(right.Child);
                    Assert.Equal(64, details.RowDefinitions[0].ActualHeight);
                    var logo = Assert.Single(right.GetVisualDescendants().OfType<Image>());
                    Assert.Equal(180, logo.Width);
                    Assert.Equal(28, logo.Height);
                    expectedLogoSize ??= logo.Bounds.Size;
                    Assert.Equal(expectedLogoSize.Value, logo.Bounds.Size);
                    Assert.NotNull(logo.Source);
                    expectedLogo ??= logo.Source;
                    Assert.Same(expectedLogo, logo.Source);
                    var textStyle = right.GetVisualDescendants().OfType<TextBlock>()
                        .Select(text => (text.Text, text.FontSize, text.FontFamily, text.FontWeight, text.Foreground))
                        .ToArray();
                    Assert.Equal(3, textStyle.Length);
                    expectedTextStyle ??= textStyle;
                    Assert.Equal(expectedTextStyle, textStyle);
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_FIELD_CURVATURE_FOOTER_CAPTURE_DIR");
                    if (width == 1200 && theme == "Light" && !string.IsNullOrWhiteSpace(directory)
                        && kind is AnalysisPresentationKind.Standard or AnalysisPresentationKind.FieldCurvatureAndDistortion)
                    {
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Directory.CreateDirectory(directory);
                        using var bitmap = window.CaptureRenderedFrame();
                        Assert.NotNull(bitmap);
                        bitmap.Save(Path.Combine(directory, $"footer-{kind}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(AnalysisAxisUnit.Millimeter, AnalysisAxisUnit.Percent, "毫米", "0.4019%")]
    [InlineData(AnalysisAxisUnit.Degree, AnalysisAxisUnit.Percent, "度", "0.4019%")]
    [InlineData(AnalysisAxisUnit.Millimeter, AnalysisAxisUnit.Millimeter, "毫米", "0.4019 毫米")]
    public async Task SummaryUsesPlotUnitsAndDisplayedFieldInsteadOfLocalizedLabelsOrConvertedMetadata(
        AnalysisAxisUnit fieldUnit, AnalysisAxisUnit distortionUnit, string expectedFieldUnit, string expectedDistortion)
    {
        var series = new AnalysisSeriesDto("wrong (mm)", "wrong (deg)",
            new[] { new AnalysisPointDto(0, 0), new AnalysisPointDto(-0.4019, -15) },
            XUnit: distortionUnit, YUnit: fieldUnit);
        var view = new AnalysisViewDto("renamed", new[]
        {
            new AnalysisRowDto("场曲.最大弧矢场曲 (mm)", "0.014321"),
            new AnalysisRowDto("场曲.最大子午场曲 (mm)", "-0.029612"),
            new AnalysisRowDto("畸变.最大视场角 (deg)", "99"),
            new AnalysisRowDto("畸变.畸变模型", "f-tan")
        }, "report", Array.Empty<AnalysisSeriesDto>(), new AnalysisPlotOptionsDto(), new[]
        {
            new AnalysisPlotPaneDto("renamed curvature", new[] { series }, new AnalysisPlotOptionsDto()),
            new AnalysisPlotPaneDto("renamed distortion", new[] { series }, new AnalysisPlotOptionsDto())
        }, 2, PresentationKind: AnalysisPresentationKind.FieldCurvatureAndDistortion);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FieldCurvatureFooterTests));
        await session.Dispatch(() =>
        {
            var grid = Summary(BuildFooter(view));
            var curvature = Body(grid, 0);
            var distortion = Body(grid, 1);
            Assert.Equal($"最大视场是 15.000 {expectedFieldUnit}。", Text(curvature, 1));
            Assert.Equal(Text(curvature, 1), Text(distortion, 1));
            Assert.Equal("弧矢场曲 = 0.0143 毫米", Text(curvature, 2));
            Assert.Equal("子午场曲 = -0.0296 毫米", Text(curvature, 3));
            Assert.Equal($"最大畸变 = {expectedDistortion}", Text(distortion, 2));
            var header = grid.Children.OfType<Border>().Single(child => Grid.GetRow(child) == 0 && Grid.GetColumn(child) == 1);
            Assert.Equal("F-Tan(Theta) 畸变", Assert.IsType<TextBlock>(header.Child).Text);
        }, CancellationToken.None);
    }

    private static Border BuildFooter(AnalysisViewDto view)
    {
        var document = new OpticalDocumentSnapshot("Imported Zemax ZMX", "[MS-L7](10X大NA大视场).ZMX",
            0, "", false, false, 0, 0, 0, 0, 17, 3, 3);
        var factory = typeof(AnalysisPanel).GetMethod("BuildAnalysisTitleBlock",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return Assert.IsType<Border>(factory.Invoke(null, new object[]
        {
            view, document, new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero)
        }));
    }

    private static StackPanel Body(Grid grid, int column) => Assert.IsType<StackPanel>(
        grid.Children.OfType<Border>().Single(child => Grid.GetRow(child) == 1 && Grid.GetColumn(child) == column).Child);

    private static Grid Summary(Border footer) => Assert.IsType<Grid>(
        Assert.IsType<Grid>(footer.Child).Children[0]);

    private static string Text(StackPanel body, int index) => Assert.IsType<TextBlock>(body.Children[index]).Text!;
}
