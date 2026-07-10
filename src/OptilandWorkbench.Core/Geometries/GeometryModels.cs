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

public sealed class StandardGeometry : IGeometry
{
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
        var root = Math.Sqrt(Math.Max(0, 1 - ((1 + Conic) * c * c * r2)));
        return c * r2 / (1 + root);
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        if (Math.Abs(Radius) < 1e-12 || double.IsInfinity(Radius))
        {
            return new PlaneGeometry().DistanceToIntersection(origin, direction);
        }

        return NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint)
    {
        const double step = 1e-5;
        var dzdx = (Sag(localPoint.X + step, localPoint.Y) - Sag(localPoint.X - step, localPoint.Y)) / (2 * step);
        var dzdy = (Sag(localPoint.X, localPoint.Y + step) - Sag(localPoint.X, localPoint.Y - step)) / (2 * step);
        return new Vector3D(-dzdx, -dzdy, 1) / new Vector3D(-dzdx, -dzdy, 1).Length;
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
            if (Math.Abs(residual) < 1e-8)
            {
                return t >= 0 ? t : null;
            }

            var dt = 1e-5;
            var next = origin + (direction * (t + dt));
            var residualNext = next.Z - sag(next.X, next.Y);
            var derivative = (residualNext - residual) / dt;
            if (Math.Abs(derivative) < 1e-12)
            {
                return null;
            }

            t -= residual / derivative;
            if (t < -1e-8)
            {
                return null;
            }
        }

        return t >= 0 ? t : null;
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
            power *= r2;
            sag += coefficient * power;
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
            power *= r;
            sag += coefficient * power;
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
        return new StandardGeometry(RadiusX, ConicX).Sag(x, 0)
            + new StandardGeometry(RadiusY, ConicY).Sag(0, y);
    }

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return StandardGeometry.NewtonSolveDistance(origin, direction, Sag);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => GeometryMath.FiniteDifferenceNormal(Sag, localPoint);

    public IGeometry Clone() => new BiconicGeometry(RadiusX, RadiusY, ConicX, ConicY);
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
        return new BiconicGeometry(SagittalRadius, TangentialRadius).Sag(x, y);
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

public sealed class PlaceholderFreeformGeometry : IGeometry
{
    public PlaceholderFreeformGeometry(string kind)
    {
        Kind = kind;
    }

    public string Kind { get; }

    public double Sag(double x, double y) => 0;

    public double? DistanceToIntersection(Vector3D origin, Vector3D direction)
    {
        return new PlaneGeometry().DistanceToIntersection(origin, direction);
    }

    public Vector3D SurfaceNormal(Vector3D localPoint) => new(0, 0, 1);

    public IGeometry Clone() => new PlaceholderFreeformGeometry(Kind);
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
