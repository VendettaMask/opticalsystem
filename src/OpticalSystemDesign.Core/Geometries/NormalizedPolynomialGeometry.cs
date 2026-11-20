using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

public enum SagPolynomialBasis
{
    EvenRadial,
    Radial,
    Cartesian
}

/// <summary>A conic with length-valued coefficients evaluated in normalized coordinates.</summary>
public sealed class NormalizedPolynomialGeometry : IGeometry
{
    public NormalizedPolynomialGeometry(double radius, double conic, double normalizationRadius,
        SagPolynomialBasis basis, int termCount, IReadOnlyList<double> coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        var maximum = basis == SagPolynomialBasis.Cartesian ? 230 : 240;
        if (!Enum.IsDefined(basis) || termCount < 0 || termCount > maximum
            || coefficients.Count < termCount || coefficients.Count > maximum
            || coefficients.Any(value => !double.IsFinite(value))
            || !double.IsFinite(normalizationRadius) || normalizationRadius <= 0)
            throw new ArgumentException("Invalid normalized sag polynomial definition.");
        Base = new StandardGeometry(radius, conic);
        NormalizationRadius = normalizationRadius;
        Basis = basis;
        TermCount = termCount;
        Coefficients = Array.AsReadOnly(coefficients.ToArray());
    }

    public string Kind => Basis switch
    {
        SagPolynomialBasis.EvenRadial => "extended_asphere",
        SagPolynomialBasis.Radial => "extended_odd_asphere",
        _ => "extended_polynomial"
    };

    public StandardGeometry Base { get; }
    public double NormalizationRadius { get; }
    public SagPolynomialBasis Basis { get; }
    public int TermCount { get; }
    public IReadOnlyList<double> Coefficients { get; }

    public double Sag(double x, double y) => Base.Sag(x, y) + Correction(x, y).Sag;

    public Vector3D SurfaceNormal(Vector3D point)
    {
        var curvature = Base.Radius == 0 || double.IsInfinity(Base.Radius) ? 0 : 1 / Base.Radius;
        var root = Math.Sqrt(1 - (1 + Base.Conic) * curvature * curvature
            * (point.X * point.X + point.Y * point.Y));
        var correction = Correction(point.X, point.Y);
        var normal = new Vector3D(
            -curvature * point.X / root - correction.Dx,
            -curvature * point.Y / root - correction.Dy, 1);
        return normal / normal.Length;
    }

    private (double Sag, double Dx, double Dy) Correction(double x, double y) =>
        Basis == SagPolynomialBasis.Cartesian ? CartesianCorrection(x, y) : RadialCorrection(x, y);

    private (double Sag, double Dx, double Dy) RadialCorrection(double x, double y)
    {
        var rhoX = x / NormalizationRadius;
        var rhoY = y / NormalizationRadius;
        var rho = Math.Sqrt(rhoX * rhoX + rhoY * rhoY);
        var even = Basis == SagPolynomialBasis.EvenRadial;
        var argument = even ? rho * rho : rho;
        var value = 0d;
        var derivative = 0d;
        // Horner evaluation retains high-order terms without rescaling their coefficients.
        for (var index = TermCount - 1; index >= 0; index--)
        {
            derivative = derivative * argument + value;
            value = value * argument + Coefficients[index];
        }
        derivative = derivative * argument + value;
        value *= argument;
        if (!even && rho == 0)
        {
            var slope = derivative == 0 ? 0 : double.NaN;
            return (value, slope, slope);
        }
        var factor = (even ? 2 * derivative : derivative / rho) / NormalizationRadius;
        return (value, factor * rhoX, factor * rhoY);
    }

    private (double Sag, double Dx, double Dy) CartesianCorrection(double x, double y)
    {
        Span<double> xp = stackalloc double[21];
        Span<double> yp = stackalloc double[21];
        xp[0] = yp[0] = 1;
        for (var order = 1; order <= 20; order++)
        {
            xp[order] = xp[order - 1] * x / NormalizationRadius;
            yp[order] = yp[order - 1] * y / NormalizationRadius;
        }
        var value = 0d;
        var dx = 0d;
        var dy = 0d;
        var index = 0;
        for (var order = 1; order <= 20 && index < TermCount; order++)
        {
            for (var yOrder = 0; yOrder <= order && index < TermCount; yOrder++)
            {
                var xOrder = order - yOrder;
                var coefficient = Coefficients[index++];
                if (coefficient == 0) continue;
                value += coefficient * xp[xOrder] * yp[yOrder];
                if (xOrder > 0)
                    dx += coefficient * xOrder * xp[xOrder - 1] * yp[yOrder] / NormalizationRadius;
                if (yOrder > 0)
                    dy += coefficient * yOrder * xp[xOrder] * yp[yOrder - 1] / NormalizationRadius;
            }
        }
        return (value, dx, dy);
    }

    internal double VertexMeridionalCurvature
    {
        get
        {
            if (Basis == SagPolynomialBasis.Radial && TermCount > 0 && Coefficients[0] != 0)
                throw new NotSupportedException("An odd radial surface with a nonzero linear term has no vertex curvature.");
            if (Basis == SagPolynomialBasis.Cartesian
                && (TermCount > 0 && Coefficients[0] != 0 || TermCount > 1 && Coefficients[1] != 0
                    || TermCount > 3 && Coefficients[3] != 0))
                throw new NotSupportedException("Tilted or XY-coupled polynomial surfaces require a coupled first-order model.");
            var index = Basis switch
            {
                SagPolynomialBasis.EvenRadial => 0,
                SagPolynomialBasis.Radial => 1,
                _ => 4
            };
            var curvature = Base.Radius == 0 || double.IsInfinity(Base.Radius) ? 0 : 1 / Base.Radius;
            return curvature + (TermCount > index ? 2 * Coefficients[index] / NormalizationRadius / NormalizationRadius : 0);
        }
    }

    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        StandardGeometry.NewtonSolveDistance(origin, direction, Sag, SurfaceNormal);

    public IGeometry Clone() => new NormalizedPolynomialGeometry(
        Base.Radius, Base.Conic, NormalizationRadius, Basis, TermCount, Coefficients);
}
