using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OptilandWorkbench.App.Theming;

internal static class IsekaiTheme
{
    public const string SettingsValue = "Isekai";

    public static ThemeVariant Variant { get; } = new(SettingsValue, ThemeVariant.Dark);

    public static ThemePalette Palette { get; } = new(
        Surface: Color.FromRgb(40, 31, 22),
        SubtleSurface: Color.FromRgb(55, 43, 29),
        Workspace: Color.FromRgb(20, 16, 13),
        Border: Color.FromRgb(116, 86, 43),
        TextPrimary: Color.FromRgb(226, 205, 158),
        TextSecondary: Color.FromRgb(190, 161, 109),
        TextMuted: Color.FromRgb(145, 119, 78),
        TextDisabled: Color.FromRgb(91, 74, 52),
        TextAccent: Color.FromRgb(225, 171, 68),
        TextOnAccent: Color.FromRgb(35, 24, 14),
        TextWarning: Color.FromRgb(242, 183, 63),
        TextError: Color.FromRgb(221, 91, 67),
        TextSuccess: Color.FromRgb(112, 181, 94),
        Hover: Color.FromRgb(67, 51, 31),
        HoverBorder: Color.FromRgb(173, 126, 50),
        RibbonHover: Color.FromRgb(73, 54, 30),
        RibbonHoverBorder: Color.FromRgb(218, 164, 66),
        RibbonTabHover: Color.FromRgb(57, 43, 26),
        PlotBackground: Color.FromRgb(25, 21, 17),
        PlotText: Color.FromRgb(225, 205, 164),
        PlotTick: Color.FromRgb(179, 151, 102),
        PlotGrid: Color.FromRgb(66, 56, 44),
        PlotAxis: Color.FromRgb(159, 120, 58),
        PlotZeroLine: Color.FromRgb(225, 177, 75),
        PlotHint: Color.FromRgb(137, 112, 76),
        PlotTooltipBackground: Color.FromArgb(240, 35, 27, 19),
        PlotTooltipBorder: Color.FromArgb(190, 193, 139, 57),
        PlotHoverMarkerFill: Color.FromRgb(25, 21, 17),
        PlotBar: Color.FromArgb(215, 201, 154, 61),
        SceneBackground: Color.FromRgb(17, 15, 13),
        SceneBackgroundAlt: Color.FromRgb(44, 35, 25),
        SceneLensFill: Color.FromArgb(120, 97, 139, 170),
        SceneReference: Color.FromRgb(171, 134, 72),
        SceneAxis: Color.FromRgb(139, 111, 72),
        SceneStop: Color.FromRgb(67, 128, 207),
        SceneApertureStop: Color.FromRgb(219, 176, 84),
        SceneSurface: Color.FromRgb(197, 164, 103),
        SceneLensEdge: Color.FromRgb(136, 103, 58),
        SceneTarget: Color.FromRgb(94, 151, 232),
        SceneOrientationFill: Color.FromArgb(218, 38, 29, 21),
        SceneOrientationBorder: Color.FromRgb(166, 121, 54));

    public static ResourceDictionary CreateResourceDictionary()
    {
        var resources = Palette.ToResourceDictionary();
        ApplyAccentResources(resources);
        return resources;
    }

    public static void ApplyAccentResources(IResourceDictionary resources)
    {
        var accent = Color.FromRgb(202, 148, 50);
        var accentBrush = new SolidColorBrush(accent);

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Color.FromRgb(170, 119, 34);
        resources["SystemAccentColorDark2"] = Color.FromRgb(137, 92, 26);
        resources["SystemAccentColorDark3"] = Color.FromRgb(103, 68, 22);
        resources["SystemAccentColorLight1"] = Color.FromRgb(220, 172, 75);
        resources["SystemAccentColorLight2"] = Color.FromRgb(232, 195, 118);
        resources["SystemAccentColorLight3"] = Color.FromRgb(244, 224, 180);
        resources["AccentFillColorDefaultBrush"] = accentBrush;
        resources["AccentFillColorSecondaryBrush"] =
            new SolidColorBrush(Color.FromRgb(184, 130, 39));
        resources["AccentFillColorTertiaryBrush"] =
            new SolidColorBrush(Color.FromRgb(158, 108, 31));
        foreach (var resourceKey in App.UnifiedDockAccentResourceKeys)
        {
            resources[resourceKey] = accentBrush;
        }

        var selectedForeground = new SolidColorBrush(Palette.TextOnAccent);
        resources["DockTabActiveForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabSelectedForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabCloseSelectedForegroundBrush"] = selectedForeground;
    }

    public static bool IsDarkLike(ThemeVariant? variant) =>
        variant == ThemeVariant.Dark || variant == Variant;
}