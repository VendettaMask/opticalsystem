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
        IPropagationModel? propagationModel = null,
        OpticalGlassDefinition? zemaxData = null)
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
        ZemaxData = zemaxData;

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

    public OpticalGlassDefinition? ZemaxData { get; }

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
            _ when Formula.StartsWith("zemax formula ", StringComparison.Ordinal) =>
                ZemaxRefractiveIndex(wavelengthMicrometers),
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

    private double ZemaxRefractiveIndex(double wavelengthMicrometers)
    {
        if (!int.TryParse(Formula.AsSpan("zemax formula ".Length), out var formulaNumber))
        {
            throw new InvalidDataException($"Invalid Zemax dispersion formula identifier '{Formula}'.");
        }

        var wavelengthSquared = wavelengthMicrometers * wavelengthMicrometers;
        return formulaNumber switch
        {
            1 => Math.Sqrt(
                Coefficient(0) +
                Coefficient(1) * wavelengthSquared +
                Coefficient(2) / wavelengthSquared +
                Coefficient(3) / Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(4) / Math.Pow(wavelengthMicrometers, 6) +
                Coefficient(5) / Math.Pow(wavelengthMicrometers, 8)),
            2 => Math.Sqrt(1 + SellmeierTerm(0, 1, wavelengthSquared) +
                SellmeierTerm(2, 3, wavelengthSquared) +
                SellmeierTerm(4, 5, wavelengthSquared)),
            3 => Herzberger(wavelengthMicrometers, wavelengthSquared),
            4 => Math.Sqrt(1 + Coefficient(0) +
                Coefficient(1) * wavelengthSquared /
                    (wavelengthSquared - Coefficient(2) * Coefficient(2)) +
                Coefficient(3) /
                    (wavelengthSquared - Coefficient(4) * Coefficient(4))),
            5 => Coefficient(0) + Coefficient(1) / wavelengthMicrometers +
                Coefficient(2) / Math.Pow(wavelengthMicrometers, 3.5),
            6 => Math.Sqrt(1 + SellmeierTerm(0, 1, wavelengthSquared) +
                SellmeierTerm(2, 3, wavelengthSquared) +
                SellmeierTerm(4, 5, wavelengthSquared) +
                SellmeierTerm(6, 7, wavelengthSquared)),
            7 => Math.Sqrt(Coefficient(0) + Coefficient(1) /
                (wavelengthSquared - Coefficient(2)) - Coefficient(3) * wavelengthSquared),
            8 => Math.Sqrt(Coefficient(0) + Coefficient(1) * wavelengthSquared /
                (wavelengthSquared - Coefficient(2)) - Coefficient(3) * wavelengthSquared),
            9 => Math.Sqrt(Coefficient(0) +
                Coefficient(1) * wavelengthSquared / (wavelengthSquared - Coefficient(2)) +
                Coefficient(3) * wavelengthSquared / (wavelengthSquared - Coefficient(4))),
            10 => Math.Sqrt(Extended(wavelengthMicrometers, wavelengthSquared, extendedKind: 1)),
            11 => Math.Sqrt(1 + SellmeierTerm(0, 1, wavelengthSquared) +
                SellmeierTerm(2, 3, wavelengthSquared) +
                SellmeierTerm(4, 5, wavelengthSquared) +
                SellmeierTerm(6, 7, wavelengthSquared) +
                SellmeierTerm(8, 9, wavelengthSquared)),
            12 => Math.Sqrt(Extended(wavelengthMicrometers, wavelengthSquared, extendedKind: 2)),
            13 => Math.Sqrt(Extended(wavelengthMicrometers, wavelengthSquared, extendedKind: 3)),
            _ => throw new NotSupportedException($"Zemax dispersion formula {formulaNumber} is not supported.")
        };
    }

    private double Herzberger(double wavelengthMicrometers, double wavelengthSquared)
    {
        var reciprocal = 1.0 / (wavelengthSquared - 0.028);
        return Coefficient(0) + Coefficient(1) * reciprocal +
            Coefficient(2) * reciprocal * reciprocal +
            Coefficient(3) * wavelengthSquared +
            Coefficient(4) * Math.Pow(wavelengthMicrometers, 4) +
            Coefficient(5) * Math.Pow(wavelengthMicrometers, 6);
    }

    private double SellmeierTerm(int numeratorIndex, int denominatorIndex, double wavelengthSquared) =>
        Coefficient(numeratorIndex) * wavelengthSquared /
        (wavelengthSquared - Coefficient(denominatorIndex));

    private double Extended(double wavelengthMicrometers, double wavelengthSquared, int extendedKind)
    {
        return extendedKind switch
        {
            1 => Coefficient(0) + Coefficient(1) * wavelengthSquared +
                Coefficient(2) / wavelengthSquared +
                Coefficient(3) / Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(4) / Math.Pow(wavelengthMicrometers, 6) +
                Coefficient(5) / Math.Pow(wavelengthMicrometers, 8) +
                Coefficient(6) / Math.Pow(wavelengthMicrometers, 10) +
                Coefficient(7) / Math.Pow(wavelengthMicrometers, 12),
            2 => Coefficient(0) + Coefficient(1) * wavelengthSquared +
                Coefficient(2) / wavelengthSquared +
                Coefficient(3) / Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(4) / Math.Pow(wavelengthMicrometers, 6) +
                Coefficient(5) / Math.Pow(wavelengthMicrometers, 8) +
                Coefficient(6) * Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(7) * Math.Pow(wavelengthMicrometers, 6),
            3 => Coefficient(0) + Coefficient(1) * wavelengthSquared +
                Coefficient(2) * Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(3) / wavelengthSquared +
                Coefficient(4) / Math.Pow(wavelengthMicrometers, 4) +
                Coefficient(5) / Math.Pow(wavelengthMicrometers, 6) +
                Coefficient(6) / Math.Pow(wavelengthMicrometers, 8) +
                Coefficient(7) / Math.Pow(wavelengthMicrometers, 10) +
                Coefficient(8) / Math.Pow(wavelengthMicrometers, 12),
            _ => throw new ArgumentOutOfRangeException(nameof(extendedKind))
        };
    }

    private double Coefficient(int index)
    {
        return index < Coefficients.Count
            ? Coefficients[index]
            : throw new InvalidDataException(
                $"Glass '{Manufacturer}:{CatalogName}' does not provide coefficient {index} for {Formula}.");
    }
}
