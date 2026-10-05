namespace OptilandWorkbench.Core.Services;

/// <summary>Deterministic randomized incremental enclosing circle, with a bounded work budget.</summary>
internal static class MinimumEnclosingCircle
{
    internal static (double X, double Y) Center(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0) throw new InvalidOperationException("空光斑没有最小包围圆。");
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        var origin = (X: minX / 2 + maxX / 2, Y: minY / 2 + maxY / 2);
        var scale = Math.Max(Math.Max(Math.Abs(minX - origin.X), Math.Abs(maxX - origin.X)),
            Math.Max(Math.Abs(minY - origin.Y), Math.Abs(maxY - origin.Y)));
        if (scale == 0) return origin;
        if (!double.IsFinite(scale)) throw new InvalidOperationException("光斑坐标范围溢出。");
        var p = points.Select(v => (X: (v.X - origin.X) / scale, Y: (v.Y - origin.Y) / scale)).ToArray();
        var random = new Random(17041);
        for (var i = p.Length - 1; i > 0; i--) { var j = random.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
        var budget = 20_000_000;
        var circle = new Circle(0, 0, -1);
        for (var i = 0; i < p.Length; i++)
        {
            if (Contains(circle, p[i])) continue;
            circle = new(p[i].X, p[i].Y, 0);
            for (var j = 0; j < i; j++)
            {
                if (Contains(circle, p[j])) continue;
                circle = Diameter(p[i], p[j]);
                // With these two boundary points fixed, choose the extremal
                // circumcircle on each side of their chord.
                Circle? left = null, right = null;
                for (var k = 0; k < j; k++)
                {
                    if (Contains(circle, p[k])) continue;
                    var cross = Cross(p[i], p[j], p[k]);
                    if (cross == 0) continue; // collinear points lie on a diameter extremum
                    var candidate = Circumcircle(p[i], p[j], p[k]);
                    var side = Cross(p[i], p[j], (candidate.X, candidate.Y));
                    if (cross > 0 && (left is null || side > Cross(p[i], p[j], (left.Value.X, left.Value.Y)))) left = candidate;
                    if (cross < 0 && (right is null || side < Cross(p[i], p[j], (right.Value.X, right.Value.Y)))) right = candidate;
                }
                if (left is not null || right is not null)
                    circle = left is null ? right!.Value : right is null || left.Value.Radius <= right.Value.Radius ? left.Value : right.Value;
            }
        }
        if (p.Any(point => !Contains(circle, point))) throw new InvalidOperationException("最小包围圆未通过包含性检查。");
        return (origin.X + circle.X * scale, origin.Y + circle.Y * scale);

        bool Contains(Circle c, (double X, double Y) point)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (--budget < 0) throw new InvalidOperationException("最小包围圆超过计算预算。");
            return c.Radius >= 0 && double.Hypot(point.X - c.X, point.Y - c.Y) <= c.Radius + 2e-14;
        }
    }
    private static Circle Diameter((double X, double Y) a, (double X, double Y) b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2, double.Hypot(a.X - b.X, a.Y - b.Y) / 2);
    private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static Circle Circumcircle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var bx = b.X - a.X; var by = b.Y - a.Y; var cx = c.X - a.X; var cy = c.Y - a.Y;
        var determinant = 2 * (bx * cy - by * cx);
        var bs = bx * bx + by * by; var cs = cx * cx + cy * cy;
        var x = a.X + (cy * bs - by * cs) / determinant;
        var y = a.Y + (bx * cs - cx * bs) / determinant;
        var radius = double.Hypot(x - a.X, y - a.Y);
        if (!double.IsFinite(radius)) throw new InvalidOperationException("最小包围圆出现退化几何。");
        return new(x, y, radius);
    }
    private readonly record struct Circle(double X, double Y, double Radius);
}
