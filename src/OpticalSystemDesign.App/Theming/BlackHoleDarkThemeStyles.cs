using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using OpticalSystemDesign.App.Controls;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Exact Dark variant only; native template layers retain action and glass semantics.</summary>
internal sealed class BlackHoleDarkThemeStyles : Styles
{
    public BlackHoleDarkThemeStyles()
    {
        foreach (var (state, fill, ink) in new[]
        {
            ("", BlackHoleDarkTheme.Accent, BlackHoleDarkTheme.Palette.TextOnAccent),
            (":pointerover", BlackHoleDarkTheme.AccentHover, BlackHoleDarkTheme.Palette.TextOnAccent),
            (":pressed", BlackHoleDarkTheme.AccentPressed, BlackHoleDarkTheme.Palette.TextOnAccent),
            (":disabled", BlackHoleDarkTheme.Palette.SettingsSurface, BlackHoleDarkTheme.Palette.TextDisabled)
        })
        {
            Selector Primary(Selector? s)
            {
                var selector = Dark<Button>(s).Class("accent");
                if (state.Length > 0) selector = selector.Class(state);
                if (state != ":disabled") selector = selector.Not(s => s.Class(":disabled"));
                return selector;
            }
            var background = new ImmutableSolidColorBrush(fill);
            var foreground = new ImmutableSolidColorBrush(ink);
            Add(new Style(Primary)
            { Setters = { new Setter(Button.BackgroundProperty, background), new Setter(Button.ForegroundProperty, foreground) } });
            Add(new Style(s => Primary(s).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            { Setters = { new Setter(ContentPresenter.BackgroundProperty, background), new Setter(ContentPresenter.ForegroundProperty, foreground) } });
            Add(new Style(s => Primary(s).Descendant().OfType<TextBlock>())
            { Setters = { new Setter(TextBlock.ForegroundProperty, foreground) } });
            Add(new Style(s => Primary(s).Descendant().OfType<LocalIcon>())
            { Setters = { new Setter(LocalIcon.StrokeProperty, foreground) } });
        }
        Add(new Style(s => Dark<DataGridCell>(s))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent) } });
        foreach (var selected in new[] { false, true })
            foreach (var hovered in new[] { false, true })
            {
                var colour = selected ? BlackHoleDarkTheme.MaterialSelected : hovered
                    ? BlackHoleDarkTheme.MaterialHover : BlackHoleDarkTheme.Palette.RibbonTabHover;
                Add(new Style(s =>
                {
                    var row = Dark<DataGridRow>(s).Class("glass-material-row")
                        .Not(s => s.Class(":disabled")).Not(s => s.Class(":invalid"));
                    row = selected ? row.Class(":selected") : row.Not(s => s.Class(":selected"));
                    row = hovered ? row.Class(":pointerover") : row.Not(s => s.Class(":pointerover"));
                    return row.Template().OfType<Rectangle>().Name("BackgroundRectangle");
                })
                { Setters = { new Setter(Shape.FillProperty, new ImmutableSolidColorBrush(colour)), new Setter(Visual.OpacityProperty, 1d) } });
            }
    }

    private static Selector Dark<T>(Selector? s) where T : Control => s.OfType<T>()
        .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Dark);
}
