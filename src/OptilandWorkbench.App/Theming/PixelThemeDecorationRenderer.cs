using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OptilandWorkbench.App.Theming;

internal sealed class PixelThemeDecorationRenderer : IThemeDecorationRenderer
{
    public static PixelThemeDecorationRenderer Instance { get; } = new();

    private static readonly IImmutableBrush HeaderBrush = Brush(Color.FromRgb(168, 216, 240));
    private static readonly IImmutableBrush HeaderDitherBrush = Brush(Color.FromArgb(80, 59, 131, 189));
    private static readonly IImmutableBrush NavyBrush = Brush(Color.FromRgb(23, 50, 77));
    private static readonly IImmutableBrush BlueBrush = Brush(Color.FromRgb(59, 131, 189));
    private static readonly IImmutableBrush CreamBrush = Brush(Color.FromRgb(255, 244, 199));
    private static readonly IImmutableBrush YellowBrush = Brush(Color.FromRgb(247, 201, 72));
    private static readonly IImmutableBrush CoralBrush = Brush(Color.FromRgb(239, 91, 91));

    private static readonly ImmutablePen NavyPen = Pen(NavyBrush, 1);
    private static readonly ImmutablePen BluePen = Pen(BlueBrush, 1);
    private static readonly ImmutablePen CreamPen = Pen(CreamBrush, 1);
    private static readonly ImmutablePen YellowPen = Pen(YellowBrush, 1);

    private PixelThemeDecorationRenderer()
    {
    }

    private static ImmutableSolidColorBrush Brush(Color color) => new(color);

    private static ImmutablePen Pen(IImmutableBrush brush, double thickness) => new(
        brush,
        thickness,
        null,
        PenLineCap.Square,
        PenLineJoin.Miter);

    public void Render(ThemeChromeRole role, DrawingContext context, Rect bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (role == ThemeChromeRole.Ribbon)
        {
            DrawRibbon(context, bounds);
            return;
        }

        if (role is ThemeChromeRole.Workspace or ThemeChromeRole.Viewport or ThemeChromeRole.Dialog)
        {
            DrawPixelFrame(context, bounds, role == ThemeChromeRole.Workspace);
        }
    }

    private static void DrawRibbon(DrawingContext context, Rect bounds)
    {
        var headerHeight = Math.Min(42, bounds.Height);
        var header = new Rect(0, 0, bounds.Width, headerHeight);
        context.DrawRectangle(HeaderBrush, null, header);

        for (var x = 10d; x < header.Right - 4; x += 32)
        {
            var y = 7 + (((int)(x / 32) & 1) * 8);
            context.DrawRectangle(HeaderDitherBrush, null, new Rect(x, y, 2, 2));
            context.DrawRectangle(HeaderDitherBrush, null, new Rect(x + 4, y + 4, 2, 2));
        }

        context.DrawLine(CreamPen, new Point(0, 0.5), new Point(header.Right, 0.5));
        context.DrawLine(BluePen, new Point(0, header.Bottom - 2.5), new Point(header.Right, header.Bottom - 2.5));
        context.DrawLine(NavyPen, new Point(0, header.Bottom - 0.5), new Point(header.Right, header.Bottom - 0.5));

        for (var x = 12d; x < header.Right; x += 128)
        {
            context.DrawRectangle(YellowBrush, null, new Rect(x, header.Bottom - 5, 3, 3));
            context.DrawRectangle(CoralBrush, null, new Rect(x + 3, header.Bottom - 5, 2, 3));
        }
    }

    private static void DrawPixelFrame(DrawingContext context, Rect bounds, bool restrained)
    {
        var outer = new Rect(0.5, 0.5, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        context.DrawRectangle(null, NavyPen, outer);
        if (!restrained)
        {
            var inner = outer.Deflate(2);
            if (inner.Width > 0 && inner.Height > 0)
            {
                context.DrawLine(CreamPen, inner.TopLeft, inner.TopRight);
                context.DrawLine(CreamPen, inner.TopLeft, inner.BottomLeft);
                context.DrawLine(BluePen, inner.BottomLeft, inner.BottomRight);
                context.DrawLine(BluePen, inner.TopRight, inner.BottomRight);
            }
        }

        DrawCorner(context, outer.TopLeft, new Vector(1, 1));
        DrawCorner(context, outer.TopRight, new Vector(-1, 1));
        DrawCorner(context, outer.BottomLeft, new Vector(1, -1));
        DrawCorner(context, outer.BottomRight, new Vector(-1, -1));
    }

    private static void DrawCorner(DrawingContext context, Point origin, Vector direction)
    {
        context.DrawLine(
            YellowPen,
            origin + new Vector(direction.X * 2, 0),
            origin + new Vector(direction.X * 8, 0));
        context.DrawLine(
            YellowPen,
            origin + new Vector(0, direction.Y * 2),
            origin + new Vector(0, direction.Y * 8));
        context.DrawRectangle(
            CoralBrush,
            null,
            new Rect(
                origin.X + (direction.X > 0 ? 2 : -4),
                origin.Y + (direction.Y > 0 ? 2 : -4),
                2,
                2));
    }
}
