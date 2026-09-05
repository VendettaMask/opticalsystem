using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SystemPropertiesPanelSectionThemeTests
{
    // Opt-in captures run in a separate test process: Avalonia's text caches cannot mix fake and Skia glyphs.
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_SYSTEM_SECTIONS_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture)
        {
            builder.UseSkia();
        }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task SectionsKeepTheirCardOutlineAcrossPointerExpansionAndThemeChanges()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(SystemPropertiesPanelSectionThemeTests));
        await session.Dispatch(() =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            using var application = WorkbenchApplication.Create("cooke");
            using var panel = new SystemPropertiesPanel(application.Prescription, application.Materials, application.Events);
            var window = new Window { Width = 380, Height = 700, Content = panel };
            try
            {
                ThemeApplicationService.Apply(app, "Light");
                window.Show();
                var sections = Assert.IsType<StackPanel>(Assert.IsType<ScrollViewer>(panel.Content).Content);
                Assert.Equal(6, sections.Children.Count);
                Assert.Equal(8, sections.Spacing);
                Assert.Equal(new Thickness(8), sections.Margin);
                var cards = sections.Children.Cast<Border>().ToArray();
                // Preserve the product's initial expanded sections; only collapse them for the screenshots.
                Assert.True(Body(cards[0]).IsVisible);
                Assert.True(Body(cards[1]).IsVisible);
                Assert.All(cards.Skip(2), card => Assert.False(Body(card).IsVisible));
                foreach (var card in cards.Where(card => Body(card).IsVisible))
                {
                    Click(Header(card));
                }

                var themeIndex = 0;
                foreach (var theme in new[] { "Light", "Dark", IsekaiTheme.SettingsValue, PixelTheme.SettingsValue, "Light" })
                {
                    var capturePrefix = themeIndex++ == 4 ? "light-after-switch" : theme.ToLowerInvariant();
                    ThemeApplicationService.Apply(app, theme);
                    window.MouseMove(new Point(375, 690));
                    RenderFrame(window);
                    var expected = ThemeRegistry.FromSettings(theme).Chrome[ThemeChromeRole.SurfaceCard];
                    foreach (var card in cards)
                    {
                        Assert.Contains("system-property-card", card.Classes);
                        Assert.True(card.ClipToBounds);
                        Assert.Equal(expected.CornerRadius, card.CornerRadius);
                        Assert.Equal(new Thickness(1), card.BorderThickness);
                        Assert.Equal(default, card.BoxShadow);
                        Assert.Equal(expected.CornerRadius, Header(card).CornerRadius);
                        Assert.Equal(new Thickness(0), Header(card).Margin);
                        Assert.Equal(new Thickness(0), Header(card).BorderThickness);
                        Assert.DoesNotContain("theme-emphasized", card.Classes);
                        Assert.True(card.Bounds.Width <= 364);
                    }

                    var target = cards[1];
                    var header = Header(target);
                    var collapsedBounds = target.Bounds;
                    var normalBorder = Assert.IsAssignableFrom<ISolidColorBrush>(target.BorderBrush).Color;
                    CaptureOptional(window, $"{capturePrefix}-collapsed");
                    var point = header.TranslatePoint(new Point(50, header.Bounds.Height / 2), window);
                    Assert.NotNull(point);
                    window.MouseMove(point.Value);
                    RenderFrame(window);
                    Assert.True(header.IsPointerOver);
                    Assert.Contains("theme-emphasized", target.Classes);
                    Assert.Equal(collapsedBounds, target.Bounds);
                    Assert.Equal(expected.CornerRadius, target.CornerRadius);
                    CaptureOptional(window, $"{capturePrefix}-hover");

                    window.MouseMove(new Point(375, 690));
                    RenderFrame(window);
                    Assert.DoesNotContain("theme-emphasized", target.Classes);
                    Assert.Equal(normalBorder, Assert.IsAssignableFrom<ISolidColorBrush>(target.BorderBrush).Color);
                    Click(header);
                    Assert.True(Body(target).IsVisible);
                    Assert.Contains("theme-emphasized", target.Classes);
                    Assert.Equal(collapsedBounds.Width, target.Bounds.Width);
                    Assert.True(target.Bounds.Height > collapsedBounds.Height);
                    Assert.Equal(expected.CornerRadius, target.CornerRadius);
                    CaptureOptional(window, $"{capturePrefix}-expanded");
                    Click(header);
                    Assert.False(Body(target).IsVisible);
                    Assert.Equal(collapsedBounds, target.Bounds);
                    Assert.DoesNotContain("theme-emphasized", target.Classes);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Button Header(Border card) =>
        Assert.IsType<Button>(Assert.IsType<StackPanel>(card.Child).Children[0]);

    private static Border Body(Border card) =>
        Assert.IsType<Border>(Assert.IsType<StackPanel>(card.Child).Children[1]);

    private static void Click(Button header)
    {
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RenderFrame(header);
    }

    private static void RenderFrame(Control control)
    {
        control.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void CaptureOptional(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_SYSTEM_SECTIONS_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        Assert.True(new FileInfo(Path.Combine(directory, $"{name}.png")).Length > 0);
    }
}
