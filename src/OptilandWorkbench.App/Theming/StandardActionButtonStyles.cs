using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace OptilandWorkbench.App.Theming;

/// <summary>Filled action buttons in the ordinary light theme only.</summary>
internal sealed class StandardActionButtonStyles : Styles
{
    internal static readonly Color Normal = Color.Parse("#D9ECFF");
    internal static readonly Color Hover = Color.Parse("#B9D9FF");
    internal static readonly Color Pressed = Color.Parse("#91C3FF");

    public StandardActionButtonStyles()
    {
        Add(new Style(ActionButton)
        {
            Setters = { new Setter(Button.BackgroundProperty, new SolidColorBrush(Normal)) }
        });
        AddState(":pointerover", Hover);
        AddState(":pressed", Pressed);
    }

    private static Selector ActionButton(Selector? selector) => selector
        .OfType<Button>()
        // Exact comparison: Pixel inherits Light but must keep its own colors.
        .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light)
        .Not(s => s.Class(":disabled"))
        .Not(s => s.Class("accent"))
        .Not(s => s.Class("ribbon-command"))
        .Not(s => s.Class("system-property-card-header"));

    private void AddState(string state, Color color) => Add(new Style(selector => ActionButton(selector)
        .Class(state)
        .Template()
        .OfType<ContentPresenter>()
        .Name("PART_ContentPresenter"))
    {
        Setters = { new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(color)) }
    });
}
