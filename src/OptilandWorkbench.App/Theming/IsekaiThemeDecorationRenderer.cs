using Avalonia;
using Avalonia.Media;

namespace OptilandWorkbench.App.Theming;

internal sealed class IsekaiThemeDecorationRenderer : IThemeDecorationRenderer
{
    public static IsekaiThemeDecorationRenderer Instance { get; } = new();

    private static readonly IBrush LeatherBrush = new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops =
        {
            new GradientStop(Color.FromRgb(48, 36, 24), 0),
            new GradientStop(Color.FromRgb(19, 17, 15), 0.52),
            new GradientStop(Color.FromRgb(38, 28, 19), 1)
        }
    };

    private static readonly IBrush BladeBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(226, 213, 177), 0),
            new GradientStop(Color.FromRgb(95, 91, 80), 0.48),
            new GradientStop(Color.FromRgb(207, 188, 143), 1)
        }
    };

    private static readonly IBrush GoldBrush =
        new SolidColorBrush(Color.FromRgb(181, 132, 54));
    private static readonly IBrush GoldHighlightBrush =
        new SolidColorBrush(Color.FromArgb(205, 239, 198, 103));
    private static readonly IBrush GoldShadowBrush =
        new SolidColorBrush(Color.FromRgb(84, 57, 25));
    private static readonly IBrush TextureBrush =
        new SolidColorBrush(Color.FromArgb(36, 231, 194, 119));

    private IsekaiThemeDecorationRenderer()
    {
    }

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
            DrawWorkspaceFrame(context, bounds);
        }
    }

    private static void DrawRibbon(DrawingContext context, Rect bounds)
    {
        var headerHeight = Math.Min(42, bounds.Height);
        var header = new Rect(0, 0, bounds.Width, headerHeight);
        context.DrawRectangle(LeatherBrush, null, header);

        DrawLeatherTexture(context, header);
        DrawTabBays(context, header);
        DrawSword(context, header);
        DrawFrame(context, header);
    }

    private static void DrawWorkspaceFrame(DrawingContext context, Rect bounds)
    {
        var outer = new Rect(0.5, 0.5, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
        var inner = outer.Deflate(3);
        context.DrawRectangle(null, new Pen(GoldShadowBrush, 1.8), outer);
        if (inner.Width > 0 && inner.Height > 0)
        {
            context.DrawRectangle(null, new Pen(GoldHighlightBrush, 0.55), inner);
        }

        const double corner = 15;
        var runePen = new Pen(GoldBrush, 1.1, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter);
        DrawCorner(context, runePen, outer.TopLeft, new Vector(1, 1), corner);
        DrawCorner(context, runePen, outer.TopRight, new Vector(-1, 1), corner);
        DrawCorner(context, runePen, outer.BottomLeft, new Vector(1, -1), corner);
        DrawCorner(context, runePen, outer.BottomRight, new Vector(-1, -1), corner);
    }

    private static void DrawCorner(
        DrawingContext context,
        Pen pen,
        Point origin,
        Vector direction,
        double length)
    {
        var horizontal = origin + new Vector(direction.X * length, 0);
        var vertical = origin + new Vector(0, direction.Y * length);
        context.DrawLine(pen, origin, horizontal);
        context.DrawLine(pen, origin, vertical);
        context.DrawLine(
            pen,
            origin + new Vector(direction.X * 4, 0),
            origin + new Vector(0, direction.Y * 4));
    }

    private static void DrawLeatherTexture(DrawingContext context, Rect header)
    {
        var texturePen = new Pen(TextureBrush, 0.65);
        for (var x = 7d; x < header.Width; x += 23)
        {
            var offset = ((int)(x / 23) % 3) * 3;
            context.DrawLine(
                texturePen,
                new Point(x, 5 + offset),
                new Point(Math.Min(header.Right, x + 13), 8 + offset));
        }

        var lowerShade = new SolidColorBrush(Color.FromArgb(112, 5, 5, 4));
        context.DrawRectangle(lowerShade, null, new Rect(0, header.Bottom - 12, header.Width, 12));
    }

    private static void DrawTabBays(DrawingContext context, Rect header)
    {
        const double startX = 126;
        const double bayWidth = 112;
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(112, 205, 155, 68)), 0.8);
        var inner = new Pen(new SolidColorBrush(Color.FromArgb(95, 42, 28, 15)), 1.2);

        for (var left = startX; left < header.Right - 8; left += bayWidth)
        {
            var right = Math.Min(header.Right - 8, left + bayWidth);
            context.DrawLine(outline, new Point(left, 4), new Point(right, 4));
            context.DrawLine(outline, new Point(left, 4), new Point(left, header.Bottom - 7));
            context.DrawLine(inner, new Point(left + 2, 6), new Point(right - 2, 6));
        }
    }

    private static void DrawSword(DrawingContext context, Rect header)
    {
        var centerY = header.Bottom - 7.5;
        var hiltX = Math.Min(102, Math.Max(58, header.Width * 0.075));

        context.DrawEllipse(GoldShadowBrush, new Pen(GoldHighlightBrush, 0.8), new Point(10, centerY), 7, 7);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(48, 25, 14)), 8), new Point(16, centerY), new Point(hiltX - 13, centerY));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(153, 91, 38)), 1.2), new Point(18, centerY - 2), new Point(hiltX - 15, centerY - 2));
        for (var x = 22d; x < hiltX - 15; x += 10)
        {
            context.DrawLine(new Pen(GoldShadowBrush, 1.4), new Point(x, centerY - 4), new Point(x + 5, centerY + 4));
        }

        var guardPen = new Pen(GoldBrush, 5, lineCap: PenLineCap.Round);
        context.DrawLine(guardPen, new Point(hiltX - 8, centerY + 9), new Point(hiltX + 7, centerY - 16));
        context.DrawLine(new Pen(GoldHighlightBrush, 1.1), new Point(hiltX - 7, centerY + 8), new Point(hiltX + 8, centerY - 15));
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(30, 104, 157)),
            new Pen(GoldHighlightBrush, 1),
            new Point(hiltX, centerY - 4),
            3.4,
            3.4);

        var blade = new StreamGeometry();
        using (var path = blade.Open())
        {
            path.BeginFigure(new Point(hiltX + 5, centerY - 3.2), true);
            path.LineTo(new Point(header.Right - 17, centerY - 2.1));
            path.LineTo(new Point(header.Right - 4, centerY));
            path.LineTo(new Point(header.Right - 17, centerY + 2.1));
            path.LineTo(new Point(hiltX + 5, centerY + 3.2));
            path.EndFigure(true);
        }

        context.DrawGeometry(BladeBrush, new Pen(GoldShadowBrush, 0.8), blade);
        context.DrawLine(
            new Pen(new SolidColorBrush(Color.FromArgb(180, 250, 234, 191)), 0.65),
            new Point(hiltX + 9, centerY - 1),
            new Point(header.Right - 18, centerY - 0.4));
    }

    private static void DrawFrame(DrawingContext context, Rect header)
    {
        context.DrawLine(new Pen(GoldShadowBrush, 3), header.TopLeft, header.TopRight);
        context.DrawLine(new Pen(GoldHighlightBrush, 0.8), new Point(0, 2), new Point(header.Right, 2));
        context.DrawLine(new Pen(GoldShadowBrush, 2.5), new Point(0, header.Bottom - 2), new Point(header.Right, header.Bottom - 2));
        context.DrawLine(new Pen(GoldHighlightBrush, 0.7), new Point(0, header.Bottom - 4), new Point(header.Right, header.Bottom - 4));

        for (var x = 7d; x < header.Right; x += 96)
        {
            context.DrawEllipse(GoldBrush, new Pen(GoldHighlightBrush, 0.55), new Point(x, 3), 1.7, 1.7);
        }
    }
}
