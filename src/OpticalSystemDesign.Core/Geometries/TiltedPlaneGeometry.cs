using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>A plane z = tangentX*x + tangentY*y in the unchanged local frame.</summary>
public sealed class TiltedPlaneGeometry : IGeometry
{
    public TiltedPlaneGeometry(double tangentX, double tangentY)
    {
        if (!double.IsFinite(tangentX) || !double.IsFinite(tangentY))
            throw new ArgumentException("Tilted-plane tangents must be finite.");
        TangentX = tangentX; TangentY = tangentY;
    }
    public string Kind => "tilted_plane";
    public double TangentX { get; }
    public double TangentY { get; }
    public double Sag(double x, double y) => TangentX * x + TangentY * y;
    public Vector3D SurfaceNormal(Vector3D localPoint)
    {
        // Scale before normalizing to retain finite normals for steep planes.
        var scale = Math.Max(1, Math.Max(Math.Abs(TangentX), Math.Abs(TangentY)));
        var v = new Vector3D(-TangentX / scale, -TangentY / scale, 1 / scale);
        return v / v.Length;
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z)
            || !double.IsFinite(direction.X) || !double.IsFinite(direction.Y) || !double.IsFinite(direction.Z)
            || direction.Length == 0) return IntersectionResult.Failure(IntersectionStatus.InvalidInput);
        var normal = SurfaceNormal(origin);
        var denominator = normal.X * direction.X + normal.Y * direction.Y + normal.Z * direction.Z;
        var numerator = normal.X * origin.X + normal.Y * origin.Y + normal.Z * origin.Z;
        if (denominator == 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var distance = -numerator / denominator;
        if (!double.IsFinite(distance) || distance < 0)
            return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var point = origin + distance * direction;
        return new(IntersectionStatus.Success, distance, point, normal,
            Math.Abs(point.Z - Sag(point.X, point.Y)), 0, direction.Length / Math.Abs(denominator));
    }
    public IGeometry Clone() => new TiltedPlaneGeometry(TangentX, TangentY);
}
