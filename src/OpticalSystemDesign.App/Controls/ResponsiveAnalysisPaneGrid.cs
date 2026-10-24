using Avalonia;
using Avalonia.Controls;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Keeps the normal field arrangement when it fits, and reflows cards in narrow panes.</summary>
internal sealed class ResponsiveAnalysisPaneGrid(int preferredColumns, double minimumCellWidth, int columnUnits = 1) : Grid
{
    private (int Column, int Row)[]? _positions;
    private int _columns;

    protected override Size MeasureOverride(Size availableSize)
    {
        _positions ??= Children.Select(child => (GetColumn(child), GetRow(child))).ToArray();
        var columns = double.IsFinite(availableSize.Width)
            ? Math.Clamp((int)(availableSize.Width / minimumCellWidth), 1, preferredColumns)
            : preferredColumns;
        if (columns != _columns)
        {
            _columns = columns;
            var normal = columns == preferredColumns;
            var rows = normal ? _positions.Select(position => position.Row + 1).DefaultIfEmpty(1).Max()
                : Math.Max(1, (int)Math.Ceiling(Children.Count / (double)columns));
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns * columnUnits)));
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("*", rows)));
            for (var index = 0; index < Children.Count; index++)
            {
                var lastRowCount = Children.Count % columns;
                var offset = columnUnits == 2 && lastRowCount > 0 && index >= Children.Count - lastRowCount
                    ? columns - lastRowCount : 0;
                SetColumn(Children[index], normal ? _positions[index].Column : index % columns * columnUnits + offset);
                SetRow(Children[index], normal ? _positions[index].Row : index / columns);
            }
        }
        return base.MeasureOverride(availableSize);
    }
}
