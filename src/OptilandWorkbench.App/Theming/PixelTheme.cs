using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OptilandWorkbench.App.Theming;

internal static class PixelTheme
{
    public const string SettingsValue = "Pixel";

    public static ThemeVariant Variant { get; } = new(SettingsValue, ThemeVariant.Light);

    public static ThemePalette Palette { get; } = new(
        Surface: Color.FromRgb(255, 244, 199),
        SubtleSurface: Color.FromRgb(221, 239, 242),
        SettingsSurface: Color.FromRgb(255, 249, 220),
        SettingsOverlaySurface: Color.FromArgb(232, 255, 249, 220),
        Workspace: Color.FromRgb(168, 216, 240),
        Border: Color.FromRgb(23, 50, 77),
        TextPrimary: Color.FromRgb(23, 50, 77),
        TextSecondary: Color.FromRgb(47, 81, 105),
        TextMuted: Color.FromRgb(80, 105, 121),
        TextDisabled: Color.FromRgb(117, 136, 147),
        TextAccent: Color.FromRgb(184, 40, 58),
        TextOnAccent: Color.FromRgb(255, 248, 213),
        TextWarning: Color.FromRgb(111, 67, 0),
        TextError: Color.FromRgb(153, 31, 48),
        TextSuccess: Color.FromRgb(25, 105, 55),
        WarningSurface: Color.FromRgb(255, 224, 106),
        ErrorSurface: Color.FromRgb(255, 208, 200),
        SuccessSurface: Color.FromRgb(200, 237, 184),
        Hover: Color.FromRgb(255, 214, 92),
        HoverBorder: Color.FromRgb(23, 50, 77),
        RibbonHover: Color.FromRgb(199, 231, 243),
        RibbonHoverBorder: Color.FromRgb(23, 50, 77),
        RibbonTabHover: Color.FromRgb(255, 225, 123),
        PlotBackground: Color.FromRgb(255, 249, 220),
        PlotText: Color.FromRgb(23, 50, 77),
        PlotTick: Color.FromRgb(47, 81, 105),
        PlotGrid: Color.FromRgb(169, 184, 181),
        PlotAxis: Color.FromRgb(23, 50, 77),
        PlotZeroLine: Color.FromRgb(184, 40, 58),
        PlotHint: Color.FromRgb(80, 105, 121),
        PlotTooltipBackground: Color.FromArgb(242, 23, 50, 77),
        PlotTooltipBorder: Color.FromArgb(220, 255, 214, 92),
        PlotHoverMarkerFill: Color.FromRgb(255, 249, 220),
        PlotBar: Color.FromArgb(224, 48, 139, 78),
        AnalysisRealRayRowBackground: Color.FromRgb(207, 235, 248),
        AnalysisRealRayRowForeground: Color.FromRgb(20, 76, 126),
        AnalysisParaxialRayRowBackground: Color.FromRgb(255, 229, 151),
        AnalysisParaxialRayRowForeground: Color.FromRgb(116, 62, 0),
        SceneBackground: Color.FromRgb(255, 249, 220),
        SceneBackgroundAlt: Color.FromRgb(210, 235, 239),
        SceneLensFill: Color.FromArgb(92, 75, 144, 203),
        SceneReference: Color.FromRgb(23, 50, 77),
        SceneAxis: Color.FromRgb(68, 102, 126),
        SceneStop: Color.FromRgb(32, 111, 176),
        SceneApertureStop: Color.FromRgb(23, 50, 77),
        SceneSurface: Color.FromRgb(23, 50, 77),
        SceneLensEdge: Color.FromRgb(44, 94, 130),
        SceneTarget: Color.FromRgb(184, 40, 58),
        SceneOrientationFill: Color.FromArgb(232, 255, 244, 199),
        SceneOrientationBorder: Color.FromRgb(23, 50, 77));

    public static void ApplyAccentResources(IResourceDictionary resources)
    {
        var accent = Color.FromRgb(184, 40, 58);
        var accentBrush = new SolidColorBrush(accent);

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Color.FromRgb(153, 31, 48);
        resources["SystemAccentColorDark2"] = Color.FromRgb(122, 25, 40);
        resources["SystemAccentColorDark3"] = Color.FromRgb(91, 20, 33);
        resources["SystemAccentColorLight1"] = Color.FromRgb(211, 74, 85);
        resources["SystemAccentColorLight2"] = Color.FromRgb(235, 128, 129);
        resources["SystemAccentColorLight3"] = Color.FromRgb(255, 208, 192);
        resources["AccentFillColorDefaultBrush"] = accentBrush;
        resources["AccentFillColorSecondaryBrush"] =
            new SolidColorBrush(Color.FromRgb(167, 35, 54));
        resources["AccentFillColorTertiaryBrush"] =
            new SolidColorBrush(Color.FromRgb(143, 29, 48));
        foreach (var resourceKey in StandardTheme.UnifiedDockAccentResourceKeys)
        {
            resources[resourceKey] = accentBrush;
        }

        var selectedForeground = new SolidColorBrush(Palette.TextOnAccent);
        resources["DockTabActiveForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabSelectedForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabCloseSelectedForegroundBrush"] = selectedForeground;
    }
}
