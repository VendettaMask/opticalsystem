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
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

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
    [InlineData("Light", 1)]
    [InlineData("Light", 1.25)]
    [InlineData("Light", 2)]
    [InlineData("Dark", 1)]
    [InlineData("Dark", 1.25)]
    [InlineData("Dark", 2)]
    public async Task BorderStaysOutsideOriginalContentAcrossThemesAndPixelScales(string theme, double scale)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var content = new Border
            {
                Width = 120,
                Height = 80,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 30, 40))
            };
            var window = new Window { Width = 120, Height = 80, Content = content };
            try
            {
                window.Show(); Render(window);
                var parent = content.Parent;
                var bounds = content.Bounds;
                using var image = ExportImageFrame.Capture(content, scale);
                Assert.Same(parent, content.Parent);
                Assert.Equal(bounds, content.Bounds);
                Assert.Equal((int)Math.Ceiling(122 * scale), image.PixelSize.Width);
                Assert.Equal((int)Math.Ceiling(82 * scale), image.PixelSize.Height);
                ExportImageAssertions.HasCompleteBorder(image);
                using var stream = new MemoryStream();
                image.Save(stream, PngBitmapEncoderOptions.Default);
                using var bitmap = SkiaSharp.SKBitmap.Decode(stream.ToArray());
                var inset = (int)Math.Ceiling(scale);
                var original = new SkiaSharp.SKColor(220, 30, 40);
                Assert.Equal(original, bitmap.GetPixel(inset, inset));
                Assert.Equal(original, bitmap.GetPixel(bitmap.Width - inset - 1, bitmap.Height - inset - 1));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
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
                    if (surface.DisplayAs == "二维热力图")
                    {
                        Assert.Equal(initialYaw, surface.ViewYawDegrees);
                        Assert.Equal(1, surface.ViewZoom);
                    }
                    else
                    {
                        Assert.NotEqual(initialYaw, surface.ViewYawDegrees);
                        Assert.True(surface.ViewZoom > 1);
                    }
                }
                var camera = (surface?.ViewYawDegrees, surface?.ViewPitchDegrees, surface?.ViewZoom);
                using var image = panel.CapturePlotImage();
                Assert.Equal(camera, (surface?.ViewYawDegrees, surface?.ViewPitchDegrees, surface?.ViewZoom));
                Assert.IsType<UniformDiagramPage>(page.Child);
                Assert.Equal(new PixelSize(2404, 1804), image.PixelSize);
                ExportImageAssertions.HasCompleteBorder(image);
                Assert.True(page.Bounds.Height < height); // No toolbar/tab strip; the exported content may scroll.
                AssertHasInk(image, 0, image.PixelSize.Height / 2); // The plot.
                AssertHasInk(image, image.PixelSize.Height * 4 / 5, image.PixelSize.Height); // The full footer.
                AnalysisPanel.SavePlotImage(image, path);
                using var decoded = new Bitmap(path);
                Assert.Equal(image.PixelSize, decoded.PixelSize);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, File.ReadAllBytes(path)[..8]);

                var offset = page.Offset;
                page.Offset = new Vector(page.Extent.Width, page.Extent.Height); Render(window);
                var scrolledOffset = page.Offset;
                using var scrolled = panel.CapturePlotImage();
                Assert.Equal(Pixels(image), Pixels(scrolled));
                Assert.Equal(scrolledOffset, page.Offset);
                page.Offset = offset; Render(window);

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

    [PlotImageTheory]
    [InlineData(840, 620, 13)]
    [InlineData(420, 280, 32)]
    public async Task CardinalSceneExportsItsGraphicAndDataTableWithoutSeries(int width, int height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            DisplayTypography.Configure(new AppSettings { FontSize = fontSize });
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
            var window = new Window { Width = width, Height = height, Content = panel };
            try
            {
                window.Show(); Render(window);
                Assert.True(panel.GetVisualDescendants().OfType<Button>().Single(item => item.Name == "ExportAnalysisImage").IsVisible);
                var sheet = Assert.Single(panel.GetVisualDescendants().OfType<UniformDiagramPage>());
                Assert.Equal(new Size(1200, 900), sheet.Bounds.Size);
                if (fontSize == 32)
                {
                    Assert.True(sheet.HasOverflow);
                    Assert.Throws<InvalidOperationException>(() => panel.CapturePlotImage());
                    return;
                }
                Assert.False(sheet.HasOverflow);
                using var image = panel.CapturePlotImage();
                AssertHasInk(image, 0, image.PixelSize.Height / 2);
                AssertHasInk(image, image.PixelSize.Height * 4 / 5, image.PixelSize.Height);
                var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>()).Child!;
                Assert.Equal(255, Pixels(image)[((image.PixelSize.Height - 20) * image.PixelSize.Width + 20) * 4 + 3]);
                foreach (var label in page.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible))
                {
                    Assert.True(label.TextLayout.Height <= label.Bounds.Height + 1);
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1);
                }
                var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR")!;
                AnalysisPanel.SavePlotImage(image, Path.Combine(directory, $"Cardinal-{width}x{height}-{fontSize}.png"));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
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

    [PlotImageTheory]
    [InlineData(32_769, 300)]
    [InlineData(8192, 8192)]
    public async Task OversizedContentIsRejectedBeforeAllocatingTheExportBitmap(int width, int height)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            panel.PresentResult(View(AnalysisPresentationKind.Standard), application.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 420, Height = 280, Content = panel };
            try
            {
                window.Show(); Render(window);
                var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>());
                page.Child!.Width = width; page.Child.Height = height; Render(window);
                var exception = Assert.Throws<InvalidOperationException>(() => panel.CapturePlotImage());
                Assert.Contains("尺寸上限", exception.Message);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
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

    [PlotImageTheory]
    [InlineData(AnalysisPresentationKind.Standard)]
    [InlineData(AnalysisPresentationKind.OpticalPathDifference)]
    public async Task AllHiddenWavelengthsExportAxesAndCanBeRestored(AnalysisPresentationKind kind)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisImageExportTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light");
            using var application = WorkbenchApplication.Create();
            using var panel = CreatePanel(application);
            var series = new[] { 0.4861, 0.5876, 0.6563 }.Select((wavelength, index) => new AnalysisSeriesDto(
                "P_y", "W", Enumerable.Range(-10, 21)
                    .Select(x => new AnalysisPointDto(x / 10.0, Math.Pow(x / 10.0, 3) * (index + 1))).ToArray(),
                Name: kind == AnalysisPresentationKind.Standard ? $"Field {index + 1}, Tangential" : $"{wavelength} µm",
                LegendKey: kind == AnalysisPresentationKind.Standard ? "" : $"wavelength:{wavelength.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                LegendLabel: kind == AnalysisPresentationKind.Standard ? "" : $"{wavelength} µm",
                XQuantity: AnalysisAxisQuantity.PupilCoordinate, XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.WavefrontError, YUnit: AnalysisAxisUnit.Wave)).ToArray();
            var view = View(kind) with
            {
                Series = series,
                PlotOptions = new AnalysisPlotOptionsDto(ShowLegend: true),
                PlotPanes = kind == AnalysisPresentationKind.Standard ? [] : Enumerable.Range(0, 6)
                    .Select(index => new AnalysisPlotPaneDto($"Field {index / 2 + 1}",
                        series.Select(item => item with { XAxisLabel = index % 2 == 0 ? "P_y" : "P_x" }).ToArray(),
                        new AnalysisPlotOptionsDto(EqualAspect: true))).ToArray()
            };
            panel.PresentResult(view, application.Documents.GetSnapshot(), DateTimeOffset.Now);
            var window = new Window { Width = 980, Height = 680, Content = panel };
            try
            {
                window.Show(); Render(window);
                var revision = application.Documents.GetSnapshot().Revision;
                using var shown = panel.CapturePlotImage();
                var toggles = panel.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.Equal(3, toggles.Length);
                foreach (var toggle in toggles) toggle.IsChecked = false;
                Render(window);
                var plots = panel.GetVisualDescendants().OfType<AnalysisPlotControl>().ToArray();
                Assert.All(plots, plot => Assert.Empty(plot.Series));
                Assert.True(panel.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ExportAnalysisImage").IsEnabled);
                using var hidden = panel.CapturePlotImage();
                Assert.False(Pixels(shown).SequenceEqual(Pixels(hidden)));
                var page = Assert.Single(panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>()).Child!;
                var bytes = Pixels(hidden);
                foreach (var plot in plots)
                {
                    var frame = (Rect)typeof(AnalysisPlotControl).GetField("_lastPlot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plot)!;
                    var bounds = frame.TransformToAABB(plot.TransformToVisual(page)!.Value);
                    var ink = 0;
                    for (var y = Math.Max(0, (int)bounds.Top - 1); y < Math.Min(hidden.PixelSize.Height, (int)Math.Ceiling(bounds.Bottom) + 1); y++)
                        for (var x = Math.Max(0, (int)bounds.Left - 1); x < Math.Min(hidden.PixelSize.Width, (int)Math.Ceiling(bounds.Right) + 1); x++)
                        {
                            var offset = (y * hidden.PixelSize.Width + x) * 4;
                            if (bytes[offset + 3] > 0 && (bytes[offset] < 220 || bytes[offset + 1] < 220 || bytes[offset + 2] < 220)) ink++;
                        }
                    Assert.True(ink > 40, "Each exported empty plot must retain visible axes and grid.");
                }
                var capture = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR")!;
                Directory.CreateDirectory(capture);
                shown.Save(Path.Combine(capture, $"{kind}-legend-shown.png"), PngBitmapEncoderOptions.Default);
                hidden.Save(Path.Combine(capture, $"{kind}-legend-hidden.png"), PngBitmapEncoderOptions.Default);
                foreach (var toggle in toggles) toggle.IsChecked = true;
                Render(window);
                using var restored = panel.CapturePlotImage();
                Assert.True(Pixels(shown).SequenceEqual(Pixels(restored)));
                Assert.Equal(revision, application.Documents.GetSnapshot().Revision);
                Assert.All(toggles, toggle => Assert.True(toggle.IsChecked));
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
        return new AnalysisViewDto("波前图", kind == AnalysisPresentationKind.WavefrontMap
                ? [new("RMS", "0.1 波"), new("显示为", "二维热力图")] : [new("RMS", "0.1 波")],
            "报告数据", [series], new(),
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
        var pixels = Enumerable.Range(Math.Max(1, startRow), Math.Min(endRow, image.PixelSize.Height - 1) - Math.Max(1, startRow))
            .SelectMany(y => Enumerable.Range(1, image.PixelSize.Width - 2).Select(x => y * image.PixelSize.Width + x))
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
