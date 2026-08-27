using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Geometries;

public sealed class PlaneGeometry : IGeometry
{
    public string Kind => "plane";

    public double Sag(double x, double y) => 0;

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        if (Math.Abs(direction.Z) < 1e-12)
        {
            return null;
        }

        var distance = -origin.Z / direction.Z;
        return distance >= 0 ? distance : null;
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => new(0, 0, 1);

    public IGeometry Clone() => new PlaneGeometry();
}

public sealed class PlaneGratingGeometry : IGratingGeometry
{
    private readonly PlaneGeometry _plane = new();

    public PlaneGratingGeometry(int gratingOrder, double gratingPeriodMicrometers, double grooveOrientationAngleRadians)
    {
        if (double.IsNaN(gratingPeriodMicrometers) || gratingPeriodMicrometers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gratingPeriodMicrometers));
        }

        GratingOrder = gratingOrder;
        GratingPeriodMicrometers = gratingPeriodMicrometers;
        GrooveOrientationAngleRadians = grooveOrientationAngleRadians;
    }

    public string Kind => "plane_grating";

    public int GratingOrder { get; }

    public double GratingPeriodMicrometers { get; }

    public double GrooveOrientationAngleRadians { get; }

    public double ParaxialRadius => double.PositiveInfinity;

    public double Sag(double x, double y) => _plane.Sag(x, y);

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        _plane.DistanceToIntersection(origin, direction);

    public Vector3D SurfaceNormal(Vector3D localPoint) => _plane.SurfaceNormal(localPoint);

    public Vector3D GratingVector(Vector3D localPoint) => new(
        -Math.Sin(GrooveOrientationAngleRadians),
        Math.Cos(GrooveOrientationAngleRadians),
        0);

    public IGeometry Clone() => new PlaneGratingGeometry(
        GratingOrder,
        GratingPeriodMicrometers,
        GrooveOrientationAngleRadians);
}

public sealed class StandardGratingGeometry : IGratingGeometry
{
    public StandardGratingGeometry(
        double radius,
        double conic,
        int gratingOrder,
        double gratingPeriodMicrometers,
        double grooveOrientationAngleRadians)
    {
        if (double.IsNaN(gratingPeriodMicrometers) || gratingPeriodMicrometers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gratingPeriodMicrometers));
        }

        Base = new StandardGeometry(radius, conic);
        GratingOrder = gratingOrder;
        GratingPeriodMicrometers = gratingPeriodMicrometers;
        GrooveOrientationAngleRadians = grooveOrientationAngleRadians;
    }

    public string Kind => "standard_grating";

    public StandardGeometry Base { get; }

    public int GratingOrder { get; }

    public double GratingPeriodMicrometers { get; }

    public double GrooveOrientationAngleRadians { get; }

    public double ParaxialRadius => Base.Radius;

    public double Sag(double x, double y) => Base.Sag(x, y);

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        Base.DistanceToIntersection(origin, direction);

    public Vector3D SurfaceNormal(Vector3D localPoint) => Base.SurfaceNormal(localPoint);

    public Vector3D GratingVector(Vector3D localPoint)
    {
        var radius = Base.Radius;
        var radialSquared = (localPoint.X * localPoint.X) + (localPoint.Y * localPoint.Y);
        var root = Math.Sqrt(1 - ((1 + Base.Conic) * radialSquared / (radius * radius)));
        var denominator = radius * root;
        var rawNormal = Normalize(new Vector3D(
            localPoint.X / denominator,
            localPoint.Y / denominator,
            -1));
        var cosine = Math.Cos(GrooveOrientationAngleRadians);
        var sine = Math.Sin(GrooveOrientationAngleRadians);
        var tangent = Normalize(new Vector3D(
            cosine,
            sine,
            ((localPoint.X * cosine) + (localPoint.Y * sine)) / denominator));
        return -Normalize(Cross(rawNormal, tangent));
    }

    public IGeometry Clone() => new StandardGratingGeometry(
        Base.Radius,
        Base.Conic,
        GratingOrder,
        GratingPeriodMicrometers,
        GrooveOrientationAngleRadians);

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static Vector3D Normalize(Vector3D vector)
    {
        var length = vector.Length;
        return length <= 1e-15 ? new Vector3D(double.NaN, double.NaN, double.NaN) : vector / length;
    }
}

public sealed class StandardGeometry : IGeometry
{
    internal const double ConicDomainTolerance = 1e-12;

    public StandardGeometry(double radius, double conic = 0)
    {
        Radius = radius;
        Conic = conic;
    }

    public string Kind => "standard";

    public double Radius { get; set; }

    public double Conic { get; set; }

    public double Sag(double x, double y)
    {
        if (Math.Abs(Radius) < 1e-12 || double.IsInfinity(Radius))
        {
            return 0;
        }

        var r2 = (x * x) + (y * y);
        var c = 1.0 / Radius;
        var rootArgument = 1.0 - ((1.0 + Conic) * c * c * r2);
        if (rootArgument < -ConicDomainTolerance)
        {
            return double.NaN;
        }

        var root = Math.Sqrt(Math.Max(0, rootArgument));
        return c * r2 / (1 + root);
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        if (Math.Abs(Radius) < 1e-12 || double.IsInfinity(Radius))
        {
            return new PlaneGeometry().DistanceToIntersection(origin, direction);
        }

        var conicFactor = 1.0 + Conic;
        var a = (direction.X * direction.X)
            + (direction.Y * direction.Y)
            + (conicFactor * direction.Z * direction.Z);
        var b = 2.0 * (
            (origin.X * direction.X)
            + (origin.Y * direction.Y)
            - (Radius * direction.Z)
            + (conicFactor * origin.Z * direction.Z));
        var c = (origin.X * origin.X)
            + (origin.Y * origin.Y)
            - (2.0 * Radius * origin.Z)
            + (conicFactor * origin.Z * origin.Z);

        if (Math.Abs(a) < 1e-15)
        {
            if (Math.Abs(b) < 1e-15)
            {
                return null;
            }

            var linearDistance = -c / b;
            return ValidateExplicitSagDistance(linearDistance, origin, direction);
        }

        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < 0)
        {
            return null;
        }

        var root = Math.Sqrt(discriminant);
        var first = (-b - root) / (2.0 * a);
        var second = (-b + root) / (2.0 * a);
        var firstDistance = ValidateExplicitSagDistance(first, origin, direction);
        var secondDistance = ValidateExplicitSagDistance(second, origin, direction);
        if (!firstDistance.HasValue)
        {
            return secondDistance;
        }

        if (!secondDistance.HasValue)
        {
            return firstDistance;
        }

        return Math.Min(firstDistance.Value, secondDistance.Value);
    }

    private double? ValidateExplicitSagDistance(double distance, Vector3D origin, Vector3D direction)
    {
        if (!double.IsFinite(distance) || distance < -ConicDomainTolerance)
        {
            return null;
        }

        var clampedDistance = Math.Max(0, distance);
        var point = origin + (direction * clampedDistance);
        var sag = Sag(point.X, point.Y);
        if (!double.IsFinite(sag))
        {
            return null;
        }

        var tolerance = 1e-8 * Math.Max(1.0, Math.Max(Math.Abs(point.Z), Math.Abs(sag)));
        return Math.Abs(point.Z - sag) <= tolerance ? clampedDistance : null;
    }

    public Vector3D SurfaceNormal(Vector3D localPoint)
    {
        if (Math.Abs(Radius) < 1e-12 || double.IsInfinity(Radius))
        {
            return new Vector3D(0, 0, 1);
        }

        var r2 = (localPoint.X * localPoint.X) + (localPoint.Y * localPoint.Y);
        var c = 1.0 / Radius;
        var rootArgument = 1.0 - ((1.0 + Conic) * c * c * r2);
        if (rootArgument < -ConicDomainTolerance)
        {
            return new Vector3D(double.NaN, double.NaN, double.NaN);
        }

        var normal = new Vector3D(
            -localPoint.X,
            -localPoint.Y,
            Radius - ((1.0 + Conic) * localPoint.Z));
        return normal / normal.Length;
    }

    public IGeometry Clone() => new StandardGeometry(Radius, Conic);

    internal static double? NewtonSolveDistance(Vector3D origin, Vector3D direction, Func<double, double, double> sag)
    {
        var t = Math.Abs(direction.Z) < 1e-12 ? 0 : -origin.Z / direction.Z;
        t = Math.Max(0, t);

        for (var iteration = 0; iteration < 32; iteration++)
        {
            var point = origin + (direction * t);
            var residual = point.Z - sag(point.X, point.Y);
            if (!double.IsFinite(residual))
            {
                return null;
            }

            if (Math.Abs(residual) < 1e-8)
            {
                return double.IsFinite(t) && t >= 0 ? t : null;
            }

            var dt = 1e-5;
            var next = origin + (direction * (t + dt));
            var residualNext = next.Z - sag(next.X, next.Y);
            if (!double.IsFinite(residualNext))
            {
                return null;
            }

            var derivative = (residualNext - residual) / dt;
            if (!double.IsFinite(derivative) || Math.Abs(derivative) < 1e-12)
            {
                return null;
            }

            t -= residual / derivative;
            if (!double.IsFinite(t) || t < -1e-8)
            {
                return null;
            }
        }

        return double.IsFinite(t) && t >= 0 ? t : null;
    }
}

public sealed class EvenAsphereGeometry : IGeometry
{
    public EvenAsphereGeometry(double radius, double conic, IReadOnlyList<double> coefficients)
    {
        Base = new StandardGeometry(radius, conic);
        Coefficients = coefficients.ToArray();
    }

    public string Kind => "even_asphere";

    public StandardGeometry Base { get; }

    public IReadOnlyList<double> Coefficients { get; }

    public double Sag(double x, double y)
    {
        var r2 = (x * x) + (y * y);
        var sag = Base.Sag(x, y);
        var power = r2;
        foreach (var coefficient in Coefficients)
        {
            sag += coefficient * power;
            power *= r2;
        }

        return sag;
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint)
    {
        return GeometryMath.FiniteDifferenceNormal(Sag, localPoint);
    }

    public IGeometry Clone() => new EvenAsphereGeometry(Base.Radius, Base.Conic, Coefficients);
}

public sealed class OddAsphereGeometry : IGeometry
{
    public OddAsphereGeometry(double radius, double conic, IReadOnlyList<double> coefficients)
    {
        Base = new StandardGeometry(radius, conic);
        Coefficients = coefficients.ToArray();
    }

    public string Kind => "odd_asphere";

    public StandardGeometry Base { get; }

    public IReadOnlyList<double> Coefficients { get; }

    public double Sag(double x, double y)
    {
        var r = Math.Sqrt((x * x) + (y * y));
        var sag = Base.Sag(x, y);
        var power = r;
        foreach (var coefficient in Coefficients)
        {
            sag += coefficient * power;
            power *= r;
        }

        return sag;
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint)
    {
        return GeometryMath.FiniteDifferenceNormal(Sag, localPoint);
    }

    public IGeometry Clone() => new OddAsphereGeometry(Base.Radius, Base.Conic, Coefficients);
}

public sealed class BiconicGeometry : IGeometry
{
    public BiconicGeometry(double radiusX, double radiusY, double conicX = 0, double conicY = 0)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;
        ConicX = conicX;
        ConicY = conicY;
    }

    public string Kind => "biconic";

    public double RadiusX { get; }

    public double RadiusY { get; }

    public double ConicX { get; }

    public double ConicY { get; }

    public double Sag(double x, double y)
    {
        var curvatureX = Curvature(RadiusX);
        var curvatureY = Curvature(RadiusY);
        var numerator = (curvatureX * x * x) + (curvatureY * y * y);
        if (Math.Abs(numerator) <= 1e-30)
        {
            return 0;
        }

        var rootArgument = 1.0
            - ((1.0 + ConicX) * curvatureX * curvatureX * x * x)
            - ((1.0 + ConicY) * curvatureY * curvatureY * y * y);
        if (rootArgument < -StandardGeometry.ConicDomainTolerance)
        {
            return double.NaN;
        }

        return numerator / (1.0 + Math.Sqrt(Math.Max(0, rootArgument)));
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new BiconicGeometry(RadiusX, RadiusY, ConicX, ConicY);

    private static double Curvature(double radius) => Math.Abs(radius) < 1e-12 || double.IsInfinity(radius)
        ? 0
        : 1.0 / radius;
}

public sealed class SeparableBiconicGeometry : IGeometry
{
    public SeparableBiconicGeometry(double radiusX, double radiusY, double conicX = 0, double conicY = 0)
    {
        RadiusX = radiusX;
        RadiusY = radiusY;
        ConicX = conicX;
        ConicY = conicY;
    }

    public string Kind => "separable_biconic";

    public double RadiusX { get; }

    public double RadiusY { get; }

    public double ConicX { get; }

    public double ConicY { get; }

    public double Sag(double x, double y)
    {
        return new StandardGeometry(RadiusX, ConicX).Sag(x, 0)
            + new StandardGeometry(RadiusY, ConicY).Sag(0, y);
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new SeparableBiconicGeometry(RadiusX, RadiusY, ConicX, ConicY);
}

public sealed class ToroidalGeometry : IGeometry
{
    public ToroidalGeometry(double tangentialRadius, double sagittalRadius)
    {
        TangentialRadius = tangentialRadius;
        SagittalRadius = sagittalRadius;
    }

    public string Kind => "toroidal";

    public double TangentialRadius { get; }

    public double SagittalRadius { get; }

    public double Sag(double x, double y)
    {
        const double epsilon = 1e-14;
        var yzSag = 0.0;
        if (!double.IsInfinity(TangentialRadius) && Math.Abs(TangentialRadius) > epsilon)
        {
            var curvature = 1.0 / TangentialRadius;
            var root = Math.Max(0, 1.0 - (curvature * curvature * y * y));
            yzSag = (curvature * y * y) / (1.0 + Math.Sqrt(root));
        }

        if (double.IsInfinity(SagittalRadius) || Math.Abs(SagittalRadius) <= epsilon)
        {
            return yzSag;
        }

        var offset = SagittalRadius - yzSag;
        var radicand = (offset * offset) - (x * x);
        if (radicand < 0)
        {
            return double.NaN;
        }

        return yzSag + offset - (Math.Sign(offset) * Math.Sqrt(radicand));
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new ToroidalGeometry(TangentialRadius, SagittalRadius);
}

public sealed class PolynomialGeometry : IGeometry
{
    public PolynomialGeometry(IReadOnlyDictionary<(int X, int Y), double> coefficients)
    {
        Coefficients = new Dictionary<(int X, int Y), double>(coefficients);
    }

    public string Kind => "polynomial";

    public IReadOnlyDictionary<(int X, int Y), double> Coefficients { get; }

    public double Sag(double x, double y)
    {
        return Coefficients.Sum(term => term.Value * Math.Pow(x, term.Key.X) * Math.Pow(y, term.Key.Y));
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new PolynomialGeometry(Coefficients);
}

public sealed class ChebyshevGeometry : IGeometry
{
    public ChebyshevGeometry(IReadOnlyDictionary<(int XOrder, int YOrder), double> coefficients, double normalizationX = 1, double normalizationY = 1)
    {
        Coefficients = new Dictionary<(int XOrder, int YOrder), double>(coefficients);
        NormalizationX = Math.Max(1e-12, Math.Abs(normalizationX));
        NormalizationY = Math.Max(1e-12, Math.Abs(normalizationY));
    }

    public string Kind => "chebyshev";

    public IReadOnlyDictionary<(int XOrder, int YOrder), double> Coefficients { get; }

    public double NormalizationX { get; }

    public double NormalizationY { get; }

    public double Sag(double x, double y)
    {
        var xn = x / NormalizationX;
        var yn = y / NormalizationY;
        return Coefficients.Sum(term =>
            term.Value
            * GeometryMath.Chebyshev(term.Key.XOrder, xn)
            * GeometryMath.Chebyshev(term.Key.YOrder, yn));
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new ChebyshevGeometry(Coefficients, NormalizationX, NormalizationY);
}

public sealed class ZernikeGeometry : IGeometry
{
    public ZernikeGeometry(IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> coefficients, double pupilRadius = 1)
    {
        Coefficients = new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>(coefficients);
        PupilRadius = Math.Max(1e-12, Math.Abs(pupilRadius));
    }

    public string Kind => "zernike";

    public IReadOnlyDictionary<(int RadialOrder, int AzimuthalFrequency), double> Coefficients { get; }

    public double PupilRadius { get; }

    public double Sag(double x, double y)
    {
        return Coefficients.Sum(term => term.Value * GeometryMath.Zernike(term.Key.RadialOrder, term.Key.AzimuthalFrequency, x, y, PupilRadius));
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new ZernikeGeometry(Coefficients, PupilRadius);
}

public sealed class ForbesQGeometry : IGeometry
{
    public ForbesQGeometry(double radius, double conic, double normalizationRadius, IReadOnlyList<double> qCoefficients)
    {
        Base = new StandardGeometry(radius, conic);
        NormalizationRadius = Math.Max(1e-12, Math.Abs(normalizationRadius));
        QCoefficients = qCoefficients.ToArray();
    }

    public string Kind => "forbes_q";

    public StandardGeometry Base { get; }

    public double NormalizationRadius { get; }

    public IReadOnlyList<double> QCoefficients { get; }

    public double Sag(double x, double y)
    {
        var rho = Math.Sqrt((x * x) + (y * y)) / NormalizationRadius;
        var rho2 = rho * rho;
        var correction = 0.0;
        for (var index = 0; index < QCoefficients.Count; index++)
        {
            var order = index + 1;
            var qBasis = Math.Pow(rho2, order) * (1.0 - rho2);
            correction += QCoefficients[index] * qBasis;
        }

        return Base.Sag(x, y) + correction;
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new ForbesQGeometry(Base.Radius, Base.Conic, NormalizationRadius, QCoefficients);
}

public interface INonComputableGeometry
{
    string OriginalType { get; }

    string BlockingReason { get; }
}

public sealed class OpaqueGeometryPayload : IGeometry, INonComputableGeometry
{
    private readonly Serialization.ComponentSnapshot _payload;

    public OpaqueGeometryPayload(Serialization.ComponentSnapshot payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(payload.Kind))
        {
            throw new ArgumentException("Opaque geometry type cannot be empty.", nameof(payload));
        }

        _payload = ClonePayload(payload);
    }

    public string Kind => _payload.Kind;

    public string OriginalType => _payload.Kind;

    public string BlockingReason => _payload.Text.TryGetValue("optiland.blockingReason", out var reason)
        && !string.IsNullOrWhiteSpace(reason)
            ? reason
            : "当前版本不支持该几何；原始数据仅作为不可计算的 opaque payload 保存。";

    public Serialization.ComponentSnapshot Payload => ClonePayload(_payload);

    public double Sag(double x, double y) => throw CannotCompute("Sag");

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        throw CannotCompute("光线交点");

    public Vector3D SurfaceNormal(Vector3D localPoint) => throw CannotCompute("表面法线");

    public IGeometry Clone() => new OpaqueGeometryPayload(_payload);

    private InvalidOperationException CannotCompute(string quantity) => new(
        $"不可计算几何“{OriginalType}”不能求取{quantity}。{BlockingReason}");

    private static Serialization.ComponentSnapshot ClonePayload(Serialization.ComponentSnapshot source) => new(
        source.Kind,
        new Dictionary<string, double>(source.Numbers, StringComparer.Ordinal),
        new Dictionary<string, string>(source.Text, StringComparer.Ordinal),
        source.Children?.ToDictionary(
            item => item.Key,
            item => ClonePayload(item.Value),
            StringComparer.Ordinal));
}

internal static class GeometryMath
{
    public static Vector3D FiniteDifferenceNormal(Func<double, double, double> sag, Vector3D localPoint)
    {
        const double step = 1e-5;
        var dzdx = (sag(localPoint.X + step, localPoint.Y) - sag(localPoint.X - step, localPoint.Y)) / (2 * step);
        var dzdy = (sag(localPoint.X, localPoint.Y + step) - sag(localPoint.X, localPoint.Y - step)) / (2 * step);
        var normal = new Vector3D(-dzdx, -dzdy, 1);
        return normal / normal.Length;
    }

    public static double Chebyshev(int order, double value)
    {
        if (order < 0)
        {
            return 0;
        }

        if (order == 0)
        {
            return 1;
        }

        if (order == 1)
        {
            return value;
        }

        var previous = 1.0;
        var current = value;
        for (var index = 2; index <= order; index++)
        {
            var next = (2 * value * current) - previous;
            previous = current;
            current = next;
        }

        return current;
    }

    public static double Zernike(int radialOrder, int azimuthalFrequency, double x, double y, double pupilRadius)
    {
        if (radialOrder < 0)
        {
            return 0;
        }

        var m = Math.Abs(azimuthalFrequency);
        if (m > radialOrder || ((radialOrder - m) % 2) != 0)
        {
            return 0;
        }

        var rho = Math.Sqrt((x * x) + (y * y)) / Math.Max(1e-12, pupilRadius);
        var theta = Math.Atan2(y, x);
        var radial = ZernikeRadial(radialOrder, m, rho);
        if (m == 0)
        {
            return radial;
        }

        return azimuthalFrequency >= 0
            ? radial * Math.Cos(m * theta)
            : radial * Math.Sin(m * theta);
    }

    private static double ZernikeRadial(int n, int m, double rho)
    {
        var value = 0.0;
        var max = (n - m) / 2;
        for (var k = 0; k <= max; k++)
        {
            var sign = (k % 2) == 0 ? 1.0 : -1.0;
            var numerator = Factorial(n - k);
            var denominator = Factorial(k) * Factorial((n + m) / 2 - k) * Factorial((n - m) / 2 - k);
            value += sign * numerator / denominator * Math.Pow(rho, n - (2 * k));
        }

        return value;
    }

    private static double Factorial(int value)
    {
        var result = 1.0;
        for (var index = 2; index <= value; index++)
        {
            result *= index;
        }

        return result;
    }
}
