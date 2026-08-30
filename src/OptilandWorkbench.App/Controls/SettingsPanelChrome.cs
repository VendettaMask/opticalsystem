using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App.Controls;

internal static class SettingsPanelChrome
{
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
        ThemeChrome.Apply(card, ThemeChromeRole.SettingsCard);
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SettingsOverlaySurface);
    }

    public static void ApplySurfaceCardStyle(Border card, bool shadow = true)
    {
        ThemeChrome.Apply(card, ThemeChromeRole.SurfaceCard, shadow);
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
    }

    public static void ApplyControlFrameStyle(Border frame)
    {
        ThemeChrome.Apply(frame, ThemeChromeRole.ControlFrame, shadow: false);
        frame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
    }
}
