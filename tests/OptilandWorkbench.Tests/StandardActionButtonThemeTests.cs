using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class StandardActionButtonThemeTests
{
    private const string CaptureDirectory = "OPTILAND_ACTION_BUTTON_CAPTURE_DIR";

    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task AddFieldUsesBlueStatesAndStillAddsExactlyOneField()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(StandardActionButtonThemeTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = 500, Height = 850, Content = panel };
            try
            {
                window.Show();
                Render(window);
                var button = panel.GetVisualDescendants().OfType<Button>().Single(control =>
                    control.Content is LocalIconLabel label && label.Children.OfType<TextBlock>()
                        .Any(text => text.Text == "添加视场"));
                button.BringIntoView();
                Render(window);
                var bounds = button.Bounds;
                var point = button.TranslatePoint(new Point(20, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(new Point(499, 849));
                Render(window);
                Assert.Equal(StandardActionButtonStyles.Normal, Background(button));
                Capture(window, "normal");
                window.MouseMove(point);
                Render(window);
                Assert.True(button.IsPointerOver);
                Assert.Equal(StandardActionButtonStyles.Hover, Background(button));
                Capture(window, "hover");
                var count = app.Prescription.GetFields().Count;
                window.MouseDown(point, MouseButton.Left);
                Render(window);
                Assert.Equal(StandardActionButtonStyles.Pressed, Background(button));
                Assert.Equal(count, app.Prescription.GetFields().Count);
                Capture(window, "pressed");
                window.MouseUp(point, MouseButton.Left);
                Render(window);
                Assert.Equal(count + 1, app.Prescription.GetFields().Count);
                Assert.Equal(StandardActionButtonStyles.Hover, Background(button));
                window.MouseMove(new Point(499, 849));
                Render(window);
                Assert.Equal(StandardActionButtonStyles.Normal, Background(button));
                Assert.Equal(bounds.Size, button.Bounds.Size);
                button.IsEnabled = false;
                window.MouseMove(point);
                Render(window);
                Assert.NotEqual(StandardActionButtonStyles.Normal, Background(button));
                Assert.NotEqual(StandardActionButtonStyles.Hover, Background(button));
                Assert.NotEqual(StandardActionButtonStyles.Pressed, Background(button));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LateCreatedDialogButtonsUseSameRuleWithoutOverridingExplicitSurfaces()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(StandardActionButtonThemeTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
            var window = new Window { Width = 300, Height = 250, Content = content };
            try
            {
                window.Show();
                foreach (var text in new[] { "应用", "确定", "保存" })
                {
                    var button = new Button { Content = text };
                    content.Children.Add(button);
                    Render(window);
                    Assert.Equal(StandardActionButtonStyles.Normal, Background(button));
                }
                var transparent = new Button { Content = "图标", Background = Brushes.Transparent };
                var settings = SettingsPanelChrome.CreateToggleButton();
                content.Children.Add(transparent);
                content.Children.Add(settings);
                Render(window);
                Assert.Equal(Colors.Transparent, Background(transparent));
                Assert.Equal(Colors.White, Background(settings));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Isekai")]
    [InlineData("Pixel")]
    public async Task OtherThemesKeepNativeButtonStatesAcrossRoundTrips(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(StandardActionButtonThemeTests));
        await session.Dispatch(() =>
        {
            var app = global::Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, theme);
            var button = new Button { Content = "应用" };
            var window = new Window { Width = 240, Height = 120, Content = new StackPanel { Children = { button } } };
            try
            {
                window.Show();
                Render(window);
                foreach (var selection in new[] { theme, "Light", theme })
                {
                    ThemeApplicationService.Apply(app, selection);
                    window.MouseMove(new Point(230, 110));
                    Render(window);
                    if (selection == "Light")
                    {
                        Assert.Equal(StandardActionButtonStyles.Normal, Background(button));
                        continue;
                    }
                    AssertNative("ButtonBackground");
                    var point = button.TranslatePoint(new Point(20, 12), window)!.Value;
                    window.MouseMove(point);
                    Render(window);
                    AssertNative("ButtonBackgroundPointerOver");
                    window.MouseDown(point, MouseButton.Left);
                    Render(window);
                    AssertNative("ButtonBackgroundPressed");
                    window.MouseUp(point, MouseButton.Left);
                    button.IsEnabled = false;
                    Render(window);
                    AssertNative("ButtonBackgroundDisabled");
                    button.IsEnabled = true;
                }

                void AssertNative(string key)
                {
                    Assert.True(button.TryFindResource(key, button.ActualThemeVariant, out var value));
                    Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(value).Color, Background(button));
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static Color Background(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(button
        .GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter")
        .Background).Color;

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"button-{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
