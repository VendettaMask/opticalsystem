using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class IsekaiDocumentBrightnessTests
{
    [Theory]
    [InlineData(13, 1.5)]
    [InlineData(18, 2)]
    public async Task DockedScientificPagesAreOpaqueFromTheirFirstVisibleFrame(double fontSize, double scaling)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            var globalReveal = DeferredContentPresentationSettings.DefaultTimeline.RevealDuration;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            var settings = new AppSettings { Theme = "Isekai", FontSize = fontSize };
            DisplayTypography.Configure(settings); ImageExportResolution.Configure(settings);
            using var app = WorkbenchApplication.Create("cooke");
            var original = app.Documents.GetRecoveryState();
            var views = new Dictionary<string, AnalysisViewDto>();
            foreach (var key in new[] { "Optical Path Difference", "Pupil Aberration", "FFT PSF Cross Section" })
            {
                var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
                Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
                views[key] = result.View;
            }
            using var panels = new PanelManager(app, settings, new WorkspaceSessionStore(
                Path.Combine(Path.GetTempPath(), "isekai-brightness", Guid.NewGuid().ToString("N"))));
            var window = new Window { Width = 1440, Height = 1080, Content = panels.WorkspaceControl };
            DisplayTypography.Apply(window);
            try
            {
                window.Show(); window.SetRenderScaling(scaling); Tick(window);
                double[]? reference = null;
                foreach (var (key, view) in views)
                {
                    panels.ShowAnalysis(key);
                    var document = panels.Factory.PrimaryDocumentDock!.ActiveDockable!;
                    var panel = panels.Factory.DocumentContent<AnalysisPanel>(document.Id)!;
                    typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
                    panel.PresentResult(view, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(8)));
                    var source = JsonSerializer.Serialize(view);
                    Control? toolbar = null;
                    for (var attempt = 0; attempt < 50; attempt++)
                    {
                        Tick(window);
                        toolbar = panel.GetVisualDescendants().OfType<Control>()
                            .SingleOrDefault(c => c.Name == "AnalysisBookToolbar" && c.IsEffectivelyVisible);
                        if (toolbar is not null) break;
                        await Task.Delay(5);
                    }
                    Assert.NotNull(toolbar);
                    for (var frame = 0; frame < 3; frame++)
                    {
                        Assert.All(toolbar.GetVisualAncestors().Prepend(toolbar), ancestor => Assert.Equal(1, ancestor.Opacity));
                        Tick(window); await Task.Delay(20);
                    }
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
                    using var pixels = SKBitmap.Decode(stream);
                    var samples = new List<SKColor>();
                    for (var x = .65; x < .8; x += .005)
                    for (var y = .25; y < .75; y += .05)
                    {
                        var point = toolbar.TranslatePoint(new Point(toolbar.Bounds.Width * x, toolbar.Bounds.Height * y), window)!.Value;
                        samples.Add(pixels.GetPixel((int)(point.X * scaling), (int)(point.Y * scaling)));
                    }
                    var rgb = new[] { samples.Average(p => p.Red), samples.Average(p => p.Green), samples.Average(p => p.Blue) };
                    reference ??= rgb;
                    for (var channel = 0; channel < 3; channel++) Assert.InRange(Math.Abs(rgb[channel] - reference[channel]), 0, 1);
                    if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BRIGHTNESS_CAPTURE_DIR") is { } output)
                    {
                        Directory.CreateDirectory(output);
                        image.Save(Path.Combine(output, $"{key.Replace(' ', '-')}-{fontSize}-{scaling}.png"), PngBitmapEncoderOptions.Default);
                    }
                    Assert.Equal(source, JsonSerializer.Serialize(view));
                }
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel", "Isekai" })
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Tick(window);
                    foreach (var control in window.GetVisualDescendants().OfType<DocumentControl>())
                    {
                        var timeline = DeferredContentScheduling.GetTimeline(control);
                        if (theme == "Isekai") { Assert.NotNull(timeline); Assert.Equal(TimeSpan.Zero, timeline.RevealDuration); }
                        else Assert.Null(timeline);
                    }
                }
                Assert.Equal(globalReveal, DeferredContentPresentationSettings.DefaultTimeline.RevealDuration);
                Assert.Equal(original, app.Documents.GetRecoveryState());
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
