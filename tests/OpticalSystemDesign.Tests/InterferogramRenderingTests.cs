using System.Reflection;
using System.Runtime.InteropServices;
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

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class InterferogramRenderingTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("灰度", "Light", 128)]
    [InlineData("伪彩色", "Light", 32)]
    [InlineData("等高线", "Light", 32)]
    [InlineData("灰度", "Dark", 32)]
    public async Task SoftwareAndActualReportRendererPreserveTheSameFringes(string mode, string theme, int sampling)
    {
        var optic = InterferogramAnalysisTests.Lens();
        var raw = new WorkbenchRuntime(optic).BuildAnalysisView("Interferogram", new Dictionary<string, string>
        { ["Sampling"] = sampling.ToString(System.Globalization.CultureInfo.InvariantCulture), ["ShowAs"] = mode, ["XTilt"] = "3", ["YTilt"] = "-1", ["ScaleFactor"] = "2", ["ContourFormat"] = "0.2 0.5 0.8" });
        var mapper = typeof(WorkbenchApplication).Assembly.GetType("OpticalSystemDesign.Application.Services.WorkbenchMapper")!;
        var view = (AnalysisViewDto)mapper.GetMethod("ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [raw])!;
        view = view with { PresentationKind = AnalysisPresentationKind.Interferogram };
        var document = new OpticalDocumentSnapshot("123456.ZMX", "123456.ZMX", 0, "", false, false, 0, 0, 0, 0, 21, 3, 3);
        var time = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(8));
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(InterferogramRenderingTests));
        await session.Dispatch(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var tabs = (TabControl)typeof(AnalysisPanel).GetMethod("BuildResultContent", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [view, document, time, null])!;
            var tab = (TabItem)((object[])tabs.ItemsSource!)[0];
            var page = (ResponsiveAnalysisPlotHost)tab.Content!; tab.Content = null;
            var host = new Border { Width = 1200, Height = 900, Child = page };
            var window = new Window { Width = 1200, Height = 900, Content = host };
            using var directory = new ReportTestDirectory();
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
                var graph = Assert.Single(page.GetVisualDescendants().OfType<WavefrontSurfaceControl>());
                Assert.Equal(mode, graph.DisplayAs);
                Assert.Equal(mode == "伪彩色" ? AnalysisColorMap.Jet : AnalysisColorMap.InverseGray, graph.ColorMapOverride);
                Assert.Equal(new[] { .2, .5, .8 }, graph.ContourLevels);
                Assert.Equal(0, graph.ValueMinimum); Assert.Equal(1, graph.ValueMaximum);
                var captions = page.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                Assert.Contains(captions, t => t.Contains("条纹数/波长 = 2") && t.Contains("X倾斜 = 3，Y倾斜 = -1"));
                using var image = new RenderTargetBitmap(new PixelSize((int)page.Child!.Bounds.Width, (int)page.Child.Bounds.Height), new Vector(96, 96));
                image.Render(page.Child);
                var pixels = Pixels(image);
                Assert.True(pixels.Distinct().Count() > 20);
                host.Child = null;
                var result = new AnalysisResultDto(Guid.NewGuid(), 1, 0, view, new("Interferogram", "test", "test"), document);
                var files = await new ReportImageRenderer(host).WriteAsync(new(result, null, document, time, "干涉图"), directory.Path, "fringes", CancellationToken.None);
                using var exported = new Bitmap(Path.Combine(directory.Path, Assert.Single(files)));
                Assert.Equal(image.PixelSize, exported.PixelSize);
                Assert.Equal(pixels, Pixels(exported));
                var output = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_INTERFEROGRAM_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var suffix = mode == "灰度" ? "gray" : mode == "伪彩色" ? "color" : "contour";
                    File.Copy(Path.Combine(directory.Path, files[0]), Path.Combine(output, $"fringes-{suffix}-{theme}.png"), true);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FullConfigurationContextAndTextSettingsReachWorkerWithoutChangingTheDocument()
    {
        using var application = WorkbenchApplication.Create();
        await application.Documents.OpenAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        application.MultiConfiguration.Add();
        var before = application.Documents.GetSnapshot();
        var settings = new Dictionary<string, string>
        {
            [AnalysisConfigurationSelection.Key] = "2",
            ["Sampling"] = "32 x 32",
            ["Beam1"] = "1",
            ["Beam2"] = "2",
            ["ContourFormat"] = "0.2 0.5 0.8",
            ["ShowAs"] = "等高线",
            ["XTilt"] = "2"
        };
        var parameters = application.Analyses.GetParameters("Interferogram", settings);
        Assert.Contains("2", parameters.Single(p => p.Key == "Beam2").Choices!);
        var result = await application.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Interferogram", settings));
        Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
        Assert.Equal(new[] { .2, .5, .8 }, result.View.InterferogramSummary!.ContourLevels);
        Assert.All(result.View.Series[0].Points, p => Assert.Equal(Math.Pow(Math.Cos(Math.PI * p.X), 2), p.Value!.Value, 10));
        Assert.Equal(before.Revision, application.Documents.GetSnapshot().Revision);
        Assert.Equal(before.Path, application.Documents.GetSnapshot().Path);
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4; var bytes = new byte[stride * bitmap.PixelSize.Height];
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pointer, bytes.Length, stride); Marshal.Copy(pointer, bytes, 0, bytes.Length); return bytes; }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
