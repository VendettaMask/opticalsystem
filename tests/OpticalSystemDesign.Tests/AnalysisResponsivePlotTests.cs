using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
public sealed class AnalysisResponsivePlotTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_FIT_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData(640, 320)]
    [InlineData(420, 260)]
    [InlineData(1200, 360)]
    [InlineData(900, 700)]
    public async Task PlotAndFooterStayInsideTheirPageWithoutOverlap(double width, double height)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            var view = View(AnalysisPresentationKind.ColorFocusShift);
            var tabs = Build(view, application);
            var window = new Window { Width = width, Height = height, Content = tabs };
            try
            {
                window.Show(); Render(window);
                AssertFits(tabs, window);
                Capture(window, $"color-focus-{width}x{height}.png");
                var plot = Assert.Single(tabs.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var originalSeries = plot.Series;
                window.Width = 1120; window.Height = 800; Render(window);
                AssertFits(tabs, window);
                Assert.Same(originalSeries, plot.Series);
                window.Width = width; window.Height = height; Render(window);
                AssertFits(tabs, window);
                Assert.Same(view.Series, originalSeries);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    public static IEnumerable<object[]> PlotPresentations() => Enum.GetValues<AnalysisPresentationKind>()
        .Where(kind => kind is not (AnalysisPresentationKind.SeidelCoefficients or AnalysisPresentationKind.ZernikeFringe
            or AnalysisPresentationKind.ZernikeStandard or AnalysisPresentationKind.ZernikeAnnular or AnalysisPresentationKind.CardinalPoints))
        .Select(kind => new object[] { kind });

    [Theory]
    [MemberData(nameof(PlotPresentations))]
    public async Task CommonAndSpecializedPlotPresentationsFitSmallPanes(AnalysisPresentationKind kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var view = View(kind);
            var originalSeries = view.Series;
            var originalReport = view.ReportText;
            var tabs = Build(view, application);
            var window = new Window { Width = 600, Height = 290, Content = tabs };
            try
            {
                window.Show(); Render(window); AssertFits(tabs, window);
                Capture(window, $"presentation-{kind}.png");
                window.Width = 360; window.Height = 240; Render(window); AssertFits(tabs, window);
                Assert.Same(originalSeries, view.Series);
                Assert.Equal(originalReport, view.ReportText);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DataAndReportTabsScrollWithinTheAvailableHeight()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var view = View(AnalysisPresentationKind.Standard) with
            {
                Rows = Enumerable.Range(0, 100).Select(index => new AnalysisRowDto($"指标 {index}", index.ToString())).ToArray(),
                ReportText = string.Join(Environment.NewLine, Enumerable.Range(0, 100).Select(index => $"Report line {index}"))
            };
            var tabs = Build(view, application);
            var window = new Window { Width = 480, Height = 210, Content = tabs };
            try
            {
                window.Show(); Render(window);
                foreach (var index in new[] { 1, 2 })
                {
                    tabs.SelectedIndex = index; Render(window);
                    var content = Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(tabs.Items[index]).Content);
                    Assert.InRange(VisualBounds(content, window).Bottom, 0, window.Height);
                    Assert.InRange(content.MinHeight, 0, 64); // Preserve the theme's normal editor minimum.
                    if (content is TextBox report)
                    {
                        Assert.Equal(view.ReportText, report.Text);
                        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            report.GetValue(ScrollViewer.VerticalScrollBarVisibilityProperty));
                    }
                    else Assert.Equal(100, Assert.IsType<DataGrid>(content).ItemsSource.Cast<AnalysisRowDto>().Count());
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", 13)]
    [InlineData("Dark", 20)]
    [InlineData("Light", 32)]
    public async Task EveryPlotFamilyKeepsItsSourceCaptionOrExplicitlyRejectsOversizeAcrossResize(string theme, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
            using var application = WorkbenchApplication.Create();
            var window = new Window { FontSize = fontSize };
            try
            {
                foreach (var kind in PlotPresentations().Select(item => (AnalysisPresentationKind)item[0]))
                {
                    var view = View(kind) with { Name = "Extended analysis title 长标题 / " + kind };
                    if (kind == AnalysisPresentationKind.SpotDiagram)
                        view = view with
                        {
                            PlotPanes = view.PlotPanes.Select(pane => pane with
                            {
                                Metrics = [new("RMS radius 均方根半径", 0.012345, "mm"), new("Geometric radius 几何半径", 0.04321, "mm")]
                            }).ToArray()
                        };
                    var document = application.Documents.GetSnapshot() with
                    { Name = "A long optical document description 复杂镜头结构与波长分析.staropt" };
                    var tabs = Assert.IsType<TabControl>(typeof(AnalysisPanel)
                        .GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [view, document, DateTimeOffset.Now, null]));
                    window.Content = tabs; window.Show();
                    foreach (var (width, height) in new[] { (420, 280), (1400, 360), (900, 900), (420, 280) })
                    {
                        window.Width = width; window.Height = height; Render(window);
                        var host = Assert.Single(tabs.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                        var page = Assert.IsType<UniformDiagramPage>(host.Child);
                        Assert.Equal(new Size(1200, 900), page.Bounds.Size);
                        if (page.HasOverflow)
                        {
                            // Six field summaries with two named metrics each also exceed
                            // the fixed caption at normal font size; preserve them and fail explicitly.
                            Assert.True(fontSize > 13 || kind == AnalysisPresentationKind.SpotDiagram, kind.ToString());
                            if (kind == AnalysisPresentationKind.SpotDiagram)
                            {
                                Assert.Equal(6, view.PlotPanes.Count);
                                Assert.All(view.PlotPanes, pane => Assert.Equal(2, pane.Metrics!.Count));
                            }
                            Assert.Throws<InvalidOperationException>(page.ValidateForExport);
                            continue;
                        }
                        page.ValidateForExport();
                        AssertFits(tabs, window);
                        foreach (var plot in host.GetVisualDescendants().OfType<AnalysisPlotControl>())
                            foreach (var field in new[] { "_xTickLabelBounds", "_yTickLabelBounds" })
                            {
                                var labels = (List<Rect>)typeof(AnalysisPlotControl).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                                for (var index = 0; index < labels.Count; index++)
                                    for (var other = index + 1; other < labels.Count; other++)
                                        Assert.False(labels[index].Intersects(labels[other]), $"{kind} {fontSize}: tick labels overlap");
                            }
                        var footer = page.Children[1];
                        foreach (var label in footer.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible))
                        {
                            var bounds = VisualBounds(label, footer);
                            Assert.True(bounds.Top >= -1 && bounds.Left >= -1 && bounds.Bottom <= footer.Bounds.Height + 1
                                && bounds.Right <= footer.Bounds.Width + 1, $"{kind} {fontSize}: {label.Text}, {bounds}, footer {footer.Bounds}");
                            Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1,
                                $"{kind} {fontSize}: clipped text {label.Text}, {label.TextLayout.Height}, {label.Bounds}");
                            Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, $"{kind}: text is wider than its caption: {label.Text}");
                        }
                        if (width == 420 && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_FIT_CAPTURE_DIR")))
                        {
                            var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_FIT_CAPTURE_DIR")!;
                            Directory.CreateDirectory(directory);
                            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(page.Bounds.Width), (int)Math.Ceiling(page.Bounds.Height)), new Vector(96, 96));
                            bitmap.Render(page);
                            bitmap.Save(Path.Combine(directory, $"complete-{kind}-{theme}-{fontSize}.png"), PngBitmapEncoderOptions.Default);
                        }
                    }
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(AnalysisPresentationKind.SeidelCoefficients)]
    [InlineData(AnalysisPresentationKind.ZernikeFringe)]
    [InlineData(AnalysisPresentationKind.ZernikeStandard)]
    [InlineData(AnalysisPresentationKind.ZernikeAnnular)]
    public async Task CoefficientReportsKeepFullTextAndScrollInSmallPanes(AnalysisPresentationKind kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var view = View(kind) with
            {
                ReportText = string.Join(Environment.NewLine,
                Enumerable.Range(0, 100).Select(index => $"Z{index}: " + new string('0', 90)))
            };
            var content = Assert.IsAssignableFrom<Control>(typeof(AnalysisPanel)
                .GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, application.Documents.GetSnapshot(), DateTimeOffset.Now, null]));
            var window = new Window { Width = 420, Height = 280, Content = content };
            try
            {
                window.Show(); Render(window);
                var report = content as TextBox ?? Assert.Single(content.GetVisualDescendants().OfType<TextBox>());
                Assert.Contains(view.ReportText, report.Text);
                report.SelectAll(); Assert.Equal(report.Text, report.SelectedText);
                var scrolls = content.GetVisualDescendants().Prepend(content).OfType<ScrollViewer>().ToArray();
                Assert.Contains(scrolls, scroll => scroll.Extent.Height > scroll.Viewport.Height);
                Assert.Contains(scrolls, scroll => scroll.Extent.Width > scroll.Viewport.Width);
                foreach (var scroll in scrolls.Where(scroll => scroll.Extent.Height > scroll.Viewport.Height))
                { scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height); Render(window); Assert.True(scroll.Offset.Y > 0); }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FittedPlotsRetainInteractiveZoomAcrossResize()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var tabs = Build(View(AnalysisPresentationKind.ColorFocusShift), application);
            var window = new Window { Width = 1400, Height = 1000, Content = tabs };
            try
            {
                window.Show(); Render(window);
                var plot = Assert.Single(tabs.GetVisualDescendants().OfType<AnalysisPlotControl>());
                var originalSeries = plot.Series;
                var viewportField = typeof(AnalysisPlotControl).GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var plotRect = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                var pointer = plot.TranslatePoint(plotRect.Center, window)!.Value;
                window.MouseMove(pointer);
                window.MouseWheel(pointer, new Vector(0, 1)); Render(window);
                Assert.NotNull(viewportField.GetValue(plot));
                plot.Focus();
                window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Add, RawInputModifiers.None, PhysicalKey.None, null); Render(window);
                var zoomed = viewportField.GetValue(plot);
                Assert.NotNull(zoomed);
                window.Width = 440; window.Height = 250; Render(window);
                Assert.Equal(zoomed, viewportField.GetValue(plot));
                Assert.Same(originalSeries, plot.Series);
                AssertFits(tabs, window);
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.None, null); Render(window);
                Assert.Null(viewportField.GetValue(plot));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SmallViewportScrollsAtReadableSizeAndCanReachTheFullFooter()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var tabs = Build(View(AnalysisPresentationKind.ColorFocusShift), application);
            var host = Assert.IsType<ResponsiveAnalysisPlotHost>(Assert.IsType<TabItem>(tabs.Items[0]).Content);
            var window = new Window { Width = 360, Height = 240, Content = tabs };
            try
            {
                window.Show(); Render(window);
                Assert.Equal(1, host.ContentScale);
                Assert.Null(host.Child!.RenderTransform);
                Assert.True(host.Extent.Height > host.Viewport.Height);
                host.Offset = new Vector(0, host.Extent.Height); Render(window);
                var footer = Assert.IsType<UniformDiagramPage>(host.Child).Caption;
                var bounds = VisualBounds(footer, host);
                Assert.InRange(bounds.Bottom, host.Viewport.Height - 1, host.Viewport.Height + 1);
                Assert.True(host.Offset.Y > 0);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ScrollHostPreservesTheOriginalSheetAndKeepsItsOffscreenFooterReachable()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            var tabs = Build(View(AnalysisPresentationKind.ColorFocusShift), application);
            var plotTab = Assert.IsType<TabItem>(tabs.Items[0]);
            var host = Assert.IsType<ResponsiveAnalysisPlotHost>(plotTab.Content);
            var page = Assert.IsType<UniformDiagramPage>(host.Child);
            host.Child = null;
            plotTab.Content = page;
            var window = new Window { Width = 640, Height = 320, Content = tabs };
            try
            {
                window.Show(); Render(window);
                // A direct sheet is intentionally larger than a small window.
                var plotBounds = VisualBounds(Assert.Single(page.GetVisualDescendants().OfType<AnalysisPlotControl>()), window);
                var footerBounds = VisualBounds(page.Children[1], window);
                Assert.True(plotBounds.Bottom > footerBounds.Top + 20 || footerBounds.Bottom > window.Height + 20);
                Capture(window, "unfitted-overflow.png");
                plotTab.Content = null;
                host.Child = page;
                plotTab.Content = host; Render(window);
                AssertFits(tabs, window);
                Assert.Equal(new Size(1200, 900), page.Bounds.Size);
                Assert.True(host.Extent.Width > host.Viewport.Width);
                Assert.True(host.Extent.Height > host.Viewport.Height);
                Capture(window, "fitted-no-overlap.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light", UiLocalization.Chinese)]
    [InlineData("Dark", UiLocalization.English)]
    [InlineData("Pixel", UiLocalization.Japanese)]
    [InlineData("Isekai", UiLocalization.Chinese)]
    public async Task StackedPlotsAndTheirLegendsRemainUsableAcrossThemes(string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResponsivePlotTests));
        await session.Dispatch(() =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var application = WorkbenchApplication.Create();
            var upper = Build(View(AnalysisPresentationKind.LateralColor), application);
            var lower = Build(View(AnalysisPresentationKind.ColorFocusShift), application);
            var split = new Grid { RowDefinitions = new RowDefinitions("*,4,*") };
            split.Children.Add(upper);
            var divider = new GridSplitter(); Grid.SetRow(divider, 1); split.Children.Add(divider);
            Grid.SetRow(lower, 2); split.Children.Add(lower);
            var window = new Window { Width = 820, Height = 610, Content = split };
            try
            {
                window.Show(); Render(window); AssertFits(upper, window); AssertFits(lower, window);
                var checkbox = Assert.Single(lower.GetVisualDescendants().OfType<CheckBox>());
                var plot = Assert.Single(lower.GetVisualDescendants().OfType<AnalysisPlotControl>());
                checkbox.BringIntoView(); Render(window);
                var point = checkbox.TranslatePoint(new Point(checkbox.Bounds.Width / 2, checkbox.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Empty(plot.Series);
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Render(window);
                Assert.Single(plot.Series);
                Capture(window, $"stacked-{theme}-{language}.png");
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
        }, CancellationToken.None);
    }

    private static void AssertFits(TabControl tabs, Window window)
    {
        var host = Assert.IsType<ResponsiveAnalysisPlotHost>(Assert.IsType<TabItem>(tabs.Items[0]).Content);
        var page = host.Child!;
        var grid = Assert.IsAssignableFrom<Grid>(page);
        var plotRoot = grid.Children[0];
        var footer = grid.Children[1];
        var pageBounds = VisualBounds(page, window);
        var plotBounds = VisualBounds(plotRoot, window);
        var footerBounds = VisualBounds(footer, window);
        Assert.True(plotBounds.Bottom <= footerBounds.Top + 1, $"Plot {plotBounds} overlaps footer {footerBounds}");
        Assert.True(footerBounds.Bottom <= pageBounds.Bottom + 1, $"Footer {footerBounds} exceeds page {pageBounds}");
        Assert.True(plotBounds.Width <= pageBounds.Width + 1);
        Assert.True(plotBounds.Height > 10);
        Assert.Equal(1, host.ContentScale);
        Assert.True(VisualBounds(host, window).Bottom <= window.Height + 1);
        foreach (var plot in page.GetVisualDescendants().OfType<AnalysisPlotControl>())
            Assert.True(VisualBounds(plot, window).Bottom <= footerBounds.Top + 1);
        var canvases = page.GetVisualDescendants().OfType<AnalysisPlotControl>()
            .Select(plot => VisualBounds(plot, page)).ToArray();
        for (var index = 0; index < canvases.Length; index++)
            for (var other = index + 1; other < canvases.Length; other++)
                Assert.False(canvases[index].Deflate(1).Intersects(canvases[other].Deflate(1)),
                    $"Canvas overlap: {canvases[index]}, {canvases[other]}");
    }

    private static Rect VisualBounds(Control control, Visual relativeTo) => new Rect(control.Bounds.Size)
        .TransformToAABB(control.TransformToVisual(relativeTo)!.Value);

    private static TabControl Build(AnalysisViewDto view, WorkbenchApplication application) => Assert.IsType<TabControl>(
        typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [view, application.Documents.GetSnapshot(), DateTimeOffset.Now, null]));

    private static AnalysisViewDto View(AnalysisPresentationKind kind)
    {
        var series = new AnalysisSeriesDto("焦移", "波长",
            Enumerable.Range(0, 31).Select(index => new AnalysisPointDto((index - 15) * 0.1, 0.45 + index * 0.008)).ToArray(),
            Name: "色焦移", LegendKey: "wavelength:0.55", LegendLabel: "0.55 µm",
            XQuantity: AnalysisAxisQuantity.Defocus, XUnit: AnalysisAxisUnit.Micrometer,
            YQuantity: AnalysisAxisQuantity.Wavelength, YUnit: AnalysisAxisUnit.Micrometer);
        var view = new AnalysisViewDto(kind == AnalysisPresentationKind.LateralColor ? "垂轴色差" : "色焦移",
            [new("最大焦移变化", "3 µm"), new("衍射极限变化", "0.2 µm")],
            "完整的分析报告", [series], new AnalysisPlotOptionsDto(ShowLegend: true, LegendBelow: true), [], 1,
            PresentationKind: kind);
        if (kind is AnalysisPresentationKind.WavefrontMap or AnalysisPresentationKind.FftPsf or AnalysisPresentationKind.HuygensPsf
            or AnalysisPresentationKind.Foucault or AnalysisPresentationKind.FullFieldAberration or AnalysisPresentationKind.Interferogram)
            view = view with
            {
                Series = [series with { Points = Enumerable.Range(-2, 5).SelectMany(y => Enumerable.Range(-2, 5)
                .Select(x => new AnalysisPointDto(x, y, Value: (x + y + 5) / 10.0))).ToArray() }]
            };
        if (kind is AnalysisPresentationKind.SpotDiagram or AnalysisPresentationKind.ThroughFocusSpot or AnalysisPresentationKind.MatrixSpot
            or AnalysisPresentationKind.ConfigurationMatrixSpot or AnalysisPresentationKind.RayFan or AnalysisPresentationKind.PupilAberration
            or AnalysisPresentationKind.OpticalPathDifference or AnalysisPresentationKind.FieldCurvatureAndDistortion)
            view = view with
            {
                PlotPanes = Enumerable.Range(0, 6).Select(index => new AnalysisPlotPaneDto($"视场 {index + 1}",
                [series], new AnalysisPlotOptionsDto(EqualAspect: true), Footer: $"视场 {index + 1}")).ToArray(),
                PlotPaneColumns = 3
            };
        if (kind == AnalysisPresentationKind.SeidelDiagram)
            view = view with
            {
                Table = new AnalysisTableDto(["表面", "球差", "彗差", "像散", "场曲", "畸变", "轴上色差", "垂轴色差"],
                Enumerable.Range(1, 3).Select(index => (IReadOnlyList<string>)new[] { index.ToString(), "0.01", "0.02", "0.01", "0", "0", "0", "0" }).ToArray())
            };
        return view;
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_FIT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
