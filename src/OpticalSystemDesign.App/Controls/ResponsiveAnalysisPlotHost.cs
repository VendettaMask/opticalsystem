using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpticalSystemDesign.App.Controls;

/// <summary>
/// Fits an analysis plot page, including its axes, legend and title block, into
/// a docked viewport without forcing the parent's layout to honor canvas minima.
/// This transforms presentation only; data, units and interactive zoom are unchanged.
/// </summary>
internal sealed class ResponsiveAnalysisPlotHost : Decorator
{
    private readonly ScaleTransform _transform = new(1, 1);
    private Size _minimumContentSize = new(600, 414);

    public ResponsiveAnalysisPlotHost() => ClipToBounds = true;

    internal double ContentScale => _transform.ScaleX;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null) return default;
        // An unconstrained-height pass discovers the real minimum, including
        // multi-pane titles and legends. Do not merely
        // clip a 360-DIP canvas behind the fixed-height analysis footer.
        var width = double.IsFinite(availableSize.Width) ? Math.Max(600, availableSize.Width) : 600;
        Child.Measure(new Size(width, double.PositiveInfinity));
        _minimumContentSize = new Size(Math.Max(600, Child.DesiredSize.Width), Math.Max(414, Child.DesiredSize.Height));
        var viewport = FiniteSize(availableSize, _minimumContentSize);
        var (contentSize, _) = Fit(viewport, _minimumContentSize);
        Child.Measure(contentSize);
        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null) return finalSize;
        var (contentSize, scale) = Fit(finalSize, _minimumContentSize);
        Child.RenderTransformOrigin = RelativePoint.TopLeft;
        Child.RenderTransform = _transform;
        _transform.ScaleX = scale;
        _transform.ScaleY = scale;
        Child.Arrange(new Rect(contentSize));
        return finalSize;
    }

    private static Size FiniteSize(Size size, Size fallback) => new(
        double.IsFinite(size.Width) ? Math.Max(0, size.Width) : fallback.Width,
        double.IsFinite(size.Height) ? Math.Max(0, size.Height) : fallback.Height);

    internal static (Size ContentSize, double Scale) Fit(Size viewport, Size minimum)
    {
        viewport = FiniteSize(viewport, minimum);
        if (viewport.Width <= 0 || viewport.Height <= 0) return (minimum, 0);
        var scale = Math.Min(1, Math.Min(viewport.Width / minimum.Width, viewport.Height / minimum.Height));
        return (new Size(viewport.Width / scale, viewport.Height / scale), scale);
    }
}
