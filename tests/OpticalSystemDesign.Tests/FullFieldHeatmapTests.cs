using System.Reflection;
using System.Security.Cryptography;
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
using OpticalSystemDesign.Core;
using FullFieldAnalysis = OpticalSystemDesign.Core.Analysis.FullFieldAberrationAnalysis;
using CoreSeriesKind = OpticalSystemDesign.Core.Analysis.AnalysisSeriesKind;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class FullFieldHeatmapTests
{
    public static AppBuilder BuildAvaloniaApp() => SurfaceColorTests.BuildAvaloniaApp();

    [Fact]
    public void HeatmapKeepsSignedWaveValuesSamplingAndPhysicalBoundsIdenticalToIcons()
    {
        var optic = Optic.CreateCookeTriplet();
        var icons = new FullFieldAnalysis(optic, maximumTerm: 9, xFieldSamples: 5, yFieldSamples: 5,
            pupilSampling: 8, displayAs: "图标").GenerateData();
        var heatmap = new FullFieldAnalysis(optic, maximumTerm: 9, xFieldSamples: 5, yFieldSamples: 5,
            pupilSampling: 8, displayAs: "颜色图").GenerateData();
        var first = Assert.Single(icons.PlotSeries);
        var second = Assert.Single(heatmap.PlotSeries);
        Assert.Equal(CoreSeriesKind.Scatter, first.Kind);
        Assert.Equal(CoreSeriesKind.Heatmap, second.Kind);
        Assert.Equal(first.Points, second.Points);
        Assert.Equal(first.ValueMinimum, second.ValueMinimum);
        Assert.Equal(first.ValueMaximum, second.ValueMaximum);
        Assert.Equal(first.ValueUnit, second.ValueUnit);
        Assert.Equal(icons.PlotOptions, heatmap.PlotOptions);
        Assert.Equal(icons.Values["ValidFieldSamples"], heatmap.Values["ValidFieldSamples"]);
    }

    [Theory]
    [InlineData("Light", 1400, 950, 1)]
    [InlineData("Dark", 1100, 900, 1.5)]
    [InlineData("Isekai", 900, 850, 2)]
    public async Task DefaultUsesSharedSampledHeatmapAndColormapRepaintsWithoutChangingResult(string theme, int width, int height, double scale)
    {
        using var app = WorkbenchApplication.Create("cooke");
        const string key = "Full Field Aberration";
        var settings = app.Analyses.MergeSettings(key, new Dictionary<string, string>
        {
            ["XFieldSamples"] = "11", ["YFieldSamples"] = "11", ["PupilSampling"] = "16 x 16", ["MaximumTerm"] = "9"
        });
        Assert.Equal("颜色图", settings["DisplayAs"]);
        var view = (await app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, settings))).View;
        Assert.Equal(AnalysisOutcome.Success, view.Outcome);
        Assert.Equal(AnalysisSeriesKind.Heatmap, Assert.Single(view.Series).Kind);
        Assert.Equal(AnalysisAxisUnit.Wave, view.Series[0].ValueUnit);
        var source = System.Text.Json.JsonSerializer.Serialize(view);
        var revision = app.Events.Revision;
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(FullFieldHeatmapTests));
        await session.Dispatch(() =>
        {
            DisplayTypography.Configure(new());
            ScientificColorMaps.Configure(new() { AnalysisColorMap = "Viridis" });
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events,
                new AppSettings(), key, initialSettings: settings);
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(view, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show(); window.SetRenderScaling(scale); Tick(window);
                var plot = Assert.Single(panel.GetVisualDescendants().OfType<AnalysisPlotControl>());
                Assert.True(plot.SampledHeatmap);
                Assert.False(plot.UseHeatmapIntervals);
                Assert.Equal(0, plot.PlotOptions.GridOpacity);
                Assert.Empty(panel.GetVisualDescendants().OfType<FullFieldAberrationControl>());
                Assert.Equal(view.Series[0].Points, Assert.Single(plot.Series).Points);
                var page = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.False(page.HasOverflow);
                if (theme == "Isekai")
                {
                    var captions = string.Join(' ', page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
                    Assert.DoesNotMatch(@"[\u4e00-\u9fff]", captions);
                    Assert.Equal("Wavefront error", UiLocalization.Text("波前差"));
                    Assert.Equal("X Field (°)", UiLocalization.Text("X视场，单位：度 (°)"));
                }
                var bounds = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                Assert.True(bounds.Width > 100);
                Assert.Equal(bounds.Width, bounds.Height, 6);
                Capture(window, $"full-field-heatmap-{theme}-{scale:0.0}.png");
                var before = PlotHash(plot);
                ScientificColorMaps.Configure(new() { AnalysisColorMap = "RedBlue" }); Tick(window);
                Assert.NotEqual(before, PlotHash(plot));
                Assert.Equal(source, System.Text.Json.JsonSerializer.Serialize(view));
                Assert.Equal(revision, app.Events.Revision);
                ScientificColorMaps.Configure(new() { AnalysisColorMap = "Viridis" }); Tick(window);
                Assert.Equal(before, PlotHash(plot));
            }
            finally { window.Close(); ScientificColorMaps.Configure(new()); ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static string Capture(Window window, string? name = null)
    {
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_FULL_FIELD_HEATMAP_CAPTURE_DIR");
        if (directory is not null && name is not null)
        { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name), stream.ToArray()); }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static string PlotHash(Control plot)
    {
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(plot.Bounds.Width), (int)Math.Ceiling(plot.Bounds.Height)));
        image.Render(plot);
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
