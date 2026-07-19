using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Materials;

public sealed class CatalogGlassMaterial : IMaterial
{
    public CatalogGlassMaterial(
        string name,
        string manufacturer,
        string formula,
        double minimumWavelengthNanometers,
        double maximumWavelengthNanometers,
        IReadOnlyList<double>? coefficients = null,
        IReadOnlyList<double>? refractiveIndexWavelengthsNanometers = null,
        IReadOnlyList<double>? refractiveIndices = null,
        IReadOnlyList<double>? extinctionWavelengthsNanometers = null,
        IReadOnlyList<double>? extinctionCoefficients = null,
        IPropagationModel? propagationModel = null)
    {
        Name = name;
        Manufacturer = manufacturer;
        Formula = formula;
        MinimumWavelengthNanometers = minimumWavelengthNanometers;
        MaximumWavelengthNanometers = maximumWavelengthNanometers;
        Coefficients = coefficients?.ToArray() ?? Array.Empty<double>();
        RefractiveIndexWavelengthsNanometers = refractiveIndexWavelengthsNanometers?.ToArray() ?? Array.Empty<double>();
        RefractiveIndices = refractiveIndices?.ToArray() ?? Array.Empty<double>();
        ExtinctionWavelengthsNanometers = extinctionWavelengthsNanometers?.ToArray() ?? Array.Empty<double>();
        ExtinctionCoefficients = extinctionCoefficients?.ToArray() ?? Array.Empty<double>();
        PropagationModel = propagationModel?.Clone() ?? new HomogeneousPropagationModel();

        if (RefractiveIndexWavelengthsNanometers.Count != RefractiveIndices.Count)
        {
            throw new ArgumentException("Catalog refractive-index wavelength and value arrays must have equal length.");
        }

        if (ExtinctionWavelengthsNanometers.Count != ExtinctionCoefficients.Count)
        {
            throw new ArgumentException("Catalog extinction wavelength and value arrays must have equal length.");
        }
    }

    public string Name { get; }

    public string Manufacturer { get; }

    public string CatalogName => Name.Contains(':', StringComparison.Ordinal)
        ? Name[(Name.IndexOf(':') + 1)..]
        : Name;

    public string Formula { get; }

    public double MinimumWavelengthNanometers { get; }

    public double MaximumWavelengthNanometers { get; }

    public IReadOnlyList<double> Coefficients { get; }

    public IReadOnlyList<double> RefractiveIndexWavelengthsNanometers { get; }

    public IReadOnlyList<double> RefractiveIndices { get; }

    public IReadOnlyList<double> ExtinctionWavelengthsNanometers { get; }

    public IReadOnlyList<double> ExtinctionCoefficients { get; }

    public IPropagationModel PropagationModel { get; }

    public double RefractiveIndex(double wavelengthNanometers)
    {
        var wavelengthMicrometers = wavelengthNanometers / 1000.0;
        return Formula switch
        {
            "formula 1" => Sellmeier(wavelengthMicrometers, squareResonance: true),
            "formula 2" => Sellmeier(wavelengthMicrometers, squareResonance: false),
            "formula 3" => Polynomial(wavelengthMicrometers, squareRoot: true),
            "formula 5" => Polynomial(wavelengthMicrometers, squareRoot: false),
            "tabulated n" or "tabulated nk" => MaterialInterpolation.Linear(
                wavelengthNanometers,
                RefractiveIndexWavelengthsNanometers,
                RefractiveIndices),
            _ => throw new NotSupportedException($"Glass dispersion formula '{Formula}' is not supported.")
        };
    }

    public double ExtinctionCoefficient(double wavelengthNanometers)
    {
        return MaterialInterpolation.Linear(
            wavelengthNanometers,
            ExtinctionWavelengthsNanometers,
            ExtinctionCoefficients);
    }

    public IMaterial Clone() => this;

    private double Sellmeier(double wavelengthMicrometers, bool squareResonance)
    {
        var wavelengthSquared = wavelengthMicrometers * wavelengthMicrometers;
        var indexSquared = 1.0 + Coefficients[0];
        for (var index = 1; index < Coefficients.Count; index += 2)
        {
            var resonance = squareResonance
                ? Coefficients[index + 1] * Coefficients[index + 1]
                : Coefficients[index + 1];
            indexSquared += Coefficients[index] * wavelengthSquared / (wavelengthSquared - resonance);
        }

        return Math.Sqrt(indexSquared);
    }

    private double Polynomial(double wavelengthMicrometers, bool squareRoot)
    {
        var value = Coefficients[0];
        for (var index = 1; index < Coefficients.Count; index += 2)
        {
            value += Coefficients[index] * Math.Pow(wavelengthMicrometers, Coefficients[index + 1]);
        }

        return squareRoot ? Math.Sqrt(value) : value;
    }
}
