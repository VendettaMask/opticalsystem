using Avalonia;
using Avalonia.Controls;

namespace OpticalSystemDesign.App.Controls;

internal sealed class ResponsivePlotLegendGrid : Grid
{
    internal const double MaximumLegendWidth = 240;
    internal const double LegendInset = 12;
    private readonly Control _legend;
    // Auto-sized legends reserve their measured rail, not the maximum allowed
    // for long labels. DesiredSize includes the shared 12-DIP side insets.
    internal double LegendWidth => _legend.IsMeasureValid ? _legend.DesiredSize.Width
        : MaximumLegendWidth + 2 * LegendInset;

    public ResponsivePlotLegendGrid(Control plot, Control legend)
    {
        _legend = legend;
        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        Children.Add(plot);
        Children.Add(legend);
        SetColumn(legend, 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _legend.MaxWidth = double.IsFinite(availableSize.Width)
            ? Math.Clamp(availableSize.Width * 0.28, 120, MaximumLegendWidth) : MaximumLegendWidth;
        return base.MeasureOverride(availableSize);
    }
}
