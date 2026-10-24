using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace OpticalSystemDesign.App.Controls;

/// <summary>
/// Reflows a plot page at its readable size. Native scrolling handles content
/// that cannot fit; labels are never reduced by a page-wide scale transform.
/// </summary>
internal sealed class ResponsiveAnalysisPlotHost : ScrollViewer
{
    private readonly PlotPagePresenter _page = new();

    public ResponsiveAnalysisPlotHost()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = _page;
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public Control? Child
    {
        get => _page.Child;
        set { _page.Child = value; Offset = default; }
    }

    internal double ContentScale => 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        _page.SetViewport(new Size(
            Viewport.Width > 0 ? Math.Min(availableSize.Width, Viewport.Width) : availableSize.Width,
            Viewport.Height > 0 ? Math.Min(availableSize.Height, Viewport.Height) : availableSize.Height));
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewportProperty && _page is not null && Viewport.Width > 0 && Viewport.Height > 0)
            _page.SetViewport(Viewport);
    }

    private sealed class PlotPagePresenter : Decorator
    {
        private Size _viewport = new(600, 414);

        internal void SetViewport(Size size)
        {
            var next = new Size(double.IsFinite(size.Width) ? Math.Max(0, size.Width) : 600,
                double.IsFinite(size.Height) ? Math.Max(0, size.Height) : 414);
            if (next == _viewport) return;
            _viewport = next;
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is null) return default;
            var width = Math.Max(Math.Max(320, _viewport.Width), MinimumReadableWidth(Child));
            Child.Measure(new Size(width, double.PositiveInfinity));
            width = Math.Max(width, Child.DesiredSize.Width);
            Child.Measure(new Size(width, double.PositiveInfinity));
            var height = Math.Max(_viewport.Height, Child.DesiredSize.Height);
            Child.Measure(new Size(width, height));
            return new Size(Math.Max(width, Child.DesiredSize.Width), Math.Max(height, Child.DesiredSize.Height));
        }

        // DesiredSize is clipped to the measure constraint by Avalonia. Keep
        // explicit canvas minima when an ancestor grid has less available width.
        private static double MinimumReadableWidth(Control control)
        {
            var children = control switch
            {
                Panel panel => panel.Children.AsEnumerable(),
                Decorator { Child: { } child } => new[] { child },
                _ => Enumerable.Empty<Control>()
            };
            var minimum = control is ResponsivePlotLegendGrid legend
                ? MinimumReadableWidth(legend.Children[0]) + ResponsivePlotLegendGrid.MaximumLegendWidth + 2 * ResponsivePlotLegendGrid.LegendInset
                : children.Select(MinimumReadableWidth).DefaultIfEmpty(0).Max();
            return Math.Max(control.MinWidth, minimum) + control.Margin.Left + control.Margin.Right;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
