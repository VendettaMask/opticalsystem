using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

internal interface ISurfaceEquation
{
    double Sag(double x, double y);
    Vector3D Normal(Vector3D point);
}

// A value representation of the existing rotational sag, usable by CPU and CUDA.
// No material model, approximation, or independently fitted surface is introduced.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public struct RotationalSurfaceEquation : ISurfaceEquation
{
    public double Radius, Conic;
    public int Kind, Count; // 0 plane, 1 standard, 2 even asphere
    public double A0, A1, A2, A3, A4, A5, A6, A7, A8, A9;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public readonly double Coefficient(int i) => i switch
    { 0 => A0, 1 => A1, 2 => A2, 3 => A3, 4 => A4, 5 => A5, 6 => A6, 7 => A7, 8 => A8, 9 => A9, _ => 0 };

    internal static RotationalSurfaceEquation From(IGeometry geometry)
    {
        if (geometry is PlaneGeometry) return default;
        var standard = geometry is StandardGeometry s ? s : ((EvenAsphereGeometry)geometry).Base;
        var result = new RotationalSurfaceEquation { Kind = geometry is EvenAsphereGeometry ? 2 : 1, Radius = standard.Radius, Conic = standard.Conic };
        if (geometry is EvenAsphereGeometry even)
        {
            if (even.Coefficients.Count > 10) throw new NotSupportedException("CUDA rotational surfaces support up to ten even coefficients.");
            result.Count = even.Coefficients.Count;
            for (var i = 0; i < result.Count; i++)
            {
                var a = even.Coefficients[i];
                switch (i)
                {
                    case 0: result.A0 = a; break;
                    case 1: result.A1 = a; break;
                    case 2: result.A2 = a; break;
                    case 3: result.A3 = a; break;
                    case 4: result.A4 = a; break;
                    case 5: result.A5 = a; break;
                    case 6: result.A6 = a; break;
                    case 7: result.A7 = a; break;
                    case 8: result.A8 = a; break;
                    case 9: result.A9 = a; break;
                }
            }
        }
        return result;
    }

    public readonly double Sag(double x, double y)
    {
        if (Kind == 0) return 0;
        var r2 = x * x + y * y;
        var sag = StandardSag(Radius, Conic, x, y);
        var power = r2;
        for (var i = 0; i < Count; i++) { sag += Coefficient(i) * power; power *= r2; }
        return sag;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static double StandardSag(double radius, double conic, double x, double y)
    {
        if (Math.Abs(radius) < 1e-12 || double.IsInfinity(radius)) return 0;
        var r2 = x * x + y * y;
        var c = 1.0 / radius;
        var argument = 1.0 - ((1.0 + conic) * c * c * r2);
        if (argument < -StandardGeometry.ConicDomainTolerance) return double.NaN;
        return c * r2 / (1 + Math.Sqrt(Math.Max(0, argument)));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public readonly Vector3D Normal(Vector3D p)
    {
        if (Kind == 0) return new(0, 0, 1);
        if (Kind == 1) return StandardNormal(Radius, Conic, p);
        var r2 = p.X * p.X + p.Y * p.Y;
        var curvature = Math.Abs(Radius) < 1e-12 || double.IsInfinity(Radius) ? 0 : 1 / Radius;
        var argument = 1 - (1 + Conic) * curvature * curvature * r2;
        if (argument < -StandardGeometry.ConicDomainTolerance) return new(double.NaN, double.NaN, double.NaN);
        var root = Math.Sqrt(Math.Max(0, argument));
        var polynomialDerivative = 0d; var power = 1d;
        for (var i = 0; i < Count; i++) { polynomialDerivative += 2 * (i + 1) * Coefficient(i) * power; power *= r2; }
        var slope = root == 0 ? curvature : curvature / root + polynomialDerivative;
        // Explicit double literals avoid ILGPU 1.5.3 miscompiling Release's
        // bool-to-double conversion for an integer 0/1 conditional.
        var normal = new Vector3D(-p.X * slope, -p.Y * slope, root == 0 ? 0d : 1d);
        return normal / normal.Length;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    internal static Vector3D StandardNormal(double radius, double conic, Vector3D p)
    {
        if (Math.Abs(radius) < 1e-12 || double.IsInfinity(radius)) return new(0, 0, 1);
        var r2 = p.X * p.X + p.Y * p.Y; var c = 1.0 / radius;
        if (1.0 - ((1.0 + conic) * c * c * r2) < -StandardGeometry.ConicDomainTolerance) return new(double.NaN, double.NaN, double.NaN);
        if (conic == 0) return new Vector3D(-c * p.X, -c * p.Y, 1 - c * p.Z) * ScalarSign.CopySign(1, radius);
        var zc = p.Z * c; var factor = c / Math.Sqrt(zc * conic * (zc * (conic + 1) - 2) + 1);
        return new Vector3D(p.X * factor, p.Y * factor, (p.Z - (1 / c - p.Z * conic)) * factor) * -ScalarSign.CopySign(1, radius);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public readonly IntersectionResult Intersect(Vector3D origin, Vector3D direction)
    {
        if (Kind == 2) return GeometryIntersectionSolver.Solve(origin, direction, this);
        if (Kind == 1 && Math.Abs(Radius) >= 1e-12 && !double.IsInfinity(Radius)) return StandardIntersect(origin, direction);
        if (!GeometryIntersectionSolver.IsFinite(origin) || !GeometryIntersectionSolver.IsFiniteNonZero(direction)) return IntersectionResult.Failure(IntersectionStatus.InvalidInput);
        if (Math.Abs(direction.Z) < 1e-12) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var distance = -origin.Z / direction.Z;
        var result = distance >= 0 ? GeometryIntersectionSolver.CreateVerifiedResult(origin, direction, distance, this, 0) : IntersectionResult.Failure(IntersectionStatus.NoRoot);
        return result.IsHit && Math.Abs(direction.Length - 1) <= 1e-14
            ? result with { Point = result.Point with { Z = 0 }, IncidenceCosineSquared = direction.Z * direction.Z } : result;
    }

    // StandardIntersect and its stable vertex-plane branch are shared with StandardGeometry.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private readonly IntersectionResult StandardIntersect(Vector3D origin, Vector3D direction)
    {
        if (TryVertexPlaneIntersection(origin, direction, out var vertex)) return vertex;
        var k = 1.0 + Conic;
        var a = direction.X * direction.X + direction.Y * direction.Y + k * direction.Z * direction.Z;
        var b = 2.0 * (origin.X * direction.X + origin.Y * direction.Y - Radius * direction.Z + k * origin.Z * direction.Z);
        var c = origin.X * origin.X + origin.Y * origin.Y - 2.0 * Radius * origin.Z + k * origin.Z * origin.Z;
        if (Math.Abs(a) < 1e-15) return Math.Abs(b) < 1e-15 ? IntersectionResult.Failure(IntersectionStatus.NoRoot) : VerifyDistance(-c / b, origin, direction);
        var discriminant = b * b - 4.0 * a * c;
        if (discriminant < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
        var root = Math.Sqrt(discriminant);
        var first = VerifyDistance((-b - root) / (2.0 * a), origin, direction);
        var second = VerifyDistance((-b + root) / (2.0 * a), origin, direction);
        if (!first.IsHit && !second.IsHit)
        {
            var roots = StandardIntersectionValidation.StableQuadraticRoots(a, b, c, root);
            first = VerifyDistance(roots.First, origin, direction); second = VerifyDistance(roots.Second, origin, direction);
        }
        return !first.IsHit ? second : !second.IsHit ? first : first.Distance <= second.Distance ? first : second;
    }

    private readonly IntersectionResult VerifyDistance(double distance, Vector3D origin, Vector3D direction) =>
        !double.IsFinite(distance) || distance < -StandardGeometry.ConicDomainTolerance ? IntersectionResult.Failure(IntersectionStatus.NoRoot)
        : GeometryIntersectionSolver.CreateVerifiedResult(origin, direction, Math.Max(0, distance), this, 0);

    private readonly bool TryVertexPlaneIntersection(Vector3D origin, Vector3D direction, out IntersectionResult result)
    {
        result = default;
        if (!GeometryIntersectionSolver.IsFinite(origin) || !GeometryIntersectionSolver.IsFiniteNonZero(direction)
            || Math.Abs(direction.Z) < 1e-12 || Math.Abs(direction.Length - 1) > 1e-14) return false;
        var toPlane = -origin.Z / direction.Z;
        var plane = new Vector3D(origin.X + direction.X * toPlane, origin.Y + direction.Y * toPlane, 0);
        var curvature = 1 / Radius; var r2 = plane.X * plane.X + plane.Y * plane.Y;
        var b = direction.Z - curvature * (plane.X * direction.X + plane.Y * direction.Y);
        var quadratic = 1 + Conic * direction.Z * direction.Z;
        var incidence = b * b - quadratic * curvature * curvature * r2;
        if (!double.IsFinite(incidence) || incidence < 0) return false;
        var denominator = b + ScalarSign.CopySign(Math.Sqrt(incidence), b);
        if (denominator == 0) return false;
        var sagDistance = curvature * r2 / denominator; var distance = toPlane + sagDistance;
        if (!double.IsFinite(distance) || distance < -StandardGeometry.ConicDomainTolerance) return false;
        var verified = GeometryIntersectionSolver.CreateVerifiedResult(plane + direction * sagDistance, direction, 0, this, 0);
        if (!verified.IsHit) return false;
        if (Conic == 0)
        {
            var otherSag = denominator / (quadratic * curvature); var otherDistance = toPlane + otherSag;
            if (double.IsFinite(otherDistance) && otherDistance >= -StandardGeometry.ConicDomainTolerance && otherDistance < distance)
            {
                var other = GeometryIntersectionSolver.CreateVerifiedResult(plane + direction * otherSag, direction, 0, this, 0);
                if (other.IsHit) { verified = other; distance = otherDistance; }
            }
        }
        result = verified with { Distance = Math.Max(0, distance), IncidenceCosineSquared = Conic == 0 ? Math.Min(1, Math.Max(0, incidence)) : null };
        return true;
    }
}
