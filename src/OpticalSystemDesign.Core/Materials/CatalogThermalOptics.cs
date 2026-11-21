namespace OpticalSystemDesign.Core.Materials;

/// <summary>Catalog thermo-optic quantities at the glass's reference temperature and 1 atm.</summary>
public static class CatalogThermalOptics
{
    // A review cue only: never rescale, reject or remove a source record.
    public static bool RequiresSourceReview(double relativeIndexDerivative) =>
        double.IsFinite(relativeIndexDerivative) && Math.Abs(relativeIndexDerivative) >= 0.01;

    public static bool TryReferenceThermalPower(CatalogGlassMaterial glass, double wavelengthNanometers,
        double relativeIndex, out double thermalPower, out double relativeIndexDerivative)
    {
        thermalPower = relativeIndexDerivative = double.NaN;
        var data = glass.ZemaxData;
        if (data is null || data.ThermalCoefficients.Count < 7
            || !data.ThermalCoefficients.All(double.IsFinite)
            || !data.ThermalExpansionLow.HasValue || !double.IsFinite(data.ThermalExpansionLow.Value)
            || !double.IsFinite(relativeIndex) || relativeIndex <= 1
            || !double.IsFinite(wavelengthNanometers) || wavelengthNanometers <= 0
            || wavelengthNanometers < glass.MinimumWavelengthNanometers
            || wavelengthNanometers > glass.MaximumWavelengthNanometers)
            return false;

        var td = data.ThermalCoefficients;
        var lambdaSquared = Math.Pow(wavelengthNanometers / 1000, 2);
        var denominator = lambdaSquared - Math.Sign(td[5]) * td[5] * td[5];
        var temperatureFactor = 1 + (td[6] - 15) * 3.4785e-3;
        if (Math.Abs(denominator) <= 1e-12 || temperatureFactor <= 0) return false;

        // TD describes the absolute index change. At ΔT=0, only D0 and E0
        // contribute. Convert that derivative back to the index relative to air.
        // Air dispersion and thermal scaling follow the AGF/Kohlrausch convention.
        var airAt15 = (6432.8 + 2949810 * lambdaSquared / (146 * lambdaSquared - 1)
            + 25540 * lambdaSquared / (41 * lambdaSquared - 1)) * 1e-8;
        var airIndex = 1 + airAt15 / temperatureFactor;
        var airDerivative = -airAt15 * 3.4785e-3 / (temperatureFactor * temperatureFactor);
        var absoluteDerivative = (relativeIndex * relativeIndex - 1) / (2 * relativeIndex)
            * (td[0] + td[3] / denominator);
        relativeIndexDerivative = (absoluteDerivative - relativeIndex * airDerivative) / airIndex;
        var expansion = data.IgnoreThermalExpansion ? 0 : data.ThermalExpansionLow.Value * 1e-6;
        thermalPower = relativeIndexDerivative / (relativeIndex - 1) - expansion;
        return double.IsFinite(thermalPower);
    }
}
