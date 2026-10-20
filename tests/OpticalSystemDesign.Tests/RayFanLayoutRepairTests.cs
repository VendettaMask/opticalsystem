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
                    measurements.Add(new { width, height, areas });
                    Save(window, analysis, width, height, measurements);
                    Assert.All(areas, area =>
                    {
                        Assert.True(area.Width >= minimum && area.Height >= minimum,
                            $"{analysis} {width}x{height}: drawable axes {area}, expected at least {minimum} DIP");
                        Assert.True(area.X >= 0 && area.Right <= width && area.Y >= 0 && area.Bottom <= height);
                    });
                    Assert.Equal(originalData, JsonSerializer.Serialize(view));
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
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
