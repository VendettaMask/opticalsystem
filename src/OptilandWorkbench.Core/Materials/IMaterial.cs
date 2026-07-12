using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Materials;

public interface IMaterial
{
    string Name { get; }

    IPropagationModel PropagationModel { get; }

    double RefractiveIndex(double wavelengthNanometers);

    double ExtinctionCoefficient(double wavelengthNanometers);

    IMaterial Clone();
}

public sealed class AirMaterial : IMaterial
{
    public string Name => "Air";

    public IPropagationModel PropagationModel { get; } = new HomogeneousPropagationModel();

    public double RefractiveIndex(double wavelengthNanometers) => 1.0;

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0.0;

    public IMaterial Clone() => new AirMaterial();
}

public sealed class ConstantIndexMaterial : IMaterial
{
    public ConstantIndexMaterial(
        string name,
        double refractiveIndex,
        double extinctionCoefficient = 0,
        IPropagationModel? propagationModel = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? $"n={refractiveIndex:0.###}" : name;
        Index = refractiveIndex;
        Extinction = extinctionCoefficient;
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }

    public IPropagationModel PropagationModel { get; }

    public double Index { get; }

    public double Extinction { get; }

    public double RefractiveIndex(double wavelengthNanometers) => Index;

    public double ExtinctionCoefficient(double wavelengthNanometers) => Extinction;

    public IMaterial Clone() => new ConstantIndexMaterial(Name, Index, Extinction, PropagationModel);
}

public sealed class CauchyMaterial : IMaterial
{
    public CauchyMaterial(string name, double a, double b, double c = 0, IPropagationModel? propagationModel = null)
    {
        Name = name;
        A = a;
        B = b;
        C = c;
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }

    public IPropagationModel PropagationModel { get; }

    public double A { get; }

    public double B { get; }

    public double C { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var microns = wavelengthNanometers / 1000.0;
        var lambda2 = microns * microns;
        return A + (B / lambda2) + (C / (lambda2 * lambda2));
    }

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;

    public IMaterial Clone() => new CauchyMaterial(Name, A, B, C, PropagationModel);
}

public sealed class SellmeierMaterial : IMaterial
{
    public SellmeierMaterial(
        string name,
        IReadOnlyList<double> b,
        IReadOnlyList<double> c,
        IPropagationModel? propagationModel = null,
        IReadOnlyList<double>? extinctionWavelengthsNanometers = null,
        IReadOnlyList<double>? extinctionCoefficients = null)
    {
        if (b.Count != c.Count)
        {
            throw new ArgumentException("Sellmeier B and C coefficient arrays must have equal length.");
        }

        if ((extinctionWavelengthsNanometers?.Count ?? 0) != (extinctionCoefficients?.Count ?? 0))
        {
            throw new ArgumentException("Extinction wavelength and coefficient arrays must have equal length.");
        }

        Name = name;
        B = b.ToArray();
        C = c.ToArray();
        ExtinctionWavelengthsNanometers = extinctionWavelengthsNanometers?.ToArray() ?? Array.Empty<double>();
        ExtinctionCoefficients = extinctionCoefficients?.ToArray() ?? Array.Empty<double>();
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }

    public IPropagationModel PropagationModel { get; }

    public IReadOnlyList<double> B { get; }

    public IReadOnlyList<double> C { get; }

    public IReadOnlyList<double> ExtinctionWavelengthsNanometers { get; }

    public IReadOnlyList<double> ExtinctionCoefficients { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var lambda = wavelengthNanometers / 1000.0;
        var lambda2 = lambda * lambda;
        var n2 = 1.0;
        for (var index = 0; index < B.Count; index++)
        {
            n2 += B[index] * lambda2 / (lambda2 - C[index]);
        }

        return Math.Sqrt(Math.Max(1.0, n2));
    }

    public double ExtinctionCoefficient(double wavelengthNanometers)
    {
        return MaterialInterpolation.Linear(
            wavelengthNanometers,
            ExtinctionWavelengthsNanometers,
            ExtinctionCoefficients);
    }

    public IMaterial Clone() => new SellmeierMaterial(
        Name,
        B,
        C,
        PropagationModel,
        ExtinctionWavelengthsNanometers,
        ExtinctionCoefficients);
}

public sealed class PolynomialDispersionMaterial : IMaterial
{
    public PolynomialDispersionMaterial(
        string name,
        IReadOnlyList<double> coefficients,
        IPropagationModel? propagationModel = null,
        IReadOnlyList<double>? extinctionWavelengthsNanometers = null,
        IReadOnlyList<double>? extinctionCoefficients = null)
    {
        if (coefficients.Count < 1 || coefficients.Count % 2 == 0)
        {
            throw new ArgumentException(
                "Polynomial dispersion coefficients must contain a constant followed by coefficient/exponent pairs.",
                nameof(coefficients));
        }

        if ((extinctionWavelengthsNanometers?.Count ?? 0) != (extinctionCoefficients?.Count ?? 0))
        {
            throw new ArgumentException("Extinction wavelength and coefficient arrays must have equal length.");
        }

        Name = name;
        Coefficients = coefficients.ToArray();
        ExtinctionWavelengthsNanometers = extinctionWavelengthsNanometers?.ToArray() ?? Array.Empty<double>();
        ExtinctionCoefficients = extinctionCoefficients?.ToArray() ?? Array.Empty<double>();
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }

    public IPropagationModel PropagationModel { get; }

    public IReadOnlyList<double> Coefficients { get; }

    public IReadOnlyList<double> ExtinctionWavelengthsNanometers { get; }

    public IReadOnlyList<double> ExtinctionCoefficients { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var wavelengthMicrometers = wavelengthNanometers / 1000.0;
        var n2 = Coefficients[0];
        for (var index = 1; index < Coefficients.Count; index += 2)
        {
            n2 += Coefficients[index] * Math.Pow(wavelengthMicrometers, Coefficients[index + 1]);
        }

        return Math.Sqrt(Math.Max(1.0, n2));
    }

    public double ExtinctionCoefficient(double wavelengthNanometers)
    {
        return MaterialInterpolation.Linear(
            wavelengthNanometers,
            ExtinctionWavelengthsNanometers,
            ExtinctionCoefficients);
    }

    public IMaterial Clone() => new PolynomialDispersionMaterial(
        Name,
        Coefficients,
        PropagationModel,
        ExtinctionWavelengthsNanometers,
        ExtinctionCoefficients);
}

public sealed class AbbeMaterial : IMaterial
{
    public AbbeMaterial(string name, double nd, double vd, IPropagationModel? propagationModel = null)
    {
        Name = name;
        Nd = nd;
        Vd = vd;
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();
    }

    public string Name { get; }

    public IPropagationModel PropagationModel { get; }

    public double Nd { get; }

    public double Vd { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var dispersion = (Nd - 1.0) / Math.Max(1.0, Vd);
        return Nd - dispersion * ((wavelengthNanometers - 587.6) / (656.3 - 486.1));
    }

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;

    public IMaterial Clone() => new AbbeMaterial(Name, Nd, Vd, PropagationModel);
}

internal static class MaterialInterpolation
{
    public static double Linear(
        double wavelengthNanometers,
        IReadOnlyList<double> wavelengthsNanometers,
        IReadOnlyList<double> values)
    {
        if (wavelengthsNanometers.Count == 0)
        {
            return 0;
        }

        if (wavelengthNanometers <= wavelengthsNanometers[0])
        {
            return values[0];
        }

        for (var index = 1; index < wavelengthsNanometers.Count; index++)
        {
            if (wavelengthNanometers <= wavelengthsNanometers[index])
            {
                var lowerWavelength = wavelengthsNanometers[index - 1];
                var fraction = (wavelengthNanometers - lowerWavelength)
                    / (wavelengthsNanometers[index] - lowerWavelength);
                return values[index - 1] + (fraction * (values[index] - values[index - 1]));
            }
        }

        return values[^1];
    }
}
