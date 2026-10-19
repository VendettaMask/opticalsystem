using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Quiet optical-instrument chrome; physical plot colours remain semantic.</summary>
internal static class LumenTheme
{
    public const string SettingsValue = "Lumen";
    public static ThemeVariant Variant { get; } = new(SettingsValue, ThemeVariant.Dark);
    internal static Color Accent => C("#D6AC6B");
    internal static Color MaterialRow => C("#183449");
    internal static Color MaterialHover => C("#24465B");
    internal static Color MaterialSelected => C("#2B5067");

    public static ThemePalette Palette { get; } = ThemePalette.DarkOpticStudio with
    {
        Surface = C("#0A1419"),
        SubtleSurface = C("#0D1C22"),
        SettingsSurface = C("#0C181D"),
        SettingsOverlaySurface = C("#F20C181D"),
        Workspace = C("#050C10"),
        Border = C("#2B4147"),
        TextPrimary = C("#D5DCD9"),
        TextSecondary = C("#A1B4B3"),
        TextMuted = C("#839D9E"),
        TextDisabled = C("#586E75"),
        TextAccent = C("#D6AC6B"),
        TextOnAccent = C("#081216"),
        TextWarning = C("#E3B977"),
        TextError = C("#F1A092"),
        TextSuccess = C("#9CC6AD"),
        WarningSurface = C("#30291B"),
        ErrorSurface = C("#332023"),
        SuccessSurface = C("#182E27"),
        Hover = C("#1B3039"),
        HoverBorder = C("#49646A"),
        RibbonHover = C("#14262D"),
        RibbonHoverBorder = C("#49646A"),
        RibbonTabHover = MaterialRow,
        PlotBackground = C("#0D1B20"),
        PlotText = C("#B7C9C4"),
        PlotTick = C("#94ACAA"),
        PlotGrid = C("#253B42"),
        PlotAxis = C("#849E9D"),
        PlotZeroLine = C("#B5C5BC"),
        PlotHint = C("#839D9E"),
        PlotTooltipBackground = C("#F20C181D"),
        PlotTooltipBorder = C("#49646A"),
        PlotHoverMarkerFill = C("#D5DCD9"),
        SceneBackground = C("#0D1B20"),
        SceneBackgroundAlt = C("#0D1B20"),
        SceneLensFill = C("#A0B1B0"),
        SceneReference = C("#819592"),
        SceneAxis = C("#526C70"),
        SceneOrientationFill = C("#EA0C181D"),
        SceneOrientationBorder = C("#49646A")
    };

    public static ThemeChromeProfile Chrome { get; } = ThemeChromeProfile.Create(
        Enum.GetValues<ThemeChromeRole>().ToDictionary(role => role, role =>
            ThemeChromeProfile.CreateStandard(Palette.Border, shadows: false)[role] with
            { CornerRadius = new CornerRadius(0), BoxShadow = default }));

    public static void ApplyAccentResources(IResourceDictionary resources)
    {
        var surface = Palette.Surface; var text = Palette.TextPrimary;
        var border = C("#405C64"); var selected = C("#233D48"); var hover = Palette.Hover;
        resources["SystemAccentColor"] = Accent;
        resources["SystemAccentColorDark1"] = C("#BE965A");
        resources["SystemAccentColorDark2"] = C("#A67E46");
        resources["SystemAccentColorDark3"] = C("#896538");
        resources["SystemAccentColorLight1"] = C("#E3BE87");
        resources["SystemAccentColorLight2"] = C("#EBD0A8");
        resources["SystemAccentColorLight3"] = C("#F3E2C7");
        Set(Accent, "AccentFillColorDefaultBrush", "SystemControlFocusVisualPrimaryBrush");
        Set(C("#E3BE87"), "AccentFillColorSecondaryBrush");
        Set(C("#BE965A"), "AccentFillColorTertiaryBrush");
        Set(surface, "SystemControlBackgroundAltHighBrush", "SolidBackgroundFillColorBaseBrush");
        Set(text, "SystemControlForegroundBaseHighBrush", "TextFillColorPrimaryBrush");
        Set(Palette.TextSecondary, "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush");
        Set(Palette.TextDisabled, "TextFillColorDisabledBrush");
        resources["ControlCornerRadius"] = new CornerRadius(0);
        resources["OverlayCornerRadius"] = new CornerRadius(0);
        resources["DockDocumentTabItemCornerRadius"] = new CornerRadius(0);
        resources["DockCornerRadiusSmall"] = 0d;
        resources[ThemeResourceBindings.SectionHeaderBackground] = Brushes.Transparent;
        resources[ThemeResourceBindings.SectionHeaderEmphasizedBackground] = Brushes.Transparent;
        resources[ThemeResourceBindings.SectionHeaderEmphasizedForeground] = B(text);
        resources[ThemeResourceBindings.SelectionBackground] = B(selected);
        resources[ThemeResourceBindings.SelectionForeground] = B(text);

        foreach (var control in new[] { "Button", "RepeatButton", "ToggleButton", "SplitButton", "TextControlButton" })
            States(control, "", C("#101F26"), hover, C("#233D48"), border);
        foreach (var state in new[] { "Checked", "Indeterminate" })
            States("ToggleButton", state, selected, C("#2A4652"), C("#304F5C"), Accent);
        States("AccentButton", "", Accent, C("#E3BE87"), C("#BE965A"), Accent, primary: true);
        foreach (var control in new[] { "ComboBoxItem", "ListBoxItem", "TreeViewItem" })
        {
            States(control, "", surface, hover, selected, Colors.Transparent);
            States(control, "Selected", selected, C("#2A4652"), C("#304F5C"), Colors.Transparent);
        }
        States("MenuFlyoutItem", "", surface, hover, selected, Colors.Transparent);
        States("ComboBox", "", C("#101F26"), hover, selected, border);
        Set(surface, "MenuFlyoutPresenterBackground", "ComboBoxDropDownBackground", "FlyoutPresenterBackground", "ListBoxBackground", "ToolTipBackground");
        Set(Palette.Border, "MenuFlyoutPresenterBorderBrush", "ComboBoxDropDownBorderBrush", "FlyoutBorderThemeBrush", "ToolTipBorderBrush", "MenuFlyoutSeparatorBackground");
        Set(text, "ToolTipForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed",
            "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed");
        Set(Palette.TextSecondary, "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed");
        Set(Palette.TextDisabled, "ComboBoxDropDownGlyphForegroundDisabled");
        Set(C("#101F26"), "ComboBoxBackgroundUnfocused", "TextControlBackground", "TextControlBackgroundFocused");
        Set(hover, "TextControlBackgroundPointerOver");
        Set(border, "ComboBoxBackgroundBorderBrushUnfocused", "TextControlBorderBrush");
        Set(Palette.HoverBorder, "TextControlBorderBrushPointerOver");
        Set(Accent, "ComboBoxBackgroundBorderBrushFocused", "TextControlBorderBrushFocused");
        Set(text, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Set(Palette.TextMuted, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
        Set(Palette.SettingsSurface, "TextControlBackgroundDisabled");
        Set(Palette.Border, "TextControlBorderBrushDisabled");
        Set(Palette.TextDisabled, "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled");
        Set(selected, "TextControlSelectionHighlightColor");
        Set(hover, "SystemControlHighlightListLowBrush");
        Set(selected, "SystemControlHighlightListMediumBrush", "SystemControlHighlightListAccentLowBrush",
            "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush");
        Set(text, "SystemControlHighlightAltBaseHighBrush");
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(2);

        foreach (var state in new[] { "Unchecked", "Checked", "Indeterminate" })
            foreach (var phase in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                var disabled = phase == "Disabled"; var active = state != "Unchecked";
                var fill = disabled ? Palette.SettingsSurface : active ? Accent : phase == "" ? surface : hover;
                Set(disabled ? Palette.TextDisabled : text, $"CheckBoxForeground{state}{phase}");
                Set(fill, $"CheckBoxCheckBackgroundFill{state}{phase}");
                Set(disabled ? Palette.TextDisabled : active ? Accent : border, $"CheckBoxCheckBackgroundStroke{state}{phase}");
                Set(disabled ? Palette.TextDisabled : Palette.TextOnAccent, $"CheckBoxCheckGlyphForeground{state}{phase}");
            }
        foreach (var (state, background) in new[] { ("Unselected", Colors.Transparent), ("Selected", selected) })
        {
            Set(background, $"TabItemHeaderBackground{state}");
            Set(hover, $"TabItemHeaderBackground{state}PointerOver");
            Set(selected, $"TabItemHeaderBackground{state}Pressed");
            Set(state == "Selected" ? text : Palette.TextSecondary, $"TabItemHeaderForeground{state}", $"TabItemHeaderForeground{state}PointerOver", $"TabItemHeaderForeground{state}Pressed");
        }
        Set(Accent, "TabItemHeaderSelectedPipeFill");
        Set(surface, "ExpanderHeaderBackground", "ExpanderContentBackground");
        Set(hover, "ExpanderHeaderBackgroundPointerOver");
        Set(selected, "ExpanderHeaderBackgroundPressed");
        Set(text, "ExpanderHeaderForeground");
        Set(Palette.Border, "ExpanderHeaderBorderBrush", "ExpanderContentBorderBrush");

        Set(surface, "DataGridRowBackgroundBrush", "DataGridCellBackgroundBrush", "DataGridDetailsPresenterBackgroundBrush", "DataGridColumnHeaderBackgroundBrush");
        Set(text, "DataGridColumnHeaderForegroundBrush");
        Set(Palette.Border, "DataGridGridLinesBrush", "DataGridColumnHeaderBorderBrush");
        Set(hover, "DataGridRowHoveredBackgroundColor", "DataGridColumnHeaderHoveredBackgroundBrush");
        Set(selected, "DataGridRowSelectedBackgroundBrush", "DataGridRowSelectedHoveredBackgroundBrush",
            "DataGridRowSelectedUnfocusedBackgroundBrush", "DataGridRowSelectedHoveredUnfocusedBackgroundBrush");
        Set(Accent, "DataGridCellFocusVisualPrimaryBrush");
        Set(Palette.TextError, "DataGridCellInvalidBrush", "DataGridRowInvalidBrush");
        foreach (var state in new[] { "Selected", "SelectedHovered", "SelectedUnfocused", "SelectedHoveredUnfocused" })
            resources[$"DataGridRow{state}BackgroundOpacity"] = 1d;

        Set(surface, "DockThemeBackgroundBrush", "DockSurfacePanelBrush", "DockSurfaceEditorBrush", "DockThemeControlBackgroundBrush");
        Set(Palette.Workspace, "DockSurfaceWorkbenchBrush", "DockTabBackgroundBrush", "DockDocumentTabStripBackgroundBrush");
        Set(Palette.SettingsSurface, "DockSurfaceSidebarBrush", "DockSurfaceHeaderBrush");
        Set(selected, "DockSurfaceHeaderActiveBrush", "DockTabActiveBackgroundBrush");
        Set(text, "DockThemeForegroundBrush", "DockTabForegroundBrush", "DockTabActiveForegroundBrush", "DockTabSelectedForegroundBrush",
            "DockDocumentTabSelectedForegroundBrush", "DockDocumentTabCloseSelectedForegroundBrush", "DockChromeButtonForegroundBrush",
            "DockToolChromeIconBrush", "DockDocumentTabPointerOverForegroundBrush", "DockDocumentTabClosePointerOverForegroundBrush");
        Set(Palette.Border, "DockThemeBorderLowBrush", "DockBorderSubtleBrush", "DockBorderStrongBrush", "DockSeparatorBrush", "DockSplitterIdleBrush", "DockDocumentContentBorderBrush");
        Set(hover, "DockTabHoverBackgroundBrush", "DockTabCloseHoverBackgroundBrush", "DockChromeButtonHoverBackgroundBrush");
        Set(selected, "DockChromeButtonPressedBackgroundBrush", "DockSplitterHoverBrush");
        Set(Accent, "DockTabActiveIndicatorBrush", "DockTargetIndicatorBrush", "DockSplitterDragBrush");

        void Set(Color color, params string[] keys)
        { foreach (var key in keys) resources[key] = B(color); }
        void States(string control, string state, Color normal, Color over, Color pressed, Color edge, bool primary = false)
        {
            foreach (var (phase, background) in new[] { ("", normal), ("PointerOver", over), ("Pressed", pressed), ("Disabled", Palette.SettingsSurface) })
            {
                var disabled = phase == "Disabled";
                Set(background, $"{control}Background{state}{phase}");
                Set(disabled ? Palette.TextDisabled : primary ? Palette.TextOnAccent : text, $"{control}Foreground{state}{phase}");
                Set(disabled ? Palette.Border : primary ? background : phase == "" ? edge : Accent, $"{control}BorderBrush{state}{phase}");
            }
        }
    }

    private static Color C(string color) => Color.Parse(color);
    // Some Fluent resources bind a concrete SolidColorBrush property. Create
    // fresh brushes per resource application rather than sharing static ones.
    private static SolidColorBrush B(Color color) => new(color);
}
