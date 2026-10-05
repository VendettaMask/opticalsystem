using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

public readonly record struct DirectionalSagResult(double Distance, Vector3D Point, Vector3D Direction);

/// <summary>Signed distance from a local reference point along a rotated local +Z axis.</summary>
public static class SurfaceDirectionalSag
{
    public static DirectionalSagResult Evaluate(OpticalSurface surface, int mode, Vector3D reference,
        double tiltXDegrees = 0, double tiltYDegrees = 0, double tiltZDegrees = 0)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (mode is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(mode), "TSAG Mode 必须为 0 或 1。");
        if (!GeometryIntersectionSolver.IsFinite(reference)
            || !double.IsFinite(tiltXDegrees) || !double.IsFinite(tiltYDegrees) || !double.IsFinite(tiltZDegrees))
            throw new ArgumentOutOfRangeException(nameof(reference), "参考坐标和测量倾角必须为有限数值。");
        // Reuse the formal coordinate convention; do not apply the surface's global pose twice.
        var frame = new CoordinateSystem(Vector3D.Zero, tiltXDegrees % 360, tiltYDegrees % 360, tiltZDegrees % 360);
        var direction = frame.ToGlobalDirection(new(0, 0, 1));
        var boundary = surface.SemiDiameter + surface.ChipZone;
        var edge = 0.0;
        if (mode == 0)
        {
            if (surface.Geometry is not (PlaneGeometry or StandardGeometry or EvenAsphereGeometry or OddAsphereGeometry))
                throw new NotSupportedException("平边矢高当前支持旋转对称标准面及偶/奇次非球面。");
            edge = surface.Geometry.Sag(boundary, 0);
            if (!double.IsFinite(boundary) || !double.IsFinite(edge))
                throw new InvalidOperationException("净半口径加延伸区超出面形的有限定义域。");
        }

        double Sag(double x, double y)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            return mode == 0 && double.Hypot(x, y) > boundary ? edge : surface.Geometry.Sag(x, y);
        }
        Vector3D Normal(Vector3D point) => mode == 0 && double.Hypot(point.X, point.Y) > boundary
            ? new(0, 0, 1) : surface.Geometry.SurfaceNormal(point);
        IntersectionResult Intersect(Vector3D rayDirection)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            return mode == 0
                ? GeometryIntersectionSolver.Solve(reference, rayDirection, Sag, Normal)
                : surface.Geometry.DistanceToIntersection(reference, rayDirection);
        }

        var forward = Intersect(direction);
        var backward = Intersect(-direction);
        if (!forward.IsHit && !backward.IsHit)
            throw new InvalidOperationException($"测量直线没有已收敛的有限面形交点（正向 {forward.Status}，反向 {backward.Status}）。");
        if (forward.IsHit && backward.IsHit && forward.Distance > 0 && backward.Distance > 0
            && Math.Abs(forward.Distance - backward.Distance) <= 1e-10 * Math.Max(1, forward.Distance))
            throw new InvalidOperationException("参考点两侧存在等距离交点，无法唯一确定矢高。");
        var useForward = forward.IsHit && (!backward.IsHit || forward.Distance <= backward.Distance);
        var hit = useForward ? forward : backward;
        if (hit.Status == IntersectionStatus.Tangent)
            throw new InvalidOperationException("测量方向与面形接近相切，矢高不能稳定求解。");
        return new(useForward ? hit.Distance : -hit.Distance, hit.Point, direction);
    }
}
