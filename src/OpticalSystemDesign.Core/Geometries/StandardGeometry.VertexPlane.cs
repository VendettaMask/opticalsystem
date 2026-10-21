using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

public sealed partial class StandardGeometry
{
    private bool TryVertexPlaneIntersection(Vector3D origin, Vector3D direction, out IntersectionResult result)
    {
        result = default;
        // The general quadratic below also accepts non-unit and transverse
        // directions. This vertex-plane form retains a normalized ray's
        // analytic incidence and avoids subtracting nearby quadratic roots.
        if (!GeometryIntersectionSolver.IsFinite(origin)
            || !GeometryIntersectionSolver.IsFiniteNonZero(direction)
            || Math.Abs(direction.Z) < 1e-12 || Math.Abs(direction.Length - 1) > 1e-14)
            return false;
        var toPlane = -origin.Z / direction.Z;
        var plane = new Vector3D(origin.X + direction.X * toPlane, origin.Y + direction.Y * toPlane, 0);
        var curvature = 1 / Radius;
        var radialSquared = plane.X * plane.X + plane.Y * plane.Y;
        var b = direction.Z - curvature * (plane.X * direction.X + plane.Y * direction.Y);
        var quadratic = 1 + Conic * direction.Z * direction.Z;
        var incidenceSquared = b * b - quadratic * curvature * curvature * radialSquared;
        if (!double.IsFinite(incidenceSquared) || incidenceSquared < 0) return false;
        var denominator = b + Math.CopySign(Math.Sqrt(incidenceSquared), b);
        if (denominator == 0) return false;
        var sagDistance = curvature * radialSquared / denominator;
        var distance = toPlane + sagDistance;
        if (!double.IsFinite(distance) || distance < -ConicDomainTolerance) return false;
        var point = plane + direction * sagDistance;
        var verified = GeometryIntersectionSolver.CreateVerifiedResult(point, direction, 0, Sag, SurfaceNormal, 0);
        if (!verified.IsHit) return false;
        // An oblique line can cross a spherical sag cap twice. The stable
        // small root is not necessarily the first forward hit from the launch
        // plane. Check its conjugate without changing the usual unique hit.
        // General conics retain their vertex-associated branch, including the
        // existing virtual continuation used by Gaussian pupil integrals.
        if (Conic == 0)
        {
            var otherSagDistance = denominator / (quadratic * curvature);
            var otherDistance = toPlane + otherSagDistance;
            if (double.IsFinite(otherDistance) && otherDistance >= -ConicDomainTolerance
                && otherDistance < distance)
            {
                var otherPoint = plane + direction * otherSagDistance;
                var other = GeometryIntersectionSolver.CreateVerifiedResult(otherPoint, direction, 0, Sag, SurfaceNormal, 0);
                if (other.IsHit) { verified = other; distance = otherDistance; }
            }
        }
        result = verified with
        {
            Distance = Math.Max(0, distance),
            IncidenceCosineSquared = Conic == 0 ? Math.Clamp(incidenceSquared, 0, 1) : null
        };
        return true;
    }
}
