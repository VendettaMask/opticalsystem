using System.Globalization;
using System.Reflection;
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
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using VignettingDiagramAnalysis = OpticalSystemDesign.Core.Analysis.VignettingDiagramAnalysis;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisResultPresentationTests
{
    private const string CaptureDirectoryKey = "OPTICAL_ANALYSIS_RESULT_CAPTURE_DIR";
    private static string MsL7Source => Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX");

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectoryKey));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public void VignettingMeasuresPhysicalClippingAndIgnoresFieldEditorCompression()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(1);
        var first = new VignettingDiagramAnalysis(optic, fieldDensity: 3).GenerateData();
        var reference = Assert.Single(first.PlotSeries).Points;
        Assert.InRange(reference[0].Y, 1d / 309, 308d / 309);

        foreach (var field in optic.Fields)
        {
            field.VignetteFactorX = 0.5;
            field.VignetteFactorY = 0.5;
        }
        var second = new VignettingDiagramAnalysis(optic, fieldDensity: 3).GenerateData();
        Assert.Equal(reference.ToArray(), Assert.Single(second.PlotSeries).Points.ToArray());
        Assert.All(optic.Fields, field => Assert.Equal(0.5, field.VignetteFactorY));
    }

    [Fact]
    public async Task MsL7ShowsNativeVignettingCurveAndYYbarSurfaceReport()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var yybar = await Run(app, "Y-Ybar");
        var vignette = await Run(app, "Vignetting Diagram");

        var curve = Assert.Single(vignette.Series);
        Assert.Equal(AnalysisAxisUnit.Millimeter, curve.XUnit);
        Assert.Equal(AnalysisAxisUnit.Dimensionless, curve.YUnit);
        Assert.Equal("未渐晕光线分数", curve.YAxisLabel);
        Assert.Equal(0d, vignette.PlotOptions.YMinimum);
        Assert.Equal(1d, vignette.PlotOptions.YMaximum);
        Assert.Equal(21, curve.Points.Count);
        Assert.Contains(vignette.Rows, row => row.Metric == "每视场追迹光线数" && row.Value == "309");
        for (var index = 0; index < curve.Points.Count; index++)
        {
            Assert.Equal(index * 0.75, curve.Points[index].X, 12);
            Assert.Equal(1d, curve.Points[index].Y);
        }

        var table = Assert.IsType<AnalysisTableDto>(yybar.Table);
        Assert.Equal(new[] { "面号", "Y-Bar (mm)", "Y (mm)" }, table.Columns);
        Assert.Equal(16, table.Rows.Count);
        Assert.Equal(2.085534166, double.Parse(table.Rows[0][1], CultureInfo.InvariantCulture), 8);
        Assert.Equal(7.094260469, double.Parse(table.Rows[0][2], CultureInfo.InvariantCulture), 8);

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, "Y-Ybar", yybar, (window, panel) =>
            {
                var page = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.False(page.HasOverflow);
                var captions = page.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
                Assert.Contains("Y–Ybar 图", captions);
                Assert.Contains(captions, caption => caption is not null &&
                    caption.Contains("图框边长：20.000 mm") && caption.Contains("第一面：1") &&
                    caption.Contains("最后面：16") && caption.Contains("波长：0.587562 µm"));
                Assert.DoesNotContain(captions, caption => caption?.Contains("见“数据”页") == true);
                Capture(window, "yybar-plot");

                AssertReportRows(ShowReport(window, panel), table);
                Capture(window, "yybar-text");
            });
            WithPanel(app, "Vignetting Diagram", vignette, (window, panel) =>
            {
                Assert.False(Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>()).HasOverflow);
                Capture(window, "vignetting-plot");
            });
        }, CancellationToken.None);
    }

    [Fact]
    public async Task IncidentAngleReportDisplaysTheThreeTracedAnglesAtEachSignedImageHeight()
    {
        const string key = "Angle vs Image Height";
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var view = await Run(app, key);
        var table = Assert.IsType<AnalysisTableDto>(view.Table);
        Assert.Equal(new[] { "像高 (mm)", "较小光瞳点光线 (°)", "主光线 (°)", "较大光瞳点光线 (°)" }, table.Columns);
        Assert.Equal(21, table.Rows.Count);
        Assert.Equal(new[] { "0.000000E+00", "23.941540", "0.000000", "-23.941540" }, table.Rows[0]);
        Assert.Equal(new[] { "-1.491645E+00", "16.973821", "-7.597689", "-31.221499" }, table.Rows[^1]);

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, view, (window, panel) =>
            {
                var report = ShowReport(window, panel);
                Assert.Contains("波长：0.5876 µm", report);
                Assert.Contains("测量表面：16", report);
                AssertReportRows(report, table);
                Capture(window, "incident-angle-text");
            });
        }, CancellationToken.None);
    }

    private static string ShowReport(Window window, AnalysisPanel panel)
    {
        var tabs = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisResultTabs>());
        tabs.SelectedIndex = 2;
        Tick(window);
        return Assert.IsType<TextBox>(Assert.IsType<AnalysisResultTab>(tabs.Items[2]).Content).Text!;
    }

    [Fact]
    public async Task SurfaceDataUsesOneTextViewAndListsThePhysicalDataForEachSurface()
    {
        const string key = "Prescription Report";
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var view = await Run(app, key);
        Assert.Equal(AnalysisPresentationKind.TextReport, view.PresentationKind);
        var sections = view.ReportText.Split(Environment.NewLine + "表面 ", StringSplitOptions.None).Skip(1).ToArray();
        Assert.Equal(17, sections.Length);
        for (var index = 0; index < sections.Length; index++)
            Assert.StartsWith($"{index} · ", sections[index]);
        var firstLensSurface = sections[1];
        Assert.Contains("厚度：3.217101    直径：17.7", firstLensSurface);
        Assert.Contains("材料：N-SK18", firstLensSurface);
        AssertValue("Y 边缘厚度：", 1.00009, 5e-6);
        AssertValue("X 边缘厚度：", 1.00009, 5e-6);
        AssertValue("0.587562   ", 1.6385400267, 1e-9);
        AssertValue("元件体积（cm³，平边）：", 0.521252, 1e-6);
        AssertValue("组合光焦度：", 0.034745, 1e-6);
        AssertValue("EFL（mm）：", 28.781, 0.0005);
        AssertValue("F/#：", 1.6261, 0.00005);
        AssertValue("形状因子：", 0.4696, 0.00005);
        Assert.DoesNotContain("System.Double", view.ReportText);

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, view, (window, panel) =>
            {
                var report = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>(), box => box.IsReadOnly);
                Assert.Equal(view.ReportText, report.Text);
                Assert.Empty(panel.GetVisualDescendants().OfType<AnalysisResultTabs>());
                Assert.Empty(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                Assert.Empty(panel.GetVisualDescendants().OfType<DataGrid>());
                Capture(window, "surface-data-text");
            });
        }, CancellationToken.None);

        void AssertValue(string label, double expected, double tolerance)
        {
            var match = System.Text.RegularExpressions.Regex.Match(firstLensSurface,
                System.Text.RegularExpressions.Regex.Escape(label) + @"\s*(?<value>-?\d+(?:\.\d+)?)");
            Assert.True(match.Success, $"Missing '{label}' in {firstLensSurface}");
            var actual = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            Assert.InRange(actual, expected - tolerance, expected + tolerance);
        }
    }

    [Fact]
    public async Task GridDistortionAndContrastLossShowTheirPhysicalSummaries()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var grid = await Run(app, "Grid Distortion");
        var contrast = await Run(app, "Contrast Loss Map");
        Assert.Contains(grid.Rows, row => row.Metric == "波长序号" && row.Value == "2");
        Assert.Contains(grid.Rows, row => row.Metric == "网格尺寸" && row.Value == "13");
        Assert.Contains("最大畸变 (%)：-0.4019", grid.ReportText);
        Assert.Contains("空间频率：69.0658", contrast.ReportText);
        Assert.All(contrast.Series, series => Assert.Equal(97 * 97, series.Points.Count));

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, "Grid Distortion", grid, (window, panel) =>
            {
                AssertFooter(panel, "视场：21.21 × 21.21 mm", "像面：2.12 × 2.12 mm",
                    "最大畸变：-0.4019%", "SMIA TV 畸变：-0.1978%", "波长：0.5876 µm");
                Capture(window, "grid-distortion");
            });
            WithPanel(app, "Contrast Loss Map", contrast, (window, panel) =>
            {
                AssertFooter(panel, "空间频率：69.0658 cycles/mm", "瞳面采样：97 × 97",
                    "子午 MTF：0.8813", "弧矢 MTF：0.8813", "衍射极限：0.9364");
                var plots = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                Assert.Equal(2, plots.Length);
                var maximum = contrast.PlotPanes.SelectMany(pane => pane.Series).Max(series => series.ValueMaximum);
                Assert.All(plots, plot =>
                {
                    Assert.True(plot.PupilHeatmap);
                    Assert.False(plot.PlotOptions.HideAxes);
                    Assert.Equal(-1, plot.PlotOptions.XMinimum);
                    Assert.Equal(1, plot.PlotOptions.YMaximum);
                    var series = Assert.Single(plot.Series);
                    Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
                    Assert.Equal(0, series.ValueMinimum);
                    Assert.Equal(maximum, series.ValueMaximum);
                });
                Assert.Equal("Tangential", Assert.Single(plots[0].Series).Name);
                Assert.Equal("Sagittal", Assert.Single(plots[1].Series).Name);
                Capture(window, "contrast-loss-map");
            });
        }, CancellationToken.None);

    }

    [Fact]
    public async Task HuygensProfileFooterUsesTheSelectedSpectrumFieldAndImageCenter()
    {
        const string key = "Huygens PSF Cross Section";
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var onAxis = await Run(app, key);
        var settings = app.Analyses.MergeSettings(key, new Dictionary<string, string>
        {
            ["FieldNumber"] = "2", ["WavelengthNumber"] = "2", ["ProfileType"] = "Y"
        });
        var offAxis = (await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, settings))).View;
        Assert.Equal(AnalysisOutcome.Success, offAxis.Outcome);
        var centerY = offAxis.Rows.Single(row => row.Metric == "图心 Y").Value;
        Assert.InRange(Math.Abs(double.Parse(centerY, CultureInfo.InvariantCulture)), 1, 1.1);

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, onAxis, (window, panel) =>
            {
                AssertFooter(panel, "惠更斯 PSF 截面图 · X 截面", "波长：0.4861–0.6563 µm", "视场：(X 0.00, Y 0.00) mm",
                    "像宽：4.58 µm", "斯特列尔比：0.439", "中心坐标（主光线）：(X 0.000000, Y 0.000000) mm");
                Capture(window, "huygens-psf-cross-section");
            });
            WithPanel(app, key, offAxis, (_, panel) =>
                AssertFooter(panel, "惠更斯 PSF 截面图 · Y 截面", "波长：0.5876 µm", "视场：(X 0.00, Y -10.60) mm", $"Y {centerY}) mm"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FourierMtfVsFieldShowsSixIndependentFrequenciesAndPreservesSingleFrequencyPresets()
    {
        const string key = "Fourier MTF vs Field";
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var view = await Run(app, key);
        Assert.Equal(new[] { 10, 20, 30, 40, 50, 60 }.SelectMany(frequency =>
            new[] { $"{frequency} cycles/mm, T", $"{frequency} cycles/mm, S" }), view.Series.Select(series => series.Name));

        var oldPreset = new Dictionary<string, string> { ["SpatialFrequency"] = "20" };
        var migrated = app.Analyses.MergeSettings(key, oldPreset);
        Assert.Equal("20", migrated["Frequency1"]);
        Assert.All(Enumerable.Range(2, 5), slot => Assert.Equal("0", migrated[$"Frequency{slot}"]));
        var oldPresetView = (await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, oldPreset))).View;
        Assert.Equal(AnalysisOutcome.Success, oldPresetView.Outcome);
        Assert.Equal(new[] { "20 cycles/mm, T", "20 cycles/mm, S" }, oldPresetView.Series.Select(series => series.Name));
        Assert.Equal(view.Series[2].Points, oldPresetView.Series[0].Points);
        Assert.Equal(view.Series[3].Points, oldPresetView.Series[1].Points);

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, view, (window, panel) =>
            {
                AssertFooter(panel, "波长：0.4861–0.6563 µm", "空间频率：10、20、30、40、50、60 cycles/mm", "T 子午 · S 弧矢");
                Capture(window, "fourier-mtf-vs-field");

                var settings = Assert.IsType<Border>(typeof(AnalysisPanel)
                    .GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel));
                settings.IsVisible = true;
                Tick(window);
                var inputs = settings.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
                NumericUpDown Frequency(int slot) => Assert.Single(inputs, control =>
                    AutomationProperties.GetAutomationId(control) == $"analysis-parameter-Frequency{slot}");
                foreach (var slot in Enumerable.Range(1, 6))
                {
                    var input = Frequency(slot);
                    Assert.Equal(slot * 10m, input.Value);
                    Assert.True(input.IsEffectivelyVisible && input.IsEnabled);
                }
                Capture(window, "fourier-mtf-vs-field-settings");

                window.Width = 420;
                Tick(window);
                Assert.True(Frequency(4).TranslatePoint(default, settings)!.Value.Y
                    > Frequency(3).TranslatePoint(default, settings)!.Value.Y);
                var lastFrequency = Frequency(6);
                Assert.InRange(lastFrequency.TranslatePoint(new Point(lastFrequency.Bounds.Width, 0), settings)!.Value.X,
                    0, settings.Bounds.Width);
            });
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("FFT Surface MTF", true)]
    [InlineData("Huygens Surface MTF", true)]
    [InlineData("FFT MTF Map", false)]
    [InlineData("Geometric MTF Map", false)]
    public async Task MtfGridCommandsRenderCalculatedFrequencySurfacesOrFieldMaps(string key, bool frequencySurface)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var settings = app.Analyses.MergeSettings(key, new Dictionary<string, string>
        {
            ["WavelengthNumber"] = "2",
            ["XFieldSamples"] = "5",
            ["YFieldSamples"] = "7",
            ["XFieldWidth"] = "10",
            ["YFieldWidth"] = "15"
        });
        var result = await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, settings));
        var view = result.View;
        Assert.True(view.Outcome == AnalysisOutcome.Success, view.OutcomeReason ?? view.ReportText);
        Assert.Equal(AnalysisPresentationKind.MtfGrid, view.PresentationKind);
        var series = Assert.Single(view.Series);
        Assert.Equal(AnalysisSeriesKind.Heatmap, series.Kind);
        Assert.Equal(AnalysisAxisQuantity.Modulation, series.ValueQuantity);
        Assert.All(series.Points, point => Assert.InRange(point.Value!.Value, 0, 1));
        Assert.True(series.Points.Max(point => point.Value) > series.Points.Min(point => point.Value));
        Assert.Equal(series.Points.Count, view.Table!.Rows.Count);

        var center = Assert.Single(series.Points, point => point.X == 0 && point.Y == 0);
        if (frequencySurface)
        {
            Assert.Equal(AnalysisAxisQuantity.SpatialFrequency, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.CyclesPerMillimeter, series.YUnit);
            Assert.Equal(1, center.Value!.Value, 12);
        }
        else
        {
            Assert.Equal(AnalysisAxisQuantity.ObjectHeight, series.XQuantity);
            Assert.Equal(35, series.Points.Count);
            Assert.Equal(-4, series.Points[0].X, 10);
            Assert.Equal(2.0 / 3, view.PlotOptions.SpatialDisplayScaleX, 10);
            Assert.Equal(series.Points.Min(point => point.Value), series.ValueMinimum);
            Assert.Equal(series.Points.Max(point => point.Value), series.ValueMaximum);
            Assert.Equal(5, view.Table.Columns.Count);
            var row = view.Table.Rows[17];
            var tangential = double.Parse(row[3], CultureInfo.InvariantCulture);
            var sagittal = double.Parse(row[4], CultureInfo.InvariantCulture);
            Assert.Equal((tangential + sagittal) / 2, center.Value!.Value, 9);
        }

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, view, (window, panel) =>
            {
                var plot = Assert.Single(panel.GetVisualDescendants().OfType<WavefrontSurfaceControl>());
                Assert.True(plot.HasRenderableGrid);
                Assert.Equal(key == "FFT Surface MTF" ? "表面" : "伪彩色", plot.DisplayAs);
                AssertFooter(panel, "波长：0.5876 µm", "结果网格");
                Capture(window, key.ToLowerInvariant().Replace(' ', '-'));
            });
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RmsFieldMapFitsCompleteSquareIntervalsToPhysicalFieldBounds()
    {
        const string key = "RMS Field Map";
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(MsL7Source);
        var saved = app.Analyses.MergeSettings(key, new Dictionary<string, string>
        {
            ["Data"] = "spot",
            ["XFieldSamples"] = "11",
            ["YFieldSamples"] = "11"
        });
        Assert.Equal("spot", saved["Data"]);
        Assert.Equal("11", saved["XFieldSamples"]);
        Assert.Equal("11", saved["YFieldSamples"]);

        var view = await Run(app, key);
        var series = Assert.Single(view.Series);
        Assert.Equal(AnalysisAxisUnit.Wave, series.ValueUnit);
        Assert.Equal(21 * 21, series.Points.Count);
        Assert.Equal(15 / Math.Sqrt(2), series.Points.Max(point => point.X), 6);
        Assert.Equal(15 / Math.Sqrt(2), series.Points.Max(point => point.Y), 6);
        Assert.Equal(view.PlotOptions.XMinimum, series.Points.Min(point => point.X));
        Assert.Equal(view.PlotOptions.YMinimum, series.Points.Min(point => point.Y));

        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultPresentationTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            WithPanel(app, key, view, (window, panel) =>
            {
                var plot = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
                Assert.True(plot.UseHeatmapIntervals);
                var bounds = Assert.IsType<Rect>(typeof(AnalysisPlotControl)
                    .GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot));
                Assert.True(bounds.Width > 100);
                Assert.Equal(bounds.Width, bounds.Height, 8);
                AssertFooter(panel, "视场半宽：X 10.606602 mm，Y 10.606602 mm", "采样：21 × 21", "显示网格：20 × 20", "波前 RMS");
                Capture(window, "rms-field-map");
            });
        }, CancellationToken.None);
    }

    private static void AssertFooter(AnalysisPanel panel, params string[] metrics)
    {
        var page = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
        Assert.False(page.HasOverflow);
        var text = string.Join(Environment.NewLine, page.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.DoesNotContain("见“数据”页", text);
        foreach (var metric in metrics) Assert.Contains(metric, text);
    }

    private static void AssertReportRows(string report, AnalysisTableDto table)
    {
        Assert.Contains("文件：" + MsL7Source, report);
        Assert.Contains("日期：2026/11/8", report);
        var reportRows = report.Split(Environment.NewLine).TakeLast(table.Rows.Count);
        Assert.Equal(table.Rows.Select(row => string.Join(' ', row)),
            reportRows.Select(row => string.Join(' ', row.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
    }

    private static async Task<AnalysisViewDto> Run(WorkbenchApplication app, string key)
    {
        var result = await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key,
            app.Analyses.MergeSettings(key, null)));
        Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
        return result.View;
    }

    private static void WithPanel(WorkbenchApplication app, string key, AnalysisViewDto view, Action<Window, AnalysisPanel> check)
    {
        using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
            new AppSettings(), key, initialSettings: app.Analyses.MergeSettings(key, null));
        typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel, true);
        var generatedAt = new DateTimeOffset(new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Local));
        panel.PresentResult(view, app.Documents.GetSnapshot(), generatedAt);
        var window = new Window { Width = 1200, Height = 960, Content = panel };
        try
        {
            window.Show();
            Tick(window);
            check(window, panel);
        }
        finally { window.Close(); }
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectoryKey);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
