using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;

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

                // All-off is a visibility state, not a missing analysis result.
                var toggles = legend.Children.Cast<CheckBox>().ToArray();
                var frames = plots.Select(DrawFrame).ToArray();
                var viewports = plots.Select(plot => Field(plot, "_lastRenderedViewport")).ToArray();
                foreach (var toggle in toggles) Click(window, toggle);
                Assert.All(plots, plot => Assert.Empty(plot.Series));
                for (var index = 0; index < plots.Length; index++)
                {
                    var hiddenFrame = DrawFrame(plots[index]);
                    AssertFrameHasAxes(hiddenFrame);
                    Assert.True(StrokeCount(hiddenFrame) < StrokeCount(frames[index]));
                    Assert.Equal(viewports[index], Field(plots[index], "_lastRenderedViewport"));
                    Assert.Contains("已隐藏", ((IInteractiveCanvasAutomationSource)plots[index]).AutomationValue);
                }
                Capture(window, $"{kind}-{legacyLegendBelow}-all-hidden.png");
                window.Width = 420; window.Height = 290; Render(window);
                Assert.All(plots, plot => AssertFrameHasAxes(DrawFrame(plot)));
                AssertAnchor(tabs, legend);
                Capture(window, $"{kind}-{legacyLegendBelow}-all-hidden-narrow.png");
                window.Width = 980; window.Height = 680; Render(window);
                foreach (var toggle in toggles) Click(window, toggle);
                for (var index = 0; index < plots.Length; index++)
                {
                    Assert.Equal(originals[index], plots[index].Series);
                    Assert.Equal(StrokeCount(frames[index]), StrokeCount(DrawFrame(plots[index])));
                }
                Assert.Equal(revision, app.Documents.GetSnapshot().Revision);

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

    [Fact]
    public async Task FilteringKeepsTypedAxesAndZoomButClearingAResultDropsItsContext()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisLegendLayoutTests));
        await session.Dispatch(() =>
        {
            var millimeters = new AnalysisSeriesDto("X", "Y", [new(-0.01, -0.02), new(0.01, 0.02)],
                XQuantity: AnalysisAxisQuantity.Coordinate, XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.Coordinate, YUnit: AnalysisAxisUnit.Millimeter);
            var micrometers = millimeters with
            {
                Points = [new(-5, -10), new(5, 10)],
                XUnit = AnalysisAxisUnit.Micrometer,
                YUnit = AnalysisAxisUnit.Micrometer
            };
            var plot = new AnalysisPlotControl { Series = [millimeters, micrometers] };
            var window = new Window { Width = 640, Height = 480, Content = plot };
            try
            {
                window.Show(); Render(window); DrawFrame(plot);
                var viewport = Field(plot, "_lastRenderedViewport");
                plot.SetVisibleSeries([micrometers]);
                AssertFrameHasAxes(DrawFrame(plot));
                Assert.Equal(viewport, Field(plot, "_lastRenderedViewport"));
                plot.Focus();
                window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                DrawFrame(plot);
                var zoomed = Field(plot, "_lastRenderedViewport");
                Assert.NotEqual(viewport, zoomed);
                plot.SetVisibleSeries([]);
                AssertFrameHasAxes(DrawFrame(plot));
                Assert.Equal(zoomed, Field(plot, "_lastRenderedViewport"));
                plot.SetVisibleSeries([millimeters, micrometers]);
                DrawFrame(plot);
                Assert.Equal(zoomed, Field(plot, "_lastRenderedViewport"));
                plot.Series = [];
                var cleared = DrawFrame(plot);
                Assert.True(StrokeCount(cleared) < 16);
                Assert.Equal(default(Rect), Field(plot, "_lastPlot"));
                Assert.Contains("没有可用数据", ((IInteractiveCanvasAutomationSource)plot).AutomationValue);
                plot.Series = [micrometers]; DrawFrame(plot);
                Assert.NotEqual(zoomed, Field(plot, "_lastRenderedViewport"));
                Assert.Same(micrometers, Assert.Single(plot.Series));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static DrawingGroup DrawFrame(AnalysisPlotControl plot)
    {
        var frame = new DrawingGroup();
        using (var context = frame.Open()) plot.Render(context);
        return frame;
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing) => drawing is DrawingGroup group
        ? group.Children.SelectMany(Drawings) : [drawing];

    private static void AssertFrameHasAxes(DrawingGroup frame) =>
        Assert.True(StrokeCount(frame) >= 16,
            "An all-hidden plot must still draw its grid and four axes.");

    private static int StrokeCount(DrawingGroup frame) =>
        Drawings(frame).OfType<GeometryDrawing>().Count(item => item.Pen is not null);

    private static object Field(AnalysisPlotControl plot, string name) => typeof(AnalysisPlotControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;

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
            view = view with
            {
                Series = Enumerable.Range(1, 8).Select(index => view.Series[0] with
                {
                    ConfigurationNumber = index,
                    LegendLabel = $"Configuration {index}: wavelength 0.4860 µm / extended descriptive label"
                }).ToArray()
            };
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
                foreach (var toggle in legend.Children.Cast<CheckBox>()) toggle.IsChecked = false;
                Render(window);
                Assert.Empty(plot.Series);
                AssertFrameHasAxes(DrawFrame(plot));
                foreach (var toggle in legend.Children.Cast<CheckBox>()) toggle.IsChecked = true;
                Render(window);
                Assert.Equal(8, plot.Series.Count);
                Capture(window, $"long-legend-{theme}-{fontSize}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previous); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("MTF")]
    [InlineData("Huygens MTF")]
    [InlineData("Geometric MTF")]
    [InlineData("Sampled MTF")]
    [InlineData("Fourier Through Focus MTF")]
    [InlineData("Huygens Through Focus MTF")]
    [InlineData("Geometric Through Focus MTF")]
    [InlineData("Fourier MTF vs Field")]
    [InlineData("Huygens MTF vs Field")]
    [InlineData("Geometric MTF vs Field")]
    public async Task EveryMtfFamilyCanToggleEachCurveAndKeepItsAxes(string analysis)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisLegendLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet());
            var view = WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView(analysis, new Dictionary<string, string>
            {
                ["Sampling"] = "8",
                ["PupilSampling"] = "8",
                ["ImageSampling"] = "16",
                ["NumRays"] = "8",
                ["PlotPointCount"] = "16",
                ["ZernikeTerms"] = "9",
                ["NumberOfSteps"] = "3",
                ["Steps"] = "3",
                ["FieldDensity"] = "3",
                ["WavelengthNumber"] = "1",
                ["ShowDiffractionLimit"] = "true",
                ["Frequency1"] = "10",
                ["Frequency2"] = "20",
                ["Frequency3"] = "30",
                ["Frequency4"] = "0",
                ["Frequency5"] = "0",
                ["Frequency6"] = "0"
            }));
            Assert.Equal(AnalysisOutcome.Success, view.Outcome);
            Assert.True(view.PlotOptions.ShowLegend);
            Assert.All(view.Series, item => Assert.True(string.IsNullOrEmpty(item.LegendKey)));
            var tabs = Build(view, app);
            var window = new Window { Width = 1260, Height = 900, Content = tabs };
            try
            {
                window.Show(); Render(window);
                var legend = Legend(tabs);
                var plot = Assert.Single(tabs.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var original = view.Series;
                Assert.Equal(original.Count, legend.Children.Count);
                Assert.True(original.Count >= 2);
                Assert.False(plot.PlotOptions.ShowLegend);
                AssertAnchor(tabs, legend);
                var revision = app.Documents.GetSnapshot().Revision;
                DrawFrame(plot);
                var viewport = Field(plot, "_lastRenderedViewport");
                var first = Assert.IsType<CheckBox>(legend.Children[0]);
                Click(window, first);
                Assert.Equal(original.Skip(1), plot.Series);
                Assert.Contains(original[1], plot.Series); // Its paired direction stays visible.
                Click(window, first);
                Assert.Equal(original, plot.Series);
                Capture(window, $"{analysis}-selectable.png");
                foreach (var toggle in legend.Children.Cast<CheckBox>()) toggle.IsChecked = false;
                Render(window);
                Assert.Empty(plot.Series);
                AssertFrameHasAxes(DrawFrame(plot));
                Assert.Equal(viewport, Field(plot, "_lastRenderedViewport"));
                Capture(window, $"{analysis}-all-hidden.png");
                foreach (var toggle in legend.Children.Cast<CheckBox>()) toggle.IsChecked = true;
                Render(window);
                Assert.Equal(original, plot.Series);
                Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NamedCurvePositionsSeparateDuplicateLabelsAndKeepUnnamedReferences()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisLegendLayoutTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create();
            var view = View(AnalysisPresentationKind.Standard, false);
            var series = view.Series.Select((item, index) => item with
            { Name = index < 2 ? "Repeated label" : "", LegendKey = "", LegendLabel = "" }).ToArray();
            view = view with { Series = series };
            foreach (var usePanes in new[] { false, true })
            {
                var tested = usePanes ? view with { PlotPanes = [new("A", series, new()), new("B", series, new())] } : view;
                var tabs = Build(tested, app);
                var window = new Window { Width = 980, Height = 680, Content = tabs };
                try
                {
                    window.Show(); Render(window);
                    var toggles = Legend(tabs).Children.Cast<CheckBox>().ToArray();
                    var plots = tabs.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                    Assert.Equal(plots.Length * 2, toggles.Length);
                    toggles[0].IsChecked = false; Render(window);
                    Assert.Equal(series.Skip(1), plots[0].Series);
                    if (usePanes) Assert.Equal(series, plots[1].Series);
                    foreach (var toggle in toggles) toggle.IsChecked = false;
                    Render(window);
                    Assert.All(plots, plot => Assert.Same(series[2], Assert.Single(plot.Series)));
                    foreach (var toggle in toggles) toggle.IsChecked = true;
                    Render(window);
                    Assert.All(plots, plot => Assert.Equal(series, plot.Series));
                }
                finally { window.Close(); }
            }
            var lateral = Build(view with { PresentationKind = AnalysisPresentationKind.LateralColor }, app);
            var lateralWindow = new Window { Width = 980, Height = 680, Content = lateral };
            try
            {
                lateralWindow.Show(); Render(lateralWindow);
                Assert.Empty(lateral.GetVisualDescendants().OfType<CheckBox>());
            }
            finally { lateralWindow.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertAnchor(TabControl tabs, StackPanel legend)
    {
        var host = Assert.Single(tabs.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
        var page = Assert.IsType<UniformDiagramPage>(host.Child);
        var layout = Assert.IsAssignableFrom<Grid>(legend.Parent);
        Assert.Same(page.Children[0], layout);
        var legendBounds = Bounds(legend, layout);
        Assert.InRange(legendBounds.Top, 11.9, 12.1);
        Assert.InRange(layout.Bounds.Width - legendBounds.Right, 11.9, 12.1);
        var plotBounds = Bounds(layout.Children[0], layout);
        Assert.True(plotBounds.Right <= legendBounds.Left - 11.9);
        Assert.True(legendBounds.Bottom <= layout.Bounds.Height + 0.1);
        var visibleLegend = Bounds(legend, page);
        var footer = Bounds(page.Children[1], page);
        Assert.True(visibleLegend.Top >= 0);
        Assert.True(visibleLegend.Right <= page.Bounds.Width + 0.1);
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
        checkbox.BringIntoView(); Render(window);
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
