using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
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
public sealed class AnalysisCurveWidthTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MenuChangesAllPanesWithoutResettingFilteringZoomOrSource(bool multiplePanes)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisCurveWidthTests));
        await session.Dispatch(() =>
        {
            using var app = WorkbenchApplication.Create();
            using var panel = Panel(app);
            var view = View();
            if (multiplePanes)
                view = view with { PlotPanes = [new("A", view.Series, view.PlotOptions), new("B", view.Series, view.PlotOptions)], PlotPaneColumns = 2 };
            panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1280, Height = 960, Content = panel };
            try
            {
                window.Show(); Render(window);
                var button = WidthButton(panel);
                var camera = panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage");
                Assert.Same(camera.Parent, button.Parent);
                var commands = Assert.IsAssignableFrom<Panel>(camera.Parent);
                Assert.Equal(commands.Children.IndexOf(camera) + 1, commands.Children.IndexOf(button));
                Assert.True(button.IsVisible);
                Assert.Equal("线宽", Assert.IsType<TextBlock>(button.Content).Text);
                var menu = Assert.IsType<MenuFlyout>(button.Flyout);
                var choices = menu.Items.Cast<MenuItem>().ToArray();
                Assert.Equal(new[] { "细", "标准", "粗" }, choices.Select(item => item.Header));
                Assert.Equal(new[] { false, true, false }, choices.Select(item => item.IsChecked));
                var plots = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                Assert.Equal(multiplePanes ? 2 : 1, plots.Length);
                Assert.All(plots, plot => Assert.Equal(AnalysisCurveWidth.Standard, plot.CurveWidth));
                var plot = plots[0];
                plot.Focus();
                window.KeyPress(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Add, RawInputModifiers.None, PhysicalKey.None, null);
                Render(window);
                var viewport = Viewport(plot);
                var toggles = panel.GetVisualDescendants().OfType<CheckBox>().Where(item => item.Content is Grid).ToArray();
                Assert.NotEmpty(toggles);
                toggles[0].IsChecked = false; Render(window);
                var visible = plot.Series;
                var options = plot.PlotOptions;
                var revision = app.Documents.GetSnapshot().Revision;
                foreach (var index in new[] { 0, 1, 2 })
                {
                    choices[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Render(window);
                    Assert.All(plots, item => Assert.Equal((AnalysisCurveWidth)index, item.CurveWidth));
                    Assert.Same(visible, plot.Series);
                    Assert.Same(options, plot.PlotOptions);
                    Assert.Equal(viewport, Viewport(plot));
                    Assert.False(toggles[0].IsChecked);
                    Assert.Single(choices, item => item.IsChecked);
                }
                foreach (var toggle in toggles) toggle.IsChecked = false;
                Render(window);
                Assert.True(button.IsVisible);
                Assert.All(plots, item => Assert.Empty(item.Series));
                foreach (var toggle in toggles) toggle.IsChecked = true;
                Render(window);
                Assert.All(plots, item => Assert.Equal(view.Series, item.Series));
                Assert.Equal(revision, app.Documents.GetSnapshot().Revision);
                Assert.Equal(1.5, view.Series[0].LineWidth);

                var tabs = panel.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 1; Render(window); Assert.False(button.IsVisible);
                tabs.SelectedIndex = 0; Render(window); Assert.True(button.IsVisible);
                panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now); Render(window);
                Assert.All(panel.GetVisualDescendants().OfType<AnalysisPlotControl>(), item => Assert.Equal(AnalysisCurveWidth.Thick, item.CurveWidth));
                panel.PresentResult(view with { PlotPanes = [], Series = view.Series.Select(item => item with { Kind = AnalysisSeriesKind.Heatmap }).ToArray() },
                    app.Documents.GetSnapshot(), DateTimeOffset.Now);
                Render(window); Assert.False(button.IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(AnalysisSeriesKind.Line)]
    [InlineData(AnalysisSeriesKind.ColoredLine)]
    public async Task ExportedCurveInkIncreasesAcrossPresetsAndReturnsExactly(AnalysisSeriesKind kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisCurveWidthTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create();
            using var panel = Panel(app);
            var view = View();
            view = view with { Series = [view.Series[0] with { Kind = kind }] };
            panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1280, Height = 960, Content = panel };
            try
            {
                window.Show(); Render(window);
                var choices = Assert.IsType<MenuFlyout>(WidthButton(panel).Flyout).Items.Cast<MenuItem>().ToArray();
                var sizes = new List<PixelSize>();
                var ink = new List<int>();
                var images = new List<byte[]>();
                foreach (var index in new[] { 0, 1, 2, 0 })
                {
                    choices[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Render(window);
                    using var image = panel.CapturePlotImage();
                    sizes.Add(image.PixelSize);
                    using var data = new MemoryStream(); image.Save(data, PngBitmapEncoderOptions.Default);
                    images.Add(data.ToArray());
                    using var bitmap = SkiaSharp.SKBitmap.Decode(images[^1]);
                    ink.Add(bitmap.Pixels.Count(pixel => pixel.Red < 240 || pixel.Green < 240 || pixel.Blue < 240));
                    if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CURVE_WIDTH_CAPTURE_DIR") is { Length: > 0 } directory)
                    {
                        Directory.CreateDirectory(directory);
                        File.WriteAllBytes(Path.Combine(directory, $"{kind}-{index}.png"), images[^1]);
                        using var screen = new RenderTargetBitmap(new PixelSize(1280, 960), new Vector(96, 96));
                        screen.Render(window); screen.Save(Path.Combine(directory, $"{kind}-toolbar-{index}.png"), PngBitmapEncoderOptions.Default);
                    }
                }
                Assert.All(sizes, size => Assert.Equal(new PixelSize(2404, 1804), size));
                Assert.True(ink[1] > ink[0], $"Standard ink {ink[1]} must exceed thin {ink[0]}.");
                Assert.True(ink[2] > ink[1], $"Thick ink {ink[2]} must exceed standard {ink[1]}.");
                Assert.Equal(images[0], images[3]);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static AnalysisPanel Panel(WorkbenchApplication app)
    {
        var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), "MTF");
        typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
        return panel;
    }

    private static AnalysisViewDto View()
    {
        var points = Enumerable.Range(0, 41).Select(index => new AnalysisPointDto(index / 40.0, Math.Pow(1 - index / 40.0, 2), Value: index / 40.0)).ToArray();
        var first = new AnalysisSeriesDto("X", "MTF", points, Name: "Field 1, T",
            XQuantity: AnalysisAxisQuantity.NormalizedField, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.Modulation, YUnit: AnalysisAxisUnit.Dimensionless);
        return new AnalysisViewDto("MTF", [], "", [first, first with { Name = "Field 1, S", LineStyle = AnalysisLineStyle.Dashed }],
            new AnalysisPlotOptionsDto(XMinimum: 0, XMaximum: 1, YMinimum: 0, YMaximum: 1, ShowLegend: true), [], 1);
    }

    private static DropDownButton WidthButton(Control root) => root.GetVisualDescendants().OfType<DropDownButton>().Single(item => item.Name == "AnalysisCurveWidth");
    private static object Viewport(AnalysisPlotControl plot) => typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
