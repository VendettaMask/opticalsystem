using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.App.Services;
using static OptilandWorkbench.App.Theming.BlueThemeTokens;

namespace OptilandWorkbench.App.Theming;

/// <summary>Maps shared blue tokens to the installed Fluent and Dock resource contracts.</summary>
internal static class LightTheme
{
    public static void ApplyResources(IResourceDictionary resources)
    {
        StandardTheme.ApplyAccentResources(resources);
        void Set(Color color, params string[] keys)
        {
            var brush = new SolidColorBrush(color);
            foreach (var key in keys) resources[key] = brush;
        }
        Set(Sidebar, ThemeResourceBindings.Sidebar);
        Set(HeaderBackground, ThemeResourceBindings.HeaderBackground);
        Set(TableHeader, ThemeResourceBindings.TableHeader);
        Set(SelectedBackground, ThemeResourceBindings.SelectionBackground);
        Set(TextPrimary, ThemeResourceBindings.SelectionForeground, ThemeResourceBindings.SectionHeaderForeground, ThemeResourceBindings.SectionHeaderEmphasizedForeground);
        Set(Surface, ThemeResourceBindings.SectionHeaderBackground, ThemeResourceBindings.SectionHeaderEmphasizedBackground);
        Set(ControlBorder, ThemeChromeResources.BorderBrush(ThemeChromeRole.ControlFrame));

        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3" })
            resources[key] = key.EndsWith("Dark1") ? AccentHover : key.Contains("Dark") ? AccentPressed : key.Contains("Light") ? TextSelectionBackground : Accent;
        Set(Accent, "AccentFillColorDefaultBrush", "SystemControlHighlightAccentBrush", "SystemControlHighlightListAccentHighBrush", "SystemControlHighlightListAccentMediumBrush");
        Set(FocusBorder, "SystemControlFocusVisualPrimaryBrush");
        Set(AccentHover, "AccentFillColorSecondaryBrush");
        Set(AccentPressed, "AccentFillColorTertiaryBrush");
        Set(Surface, "SystemControlBackgroundAltHighBrush", "SystemControlBackgroundChromeWhiteBrush", "SystemControlBackgroundChromeLowBrush", "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorSecondaryBrush", "SystemControlFocusVisualSecondaryBrush");
        Set(TextPrimary, "SystemControlForegroundBaseHighBrush", "TextFillColorPrimaryBrush");
        Set(TextSecondary, "SystemControlForegroundBaseMediumBrush", "SystemControlForegroundBaseMediumHighBrush", "SystemControlForegroundBaseMediumLowBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush");
        Set(TextDisabled, "SystemControlForegroundBaseLowBrush", "TextFillColorDisabledBrush");
        Set(Divider, "SystemControlForegroundChromeHighBrush", "SystemControlForegroundChromeMediumBrush", "SystemControlForegroundChromeDisabledLowBrush");
        Set(Error, "SystemControlErrorTextForegroundBrush", "DataValidationErrorsForeground");
        resources["SystemErrorTextColor"] = Error;
        resources["TextControlBorderThemeThicknessFocused"] = new Thickness(2);
        resources["ControlCornerRadius"] = new CornerRadius(5);
        resources["OverlayCornerRadius"] = ThemeChromeResources.StandardOverlayCornerRadius;

        foreach (var control in new[] { "Button", "RepeatButton", "ToggleButton", "SplitButton", "TextControlButton" })
        {
            States(control, "", Surface, HoverBackground, PressedBackground, ControlBorder);
            if (control == "ToggleButton")
                foreach (var state in new[] { "Checked", "Indeterminate" })
                    States(control, state, SelectedBackground, SelectedHover, SelectedPressed, Accent);
        }
        States("AccentButton", "", PrimaryButtonBackground, PrimaryButtonHoverBackground, PrimaryButtonPressedBackground, PrimaryButtonBackground, primary: true);
        foreach (var control in new[] { "ComboBoxItem", "ListBoxItem", "TreeViewItem" })
        {
            States(control, "", Surface, HoverBackground, PressedBackground, Colors.Transparent);
            States(control, "Selected", SelectedBackground, SelectedHover, SelectedPressed, Accent);
        }
        States("MenuFlyoutItem", "", Surface, HoverBackground, PressedBackground, Colors.Transparent);
        Set(TextSecondary, "MenuFlyoutItemKeyboardAcceleratorTextForeground", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", "MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", "MenuFlyoutSubItemChevron", "MenuFlyoutSubItemChevronPointerOver", "MenuFlyoutSubItemChevronPressed", "MenuFlyoutSubItemChevronSubMenuOpened");
        Set(TextDisabled, "MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled", "MenuFlyoutSubItemChevronDisabled");
        Set(Surface, "MenuFlyoutPresenterBackground", "ComboBoxDropDownBackground", "FlyoutPresenterBackground", "ListBoxBackground", "TabControlBackground", "ToolTipBackground");
        Set(Divider, "MenuFlyoutPresenterBorderBrush", "ComboBoxDropDownBorderBrush", "FlyoutBorderThemeBrush", "ToolTipBorderBrush", "MenuFlyoutSeparatorBackground");
        Set(TextPrimary, "ToolTipForeground");

        States("ComboBox", "", Surface, HoverBackground, PressedBackground, ControlBorder);
        Set(Surface, "ComboBoxBackgroundUnfocused");
        Set(FocusBorder, "ComboBoxBackgroundBorderBrushFocused");
        Set(ControlBorder, "ComboBoxBackgroundBorderBrushUnfocused");
        Set(TextPrimary, "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed", "ComboBoxDropDownGlyphForeground", "ComboBoxDropDownGlyphForegroundFocused", "ComboBoxDropDownGlyphForegroundFocusedPressed");
        Set(TextSecondary, "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed");
        Set(TextDisabled, "ComboBoxDropDownGlyphForegroundDisabled");
        Set(Surface, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused");
        Set(TextPrimary, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused");
        Set(ControlBorder, "TextControlBorderBrush");
        Set(PressedBorder, "TextControlBorderBrushPointerOver");
        Set(FocusBorder, "TextControlBorderBrushFocused");
        Set(TextSecondary, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
        Set(DisabledBackground, "TextControlBackgroundDisabled");
        Set(Divider, "TextControlBorderBrushDisabled");
        Set(TextDisabled, "TextControlForegroundDisabled", "TextControlPlaceholderForegroundDisabled");
        Set(TextSelectionBackground, "TextControlSelectionHighlightColor");

        foreach (var (state, background, hover, pressed) in new[] { ("Unselected", Surface, HoverBackground, PressedBackground), ("Selected", SelectedBackground, SelectedHover, SelectedPressed) })
        {
            Set(background, $"TabItemHeaderBackground{state}");
            Set(hover, $"TabItemHeaderBackground{state}PointerOver");
            Set(pressed, $"TabItemHeaderBackground{state}Pressed");
            Set(TextPrimary, $"TabItemHeaderForeground{state}");
        }
        Set(TextPrimary, "TabItemHeaderForegroundPointerOver", "TabItemHeaderForegroundPressed");
        Set(DisabledBackground, "TabItemHeaderBackgroundDisabled");
        Set(TextDisabled, "TabItemHeaderForegroundDisabled");
        Set(Accent, "TabItemHeaderSelectedPipeFill");
        Set(Surface, "ExpanderHeaderBackground", "ExpanderContentBackground");
        Set(HoverBackground, "ExpanderHeaderBackgroundPointerOver");
        Set(PressedBackground, "ExpanderHeaderBackgroundPressed");
        Set(TextPrimary, "ExpanderHeaderForeground");
        Set(Divider, "ExpanderHeaderBorderBrush", "ExpanderContentBorderBrush");

        foreach (var state in new[] { "Unchecked", "Checked", "Indeterminate" })
            foreach (var phase in new[] { "", "PointerOver", "Pressed", "Disabled" })
            {
                var disabled = phase == "Disabled";
                var active = state != "Unchecked";
                var fill = disabled ? DisabledBackground : active ? phase == "Pressed" ? AccentPressed : phase == "PointerOver" ? AccentHover : Accent
                    : phase == "Pressed" ? PressedBackground : phase == "PointerOver" ? HoverBackground : Surface;
                var stroke = disabled ? TextDisabled : active ? fill : phase == "" ? ControlBorder : Accent;
                Set(disabled ? TextDisabled : TextPrimary, $"CheckBoxForeground{state}{phase}");
                Set(Colors.Transparent, $"CheckBoxBackground{state}{phase}", $"CheckBoxBorderBrush{state}{phase}");
                Set(fill, $"CheckBoxCheckBackgroundFill{state}{phase}");
                Set(stroke, $"CheckBoxCheckBackgroundStroke{state}{phase}");
                Set(disabled ? TextDisabled : Surface, $"CheckBoxCheckGlyphForeground{state}{phase}");
            }
        foreach (var phase in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var disabled = phase == "Disabled";
            var accent = phase == "Pressed" ? AccentPressed : phase == "PointerOver" ? AccentHover : Accent;
            Set(disabled ? TextDisabled : TextPrimary, $"RadioButtonForeground{phase}");
            Set(Colors.Transparent, $"RadioButtonBackground{phase}", $"RadioButtonBorderBrush{phase}");
            Set(disabled ? DisabledBackground : Surface, $"RadioButtonOuterEllipseFill{phase}");
            Set(disabled ? TextDisabled : phase == "" ? ControlBorder : accent, $"RadioButtonOuterEllipseStroke{phase}");
            Set(disabled ? DisabledBackground : accent, $"RadioButtonOuterEllipseCheckedFill{phase}");
            Set(disabled ? TextDisabled : accent, $"RadioButtonOuterEllipseCheckedStroke{phase}");
            Set(disabled ? TextDisabled : Surface, $"RadioButtonCheckGlyphFill{phase}", $"RadioButtonCheckGlyphStroke{phase}");
        }

        Set(Surface, "DataGridRowBackgroundBrush", "DataGridCellBackgroundBrush", "DataGridDetailsPresenterBackgroundBrush");
        Set(TableHeader, "DataGridColumnHeaderBackgroundBrush");
        Set(HoverBackground, "DataGridColumnHeaderHoveredBackgroundBrush", "DataGridRowHoveredBackgroundColor");
        Set(PressedBackground, "DataGridColumnHeaderPressedBackgroundBrush", "DataGridColumnHeaderDraggedBackgroundBrush");
        Set(TextPrimary, "DataGridColumnHeaderForegroundBrush");
        Set(Divider, "DataGridGridLinesBrush", "DataGridColumnHeaderBorderBrush");
        Set(SelectedBackground, "DataGridRowSelectedBackgroundBrush", "DataGridRowSelectedUnfocusedBackgroundBrush");
        Set(SelectedHover, "DataGridRowSelectedHoveredBackgroundBrush", "DataGridRowSelectedHoveredUnfocusedBackgroundBrush");
        Set(FocusBorder, "DataGridCellFocusVisualPrimaryBrush");
        Set(Colors.Transparent, "DataGridCellFocusVisualSecondaryBrush");
        Set(Error, "DataGridCellInvalidBrush", "DataGridRowInvalidBrush");
        foreach (var state in new[] { "Selected", "SelectedHovered", "SelectedUnfocused", "SelectedHoveredUnfocused" })
            resources[$"DataGridRow{state}BackgroundOpacity"] = 1d;

        Set(ControlBorder, "ScrollBarForeground", "ScrollBarPanningThumbBackground");
        resources["ScrollBarThumbBackgroundColor"] = ControlBorder;
        resources["ScrollBarPanningThumbBackgroundColor"] = ControlBorder;
        Set(Surface, "ScrollBarBackground", "ScrollBarTrackFill", "ScrollBarButtonBackground");
        Set(HoverBackground, "ScrollBarBackgroundPointerOver", "ScrollBarTrackFillPointerOver");
        Set(AccentHover, "ScrollBarThumbFillPointerOver", "ScrollBarButtonArrowForeground");
        Set(AccentPressed, "ScrollBarThumbFillPressed");
        Set(TextDisabled, "ScrollBarButtonArrowForegroundDisabled", "ScrollBarThumbFillDisabled");
        States("ScrollBarButton", "", Surface, HoverBackground, PressedBackground, Colors.Transparent);
        Set(AccentHover, "ScrollBarButtonArrowForegroundPointerOver");
        Set(AccentPressed, "ScrollBarButtonArrowForegroundPressed");
        Set(Colors.Transparent, "ScrollBarBorderBrush", "ScrollBarTrackStroke");
        Set(Divider, "ScrollBarTrackStrokePointerOver");

        Set(Surface, "DockThemeBackgroundBrush", "DockSurfacePanelBrush", "DockSurfaceEditorBrush", "DockThemeControlBackgroundBrush", "DockTabBackgroundBrush", "DockDocumentTabStripBackgroundBrush");
        Set(AppBackground, "DockSurfaceWorkbenchBrush");
        Set(Sidebar, "DockSurfaceSidebarBrush", "DockSurfaceHeaderBrush");
        Set(SelectedBackground, "DockSurfaceHeaderActiveBrush", "DockTabActiveBackgroundBrush");
        Set(TextPrimary, "DockTabActiveForegroundBrush", "DockDocumentTabSelectedForegroundBrush", "DockDocumentTabCloseSelectedForegroundBrush", "DockThemeForegroundBrush", "DockTabForegroundBrush", "DockChromeButtonForegroundBrush", "DockToolChromeIconBrush", "DockDocumentTabPointerOverForegroundBrush", "DockDocumentTabClosePointerOverForegroundBrush");
        Set(Divider, "DockThemeBorderLowBrush", "DockBorderSubtleBrush", "DockBorderStrongBrush", "DockSeparatorBrush", "DockSplitterIdleBrush", "DockDocumentContentBorderBrush");
        Set(HoverBackground, "DockTabHoverBackgroundBrush", "DockTabCloseHoverBackgroundBrush", "DockChromeButtonHoverBackgroundBrush");
        Set(PressedBackground, "DockChromeButtonPressedBackgroundBrush", "DockSplitterHoverBrush");
        Set(Accent, "DockTabActiveIndicatorBrush", "DockTargetIndicatorBrush", "DockSplitterDragBrush");

        void States(string control, string state, Color normal, Color hover, Color pressed, Color border, bool primary = false)
        {
            foreach (var (phase, background) in new[] { ("", normal), ("PointerOver", hover), ("Pressed", pressed), ("Disabled", DisabledBackground) })
            {
                var disabled = phase == "Disabled";
                Set(background, $"{control}Background{state}{phase}");
                Set(disabled ? TextDisabled : primary ? PrimaryButtonText : TextPrimary, $"{control}Foreground{state}{phase}");
                Set(disabled ? Divider : primary ? background : phase == "Pressed" ? PressedBorder : phase == "PointerOver" ? Accent : border, $"{control}BorderBrush{state}{phase}");
            }
        }
    }
}
