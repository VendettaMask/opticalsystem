using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class UniformDiagramExportTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Ray Fan")]
    [InlineData("MTF")]
    [InlineData("Field Curvature and Distortion")]
    [InlineData("Wavefront Map")]
    [InlineData("Through Focus")]
    [InlineData("Incoherent Irradiance")]
    public async Task OriginalSheetGeometryFontsAndPixelsStayStableAcrossWindowResizing(string key)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UniformDiagramExportTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings());
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            if (key is "Through Focus" or "Incoherent Irradiance")
                await app.Documents.OpenAsync(SequentialExampleCatalog.Examples.Single(example =>
                    example.RelativePath == "Objectives/Double Gauss 28 degree field.zmx").FilePath);
            var sourceState = app.Documents.GetRecoveryState();
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            if (key == "Through Focus") Assert.Equal(15, result.View.PlotPanes.Count);
            if (key == "Incoherent Irradiance")
            {
                Assert.Equal(9, result.View.PlotPanes.Count);
                Assert.Contains(result.View.Rows, row => row.Metric == "探测器窗口来源" && row.Value.Contains("未添加物理孔径"));
            }
            Assert.Equal(sourceState, app.Documents.GetRecoveryState());
            using var panel = Panel(app, key);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1400, Height = 1100, Content = panel };
            window.Show();
            try
            {
                await Settle(window);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.False(sheet.HasOverflow);
                var original = Geometry(sheet);
                var curves = sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(plot => plot.Series).ToArray();
                foreach (var size in new[] { (420, 420), (1000, 800), (1600, 1100) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    await Settle(window);
                    Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                    Assert.Equal(160, sheet.Caption.Bounds.Height);
                    Assert.Equal(original, Geometry(sheet));
                    Assert.Equal(curves, sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(plot => plot.Series).ToArray());
                    using var image = panel.CapturePlotImage();
                    Assert.Equal(new PixelSize(2404, 1804), image.PixelSize);
                    Assert.Equal(new Vector(192, 192), image.Dpi);
                    ExportImageAssertions.HasCompleteBorder(image);
                    Assert.Equal(original, Geometry(sheet));
                    if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_UNIFORM_DIAGRAM_CAPTURE_DIR") is { } directory)
                    {
                        Directory.CreateDirectory(directory);
                        AnalysisPanel.SavePlotImage(image, Path.Combine(directory, key.Replace(' ', '-') + $"-{size.Item1}x{size.Item2}.png"));
                    }
                }
                using var standard = panel.CapturePlotImage(1);
                using var fractional = panel.CapturePlotImage(1.25);
                Assert.Equal(new PixelSize(1202, 902), standard.PixelSize);
                Assert.Equal(new PixelSize(1503, 1128), fractional.PixelSize);
                Assert.Equal(original, Geometry(sheet));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional)]
    [InlineData(SceneDimension.ThreeDimensional)]
    public async Task LayoutViewAndCameraExportShareTheSameSheetAndKeepCurrentCamera(SceneDimension dimension)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UniformDiagramExportTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension);
            var window = new Window { Width = 1400, Height = 1100, Content = panel };
            window.Show();
            try
            {
                for (var attempt = 0; attempt < 150; attempt++)
                {
                    await Settle(window);
                    if (panel.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExportViewerImage").IsEnabled) break;
                }
                var scene = panel.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                Assert.NotNull(scene.Scene);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                var original = Geometry(sheet);
                var axes = scene.OrientationAxesForRendering();
                foreach (var size in new[] { (420, 420), (1000, 800), (1600, 1100) })
                {
                    window.Width = size.Item1; window.Height = size.Item2;
                    await Settle(window);
                    using var image = panel.CaptureViewerImage();
                    Assert.Equal(new PixelSize(2404, 1804), image.PixelSize);
                    Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                    Assert.Equal(original, Geometry(sheet));
                    Assert.Equal(axes, scene.OrientationAxesForRendering());
                    ExportImageAssertions.HasCompleteBorder(image);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OversizedSourceIsExplicitlyRejectedWithoutClippingShrinkingOrChangingTheResult()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(UniformDiagramExportTests));
        await session.Dispatch(async () =>
        {
            using var app = WorkbenchApplication.Create("cooke");
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Ray Fan", new Dictionary<string, string>()));
            var crowded = result.View with
            {
                PlotPanes = Enumerable.Range(0, 24).SelectMany(index => result.View.PlotPanes.Take(2)
                    .Select(pane => pane with { Title = "Field " + index })).ToArray()
            };
            using var panel = Panel(app, "Ray Fan");
            panel.PresentResult(crowded, app.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 1400, Height = 1100, Content = panel };
            window.Show();
            try
            {
                await Settle(window);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.True(sheet.HasOverflow);
                Assert.Throws<InvalidOperationException>(() => panel.CapturePlotImage());
                Assert.Equal(48, crowded.PlotPanes.Count);
                Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                panel.PresentResult(result.View, app.Documents.GetSnapshot(), DateTimeOffset.Now);
                await Settle(window);
                using var recovered = panel.CapturePlotImage();
                Assert.Equal(new PixelSize(2404, 1804), recovered.PixelSize);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static AnalysisPanel Panel(WorkbenchApplication app, string key)
    {
        var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), key);
        typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
        return panel;
    }

    private static string[] Geometry(UniformDiagramPage sheet) => sheet.GetLogicalDescendants().OfType<Control>()
        .Where(control => control is AnalysisPlotControl or WavefrontSurfaceControl or CompanyLogoControl or TextBlock)
        .Select(control => $"{control.GetType().Name}|{control.Bounds}|{(control as TextBlock)?.FontSize}").ToArray();

    private static async Task Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); await Task.Delay(16); }
    }
}
