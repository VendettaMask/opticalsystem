using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
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
public sealed class AnalysisResultBookTabsTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData("Huygens MTF", 0)]
    [InlineData("Huygens PSF", 1)]
    [InlineData("Wavefront Map", 2)]
    [InlineData("Spot Diagram", 3)]
    public async Task BookContainsWorkingNativeTabsAndOnlySafeTypedAccessories(string key, int artwork)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultBookTabsTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings()); ImageExportResolution.Configure(new AppSettings());
            using var app = WorkbenchApplication.Create("cooke");
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, key, new Dictionary<string, string>()));
            Assert.Equal(AnalysisOutcome.Success, result.View.Outcome);
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), key);
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1440, Height = 1080, Content = panel };
            try
            {
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Isekai"); window.Show(); Settle(window);
                var tabs = panel.GetVisualDescendants().OfType<AnalysisResultTabs>().Single();
                var sheet = panel.GetVisualDescendants().OfType<UniformDiagramPage>().Single();
                var host = panel.GetVisualDescendants().OfType<ResponsiveAnalysisPlotHost>().Single();
                Assert.Equal((AnalysisArtworkKind)artwork, AnalysisArtwork.GetKind(sheet));
                Assert.Equal(AnalysisArtwork.GetKind(sheet), AnalysisArtwork.For(result.View with { Name = "任意翻译名称" }));
                Assert.Equal(new Size(1200, 900), sheet.Bounds.Size); Assert.False(sheet.HasOverflow);
                Assert.True(Assert.IsType<AnalysisImageFrame>(sheet.Parent).IsIntegrated);
                var book = panel.GetVisualDescendants().OfType<AnalysisImageFrame>().Single(f => f.Name == "AnalysisResultBookFrame");
                AssertContainedTabs(tabs, book);
                var workspace = panel.GetVisualDescendants().OfType<AnalysisImageFrame>().Single(f => f.Name == "AnalysisWorkspaceBookFrame");
                var commands = panel.GetVisualDescendants().OfType<WrapPanel>().Single(c => c.Classes.Contains("analysis-actions"));
                foreach (var command in commands.Children.Where(c => c.IsVisible))
                {
                    var rect = new Rect(command.Bounds.Size).TransformToAABB(command.TransformToVisual(workspace)!.Value);
                    Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= workspace.Bounds.Width && rect.Bottom < workspace.Bounds.Height);
                }
                var settings = Assert.IsType<Button>(commands.Children[0]);
                var card = (Border)typeof(AnalysisPanel).GetField("_settingsHost", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
                Assert.False(card.IsVisible);
                var settingsPoint = settings.TranslatePoint(new Point(settings.Bounds.Width / 2, settings.Bounds.Height / 2), window)!.Value;
                window.MouseDown(settingsPoint, MouseButton.Left); window.MouseUp(settingsPoint, MouseButton.Left); Settle(window);
                Assert.True(card.IsVisible); Assert.NotEmpty(card.GetVisualDescendants().OfType<TextBlock>());
                var cardBounds = new Rect(card.Bounds.Size).TransformToAABB(card.TransformToVisual(workspace)!.Value);
                Assert.True(cardBounds.Left >= 0 && cardBounds.Top > settings.Bounds.Height && cardBounds.Right <= workspace.Bounds.Width);
                window.MouseDown(settingsPoint, MouseButton.Left); window.MouseUp(settingsPoint, MouseButton.Left); Settle(window);
                Assert.False(card.IsVisible);
                var state = app.Documents.GetRecoveryState();
                using var original = panel.CapturePlotImage(1);
                var hash = Hash(original);
                Assert.Equal(new PixelSize(1256, 956), original.PixelSize);
                var folio = sheet.GetVisualDescendants().OfType<ThemeDrawingSurface.CaptionMargin>().Single();
                Assert.NotNull(folio.OrnamentBounds); // These four fixtures have measured caption space.
                if (folio.OrnamentBounds is { } art)
                    Assert.DoesNotContain(folio.OccupiedTextBounds(), text => text.Intersects(art));
                var toggle = sheet.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
                if (toggle is not null) { toggle.IsChecked = false; Settle(window); }
                var data = Assert.IsType<AnalysisResultTab>(tabs.Items[1]);
                var report = Assert.IsType<AnalysisResultTab>(tabs.Items[2]);
                var plot = Assert.IsType<AnalysisResultTab>(tabs.Items[0]);
                plot.Focus(); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); Settle(window);
                Assert.Equal(1, tabs.SelectedIndex); Assert.NotNull(data.Content);
                tabs.SelectedIndex = 2; Settle(window);
                Assert.Equal(result.View.ReportText, Assert.IsType<TextBox>(report.Content).Text);
                tabs.SelectedIndex = 0; Settle(window);
                Assert.Same(sheet, host.Child);
                if (toggle is not null) { Assert.False(toggle.IsChecked); toggle.IsChecked = true; Settle(window); }
                using var restored = panel.CapturePlotImage(1); Assert.Equal(hash, Hash(restored));
                window.Width = 600; window.Height = 440; Settle(window); AssertContainedTabs(tabs, book);
                using var small = panel.CapturePlotImage(1); Assert.Equal(hash, Hash(small));
                host.FitToViewport = false; Settle(window); Assert.Equal(1, host.ContentScale);
                host.FitToViewport = true; window.Width = 1440; window.Height = 1080; Settle(window);
                Assert.Equal(state, app.Documents.GetRecoveryState());
                using var live = window.CaptureRenderedFrame(); Assert.NotNull(live);
                Save(live, key.Replace(' ', '-') + "-live.png"); Save(restored, key.Replace(' ', '-') + "-source.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Pixel")]
    public async Task OtherThemesKeepNativePixelsThroughBookThemeRoundTrips(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(AnalysisResultBookTabsTests));
        await session.Dispatch(async () =>
        {
            DisplayTypography.Configure(new AppSettings()); ImageExportResolution.Configure(new AppSettings());
            // Start each theme from its own realized tree, avoiding a cached live
            // frame inherited from a different non-material theme in headless Skia.
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var result = await app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "Huygens MTF", new Dictionary<string, string>()));
            using var panel = new AnalysisPanel(app.Analyses, app.Visualization, app.Documents, app.Events, new AppSettings(), "Huygens MTF");
            typeof(AnalysisPanel).GetField("_initialRunRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(panel, true);
            panel.PresentResult(result.View, app.Documents.GetSnapshot(), new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(8)));
            var window = new Window { Width = 1440, Height = 1080, Content = panel };
            try
            {
                window.Show(); Settle(window);
                var hashes = new SortedDictionary<string, string>();
                {
                    Apply(theme, window);
                    // Retain the historical capture order for the optional baseline receipt.
                    using var baselineLive = window.CaptureRenderedFrame(); Assert.NotNull(baselineLive);
                    using var png = panel.CapturePlotImage(1);
                    var sheet = panel.GetVisualDescendants().OfType<UniformDiagramPage>().Single();
                    using var report = ExportImageFrame.CaptureForReport(sheet);
                    hashes[theme + "/live"] = Hash(baselineLive); hashes[theme + "/png"] = Hash(png); hashes[theme + "/report"] = Hash(report);
                    // Flush the headless renderer after source capture before comparing live frames.
                    Settle(window);
                    using var before = window.CaptureRenderedFrame(); Assert.NotNull(before);
                    Apply("Isekai", window); Apply(theme, window);
                    using var after = window.CaptureRenderedFrame(); Assert.NotNull(after);
                    if (Hash(before) != Hash(after))
                    {
                        Save(before, theme + "-before-failure.png"); Save(after, theme + "-after-failure.png");
                    }
                    Assert.True(Hash(before) == Hash(after), theme + " live pixels changed after returning from Isekai");
                }
                if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOOK_TABS_BASELINE") is { } baselinePath)
                {
                    var baseline = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(baselinePath))!;
                    foreach (var (key, value) in hashes.Where(pair => !pair.Key.EndsWith("/live"))) Assert.Equal(baseline[key], value);
                }
                if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOOK_TABS_CAPTURE_DIR") is { } directory)
                { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, theme + "-pixels.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true })); }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertContainedTabs(AnalysisResultTabs tabs, AnalysisImageFrame frame)
    {
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            var rect = new Rect(item.Bounds.Size).TransformToAABB(item.TransformToVisual(frame)!.Value);
            Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= frame.Bounds.Width + 1 && rect.Bottom < frame.Bounds.Height);
        }
    }
    private static void Apply(string theme, Window window) { ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); Settle(window); }
    private static void Settle(Window window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    }
    private static string Hash(Bitmap image)
    {
        var bytes = new byte[image.PixelSize.Width * image.PixelSize.Height * 4]; var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(image.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, image.PixelSize.Width * 4); }
        finally { handle.Free(); }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    private static void Save(Bitmap image, string name)
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BOOK_TABS_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory); image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
