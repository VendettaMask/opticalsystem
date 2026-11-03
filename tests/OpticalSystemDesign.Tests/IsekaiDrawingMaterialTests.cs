using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
public sealed class IsekaiDrawingMaterialTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Huygens MTF")]
    [InlineData("Spot Diagram")]
    [InlineData("Wavefront Map")]
    public async Task PaperRendersActualAnalysesWithoutChangingGeometryResultsOrOtherThemes(string key)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiDrawingMaterialTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings());
            ImageExportResolution.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), key);
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1440, Height = 1080, Content = panel };
            try
            {
                window.Show(); Settle(window);
                var sheet = panel.GetVisualDescendants().OfType<UniformDiagramPage>().Single();
                var state = app.Documents.GetRecoveryState();
                var originalSeries = sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(p => p.Series).ToArray();
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel" })
                {
                    Apply(theme, window);
                    using var before = panel.CapturePlotImage(1);
                    var beforeGeometry = Geometry(sheet);
                    Apply("Isekai", window);
                    if (theme != "Pixel") Assert.Equal(beforeGeometry, Geometry(sheet));
                    Assert.False(sheet.HasOverflow);
                    using var paper = panel.CapturePlotImage(1);
                    Assert.NotEqual(Hash(before), Hash(paper));
                    var inset = (int)ThemeDrawingSurface.FrameInset(sheet);
                    AssertPaper(paper, inset + 1180, inset + 400);
                    Assert.Equal(state, app.Documents.GetRecoveryState());
                    Assert.Equal(originalSeries, sheet.GetVisualDescendants().OfType<AnalysisPlotControl>().Select(p => p.Series).ToArray());
                    Apply(theme, window);
                    using var after = panel.CapturePlotImage(1);
                    Assert.Equal(Hash(before), Hash(after));
                }
                Apply("Isekai", window);
                using var full = panel.CapturePlotImage(1);
                Save(full, key.Replace(' ', '-') + ".png");
                Assert.Equal(new PixelSize(1256, 956), full.PixelSize);
                using var configured = panel.CapturePlotImage();
                Assert.Equal(new PixelSize(1920, 1080), configured.PixelSize);
                Save(configured, key.Replace(' ', '-') + "-1080p.png");
                using var report = ExportImageFrame.CaptureForReport(sheet);
                Assert.Equal(ImageExportResolution.SheetPixels(1080), report.PixelSize);
                AssertPaper(report, report.PixelSize.Width * 11 / 12, report.PixelSize.Height * 6 / 10);
                using (var live = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(live);
                    Save(live, key.Replace(' ', '-') + "-live.png");
                }
                var host = panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                var frame = Assert.IsType<AnalysisImageFrame>(sheet.Parent);
                var framedBounds = new Rect(frame.Bounds.Size).TransformToAABB(frame.TransformToVisual(host)!.Value);
                Assert.True(framedBounds.Left >= -1 && framedBounds.Top >= -1);
                Assert.True(framedBounds.Right <= host.Viewport.Width + 1 && framedBounds.Bottom <= host.Viewport.Height + 1);
                window.Width = 600; window.Height = 440; Settle(window);
                using var small = panel.CapturePlotImage(1);
                Assert.Equal(Hash(full), Hash(small));
                var toggle = sheet.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
                if (toggle is not null)
                {
                    toggle.IsChecked = false; Settle(window);
                    using var filtered = panel.CapturePlotImage(1);
                    Assert.NotEqual(Hash(full), Hash(filtered));
                    toggle.IsChecked = true; Settle(window);
                    using var restored = panel.CapturePlotImage(1);
                    Assert.Equal(Hash(full), Hash(restored));
                }
                host.FitToViewport = false; Settle(window);
                Assert.Equal(1, host.ContentScale);
                Assert.True(host.Extent.Width >= full.PixelSize.Width && host.Extent.Height >= full.PixelSize.Height);
                host.FitToViewport = true; Settle(window);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(SceneDimension.TwoDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.OpticalLayout)]
    [InlineData(SceneDimension.ThreeDimensional, ViewerPresentationMode.SolidModel)]
    public async Task SceneAndExportSharePaperAndPreserveCameraAndMaterialColours(SceneDimension dimension, ViewerPresentationMode mode)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiDrawingMaterialTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new ViewerPanel(app.Visualization, app.Events, new SurfaceSelectionService(), dimension, mode);
            var window = new Window { Width = 1200, Height = 960, Content = panel };
            try
            {
                window.Show();
                for (var i = 0; i < 200; i++)
                {
                    await Task.Delay(10); Settle(window);
                    if (panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportViewerImage").IsEnabled) break;
                }
                var scene = panel.GetVisualDescendants().OfType<OpticSceneControl>().Single();
                Assert.NotNull(scene.Scene);
                scene.SetViewPreset(OpticSceneViewPreset.Isometric);
                var source = scene.Scene;
                var state = app.Documents.GetRecoveryState();
                var camera = scene.OrientationAxesForRendering();
                foreach (var theme in new[] { "Light", "Dark", "Lumen", "Pixel" })
                {
                    Apply(theme, window);
                    using var before = panel.CaptureViewerImage(1);
                    Apply("Isekai", window);
                    using var paper = panel.CaptureViewerImage(1);
                    AssertPaper(paper, paper.PixelSize.Width / 2, (int)ThemeDrawingSurface.FrameInset(scene) + 80);
                    Assert.Same(source, scene.Scene);
                    Assert.Equal(camera, scene.OrientationAxesForRendering());
                    Assert.Equal(state, app.Documents.GetRecoveryState());
                    Apply(theme, window);
                    using var after = panel.CaptureViewerImage(1);
                    Assert.Equal(Hash(before), Hash(after));
                }
                Apply("Isekai", window);
                using var image = panel.CaptureViewerImage(1);
                Save(image, $"{dimension}-{mode}.png");
                using (var composed = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(composed);
                    var p = scene.TranslatePoint(new Point(scene.Bounds.Width / 2, 80), window)!.Value;
                    AssertPaper(composed, (int)p.X, (int)p.Y);
                }
                using var live = new RenderTargetBitmap(new PixelSize(1200, 960));
                live.Render(window); Save(live, $"{dimension}-{mode}-live.png");
                // The exact background is shared by live scene drawing and the
                // focus-free scene used for export; no synthetic lens rendering.
                using var sceneImage = new RenderTargetBitmap(new PixelSize((int)scene.Bounds.Width, (int)scene.Bounds.Height));
                sceneImage.Render(scene);
                AssertPaper(sceneImage, sceneImage.PixelSize.Width / 2, 80);
                AssertInteriorBotanicalInk(sceneImage);
                var rays = scene.ShowRays;
                scene.ShowRays = false; scene.InvalidateVisual(); Settle(window);
                using var hidden = panel.CaptureViewerImage(1);
                Assert.NotEqual(Hash(image), Hash(hidden));
                scene.ShowRays = rays; scene.InvalidateVisual(); Settle(window);
                using var restored = panel.CaptureViewerImage(1);
                Assert.Equal(Hash(image), Hash(restored));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OrnamentCannotCropOrResampleTheSourceEdgesAndOnePixelDetails()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(IsekaiDrawingMaterialTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai");
            var source = new Canvas { Width = 1200, Height = 900, Background = Brushes.White };
            foreach (var (x, y, width, height, brush) in new (int, int, int, int, IBrush)[]
            {
                (0, 0, 12, 12, Brushes.Red), (1188, 0, 12, 12, Brushes.Green),
                (0, 888, 12, 12, Brushes.Blue), (1188, 888, 12, 12, Brushes.Orange),
                (600, 0, 1, 900, Brushes.Black), (0, 450, 1200, 1, Brushes.Black)
            })
            {
                var mark = new Border { Width = width, Height = height, Background = brush };
                Canvas.SetLeft(mark, x); Canvas.SetTop(mark, y); source.Children.Add(mark);
            }
            var window = new Window { Width = 1200, Height = 900, Content = source };
            try
            {
                window.Show(); Settle(window);
                using var image = ExportImageFrame.Capture(source);
                AssertSourcePixelsUnchanged(source, image);
                var pixels = Pixels(image);
                foreach (var (x, y) in new[] { (0, 0), (image.PixelSize.Width - 1, 0),
                    (0, image.PixelSize.Height - 1), (image.PixelSize.Width - 1, image.PixelSize.Height - 1) })
                    Assert.Equal(0, pixels[(y * image.PixelSize.Width + x) * 4 + 3]);
                Assert.Equal(255, pixels[(14 * image.PixelSize.Width + image.PixelSize.Width / 2) * 4 + 3]);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Apply(string theme, Window window)
    {
        ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
        Settle(window);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }

    private static string[] Geometry(UniformDiagramPage sheet) => sheet.GetVisualDescendants()
        .Where(c => c is AnalysisPlotControl or WavefrontSurfaceControl or TextBlock or CompanyLogoControl)
        .Select(c => $"{c.GetType().Name}|{c.Bounds}|{(c as TextBlock)?.FontSize}").ToArray();

    private static byte[] Pixels(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4;
        var bytes = new byte[stride * bitmap.PixelSize.Height];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { pinned.Free(); }
        return bytes;
    }

    private static string Hash(Bitmap bitmap) => Convert.ToHexString(SHA256.HashData(Pixels(bitmap)));

    private static void AssertSourcePixelsUnchanged(Control sheet, Bitmap framed)
    {
        var sourceSize = new PixelSize(1200, 900);
        Assert.Equal(new Size(sourceSize.Width, sourceSize.Height), sheet.Bounds.Size);
        using var source = new RenderTargetBitmap(sourceSize);
        source.Render(sheet);
        var expected = Pixels(source); var actual = Pixels(framed);
        var inset = (framed.PixelSize.Width - sourceSize.Width) / 2;
        Assert.True(inset > 1);
        Assert.Equal(sourceSize.Height + 2 * inset, framed.PixelSize.Height);
        // Crisp pixel-aligned fiducials isolate cropping/scaling from Skia's
        // translation-dependent coverage of fractional vector/text edges.
        for (var y = 0; y < sourceSize.Height; y++)
            Assert.True(expected.AsSpan(y * sourceSize.Width * 4, sourceSize.Width * 4).SequenceEqual(
                actual.AsSpan(((y + inset) * framed.PixelSize.Width + inset) * 4, sourceSize.Width * 4)),
                $"The decorative frame changed source pixels at row {y}.");
    }

    private static void AssertPaper(Bitmap bitmap, int x, int y)
    {
        var bytes = Pixels(bitmap); var offset = (y * bitmap.PixelSize.Width + x) * 4;
        var red = bytes[offset + (bitmap.Format == PixelFormat.Rgba8888 ? 0 : 2)];
        var blue = bytes[offset + (bitmap.Format == PixelFormat.Rgba8888 ? 2 : 0)];
        Assert.True(red > 200 && bytes[offset + 1] > 180 && blue < red,
            $"Expected warm paper at {x},{y}; RGB={red},{bytes[offset + 1]},{blue}.");
    }

    private static void AssertInteriorBotanicalInk(Bitmap sourceScene)
    {
        // This capture is the scene alone: no external frame or floating UI.
        // The fixture's real optical geometry is below this top-left paper area.
        var pixels = Pixels(sourceScene);
        var redIndex = sourceScene.Format == PixelFormat.Rgba8888 ? 0 : 2;
        var blueIndex = sourceScene.Format == PixelFormat.Rgba8888 ? 2 : 0;
        var ink = 0;
        for (var y = 0; y < sourceScene.PixelSize.Height / 5; y++)
            for (var x = 0; x < sourceScene.PixelSize.Width / 5; x++)
            {
                var offset = (y * sourceScene.PixelSize.Width + x) * 4;
                if (pixels[offset + redIndex] < 200 && pixels[offset + 1] < 180
                    && pixels[offset + redIndex] > pixels[offset + blueIndex] + 10) ink++;
            }
        Assert.True(ink > 500, $"Expected botanical ink inside the source scene; found {ink} pixels.");
    }

    private static void Save(Bitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ISEKAI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
