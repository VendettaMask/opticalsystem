using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Conic plus normalized spherical, astigmatism and coma sag in a temporary frame.</summary>
public sealed class IrregularGeometry : IGeometry
{
    public IrregularGeometry(double radius, double conic, double normalizationRadius, double decenterX, double decenterY,
        double tiltXDegrees, double tiltYDegrees, double spherical, double astigmatism, double coma, double angleDegrees)
    {
        if (!double.IsFinite(normalizationRadius) || normalizationRadius <= 0
            || new[] { conic, decenterX, decenterY, tiltXDegrees, tiltYDegrees, spherical, astigmatism, coma, angleDegrees }.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("不规则面需要正归一化半径和有限参数。");
        Base = new StandardGeometry(radius, conic); NormalizationRadius = normalizationRadius;
        TiltXDegrees = tiltXDegrees; TiltYDegrees = tiltYDegrees;
        // Native coordinate breaks rotate about the successive local X, then Y
        // axes (Rx*Ry). CoordinateSystem stores the equivalent Rz*Ry*Rx Euler angles.
        var x = tiltXDegrees * Math.PI / 180; var y = tiltYDegrees * Math.PI / 180;
        var cx = Math.Cos(x); var sx = Math.Sin(x); var cy = Math.Cos(y); var sy = Math.Sin(y);
        Coordinates = new(new(decenterX, decenterY, 0), Math.Atan2(sx, cx * cy) * 180 / Math.PI,
            Math.Asin(Math.Clamp(cx * sy, -1, 1)) * 180 / Math.PI, Math.Atan2(sx * sy, cy) * 180 / Math.PI);
        Spherical = spherical; Astigmatism = astigmatism; Coma = coma; AngleDegrees = angleDegrees;
    }
    public string Kind => "irregular_sag";
    public StandardGeometry Base { get; }
    public double NormalizationRadius { get; }
    public CoordinateSystem Coordinates { get; }
    public double TiltXDegrees { get; }
    public double TiltYDegrees { get; }
    public double Spherical { get; }
    public double Astigmatism { get; }
    public double Coma { get; }
    public double AngleDegrees { get; }
    private (double Sag, double Dx, double Dy) Local(double x, double y)
    {
        var u = x / NormalizationRadius; var v = y / NormalizationRadius;
        var r2 = u * u + v * v; var sine = Math.Sin(AngleDegrees * Math.PI / 180); var cosine = Math.Cos(AngleDegrees * Math.PI / 180);
        var w = v * cosine - u * sine;
        var n = Base.SurfaceNormal(new(x, y, Base.Sag(x, y)));
        var dx = 4 * Spherical * r2 * u - 2 * Astigmatism * w * sine + Coma * (2 * u * w - r2 * sine);
        var dy = 4 * Spherical * r2 * v + 2 * Astigmatism * w * cosine + Coma * (2 * v * w + r2 * cosine);
        return (Base.Sag(x, y) + Spherical * r2 * r2 + Astigmatism * w * w + Coma * r2 * w,
            -n.X / n.Z + dx / NormalizationRadius, -n.Y / n.Z + dy / NormalizationRadius);
    }
    private double LocalSag(double x, double y) => Local(x, y).Sag;
    private Vector3D LocalNormal(Vector3D point)
    {
        var p = Local(point.X, point.Y); var n = new Vector3D(-p.Dx, -p.Dy, 1); return n / n.Length;
    }
    public double Sag(double x, double y)
    {
        var z = 0d;
        for (var i = 0; i < 48; i++)
        {
            var p = Coordinates.ToLocalPoint(new(x, y, z)); var local = Local(p.X, p.Y);
            var residual = p.Z - local.Sag;
            if (Math.Abs(residual) < 1e-12) return z;
            var derivative = Coordinates.ToGlobalDirection(new(-local.Dx, -local.Dy, 1)).Z;
            if (!double.IsFinite(residual) || Math.Abs(derivative) < 1e-14) return double.NaN;
            z -= residual / derivative;
        }
        return double.NaN;
    }
    public Vector3D SurfaceNormal(Vector3D point) => Coordinates.ToGlobalDirection(LocalNormal(Coordinates.ToLocalPoint(point)));
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        var result = StandardGeometry.NewtonSolveDistance(Coordinates.ToLocalPoint(origin), Coordinates.ToLocalDirection(direction), LocalSag, LocalNormal);
        return result.IsHit ? result with { Point = Coordinates.ToGlobalPoint(result.Point), Normal = Coordinates.ToGlobalDirection(result.Normal) } : result;
    }
    public IGeometry Clone() => new IrregularGeometry(Base.Radius, Base.Conic, NormalizationRadius, Coordinates.Origin.X, Coordinates.Origin.Y,
        TiltXDegrees, TiltYDegrees, Spherical, Astigmatism, Coma, AngleDegrees);
    internal IrregularGeometry WithNormalizationRadius(double radius) => new(Base.Radius, Base.Conic, radius,
        Coordinates.Origin.X, Coordinates.Origin.Y, TiltXDegrees, TiltYDegrees, Spherical, Astigmatism, Coma, AngleDegrees);
}
