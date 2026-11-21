using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OpticalSystemDesign.App.Theming;

/// <summary>User annotation colours override automatic row identities without replacing native interaction states.</summary>
internal sealed class CustomSurfaceRowStyles : Styles
{
    internal const string RowClass = "custom-surface-row";
    internal const string NumericEditorClass = "lens-numeric-editor";

    internal static (Color Background, Color Foreground) ColorsFor(Color color, bool selected, bool hovered)
    {
        // Preserve the chosen RGB in the normal state; distinguish interaction through tonal steps.
        var amount = selected ? hovered ? .24 : .18 : hovered ? .08 : 0;
        var target = Luminance(color) > .179 ? Colors.Black : Colors.White;
        byte Blend(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
        var background = Color.FromRgb(Blend(color.R, target.R), Blend(color.G, target.G), Blend(color.B, target.B));
        var foreground = Luminance(background) > .179 ? Colors.Black : Colors.White;
        return (background, foreground);
    }

    internal static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            var channel = value / 255d;
            return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    internal CustomSurfaceRowStyles()
    {
        foreach (var selected in new[] { false, true })
            foreach (var hovered in new[] { false, true })
            {
                Selector Row(Selector? selector)
                {
                    var row = selector.OfType<DataGridRow>().Class(RowClass)
                        .Not(s => s.Class(":disabled")).Not(s => s.Class(":invalid"));
                    row = selected ? row.Class(":selected") : row.Not(s => s.Class(":selected"));
                    return hovered ? row.Class(":pointerover") : row.Not(s => s.Class(":pointerover"));
                }
                Binding Brush(bool foreground) => new("Drawing.RowColor")
                {
                    Converter = new RowBrushConverter(selected, hovered, foreground)
                };
                Add(new Style(Row)
                {
                    Setters =
                    {
                        new Setter(DataGridRow.BackgroundProperty, Brush(false)),
                        new Setter(DataGridRow.ForegroundProperty, Brush(true))
                    }
                });
                Add(new Style(s => Row(s).Template().OfType<Rectangle>().Name("BackgroundRectangle"))
                {
                    Setters =
                    {
                        new Setter(Shape.FillProperty, Brush(false)),
                        new Setter(Visual.OpacityProperty, 1d)
                    }
                });
                Add(new Style(s => Row(s).Descendant().OfType<DataGridCell>())
                {
                    Setters =
                    {
                        new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent),
                        new Setter(DataGridCell.ForegroundProperty, Brush(true))
                    }
                });
                // DataGrid's generated text blocks have their own foreground
                // setter, so inheriting the row/cell colour alone is insufficient.
                Add(new Style(s => Row(s).Descendant().OfType<TextBlock>())
                {
                    Setters = { new Setter(TextBlock.ForegroundProperty, Brush(true)) }
                });
                // Dense numeric editors have a transparent local background, while
                // a normal DataGrid text editor owns an opaque theme input surface.
                Add(new Style(s => Row(s).Descendant().OfType<TextBox>().Class(NumericEditorClass)
                    .Not(s => s.Class(":disabled")).Not(s => s.Class(":invalid")))
                {
                    Setters = { new Setter(TextBox.ForegroundProperty, Brush(true)) }
                });
            }
    }

    private sealed class RowBrushConverter(bool selected, bool hovered, bool foreground) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string text || !Color.TryParse(text, out var color)) return AvaloniaProperty.UnsetValue;
            var palette = ColorsFor(color, selected, hovered);
            return new ImmutableSolidColorBrush(foreground ? palette.Foreground : palette.Background);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
