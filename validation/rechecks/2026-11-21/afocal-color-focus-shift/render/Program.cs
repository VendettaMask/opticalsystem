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
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Tests;

var output = Path.GetFullPath(args[0]);
var source = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
Environment.SetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_SETTINGS_DIRECTORY", Path.Combine(output, ".settings"));
var json = new JsonSerializerOptions { WriteIndented = true };
var raw = new OpticalSystemDesign.Core.Analysis.ColorFocusShiftAnalysis(
    OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx")).GenerateData();
File.WriteAllText(Path.Combine(output, "fixed-core.json"), JsonSerializer.Serialize(new { source, raw.Values, raw.PlotOptions,
    points = raw.PlotSeries.Single().Points }, json));
var captured = new List<object>();
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
await session.Dispatch(async () =>
{
    using var app = WorkbenchApplication.Create();
    await app.Documents.OpenAsync(source);
    foreach (var (theme, language, width) in new[] { ("Light", UiLocalization.Chinese, 1440),
        ("Dark", UiLocalization.English, 1100), ("Isekai", UiLocalization.English, 1000),
        ("Light", UiLocalization.Japanese, 1100) })
    {
        ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
        UiLocalization.Apply(language);
        using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
            new AppSettings { Theme = theme }, "Color Focus Shift",
            initialSettings: new Dictionary<string, string> { ["MaximumShift"] = "0", ["PupilZone"] = "0" });
        var run = (Task<bool>)typeof(AnalysisPanel).GetMethod("RunForGuiCaptureAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;
        Assert.True(await run);
        var view = (AnalysisViewDto)typeof(AnalysisPanel).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        Assert.Equal(AnalysisOutcome.Success, view.Outcome);
        var summary = Assert.IsType<ColorFocusShiftSummaryDto>(view.ColorFocusShiftSummary);
        Assert.Equal(AnalysisAxisUnit.Diopter, summary.Unit);
        Assert.Null(summary.DiffractionLimitChange);
        Assert.InRange(summary.MaximumChange, 0.15847046, 0.15847048);
        var series = Assert.Single(view.Series);
        Assert.Equal(AnalysisAxisUnit.Diopter, series.XUnit);
        Assert.Equal(101, series.Points.Count);
        Assert.Equal(-0.2, view.PlotOptions.XMinimum);
        Assert.Equal(0.2, view.PlotOptions.XMaximum);
        var stem = "color-focus-" + theme + "-" + language;
        var window = new Window { Width = width, Height = 960, Content = panel };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
            using (var bitmap = new RenderTargetBitmap(new PixelSize(width, 960), new Vector(96, 96)))
            {
                bitmap.Render(window); bitmap.Save(Path.Combine(output, stem + "-live.png"), PngBitmapEncoderOptions.Default);
            }
            using (var bitmap = panel.CapturePlotImage())
                AnalysisPanel.SavePlotImage(bitmap, Path.Combine(output, stem + "-export.png"));
            captured.Add(new { theme, language, width, summary.MaximumChange, summary.Unit, points = series.Points.Count });
        }
        finally { window.Close(); }
    }
}, CancellationToken.None);
File.WriteAllText(Path.Combine(output, "capture-summary.json"), JsonSerializer.Serialize(captured, json));
Console.WriteLine("Captured four real panels and four actual PNG exports; 101 points, D units and 0.158470469 D range verified.");
