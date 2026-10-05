using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.Materials;

/// <summary>
/// Gradient 5 reference index: n0 + nr2*r² + nr4*r⁴ + nz1*z + nz2*z² + nz3*z³ + nz4*z⁴.
/// Coordinates are lens units. This volume profile does not encode the separate boundary sag/tilt.
/// </summary>
public sealed class Gradient5IndexProfile : ISpatialRefractiveIndex
{
    public Gradient5IndexProfile(double baseIndex, double radial2 = 0, double radial4 = 0,
        double axial1 = 0, double axial2 = 0, double axial3 = 0, double axial4 = 0,
        Gradient5Dispersion? dispersion = null)
    {
        SpatialIndexValidation.Coefficients(baseIndex, radial2, radial4, axial1, axial2, axial3, axial4);
        BaseIndex = baseIndex; Radial2 = radial2; Radial4 = radial4;
        Axial1 = axial1; Axial2 = axial2; Axial3 = axial3; Axial4 = axial4;
        Dispersion = dispersion;
    }

    public double BaseIndex { get; }
    public double Radial2 { get; }
    public double Radial4 { get; }
    public double Axial1 { get; }
    public double Axial2 { get; }
    public double Axial3 { get; }
    public double Axial4 { get; }
    public Gradient5Dispersion? Dispersion { get; }

    public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) =>
        Evaluate(localPosition, wavelengthNanometers).Index;

    public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers)
    {
        SpatialIndexValidation.Input(localPosition, wavelengthNanometers);
        var (x, y, z) = localPosition;
        var r2 = x * x + y * y;
        var reference = BaseIndex + r2 * (Radial2 + r2 * Radial4)
            + z * (Axial1 + z * (Axial2 + z * (Axial3 + z * Axial4)));
        var radial = 2 * Radial2 + 4 * Radial4 * r2;
        var gradient = new Vector3D(x * radial, y * radial,
            Axial1 + z * (2 * Axial2 + z * (3 * Axial3 + z * 4 * Axial4)));
        _ = SpatialIndexValidation.Sample(reference, gradient);
        if (Dispersion is null) return new(reference, gradient);
        var (index, derivative) = Dispersion.Evaluate(reference, wavelengthNanometers);
        return SpatialIndexValidation.Sample(index, gradient * derivative);
    }

    internal double AxialTransverseCurvature(double z, double wavelengthNanometers)
    {
        var reference = BaseIndex + z * (Axial1 + z * (Axial2 + z * (Axial3 + z * Axial4)));
        var derivative = Dispersion?.Evaluate(reference, wavelengthNanometers).Derivative ?? 1;
        return 2 * Radial2 * derivative;
    }
}

/// <summary>
/// Three generalized Sellmeier terms. K and L are polynomials in the local reference index,
/// in ascending powers (1..8 coefficients each, a common count per family).
/// Wavelength arguments/range are nanometers; the formula uses micrometers, so L has μm² units.
/// </summary>
public sealed class Gradient5Dispersion
{
    public Gradient5Dispersion(double referenceWavelengthNanometers, double minimumWavelengthNanometers,
        double maximumWavelengthNanometers, IReadOnlyList<IReadOnlyList<double>> k,
        IReadOnlyList<IReadOnlyList<double>> l)
    {
        if (!double.IsFinite(minimumWavelengthNanometers) || minimumWavelengthNanometers <= 0
            || !double.IsFinite(maximumWavelengthNanometers) || maximumWavelengthNanometers < minimumWavelengthNanometers
            || !double.IsFinite(referenceWavelengthNanometers)
            || referenceWavelengthNanometers < minimumWavelengthNanometers || referenceWavelengthNanometers > maximumWavelengthNanometers)
            throw new ArgumentOutOfRangeException(nameof(referenceWavelengthNanometers), "GRIN 色散需要有限正波长范围，参考波长必须在范围内。");
        ReferenceWavelengthNanometers = referenceWavelengthNanometers;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
        K = Copy(k); L = Copy(l);
    }

    public double ReferenceWavelengthNanometers { get; }
    public double MinimumWavelengthNanometers { get; }
    public double MaximumWavelengthNanometers { get; }
    public IReadOnlyList<IReadOnlyList<double>> K { get; }
    public IReadOnlyList<IReadOnlyList<double>> L { get; }

    internal (double Index, double Derivative) Evaluate(double referenceIndex, double wavelengthNanometers)
    {
        _ = SpatialIndexValidation.Sample(referenceIndex, Vector3D.Zero);
        ValidateWavelength(wavelengthNanometers);
        var square = Math.Pow(wavelengthNanometers * .001, 2);
        var difference = (wavelengthNanometers - ReferenceWavelengthNanometers) * .001
            * ((wavelengthNanometers + ReferenceWavelengthNanometers) * .001);
        var indexSquared = referenceIndex * referenceIndex;
        var slope = 2 * referenceIndex;
        for (var i = 0; i < 3; i++)
        {
            var (k, dk) = Polynomial(K[i], referenceIndex);
            var (l, dl) = Polynomial(L[i], referenceIndex);
            var denominator = square - l;
            if (!double.IsFinite(denominator) || denominator == 0)
                throw new SpatialIndexDomainException("GRIN 色散波长位于 Sellmeier 极点或超出有限数值范围。");
            indexSquared += difference * k / denominator;
            slope += difference / denominator * (dk + k * dl / denominator);
        }
        if (!double.IsFinite(indexSquared) || indexSquared <= 0)
            throw new SpatialIndexDomainException("GRIN 色散后的折射率平方必须为有限正数。");
        var index = Math.Sqrt(indexSquared);
        var derivative = slope / (2 * index);
        if (!double.IsFinite(derivative)) throw new SpatialIndexDomainException("GRIN 色散空间导数不是有限值。");
        return (index, derivative);
    }

    internal void ValidateWavelength(double wavelength)
    {
        if (!double.IsFinite(wavelength) || wavelength < MinimumWavelengthNanometers || wavelength > MaximumWavelengthNanometers)
            throw new SpatialIndexDomainException("请求波长不在 GRIN 材料声明的色散范围内；不能外推。");
    }

    private static IReadOnlyList<IReadOnlyList<double>> Copy(IReadOnlyList<IReadOnlyList<double>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count != 3 || source.Any(row => row is null || row.Count is < 1 or > 8)
            || source.Any(row => row.Count != source[0].Count || row.Any(value => !double.IsFinite(value))))
            throw new ArgumentException("GRIN 色散 K/L 每组必须各有三行，每行 1～8 个有限系数且组内长度相同。", nameof(source));
        return Array.AsReadOnly(source.Select(row => (IReadOnlyList<double>)Array.AsReadOnly(row.ToArray())).ToArray());
    }

    private static (double Value, double Derivative) Polynomial(IReadOnlyList<double> coefficients, double argument)
    {
        var value = coefficients[^1]; var derivative = 0.0;
        for (var j = coefficients.Count - 2; j >= 0; j--)
        {
            derivative = derivative * argument + value;
            value = value * argument + coefficients[j];
        }
        if (!double.IsFinite(value) || !double.IsFinite(derivative))
            throw new SpatialIndexDomainException("GRIN 色散多项式超出有限数值范围。");
        return (value, derivative);
    }
}
