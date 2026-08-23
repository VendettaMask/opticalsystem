using Avalonia;
using Avalonia.Controls;

namespace OptilandWorkbench.App.Controls;

internal sealed class ResponsiveTwoPaneGrid : Grid
{
    private readonly Control _first;
    private readonly Control _second;
    private readonly string _wideColumns;
    private readonly string _narrowRows;
    private readonly double _breakpoint;
    private bool _isNarrow;

    internal ResponsiveTwoPaneGrid(
        Control first,
        Control second,
        string wideColumns,
        string narrowRows,
        double breakpoint)
    {
        _first = first;
        _second = second;
        _wideColumns = wideColumns;
        _narrowRows = narrowRows;
        _breakpoint = breakpoint;
        Children.Add(first);
        Children.Add(second);
        ApplyLayout(isNarrow: false);
    }

    internal bool IsNarrow => _isNarrow;

    protected override Size MeasureOverride(Size availableSize)
    {
        var isNarrow = double.IsFinite(availableSize.Width)
            && availableSize.Width < _breakpoint;
        if (isNarrow != _isNarrow)
        {
            ApplyLayout(isNarrow);
        }

        return base.MeasureOverride(availableSize);
    }

    private void ApplyLayout(bool isNarrow)
    {
        _isNarrow = isNarrow;
        if (isNarrow)
        {
            ColumnDefinitions = new ColumnDefinitions("*");
            RowDefinitions = new RowDefinitions(_narrowRows);
            SetPosition(_first, row: 0, column: 0);
            SetPosition(_second, row: 2, column: 0);
        }
        else
        {
            ColumnDefinitions = new ColumnDefinitions(_wideColumns);
            RowDefinitions = new RowDefinitions("*");
            SetPosition(_first, row: 0, column: 0);
            SetPosition(_second, row: 0, column: 2);
        }
    }

    private static void SetPosition(Control control, int row, int column)
    {
        SetRow(control, row);
        SetColumn(control, column);
    }
}
