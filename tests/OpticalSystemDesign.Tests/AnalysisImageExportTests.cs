using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class AnalysisImageExportTests
{
    private static bool RenderImages => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR"));

    public static AppBuilder BuildAvaloniaApp()
    {
        // Avalonia caches glyphs across sessions. Keep real Skia pixel/encoding
        // checks in their own process, as for the existing plot capture tests.
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (RenderImages) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !RenderImages });
    }

    [PlotImageTheory]
    [InlineData(AnalysisPresentationKind.Standard, 840, 620)]
    [InlineData(AnalysisPresentationKind.WavefrontMap, 840, 620)]
    [InlineData(AnalysisPresentationKind.SpotDiagram, 840, 620)]
    [InlineData(AnalysisPresentationKind.FftPsf, 840, 620)]
    [InlineData(AnalysisPresentationKind.Standard, 420, 280)]
    [InlineData(AnalysisPresentationKind.WavefrontMap, 420, 280)]
    public async Task ExportSavesTheCompleteVisiblePlotPageWithoutToolbarOrSettings(AnalysisPresentationKind kind, int width, int height)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            panel.PresentResult(View(kind), application.Documents.GetSnapshot(), new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = width, Height = height, Content = panel };
            var path = Path.Combine(Path.GetTempPath(), $"analysis-export-{Guid.NewGuid():N}.png");
            try
            {
                window.Show(); Render(window);
                var button = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), item => item.Name == "ExportAnalysisImage");
                Assert.True(button.IsVisible);
                Assert.True(button.IsEnabled);
                Assert.Equal("camera", Assert.IsType<LocalIcon>(button.Content).IconName);
                Assert.Equal("导出图片（PNG）", AutomationProperties.GetName(button));
                var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                var surface = panel.GetVisualDescendants().OfType<WavefrontSurfaceControl>().FirstOrDefault();
                if (kind == AnalysisPresentationKind.WavefrontMap)
                {
                    Assert.NotNull(surface);
                    var initialYaw = surface.ViewYawDegrees;
                    var pointer = surface.TranslatePoint(new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(pointer, MouseButton.Left);
                    window.MouseMove(pointer + new Vector(35, 10));
                    window.MouseUp(pointer + new Vector(35, 10), MouseButton.Left);
                    window.MouseWheel(pointer, new Vector(0, 1)); Render(window);
                    Assert.NotEqual(initialYaw, surface.ViewYawDegrees);
                    Assert.True(surface.ViewZoom > 1);
                }
                var camera = (surface?.ViewYawDegrees, surface?.ViewPitchDegrees, surface?.ViewZoom);
                using var image = panel.CapturePlotImage();
                Assert.Equal(camera, (surface?.ViewYawDegrees, surface?.ViewPitchDegrees, surface?.ViewZoom));
                Assert.Equal((int)Math.Ceiling(page.Bounds.Width * window.RenderScaling), image.PixelSize.Width);
                Assert.Equal((int)Math.Ceiling(page.Bounds.Height * window.RenderScaling), image.PixelSize.Height);
                Assert.True(image.PixelSize.Height < height); // No toolbar/tab strip.
                AssertHasInk(image, 0, image.PixelSize.Height / 2); // The plot.
                AssertHasInk(image, image.PixelSize.Height * 4 / 5, image.PixelSize.Height); // The full footer.
                AnalysisPanel.SavePlotImage(image, path);
                using var decoded = new Bitmap(path);
                Assert.Equal(image.PixelSize, decoded.PixelSize);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, File.ReadAllBytes(path)[..8]);

                // Opening settings overlays the graph but must not enter export.
                var settings = (Border)typeof(AnalysisPanel).GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
                settings.IsVisible = true; Render(window);
                using var withSettings = panel.CapturePlotImage();
                Assert.Equal(Pixels(image), Pixels(withSettings));
                var captureDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(captureDirectory))
                {
                    Directory.CreateDirectory(captureDirectory);
                    AnalysisPanel.SavePlotImage(image, Path.Combine(captureDirectory, $"{kind}-{width}x{height}.png"));
                    using var preview = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                    preview.Render(window);
                    preview.Save(Path.Combine(captureDirectory, $"toolbar-{kind}-{width}x{height}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    [PlotImageFact]
    public async Task CardinalSceneExportsItsGraphicAndDataTableWithoutSeries()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            var document = application.Documents.GetSnapshot();
            var scene = new SceneDto(0, SceneDimension.TwoDimensional,
                new Scene2Dto([
                    new SceneSurface2Dto(1, "Lens", false, false, [new(0, -5), new(0, 5)]),
                    new SceneSurface2Dto(2, "Image", false, true, [new(20, -5), new(20, 5)])
                ], [], [], [], -10, 30, 10), null, document);
            panel.PresentResult(View(AnalysisPresentationKind.CardinalPoints) with
            {
                Series = [],
                Table = new AnalysisTableDto(["基面量", "物空间", "像空间"],
                    [["焦平面", "-10", "10"], ["主平面", "1", "2"], ["焦长", "10", "10"]])
            }, document, DateTimeOffset.Now, scene);
            var window = new Window { Width = 840, Height = 620, Content = panel };
            try
            {
                window.Show(); Render(window);
                Assert.True(panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage").IsVisible);
                using var image = panel.CapturePlotImage();
                AssertHasInk(image, 0, image.PixelSize.Height / 2);
                AssertHasInk(image, image.PixelSize.Height * 4 / 5, image.PixelSize.Height);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(UiLocalization.Chinese)]
    [InlineData(UiLocalization.English)]
    [InlineData(UiLocalization.Japanese)]
    public async Task ImageActionHasOnlyAnIconAndLocalizedTooltip(string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            var previous = UiLocalization.CurrentLanguage;
            UiLocalization.InstallGlobalHooks();
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            panel.PresentResult(View(AnalysisPresentationKind.Standard), application.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 840, Height = 620, Content = panel };
            try
            {
                window.Show(); Render(window);
                UiLocalization.Apply(language); Render(window);
                var button = panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage");
                Assert.IsType<LocalIcon>(button.Content);
                Assert.True(ToolTip.GetShowOnDisabled(button));
                Assert.Equal(UiLocalization.Text("导出图片（PNG）", language), ToolTip.GetTip(button));
                Assert.Equal(UiLocalization.Text("导出图片（PNG）", language), AutomationProperties.GetName(button));
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }

    [PlotImageFact]
    public async Task ClickingTheImageIconSavesPngAndCancelPreservesTheFile()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            panel.PresentResult(View(AnalysisPresentationKind.WavefrontMap), application.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 840, Height = 620, Content = panel };
            var path = Path.Combine(Path.GetTempPath(), $"analysis-picker-{Guid.NewGuid():N}.png");
            try
            {
                window.Show(); Render(window);
                FilePickerSaveOptions? options = null;
                var cancel = false;
                var file = DispatchProxy.Create<IStorageFile, StorageProxy>();
                ((StorageProxy)(object)file).Handler = method => method.Name == "get_Path" ? new Uri(path) : null;
                var provider = DispatchProxy.Create<IStorageProvider, StorageProxy>();
                ((StorageProxy)(object)provider).Handler = method => method.Name switch
                {
                    "get_CanSave" => true,
                    "SaveFilePickerAsync" => Task.FromResult<IStorageFile?>(cancel ? null : file),
                    _ => throw new InvalidOperationException(method.Name)
                };
                ((StorageProxy)(object)provider).Arguments = args => options = Assert.IsType<FilePickerSaveOptions>(Assert.Single(args!));
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, provider);
                var button = panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage");
                File.WriteAllBytes(path, [1, 2, 3]);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render(window);
                var saved = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, saved[..8]);
                Assert.NotNull(options);
                Assert.Equal("png", options.DefaultExtension);
                Assert.True(options.ShowOverwritePrompt);
                Assert.Equal("波前图.png", options.SuggestedFileName);
                cancel = true;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render(window);
                Assert.Equal(saved, File.ReadAllBytes(path));
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    public class StorageProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler { get; set; } = _ => null;
        public Action<object?[]?>? Arguments { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "SaveFilePickerAsync") Arguments?.Invoke(args);
            return Handler(targetMethod);
        }
    }

    public sealed class PlotImageFactAttribute : FactAttribute
    {
        public PlotImageFactAttribute()
        {
            if (!RenderImages) Skip = "Set OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR and run AnalysisImageExportTests in a separate process for Skia pixel/PNG checks.";
        }
    }

    public sealed class PlotImageTheoryAttribute : TheoryAttribute
    {
        public PlotImageTheoryAttribute()
        {
            if (!RenderImages) Skip = "Set OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR and run AnalysisImageExportTests in a separate process for Skia pixel/PNG checks.";
        }
    }

    [Fact]
    public async Task ImageActionFollowsGraphSelectionAndNeverExportsTextOrUnavailableResults()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            var window = new Window { Width = 840, Height = 620, Content = panel };
            try
            {
                window.Show(); Render(window);
                var button = panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage");
                Assert.False(button.IsVisible);
                panel.PresentResult(View(AnalysisPresentationKind.Standard), application.Documents.GetSnapshot(), DateTimeOffset.Now);
                Render(window);
                var tabs = Assert.Single(panel.GetVisualDescendants().OfType<TabControl>());
                Assert.True(button.IsVisible);
                foreach (var index in new[] { 1, 2 })
                {
                    tabs.SelectedIndex = index; Render(window);
                    Assert.False(button.IsVisible);
                    Assert.Throws<InvalidOperationException>(() => panel.CapturePlotImage());
                }
                tabs.SelectedIndex = 0; Render(window);
                Assert.True(button.IsVisible);
                foreach (var kind in new[] { AnalysisPresentationKind.Standard, AnalysisPresentationKind.SeidelCoefficients,
                    AnalysisPresentationKind.ZernikeFringe, AnalysisPresentationKind.ZernikeStandard, AnalysisPresentationKind.ZernikeAnnular })
                {
                    panel.PresentResult(View(kind) with { Series = [], PlotPanes = [] }, application.Documents.GetSnapshot(), DateTimeOffset.Now);
                    Render(window);
                    Assert.False(button.IsVisible);
                }
                panel.PresentResult(View(AnalysisPresentationKind.Standard) with { Outcome = AnalysisOutcome.Unavailable }, application.Documents.GetSnapshot(), DateTimeOffset.Now);
                Render(window);
                Assert.False(button.IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static AnalysisPanel CreatePanel(WorkbenchApplication application)
    {
        var panel = new AnalysisPanel(application.Analyses, application.Visualization, application.Documents, application.Events, new AppSettings(), "Wavefront Map");
        // Synthetic DTOs exercise the real UI/export without expensive optical runs.
        typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
        return panel;
    }

    private static AnalysisViewDto View(AnalysisPresentationKind kind)
    {
        var points = Enumerable.Range(-4, 9).SelectMany(y => Enumerable.Range(-4, 9)
            .Select(x => new AnalysisPointDto(x / 4.0, y / 4.0, Value: (x * x + y * y) / 32.0))).ToArray();
        var series = new AnalysisSeriesDto("X", "Y", points, kind == AnalysisPresentationKind.Standard ? AnalysisSeriesKind.Line : AnalysisSeriesKind.Heatmap,
            XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.PupilCoordinate, YUnit: AnalysisAxisUnit.Dimensionless);
        return new AnalysisViewDto("波前图", [new("RMS", "0.1 波")], "报告数据", [series], new(),
            kind == AnalysisPresentationKind.SpotDiagram ? [new AnalysisPlotPaneDto("视场 1", [series], new(), Footer: "0°")] : [],
            1, PresentationKind: kind);
    }

    private static byte[] Pixels(Bitmap image)
    {
        var stride = image.PixelSize.Width * 4;
        var bytes = new byte[stride * image.PixelSize.Height];
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            image.CopyPixels(new PixelRect(image.PixelSize), pointer, bytes.Length, stride);
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static void AssertHasInk(Bitmap image, int startRow, int endRow)
    {
        var bytes = Pixels(image);
        var pixels = Enumerable.Range(startRow * image.PixelSize.Width, (endRow - startRow) * image.PixelSize.Width)
            .Count(index => bytes[index * 4 + 3] > 0 && (bytes[index * 4] < 220 || bytes[index * 4 + 1] < 220 || bytes[index * 4 + 2] < 220));
        Assert.True(pixels > 40, "The exported plot/footer must contain rendered content.");
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
