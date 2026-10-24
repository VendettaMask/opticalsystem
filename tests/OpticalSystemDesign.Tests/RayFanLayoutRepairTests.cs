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
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class RayFanLayoutRepairTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RAY_FAN_LAYOUT_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Ray Fan")]
    [InlineData("Pupil Aberration")]
    [InlineData("Optical Path Difference")]
    public async Task RealPairedFansKeepDrawableAxesAcrossDockSizes(string analysis)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential", "Objectives", "Cooke 40 degree field.zmx");
        var runtime = new WorkbenchRuntime(OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx"));
        var raw = runtime.BuildAnalysisView(analysis, new Dictionary<string, string>
        {
            ["NumberOfRays"] = "10",
            ["WavelengthNumber"] = "所有",
            ["SurfaceNumber"] = "像面"
        });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType("OpticalSystemDesign.Application.Services.WorkbenchMapper")!;
        var view = Assert.IsType<AnalysisViewDto>(mapper.GetMethod("ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [raw])) with
        { PresentationKind = WorkbenchAnalysisCatalog.PresentationKind(analysis) };
        Assert.Equal(6, view.PlotPanes.Count);
        Assert.Contains(view.PlotPanes, pane => pane.Series.All(series => series.XAxisLabel == "P_y"));
        Assert.Contains(view.PlotPanes, pane => pane.Series.All(series => series.XAxisLabel == "P_x"));
        var originalData = JsonSerializer.Serialize(view);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RayFanLayoutRepairTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            var tabs = Assert.IsType<TabControl>(typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, application.Documents.GetSnapshot(), DateTimeOffset.Now, null]));
            var window = new Window { Width = 1000, Height = 460, Content = tabs };
            try
            {
                window.Show();
                var measurements = new List<object>();
                foreach (var (width, height, minimum) in new[] { (1000, 460, 64), (1000, 340, 45), (600, 290, 32), (1000, 460, 64) })
                {
                    window.Width = width; window.Height = height;
                    Render(window);
                    var plots = tabs.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                    Assert.Equal(6, plots.Length);
                    var areas = plots.Select(plot => ((Rect)typeof(AnalysisPlotControl)
                        .GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!)
                        .TransformToAABB(plot.TransformToVisual(window)!.Value)).ToArray();
                    var gaps = PairGaps(plots, window);
                    measurements.Add(new { width, height, areas, gaps });
                    Save(window, analysis, width, height, measurements);
                    Assert.All(areas, area =>
                    {
                        Assert.True(area.Width >= minimum && area.Height >= minimum,
                            $"{analysis} {width}x{height}: drawable axes {area}, expected at least {minimum} DIP");
                        var page = Assert.Single(tabs.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>()).Child!;
                        var pageBounds = new Rect(page.Bounds.Size).TransformToAABB(page.TransformToVisual(window)!.Value);
                        Assert.True(area.X >= 0 && area.Right <= pageBounds.Right && area.Y >= 0 && area.Bottom <= pageBounds.Bottom);
                    });
                    Assert.Equal(originalData, JsonSerializer.Serialize(view));
                    Assert.All(gaps, gap => Assert.InRange(gap, -0.5, 6));
                }

                var single = view with { PlotPanes = view.PlotPanes.Where(pane => pane.Title == view.PlotPanes[0].Title).ToArray() };
                Assert.Equal(2, single.PlotPanes.Count);
                foreach (var (theme, fontSize) in new[] { ("Light", 13), ("Dark", 18) })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
                    DisplayTypography.Configure(new AppSettings { Theme = theme, FontSize = fontSize });
                    window.FontSize = fontSize;
                    var singleTabs = Assert.IsType<TabControl>(typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, [single, application.Documents.GetSnapshot(), DateTimeOffset.Now, null]));
                    window.Content = singleTabs; window.Width = 840; window.Height = 460; Render(window);
                    var plots = singleTabs.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                    Assert.Equal(2, plots.Length);
                    var gaps = PairGaps(plots, window);
                    Assert.All(gaps, gap => Assert.InRange(gap, -0.5, 6));
                    Save(window, analysis + "-single-" + theme, 840, 460, new object[] { new { gaps, fontSize } });
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static double[] PairGaps(IReadOnlyList<AnalysisPlotControl> plots, Window window)
    {
        var frames = plots.Select(plot => new Rect(plot.Bounds.Size)
            .TransformToAABB(plot.TransformToVisual(window)!.Value)).ToArray();
        return Enumerable.Range(0, frames.Length / 2)
            .Select(index => frames[index * 2 + 1].Left - frames[index * 2].Right).ToArray();
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Save(Window window, string analysis, int width, int height, IReadOnlyList<object> measurements)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_RAY_FAN_LAYOUT_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var name = analysis.Replace(' ', '-');
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, $"{name}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
        File.WriteAllText(Path.Combine(directory, name + "-layout.json"), JsonSerializer.Serialize(measurements,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
