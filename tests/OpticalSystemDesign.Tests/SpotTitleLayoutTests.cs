using System.Reflection;
using System.Text.Json;
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
public sealed class SpotTitleLayoutTests
{
    private static string? CaptureDirectory => Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SPOT_TITLE_CAPTURE_DIR");

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(CaptureDirectory);
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 13)]
    [InlineData("Dark", 18)]
    public async Task FieldTitlesRemainNearTheirFramesAcrossTallShortAndWrappedLayouts(string theme, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SpotTitleLayoutTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var application = WorkbenchApplication.Create();
            var window = new Window { Width = 980, Height = 900, FontSize = fontSize };
            try
            {
                foreach (var count in new[] { 3, 1, 5, 9 })
                {
                    var series = new AnalysisSeriesDto("X", "Y", [new(-0.01, -0.01), new(0.01, 0.01)],
                        Kind: AnalysisSeriesKind.Scatter, Name: "0.55 µm", LegendKey: "wavelength:0.55", LegendLabel: "0.55 µm",
                        XQuantity: AnalysisAxisQuantity.Coordinate, XUnit: AnalysisAxisUnit.Millimeter,
                        YQuantity: AnalysisAxisQuantity.Coordinate, YUnit: AnalysisAxisUnit.Millimeter);
                    var panes = Enumerable.Range(0, count).Select(index => new AnalysisPlotPaneDto(
                        index == 0 ? "On axis (Y=0 °)" : $"Field {index + 1} (Y={index * 5} °)",
                        [series], new AnalysisPlotOptionsDto(EqualAspect: true))).ToArray();
                    if (count == 9) panes[^1] = panes[^1] with { Title = "Field 9: extended field title that wraps within a narrow cell" };
                    var view = new AnalysisViewDto("Spot Diagram", [], "Layout fixture", [series],
                        new AnalysisPlotOptionsDto(), panes, 3, PresentationKind: AnalysisPresentationKind.SpotDiagram);
                    var originalData = JsonSerializer.Serialize(view);
                    var tabs = Assert.IsType<TabControl>(typeof(AnalysisPanel)
                        .GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [view, application.Documents.GetSnapshot(), DateTimeOffset.Now, null]));
                    window.Content = tabs;
                    window.Show();
                    foreach (var (width, height) in new[] { (980, 900), (1400, 360), (420, 280), (980, 900) })
                    {
                        window.Width = width; window.Height = height;
                        Render(window);
                        var plots = tabs.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                        Assert.Equal(count, plots.Length);
                        var measures = plots.Select(plot =>
                        {
                            var card = Assert.IsAssignableFrom<Grid>(plot.Parent!.Parent);
                            var title = Assert.Single(card.Children.OfType<TextBlock>());
                            var titleBounds = Bounds(title, window);
                            var frame = ((Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!
                                .GetValue(plot)!).TransformToAABB(plot.TransformToVisual(window)!.Value);
                            return new { Title = title.Text, TitleBounds = titleBounds, Frame = frame, Gap = frame.Top - titleBounds.Bottom };
                        }).ToArray();
                        if (!string.IsNullOrWhiteSpace(CaptureDirectory))
                        {
                            Directory.CreateDirectory(CaptureDirectory);
                            var name = $"{theme}-{count}-{width}x{height}";
                            using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                            image.Render(window);
                            image.Save(Path.Combine(CaptureDirectory, name + ".png"), PngBitmapEncoderOptions.Default);
                            File.WriteAllText(Path.Combine(CaptureDirectory, name + ".json"), JsonSerializer.Serialize(measures));
                        }
                        Assert.All(measures, measure =>
                        {
                            Assert.InRange(measure.Gap, 0, 40);
                            Assert.True(measure.TitleBounds.Top >= -0.1);
                            var page = Assert.Single(tabs.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>()).Child!;
                            var pageBounds = Bounds(page, window);
                            Assert.True(measure.TitleBounds.Right <= pageBounds.Right + 0.1);
                            Assert.True(measure.Frame.Bottom <= pageBounds.Bottom + 0.1);
                        });
                        Assert.Equal(originalData, JsonSerializer.Serialize(view));
                    }
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Rect Bounds(Control control, Visual relativeTo) => new Rect(control.Bounds.Size)
        .TransformToAABB(control.TransformToVisual(relativeTo)!.Value);

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
