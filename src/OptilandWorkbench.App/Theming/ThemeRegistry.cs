using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Theming;

internal sealed record ThemeDefinition(
    string SettingsValue,
    string DisplayName,
    ThemeVariant RequestedVariant,
    ThemePalette? Palette,
    IThemeIconPack IconPack,
    FontFamily UiFontFamily,
    TextRenderingMode TextRenderingMode,
    ThemeChromeProfile Chrome,
    IThemeDecorationRenderer DecorationRenderer,
    Action<IResourceDictionary> AccentApplicator,
    bool IsDarkLike,
    bool FollowsSystem = false)
{
    public ResourceDictionary BuildResources()
    {
        if (Palette is null)
        {
            throw new InvalidOperationException($"主题 {SettingsValue} 跟随系统，不具有独立资源字典。");
        }

        var resources = Palette.ToResourceDictionary();
        Chrome.AddResources(resources);
        AccentApplicator(resources);

        return resources;
    }

    public ThemeDefinition ResolveVisual(ThemeVariant? actualVariant) =>
        FollowsSystem ? ThemeRegistry.FromActualVariant(actualVariant) : this;

    public override string ToString() => DisplayName;
}

internal static class ThemeRegistry
{
    private static readonly ThemeDefinition Light = new(
        "Light",
        "普通模式",
        ThemeVariant.Light,
        ThemePalette.Light,
        StandardThemeIconPack.Instance,
        FontFamily.Default,
        TextRenderingMode.Unspecified,
        ThemeChromeProfile.CreateStandard(ThemePalette.Light.Border),
        NoThemeDecorationRenderer.Instance,
        StandardTheme.ApplyAccentResources,
        IsDarkLike: false);

    private static readonly ThemeDefinition Dark = new(
        "Dark",
        "暗夜模式",
        ThemeVariant.Dark,
        ThemePalette.DarkOpticStudio,
        StandardThemeIconPack.Instance,
        FontFamily.Default,
        TextRenderingMode.Unspecified,
        ThemeChromeProfile.CreateStandard(ThemePalette.DarkOpticStudio.Border),
        NoThemeDecorationRenderer.Instance,
        StandardTheme.ApplyAccentResources,
        IsDarkLike: true);

    private static readonly ThemeDefinition Isekai = new(
        IsekaiTheme.SettingsValue,
        "异世界",
        IsekaiTheme.Variant,
        IsekaiTheme.Palette,
        IsekaiThemeIconPack.Instance,
        FontFamily.Default,
        TextRenderingMode.Unspecified,
        ThemeChromeProfile.CreateIsekai(),
        IsekaiThemeDecorationRenderer.Instance,
        IsekaiTheme.ApplyAccentResources,
        IsDarkLike: true);

    private static readonly ThemeDefinition Pixel = new(
        PixelTheme.SettingsValue,
        "像素风格",
        PixelTheme.Variant,
        PixelTheme.Palette,
        PixelThemeIconPack.Instance,
        new FontFamily("avares://OptilandWorkbench.App/Assets/Fonts#Fusion Pixel 10px Mono zh_hans"),
        TextRenderingMode.Alias,
        ThemeChromeProfile.CreatePixel(),
        PixelThemeDecorationRenderer.Instance,
        PixelTheme.ApplyAccentResources,
        IsDarkLike: false);

    private static readonly ThemeDefinition System = new(
        "System",
        "跟随系统",
        ThemeVariant.Default,
        null,
        StandardThemeIconPack.Instance,
        FontFamily.Default,
        TextRenderingMode.Unspecified,
        ThemeChromeProfile.CreateStandard(ThemePalette.Light.Border),
        NoThemeDecorationRenderer.Instance,
        StandardTheme.ApplyAccentResources,
        IsDarkLike: false,
        FollowsSystem: true);

    public static IReadOnlyList<ThemeDefinition> SelectableThemes { get; } =
        new[] { Light, Dark, Isekai, Pixel, System };

    public static IReadOnlyList<ThemeDefinition> ConcreteThemes { get; } =
        new[] { Light, Dark, Isekai, Pixel };

    public static bool IsSupportedSettingsValue(string? value) =>
        SelectableThemes.Any(theme => string.Equals(
            theme.SettingsValue,
            value,
            StringComparison.Ordinal));

    public static string NormalizeSettingsValue(string? value) =>
        IsSupportedSettingsValue(value) ? value! : Light.SettingsValue;

    public static ThemeDefinition FromSettings(string? value) =>
        SelectableThemes.FirstOrDefault(theme => string.Equals(
            theme.SettingsValue,
            value,
            StringComparison.Ordinal)) ?? Light;

    public static ThemeDefinition FromActualVariant(ThemeVariant? variant)
    {
        var registered = ConcreteThemes.FirstOrDefault(theme => theme.RequestedVariant == variant);
        if (registered is not null)
        {
            return registered;
        }

        return variant == ThemeVariant.Dark ? Dark : Light;
    }

    public static bool IsDarkVisual(ThemeVariant? variant) =>
        FromActualVariant(variant).IsDarkLike;
}

internal static class StandardTheme
{
    public static Color AccentColor { get; } = Color.FromRgb(0, 122, 255);

    public static IReadOnlyList<string> UnifiedDockAccentResourceKeys { get; } =
        new[]
        {
            "DockSurfaceHeaderActiveBrush",
            "DockTabActiveBackgroundBrush",
            "DockTabActiveIndicatorBrush",
            "DockTargetIndicatorBrush",
            "DockSplitterDragBrush"
        };

    public static void ApplyAccentResources(IResourceDictionary resources)
    {
        // Dock binds this to the tab body in every state, not just on pointer-over.
        resources["DockDocumentTabItemCornerRadius"] = ThemeChromeResources.StandardDocumentTabCornerRadius;
        var accentBrush = new SolidColorBrush(AccentColor);
        resources["SystemAccentColor"] = AccentColor;
        resources["SystemAccentColorDark1"] = Color.FromRgb(0, 102, 204);
        resources["SystemAccentColorDark2"] = Color.FromRgb(0, 82, 164);
        resources["SystemAccentColorDark3"] = Color.FromRgb(0, 64, 128);
        resources["SystemAccentColorLight1"] = Color.FromRgb(64, 156, 255);
        resources["SystemAccentColorLight2"] = Color.FromRgb(128, 190, 255);
        resources["SystemAccentColorLight3"] = Color.FromRgb(204, 229, 255);
        resources["AccentFillColorDefaultBrush"] = accentBrush;
        resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 112, 235));
        resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 102, 214));
        resources[ThemeResourceBindings.SelectionBackground] = accentBrush;
        resources[ThemeResourceBindings.SelectionForeground] =
            new SolidColorBrush(Color.FromRgb(255, 255, 255));
        foreach (var resourceKey in UnifiedDockAccentResourceKeys)
        {
            resources[resourceKey] = accentBrush;
        }

        var selectedForeground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        resources["DockTabActiveForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabSelectedForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabCloseSelectedForegroundBrush"] = selectedForeground;
    }
}
