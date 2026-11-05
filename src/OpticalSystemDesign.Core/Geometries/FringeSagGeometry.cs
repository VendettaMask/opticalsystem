using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Geometries;

/// <summary>Conic/even-asphere sag plus decentered, unnormalized Fringe Zernike sag terms.</summary>
public sealed class FringeSagGeometry : IGeometry
{
    public FringeSagGeometry(double radius, double conic, IReadOnlyList<double> asphereCoefficients,
        double normalizationRadius, IReadOnlyList<double> fringeCoefficients,
        double decenterX = 0, double decenterY = 0, bool extrapolate = false)
    {
        if (!double.IsFinite(normalizationRadius) || normalizationRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(normalizationRadius));
        if (asphereCoefficients.Count > 8 || fringeCoefficients.Count > 37
            || asphereCoefficients.Any(v => !double.IsFinite(v)) || fringeCoefficients.Any(v => !double.IsFinite(v))
            || !double.IsFinite(decenterX) || !double.IsFinite(decenterY))
            throw new ArgumentException("Invalid Fringe sag coefficients or decenter.");
        Base = new EvenAsphereGeometry(radius, conic, asphereCoefficients);
        NormalizationRadius = normalizationRadius; DecenterX = decenterX; DecenterY = decenterY; Extrapolate = extrapolate;
        FringeCoefficients = Array.AsReadOnly(fringeCoefficients.ToArray());
    }
    public string Kind => "fringe_sag";
    public EvenAsphereGeometry Base { get; }
    public double NormalizationRadius { get; }
    public IReadOnlyList<double> FringeCoefficients { get; }
    public double DecenterX { get; }
    public double DecenterY { get; }
    public bool Extrapolate { get; }
    public double Sag(double x, double y)
    {
        var sag = Base.Sag(x, y);
        var u = (x - DecenterX) / NormalizationRadius; var v = (y - DecenterY) / NormalizationRadius;
        if (!Extrapolate && u * u + v * v > 1) return sag;
        for (var i = 0; i < FringeCoefficients.Count; i++)
        {
            if (FringeCoefficients[i] == 0) continue;
            sag += FringeCoefficients[i] * FringeZernikeBasis.Evaluate(i, u, v).Value;
        }
        return sag;
    }
    public Vector3D SurfaceNormal(Vector3D point)
    {
        var normal = Base.SurfaceNormal(point);
        var u=(point.X-DecenterX)/NormalizationRadius;var v=(point.Y-DecenterY)/NormalizationRadius;
        if (!Extrapolate && u*u+v*v>1) return normal;
        var sx=-normal.X/normal.Z;var sy=-normal.Y/normal.Z;
        for(var i=0;i<FringeCoefficients.Count;i++)
        {
            if(FringeCoefficients[i]==0) continue;
            var (_,dx,dy)=FringeZernikeBasis.Evaluate(i,u,v);
            sx+=FringeCoefficients[i]*dx/NormalizationRadius;sy+=FringeCoefficients[i]*dy/NormalizationRadius;
        }
        var result=new Vector3D(-sx,-sy,1);return result/result.Length;
    }
    public IntersectionResult DistanceToIntersection(Vector3D origin, Vector3D direction) =>
        StandardGeometry.NewtonSolveDistance(origin, direction, Sag, SurfaceNormal);
    public IGeometry Clone() => new FringeSagGeometry(Base.Base.Radius, Base.Base.Conic, Base.Coefficients,
        NormalizationRadius, FringeCoefficients, DecenterX, DecenterY, Extrapolate);
}

internal static class FringeZernikeBasis
{
    internal static readonly IReadOnlyList<(int N, int M)> Orders = Array.AsReadOnly(new (int N, int M)[]
    {
        (0,0), (1,1),(1,-1), (2,0),(2,2),(2,-2), (3,1),(3,-1), (4,0), (3,3),(3,-3),
        (4,2),(4,-2), (5,1),(5,-1), (6,0), (4,4),(4,-4), (5,3),(5,-3), (6,2),(6,-2),
        (7,1),(7,-1), (8,0), (5,5),(5,-5), (6,4),(6,-4), (7,3),(7,-3), (8,2),(8,-2),
        (9,1),(9,-1), (10,0), (12,0)
    });
    // Cartesian polynomial derivatives remain well defined at the pupil center.
    internal static (double Value, double Dx, double Dy) Evaluate(int index, double x, double y)
    {
        var (n, signedM) = Orders[index];
        return EvaluateOrder(n, signedM, x, y);
    }

    internal static (double Value, double Dx, double Dy) EvaluateOrder(int n, int signedM, double x, double y)
    {
        var m = Math.Abs(signedM);
        var real = 1.0;
        var imaginary = 0.0;
        var previousReal = 1.0;
        var previousImaginary = 0.0;
        for (var i = 0; i < m; i++)
        {
            previousReal = real;
            previousImaginary = imaginary;
            (real, imaginary) = (real * x - imaginary * y, real * y + imaginary * x);
        }
        var angular = signedM < 0 ? imaginary : real;
        var angularDx = m * (signedM < 0 ? previousImaginary : previousReal);
        var angularDy = m * (signedM < 0 ? previousReal : -previousImaginary);
        var order = (n - m) / 2;
        var argument = 2 * (x * x + y * y) - 1;
        // R_n^m = r^m P_order^(0,m)(2r² - 1), avoiding cancellation of large monomial coefficients.
        var radial = Jacobi(order, 0, m, argument);
        var derivative = order == 0 ? 0 : (order + m + 1) * Jacobi(order - 1, 1, m + 1, argument);
        return (radial * angular,
            2 * x * derivative * angular + radial * angularDx,
            2 * y * derivative * angular + radial * angularDy);
    }

    private static double Jacobi(int order, int a, int b, double argument)
    {
        if (order == 0) return 1;
        var previous = 1.0;
        var value = (a - b + (a + b + 2) * argument) / 2;
        for (var i = 2; i <= order; i++)
        {
            var sum = 2 * i + a + b;
            var next = ((sum - 1) * (sum * (sum - 2) * argument + a * a - b * b) * value
                - 2 * (i + a - 1) * (i + b - 1) * sum * previous)
                / (2 * i * (i + a + b) * (sum - 2));
            previous = value;
            value = next;
        }
        return value;
    }
}
