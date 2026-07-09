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
        return new StandardGeometry(0).SurfaceNormal(localPoint with { Z = Sag(localPoint.X, localPoint.Y) });
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
        return Base.SurfaceNormal(localPoint);
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

    public Vector3D SurfaceNormal(Vector3D localPoint) => new StandardGeometry(RadiusX, ConicX).SurfaceNormal(localPoint);

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

    public Vector3D SurfaceNormal(Vector3D localPoint) => new StandardGeometry(TangentialRadius).SurfaceNormal(localPoint);

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

    public Vector3D SurfaceNormal(Vector3D localPoint) => new StandardGeometry(0).SurfaceNormal(localPoint);

    public IGeometry Clone() => new PolynomialGeometry(Coefficients);
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
