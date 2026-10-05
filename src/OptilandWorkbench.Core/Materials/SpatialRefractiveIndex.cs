using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Materials;

/// <summary>Index and its gradient in the same local, lens-unit coordinate system.</summary>
public readonly record struct SpatialIndexSample(double Index, Vector3D Gradient);

/// <summary>A trial point is outside the field's finite, positive-index domain.</summary>
public sealed class SpatialIndexDomainException(string message) : InvalidOperationException(message);

/// <summary>
/// A continuously differentiable index field, not a homogeneous IMaterial.
/// Implementations must be immutable and return a positive finite index.
/// </summary>
public interface ISpatialRefractiveIndex
{
    double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers);

    SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers);
}

/// <summary>Gradient 1: n = n0 + nr2 r² + nr1 r. No dispersion.</summary>
public sealed class Gradient1IndexProfile : ISpatialRefractiveIndex
{
    public Gradient1IndexProfile(double baseIndex, double radialQuadratic = 0, double radialLinear = 0)
    {
        SpatialIndexValidation.Coefficients(baseIndex, radialQuadratic, radialLinear);
        BaseIndex = baseIndex;
        RadialQuadratic = radialQuadratic;
        RadialLinear = radialLinear;
    }

    public double BaseIndex { get; }
    public double RadialQuadratic { get; }
    public double RadialLinear { get; }

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var r2 = localPosition.X * localPosition.X + localPosition.Y * localPosition.Y;
        return SpatialIndexValidation.Sample(BaseIndex + RadialQuadratic * r2 + RadialLinear * Math.Sqrt(r2), Vector3D.Zero).Index;
    }

    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var (x, y, _) = localPosition;
        var r2 = x * x + y * y;
        var radius = Math.Sqrt(r2);
        if (radius == 0 && RadialLinear != 0)
            throw new NotSupportedException("Gradient 1 的非零径向一次项在轴上不可微，不能用零梯度代替。原生轴上规则尚待核实。");
        var derivative = 2 * RadialQuadratic + (RadialLinear == 0 ? 0 : RadialLinear / radius);
        return SpatialIndexValidation.Sample(BaseIndex + RadialQuadratic * r2 + RadialLinear * radius,
            new Vector3D(derivative * x, derivative * y, 0));
    }
}

/// <summary>Gradient 2: n² = n0 + nr2 r² + … + nr12 r¹². n0 is index SQUARED.</summary>
public sealed class Gradient2IndexProfile : ISpatialRefractiveIndex
{
    public Gradient2IndexProfile(double baseIndexSquared, double radial2 = 0, double radial4 = 0,
        double radial6 = 0, double radial8 = 0, double radial10 = 0, double radial12 = 0)
    {
        SpatialIndexValidation.Coefficients(baseIndexSquared, radial2, radial4, radial6, radial8, radial10, radial12);
        BaseIndexSquared = baseIndexSquared;
        RadialCoefficients = Array.AsReadOnly(new[] { radial2, radial4, radial6, radial8, radial10, radial12 });
    }

    public double BaseIndexSquared { get; }
    public IReadOnlyList<double> RadialCoefficients { get; }

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => Evaluate(localPosition, wavelengthNanometers).Index;

    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var (x, y, _) = localPosition;
        var r2 = x * x + y * y;
        var value = RadialCoefficients[^1];
        var derivative = 0.0;
        for (var i = RadialCoefficients.Count - 2; i >= 0; i--)
        {
            derivative = derivative * r2 + value;
            value = value * r2 + RadialCoefficients[i];
        }
        derivative = derivative * r2 + value;
        var square = BaseIndexSquared + value * r2;
        if (!double.IsFinite(square) || square <= 0)
            throw new SpatialIndexDomainException("Gradient 2 在当前点的折射率平方必须为有限正数。");
        var index = Math.Sqrt(square);
        return SpatialIndexValidation.Sample(index, new Vector3D(x * derivative / index, y * derivative / index, 0));
    }
}

/// <summary>Gradient 3: n = n0 + nr2 r² + nr4 r⁴ + nr6 r⁶ + nz1 z + nz2 z² + nz3 z³.</summary>
public sealed class Gradient3IndexProfile : ISpatialRefractiveIndex
{
    public Gradient3IndexProfile(double baseIndex, double radial2 = 0, double radial4 = 0, double radial6 = 0,
        double axial1 = 0, double axial2 = 0, double axial3 = 0)
    {
        SpatialIndexValidation.Coefficients(baseIndex, radial2, radial4, radial6, axial1, axial2, axial3);
        BaseIndex = baseIndex;
        Radial2 = radial2;
        Radial4 = radial4;
        Radial6 = radial6;
        Axial1 = axial1;
        Axial2 = axial2;
        Axial3 = axial3;
    }

    public double BaseIndex { get; }
    public double Radial2 { get; }
    public double Radial4 { get; }
    public double Radial6 { get; }
    public double Axial1 { get; }
    public double Axial2 { get; }
    public double Axial3 { get; }

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => Evaluate(localPosition, wavelengthNanometers).Index;

    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var (x, y, z) = localPosition;
        var r2 = x * x + y * y;
        var index = BaseIndex + r2 * (Radial2 + r2 * (Radial4 + r2 * Radial6))
            + z * (Axial1 + z * (Axial2 + z * Axial3));
        var radialDerivative = 2 * (Radial2 + r2 * (2 * Radial4 + 3 * r2 * Radial6));
        return SpatialIndexValidation.Sample(index, new Vector3D(x * radialDerivative, y * radialDerivative,
            Axial1 + z * (2 * Axial2 + 3 * z * Axial3)));
    }
}

/// <summary>Gradient 4: n = n0 + nx1 x + nx2 x² + ny1 y + ny2 y² + nz1 z + nz2 z².</summary>
public sealed class Gradient4IndexProfile : ISpatialRefractiveIndex
{
    public Gradient4IndexProfile(double baseIndex, double x1 = 0, double x2 = 0,
        double y1 = 0, double y2 = 0, double z1 = 0, double z2 = 0)
    {
        SpatialIndexValidation.Coefficients(baseIndex, x1, x2, y1, y2, z1, z2);
        BaseIndex = baseIndex;
        Linear = new Vector3D(x1, y1, z1);
        Quadratic = new Vector3D(x2, y2, z2);
    }

    public double BaseIndex { get; }
    public Vector3D Linear { get; }
    public Vector3D Quadratic { get; }

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => Evaluate(localPosition, wavelengthNanometers).Index;

    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var (x, y, z) = localPosition;
        var index = BaseIndex + x * (Linear.X + x * Quadratic.X)
            + y * (Linear.Y + y * Quadratic.Y) + z * (Linear.Z + z * Quadratic.Z);
        return SpatialIndexValidation.Sample(index, new Vector3D(Linear.X + 2 * x * Quadratic.X,
            Linear.Y + 2 * y * Quadratic.Y, Linear.Z + 2 * z * Quadratic.Z));
    }
}

internal static class SpatialIndexValidation
{
    internal static void Coefficients(double baseValue, params double[] coefficients)
    {
        if (!double.IsFinite(baseValue) || baseValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseValue), "基准折射率（或其平方）必须为有限正数。");
        if (coefficients.Any(value => !double.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(coefficients), "梯度系数必须为有限数。");
    }

    internal static void Input(Vector3D position, double wavelength)
    {
        if (!Finite(position)) throw new ArgumentOutOfRangeException(nameof(position));
        if (!double.IsFinite(wavelength) || wavelength <= 0) throw new ArgumentOutOfRangeException(nameof(wavelength));
    }

    internal static bool Finite(Vector3D vector) => double.IsFinite(vector.X) && double.IsFinite(vector.Y) && double.IsFinite(vector.Z);

    internal static SpatialIndexSample Sample(double index, Vector3D gradient)
    {
        if (!double.IsFinite(index) || index <= 0 || !Finite(gradient))
            throw new SpatialIndexDomainException("GRIN 折射率必须为有限正数，空间梯度必须为有限向量。");
        return new SpatialIndexSample(index, gradient);
    }
}
