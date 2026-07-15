using Avalonia;

namespace OptilandWorkbench.App.Controls;

public sealed class SceneViewport
{
    private const double MinimumZoom = 0.2;
    private const double MaximumZoom = 12;

    public double Zoom { get; private set; } = 1;

    public Vector Pan { get; private set; }

    public Point Apply(Point point, Size viewportSize)
    {
        var center = new Point(viewportSize.Width / 2.0, viewportSize.Height / 2.0);
        return new Point(
            center.X + ((point.X - center.X) * Zoom) + Pan.X,
            center.Y + ((point.Y - center.Y) * Zoom) + Pan.Y);
    }

    public void PanBy(Vector delta)
    {
        Pan += delta;
    }

    public void ZoomAt(double factor, Point anchor, Size viewportSize)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return;
        }

        var targetZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        var ratio = targetZoom / Zoom;
        if (Math.Abs(ratio - 1) <= 1e-12)
        {
            return;
        }

        var center = new Point(viewportSize.Width / 2.0, viewportSize.Height / 2.0);
        var offset = anchor - center;
        Pan = new Vector(
            offset.X - ((offset.X - Pan.X) * ratio),
            offset.Y - ((offset.Y - Pan.Y) * ratio));
        Zoom = targetZoom;
    }

    public void Reset()
    {
        Zoom = 1;
        Pan = default;
    }
}
