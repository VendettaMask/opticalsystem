using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class LumenThemeTests
{
    [Fact]
    public void PackageIsRegisteredCompleteReadableAndDoesNotChangePhysicalColourRoles()
    {
        var theme = ThemeRegistry.FromSettings("Lumen");
        Assert.Equal("光域", theme.DisplayName);
        Assert.Equal(LumenTheme.Variant, theme.RequestedVariant);
        Assert.True(theme.IsDarkLike);
        Assert.Same(StandardThemeIconPack.Instance, theme.IconPack);
        Assert.Same(NoThemeDecorationRenderer.Instance, theme.DecorationRenderer);
        var settings = new AppSettings { Theme = "Lumen" }; settings.NormalizeDisplaySettings();
        Assert.Equal("Lumen", settings.Theme);
        Assert.Equal("Light", AppSettings.DefaultTheme);
        var resources = theme.BuildResources();
        foreach (var role in Enum.GetValues<ThemeChromeRole>())
        {
            Assert.Equal(new CornerRadius(0), theme.Chrome[role].CornerRadius);
            Assert.Equal(default, theme.Chrome[role].BoxShadow);
            Assert.Equal(ThemeRegistry.FromSettings("Dark").Chrome[role].BorderThickness, theme.Chrome[role].BorderThickness);
        }
        foreach (var ink in new[] { LumenTheme.Palette.TextPrimary, LumenTheme.Palette.TextSecondary, LumenTheme.Palette.TextMuted })
            Assert.True(Contrast(ink, LumenTheme.Palette.Surface) >= 4.5);
        Assert.True(Contrast(LumenTheme.Palette.TextOnAccent, LumenTheme.Accent) >= 4.5);
        Assert.True(Contrast(LumenTheme.Palette.TextPrimary, LumenTheme.MaterialRow) >= 4.5);
        Assert.Equal(ThemePalette.DarkOpticStudio.SceneStop, LumenTheme.Palette.SceneStop);
        Assert.Equal(ThemePalette.DarkOpticStudio.SceneSurface, LumenTheme.Palette.SceneSurface);
        Assert.Equal(ThemePalette.DarkOpticStudio.PlotBar, LumenTheme.Palette.PlotBar);
        Assert.Equal(LumenTheme.Accent, Colour(resources["AccentButtonBackground"]));
        Assert.NotEqual(LumenTheme.Accent, Colour(resources["ButtonBackground"]));
    }

    [Fact]
    public async Task SelectedListUsesQuietSlateRatherThanTheExecutionAccent()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, "Lumen");
            var list = new ListBox { ItemsSource = new[] { "当前方案", "参考方案" }, SelectedIndex = 0 };
            var window = new Window { Width = 380, Height = 230, Content = list };
            try
            {
                window.Show(); Tick(window);
                var selected = list.GetVisualDescendants().OfType<ListBoxItem>().Single(item => item.IsSelected);
                var presenter = selected.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                Assert.Equal(Color.Parse("#233D48"), Colour(presenter.Background));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LiveControlsAndMaterialRowsUseThemeAndSwitchBackWithoutLeakingResources()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OpticalSystemDesign.App.App));
        await session.Dispatch(() =>
        {
            var app = Avalonia.Application.Current!;
            ThemeApplicationService.Apply(app, "Lumen");
            using var workbench = WorkbenchApplication.Create();
            for (var i = 0; i < 4; i++) workbench.Prescription.AddSurface();
            var surfaces = workbench.Prescription.GetSurfaces();
            foreach (var i in new[] { 1, 3 }) workbench.Prescription.UpdateSurface(surfaces[i] with { Material = "N-BK7" });
            using var editor = new LensEditorPanel(workbench.Prescription, workbench.Events, new SurfaceSelectionService());
            var primary = new Button { Content = "执行" }; primary.Classes.Add("accent");
            var neutral = new Button { Content = "取消" };
            var input = new TextBox { Text = "12.5" };
            var window = new Window
            {
                Width = 1300,
                Height = 550,
                Content = new StackPanel { Children = { input, primary, neutral, editor } }
            };
            try
            {
                window.Show(); Tick(window);
                Assert.Equal(LumenTheme.Variant, input.ActualThemeVariant);
                Assert.Equal(new CornerRadius(0), input.CornerRadius);
                Assert.Equal(Color.Parse("#101F26"), Colour(input.Background));
                Assert.Equal(LumenTheme.Accent, Colour(primary.Background));
                Assert.Equal(LumenTheme.Palette.TextOnAccent, Colour(primary.Foreground));
                Assert.NotEqual(Colour(primary.Background), Colour(neutral.Background));
                var table = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                table.SelectedIndex = 0; Tick(window);
                var row = table.GetVisualDescendants().OfType<DataGridRow>().First(r => r.Classes.Contains("glass-material-row"));
                Rectangle Fill() => row.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "BackgroundRectangle" && !r.GetVisualAncestors().OfType<DataGridRowHeader>().Any());
                Assert.Equal(LumenTheme.MaterialRow, Colour(Fill().Fill));
                ((IPseudoClasses)row.Classes).Set(":pointerover", true); Tick(window);
                Assert.Equal(LumenTheme.MaterialHover, Colour(Fill().Fill));
                ((IPseudoClasses)row.Classes).Set(":pointerover", false);
                table.SelectedItem = row.DataContext; Tick(window);
                Assert.Equal(LumenTheme.MaterialSelected, Colour(Fill().Fill));
                Assert.Equal(1, Fill().Opacity);
                ((IPseudoClasses)primary.Classes).Set(":pointerover", true); Tick(window);
                var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                Assert.Equal(Color.Parse("#E3BE87"), Colour(presenter.Background));
                ((IPseudoClasses)primary.Classes).Set(":pointerover", false);
                primary.IsEnabled = false; Tick(window);
                Assert.Equal(LumenTheme.Palette.TextDisabled, Colour(presenter.Foreground));
                primary.IsEnabled = true;
                ThemeApplicationService.Apply(app, "Dark"); Tick(window);
                Assert.False(app.Resources.ContainsKey("TextControlBackground"));
                Assert.Equal(new CornerRadius(5), input.CornerRadius);
                ThemeApplicationService.Apply(app, "Light"); Tick(window);
                Assert.Equal(Colors.White, Colour(input.Background));
                ThemeApplicationService.Apply(app, "Lumen"); Tick(window);
                Assert.Equal(LumenTheme.MaterialSelected, Colour(Fill().Fill));
                Assert.Equal(new CornerRadius(0), input.CornerRadius);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Tick(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static Color Colour(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Linear(byte b) { var v = b / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var first = L(a); var second = L(b); return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
