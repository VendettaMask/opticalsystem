using System.Reflection;
using System.Security.Cryptography;
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
using OpticalSystemDesign.Tests;

namespace Gauss28ReportSample;

public static class SampleApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var source = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SampleApplication));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            UiLocalization.Apply(UiLocalization.Chinese);
            DisplayTypography.Configure(new AppSettings { Theme = "Light", Language = "zh-CN" });
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(source);
            var document = app.Documents.GetSnapshot();
            var records = new List<object>();
            var time = DateTimeOffset.Now;
            foreach (var key in new[] { "Spot Diagram", "Ray Fan", "MTF", "Field Curvature and Distortion", "Wavefront Map" })
            {
                var result = await Task.Run(() => app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>())));
                if (result.View.Outcome != AnalysisOutcome.Success) throw new InvalidOperationException(key);
                using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), key);
                typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
                panel.PresentResult(result.View, document, time, result.ReferenceScene);
                var window = new Window { Width = 1000, Height = 800, Content = panel };
                window.Show();
                try
                {
                    foreach (var size in new[] { (1000, 800), (600, 600) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        await Settle(window);
                        var page = panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                        var content = page.Child!;
                        var footer = (content as Grid)?.Children.LastOrDefault();
                        var plots = content.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(plot => new { plot.Bounds.Width, plot.Bounds.Height }).ToArray();
                        using var bitmap = panel.CapturePlotImage();
                        using var repeat = panel.CapturePlotImage();
                        if (bitmap.PixelSize != repeat.PixelSize) throw new InvalidOperationException("Repeated export changed size.");
                        var file = key.Replace(' ', '-') + $"-{size.Item1}x{size.Item2}.png";
                        AnalysisPanel.SavePlotImage(bitmap, Path.Combine(output, file));
                        using var scaled = ExportImageFrame.Capture(content, 1.25, panel);
                        records.Add(new { Kind = key, Window = size, NativeScale = window.RenderScaling,
                            ContentWidth = content.Bounds.Width, ContentHeight = content.Bounds.Height,
                            ViewportWidth = page.Viewport.Width, ViewportHeight = page.Viewport.Height,
                            FooterWidth = footer?.Bounds.Width, FooterHeight = footer?.Bounds.Height,
                            Plots = plots, PixelWidth = bitmap.PixelSize.Width, PixelHeight = bitmap.PixelSize.Height,
                            SharedFrame125Width = scaled.PixelSize.Width, SharedFrame125Height = scaled.PixelSize.Height,
                            RepeatedSizeIdentical = true, File = file });
                    }
                }
                finally { window.Close(); }
            }
            foreach (var dimension in new[] { SceneDimension.TwoDimensional, SceneDimension.ThreeDimensional })
            {
                using var panel = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension);
                var window = new Window { Width = 1000, Height = 800, Content = panel };
                window.Show();
                try
                {
                    for (var attempt = 0; attempt < 300; attempt++)
                    {
                        await Settle(window);
                        if (panel.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExportViewerImage").IsEnabled) break;
                    }
                    foreach (var size in new[] { (1000, 800), (600, 600) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        await Settle(window);
                        var scene = panel.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                        var footer = panel.GetVisualDescendants().OfType<Border>().Single(border => border.Name ==
                            (dimension == SceneDimension.TwoDimensional ? "Viewer2DFooter" : "Viewer3DFooter"));
                        using var bitmap = panel.CaptureViewerImage();
                        using var repeated = panel.CaptureViewerImage();
                        using var scaled = panel.CaptureViewerImage(1.25);
                        if (bitmap.PixelSize != repeated.PixelSize) throw new InvalidOperationException("Repeated viewer export changed size.");
                        var file = dimension + $"-{size.Item1}x{size.Item2}.png";
                        ViewerPanel.SaveViewerImage(bitmap, Path.Combine(output, file));
                        records.Add(new { Kind = dimension.ToString(), Window = size, NativeScale = window.RenderScaling,
                            SceneWidth = scene.Bounds.Width, SceneHeight = scene.Bounds.Height,
                            FooterWidth = footer.Bounds.Width, FooterHeight = footer.Bounds.Height,
                            PixelWidth = bitmap.PixelSize.Width, PixelHeight = bitmap.PixelSize.Height,
                            SharedFrame125Width = scaled.PixelSize.Width, SharedFrame125Height = scaled.PixelSize.Height,
                            RepeatedSizeIdentical = true, File = file });
                    }
                }
                finally { window.Close(); }
            }
            var afterHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
            if (!sourceHash.SequenceEqual(afterHash))
                throw new InvalidOperationException("Source changed.");
            var json = JsonSerializer.Serialize(new { Source = source, SourceSha256 = Convert.ToHexString(sourceHash),
                SourceUnchanged = true, Scope = "Native PNG capture methods, no PDF/report renderer", Records = records }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
            await File.WriteAllTextAsync(Path.Combine(output, "audit.json"), json);
            Console.WriteLine("Completed 14 native PNG captures; repeated dimensions stable; source unchanged.");
        }, CancellationToken.None);
    }

    private static async Task Settle(Window window)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); await Task.Delay(16);
        }
    }
}
