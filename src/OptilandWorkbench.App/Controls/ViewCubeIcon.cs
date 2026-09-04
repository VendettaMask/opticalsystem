using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.App.Services;

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

public sealed class ViewCubeIcon : Control
{
    public ViewCubeIcon(ViewCubeFace face)
    {
        Face = face;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public ViewCubeFace Face { get; }

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

        var palette = CreatePalette();
        var (topBrush, leftBrush, rightBrush) = BrushesForFace(palette);
        DrawPolygon(context, top, topBrush, palette.Outline);
        DrawPolygon(context, left, leftBrush, palette.Outline);
        DrawPolygon(context, right, rightBrush, palette.Outline);
    }

    private ViewCubePalette CreatePalette()
    {
        var neutral = ThemeColor(ThemeResourceBindings.SceneOrientationFill);
        var highlight = ThemeColor(ThemeResourceBindings.SelectionBackground);
        return new ViewCubePalette(
            new SolidColorBrush(Shade(neutral, 0.10)),
            new SolidColorBrush(Shade(neutral, -0.03)),
            new SolidColorBrush(Shade(neutral, -0.14)),
            new SolidColorBrush(Shade(highlight, 0.14)),
            new SolidColorBrush(Shade(highlight, -0.02)),
            new SolidColorBrush(Shade(highlight, -0.16)),
            new Pen(ThemeBrush(ThemeResourceBindings.SceneOrientationBorder), 1));
    }

    private (IBrush Top, IBrush Left, IBrush Right) BrushesForFace(ViewCubePalette palette) => Face switch
    {
        ViewCubeFace.Isometric => (palette.HighlightTop, palette.HighlightLeft, palette.HighlightRight),
        ViewCubeFace.Top or ViewCubeFace.Bottom => (palette.HighlightTop, palette.NeutralLeft, palette.NeutralRight),
        ViewCubeFace.Front or ViewCubeFace.Back => (palette.NeutralTop, palette.HighlightLeft, palette.NeutralRight),
        ViewCubeFace.Left or ViewCubeFace.Right => (palette.NeutralTop, palette.NeutralLeft, palette.HighlightRight),
        _ => (palette.NeutralTop, palette.NeutralLeft, palette.NeutralRight)
    };

    private IBrush ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Transparent;

    private Color ThemeColor(string key) =>
        ThemeBrush(key) is ISolidColorBrush solid
            ? solid.Color
            : Colors.Transparent;

    private static Color Shade(Color color, double amount) => new(
        color.A,
        Component(color.R, amount),
        Component(color.G, amount),
        Component(color.B, amount));

    private static byte Component(byte value, double amount)
    {
        var target = amount >= 0 ? byte.MaxValue : byte.MinValue;
        var adjusted = value + ((target - value) * Math.Abs(amount));
        return (byte)Math.Clamp(Math.Round(adjusted), byte.MinValue, byte.MaxValue);
    }

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

    private static void DrawPolygon(DrawingContext context, IReadOnlyList<Point> points, IBrush fill, Pen outline)
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

        context.DrawGeometry(fill, outline, geometry);
    }

    private sealed record ViewCubePalette(
        IBrush NeutralTop,
        IBrush NeutralLeft,
        IBrush NeutralRight,
        IBrush HighlightTop,
        IBrush HighlightLeft,
        IBrush HighlightRight,
        Pen Outline);
}
