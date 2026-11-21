using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OpticalSystemDesign.App.Theming;

internal sealed record MirrorRowColors(Color Normal, Color Hover, Color Selected, Color SelectedHover)
{
    internal Color ForState(bool selected, bool hovered) =>
        selected ? hovered ? SelectedHover : Selected : hovered ? Hover : Normal;
}

/// <summary>Reflective surfaces keep a teal identity on the native row and frozen cells.</summary>
internal sealed class MirrorRowStyles : Styles
{
    internal const string RowClass = "mirror-row";

    internal static MirrorRowColors ColorsFor(ThemeDefinition theme) => theme.SettingsValue switch
    {
        "Dark" => Colors("#243A36", "#2D4742", "#36564F", "#3D6058"),
        "Isekai" => Colors("#293B33", "#33483E", "#3C5549", "#435E51"),
        "Lumen" => Colors("#203B3B", "#294949", "#315756", "#38615F"),
        "Pixel" => Colors("#E1F2ED", "#D1EAE3", "#BFDFD5", "#B2D8CC"),
        _ => Colors("#E6F5F0", "#D6EEE5", "#C5E5D9", "#B9DDD0")
    };

    internal MirrorRowStyles()
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

    private static MirrorRowColors Colors(string normal, string hover, string selected, string selectedHover) =>
        new(Color.Parse(normal), Color.Parse(hover), Color.Parse(selected), Color.Parse(selectedHover));
}
