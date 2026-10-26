using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories;

/// <summary>Presentation shared by the coating, alignment and camera laboratories.</summary>
internal static class LaboratoryPresentation
{
    internal static void Apply(Window window)
    {
        window.BindThemeResource(TemplatedControl.BackgroundProperty, ThemeResourceBindings.Workspace);
        window.BindThemeResource(TemplatedControl.ForegroundProperty, ThemeResourceBindings.TextPrimary);
        var buttons = new Style(s => s.OfType<Button>().Class("lab-command"));
        buttons.Setters.Add(new Setter(Layoutable.MinHeightProperty, 34d));
        buttons.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 6)));
        window.Styles.Add(buttons);
        var headers = new Style(s => s.OfType<DataGridColumnHeader>());
        headers.Setters.Add(new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        window.Styles.Add(headers);
        var tabs = new Style(s => s.OfType<TabItem>().Class("lab-tab"));
        tabs.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 10)));
        tabs.Setters.Add(new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        window.Styles.Add(tabs);
        UiLocalization.Attach(window);
        DisplayTypography.Apply(window);
    }

    internal static TextBlock Hint(string text) => Muted(new TextBlock
    { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = DisplayTypography.BodySmall });

    internal static TextBlock Muted(TextBlock text)
    {
        text.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        return text;
    }

    internal static Control Heading(string title, string icon, bool page = false)
    {
        var glyph = new LocalIcon
        {
            IconName = icon,
            Width = page ? 26 : 18,
            Height = page ? 26 : 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextAccent);
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = page ? 10 : 8 };
        row.Children.Add(glyph);
        var label = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = page ? DisplayTypography.PageTitle : DisplayTypography.CardTitle,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1); row.Children.Add(label);
        return row;
    }

    internal static Control Header(string title, string icon, string description, Control context, params Control[] actions) =>
        new StackPanel
        {
            Name = "LaboratoryHeader",
            Spacing = 7,
            Children = { Heading(title, icon, page: true), Hint(description), context, Commands(actions) }
        };

    internal static WrapPanel Commands(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 5);
            if (control is Button button) { button.Classes.Add("lab-command"); button.Height = double.NaN; }
            panel.Children.Add(control);
        }
        return panel;
    }

    internal static Border Card(Control content, string? name = null)
    {
        var card = new Border { Name = name, Child = content, ClipToBounds = true };
        card.Classes.Add("lab-card");
        ThemeChrome.Apply(card, ThemeChromeRole.SurfaceCard, shadow: false);
        card.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        return card;
    }

    internal static Border Section(string title, string icon, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        panel.Children.Add(Heading(title, icon));
        foreach (var control in controls) panel.Children.Add(control);
        return Card(panel);
    }

    internal static Control Status(TextBlock text)
    {
        text.FontSize = DisplayTypography.BodySmall;
        var panel = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8, Margin = new Thickness(12, 9) };
        var glyph = new LocalIcon { IconName = "info", Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Top };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextMuted);
        panel.Children.Add(glyph); Grid.SetColumn(text, 1); panel.Children.Add(text);
        return Card(panel, "LaboratoryStatus");
    }

    internal static ScrollViewer Scroll(Control content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
}
