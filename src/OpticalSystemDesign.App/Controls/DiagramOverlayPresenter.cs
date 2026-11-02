using Avalonia;
using Avalonia.Controls;

namespace OpticalSystemDesign.App.Controls;

/// <summary>Positions native-size controls over the displayed drawing rectangle.
/// It does not scale the controls, measure the source or change its viewport.</summary>
internal sealed class DiagramOverlayPresenter(Control source) : Decorator
{
    private Rect _sourceBounds;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LayoutUpdated += OnLayoutUpdated;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }
    private Rect SourceBounds() => source.TransformToVisual(this) is { } transform
        ? new Rect(source.Bounds.Size).TransformToAABB(transform) : default;
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        var next = SourceBounds();
        if (Math.Abs(next.X - _sourceBounds.X) < .001 && Math.Abs(next.Y - _sourceBounds.Y) < .001
            && Math.Abs(next.Width - _sourceBounds.Width) < .001 && Math.Abs(next.Height - _sourceBounds.Height) < .001) return;
        _sourceBounds = next; InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var sourceSize = _sourceBounds.Width > 0 && _sourceBounds.Height > 0 ? _sourceBounds.Size : availableSize;
        Child?.Measure(sourceSize);
        // UI overlays must not add another minimum size to the drawing host.
        return default;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        _sourceBounds = SourceBounds();
        Child?.Measure(_sourceBounds.Size);
        Child?.Arrange(_sourceBounds);
        return finalSize;
    }
}
