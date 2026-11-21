using System.Reflection;
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
using OpticalSystemDesign.Tests;

Directory.CreateDirectory(args[1]);
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(RenderBootstrap));
await session.Dispatch(async () =>
{
    DisplayTypography.Configure(new());
    ImageExportResolution.Configure(new() { DiagramAspectRatio = DiagramSheetLayout.Standard });
    ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
    using var app = WorkbenchApplication.Create();
    await app.Documents.OpenAsync(args[0]);
    var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Through Focus",
        new Dictionary<string, string>
        {
            ["RayDensity"] = "6", ["Pattern"] = "六边", ["Reference"] = "主光线",
            ["DefocusStepMicrometers"] = "50", ["UseSymbols"] = "true"
        }));
    if (result.View.Outcome != AnalysisOutcome.Success || result.View.PlotPanes.Count != 15
        || result.View.PlotPanes.Any(p => p.Series.Any(s => s.Points.Count != 127)))
        throw new InvalidOperationException("Desktop analysis did not retain all rays.");
    using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new(), "Through Focus");
    typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
    panel.PresentResult(result.View, app.Documents.GetSnapshot(), DateTimeOffset.Now);
    var window = new Window { Width = 1600, Height = 1180, Content = panel };
    try
    {
        window.Show(); Tick(window);
        var plots = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
        if (plots.Length != 15 || plots.Any(p => p.Series.Sum(s => s.Points.Count) != 381))
            throw new InvalidOperationException("Displayed plot data lost rays.");
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(args[1], "through-focus-fixed-live.png"), PngBitmapEncoderOptions.Default);
        using var image = panel.CapturePlotImage();
        image.Save(Path.Combine(args[1], "through-focus-fixed-export.png"), PngBitmapEncoderOptions.Default);
        Console.WriteLine($"VERIFIED DESKTOP: {plots.Length} plots, 381 displayed points per plot; live and PNG captured.");
    }
    finally { window.Close(); }
}, CancellationToken.None);

static void Tick(Window window)
{
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
}

public static class RenderBootstrap
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
