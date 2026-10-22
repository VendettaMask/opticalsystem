using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class MainWindow
{
    // Scope presentation to this module; ordinary Workbench tabs and tables stay unchanged.
    private static void ApplyPresentationStyles(Control root)
    {
        var tabs = new Style(selector => selector.OfType<TabItem>().Class("initial-tab"));
        tabs.Setters.Add(new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        tabs.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 10)));
        tabs.Setters.Add(new Setter(Layoutable.MinHeightProperty, 40d));
        root.Styles.Add(tabs);
        var selectedLabel = new Style(selector => selector.OfType<TabItem>().Class("initial-tab").Class(":selected")
            .Descendant().OfType<TextBlock>().Class("initial-tab-label"));
        selectedLabel.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextAccent)));
        root.Styles.Add(selectedLabel);
        var headings = new Style(selector => selector.OfType<DataGridColumnHeader>());
        headings.Setters.Add(new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold));
        root.Styles.Add(headings);
    }

    private static TabControl CompactTabs() => new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel())
    };

    private static TabItem DetailTab(string label, Control content)
    {
        var heading = new TextBlock { Text = label, TextWrapping = TextWrapping.NoWrap };
        heading.Classes.Add("initial-tab-label");
        var tab = new TabItem { Header = heading, Content = content, FontSize = DisplayTypography.BodySmall };
        tab.Classes.Add("initial-tab");
        // WrapPanel can keep the previous arrangement when its overall desired size is unchanged.
        // Reflow translated or resized labels even when the tab strip still fits on one line.
        heading.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBlock.TextProperty && args.Property != TextBlock.FontSizeProperty) return;
            tab.InvalidateMeasure();
            foreach (var panel in tab.GetVisualAncestors().OfType<WrapPanel>())
            {
                panel.InvalidateMeasure();
                panel.InvalidateArrange();
            }
        };
        return tab;
    }

    private static Border Frame(Control content, bool subtle = false)
    {
        var frame = new Border { Child = content, ClipToBounds = true };
        ThemeChrome.Apply(frame, subtle ? ThemeChromeRole.SettingsCard : ThemeChromeRole.SurfaceCard, shadow: false);
        frame.BindThemeResource(Border.BackgroundProperty, subtle ? ThemeResourceBindings.SubtleSurface : ThemeResourceBindings.Surface);
        return frame;
    }

    private static TextBlock Hint(string text)
    {
        var hint = Text(text);
        hint.FontSize = DisplayTypography.CompactBody;
        hint.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        return hint;
    }

    private static StackPanel SectionHeading(string title, string icon, double? size = null)
    {
        var glyph = new LocalIcon { IconName = icon, Width = 19, Height = 19, VerticalAlignment = VerticalAlignment.Center };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextAccent);
        var label = Text(title);
        label.FontSize = size ?? DisplayTypography.CardTitle;
        label.FontWeight = FontWeight.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { glyph, label } };
    }

    private static Border Section(string title, string icon, params Control[] controls)
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        content.Children.Add(SectionHeading(title, icon));
        foreach (var control in controls) content.Children.Add(control);
        return Frame(content, subtle: true);
    }

    private static Expander Disclosure(string title, string id, params Control[] controls)
    {
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var control in controls) content.Children.Add(control);
        return Named(new Expander
        {
            Header = title, Content = content, IsExpanded = false,
            FontSize = DisplayTypography.BodySmall,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        }, id, title);
    }

    private static Border EmptyState(string id, string title, string description, string icon)
    {
        var content = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 420, Margin = new Thickness(18) };
        var heading = SectionHeading(title, icon);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(heading);
        var hint = Hint(description);
        hint.TextAlignment = TextAlignment.Center;
        content.Children.Add(hint);
        var state = Named(new Border { Child = content, IsHitTestVisible = false }, id, title);
        state.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        return state;
    }
}
