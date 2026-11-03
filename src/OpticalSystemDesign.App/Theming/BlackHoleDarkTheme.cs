using Avalonia.Controls;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Warm black and accretion-light accents from the existing product artwork.</summary>
internal static class BlackHoleDarkTheme
{
    internal static Color Accent => C("#F0AE79");
    internal static Color AccentHover => C("#FFD0A6");
    internal static Color AccentPressed => C("#CD8B59");
    internal static Color Selected => C("#493023");
    internal static Color ControlBackground => C("#201A16");
    internal static Color MaterialHover => C("#264866");
    internal static Color MaterialSelected => C("#315C7E");

    // Keep the shared historical base intact: Lumen inherits its physical colour roles.
    internal static ThemePalette Palette { get; } = ThemePalette.DarkOpticStudio with
    {
        Workspace = C("#080808"),
        Surface = C("#151311"),
        SubtleSurface = C("#1D1916"),
        SettingsSurface = C("#191614"),
        SettingsOverlaySurface = C("#F2191614"),
        Border = C("#44382F"),
        TextPrimary = C("#F4ECE5"),
        TextSecondary = C("#C8BBAF"),
        TextMuted = C("#A5978B"),
        TextDisabled = C("#756B64"),
        TextAccent = Accent,
        TextOnAccent = C("#160D08"),
        Hover = C("#30241D"),
        HoverBorder = C("#966547"),
        RibbonHover = C("#35271F"),
        RibbonHoverBorder = C("#B67D53"),
        // This existing role also marks actual glass rows; retain its blue data cue.
        RibbonTabHover = ThemePalette.DarkOpticStudio.RibbonTabHover,
        PlotBackground = C("#0D0C0B"),
        PlotText = C("#F4ECE5"),
        PlotTick = C("#C8BBAF"),
        PlotGrid = C("#39312B"),
        PlotAxis = C("#A18C7B"),
        PlotZeroLine = C("#D5B89C"),
        PlotHint = C("#A5978B"),
        PlotTooltipBackground = C("#F2211A15"),
        PlotTooltipBorder = C("#966547"),
        PlotHoverMarkerFill = C("#0D0C0B"),
        SceneBackground = C("#0D0C0B"),
        SceneBackgroundAlt = C("#181411"),
        SceneOrientationFill = C("#EC191614"),
        SceneOrientationBorder = C("#966547")
    };

    internal static void ApplyResources(IResourceDictionary resources)
    {
        StandardTheme.ApplyAccentResources(resources);
        var surface = Palette.Surface;
        var ink = Palette.TextPrimary;
        var hover = Palette.Hover;
        var border = Palette.Border;
        var field = ControlBackground;
        resources["SystemAccentColor"] = Accent;
        resources["SystemAccentColorDark1"] = AccentPressed;
        resources["SystemAccentColorDark2"] = C("#AC6C40");
        resources["SystemAccentColorDark3"] = C("#7B492A");
        resources["SystemAccentColorLight1"] = AccentHover;
        resources["SystemAccentColorLight2"] = C("#FFE1C7");
        resources["SystemAccentColorLight3"] = C("#FFF0E2");
        Set(Accent, "AccentFillColorDefaultBrush", "SystemControlFocusVisualPrimaryBrush", "SystemControlHighlightAccentBrush");
        Set(AccentHover, "AccentFillColorSecondaryBrush");
        Set(AccentPressed, "AccentFillColorTertiaryBrush");
        Set(Palette.TextOnAccent, "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush");
        Set(surface, "SystemControlBackgroundAltHighBrush", "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorSecondaryBrush");
        Set(ink, "SystemControlForegroundBaseHighBrush", "TextFillColorPrimaryBrush");
        Set(Palette.TextSecondary, "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush");
        Set(Palette.TextDisabled, "TextFillColorDisabledBrush");
        Set(Selected, ThemeResourceBindings.SelectionBackground);
        Set(ink, ThemeResourceBindings.SelectionForeground);

        foreach (var control in new[] { "Button", "RepeatButton", "ToggleButton", "SplitButton", "TextControlButton", "ComboBox", "MenuFlyoutItem" })
            States(control, "", field, hover, Selected, border);
        foreach (var state in new[] { "Checked", "Indeterminate" })
            States("ToggleButton", state, Selected, C("#5B3B28"), C("#65432F"), Accent);
        States("AccentButton", "", Accent, AccentHover, AccentPressed, Accent, primary: true);
        foreach (var control in new[] { "ComboBoxItem", "ListBoxItem", "TreeViewItem" })
        {
            States(control, "", surface, hover, Selected, Colors.Transparent);
            States(control, "Selected", Selected, C("#5B3B28"), C("#65432F"), Colors.Transparent);
        }
        Set(surface, "MenuFlyoutPresenterBackground", "ComboBoxDropDownBackground", "FlyoutPresenterBackground", "ListBoxBackground", "ToolTipBackground");
        Set(border, "MenuFlyoutPresenterBorderBrush", "ComboBoxDropDownBorderBrush", "FlyoutBorderThemeBrush", "ToolTipBorderBrush", "MenuFlyoutSeparatorBackground");
        Set(ink, "ToolTipForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed");
        Set(Palette.TextMuted, "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed");
        Set(Palette.TextDisabled, "ComboBoxDropDownGlyphForegroundDisabled");
        Set(field, "ComboBoxBackgroundUnfocused", "TextControlBackground", "TextControlBackgroundFocused");
        Set(hover, "TextControlBackgroundPointerOver");
        Set(border, "ComboBoxBackgroundBorderBrushUnfocused", "TextControlBorderBrush");
        Set(Palette.HoverBorder, "TextControlBorderBrushPointerOver");
        Set(Accent, "ComboBoxBackgroundBorderBrushFocused", "TextControlBorderBrushFocused");
        Set(ink, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Set(Palette.TextMuted, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
        Set(Palette.SettingsSurface, "TextControlBackgroundDisabled");
        Set(border, "TextControlBorderBrushDisabled");
        Set(Palette.TextDisabled, "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled");
        Set(Selected, "TextControlSelectionHighlightColor", "SystemControlHighlightListMediumBrush", "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush");
        Set(hover, "SystemControlHighlightListLowBrush");
        Set(ink, "SystemControlHighlightAltBaseHighBrush");

        foreach (var state in new[] { "Unchecked", "Checked", "Indeterminate" })
            foreach (var phase in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                var disabled = phase == "Disabled";
                var active = state != "Unchecked";
                var fill = disabled ? Palette.SettingsSurface : active
                    ? phase == "Pressed" ? AccentPressed : phase == "PointerOver" ? AccentHover : Accent
                    : phase == "" ? field : hover;
                Set(disabled ? Palette.TextDisabled : ink, $"CheckBoxForeground{state}{phase}");
                Set(fill, $"CheckBoxCheckBackgroundFill{state}{phase}");
                Set(disabled ? Palette.TextDisabled : active ? fill : border, $"CheckBoxCheckBackgroundStroke{state}{phase}");
                Set(disabled ? Palette.TextDisabled : Palette.TextOnAccent, $"CheckBoxCheckGlyphForeground{state}{phase}");
            }
        foreach (var (state, fill) in new[] { ("Unselected", Colors.Transparent), ("Selected", Selected) })
        {
            Set(fill, $"TabItemHeaderBackground{state}");
            Set(hover, $"TabItemHeaderBackground{state}PointerOver");
            Set(Selected, $"TabItemHeaderBackground{state}Pressed");
            Set(state == "Selected" ? ink : Palette.TextSecondary, $"TabItemHeaderForeground{state}", $"TabItemHeaderForeground{state}PointerOver", $"TabItemHeaderForeground{state}Pressed");
        }
        Set(Accent, "TabItemHeaderSelectedPipeFill");
        Set(surface, "ExpanderHeaderBackground", "ExpanderContentBackground");
        Set(hover, "ExpanderHeaderBackgroundPointerOver");
        Set(Selected, "ExpanderHeaderBackgroundPressed");
        Set(ink, "ExpanderHeaderForeground");
        Set(border, "ExpanderHeaderBorderBrush", "ExpanderContentBorderBrush");
        Set(surface, "DataGridRowBackgroundBrush", "DataGridCellBackgroundBrush", "DataGridDetailsPresenterBackgroundBrush");
        Set(Palette.SubtleSurface, "DataGridColumnHeaderBackgroundBrush");
        Set(ink, "DataGridColumnHeaderForegroundBrush");
        Set(border, "DataGridGridLinesBrush", "DataGridColumnHeaderBorderBrush");
        Set(hover, "DataGridRowHoveredBackgroundColor", "DataGridColumnHeaderHoveredBackgroundBrush");
        Set(Selected, "DataGridRowSelectedBackgroundBrush", "DataGridRowSelectedHoveredBackgroundBrush", "DataGridRowSelectedUnfocusedBackgroundBrush", "DataGridRowSelectedHoveredUnfocusedBackgroundBrush");
        Set(Accent, "DataGridCellFocusVisualPrimaryBrush");
        foreach (var state in new[] { "Selected", "SelectedHovered", "SelectedUnfocused", "SelectedHoveredUnfocused" })
            resources[$"DataGridRow{state}BackgroundOpacity"] = 1d;
        Set(surface, "DockThemeBackgroundBrush", "DockSurfacePanelBrush", "DockSurfaceEditorBrush", "DockThemeControlBackgroundBrush");
        Set(Palette.Workspace, "DockSurfaceWorkbenchBrush", "DockTabBackgroundBrush", "DockDocumentTabStripBackgroundBrush");
        Set(Palette.SettingsSurface, "DockSurfaceSidebarBrush", "DockSurfaceHeaderBrush");
        Set(Selected, "DockSurfaceHeaderActiveBrush", "DockTabActiveBackgroundBrush");
        Set(ink, "DockThemeForegroundBrush", "DockTabForegroundBrush", "DockTabActiveForegroundBrush", "DockDocumentTabSelectedForegroundBrush", "DockDocumentTabCloseSelectedForegroundBrush", "DockChromeButtonForegroundBrush", "DockToolChromeIconBrush", "DockDocumentTabPointerOverForegroundBrush", "DockDocumentTabClosePointerOverForegroundBrush");
        Set(border, "DockThemeBorderLowBrush", "DockBorderSubtleBrush", "DockBorderStrongBrush", "DockSeparatorBrush", "DockSplitterIdleBrush", "DockDocumentContentBorderBrush");
        Set(hover, "DockTabHoverBackgroundBrush", "DockTabCloseHoverBackgroundBrush", "DockChromeButtonHoverBackgroundBrush");
        Set(Selected, "DockChromeButtonPressedBackgroundBrush", "DockSplitterHoverBrush");
        Set(Accent, "DockTabActiveIndicatorBrush", "DockTargetIndicatorBrush", "DockSplitterDragBrush");

        void Set(Color color, params string[] keys)
        {
            foreach (var key in keys) resources[key] = new SolidColorBrush(color);
        }
        void States(string control, string state, Color normal, Color over, Color pressed, Color edge, bool primary = false)
        {
            foreach (var (phase, background) in new[] { ("", normal), ("PointerOver", over), ("Pressed", pressed), ("Disabled", Palette.SettingsSurface) })
            {
                var disabled = phase == "Disabled";
                Set(background, $"{control}Background{state}{phase}");
                Set(disabled ? Palette.TextDisabled : primary ? Palette.TextOnAccent : ink, $"{control}Foreground{state}{phase}");
                Set(disabled ? border : primary ? background : phase == "" ? edge : Accent, $"{control}BorderBrush{state}{phase}");
            }
        }
    }

    private static Color C(string value) => Color.Parse(value);
}
