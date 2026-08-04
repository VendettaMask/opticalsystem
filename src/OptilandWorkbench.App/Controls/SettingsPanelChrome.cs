using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

internal static class SettingsPanelChrome
{
    public static CornerRadius CardCornerRadius { get; } = new(8);
    public static CornerRadius ControlCornerRadius { get; } = new(5);
    public static BoxShadows CardShadow { get; } = BoxShadows.Parse("0 5 16 0 #20000000");

    public static Button CreateToggleButton()
    {
        var button = new Button
        {
            Content = new LocalIconLabel("settings", "设置"),
            MinWidth = 0,
            Height = 32,
            Padding = new Thickness(8, 3)
        };
        button.BindThemeResource(Button.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
        return button;
    }

    public static Border CreateCard(
        Control child,
        Thickness margin,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
    {
        var card = new Border
        {
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Margin = margin,
            Child = child
        };
        ApplyCardStyle(card);
        return card;
    }

    public static void ApplyCardStyle(Border card)
    {
        card.CornerRadius = CardCornerRadius;
        card.BorderThickness = new Thickness(1);
        card.BoxShadow = CardShadow;
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SettingsOverlaySurface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
    }

    public static void ApplySurfaceCardStyle(Border card, bool shadow = true)
    {
        card.CornerRadius = CardCornerRadius;
        card.BorderThickness = new Thickness(1);
        card.BoxShadow = shadow ? CardShadow : default;
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
    }

    public static void ApplyControlFrameStyle(Border frame)
    {
        frame.CornerRadius = ControlCornerRadius;
        frame.BorderThickness = new Thickness(1);
        frame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        frame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
    }
}
