using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BlackHoleDarkThemeTests
{
    [Fact]
    public void WarmChromeHasReadableInkAndRetainsPhysicalAndMaterialColourRoles()
    {
        var theme = ThemeRegistry.FromSettings("Dark");
        Assert.Equal("Dark", theme.SettingsValue);
        Assert.IsType<ScientificThemeIconPack>(theme.IconPack);
        Assert.Equal(BlackHoleDarkTheme.Accent, theme.IconPalette.Signal);
        Assert.Same(NoThemeDecorationRenderer.Instance, theme.DecorationRenderer);
        var palette = Assert.IsType<ThemePalette>(theme.Palette);
        Assert.True(palette.Workspace.R < 16 && palette.Workspace.G < 16 && palette.Workspace.B < 16);
        Assert.True(BlackHoleDarkTheme.Accent.R > BlackHoleDarkTheme.Accent.G
            && BlackHoleDarkTheme.Accent.G > BlackHoleDarkTheme.Accent.B);
        foreach (var ink in new[] { palette.TextPrimary, palette.TextSecondary, palette.TextMuted })
            Assert.True(Contrast(ink, palette.Surface) >= 4.5);
        Assert.True(Contrast(palette.TextOnAccent, BlackHoleDarkTheme.Accent) >= 4.5);
        Assert.True(Contrast(palette.TextPrimary, BlackHoleDarkTheme.Selected) >= 4.5);
        Assert.Equal(ThemePalette.DarkOpticStudio.RibbonTabHover, palette.RibbonTabHover);
        Assert.Equal(ThemePalette.DarkOpticStudio.SceneLensFill, palette.SceneLensFill);
        Assert.Equal(ThemePalette.DarkOpticStudio.SceneStop, palette.SceneStop);
        Assert.Equal(ThemePalette.DarkOpticStudio.SceneSurface, palette.SceneSurface);
        Assert.Equal(ThemePalette.DarkOpticStudio.PlotBar, palette.PlotBar);
        var resources = theme.BuildResources();
        Assert.Equal(BlackHoleDarkTheme.Accent, Colour(resources["AccentButtonBackground"]));
        Assert.NotEqual(BlackHoleDarkTheme.Accent, Colour(resources["ButtonBackground"]));
    }

    [Theory]
    [InlineData("zh-CN", 13)]
    [InlineData("en-US", 18)]
    [InlineData("ja-JP", 13)]
    public async Task NativeControlsKeepWarmStatesAndRecoverAfterOtherThemes(string language, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            UiLocalization.Apply(language);
            DisplayTypography.Configure(new AppSettings { Theme = "Dark", FontSize = fontSize });
            ThemeApplicationService.Apply(app, "Dark");
            var input = new TextBox { Text = "125.5 / ∞", Name = "PreviewNumericInput" };
            var primary = new Button { Content = "开始计算", Classes = { "accent" } };
            var neutral = new Button { Content = "设置" };
            var choices = new ListBox { ItemsSource = new[] { "Field 1", "Field 2" }, SelectedIndex = 0 };
            var root = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Optical System Design", FontSize = DisplayTypography.PageTitle },
                    input, new ComboBox { ItemsSource = new[] { "FFT PSF", "MTF", "Wavefront" }, SelectedIndex = 0 },
                    new CheckBox { Content = "自动更新", IsChecked = true }, choices,
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12,
                        Children = { primary, neutral, new Button { Content = "不可用", IsEnabled = false } } }
                }
            };
            root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Surface);
            var window = new Window { Width = 680, Height = 540, Content = root };
            try
            {
                window.Show(); Tick(window);
                foreach (var theme in new[] { "Dark", "Light", "Dark", "Lumen", "Dark", "Pixel", "Dark" })
                {
                    ThemeApplicationService.Apply(app, theme); Tick(window);
                    if (theme != "Dark") continue;
                    Assert.Equal(BlackHoleDarkTheme.ControlBackground, Colour(input.Background));
                    Assert.Equal(BlackHoleDarkTheme.Accent, Colour(primary.Background));
                    Assert.NotEqual(Colour(primary.Background), Colour(neutral.Background));
                    var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>()
                        .Single(control => control.Name == "PART_ContentPresenter");
                    Assert.True(Contrast(Colour(presenter.Foreground), Colour(presenter.Background)) >= 4.5);
                    var point = primary.TranslatePoint(new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point); Tick(window);
                    Assert.Equal(BlackHoleDarkTheme.AccentHover, Colour(presenter.Background));
                    window.MouseDown(point, MouseButton.Left); Tick(window);
                    Assert.Equal(BlackHoleDarkTheme.AccentPressed, Colour(presenter.Background));
                    window.MouseUp(point, MouseButton.Left);
                    window.MouseMove(new Point(1, 1)); Tick(window);
                    primary.IsEnabled = false; Tick(window);
                    Assert.Equal(BlackHoleDarkTheme.Palette.SettingsSurface, Colour(presenter.Background));
                    Assert.Equal(BlackHoleDarkTheme.Palette.TextDisabled, Colour(presenter.Foreground));
                    primary.IsEnabled = true; Tick(window);
                    Assert.Equal("125.5 / ∞", input.Text);
                    Assert.Equal(0, choices.SelectedIndex);
                }
                Capture(window, $"controls-{language}-{fontSize}.png");
            }
            finally
            {
                window.Close(); UiLocalization.Apply(UiLocalization.Chinese);
                DisplayTypography.Configure(new AppSettings());
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(1500, 950, 13)]
    [InlineData(1120, 800, 18)]
    public async Task ActualWorkbenchCanSwitchThemesWithoutChangingTheLens(double width, double height, double fontSize)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(async () =>
        {
            using var workbench = WorkbenchApplication.Create();
            await workbench.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var original = workbench.Documents.GetRecoveryState();
            var settings = new AppSettings { Theme = "Dark", FontSize = fontSize, WindowWidth = width, WindowHeight = height };
            var store = new WorkspaceSessionStore(Path.Combine(Path.GetTempPath(), "black-hole-theme", Guid.NewGuid().ToString("N")));
            var window = new MainWindow(settings, workbench, store);
            var ready = false;
            window.StartupCompleted += (_, _) => ready = true;
            try
            {
                window.Show();
                for (var attempt = 0; attempt < 200 && !ready; attempt++) { await Task.Delay(10); Tick(window); }
                Assert.True(ready);
                Tick(window);
                Assert.NotEmpty(window.GetVisualDescendants().OfType<LensEditorPanel>());
                var glassRows = window.GetVisualDescendants().OfType<DataGridRow>()
                    .Where(row => row.Classes.Contains("glass-material-row")).ToArray();
                Assert.NotEmpty(glassRows);
                foreach (var row in glassRows)
                {
                    var fill = Colour(row.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>()
                        .Single(rectangle => rectangle.Name == "BackgroundRectangle").Fill);
                    Assert.True(fill.B > fill.R);
                    Assert.True(Contrast(BlackHoleDarkTheme.Palette.TextPrimary, fill) >= 4.5);
                }
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
                Capture(window, $"workbench-{width}x{height}-{fontSize}.png");
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Light"); Tick(window);
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
                ThemeApplicationService.Apply(Avalonia.Application.Current!, "Dark"); Tick(window);
                Assert.Equal(original, workbench.Documents.GetRecoveryState());
                Assert.Equal(BlackHoleDarkTheme.Accent, Colour(Avalonia.Application.Current!.Resources["AccentFillColorDefaultBrush"]));
            }
            finally { window.Close(); DisplayTypography.Configure(new AppSettings()); }
        }, CancellationToken.None);
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_BLACK_HOLE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
    private static void Tick(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
    private static Color Colour(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var first = L(a); var second = L(b);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }
}
