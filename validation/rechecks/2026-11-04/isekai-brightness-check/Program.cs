using System.Reflection;
using System.Security.Cryptography;
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
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Tests;
using SkiaSharp;

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
await session.Dispatch(async () =>
{
    ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
    var settings = new AppSettings { Theme = "Isekai", FontSize = 13 };
    DisplayTypography.Configure(settings); ImageExportResolution.Configure(settings);
    using var app = WorkbenchApplication.Create("cooke");
    var records = new List<object>();
    var views = new Dictionary<string, AnalysisViewDto>();
    var window = new Window { Width = 1440, Height = 1080, Background = new SolidColorBrush(IsekaiTheme.Palette.Workspace) };
    try
    {
        window.Show(); window.SetRenderScaling(2);
        foreach (var key in new[] { "Optical Path Difference", "Pupil Aberration", "FFT PSF Cross Section" })
        {
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
            views[key] = result.View;
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, settings, key);
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(8)));
            window.Content = panel;
            for (var n = 0; n < 8; n++) { Tick(window); await Task.Delay(25); }
            var toolbar = panel.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "AnalysisBookToolbar");
            using var image = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Missing rendered frame");
            image.Save(Path.Combine(output, key.Replace(' ', '-') + ".png"), PngBitmapEncoderOptions.Default);
            using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
            using var pixels = SKBitmap.Decode(stream);
            var points = new List<int[]>();
            for (var x = .65; x < .8; x += .005)
            for (var y = .25; y < .75; y += .05)
            {
                var point = toolbar.TranslatePoint(new Point(toolbar.Bounds.Width * x, toolbar.Bounds.Height * y), window)!.Value;
                var sample = pixels.GetPixel((int)(point.X * 2), (int)(point.Y * 2));
                points.Add([sample.Red, sample.Green, sample.Blue]);
            }
            var rgb = Enumerable.Range(0, 3).Select(c => points.Average(p => p[c])).ToArray();
            var series = result.View.PlotPanes.SelectMany(p => p.Series).Concat(result.View.Series).ToArray();
            records.Add(new
            {
                Analysis = key, Outcome = result.View.Outcome.ToString(), Toolbar = toolbar.Bounds.ToString(),
                MeanRgb = rgb, Luma = rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722,
                SeriesOpacities = series.Select(s => s.Opacity).Distinct().ToArray(),
                ToolbarAncestors = toolbar.GetVisualAncestors().Prepend(toolbar).OfType<Control>().Select(c => new
                    { Type = c.GetType().Name, c.Name, c.Opacity, c.IsEnabled, c.IsEffectivelyEnabled, Theme = c.ActualThemeVariant.ToString() }).ToArray(),
            });
            window.Content = null;
        }
        using (var panels = new PanelManager(app, settings, new WorkspaceSessionStore(Path.Combine(output, "session"))))
        {
            window.Content = panels.WorkspaceControl; Tick(window);
            var dockRecords = new List<object>();
            foreach (var (key, view) in views)
            {
                panels.ShowAnalysis(key);
                var document = panels.Factory.PrimaryDocumentDock!.ActiveDockable!;
                var panel = panels.Factory.DocumentContent<AnalysisPanel>(document.Id)!;
                typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
                panel.PresentResult(view, app.Documents.GetSnapshot(), DateTimeOffset.Now);
                for (var frame = 0; frame < 16; frame++)
                {
                    Tick(window);
                    var toolbar = panel.GetVisualDescendants().OfType<Control>().SingleOrDefault(c => c.Name == "AnalysisBookToolbar");
                    if (toolbar is not null && toolbar.IsEffectivelyVisible)
                    {
                        var ancestors = toolbar.GetVisualAncestors().Prepend(toolbar).OfType<Control>().ToArray();
                        dockRecords.Add(new { Analysis = key, Frame = frame, EffectiveOpacity = ancestors.Aggregate(1d, (value, c) => value * c.Opacity),
                            ChangedAncestors = ancestors.Where(c => c.Opacity < .999).Select(c => new { Type = c.GetType().FullName, c.Name, c.Opacity }).ToArray() });
                        if (frame == 15 || ancestors.Any(c => c.Opacity is > .6 and < .99))
                        {
                            using var image = window.CaptureRenderedFrame()!;
                            image.Save(Path.Combine(output, "dock-" + key.Replace(' ', '-') + "-" + frame + ".png"), PngBitmapEncoderOptions.Default);
                        }
                    }
                    await Task.Delay(20);
                }
            }
            File.WriteAllText(Path.Combine(output, "dock-measurements.json"), JsonSerializer.Serialize(dockRecords, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(dockRecords));
        }
        var assembly = typeof(AnalysisPanel).Assembly.Location;
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(new
        {
            Assembly = assembly, AssemblySHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))),
            Records = records
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(records));
    }
    finally { window.Close(); }
}, CancellationToken.None);

static void Tick(Window window)
{
    window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
}
