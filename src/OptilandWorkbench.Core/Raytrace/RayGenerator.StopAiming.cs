using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public sealed partial class RayGenerator
{
    // Solve the actual two-dimensional stop map. Unit-gain shifts and independent
    // axis secants cannot represent inverted pupils or transverse coupling.
    private bool TryAimStopTarget(Vector3D baseOrigin, Vector3D fixedDirection,
        double wavelengthNanometers, int stopIndex, double targetX, double targetY,
        double x, double y, bool infinite, out Vector3D aimedOrigin,
        out Vector3D aimedDirection, out double residual)
    {
        var stop = _optic.SurfaceGroup.Items[stopIndex];
        var tolerance = CircularAperture.BoundaryTolerance(double.Hypot(targetX, targetY)) / 4;
        aimedOrigin = default;
        aimedDirection = default;
        residual = double.PositiveInfinity;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!Sample(x, y, out var point)) return false;
            var errorX = targetX - point.X;
            var errorY = targetY - point.Y;
            residual = double.Hypot(errorX, errorY);
            if (residual <= tolerance)
            {
                (aimedOrigin, aimedDirection) = Geometry(x, y);
                return true; // Return only this successfully traced, checked iterate.
            }
            var hx = 1e-6 * Math.Max(1, Math.Abs(x));
            var hy = 1e-6 * Math.Max(1, Math.Abs(y));
            if (!Sample(x + hx, y, out var px))
            {
                hx = -hx;
                if (!Sample(x + hx, y, out px)) return false;
            }
            if (!Sample(x, y + hy, out var py))
            {
                hy = -hy;
                if (!Sample(x, y + hy, out py)) return false;
            }
            var a = (px.X - point.X) / hx;
            var b = (py.X - point.X) / hy;
            var c = (px.Y - point.Y) / hx;
            var d = (py.Y - point.Y) / hy;
            var determinant = a * d - b * c;
            if (!double.IsFinite(determinant) || Math.Abs(determinant) <= 1e-24) return false;
            var dx = (d * errorX - b * errorY) / determinant;
            var dy = (a * errorY - c * errorX) / determinant;
            var improved = false;
            for (var backtrack = 0; backtrack < 12; backtrack++)
            {
                var scale = Math.ScaleB(1, -backtrack);
                var nextX = x + scale * dx;
                var nextY = y + scale * dy;
                if (!Sample(nextX, nextY, out var next)
                    || double.Hypot(targetX - next.X, targetY - next.Y) >= residual) continue;
                x = nextX;
                y = nextY;
                improved = true;
                break;
            }
            if (!improved) return false;
        }
        return false;

        (Vector3D Origin, Vector3D Direction) Geometry(double u, double v) => infinite
            ? (new Vector3D(u, v, baseOrigin.Z), fixedDirection)
            : (baseOrigin, Normalize(new Vector3D(u, v, 1)));

        bool Sample(double u, double v, out Vector3D point)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            point = default;
            if (!double.IsFinite(u) || !double.IsFinite(v)) return false;
            var (origin, direction) = Geometry(u, v);
            var sample = _optic.SequentialRayTracer.TraceToSurface(
                new RealRay(origin, direction, wavelengthNanometers), stopIndex);
            if (sample is null) return false;
            point = stop.CoordinateSystem.ToLocalPoint(sample.Position);
            return double.IsFinite(point.X) && double.IsFinite(point.Y);
        }
    }
}
