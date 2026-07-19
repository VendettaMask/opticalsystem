using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OptilandWorkbench.App.Controls;

public enum ViewCubeFace
{
    Isometric,
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom
}

public sealed class ViewCubeIcon(ViewCubeFace face) : Control
{
    private static readonly IBrush NeutralTop = new SolidColorBrush(Color.FromRgb(239, 243, 247));
    private static readonly IBrush NeutralLeft = new SolidColorBrush(Color.FromRgb(224, 230, 236));
    private static readonly IBrush NeutralRight = new SolidColorBrush(Color.FromRgb(207, 216, 225));
    private static readonly IBrush HighlightTop = new SolidColorBrush(Color.FromRgb(126, 176, 216));
    private static readonly IBrush HighlightLeft = new SolidColorBrush(Color.FromRgb(91, 151, 201));
    private static readonly IBrush HighlightRight = new SolidColorBrush(Color.FromRgb(70, 126, 174));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(52, 68, 82)), 1);

    public ViewCubeFace Face { get; } = face;

    protected override Size MeasureOverride(Size availableSize) => new(26, 24);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var mirrorX = Face is ViewCubeFace.Back or ViewCubeFace.Right;
        var mirrorY = Face == ViewCubeFace.Bottom;
        var top = Transform(
            [new Point(13, 2.5), new Point(22, 7), new Point(13, 11.5), new Point(4, 7)],
            mirrorX,
            mirrorY);
        var left = Transform(
            [new Point(4, 7), new Point(13, 11.5), new Point(13, 21.5), new Point(4, 17)],
            mirrorX,
            mirrorY);
        var right = Transform(
            [new Point(13, 11.5), new Point(22, 7), new Point(22, 17), new Point(13, 21.5)],
            mirrorX,
            mirrorY);

        var (topBrush, leftBrush, rightBrush) = BrushesForFace();
        DrawPolygon(context, top, topBrush);
        DrawPolygon(context, left, leftBrush);
        DrawPolygon(context, right, rightBrush);
    }

    private (IBrush Top, IBrush Left, IBrush Right) BrushesForFace() => Face switch
    {
        ViewCubeFace.Isometric => (HighlightTop, HighlightLeft, HighlightRight),
        ViewCubeFace.Top or ViewCubeFace.Bottom => (HighlightTop, NeutralLeft, NeutralRight),
        ViewCubeFace.Front or ViewCubeFace.Back => (NeutralTop, HighlightLeft, NeutralRight),
        ViewCubeFace.Left or ViewCubeFace.Right => (NeutralTop, NeutralLeft, HighlightRight),
        _ => (NeutralTop, NeutralLeft, NeutralRight)
    };

    private Point[] Transform(IReadOnlyList<Point> points, bool mirrorX, bool mirrorY)
    {
        var scale = Math.Min(Bounds.Width / 26.0, Bounds.Height / 24.0);
        var offsetX = (Bounds.Width - (26 * scale)) / 2.0;
        var offsetY = (Bounds.Height - (24 * scale)) / 2.0;
        return points
            .Select(point => new Point(
                offsetX + ((mirrorX ? 26 - point.X : point.X) * scale),
                offsetY + ((mirrorY ? 24 - point.Y : point.Y) * scale)))
            .ToArray();
    }

    private static void DrawPolygon(DrawingContext context, IReadOnlyList<Point> points, IBrush fill)
    {
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(points[0], true);
            for (var index = 1; index < points.Count; index++)
            {
                stream.LineTo(points[index]);
            }

            stream.EndFigure(true);
        }

        context.DrawGeometry(fill, Outline, geometry);
    }
}
