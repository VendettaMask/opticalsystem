namespace OptilandWorkbench.Core.Materials;

public interface IMaterial
{
    string Name { get; }

    double RefractiveIndex(double wavelengthNanometers);

    double ExtinctionCoefficient(double wavelengthNanometers);

    IMaterial Clone();
}

public sealed class AirMaterial : IMaterial
{
    public string Name => "Air";

    public double RefractiveIndex(double wavelengthNanometers) => 1.0;

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0.0;

    public IMaterial Clone() => new AirMaterial();
}

public sealed class ConstantIndexMaterial : IMaterial
{
    public ConstantIndexMaterial(string name, double refractiveIndex, double extinctionCoefficient = 0)
    {
        Name = string.IsNullOrWhiteSpace(name) ? $"n={refractiveIndex:0.###}" : name;
        Index = refractiveIndex;
        Extinction = extinctionCoefficient;
    }

    public string Name { get; }

    public double Index { get; }

    public double Extinction { get; }

    public double RefractiveIndex(double wavelengthNanometers) => Index;

    public double ExtinctionCoefficient(double wavelengthNanometers) => Extinction;

    public IMaterial Clone() => new ConstantIndexMaterial(Name, Index, Extinction);
}

public sealed class CauchyMaterial : IMaterial
{
    public CauchyMaterial(string name, double a, double b, double c = 0)
    {
        Name = name;
        A = a;
        B = b;
        C = c;
    }

    public string Name { get; }

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

    public IMaterial Clone() => new CauchyMaterial(Name, A, B, C);
}

public sealed class SellmeierMaterial : IMaterial
{
    public SellmeierMaterial(string name, IReadOnlyList<double> b, IReadOnlyList<double> c)
    {
        if (b.Count != c.Count)
        {
            throw new ArgumentException("Sellmeier B and C coefficient arrays must have equal length.");
        }

        Name = name;
        B = b.ToArray();
        C = c.ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<double> B { get; }

    public IReadOnlyList<double> C { get; }

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

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;

    public IMaterial Clone() => new SellmeierMaterial(Name, B, C);
}

public sealed class AbbeMaterial : IMaterial
{
    public AbbeMaterial(string name, double nd, double vd)
    {
        Name = name;
        Nd = nd;
        Vd = vd;
    }

    public string Name { get; }

    public double Nd { get; }

    public double Vd { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var dispersion = (Nd - 1.0) / Math.Max(1.0, Vd);
        return Nd - dispersion * ((wavelengthNanometers - 587.6) / (656.3 - 486.1));
    }

    public double ExtinctionCoefficient(double wavelengthNanometers) => 0;

    public IMaterial Clone() => new AbbeMaterial(Name, Nd, Vd);
}
