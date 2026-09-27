using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "光学镀膜设计实验室";
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid"))
        { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        Resources["CoatingSurface"] = new SolidColorBrush(BlueThemeTokens.Surface);
        Resources["CoatingBackground"] = new SolidColorBrush(BlueThemeTokens.AppBackground);
        Resources["CoatingText"] = new SolidColorBrush(BlueThemeTokens.TextPrimary);
        Resources["CoatingMuted"] = new SolidColorBrush(BlueThemeTokens.TextSecondary);
        Resources["CoatingBorder"] = new SolidColorBrush(BlueThemeTokens.ControlBorder);
        Resources["CoatingError"] = new SolidColorBrush(BlueThemeTokens.Error);
        Resources["SystemAccentColor"] = BlueThemeTokens.Accent;
        ApplyLightControlResources();
        Styles.Add(new Style(s => s.OfType<Button>()) { Setters =
        { new Setter(Button.MinHeightProperty, UiDensity.StandardControlHeight), new Setter(Button.PaddingProperty, new Thickness(12, 4)) } });
        Styles.Add(new Style(s => s.OfType<TextBox>()) { Setters =
        { new Setter(TextBox.MinHeightProperty, UiDensity.StandardControlHeight) } });
        Styles.Add(new Style(s => s.OfType<DataGrid>()) { Setters =
        { new Setter(DataGrid.RowHeightProperty, UiDensity.CompactTableRowHeight), new Setter(DataGrid.ColumnHeaderHeightProperty, UiDensity.TableHeaderHeight) } });
    }
    private void ApplyLightControlResources()
    {
        // Fluent adapter uses the formal application's single-source semantic colors.
        void Brush(Color color, params string[] keys)
        {
            foreach (var key in keys) Resources[key] = new SolidColorBrush(color);
        }
        foreach (var control in new[] { "Button", "ComboBox", "ToggleButton" })
        {
            foreach (var (state, background, foreground) in new[]
            {
                ("", BlueThemeTokens.Surface, BlueThemeTokens.TextPrimary),
                ("PointerOver", BlueThemeTokens.HoverBackground, BlueThemeTokens.TextPrimary),
                ("Pressed", BlueThemeTokens.PressedBackground, BlueThemeTokens.TextPrimary),
                ("Disabled", BlueThemeTokens.DisabledBackground, BlueThemeTokens.TextDisabled)
            })
            {
                Brush(background, control + "Background" + state);
                Brush(foreground, control + "Foreground" + state);
                Brush(BlueThemeTokens.ControlBorder, control + "BorderBrush" + state);
            }
        }
        Brush(BlueThemeTokens.Surface, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "DataGridBackground");
        Brush(BlueThemeTokens.ControlBorder, "TextControlBorderBrush", "ComboBoxBackgroundBorderBrushUnfocused");
        Brush(BlueThemeTokens.FocusBorder, "TextControlBorderBrushFocused", "ComboBoxBackgroundBorderBrushFocused", "DataGridCellFocusVisualPrimaryBrush");
        Brush(BlueThemeTokens.TextSelectionBackground, "TextControlSelectionHighlightColor");
        Brush(BlueThemeTokens.SelectedBackground, "DataGridRowSelectedBackgroundBrush", "DataGridRowSelectedUnfocusedBackgroundBrush");
        Brush(BlueThemeTokens.SelectedHover, "DataGridRowSelectedHoveredBackgroundBrush");
        Brush(BlueThemeTokens.TableHeader, "DataGridColumnHeaderBackgroundBrush");
        Brush(BlueThemeTokens.TextPrimary, "DataGridColumnHeaderForegroundBrush");
        Brush(BlueThemeTokens.PrimaryButtonBackground, "AccentButtonBackground");
        Brush(BlueThemeTokens.PrimaryButtonHoverBackground, "AccentButtonBackgroundPointerOver");
        Brush(BlueThemeTokens.PrimaryButtonPressedBackground, "AccentButtonBackgroundPressed");
        Brush(BlueThemeTokens.PrimaryButtonText, "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed");
        Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(2);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class LaboratoryTypography
{
    public const double Body = 13;
    public const double Caption = 10.5;
    public const double Section = 16;
}
