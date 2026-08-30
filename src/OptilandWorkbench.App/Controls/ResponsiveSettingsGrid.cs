using Avalonia;
using Avalonia.Controls;

namespace OptilandWorkbench.App.Controls;

internal sealed class ResponsiveSettingsGrid : Grid
{
    private readonly double _breakpoint;
    private bool _isNarrow;

    internal ResponsiveSettingsGrid(
        IEnumerable<Control> items,
        double breakpoint = 400)
    {
        ArgumentNullException.ThrowIfNull(items);
        _breakpoint = breakpoint > 0
            ? breakpoint
            : throw new ArgumentOutOfRangeException(nameof(breakpoint));
        foreach (var item in items)
        {
            Children.Add(item);
        }

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
        var columnCount = isNarrow ? 1 : 2;
        ColumnDefinitions = new ColumnDefinitions(isNarrow ? "*" : "*,12,*");
        var rowCount = Math.Max(1, (Children.Count + columnCount - 1) / columnCount);
        RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", rowCount)));

        for (var index = 0; index < Children.Count; index++)
        {
            SetRow(Children[index], index / columnCount);
            SetColumn(Children[index], isNarrow ? 0 : (index % columnCount) * 2);
        }
    }
}
