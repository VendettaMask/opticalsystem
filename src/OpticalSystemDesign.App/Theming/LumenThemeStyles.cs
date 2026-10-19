using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Only Lumen: keep the material cue on Fluent's actual visible row layer.</summary>
internal sealed class LumenThemeStyles : Styles
{
    public LumenThemeStyles()
    {
        foreach (var (state, fill, ink) in new[]
        {
            ("", LumenTheme.Accent, LumenTheme.Palette.TextOnAccent),
            (":pointerover", Color.Parse("#E3BE87"), LumenTheme.Palette.TextOnAccent),
            (":pressed", Color.Parse("#BE965A"), LumenTheme.Palette.TextOnAccent),
            (":disabled", LumenTheme.Palette.SettingsSurface, LumenTheme.Palette.TextDisabled)
        })
        {
            Selector AccentButton(Selector? s)
            {
                var selector = s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, LumenTheme.Variant).Class("accent");
                if (state.Length > 0) selector = selector.Class(state);
                if (state != ":disabled") selector = selector.Not(s => s.Class(":disabled"));
                return selector;
            }
            Add(new Style(AccentButton)
            {
                Setters = { new Setter(Button.BackgroundProperty, new ImmutableSolidColorBrush(fill)),
                    new Setter(Button.ForegroundProperty, new ImmutableSolidColorBrush(ink)) }
            });
            Add(new Style(s => AccentButton(s).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, new ImmutableSolidColorBrush(fill)),
                    new Setter(ContentPresenter.ForegroundProperty, new ImmutableSolidColorBrush(ink)) }
            });
        }
        Add(new Style(s => Lumen<DataGridCell>(s))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent) } });
        foreach (var selected in new[] { false, true })
            foreach (var hovered in new[] { false, true })
            {
                var colour = selected ? LumenTheme.MaterialSelected : hovered ? LumenTheme.MaterialHover : LumenTheme.MaterialRow;
                Add(new Style(s =>
                {
                    var row = Lumen<DataGridRow>(s).Class("glass-material-row")
                        .Not(s => s.Class(":disabled")).Not(s => s.Class(":invalid"));
                    row = selected ? row.Class(":selected") : row.Not(s => s.Class(":selected"));
                    row = hovered ? row.Class(":pointerover") : row.Not(s => s.Class(":pointerover"));
                    return row.Template().OfType<Rectangle>().Name("BackgroundRectangle");
                })
                { Setters = { new Setter(Shape.FillProperty, new ImmutableSolidColorBrush(colour)), new Setter(Visual.OpacityProperty, 1d) } });
            }
        Add(new Style(s => Lumen<DataGridCell>(s).Template().OfType<Rectangle>().Name("CurrencyVisual"))
        { Setters = { new Setter(Shape.StrokeProperty, new ImmutableSolidColorBrush(LumenTheme.Accent)), new Setter(Shape.StrokeThicknessProperty, 2d) } });
    }
    private static Selector Lumen<T>(Selector? selector) where T : Control => selector.OfType<T>()
        .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, LumenTheme.Variant);
}
