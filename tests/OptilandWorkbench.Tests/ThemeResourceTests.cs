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
        var lightResources = ThemePalette.Light.ToResourceDictionary();
        var resources = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        var isekaiResources = IsekaiTheme.CreateResourceDictionary();
        var settings = new AppSettings { Theme = IsekaiTheme.SettingsValue };
        settings.NormalizeDisplaySettings();

        Assert.Equal(IsekaiTheme.SettingsValue, settings.Theme);
        AssertAllThemeBrushes(lightResources);
        AssertAllThemeBrushes(resources);
        AssertAllThemeBrushes(isekaiResources);

        AssertBrush(resources, ThemeResourceBindings.TextPrimary);
        AssertBrush(resources, ThemeResourceBindings.TextSecondary);
        AssertBrush(resources, ThemeResourceBindings.TextMuted);
        AssertBrush(resources, ThemeResourceBindings.TextDisabled);
        AssertBrush(resources, ThemeResourceBindings.TextAccent);
        AssertBrush(resources, ThemeResourceBindings.TextOnAccent);
        AssertBrush(resources, ThemeResourceBindings.TextWarning);
        AssertBrush(resources, ThemeResourceBindings.TextError);
        AssertBrush(resources, ThemeResourceBindings.TextSuccess);
        AssertBrush(resources, ThemeResourceBindings.WarningSurface);
        AssertBrush(resources, ThemeResourceBindings.ErrorSurface);
        AssertBrush(resources, ThemeResourceBindings.SuccessSurface);
        AssertBrush(resources, ThemeResourceBindings.SettingsSurface);
        AssertBrush(resources, ThemeResourceBindings.SettingsOverlaySurface);
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
        Assert.Equal(
            Color.FromRgb(255, 255, 255),
            ColorOf(lightResources, ThemeResourceBindings.SettingsSurface));
        Assert.Equal((byte)255, ColorOf(lightResources, ThemeResourceBindings.SettingsSurface).A);
        Assert.True(ColorOf(lightResources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
        Assert.True(ColorOf(resources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
        Assert.True(ColorOf(isekaiResources, ThemeResourceBindings.SettingsOverlaySurface).A < 255);
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
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextWarning))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.WarningSurface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextError))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.ErrorSurface)));
        Assert.True(Luminance(ColorOf(isekai, ThemeResourceBindings.TextSuccess))
            > Luminance(ColorOf(isekai, ThemeResourceBindings.SuccessSurface)));
        AssertReadable(light, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(light, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(dark, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(dark, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);
        AssertReadable(isekai, ThemeResourceBindings.AnalysisRealRayRowForeground, ThemeResourceBindings.AnalysisRealRayRowBackground);
        AssertReadable(isekai, ThemeResourceBindings.AnalysisParaxialRayRowForeground, ThemeResourceBindings.AnalysisParaxialRayRowBackground);

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

    private static void AssertAllThemeBrushes(ResourceDictionary resources)
    {
        foreach (var field in typeof(ThemeResourceBindings).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }

            AssertBrush(resources, Assert.IsType<string>(field.GetRawConstantValue()));
        }
    }

    private static Color ColorOf(ResourceDictionary resources, string key)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(resources[key]);
        return brush.Color;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static void AssertReadable(ResourceDictionary resources, string foregroundKey, string backgroundKey)
    {
        Assert.True(
            ContrastRatio(ColorOf(resources, foregroundKey), ColorOf(resources, backgroundKey)) >= 4.5,
            $"{foregroundKey} must be readable on {backgroundKey}");
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var first = RelativeLuminance(foreground) + 0.05;
        var second = RelativeLuminance(background) + 0.05;
        return Math.Max(first, second) / Math.Min(first, second);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }
}
