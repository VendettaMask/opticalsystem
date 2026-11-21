using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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

var output = Path.GetFullPath(args[0]);
var source = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(output, ".settings"));
var passed = new List<string>();
void Pass(string text) { passed.Add(text); Console.WriteLine("PASS " + text); }
var existing = new RmsAnalysisTests();
existing.RmsVsFocusProducesFiniteSeriesForEveryDefinedField(); Pass("existing finite focus sampling and field coverage");
existing.ShowDiffractionLimitAddsZemaxReferenceSeriesToAllRmsScans(); Pass("existing RMS diffraction-limit definitions");

using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
await session.Dispatch(async () =>
{
    using var app = WorkbenchApplication.Create();
    await app.Documents.OpenAsync(source);
    foreach (var data in new[] { "wavefront", "spot" })
    foreach (var theme in new[] { "Light", "Dark", "Isekai" })
    {
        ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
        var settings = new Dictionary<string, string> { ["Data"] = data, ["FocusDensity"] = "16",
            ["MinimumFocus"] = "-0.01", ["MaximumFocus"] = "0.01", ["NumRings"] = "6" };
        using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
            new AppSettings { Theme = theme }, "RMS vs Focus", initialSettings: settings);
        var run = (Task<bool>)typeof(AnalysisPanel).GetMethod("RunForGuiCaptureAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;
        Assert.True(await run);
        var view = (AnalysisViewDto)typeof(AnalysisPanel).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        Assert.Equal(AnalysisOutcome.Success, view.Outcome);
        Assert.True(view.PlotOptions.ShowVerticalZeroLine);
        Assert.Equal(16, view.Series[0].Points.Count);
        if (theme == "Light")
        {
            File.WriteAllText(Path.Combine(output, data + "-data.json"), JsonSerializer.Serialize(new
            {
                source, settings, view.PlotOptions,
                curves = view.Series.Select(s => new { s.Name, s.XUnit, s.YUnit, s.Points })
            }, new JsonSerializerOptions { WriteIndented = true }));
            Pass(data + " runtime settings and mapped zero reference");
        }
        var window = new Window { Width = 1440, Height = 960, Content = panel };
        try
        {
            window.Show(); Render(window);
            var plot = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
            AssertReference(plot, 1);
            Capture(window, Path.Combine(output, data + "-" + theme + "-live.png"));
            using (var bitmap = panel.CapturePlotImage())
                AnalysisPanel.SavePlotImage(bitmap, Path.Combine(output, data + "-" + theme + "-export.png"));
            var host = (Control)typeof(AnalysisPanel).GetField("_resultHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            var toggles = host.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(view.Series.Count, toggles.Length);
            foreach (var toggle in toggles) toggle.IsChecked = false;
            Render(window); Assert.Empty(plot.Series); AssertReference(plot, 1);
            using (var bitmap = panel.CapturePlotImage())
                AnalysisPanel.SavePlotImage(bitmap, Path.Combine(output, data + "-" + theme + "-hidden-export.png"));
            var options = plot.PlotOptions;
            plot.PlotOptions = options with { ShowVerticalZeroLine = false }; AssertReference(plot, 0);
            plot.PlotOptions = options with { XMinimum = -0.02, XMaximum = 0.1 }; AssertReference(plot, 1);
            plot.PlotOptions = options with { XMinimum = 0.01, XMaximum = 0.1 }; AssertReference(plot, 0);
            Pass(data + "/" + theme + " zero position, full height, all-hidden, disabled control, shifted/excluded range and actual PNG export");
        }
        finally { window.Close(); }
    }
}, CancellationToken.None);
File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(passed, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{passed.Count} grouped checks passed");

static void Render(Window window)
{
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
}
static void Capture(Window window, string path)
{
    using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
    image.Render(window); image.Save(path, PngBitmapEncoderOptions.Default);
}
static IEnumerable<Drawing> Flatten(Drawing drawing) => drawing is DrawingGroup group ? group.Children.SelectMany(Flatten) : [drawing];
static void AssertReference(AnalysisPlotControl plot, int expected)
{
    var frame = new DrawingGroup(); using (var context = frame.Open()) plot.Render(context);
    var rectangle = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
    var viewport = typeof(AnalysisPlotControl).GetField("_lastRenderedViewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
    double Bound(string name) => (double)viewport.GetType().GetProperty(name)!.GetValue(viewport)!;
    var x = rectangle.Left + (-Bound("XMinimum") / (Bound("XMaximum") - Bound("XMinimum")) * rectangle.Width);
    var count = Flatten(frame).OfType<GeometryDrawing>().Count(d => d.Pen is { } pen
        && Math.Abs(pen.Thickness - 0.5) < 0.001 && d.Geometry is { } geometry
        && Math.Abs(geometry.Bounds.Width) < 0.001 && Math.Abs(geometry.Bounds.X - x) < 0.01
        && Math.Abs(geometry.Bounds.Top - rectangle.Top) < 0.01 && Math.Abs(geometry.Bounds.Bottom - rectangle.Bottom) < 0.01);
    Assert.Equal(expected, count);
}
