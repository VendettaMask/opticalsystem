using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Two independent rotational zones. The boundary belongs to the inner zone.</summary>
public sealed class ConcentricZoneGeometry : IGeometry
{
    public ConcentricZoneGeometry(double radius1, double conic1, IReadOnlyList<double> coefficients1,
        double radius2, double conic2, IReadOnlyList<double> coefficients2, double boundaryRadius, bool breakSag = false)
    {
        if (!double.IsFinite(boundaryRadius) || boundaryRadius <= 0) throw new ArgumentOutOfRangeException(nameof(boundaryRadius));
        if (coefficients1.Count > 60 || coefficients2.Count > 60 || coefficients1.Any(c => !double.IsFinite(c))
            || coefficients2.Any(c => !double.IsFinite(c))) throw new ArgumentException("Invalid bounded zone coefficients.");
        Inner = new(radius1, conic1, coefficients1); Outer = new(radius2, conic2, coefficients2);
        BoundaryRadius = boundaryRadius; BreakSag = breakSag;
        if (!double.IsFinite(OuterOffset)) throw new ArgumentException("The zone boundary is outside the conic domain.");
    }
    public string Kind => "concentric_zones";
    public EvenAsphereGeometry Inner { get; }
    public EvenAsphereGeometry Outer { get; }
    public double BoundaryRadius { get; }
    public bool BreakSag { get; }
    public double OuterOffset => BreakSag ? 0 : Inner.Sag(BoundaryRadius, 0) - Outer.Sag(BoundaryRadius, 0);
    private bool IsInner(double x, double y) => double.Hypot(x, y) <= BoundaryRadius;
    public double Sag(double x, double y) => IsInner(x, y) ? Inner.Sag(x, y) : Outer.Sag(x, y) + OuterOffset;
    public Vector3D SurfaceNormal(Vector3D point) => IsInner(point.X, point.Y) ? Inner.SurfaceNormal(point) : Outer.SurfaceNormal(point);
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        // Solve each smooth surface independently. Newton iteration must never
        // step across the zone's sag break and report a fictitious root there.
        var inner = Inner.DistanceToIntersection(origin, direction);
        if (inner.IsHit && !IsInner(inner.Point.X, inner.Point.Y)) inner = IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var offset = new Vector3D(0, 0, OuterOffset);
        var outer = Outer.DistanceToIntersection(origin - offset, direction);
        if (outer.IsHit)
        {
            outer = outer with { Point = outer.Point + offset };
            if (IsInner(outer.Point.X, outer.Point.Y)) outer = IntersectionResult.Failure(IntersectionStatus.NoRoot);
        }
        return inner.IsHit && (!outer.IsHit || inner.Distance <= outer.Distance) ? inner : outer;
    }
    public IGeometry Clone() => new ConcentricZoneGeometry(Inner.Base.Radius, Inner.Base.Conic, Inner.Coefficients,
        Outer.Base.Radius, Outer.Base.Conic, Outer.Coefficients, BoundaryRadius, BreakSag);
}
