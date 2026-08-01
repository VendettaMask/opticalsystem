using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

internal static class SettingsPanelChrome
{
    public static Button CreateToggleButton()
    {
        return new Button
        {
            Content = new LocalIconLabel("settings", "设置"),
            MinWidth = 0,
            Height = 32,
            Padding = new Thickness(8, 3)
        };
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
        card.CornerRadius = new CornerRadius(8);
        card.BorderThickness = new Thickness(1);
        card.BoxShadow = BoxShadows.Parse("0 5 16 0 #20000000");
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SettingsSurface);
        card.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
    }
}
