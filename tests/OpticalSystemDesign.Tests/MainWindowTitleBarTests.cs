using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MainWindowTitleBarTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CAPTION_REPAIR_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OpticalSystemDesign.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Pixel")]
    [InlineData("Isekai")]
    public async Task RealCaptionButtonsKeepEffectsContentAndFullHitAreas(string theme)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MainWindowTitleBarTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            var window = new Window { Width = 400, Height = 160 };
            window.Content = MainWindowTitleBar.Wrap(window, new Border());
            window.Show();
            try
            {
                var host = Assert.Single(window.GetVisualAncestors().OfType<Control>(), control => control.GetType().Name == "TopLevelHost");
                // Headless has no native decoration hint. Exercise the same
                // platform host that Windows uses, without creating a desktop window.
                var update = host.GetType().GetMethod("UpdateDrawnDecorations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                var partType = Nullable.GetUnderlyingType(update.GetParameters()[0].ParameterType)!;
                void UpdateDecorations() => update.Invoke(host, new[] { Enum.Parse(partType, "TitleBar"), window.WindowState, window.WindowDecorationsTheme });
                UpdateDecorations();
                var overlay = host;
                Tick(window);
                Capture(host, $"caption-{theme}-idle");
                Geometry MaximizeGeometry() => Assert.Single(Assert.Single(overlay.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_MaximizeButton")
                    .GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()).Data!;
                var normalGeometry = MaximizeGeometry();
                window.WindowState = WindowState.Maximized;
                UpdateDecorations();
                Tick(window);
                Assert.NotSame(normalGeometry, MaximizeGeometry());
                Capture(host, $"caption-{theme}-restore");
                window.WindowState = WindowState.Normal;
                UpdateDecorations();
                Tick(window);
                Assert.Same(normalGeometry, MaximizeGeometry());
                foreach (var name in new[] { "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton" })
                {
                    var button = Assert.Single(overlay.GetVisualDescendants().OfType<Button>(), control => control.Name == name);
                    Assert.True(button.IsEffectivelyVisible);
                    var glyph = Assert.Single(button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                    Assert.True(glyph.Fill is ISolidColorBrush glyphBrush && glyphBrush.Color == MainWindowTitleBar.CaptionButtonGlyphColor,
                        $"Glyph fill={glyph.Fill}; content={button.Content?.GetType()}; button foreground={button.Foreground}; parent={glyph.Parent}");
                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CAPTION_REPAIR_CAPTURE_DIR")))
                        Assert.True(glyph.Data!.Bounds.Width > 0 && glyph.Data.Bounds.Height > 0,
                            $"Native glyph has empty geometry: {name}; bounds={glyph.Data.Bounds}");
                    Assert.Equal(45, button.Bounds.Width);
                    var disc = Assert.Single(button.GetVisualDescendants().OfType<Border>(), control => control.Name == "PART_TrafficLightDisc");
                    Assert.Equal(new Size(22, 22), disc.Bounds.Size);
                    var idle = Assert.IsAssignableFrom<ILinearGradientBrush>(disc.Background);
                    Assert.Equal(ThemeChromeResources.CaptionButtonShadow, disc.BoxShadow);
                    var originalSize = button.Bounds.Size;
                    ((IPseudoClasses)button.Classes).Set(":pointerover", true);
                    Tick(window);
                    Assert.NotEqual(idle, disc.Background);
                    var hovered = disc.Background;
                    Assert.Equal(ThemeChromeResources.CaptionButtonHoverShadow, disc.BoxShadow);
                    if (theme == "Light") Capture(host, $"caption-{name}-hover");
                    ((IPseudoClasses)button.Classes).Set(":pressed", true);
                    Tick(window);
                    Assert.NotEqual(hovered, disc.Background);
                    Assert.Equal(ThemeChromeResources.CaptionButtonPressedShadow, disc.BoxShadow);
                    if (theme == "Light") Capture(host, $"caption-{name}-pressed");
                    ((IPseudoClasses)button.Classes).Set(":pressed", false);
                    ((IPseudoClasses)button.Classes).Set(":pointerover", false);
                    Tick(window);
                    Assert.Equal(idle, disc.Background);
                    Assert.Equal(originalSize, button.Bounds.Size);
                    var presenter = Assert.Single(button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>());
                    Assert.Same(button.Content, presenter.Content);
                    var originalContent = button.Content;
                    var replacement = new TextBlock { Text = "updated" };
                    button.Content = replacement;
                    Tick(window);
                    Assert.Same(replacement, presenter.Content);
                    Assert.Contains(replacement, button.GetVisualDescendants());
                    var point = button.TranslatePoint(new Point(1, button.Bounds.Height / 2), host)!.Value;
                    var hit = Assert.IsAssignableFrom<Visual>(host.InputHitTest(point));
                    Assert.Contains(button, hit.GetVisualAncestors().Prepend(hit));
                    button.Content = originalContent;
                    Tick(window);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Capture(Control control, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_CAPTION_REPAIR_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)control.Bounds.Width * 3, (int)MainWindowTitleBar.CaptionHeight * 3), new Vector(288, 288));
        bitmap.Render(control);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void Tick(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    [Fact]
    public async Task CaptionTracksDocumentTitleAndThemeWithoutReplacingNativeWindowActions()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(MainWindowTitleBarTests));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Light");
            var window = new Window { Title = "Untitled optic", Width = 640, Height = 480 };
            var content = new Border();
            var root = Assert.IsType<DockPanel>(MainWindowTitleBar.Wrap(window, content));
            window.Content = root;
            window.Show();
            try
            {
                window.UpdateLayout();
                var caption = Assert.IsType<MainWindowTitleBar>(root.Children[0]);
                var title = Assert.Single(caption.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "MainWindowTitle");
                Assert.Same(content, root.Children[1]);
                Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                Assert.True(window.CanMinimize && window.CanMaximize && window.CanResize);
                Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(caption));
                Assert.False(title.IsHitTestVisible);
                Assert.Same(BlueThemeTokens.HeaderGradient, caption.Background);
                Assert.Equal(new Thickness(0), caption.BorderThickness);
                Assert.Equal(Color.Parse("#FFFFFF"), ((ISolidColorBrush)title.Foreground!).Color);
                if (OperatingSystem.IsWindows())
                {
                    Assert.Equal(Color.Parse("#FFBD2E"), MainWindowTitleBar.MinimizeButtonColor);
                    Assert.Equal(Color.Parse("#28C840"), MainWindowTitleBar.MaximizeButtonColor);
                    Assert.Equal(Color.Parse("#FF5F57"), MainWindowTitleBar.CloseButtonColor);
                    Assert.Equal(Color.Parse("#301C16"), MainWindowTitleBar.CaptionButtonGlyphColor);
                    Assert.Equal(22, MainWindowTitleBar.TrafficLightDiameter);
                    var decorationsTheme = Assert.IsType<ControlTheme>(window.WindowDecorationsTheme);
                    Assert.Equal(
                        MainWindowTitleBar.CaptionButtonGlyphColor,
                        Assert.IsAssignableFrom<ISolidColorBrush>(decorationsTheme.Resources["CaptionButtonForeground"]).Color);
                    foreach (var partName in new[] { "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton" })
                    {
                        var style = Assert.Single(
                            decorationsTheme.Children.OfType<Style>(),
                            candidate => candidate.Selector?.ToString().Contains(partName, StringComparison.Ordinal) == true
                                && candidate.Setters.Any(
                                    setter => setter is Setter concrete && concrete.Property == Button.TemplateProperty));
                        Assert.Contains(
                            style.Setters,
                            setter => setter is Setter concrete && concrete.Property == Button.TemplateProperty);
                    }
                }
                window.Title = "Cooke * - 顺序模式 - Optical System Design";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(window.Title, title.Text);
                foreach (var theme in new[] { "Dark", "Pixel", "Isekai", "Light", "System", "Light" })
                {
                    ThemeApplicationService.Apply(app, theme);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var expected = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();
                    Assert.Equal(expected, window.ExtendClientAreaToDecorationsHint);
                    Assert.Equal(expected, caption.IsVisible);
                    Assert.Equal(expected ? caption.Bounds.Height : -1, window.ExtendClientAreaTitleBarHeightHint);
                    Assert.Equal(expected ? caption.Bounds.Height : 0, content.Bounds.Y);
                }
                window.WindowState = WindowState.FullScreen;
                Dispatcher.UIThread.RunJobs();
                Assert.False(caption.IsVisible);
                window.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows(), caption.IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
