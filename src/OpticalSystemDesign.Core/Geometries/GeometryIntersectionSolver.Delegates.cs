using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

internal static partial class GeometryIntersectionSolver
{
    private readonly record struct DelegatedEquation(Func<double, double, double> SurfaceSag,
        Func<Vector3D, Vector3D> SurfaceNormal) : ISurfaceEquation
    {
        public double Sag(double x, double y) => SurfaceSag(x, y);
        public Vector3D Normal(Vector3D p) => SurfaceNormal(p);
    }

    public static IntersectionResult Solve(Vector3D origin, Vector3D direction,
        Func<double, double, double> sag, Func<Vector3D, Vector3D> normal)
    {
        ArgumentNullException.ThrowIfNull(sag); ArgumentNullException.ThrowIfNull(normal);
        return Solve(origin, direction, new DelegatedEquation(sag, normal));
    }

    public static IntersectionResult CreateVerifiedResult(Vector3D origin, Vector3D direction, double distance,
        Func<double, double, double> sag, Func<Vector3D, Vector3D> normal, int iterations, double? conditionEstimate = null) =>
        CreateVerifiedResult(origin, direction, distance, new DelegatedEquation(sag, normal), iterations, conditionEstimate);
}
