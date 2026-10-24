using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
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
public sealed class AnalysisLegendLayoutTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(CaptureDirectory);
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(AnalysisPresentationKind.Standard, false)]
    [InlineData(AnalysisPresentationKind.Standard, true)]
    [InlineData(AnalysisPresentationKind.SpotDiagram, false)]
    [InlineData(AnalysisPresentationKind.ThroughFocusSpot, false)]
    [InlineData(AnalysisPresentationKind.MatrixSpot, false)]
    [InlineData(AnalysisPresentationKind.ConfigurationMatrixSpot, false)]
    [InlineData(AnalysisPresentationKind.RayFan, false)]
    [InlineData(AnalysisPresentationKind.PupilAberration, false)]
    [InlineData(AnalysisPresentationKind.OpticalPathDifference, false)]
    [InlineData(AnalysisPresentationKind.FieldCurvatureAndDistortion, false)]
    public async Task SelectableLegendsKeepTheSameTopRightAnchorAcrossPlotFamilies(
        AnalysisPresentationKind kind, bool legacyLegendBelow)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisLegendLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            var view = View(kind, legacyLegendBelow);
            var tabs = Build(view, app);
            var window = new Window { Width = 980, Height = 680, Content = tabs };
            try
            {
                window.Show(); Render(window);
                var legend = Legend(tabs);
                AssertAnchor(tabs, legend);
                Assert.Equal(3, legend.Children.Count);
                Assert.Equal(Orientation.Vertical, legend.Orientation);
                var plots = tabs.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                Assert.All(plots, plot => Assert.False(plot.PlotOptions.ShowLegend));
                var originals = plots.Select(plot => plot.Series).ToArray();
                var revision = app.Documents.GetSnapshot().Revision;
                var first = Assert.IsType<CheckBox>(legend.Children[0]);
                Click(window, first);
                Assert.False(first.IsChecked);
                Assert.All(plots, plot => Assert.DoesNotContain(plot.Series, series => series.LegendKey == "wavelength:0.486"));
                Click(window, first);
                Assert.True(first.IsChecked);
                for (var index = 0; index < plots.Length; index++)
                    Assert.Equal(originals[index], plots[index].Series);
                Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
                Capture(window, $"{kind}-{legacyLegendBelow}-wide.png");

                window.Width = 420; window.Height = 290; Render(window);
                AssertAnchor(tabs, legend);
                // An unchecked legend entry retains its place through resizing.
                first.IsChecked = false; Render(window); AssertAnchor(tabs, legend);
                first.IsChecked = true; Render(window); AssertAnchor(tabs, legend);
                Capture(window, $"{kind}-{legacyLegendBelow}-narrow.png");
                Assert.Equal(legacyLegendBelow, view.PlotOptions.LegendBelow);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", UiLocalization.Chinese, 13)]
    [InlineData("Dark", UiLocalization.English, 18)]
    [InlineData("Pixel", UiLocalization.Japanese, 13)]
    [InlineData("Isekai", UiLocalization.Chinese, 18)]
    [InlineData("Lumen", UiLocalization.English, 13)]
    public async Task LongConfigurationLegendsRemainInsideTheRailAcrossThemes(
        string theme, string language, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisLegendLayoutTests));
        await session.Dispatch(() =>
        {
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
            using var app = WorkbenchApplication.Create();
            var view = View(AnalysisPresentationKind.Standard, true);
            view = view with { Series = Enumerable.Range(1, 8).Select(index => view.Series[0] with
            {
                ConfigurationNumber = index,
                LegendLabel = $"Configuration {index}: wavelength 0.4860 µm / extended descriptive label"
            }).ToArray() };
            var tabs = Build(view, app);
            DisplayTypography.Apply(tabs);
            var window = new Window { Width = 640, Height = 420, Content = tabs };
            try
            {
                window.Show(); Render(window);
                var legend = Legend(tabs);
                AssertAnchor(tabs, legend);
                Assert.Equal(8, legend.Children.Count);
                var labels = legend.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Text?.StartsWith("Configuration", StringComparison.Ordinal) == true).ToArray();
                Assert.Equal(8, labels.Length);
                Assert.All(labels, label => Assert.True(label.Bounds.Width <= 220.1));
                var plot = Assert.Single(tabs.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var first = Assert.IsType<CheckBox>(legend.Children[0]);
                Click(window, first);
                Assert.Equal(7, plot.Series.Count);
                Assert.DoesNotContain(plot.Series, series => series.ConfigurationNumber == 1);
                Click(window, first);
                Assert.Equal(8, plot.Series.Count);
                Capture(window, $"long-legend-{theme}-{fontSize}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previous); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void AssertAnchor(TabControl tabs, StackPanel legend)
    {
        var host = Assert.Single(tabs.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
        var page = Assert.IsType<Grid>(host.Child);
        var layout = Assert.IsType<Grid>(legend.Parent);
        Assert.Same(page.Children[0], layout);
        var legendBounds = Bounds(legend, layout);
        Assert.InRange(legendBounds.Top, 11.9, 12.1);
        Assert.InRange(layout.Bounds.Width - legendBounds.Right, 11.9, 12.1);
        var plotBounds = Bounds(layout.Children[0], layout);
        Assert.True(plotBounds.Right <= legendBounds.Left - 11.9);
        Assert.True(legendBounds.Bottom <= layout.Bounds.Height + 0.1);
        var visibleLegend = Bounds(legend, host);
        var footer = Bounds(page.Children[1], host);
        Assert.True(visibleLegend.Top >= 0);
        Assert.True(visibleLegend.Right <= host.Bounds.Width + 0.1);
        Assert.True(visibleLegend.Bottom <= footer.Top + 0.1);
        var toggles = legend.Children.Cast<CheckBox>().ToArray();
        for (var index = 1; index < toggles.Length; index++)
            Assert.True(toggles[index].Bounds.Top >= toggles[index - 1].Bounds.Bottom);
    }

    private static Rect Bounds(Control control, Visual relativeTo) => new Rect(control.Bounds.Size)
        .TransformToAABB(control.TransformToVisual(relativeTo)!.Value);

    private static StackPanel Legend(Control root) => Assert.Single(root.GetVisualDescendants()
        .OfType<StackPanel>(), panel => panel.Children.Count > 0 && panel.Children.All(child => child is CheckBox));

    private static TabControl Build(AnalysisViewDto view, WorkbenchApplication app) => Assert.IsType<TabControl>(
        typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [view, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(8)), null]));

    private static AnalysisViewDto View(AnalysisPresentationKind kind, bool legendBelow)
    {
        var series = new[] { 0.486, 0.589, 0.656 }.Select((wavelength, index) => new AnalysisSeriesDto(
            "P_y", "Y (µm)", Enumerable.Range(-10, 21)
                .Select(x => new AnalysisPointDto(x / 10.0, Math.Pow(x / 10.0, 3) * (index + 1))).ToArray(),
            Name: $"{wavelength.ToString("0.0000", CultureInfo.InvariantCulture)} µm", ColorIndex: index,
            LegendKey: $"wavelength:{wavelength.ToString(CultureInfo.InvariantCulture)}",
            LegendLabel: $"{wavelength.ToString("0.0000", CultureInfo.InvariantCulture)} µm",
            XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.Coordinate, YUnit: AnalysisAxisUnit.Micrometer)).ToArray();
        var options = new AnalysisPlotOptionsDto(Title: "Analysis", ShowLegend: true, LegendBelow: legendBelow);
        var view = new AnalysisViewDto(kind.ToString(), [], "Report", series, options, [], 3, PresentationKind: kind);
        if (kind == AnalysisPresentationKind.Standard) return view;
        var panes = Enumerable.Range(0, 6).Select(index => new AnalysisPlotPaneDto(
            kind == AnalysisPresentationKind.MatrixSpot ? series[index % 3].Name
                : kind == AnalysisPresentationKind.ThroughFocusSpot ? $"Defocus: {index % 3 * 0.01} mm"
                : $"Field {index / 2 + 1}",
            (kind == AnalysisPresentationKind.MatrixSpot ? [series[index % 3]] : series)
                .Select(item => item with { XAxisLabel = index % 2 == 0 ? "P_y" : "P_x" }).ToArray(),
            options with { ShowLegend = false, EqualAspect = true }, Footer: $"Field {index / 3 + 1}",
            MatrixCell: new AnalysisMatrixCellDto(index % 3 + 1, index / 3 + 1, $"Field {index / 3 + 1}"))).ToArray();
        return view with { PlotPanes = panes };
    }

    private static void Click(Window window, CheckBox checkbox)
    {
        var point = checkbox.TranslatePoint(new Point(checkbox.Bounds.Width / 2, checkbox.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Render(window);
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static string? CaptureDirectory => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_LEGEND_CAPTURE_DIR");

    private static void Capture(Window window, string name)
    {
        if (string.IsNullOrWhiteSpace(CaptureDirectory)) return;
        Directory.CreateDirectory(CaptureDirectory);
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        image.Render(window);
        image.Save(Path.Combine(CaptureDirectory, name), PngBitmapEncoderOptions.Default);
    }
}
