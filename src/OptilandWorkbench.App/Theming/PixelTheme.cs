using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OptilandWorkbench.App.Services;

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
        TextOnAccent: Color.FromRgb(11, 34, 57),
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
        var accent = Color.FromRgb(239, 91, 91);
        var accentBrush = new SolidColorBrush(accent);

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Color.FromRgb(211, 67, 75);
        resources["SystemAccentColorDark2"] = Color.FromRgb(184, 40, 58);
        resources["SystemAccentColorDark3"] = Color.FromRgb(145, 31, 49);
        resources["SystemAccentColorLight1"] = Color.FromRgb(247, 123, 112);
        resources["SystemAccentColorLight2"] = Color.FromRgb(255, 166, 139);
        resources["SystemAccentColorLight3"] = Color.FromRgb(255, 208, 192);
        resources["AccentFillColorDefaultBrush"] = accentBrush;
        resources["AccentFillColorSecondaryBrush"] =
            new SolidColorBrush(Color.FromRgb(226, 77, 82));
        resources["AccentFillColorTertiaryBrush"] =
            new SolidColorBrush(Color.FromRgb(211, 67, 75));

        ApplySemanticResources(resources);
        ApplyFluentControlResources(resources);
        ApplyDockResources(resources, accentBrush);

        var selectedForeground = new SolidColorBrush(Palette.TextOnAccent);
        resources["DockTabActiveForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabSelectedForegroundBrush"] = selectedForeground;
        resources["DockDocumentTabCloseSelectedForegroundBrush"] = selectedForeground;
    }

    private static void ApplySemanticResources(IResourceDictionary resources)
    {
        var navy = Brush(Color.FromRgb(23, 50, 77));
        var blue = Brush(Color.FromRgb(59, 131, 189));
        var deepBlue = Brush(Color.FromRgb(41, 105, 157));
        var cream = Brush(Color.FromRgb(255, 244, 199));
        var paleBlue = Brush(Color.FromRgb(221, 239, 242));
        var yellow = Brush(Color.FromRgb(247, 201, 72));

        resources[ThemeResourceBindings.SectionHeaderBackground] = blue;
        resources[ThemeResourceBindings.SectionHeaderForeground] = cream;
        resources[ThemeResourceBindings.SectionHeaderEmphasizedBackground] = deepBlue;
        resources[ThemeResourceBindings.SectionHeaderEmphasizedForeground] = cream;
        resources[ThemeResourceBindings.SelectionBackground] = yellow;
        resources[ThemeResourceBindings.SelectionForeground] = navy;
        resources[ThemeResourceBindings.RibbonCommandBackground] = Brushes.Transparent;
        resources[ThemeResourceBindings.RibbonCommandBorder] = Brushes.Transparent;
        resources[ThemeResourceBindings.RibbonGroupBackground] = paleBlue;
        resources[ThemeResourceBindings.RibbonGroupBorder] = Brushes.Transparent;
        resources[ThemeResourceBindings.RibbonGroupCaptionBackground] = paleBlue;
        resources[ThemeResourceBindings.RibbonGroupCaptionForeground] = navy;
        resources[ThemeLayoutResources.RibbonMinHeight] = 96d;
        resources[ThemeLayoutResources.RibbonTabPadding] = new Thickness(8, 3);
        resources[ThemeLayoutResources.RibbonTabHeight] = 32d;
        resources[ThemeLayoutResources.RibbonPageSpacing] = 1d;
        resources[ThemeLayoutResources.RibbonPageMargin] = new Thickness(3, 1, 3, 0);
        resources[ThemeLayoutResources.RibbonGroupSpacing] = 1d;
        resources[ThemeLayoutResources.RibbonGroupCommandMargin] = new Thickness(3, 1, 3, 0);
        resources[ThemeLayoutResources.RibbonGroupCaptionHeight] = 0d;
        resources[ThemeLayoutResources.RibbonGroupCaptionPadding] = new Thickness(4, 0);
        resources[ThemeLayoutResources.RibbonGroupCaptionMaxHeight] = 0d;
        resources[ThemeLayoutResources.RibbonGroupMargin] = new Thickness(0, 0, 1, 0);
        resources[ThemeLayoutResources.RibbonCommandMinWidth] = 48d;
        resources[ThemeLayoutResources.RibbonCommandWidth] = 48d;
        resources[ThemeLayoutResources.RibbonCommandMinHeight] = 50d;
        resources[ThemeLayoutResources.RibbonCommandMargin] = new Thickness(1, 0, 1, 1);
        resources[ThemeLayoutResources.RibbonCommandPadding] = new Thickness(2);
        resources[ThemeLayoutResources.RibbonCommandBorderThickness] = new Thickness(0);
        resources[ThemeLayoutResources.RibbonCommandContentMinWidth] = 44d;
        resources[ThemeLayoutResources.RibbonCommandContentMinHeight] = 42d;
        resources[ThemeLayoutResources.RibbonCommandIconSize] = 22d;
        resources[ThemeLayoutResources.RibbonCommandStrokeWidth] = 1.6d;
        resources[ThemeLayoutResources.RibbonCommandTextMaxWidth] = 92d;
        resources["TabItemMinHeight"] = 32d;
    }

    private static void ApplyFluentControlResources(IResourceDictionary resources)
    {
        var navy = Brush(Color.FromRgb(23, 50, 77));
        var muted = Brush(Color.FromRgb(80, 105, 121));
        var cream = Brush(Color.FromRgb(255, 244, 199));
        var paper = Brush(Color.FromRgb(255, 249, 220));
        var paleBlue = Brush(Color.FromRgb(221, 239, 242));
        var sky = Brush(Color.FromRgb(168, 216, 240));
        var blue = Brush(Color.FromRgb(59, 131, 189));
        var deepBlue = Brush(Color.FromRgb(41, 105, 157));
        var yellow = Brush(Color.FromRgb(247, 201, 72));
        var lightYellow = Brush(Color.FromRgb(255, 225, 123));
        var coral = Brush(Color.FromRgb(239, 91, 91));
        var darkCoral = Brush(Color.FromRgb(211, 67, 75));
        var green = Brush(Color.FromRgb(48, 139, 78));

        resources["ControlCornerRadius"] = new CornerRadius(0);
        resources["OverlayCornerRadius"] = new CornerRadius(0);

        Set(resources, paleBlue, "ButtonBackground", "RepeatButtonBackground", "ToggleButtonBackground", "SplitButtonBackground");
        Set(resources, lightYellow, "ButtonBackgroundPointerOver", "RepeatButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver", "SplitButtonBackgroundPointerOver");
        Set(resources, yellow, "ButtonBackgroundPressed", "RepeatButtonBackgroundPressed", "ToggleButtonBackgroundPressed", "SplitButtonBackgroundPressed");
        Set(resources, cream, "ButtonBackgroundDisabled", "RepeatButtonBackgroundDisabled", "ToggleButtonBackgroundDisabled", "SplitButtonBackgroundDisabled");
        Set(resources, navy, "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "RepeatButtonForeground", "RepeatButtonForegroundPointerOver", "RepeatButtonForegroundPressed",
            "ToggleButtonForeground", "ToggleButtonForegroundPointerOver", "ToggleButtonForegroundPressed",
            "SplitButtonForeground", "SplitButtonForegroundPointerOver", "SplitButtonForegroundPressed");
        Set(resources, muted, "ButtonForegroundDisabled", "RepeatButtonForegroundDisabled", "ToggleButtonForegroundDisabled", "SplitButtonForegroundDisabled");
        Set(resources, navy, "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
            "RepeatButtonBorderBrush", "RepeatButtonBorderBrushPointerOver", "RepeatButtonBorderBrushPressed",
            "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver", "ToggleButtonBorderBrushPressed",
            "SplitButtonBorderBrush", "SplitButtonBorderBrushPointerOver", "SplitButtonBorderBrushPressed");
        Set(resources, muted, "ButtonBorderBrushDisabled", "RepeatButtonBorderBrushDisabled", "ToggleButtonBorderBrushDisabled", "SplitButtonBorderBrushDisabled");

        Set(resources, coral, "AccentButtonBackground");
        Set(resources, lightYellow, "AccentButtonBackgroundPointerOver");
        Set(resources, darkCoral, "AccentButtonBackgroundPressed");
        Set(resources, cream, "AccentButtonBackgroundDisabled");
        Set(resources, navy, "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
            "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed");
        Set(resources, muted, "AccentButtonForegroundDisabled", "AccentButtonBorderBrushDisabled");

        Set(resources, paper, "TextControlBackground", "TextControlBackgroundFocused", "ComboBoxBackground");
        Set(resources, cream, "TextControlBackgroundPointerOver", "ComboBoxBackgroundPointerOver");
        Set(resources, paleBlue, "TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled");
        Set(resources, yellow, "ComboBoxBackgroundPressed", "ComboBoxBackgroundUnfocused", "TextControlSelectionHighlightColor");
        Set(resources, navy, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
            "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed",
            "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed",
            "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed");
        Set(resources, muted, "TextControlForegroundDisabled", "TextControlBorderBrushDisabled",
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundDisabled",
            "ComboBoxForegroundDisabled", "ComboBoxBorderBrushDisabled", "ComboBoxDropDownGlyphForegroundDisabled");

        Set(resources, green, "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundFillIndeterminatePointerOver");
        Set(resources, deepBlue, "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundFillIndeterminatePressed");
        Set(resources, navy, "CheckBoxCheckBackgroundStrokeUnchecked", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeUncheckedPressed", "CheckBoxForegroundUnchecked", "CheckBoxForegroundUncheckedPointerOver",
            "CheckBoxForegroundUncheckedPressed", "CheckBoxForegroundChecked", "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundCheckedPressed");
        Set(resources, cream, "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "CheckBoxCheckGlyphForegroundIndeterminate",
            "CheckBoxCheckGlyphForegroundIndeterminatePointerOver", "CheckBoxCheckGlyphForegroundIndeterminatePressed");

        Set(resources, blue, "TabItemHeaderBackgroundUnselected");
        Set(resources, deepBlue, "TabItemHeaderBackgroundUnselectedPointerOver", "TabItemHeaderBackgroundUnselectedPressed");
        Set(resources, coral, "TabItemHeaderBackgroundSelected", "TabItemHeaderBackgroundSelectedPointerOver");
        Set(resources, darkCoral, "TabItemHeaderBackgroundSelectedPressed");
        Set(resources, cream, "TabItemHeaderForegroundUnselected", "TabItemHeaderForegroundUnselectedPointerOver", "TabItemHeaderForegroundUnselectedPressed");
        Set(resources, navy, "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundSelectedPointerOver", "TabItemHeaderForegroundSelectedPressed");
        Set(resources, yellow, "TabItemHeaderSelectedPipeFill");

        Set(resources, paper, "MenuFlyoutPresenterBackground", "ComboBoxDropDownBackground", "FlyoutPresenterBackground");
        Set(resources, navy, "MenuFlyoutPresenterBorderBrush", "ComboBoxDropDownBorderBrush", "FlyoutBorderThemeBrush");
        Set(resources, lightYellow, "MenuFlyoutItemBackgroundPointerOver", "ComboBoxItemBackgroundPointerOver");
        Set(resources, yellow, "MenuFlyoutItemBackgroundPressed", "ComboBoxItemBackgroundPressed",
            "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPressed", "ComboBoxItemBackgroundSelectedPointerOver");
        Set(resources, navy, "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed",
            "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed",
            "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundSelectedPressed");

        Set(resources, sky, "ExpanderHeaderBackground", "ExpanderHeaderBackgroundPointerOver");
        Set(resources, blue, "ExpanderHeaderBackgroundPressed");
        Set(resources, navy, "ExpanderHeaderForeground", "ExpanderHeaderForegroundPointerOver", "ExpanderHeaderForegroundPressed",
            "ExpanderHeaderBorderBrush", "ExpanderHeaderBorderBrushPointerOver", "ExpanderHeaderBorderBrushPressed");
        Set(resources, paper, "ExpanderContentBackground");
        Set(resources, navy, "ExpanderContentBorderBrush");

        Set(resources, lightYellow, "TreeViewItemBackgroundPointerOver");
        Set(resources, yellow, "TreeViewItemBackgroundPressed", "TreeViewItemBackgroundSelected",
            "TreeViewItemBackgroundSelectedPointerOver", "TreeViewItemBackgroundSelectedPressed");
        Set(resources, navy, "TreeViewItemForeground", "TreeViewItemForegroundPointerOver", "TreeViewItemForegroundPressed",
            "TreeViewItemForegroundSelected", "TreeViewItemForegroundSelectedPointerOver", "TreeViewItemForegroundSelectedPressed");

        Set(resources, paper, "ListBoxBackground");
        Set(resources, lightYellow, "ListBoxItemBackgroundPointerOver");
        Set(resources, yellow, "ListBoxItemBackgroundPressed", "ListBoxItemBackgroundSelected",
            "ListBoxItemBackgroundSelectedPointerOver", "ListBoxItemBackgroundSelectedPressed");
        Set(resources, navy, "ListBoxItemForeground", "ListBoxItemForegroundPointerOver", "ListBoxItemForegroundPressed",
            "ListBoxItemForegroundSelected", "ListBoxItemForegroundSelectedPointerOver", "ListBoxItemForegroundSelectedPressed");
        Set(resources, lightYellow, "SystemControlHighlightListLowBrush");
        Set(resources, yellow, "SystemControlHighlightListMediumBrush",
            "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush",
            "SystemControlHighlightListAccentHighBrush");
        Set(resources, navy, "SystemControlHighlightAltBaseHighBrush");

        Set(resources, paper, "DataGridRowBackgroundBrush", "DataGridCellBackgroundBrush");
        Set(resources, lightYellow, "DataGridRowHoveredBackgroundColor");
        Set(resources, yellow, "DataGridRowSelectedBackgroundBrush", "DataGridRowSelectedHoveredBackgroundBrush",
            "DataGridRowSelectedUnfocusedBackgroundBrush", "DataGridRowSelectedHoveredUnfocusedBackgroundBrush");
        Set(resources, navy, "DataGridGridLinesBrush", "DataGridCellFocusVisualPrimaryBrush");
        resources["DataGridRowSelectedBackgroundOpacity"] = 1d;
        resources["DataGridRowSelectedHoveredBackgroundOpacity"] = 1d;
        resources["DataGridRowSelectedUnfocusedBackgroundOpacity"] = 1d;
        resources["DataGridRowSelectedHoveredUnfocusedBackgroundOpacity"] = 1d;
    }

    private static void ApplyDockResources(IResourceDictionary resources, IBrush accentBrush)
    {
        var navy = Brush(Color.FromRgb(23, 50, 77));
        var cream = Brush(Color.FromRgb(255, 244, 199));
        var paper = Brush(Color.FromRgb(255, 249, 220));
        var paleBlue = Brush(Color.FromRgb(221, 239, 242));
        var sky = Brush(Color.FromRgb(168, 216, 240));
        var blue = Brush(Color.FromRgb(59, 131, 189));
        var deepBlue = Brush(Color.FromRgb(41, 105, 157));
        var yellow = Brush(Color.FromRgb(247, 201, 72));
        var lightYellow = Brush(Color.FromRgb(255, 225, 123));

        Set(resources, navy,
            "DockThemeBorderLowBrush", "DockThemeForegroundBrush", "DockBorderSubtleBrush",
            "DockBorderStrongBrush", "DockSeparatorBrush", "DockSplitterIdleBrush",
            "DockChromeButtonForegroundBrush", "DockToolChromeIconBrush",
            "DockDocumentContentBorderBrush");
        Set(resources, paper, "DockThemeBackgroundBrush", "DockSurfacePanelBrush");
        Set(resources, cream, "DockThemeControlBackgroundBrush", "DockSurfaceEditorBrush");
        resources["DockSurfaceWorkbenchBrush"] = sky;
        resources["DockSurfaceSidebarBrush"] = paleBlue;
        resources["DockSurfaceHeaderBrush"] = blue;
        resources["DockSurfaceHeaderActiveBrush"] = deepBlue;
        resources["DockTabBackgroundBrush"] = blue;
        resources["DockDocumentTabStripBackgroundBrush"] = blue;
        resources["DockTabHoverBackgroundBrush"] = lightYellow;
        resources["DockTabActiveBackgroundBrush"] = accentBrush;
        resources["DockTabActiveIndicatorBrush"] = accentBrush;
        resources["DockTabForegroundBrush"] = cream;
        resources["DockTabSelectedForegroundBrush"] = navy;
        resources["DockTabActiveForegroundBrush"] = cream;
        resources["DockDocumentTabSelectedForegroundBrush"] = navy;
        resources["DockDocumentTabPointerOverForegroundBrush"] = navy;
        resources["DockDocumentTabCloseSelectedForegroundBrush"] = navy;
        resources["DockDocumentTabClosePointerOverForegroundBrush"] = navy;
        resources["DockTabCloseHoverBackgroundBrush"] = yellow;
        resources["DockChromeButtonHoverBackgroundBrush"] = lightYellow;
        resources["DockChromeButtonPressedBackgroundBrush"] = yellow;
        resources["DockTargetIndicatorBrush"] = deepBlue;
        resources["DockSplitterHoverBrush"] = yellow;
        resources["DockSplitterDragBrush"] = deepBlue;
        resources["DockCornerRadiusSmall"] = 0d;
        resources["DockDocumentTabItemCornerRadius"] = new CornerRadius(0);
        resources["DockFontSizeNormal"] = 11d;
    }

    private static void Set(IResourceDictionary resources, IBrush brush, params string[] keys)
    {
        foreach (var key in keys)
        {
            resources[key] = brush;
        }
    }

    private static SolidColorBrush Brush(Color color) => new(color);
}
