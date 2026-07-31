using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

public sealed class ThemeResourceTests
{
    [Fact]
    public void DarkAndIsekaiThemesDefineAnalysisAndSceneRenderingResources()
    {
        var resources = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        var isekaiResources = IsekaiTheme.CreateResourceDictionary();
        var settings = new AppSettings { Theme = IsekaiTheme.SettingsValue };
        settings.NormalizeDisplaySettings();

        Assert.Equal(IsekaiTheme.SettingsValue, settings.Theme);

        AssertBrush(resources, ThemeResourceBindings.TextPrimary);
        AssertBrush(resources, ThemeResourceBindings.TextSecondary);
        AssertBrush(resources, ThemeResourceBindings.TextMuted);
        AssertBrush(resources, ThemeResourceBindings.TextDisabled);
        AssertBrush(resources, ThemeResourceBindings.TextAccent);
        AssertBrush(resources, ThemeResourceBindings.TextOnAccent);
        AssertBrush(resources, ThemeResourceBindings.TextWarning);
        AssertBrush(resources, ThemeResourceBindings.TextError);
        AssertBrush(resources, ThemeResourceBindings.TextSuccess);
        AssertBrush(resources, ThemeResourceBindings.PlotBackground);
        AssertBrush(resources, ThemeResourceBindings.PlotText);
        AssertBrush(resources, ThemeResourceBindings.PlotGrid);
        AssertBrush(resources, ThemeResourceBindings.SceneBackground);
        AssertBrush(resources, ThemeResourceBindings.SceneLensFill);
        AssertBrush(resources, ThemeResourceBindings.SceneSurface);
        AssertBrush(resources, ThemeResourceBindings.SceneOrientationFill);
        AssertBrush(isekaiResources, ThemeResourceBindings.TextPrimary);
        AssertBrush(isekaiResources, ThemeResourceBindings.PlotBackground);
        AssertBrush(isekaiResources, ThemeResourceBindings.SceneBackground);
        AssertBrush(isekaiResources, "AccentFillColorDefaultBrush");
        var applicationResources = new ResourceDictionary
        {
            ["AccentFillColorDefaultBrush"] = new SolidColorBrush(Color.FromRgb(0, 122, 255))
        };
        IsekaiTheme.ApplyAccentResources(applicationResources);
        Assert.Equal(
            Color.FromRgb(202, 148, 50),
            ColorOf(applicationResources, "AccentFillColorDefaultBrush"));
        Assert.True(IsekaiTheme.IsDarkLike(IsekaiTheme.Variant));
        Assert.False(IsekaiTheme.IsDarkLike(ThemeVariant.Light));
    }

    [Fact]
    public void DarkThemeKeepsRenderingPaletteDarkerThanLightTheme()
    {
        var light = ThemePalette.Light.ToResourceDictionary();
        var dark = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        var isekai = IsekaiTheme.CreateResourceDictionary();

        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.PlotBackground))
            < Luminance(ColorOf(light, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.SceneBackground))
            < Luminance(ColorOf(light, ThemeResourceBindings.SceneBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.PlotText))
            > Luminance(ColorOf(dark, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextPrimary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextSecondary)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextSecondary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextMuted)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextMuted))
            > Luminance(ColorOf(dark, ThemeResourceBindings.TextDisabled)));
        Assert.True(Luminance(ColorOf(dark, ThemeResourceBindings.TextPrimary))
            > Luminance(ColorOf(dark, ThemeResourceBindings.Surface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.PlotText))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.PlotBackground)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextAccent))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.Surface)));
    }

    [Fact]
    public void IsekaiRibbonChromeIsAnInputTransparentThemeDecoration()
    {
        var chrome = new IsekaiRibbonChrome();

        Assert.False(chrome.IsHitTestVisible);
        Assert.True(chrome.ClipToBounds);
    }
    private static void AssertBrush(ResourceDictionary resources, string key)
    {
        Assert.True(resources.ContainsKey(key));
        Assert.IsAssignableFrom<IBrush>(resources[key]);
    }

    private static Color ColorOf(ResourceDictionary resources, string key)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resources[key]);
        return brush.Color;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
}
