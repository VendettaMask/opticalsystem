using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ScientificIconTests
{
    private static readonly string[] Approved =
    [
        "tolerance-data-editor", "tolerance-analysis", "tolerance-data-viewer", "tolerance-histogram", "tolerance-yield", "tolerance-report",
        "user-guide", "operand-help", "recover-unsaved-project", "about", "window-layout", "clone-analysis", "toggle-page-lock",
        "close-other-pages", "save-default-layout", "load-default-layout"
    ];

    [Fact]
    public void EveryCatalogEntryIsNativeVectorAndUnknownNamesUseTheScientificFallback()
    {
        Assert.Equal(1969, ScientificIconLibrary.Names.Count);
        foreach (var name in LocalIconLibrary.Names.Concat(Approved))
            Assert.Contains(name, ScientificIconLibrary.Names);
        foreach (var name in ScientificIconLibrary.Names)
        {
            var icon = ScientificIconLibrary.Resolve(name);
            Assert.Null(icon.Image);
            Assert.NotNull(icon.Artwork);
            Assert.True(icon.Artwork.PartCount > 0, name);
            Assert.All(icon.Artwork.Roles, role => Assert.InRange(role, 0, 2));
        }
        Assert.Same(ScientificIconLibrary.Resolve("circle-question-mark"), ScientificIconLibrary.Resolve("unknown-icon"));
        Assert.NotSame(ScientificIconLibrary.Resolve("user-guide"), ScientificIconLibrary.Resolve("operand-help"));
        Assert.NotSame(ScientificIconLibrary.Resolve("save"), ScientificIconLibrary.Resolve("save-default-layout"));
    }

    [Fact]
    public async Task EntireScientificCatalogParsesThroughTheNativeRenderer()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var name in ScientificIconLibrary.Names)
                ScientificIconLibrary.Resolve(name).Artwork!.EnsureGeometry();
        }, CancellationToken.None);
    }

    [Fact]
    public void EveryThemeOwnsACompleteScientificPackageAndItsActualColours()
    {
        Assert.Equal(5, ThemeRegistry.ConcreteThemes.Select(theme => theme.IconPack.Id).Distinct().Count());
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            Assert.IsType<ScientificThemeIconPack>(theme.IconPack);
            foreach (var name in Approved)
            {
                Assert.True(theme.IconPack.TryResolve(name, out var icon));
                Assert.Same(ScientificIconLibrary.Resolve(name, theme.SettingsValue), icon);
            }
            Assert.True(Contrast(theme.IconPalette.Signal, theme.Palette!.Surface) >= 3, theme.SettingsValue);
        }
        var dark = ThemeRegistry.FromSettings("Dark").IconPalette;
        Assert.Equal(Color.Parse("#A5978B"), dark.Structure);
        Assert.Equal(Color.Parse("#F0AE79"), dark.Signal);
        Assert.Equal(Color.Parse("#D5B89C"), dark.Secondary);
        Assert.Equal(Color.Parse("#78DCFF"), ThemeRegistry.FromSettings("Lumen").IconPalette.Signal);
        Assert.Equal(PixelTheme.Palette.TextAccent, ThemeRegistry.FromSettings("Pixel").IconPalette.Signal);
        Assert.Equal(Color.Parse("#EACC8B"), ThemeRegistry.FromSettings("Isekai").IconPalette.Signal);
    }

    [Fact]
    public async Task ExistingIconRepaintsAllThreeRolesAndDisabledStateAcrossEveryTheme()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            var icon = new LocalIcon { IconName = "window-layout", Width = 64, Height = 64 };
            var window = new Window { Width = 140, Height = 140, Content = icon };
            var hashes = new HashSet<string>();
            try
            {
                window.Show();
                foreach (var theme in ThemeRegistry.ConcreteThemes)
                {
                    ThemeApplicationService.Apply(app, theme.SettingsValue); Tick(window);
                    Assert.Equal(theme.RequestedVariant, icon.ActualThemeVariant);
                    Assert.Equal(new Size(64, 64), icon.Bounds.Size);
                    var bytes = Render(icon);
                    hashes.Add(Convert.ToHexString(SHA256.HashData(bytes)));
                    using var bitmap = SKBitmap.Decode(bytes);
                    var expectedColours = theme.IconPalette.Finish == ScientificIconFinish.SteelHud
                        ? new[] { theme.IconPalette.Signal, theme.IconPalette.Secondary }
                        : new[] { theme.IconPalette.Structure, theme.IconPalette.Signal, theme.IconPalette.Secondary };
                    foreach (var colour in expectedColours)
                        Assert.True(HasColour(bitmap, colour), $"Missing actual {colour} pixels for {theme.SettingsValue}");
                    if (theme.IconPalette.Finish == ScientificIconFinish.SteelHud)
                        Assert.IsAssignableFrom<ILinearGradientBrush>(theme.IconPalette.Brush(0, false));
                    icon.IsEnabled = false; Tick(window);
                    using var disabled = SKBitmap.Decode(Render(icon));
                    Assert.True(HasColour(disabled, theme.Palette!.TextDisabled), theme.SettingsValue);
                    Assert.False(HasColour(disabled, theme.IconPalette.Signal), theme.SettingsValue);
                    icon.IsEnabled = true;
                }
                Assert.Equal(5, hashes.Count);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task AccentedSurfacesKeepActualIconPixelsReadableDuringStateChanges(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, themeName);
            var theme = ThemeRegistry.FromSettings(themeName);
            var icon = new LocalIcon { IconName = "window-layout", Width = 64, Height = 64 };
            var host = new Button { Content = icon, Background = new SolidColorBrush(theme.IconPalette.Signal),
                Foreground = new SolidColorBrush(theme.Palette!.TextOnAccent), Width = 100, Height = 100 };
            var window = new Window { Width = 180, Height = 180, Content = host };
            try
            {
                window.Show(); Tick(window);
                using var painted = SKBitmap.Decode(Render(icon));
                var readable = themeName == "Pixel" ? theme.Palette.Surface : theme.Palette.TextOnAccent;
                Assert.True(HasColour(painted, readable));
                Assert.False(HasColour(painted, theme.IconPalette.Signal));
                host.Background = new SolidColorBrush(theme.Palette.Surface); Tick(window);
                using var normal = SKBitmap.Decode(Render(icon));
                Assert.True(HasColour(normal, theme.IconPalette.Signal));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Lumen")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task NativeAtlasIncludesApprovedAndGeneralIconsAtRealUiSizes(string themeName)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, themeName);
            var theme = ThemeRegistry.FromSettings(themeName);
            var names = Approved.Concat(new[] { "file-plus", "folder-open", "save", "settings", "refresh-cw", "camera", "search", "zoom-in", "zoom-out", "layers", "aperture", "database", "sparkles", "wand", "plus", "trash", "arrow-up", "arrow-down", "check", "x" }).ToArray();
            var cells = new WrapPanel { Width = 1120 };
            foreach (var name in names)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                foreach (var size in new[] { 18, 24, 32 }) row.Children.Add(new LocalIcon { IconName = name, Width = size, Height = size });
                var content = new StackPanel { Spacing = 10, Children =
                    { row, new TextBlock { Text = name, FontSize = 11, Foreground = theme.IconPalette.Brushes[0] } } };
                cells.Children.Add(new Border { Width = 224, Height = 85, Padding = new Thickness(12), Child = content });
            }
            var root = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children =
                { new TextBlock { Text = theme.DisplayName + "  —  18 / 24 / 32 DIP", FontSize = 20 }, cells } };
            var window = new Window { Width = 1160, Height = 740, Background = new SolidColorBrush(theme.Palette!.Surface), Content = root };
            try { window.Show(); Tick(window); Capture(window, $"atlas-{themeName}.png"); }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(18)]
    public async Task ActualWorkbenchUsesApprovedSemanticsAcrossThemesWithoutChangingTheLens(double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var workbench = WorkbenchApplication.Create();
            await workbench.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var original = workbench.Documents.GetRecoveryState();
            var settings = new AppSettings { Theme = "Light", FontSize = fontSize, WindowWidth = 1500, WindowHeight = 950 };
            var store = new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "scientific-icons", Guid.NewGuid().ToString("N")));
            var window = new MainWindow(settings, workbench, store);
            var ready = false;
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var attempt = 0; attempt < 200 && !ready; attempt++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.Classes.Contains("ribbon-tabs"));
                foreach (var theme in ThemeRegistry.ConcreteThemes)
                {
                    ThemeApplicationService.Apply(Avalonia.Application.Current!, theme.SettingsValue);
                    var found = new HashSet<string>();
                    foreach (var tabName in new[] { "公差", "帮助", "窗口" })
                    {
                        tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => AutomationProperties.GetName(t) == UiLocalization.Text(tabName));
                        Tick(window);
                        foreach (var icon in window.GetVisualDescendants().OfType<LocalIcon>())
                        {
                            Assert.True(ScientificIconLibrary.Contains(icon.IconName), icon.IconName);
                            if (Approved.Contains(icon.IconName)) found.Add(icon.IconName);
                        }
                        if (tabName == "公差") Capture(window, $"workbench-{theme.SettingsValue}-{fontSize}.png");
                    }
                    Assert.Equal(16, found.Count);
                    Assert.Equal(original, workbench.Documents.GetRecoveryState());
                }
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static byte[] Render(Control control)
    {
        using var image = new RenderTargetBitmap(new PixelSize(64, 64));
        image.Render(control);
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static bool HasColour(SKBitmap image, Color colour)
    {
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var p = image.GetPixel(x, y);
            if (p.Alpha >= 250 && Math.Abs(p.Red - colour.R) <= 1 && Math.Abs(p.Green - colour.G) <= 1 && Math.Abs(p.Blue - colour.B) <= 1) return true;
        }
        return false;
    }

    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_ICON_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double C(byte v) { var s = v / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
        }
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
}
