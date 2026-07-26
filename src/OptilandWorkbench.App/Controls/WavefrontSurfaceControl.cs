using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Controls;

public sealed class WavefrontSurfaceControl : Control
{
    public AnalysisSeriesDto? Series { get; init; }

    public double RotationDegrees { get; init; }

    public double DisplayScale { get; init; } = 1;

    public string DisplayAs { get; init; } = "表面";

    public string ColorBarTitle { get; init; } = "波前函数";

    public string ColorBarUnit { get; init; } = "波";

    public string XAxisLabel { get; init; } = "X 光瞳（归一化）";

    public string YAxisLabel { get; init; } = "Y 光瞳（归一化）";

    public double? ValueMinimum { get; init; }

    public double? ValueMaximum { get; init; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.White, null, Bounds);
        if (Series is null || Bounds.Width < 260 || Bounds.Height < 220)
        {
            return;
        }

        var samples = Series.Points
            .Where(point => point.Value.HasValue && double.IsFinite(point.Value.Value))
            .ToArray();
        if (samples.Length < 4)
        {
            return;
        }

        var xs = samples.Select(point => point.X).Distinct().Order().ToArray();
        var ys = samples.Select(point => point.Y).Distinct().Order().ToArray();
        var byCoordinate = samples.ToDictionary(
            point => (X: CoordinateKey(point.X), Y: CoordinateKey(point.Y)));
        var minimum = ValueMinimum ?? samples.Min(point => point.Value!.Value);
        var maximum = ValueMaximum ?? samples.Max(point => point.Value!.Value);
        var range = Math.Max(1e-12, maximum - minimum);
        var plot = new Rect(28, 22, Math.Max(1, Bounds.Width - 150), Math.Max(1, Bounds.Height - 62));
        var xCenter = (xs[0] + xs[^1]) / 2;
        var yCenter = (ys[0] + ys[^1]) / 2;
        var xRadius = Math.Max(1e-12, (xs[^1] - xs[0]) / 2);
        var yRadius = Math.Max(1e-12, (ys[^1] - ys[0]) / 2);
        var angle = (RotationDegrees + 35) * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var spatialScale = Math.Min(plot.Width / 2.35, plot.Height / 2.05);
        var center = new Point(plot.Center.X, plot.Center.Y + (plot.Height * 0.14));
        var surfaceMode = !DisplayAs.Contains("等高", StringComparison.Ordinal);

        ProjectedPoint Project(AnalysisPointDto point)
        {
            var x = (point.X - xCenter) / xRadius;
            var y = (point.Y - yCenter) / yRadius;
            var rotatedX = (x * cos) - (y * sin);
            var rotatedY = (x * sin) + (y * cos);
            var normalizedValue = (point.Value!.Value - minimum) / range;
            var lift = surfaceMode
                ? normalizedValue * Math.Clamp(DisplayScale, 0.01, 100) * spatialScale * 0.62
                : 0;
            return new ProjectedPoint(
                new Point(
                    center.X + (rotatedX * spatialScale * 0.78),
                    center.Y + (rotatedY * spatialScale * 0.34) - lift),
                rotatedY,
                normalizedValue);
        }

        var triangles = new List<SurfaceTriangle>();
        for (var row = 0; row < ys.Length - 1; row++)
        {
            for (var column = 0; column < xs.Length - 1; column++)
            {
                if (!TryPoint(xs[column], ys[row], out var p00)
                    || !TryPoint(xs[column + 1], ys[row], out var p10)
                    || !TryPoint(xs[column], ys[row + 1], out var p01)
                    || !TryPoint(xs[column + 1], ys[row + 1], out var p11))
                {
                    continue;
                }

                AddTriangle(p00, p10, p11);
                AddTriangle(p00, p11, p01);
            }
        }

        var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(40, 20, 20, 20)), 0.35);
        foreach (var triangle in triangles.OrderBy(item => item.Depth))
        {
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(triangle.A, true);
                stream.LineTo(triangle.B);
                stream.LineTo(triangle.C);
                stream.EndFigure(true);
            }

            context.DrawGeometry(
                new SolidColorBrush(JetColor(triangle.Value)),
                surfaceMode ? edgePen : null,
                geometry);
        }

        DrawAxes(context, plot, center, spatialScale, cos, sin, surfaceMode);
        DrawColorBar(context, minimum, maximum);

        bool TryPoint(double x, double y, out AnalysisPointDto point)
        {
            if (byCoordinate.TryGetValue((CoordinateKey(x), CoordinateKey(y)), out var found))
            {
                point = found;
                return true;
            }

            point = null!;
            return false;
        }

        void AddTriangle(AnalysisPointDto first, AnalysisPointDto second, AnalysisPointDto third)
        {
            var a = Project(first);
            var b = Project(second);
            var c = Project(third);
            triangles.Add(new SurfaceTriangle(
                a.Screen,
                b.Screen,
                c.Screen,
                (a.Depth + b.Depth + c.Depth) / 3,
                (a.Value + b.Value + c.Value) / 3));
        }
    }

    private void DrawColorBar(DrawingContext context, double minimum, double maximum)
    {
        var bar = new Rect(Math.Max(8, Bounds.Width - 92), 72, 28, Math.Max(80, Bounds.Height - 175));
        const int strips = 96;
        for (var index = 0; index < strips; index++)
        {
            var fraction = index / (double)(strips - 1);
            var y = bar.Bottom - ((index + 1) * bar.Height / strips);
            context.DrawRectangle(
                new SolidColorBrush(JetColor(fraction)),
                null,
                new Rect(bar.Left, y, bar.Width, (bar.Height / strips) + 1));
        }

        context.DrawRectangle(null, new Pen(Brushes.Black, 0.7), bar);
        var title = Text(ColorBarTitle, 12, Brushes.Black);
        context.DrawText(title, new Point(bar.Center.X - (title.Width / 2), bar.Top - title.Height - 8));
        for (var index = 0; index <= 8; index++)
        {
            var fraction = index / 8.0;
            var value = minimum + ((maximum - minimum) * fraction);
            var label = Text(value.ToString("0.####", CultureInfo.InvariantCulture), 10, Brushes.Black);
            var y = bar.Bottom - (fraction * bar.Height) - (label.Height / 2);
            context.DrawText(label, new Point(bar.Right + 6, y));
        }

        if (!string.IsNullOrWhiteSpace(ColorBarUnit))
        {
            var unit = Text(ColorBarUnit, 11, Brushes.Black);
            context.DrawText(unit, new Point(bar.Right + 6, bar.Bottom + 8));
        }
    }

    private void DrawAxes(
        DrawingContext context,
        Rect plot,
        Point center,
        double scale,
        double cos,
        double sin,
        bool surfaceMode)
    {
        Point BasePoint(double x, double y)
        {
            var rotatedX = (x * cos) - (y * sin);
            var rotatedY = (x * sin) + (y * cos);
            return new Point(
                center.X + (rotatedX * scale * 0.78),
                center.Y + (rotatedY * scale * 0.34));
        }

        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 45)), 0.8);
        var origin = BasePoint(-1, -1);
        var xEnd = BasePoint(1, -1);
        var yEnd = BasePoint(-1, 1);
        context.DrawLine(axisPen, origin, xEnd);
        context.DrawLine(axisPen, origin, yEnd);
        if (surfaceMode)
        {
            context.DrawLine(axisPen, origin, new Point(origin.X, Math.Max(plot.Top, origin.Y - (scale * 0.62))));
        }

        var xLabel = Text(XAxisLabel, 10, Brushes.Black);
        var yLabel = Text(YAxisLabel, 10, Brushes.Black);
        context.DrawText(xLabel, new Point(xEnd.X - xLabel.Width, xEnd.Y + 7));
        context.DrawText(yLabel, new Point(yEnd.X, yEnd.Y + 7));
    }

    private static long CoordinateKey(double value) => (long)Math.Round(value * 1_000_000_000);

    private static Color JetColor(double value)
    {
        value = Math.Clamp(value, 0, 1);
        var red = Math.Clamp(1.5 - Math.Abs((4 * value) - 3), 0, 1);
        var green = Math.Clamp(1.5 - Math.Abs((4 * value) - 2), 0, 1);
        var blue = Math.Clamp(1.5 - Math.Abs((4 * value) - 1), 0, 1);
        return Color.FromRgb((byte)(red * 255), (byte)(green * 255), (byte)(blue * 255));
    }

    private static FormattedText Text(string value, double size, IBrush brush)
    {
        return new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            DisplayTypography.Typeface(),
            DisplayTypography.Scale(size),
            brush);
    }

    private readonly record struct ProjectedPoint(Point Screen, double Depth, double Value);

    private readonly record struct SurfaceTriangle(
        Point A,
        Point B,
        Point C,
        double Depth,
        double Value);
}
