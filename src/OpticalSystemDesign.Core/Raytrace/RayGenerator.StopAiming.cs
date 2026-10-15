using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Raytrace;

public sealed partial class RayGenerator
{
    // Solve the actual two-dimensional stop map. Unit-gain shifts and independent
    // axis secants cannot represent inverted pupils or transverse coupling.
    private bool TryAimStopTarget(Vector3D baseOrigin, Vector3D fixedDirection,
        double wavelengthNanometers, int stopIndex, double targetX, double targetY,
        double x, double y, bool infinite, out Vector3D aimedOrigin,
        out Vector3D aimedDirection, out double residual, Vector3D? infiniteOriginGuess = null)
    {
        var stop = _optic.SurfaceGroup.Items[stopIndex];
        // A chief target is zero but its optical stop is not: scale the solver
        // to the same stop boundary used for physical clipping, including at
        // the axis. Keep the residual strictly inside that boundary budget.
        var tolerance = CircularAperture.BoundaryTolerance(
            Math.Max(stop.SemiDiameter, double.Hypot(targetX, targetY))) / 4;
        aimedOrigin = default;
        aimedDirection = default;
        residual = double.PositiveInfinity;
        // Near a transverse field, shifts parallel to the incident ray cannot
        // control the stop intercept. Solve on a plane with a finite transverse
        // Jacobian instead of forcing the world-Z plane through 90 degrees.
        var fixedAxis = !infinite || Math.Abs(fixedDirection.Z) >= .5 ? 2
            : Math.Abs(fixedDirection.Y) >= Math.Abs(fixedDirection.X) ? 1 : 0;
        var plane = baseOrigin;
        if (infinite)
        {
            var guess = infiniteOriginGuess ?? new Vector3D(x, y, baseOrigin.Z);
            if (fixedAxis != 2)
            {
                var first = _optic.SurfaceGroup.Items[1].CoordinateSystem.Origin;
                var extent = InfiniteLaunchExtent(EntrancePupilRadius());
                plane = fixedAxis == 1 ? new Vector3D(first.X, first.Y - Math.CopySign(extent, fixedDirection.Y), first.Z)
                    : new Vector3D(first.X - Math.CopySign(extent, fixedDirection.X), first.Y, first.Z);
            }
            var coordinate = fixedAxis == 2 ? plane.Z : fixedAxis == 1 ? plane.Y : plane.X;
            var delta = coordinate - (fixedAxis == 2 ? guess.Z : fixedAxis == 1 ? guess.Y : guess.X);
            guess += fixedDirection * (delta / (fixedAxis == 2 ? fixedDirection.Z : fixedAxis == 1 ? fixedDirection.Y : fixedDirection.X));
            (x, y) = fixedAxis == 2 ? (guess.X, guess.Y) : fixedAxis == 1 ? (guess.X, guess.Z) : (guess.Y, guess.Z);
        }
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
            ? (fixedAxis == 2 ? new Vector3D(u, v, plane.Z)
                : fixedAxis == 1 ? new Vector3D(u, plane.Y, v) : new Vector3D(plane.X, u, v), fixedDirection)
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
