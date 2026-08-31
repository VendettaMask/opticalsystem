using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OptilandWorkbench.App.Theming;

internal sealed class IsekaiThemeDecorationRenderer : IThemeDecorationRenderer
{
    public static IsekaiThemeDecorationRenderer Instance { get; } = new();

    private static readonly IImmutableBrush LeatherBrush =
        new LinearGradientBrush
        {
            StartPoint = RelativePoint.TopLeft,
            EndPoint = RelativePoint.BottomRight,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(48, 36, 24), 0),
                new GradientStop(Color.FromRgb(19, 17, 15), 0.52),
                new GradientStop(Color.FromRgb(38, 28, 19), 1)
            }
        }.ToImmutable();

    private static readonly IImmutableBrush BladeBrush =
        new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(226, 213, 177), 0),
                new GradientStop(Color.FromRgb(95, 91, 80), 0.48),
                new GradientStop(Color.FromRgb(207, 188, 143), 1)
            }
        }.ToImmutable();

    private static readonly IImmutableBrush GoldBrush = Brush(Color.FromRgb(181, 132, 54));
    private static readonly IImmutableBrush GoldHighlightBrush = Brush(Color.FromArgb(205, 239, 198, 103));
    private static readonly IImmutableBrush GoldShadowBrush = Brush(Color.FromRgb(84, 57, 25));
    private static readonly IImmutableBrush TextureBrush = Brush(Color.FromArgb(36, 231, 194, 119));
    private static readonly IImmutableBrush LowerShadeBrush = Brush(Color.FromArgb(112, 5, 5, 4));
    private static readonly IImmutableBrush HiltBrush = Brush(Color.FromRgb(48, 25, 14));
    private static readonly IImmutableBrush HiltHighlightBrush = Brush(Color.FromRgb(153, 91, 38));
    private static readonly IImmutableBrush TabOutlineBrush = Brush(Color.FromArgb(112, 205, 155, 68));
    private static readonly IImmutableBrush TabInnerBrush = Brush(Color.FromArgb(95, 42, 28, 15));
    private static readonly IImmutableBrush GemBrush = Brush(Color.FromRgb(30, 104, 157));
    private static readonly IImmutableBrush BladeHighlightBrush = Brush(Color.FromArgb(180, 250, 234, 191));

    private static readonly ImmutablePen WorkspaceOuterPen = Pen(GoldShadowBrush, 1.8);
    private static readonly ImmutablePen WorkspaceInnerPen = Pen(GoldHighlightBrush, 0.55);
    private static readonly ImmutablePen RunePen = Pen(
        GoldBrush,
        1.1,
        lineCap: PenLineCap.Square,
        lineJoin: PenLineJoin.Miter);
    private static readonly ImmutablePen TexturePen = Pen(TextureBrush, 0.65);
    private static readonly ImmutablePen TabOutlinePen = Pen(TabOutlineBrush, 0.8);
    private static readonly ImmutablePen TabInnerPen = Pen(TabInnerBrush, 1.2);
    private static readonly ImmutablePen PommelPen = Pen(GoldHighlightBrush, 0.8);
    private static readonly ImmutablePen HiltPen = Pen(HiltBrush, 8);
    private static readonly ImmutablePen HiltHighlightPen = Pen(HiltHighlightBrush, 1.2);
    private static readonly ImmutablePen HiltWrapPen = Pen(GoldShadowBrush, 1.4);
    private static readonly ImmutablePen GuardPen = Pen(GoldBrush, 5, lineCap: PenLineCap.Round);
    private static readonly ImmutablePen GuardHighlightPen = Pen(GoldHighlightBrush, 1.1);
    private static readonly ImmutablePen GemPen = Pen(GoldHighlightBrush, 1);
    private static readonly ImmutablePen BladeOutlinePen = Pen(GoldShadowBrush, 0.8);
    private static readonly ImmutablePen BladeHighlightPen = Pen(BladeHighlightBrush, 0.65);
    private static readonly ImmutablePen HeaderTopPen = Pen(GoldShadowBrush, 3);
    private static readonly ImmutablePen HeaderTopHighlightPen = Pen(GoldHighlightBrush, 0.8);
    private static readonly ImmutablePen HeaderBottomPen = Pen(GoldShadowBrush, 2.5);
    private static readonly ImmutablePen HeaderBottomHighlightPen = Pen(GoldHighlightBrush, 0.7);
    private static readonly ImmutablePen RivetPen = Pen(GoldHighlightBrush, 0.55);

    private double _bladeRight = double.NaN;
    private double _bladeHiltX = double.NaN;
    private double _bladeCenterY = double.NaN;
    private StreamGeometry? _bladeGeometry;
    private int _bladeGeometryBuildCount;

    private IsekaiThemeDecorationRenderer()
    {
    }

    internal int BladeGeometryBuildCount => _bladeGeometryBuildCount;

    private static ImmutableSolidColorBrush Brush(Color color) => new(color);

    private static ImmutablePen Pen(
        IImmutableBrush brush,
        double thickness,
        PenLineCap lineCap = PenLineCap.Flat,
        PenLineJoin lineJoin = PenLineJoin.Miter) =>
        new(brush, thickness, null, lineCap, lineJoin);

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

    private void DrawRibbon(DrawingContext context, Rect bounds)
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
        context.DrawRectangle(null, WorkspaceOuterPen, outer);
        if (inner.Width > 0 && inner.Height > 0)
        {
            context.DrawRectangle(null, WorkspaceInnerPen, inner);
        }

        const double corner = 15;
        DrawCorner(context, RunePen, outer.TopLeft, new Vector(1, 1), corner);
        DrawCorner(context, RunePen, outer.TopRight, new Vector(-1, 1), corner);
        DrawCorner(context, RunePen, outer.BottomLeft, new Vector(1, -1), corner);
        DrawCorner(context, RunePen, outer.BottomRight, new Vector(-1, -1), corner);
    }

    private static void DrawCorner(
        DrawingContext context,
        IPen pen,
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
        for (var x = 7d; x < header.Width; x += 23)
        {
            var offset = ((int)(x / 23) % 3) * 3;
            context.DrawLine(
                TexturePen,
                new Point(x, 5 + offset),
                new Point(Math.Min(header.Right, x + 13), 8 + offset));
        }

        context.DrawRectangle(LowerShadeBrush, null, new Rect(0, header.Bottom - 12, header.Width, 12));
    }

    private static void DrawTabBays(DrawingContext context, Rect header)
    {
        const double startX = 126;
        const double bayWidth = 112;

        for (var left = startX; left < header.Right - 8; left += bayWidth)
        {
            var right = Math.Min(header.Right - 8, left + bayWidth);
            context.DrawLine(TabOutlinePen, new Point(left, 4), new Point(right, 4));
            context.DrawLine(TabOutlinePen, new Point(left, 4), new Point(left, header.Bottom - 7));
            context.DrawLine(TabInnerPen, new Point(left + 2, 6), new Point(right - 2, 6));
        }
    }

    private void DrawSword(DrawingContext context, Rect header)
    {
        var centerY = header.Bottom - 7.5;
        var hiltX = Math.Min(102, Math.Max(58, header.Width * 0.075));

        context.DrawEllipse(GoldShadowBrush, PommelPen, new Point(10, centerY), 7, 7);
        context.DrawLine(HiltPen, new Point(16, centerY), new Point(hiltX - 13, centerY));
        context.DrawLine(HiltHighlightPen, new Point(18, centerY - 2), new Point(hiltX - 15, centerY - 2));
        for (var x = 22d; x < hiltX - 15; x += 10)
        {
            context.DrawLine(HiltWrapPen, new Point(x, centerY - 4), new Point(x + 5, centerY + 4));
        }

        context.DrawLine(GuardPen, new Point(hiltX - 8, centerY + 9), new Point(hiltX + 7, centerY - 16));
        context.DrawLine(GuardHighlightPen, new Point(hiltX - 7, centerY + 8), new Point(hiltX + 8, centerY - 15));
        context.DrawEllipse(
            GemBrush,
            GemPen,
            new Point(hiltX, centerY - 4),
            3.4,
            3.4);

        var blade = CachedBladeGeometry(header.Right, hiltX, centerY);
        context.DrawGeometry(BladeBrush, BladeOutlinePen, blade);
        context.DrawLine(
            BladeHighlightPen,
            new Point(hiltX + 9, centerY - 1),
            new Point(header.Right - 18, centerY - 0.4));
    }

    private StreamGeometry CachedBladeGeometry(double right, double hiltX, double centerY)
    {
        if (_bladeGeometry is not null
            && _bladeRight.Equals(right)
            && _bladeHiltX.Equals(hiltX)
            && _bladeCenterY.Equals(centerY))
        {
            return _bladeGeometry;
        }

        var blade = new StreamGeometry();
        using (var path = blade.Open())
        {
            path.BeginFigure(new Point(hiltX + 5, centerY - 3.2), true);
            path.LineTo(new Point(right - 17, centerY - 2.1));
            path.LineTo(new Point(right - 4, centerY));
            path.LineTo(new Point(right - 17, centerY + 2.1));
            path.LineTo(new Point(hiltX + 5, centerY + 3.2));
            path.EndFigure(true);
        }

        _bladeRight = right;
        _bladeHiltX = hiltX;
        _bladeCenterY = centerY;
        _bladeGeometry = blade;
        _bladeGeometryBuildCount++;
        return blade;
    }

    private static void DrawFrame(DrawingContext context, Rect header)
    {
        context.DrawLine(HeaderTopPen, header.TopLeft, header.TopRight);
        context.DrawLine(HeaderTopHighlightPen, new Point(0, 2), new Point(header.Right, 2));
        context.DrawLine(HeaderBottomPen, new Point(0, header.Bottom - 2), new Point(header.Right, header.Bottom - 2));
        context.DrawLine(HeaderBottomHighlightPen, new Point(0, header.Bottom - 4), new Point(header.Right, header.Bottom - 4));

        for (var x = 7d; x < header.Right; x += 96)
        {
            context.DrawEllipse(GoldBrush, RivetPen, new Point(x, 3), 1.7, 1.7);
        }
    }
}
