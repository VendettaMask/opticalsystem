using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.Services;

/// <summary>Exact first and second derivatives of the local sag graph, not finite differences.</summary>
public readonly record struct SurfaceSagDifferential(double Dx, double Dy, double Dxx, double Dxy, double Dyy)
{
    public double Slope(double x, double y) => Dx * x + Dy * y;
    public double SecondDerivative(double x, double y) => Dxx * x * x + 2 * Dxy * x * y + Dyy * y * y;
    public double NormalCurvature(double x, double y) => SecondDerivative(x, y)
        / (Math.Sqrt(1 + Dx * Dx + Dy * Dy) * (1 + Math.Pow(Slope(x, y), 2)));
}

public static class SurfaceDifferentialMetrics
{
    public static SurfaceSagDifferential At(OpticalSurface surface, double x, double y)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        OpticCapabilityPreflight.EnsureSurfaceSupported(surface, OpticCapabilityOperation.Optimization);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(x * x + y * y))
            throw new ArgumentOutOfRangeException(nameof(x), "面形坐标必须为有限数值。");
        if (!double.IsFinite(surface.Geometry.Sag(x, y)))
            throw new InvalidOperationException("指定点超出面形有限定义域。");
        var result = surface.Geometry switch
        {
            PlaneGeometry => new SurfaceSagDifferential(),
            StandardGeometry standard => Radial(standard, [], false, x, y),
            EvenAsphereGeometry even => Radial(even.Base, even.Coefficients, false, x, y),
            OddAsphereGeometry odd => Radial(odd.Base, odd.Coefficients, true, x, y),
            PolynomialGeometry polynomial => Polynomial(polynomial, x, y),
            _ => throw new NotSupportedException("面形导数目前支持标准面、Even/Odd Asphere 和 XY 多项式面。")
        };
        if (new[] { result.Dx, result.Dy, result.Dxx, result.Dxy, result.Dyy }.Any(v => !double.IsFinite(v)))
            throw new InvalidOperationException("该点没有有限的一阶和二阶面形导数。");
        return result;
    }

    public static double Derivative(OpticalSurface surface, double x, double y, int data)
    {
        if (data is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(data));
        var d = At(surface, x, y);
        var (tx, ty) = RadialDirection(x, y);
        var (dx, dy) = data is 1 or 3 ? (-ty, tx) : (tx, ty);
        return data < 2 ? d.Slope(dx, dy) : d.SecondDerivative(dx, dy);
    }

    public static double Curvature(OpticalSurface surface, double x, double y, int data)
    {
        if (data is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(data));
        if (data is 3 or 7 or 9)
        {
            var maximum = 0.0;
            for (var i = 0; i < 50; i++)
                maximum = Math.Max(maximum, Math.Abs(Curvature(surface, x * (i / 49.0), y * (i / 49.0), data - 1)));
            return maximum;
        }
        var d = At(surface, x, y);
        var (tx, ty) = RadialDirection(x, y);
        var tangential = d.NormalCurvature(tx, ty);
        var sagittal = d.NormalCurvature(-ty, tx);
        return data switch
        {
            0 => tangential,
            1 => sagittal,
            2 => tangential - sagittal,
            4 => d.NormalCurvature(1, 0),
            5 => d.NormalCurvature(0, 1),
            6 => d.NormalCurvature(1, 0) - d.NormalCurvature(0, 1),
            _ => Math.Abs(Math.Sqrt(x * x + y * y) * sagittal)
        };
    }

    // At the vertex choose tangential +Y and sagittal -X. Rotational surfaces
    // have identical curvatures there; this fixes the orientation for XY polynomials.
    private static (double X, double Y) RadialDirection(double x, double y)
    {
        var r = Math.Sqrt(x * x + y * y);
        return r == 0 ? (0, 1) : (x / r, y / r);
    }

    private static SurfaceSagDifferential Radial(StandardGeometry standard, IReadOnlyList<double> coefficients,
        bool odd, double x, double y)
    {
        var r = Math.Sqrt(x * x + y * y);
        if (double.IsNaN(standard.Radius) || !double.IsFinite(standard.Conic))
            throw new InvalidOperationException("无效曲率半径或圆锥系数。");
        var c = double.IsInfinity(standard.Radius) || Math.Abs(standard.Radius) < 1e-12 ? 0 : 1 / standard.Radius;
        var q = 1 - (1 + standard.Conic) * c * c * r * r;
        if (q <= 0) throw new InvalidOperationException("面形定义域边界没有有限导数。");
        var first = c * r / Math.Sqrt(q);
        var second = c / Math.Pow(q, 1.5);
        for (var i = 0; i < coefficients.Count; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var a = coefficients[i];
            if (!double.IsFinite(a)) throw new InvalidOperationException("非有限面形系数。");
            if (a == 0) continue;
            var power = odd ? i + 1 : 2 * (i + 1);
            if (r == 0 && power == 1) throw new InvalidOperationException("含非零 r 项的面形在轴上不可微。");
            first += a * power * Math.Pow(r, power - 1);
            if (power > 1) second += a * power * (power - 1) * Math.Pow(r, power - 2);
        }
        if (r == 0) return new(0, 0, second, 0, second);
        var tx = x / r;
        var ty = y / r;
        var transverse = first / r;
        return new(first * tx, first * ty,
            second * tx * tx + transverse * ty * ty,
            (second - transverse) * tx * ty,
            second * ty * ty + transverse * tx * tx);
    }

    private static SurfaceSagDifferential Polynomial(PolynomialGeometry polynomial, double x, double y)
    {
        var dx = 0.0; var dy = 0.0; var dxx = 0.0; var dxy = 0.0; var dyy = 0.0;
        foreach (var ((p, q), a) in polynomial.Coefficients)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (p < 0 || q < 0 || !double.IsFinite(a)) throw new InvalidOperationException("无效 XY 多项式项。");
            if (a == 0) continue;
            if (p > 0) dx += a * p * Math.Pow(x, p - 1) * Math.Pow(y, q);
            if (q > 0) dy += a * q * Math.Pow(x, p) * Math.Pow(y, q - 1);
            if (p > 1) dxx += a * p * (p - 1.0) * Math.Pow(x, p - 2) * Math.Pow(y, q);
            if (p > 0 && q > 0) dxy += a * p * q * Math.Pow(x, p - 1) * Math.Pow(y, q - 1);
            if (q > 1) dyy += a * q * (q - 1.0) * Math.Pow(x, p) * Math.Pow(y, q - 2);
        }
        return new(dx, dy, dxx, dxy, dyy);
    }
}
