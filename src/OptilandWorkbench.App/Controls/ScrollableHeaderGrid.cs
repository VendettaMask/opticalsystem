using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OptilandWorkbench.App.Controls;

/// <summary>Keeps an existing top band and body usable when a docked viewport becomes short.</summary>
internal sealed class ScrollableHeaderGrid : Grid
{
    private readonly ScrollViewer _header;

    internal ScrollableHeaderGrid(Control header, Control body)
    {
        RowDefinitions = new RowDefinitions("Auto,*");
        ClipToBounds = true;
        _header = new ScrollViewer
        {
            Content = header,
            AllowAutoHide = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Children.Add(_header);
        SetRow(body, 1);
        Children.Add(body);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Auto rows otherwise measure the band with infinite height and can consume the
        // entire viewport. Preserve its natural height until the body needs scrolling room.
        _header.MaxHeight = double.IsFinite(availableSize.Height)
            ? Math.Max(0, availableSize.Height - Math.Min(180, availableSize.Height * 0.4))
            : double.PositiveInfinity;
        return base.MeasureOverride(availableSize);
    }
}
