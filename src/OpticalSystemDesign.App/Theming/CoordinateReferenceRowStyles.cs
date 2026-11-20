using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OpticalSystemDesign.App.Theming;

internal sealed record CoordinateReferenceRowColors(Color Normal, Color Hover, Color Selected, Color SelectedHover)
{
    internal Color ForState(bool selected, bool hovered) =>
        selected ? hovered ? SelectedHover : Selected : hovered ? Hover : Normal;
}

/// <summary>Coordinate reference semantics on the native row surface, including frozen cells.</summary>
internal sealed class CoordinateReferenceRowStyles : Styles
{
    internal const string RowClass = "coordinate-reference-row";

    internal static CoordinateReferenceRowColors ColorsFor(ThemeDefinition theme) => theme.SettingsValue switch
    {
        "Dark" => Colors("#2F2939", "#3C3348", "#51405F", "#5B476C"),
        "Isekai" => Colors("#392D3B", "#49384B", "#5A4259", "#624861"),
        "Lumen" => Colors("#292D46", "#343B56", "#414D6A", "#495776"),
        "Pixel" => Colors("#E9E6F6", "#DDDAF0", "#CBC8E8", "#C1BDE1"),
        _ => Colors("#F1EDF8", "#E7DFF2", "#D8CBEA", "#CFBFE4")
    };

    internal CoordinateReferenceRowStyles()
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

    private static CoordinateReferenceRowColors Colors(string normal, string hover, string selected, string selectedHover) =>
        new(Color.Parse(normal), Color.Parse(hover), Color.Parse(selected), Color.Parse(selectedHover));
}
