using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.ViewModels;

namespace OpticalSystemDesign.App.Panels;

// One subscription per realised container; detached/recycled rows must not retain old operands.
internal sealed class MeritOperandRowAppearance : IDisposable
{
    private readonly DataGridRow _row;
    private MeritOperandEditorRow? _operand;

    internal MeritOperandRowAppearance(DataGridRow row)
    {
        _row = row;
        row.Classes.Set("merit-color-row", true);
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
        _operand = _row.DataContext as MeritOperandEditorRow;
        if (_operand is not null) _operand.PropertyChanged += OperandChanged;
        Paint();
    }

    private void OperandChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MeritOperandEditorRow.Type) or nameof(MeritOperandEditorRow.Error)) Paint();
    }

    private void Paint()
    {
        var visual = MeritOperandRowPalette.ResolveVisual(_operand?.Type,
            ThemeRegistry.FromActualVariant(_row.ActualThemeVariant),
            !string.IsNullOrWhiteSpace(_operand?.Error), _row.IsPointerOver, _row.IsSelected, _row.IsEnabled);
        _row.Background = new SolidColorBrush(visual.Background);
        _row.Foreground = new SolidColorBrush(visual.Foreground);
    }
}
