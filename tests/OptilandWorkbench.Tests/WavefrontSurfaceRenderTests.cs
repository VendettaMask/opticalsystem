using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class WavefrontSurfaceRenderTests
{
    [Fact]
    public async Task SettingsToggleUsesConfiguredSurfaceInLightTheme()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var button = SettingsPanelChrome.CreateToggleButton();
            var window = new Window
            {
                Width = 160,
                Height = 80,
                Content = button,
                RequestedThemeVariant = ThemeVariant.Light
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                var background = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background);
                var lightResources = ThemePalette.Light.ToResourceDictionary();
                var configuredSurface = Assert.IsAssignableFrom<ISolidColorBrush>(
                    lightResources[ThemeResourceBindings.SettingsSurface]);
                Assert.Equal(configuredSurface.Color, background.Color);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InitialViewDoesNotInvalidateTheActiveRenderPass()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApplication));
        await session.Dispatch(() =>
        {
            var surface = new WavefrontSurfaceControl
            {
                Series = new AnalysisSeriesDto(
                    "Pupil X",
                    "Pupil Y",
                    new[]
                    {
                        new AnalysisPointDto(-1, -1, Value: 0.1),
                        new AnalysisPointDto(1, -1, Value: 0.2),
                        new AnalysisPointDto(-1, 1, Value: 0.3),
                        new AnalysisPointDto(1, 1, Value: 0.4)
                    },
                    AnalysisSeriesKind.Heatmap),
                DisplayAs = "表面"
            };
            var window = new Window
            {
                Width = 520,
                Height = 420,
                Content = surface
            };

            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();

                Assert.Equal(35, surface.ViewYawDegrees, precision: 10);
                Assert.Equal(28, surface.ViewPitchDegrees, precision: 10);
                Assert.Equal(1, surface.ViewZoom, precision: 10);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessAvaloniaCollection
{
    public const string Name = "Headless Avalonia";
}

public sealed class HeadlessTestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            Resources.ThemeDictionaries[theme.RequestedVariant] = theme.BuildResources();
        }

        ThemeApplicationService.Apply(this, "Light");
    }
}
