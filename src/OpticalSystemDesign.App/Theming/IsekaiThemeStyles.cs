using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using OpticalSystemDesign.App.Controls;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Material on existing Fluent bodies; all command and Dock behavior stays native.</summary>
internal sealed class IsekaiThemeStyles : Styles
{
    internal IsekaiThemeStyles()
    {
        var paperInk = new SolidColorBrush(Color.Parse("#513723"));
        Add(new Style(s => Command(s))
        { Setters = { new Setter(Button.ForegroundProperty, paperInk) } });
        CommandTextStyles(paperInk, disabled: false);
        Add(new Style(s => Command(s).Child().OfType<LocalIcon>())
        { Setters = { new Setter(LocalIcon.StrokeProperty, new SolidColorBrush(IsekaiTheme.Palette.TextOnAccent)) } });
        Add(new Style(s => Command(s).Child().OfType<LocalIconLabel>().Child().OfType<LocalIcon>())
        { Setters = { new Setter(LocalIcon.StrokeProperty, new SolidColorBrush(IsekaiTheme.Palette.TextOnAccent)) } });
        Add(new Style(s => Isekai<DocumentTabStripItem>(s).Class(":selected")
            .Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter"))
        { Setters = { new Setter(ContentPresenter.ForegroundProperty, paperInk) } });
        Add(new Style(s => Isekai<DocumentTabStripItem>(s).Class(":selected")
            .Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter").Descendant().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, paperInk) } });
        foreach (var (selected, hovered, state) in new[]
        {
            (false, false, 0), (false, true, 3), (true, false, 1), (true, true, 1)
        })
        {
            Add(new Style(s =>
            {
                var tab = Isekai<DocumentTabStripItem>(s);
                tab = selected ? tab.Class(":selected") : tab.Not(x => x.Class(":selected"));
                tab = hovered ? tab.Class(":pointerover") : tab.Not(x => x.Class(":pointerover"));
                return tab.Template().OfType<Border>().Name("PART_TabBody");
            })
            {
                Setters = { new Setter(Border.BackgroundProperty, IsekaiCraftsmanship.PlaqueBrush(state)),
                    new Setter(Border.BorderBrushProperty, Brushes.Transparent) }
            });
        }
        foreach (var hovered in new[] { false, true })
        {
            Add(new Style(s =>
            {
                var command = Command(s);
                command = hovered ? command.Class(":pointerover") : command.Not(x => x.Class(":pointerover"));
                return command.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            }) { Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, hovered ? new SolidColorBrush(Color.Parse("#2071502C")) : Brushes.Transparent),
                new Setter(ContentPresenter.ForegroundProperty, paperInk),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)
            } });
            Add(new Style(s =>
            {
                var command = Command(s);
                command = hovered ? command.Class(":pointerover") : command.Not(x => x.Class(":pointerover"));
                return command.Template().OfType<Border>().Name("RootBorder");
            }) { Setters =
            {
                new Setter(Border.BackgroundProperty, hovered ? new SolidColorBrush(Color.Parse("#2071502C")) : Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent)
            } });
        }
        var disabledInk = new SolidColorBrush(Color.Parse("#A18A68"));
        Add(new Style(s => Command(s).Class(":disabled"))
        { Setters = { new Setter(Button.ForegroundProperty, disabledInk) } });
        CommandTextStyles(disabledInk, disabled: true);
        Add(new Style(s => Command(s).Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        { Setters = { new Setter(ContentPresenter.ForegroundProperty, disabledInk) } });
    }
    private void CommandTextStyles(IBrush ink, bool disabled)
    {
        Selector Button(Selector? s) => disabled ? Command(s).Class(":disabled") : Command(s);
        // Restrict ink to the command's own content/presenter. A flyout can be
        // a logical descendant of its button while retaining a dark surface.
        Add(new Style(s => Button(s).Child().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
        Add(new Style(s => Button(s).Child().OfType<LocalIconLabel>().Child().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
        Add(new Style(s => Button(s).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")
            .Descendant().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
    }
    private static Selector Command(Selector? s) => Isekai<WrapPanel>(s).Class("analysis-actions").Child().Is<Button>();
    private static Selector Isekai<T>(Selector? s) where T : Control => s.OfType<T>()
        .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, IsekaiTheme.Variant);
}
