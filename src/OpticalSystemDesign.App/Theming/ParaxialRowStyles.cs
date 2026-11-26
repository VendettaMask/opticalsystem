using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OpticalSystemDesign.App.Theming;

internal sealed record ParaxialRowColors(Color Normal, Color Hover, Color Selected, Color SelectedHover)
{
    internal Color ForState(bool selected, bool hovered) =>
        selected ? hovered ? SelectedHover : Selected : hovered ? Hover : Normal;
}

/// <summary>Ideal paraxial surfaces keep a soft yellow-green identity across the native row, including frozen cells.</summary>
internal sealed class ParaxialRowStyles : Styles
{
    internal const string RowClass = "paraxial-row";

    internal static ParaxialRowColors ColorsFor(ThemeDefinition theme) => theme.SettingsValue switch
    {
        "Dark" => Colors("#343C23", "#3F482A", "#4C5532", "#565F39"),
        "Isekai" => Colors("#303923", "#3B452A", "#475333", "#505C3A"),
        "Lumen" => Colors("#2A3523", "#34412B", "#404E34", "#48583B"),
        "Pixel" => Colors("#E4F0C7", "#D6E6B2", "#C5DB9A", "#B6CF87"),
        _ => Colors("#E6F1D5", "#D8EABE", "#C9DFA9", "#BDD594")
    };

    internal ParaxialRowStyles()
    {
        Add(new Style(s => s.OfType<DataGridRow>().Class(RowClass).Descendant().OfType<DataGridCell>())
        { Setters = { new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent) } });
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            var palette = ColorsFor(theme);
            foreach (var selected in new[] { false, true })
                foreach (var hovered in new[] { false, true })
                {
                    var brush = new ImmutableSolidColorBrush(palette.ForState(selected, hovered));
                    Selector Row(Selector? selector)
                    {
                        var row = selector.OfType<DataGridRow>().Class(RowClass)
                            .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, theme.RequestedVariant)
                            .Not(s => s.Class(":disabled")).Not(s => s.Class(":invalid"));
                        row = selected ? row.Class(":selected") : row.Not(s => s.Class(":selected"));
                        return hovered ? row.Class(":pointerover") : row.Not(s => s.Class(":pointerover"));
                    }
                    Add(new Style(Row) { Setters = { new Setter(DataGridRow.BackgroundProperty, brush) } });
                    Add(new Style(s => Row(s).Template().OfType<Rectangle>().Name("BackgroundRectangle"))
                    { Setters = { new Setter(Shape.FillProperty, brush), new Setter(Visual.OpacityProperty, 1d) } });
                }
        }
    }

    private static ParaxialRowColors Colors(string normal, string hover, string selected, string selectedHover) =>
        new(Color.Parse(normal), Color.Parse(hover), Color.Parse(selected), Color.Parse(selectedHover));
}
