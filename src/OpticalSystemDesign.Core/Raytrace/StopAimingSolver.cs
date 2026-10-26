using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Raytrace;

internal interface IStopMap
{
    bool Sample(double x, double y, out Vector3D point);
}

internal static class StopAimingSolver
{
    internal static double Hypot(double x, double y)
    {
        var a = Math.Abs(x); var b = Math.Abs(y); var hi = Math.Max(a, b); var lo = Math.Min(a, b);
        if (double.IsInfinity(hi)) return double.PositiveInfinity;
        return hi == 0 ? 0 : hi * Math.Sqrt(1 + (lo / hi) * (lo / hi));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static bool Solve<T>(ref T map, double targetX, double targetY, double tolerance,
        ref double x, ref double y, out double residual) where T : struct, IStopMap
    {
        residual = double.PositiveInfinity;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            if (!map.Sample(x, y, out var point)) return false;
            var ex = targetX - point.X; var ey = targetY - point.Y;
            residual = Hypot(ex, ey);
            if (residual <= tolerance) return true;
            var hx = 1e-6 * Math.Max(1, Math.Abs(x)); var hy = 1e-6 * Math.Max(1, Math.Abs(y));
            if (!map.Sample(x + hx, y, out var px)) { hx = -hx; if (!map.Sample(x + hx, y, out px)) return false; }
            if (!map.Sample(x, y + hy, out var py)) { hy = -hy; if (!map.Sample(x, y + hy, out py)) return false; }
            var a = (px.X - point.X) / hx; var b = (py.X - point.X) / hy;
            var c = (px.Y - point.Y) / hx; var d = (py.Y - point.Y) / hy;
            var determinant = a * d - b * c;
            if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-24) return false;
            var dx = (d * ex - b * ey) / determinant; var dy = (a * ey - c * ex) / determinant;
            var improved = false; var scale = 1d;
            for (var backtrack = 0; backtrack < 12; backtrack++, scale *= .5)
            {
                var nextX = x + scale * dx; var nextY = y + scale * dy;
                if (!map.Sample(nextX, nextY, out var next) || Hypot(targetX - next.X, targetY - next.Y) >= residual) continue;
                x = nextX; y = nextY; improved = true; break;
            }
            if (!improved && (residual > 64 * tolerance || !TryRepresentableImprovement(ref map, targetX, targetY,
                    tolerance, dx != 0, dy != 0, ref x, ref y, ref residual))) return false;
            if (!improved && residual <= tolerance) return true;
        }
        return false;
    }

    private static bool TryRepresentableImprovement<T>(ref T map, double targetX, double targetY,
        double tolerance, bool varyX, bool varyY, ref double x, ref double y, ref double residual) where T : struct, IStopMap
    {
        // Rounded intersections can make the stop map locally non-monotone at
        // machine precision. After Newton stalls, inspect a bounded stencil of
        // representable launches. Every candidate must trace successfully and
        // improve the actual residual; acceptance keeps the original tolerance.
        // This is finishing recovery only (within 64 tolerance budgets), not a
        // search over missed or far-from-converged rays. Preserve stationary axes.
        const int radius = 32;
        var radiusX = varyX ? radius : 0; var radiusY = varyY ? radius : 0;
        var bestX = x; var bestY = y; var bestResidual = residual;
        for (var i = -radiusX; i <= radiusX; i++)
            for (var j = -radiusY; j <= radiusY; j++)
            {
                var nextX = Adjacent(x, i); var nextY = Adjacent(y, j);
                if (i == 0 && j == 0 || !double.IsFinite(nextX) || !double.IsFinite(nextY)
                    || !map.Sample(nextX, nextY, out var point)) continue;
                var candidate = Hypot(targetX - point.X, targetY - point.Y);
                if (!(candidate < bestResidual)) continue;
                bestX = nextX; bestY = nextY; bestResidual = candidate;
                if (candidate <= tolerance)
                { x = bestX; y = bestY; residual = bestResidual; return true; }
            }
        if (!(bestResidual < residual)) return false;
        x = bestX; y = bestY; residual = bestResidual;
        return true;
    }

    // Signed, ordered IEEE indices also cross zero correctly. BitConverter is
    // supported by the CUDA compiler; keep this shared solver allocation-free.
    private static double Adjacent(double value, int offset)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var magnitude = bits & long.MaxValue;
        var index = (bits < 0 ? -magnitude : magnitude) + offset;
        return BitConverter.Int64BitsToDouble(index < 0 ? long.MinValue | -index : index);
    }
}
