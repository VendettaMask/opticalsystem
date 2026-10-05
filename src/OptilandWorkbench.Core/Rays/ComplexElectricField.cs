using System.Numerics;
using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Rays;

/// <summary>A complex three-dimensional field, in an explicitly chosen coordinate frame.</summary>
public readonly record struct ComplexElectricField(Complex X, Complex Y, Complex Z)
{
    public double SquaredNorm => X.Magnitude * X.Magnitude + Y.Magnitude * Y.Magnitude + Z.Magnitude * Z.Magnitude;
    public Complex Dot(Vector3D vector) => X * vector.X + Y * vector.Y + Z * vector.Z;
    public static ComplexElectricField FromReal(Vector3D value) => new(value.X, value.Y, value.Z);
    public static ComplexElectricField operator *(ComplexElectricField field, Complex scale) => new(field.X * scale, field.Y * scale, field.Z * scale);
    public static ComplexElectricField operator +(ComplexElectricField a, ComplexElectricField b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

public enum PolarizationReferenceAxis { X, Y, Z }

/// <summary>The positive-time convention exp(+i omega t) uses negative propagation phase.</summary>
public enum HarmonicTimeConvention { Positive, Negative }

/// <summary>Relative Jones magnitudes and phases (degrees), normalized before tracing.</summary>
public sealed record JonesInputState(double Jx, double Jy, double XPhaseDegrees = 0, double YPhaseDegrees = 0,
    PolarizationReferenceAxis ReferenceAxis = PolarizationReferenceAxis.X)
{
    internal (Vector3D X, Vector3D Y, Complex Jx, Complex Jy) Resolve(Vector3D direction)
    {
        if (!double.IsFinite(Jx) || !double.IsFinite(Jy) || Jx < 0 || Jy < 0 || Math.Max(Jx, Jy) <= 0
            || !double.IsFinite(XPhaseDegrees) || !double.IsFinite(YPhaseDegrees) || !Enum.IsDefined(ReferenceAxis))
            throw new ArgumentException("偏振输入需要非负有限 Jx/Jy、至少一个非零分量、有限相位和有效参考轴。");
        var k = Unit(direction);
        Vector3D x, y;
        switch (ReferenceAxis)
        {
            case PolarizationReferenceAxis.X:
                y = Unit(Cross(k, new(1, 0, 0))); x = Unit(Cross(y, k)); break;
            case PolarizationReferenceAxis.Y:
                x = Unit(Cross(new(0, 1, 0), k)); y = Unit(Cross(k, x)); break;
            default:
                x = Unit(Cross(k, new(0, 0, 1))); y = Unit(Cross(k, x)); break;
        }
        var scale = Math.Max(Jx, Jy);
        var a = Jx / scale; var b = Jy / scale;
        var norm = Math.Sqrt(a * a + b * b);
        return (x, y, Complex.FromPolarCoordinates(a / norm, Math.IEEERemainder(XPhaseDegrees, 360) * Math.PI / 180),
            Complex.FromPolarCoordinates(b / norm, Math.IEEERemainder(YPhaseDegrees, 360) * Math.PI / 180));
    }

    private static Vector3D Unit(Vector3D value) => double.IsFinite(value.Length) && value.Length > 1e-15
        ? value / value.Length : throw new NotSupportedException("偏振参考轴与光线平行或方向无效，不能确定横向偏振基矢。");
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
