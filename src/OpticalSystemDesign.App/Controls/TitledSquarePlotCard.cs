using Avalonia;
using Avalonia.Controls;

namespace OpticalSystemDesign.App.Controls;

internal sealed class TitledSquarePlotCard : Grid
{
    private readonly TextBlock _title;
    private readonly OptionalSquarePlotHost _plotHost;

    public TitledSquarePlotCard(TextBlock title, OptionalSquarePlotHost plotHost)
    {
        _title = title;
        _plotHost = plotHost;
        RowDefinitions = new RowDefinitions("Auto,*");
        Children.Add(title);
        SetRow(plotHost, 1);
        Children.Add(plotHost);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (_plotHost.Child is { } plot)
        {
            // Keep the square canvas and its axes in their existing slot. Attach
            // only the title to the canvas instead of the top of a tall grid cell.
            var squareTop = _plotHost.Bounds.Top + plot.Bounds.Top;
            var titleHeight = _title.DesiredSize.Height;
            _title.Arrange(new Rect(0, Math.Max(0, squareTop - titleHeight), finalSize.Width, titleHeight));
        }
        return result;
    }
}
