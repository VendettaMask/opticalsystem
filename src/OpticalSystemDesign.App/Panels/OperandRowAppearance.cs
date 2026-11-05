using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;

namespace OpticalSystemDesign.App.Panels;

internal sealed class OperandRowAppearance : IDisposable
{
    private readonly DataGrid _grid;
    private readonly Dictionary<DataGridRow, RowAppearance> _rows = new();

    internal OperandRowAppearance(DataGrid grid)
    {
        _grid = grid;
        // Some theme cell surfaces are opaque; reveal the classified row underneath.
        grid.Styles.Add(new Style(s => s.OfType<DataGridCell>())
        { Setters = { new Setter(DataGridCell.BackgroundProperty, Brushes.Transparent) } });
        // Fluent paints an opaque rectangle above Row.Background. Bind that real surface,
        // for both frozen and scrolling cells, rather than only colouring the container.
        grid.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("operand-color-row")
            .Template().OfType<Rectangle>().Name("BackgroundRectangle"))
        {
            Setters =
            {
                new Setter(Shape.FillProperty, new Binding("Background")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DataGridRow) }
                }),
                new Setter(Avalonia.Visual.OpacityProperty, 1d)
            }
        });
        grid.Styles.Add(new Style(selector => selector
            .OfType<DataGridRow>()
            .Class("operand-color-row")
            .Class(":selected"))
        {
            Setters =
            {
                new Setter(
                    DataGridRow.BorderBrushProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush"))
            }
        });
        grid.Styles.Add(new Style(s => s.OfType<DataGridRow>().Class("operand-color-row").Class(":selected")
            .Template().OfType<Rectangle>().Name("BackgroundRectangle"))
        {
            Setters =
            {
                new Setter(Shape.StrokeProperty, new DynamicResourceExtension("AccentFillColorDefaultBrush")),
                new Setter(Shape.StrokeThicknessProperty, 1d)
            }
        });
        grid.LoadingRow += LoadingRow;
        grid.UnloadingRow += UnloadingRow;
    }

    public void Dispose()
    {
        _grid.LoadingRow -= LoadingRow;
        _grid.UnloadingRow -= UnloadingRow;
        foreach (var appearance in _rows.Values) appearance.Dispose();
        _rows.Clear();
    }

    private void LoadingRow(object? sender, DataGridRowEventArgs args)
    {
        UnloadingRow(sender, args);
        _rows.Add(args.Row, new RowAppearance(args.Row));
    }

    private void UnloadingRow(object? sender, DataGridRowEventArgs args)
    {
        if (_rows.Remove(args.Row, out var appearance)) appearance.Dispose();
    }

    // A recycled row observes its current operand only.
    private sealed class RowAppearance : IDisposable
    {
        private readonly DataGridRow _row;
        private INotifyPropertyChanged? _operand;

        internal RowAppearance(DataGridRow row)
        {
            _row = row;
            row.Classes.Set("operand-color-row", true);
            row.PropertyChanged += RowChanged;
            row.ActualThemeVariantChanged += ThemeChanged;
            ObserveOperand();
        }

        public void Dispose()
        {
            _row.PropertyChanged -= RowChanged;
            _row.ActualThemeVariantChanged -= ThemeChanged;
            if (_operand is not null) _operand.PropertyChanged -= OperandChanged;
            _operand = null;
        }

        private void RowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == StyledElement.DataContextProperty) ObserveOperand();
            else if (args.Property == DataGridRow.IsSelectedProperty
                || args.Property == InputElement.IsPointerOverProperty
                || args.Property == InputElement.IsEnabledProperty)
                Paint();
        }

        private void ThemeChanged(object? sender, EventArgs args) => Paint();

        private void ObserveOperand()
        {
            if (_operand is not null) _operand.PropertyChanged -= OperandChanged;
            _operand = _row.DataContext as INotifyPropertyChanged;
            if (_operand is not null) _operand.PropertyChanged += OperandChanged;
            Paint();
        }

        private void OperandChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(MeritOperandEditorRow.Type) or nameof(MeritOperandEditorRow.Error)
                or nameof(ToleranceOperandEditorRow.Kind) or null or "") Paint();
        }

        private void Paint()
        {
            var theme = ThemeRegistry.FromActualVariant(_row.ActualThemeVariant);
            var visual = _operand is ToleranceOperandEditorRow tolerance
                ? MeritOperandRowPalette.ResolveToleranceVisual(tolerance.Kind, theme,
                    _row.IsPointerOver, _row.IsSelected, _row.IsEnabled)
                : MeritOperandRowPalette.ResolveVisual((_operand as MeritOperandEditorRow)?.Type, theme,
                    _operand is MeritOperandEditorRow merit && !string.IsNullOrWhiteSpace(merit.Error),
                    _row.IsPointerOver, _row.IsSelected, _row.IsEnabled);
            _row.Background = new SolidColorBrush(visual.Background);
            _row.Foreground = new SolidColorBrush(visual.Foreground);
        }
    }
}
