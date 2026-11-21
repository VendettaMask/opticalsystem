namespace OpticalSystemDesign.Core.Materials;

public enum GlassConstantsStatus { Available, WavelengthOutOfRange, UnsupportedModel, InvalidIndex, UndefinedAbbeNumber }

public sealed record CatalogGlassConstantsResult(
    double? IndexF, double? IndexD, double? IndexC, double? AbbeNumber, GlassConstantsStatus Status);

/// <summary>F-d-C constants evaluated from the actual dispersion model, independently of catalog reference values.</summary>
public static class CatalogGlassConstants
{
    public const double WavelengthF = 486.1327;
    public const double WavelengthD = 587.5618;
    public const double WavelengthC = 656.2725;

    public static CatalogGlassConstantsResult Calculate(CatalogGlassMaterial glass)
    {
        ArgumentNullException.ThrowIfNull(glass);
        var wavelengths = new[] { WavelengthF, WavelengthD, WavelengthC };
        if (!double.IsFinite(glass.MinimumWavelengthNanometers) || !double.IsFinite(glass.MaximumWavelengthNanometers)
            || glass.MinimumWavelengthNanometers > glass.MaximumWavelengthNanometers)
            return new(null, null, null, null, GlassConstantsStatus.InvalidIndex);
        var tabulated = glass.Formula.StartsWith("tabulated ", StringComparison.Ordinal);
        if (tabulated && glass.RefractiveIndexWavelengthsNanometers.Count == 0)
            return new(null, null, null, null, GlassConstantsStatus.InvalidIndex);
        if (wavelengths.Any(w => w < glass.MinimumWavelengthNanometers || w > glass.MaximumWavelengthNanometers)
            || tabulated && wavelengths.Any(w =>
                w < glass.RefractiveIndexWavelengthsNanometers.Min() || w > glass.RefractiveIndexWavelengthsNanometers.Max()))
            return new(null, null, null, null, GlassConstantsStatus.WavelengthOutOfRange);
        try
        {
            var nf = glass.RefractiveIndex(WavelengthF);
            var nd = glass.RefractiveIndex(WavelengthD);
            var nc = glass.RefractiveIndex(WavelengthC);
            if (!double.IsFinite(nf) || !double.IsFinite(nd) || !double.IsFinite(nc) || nf <= 0 || nd <= 0 || nc <= 0)
                return new(null, null, null, null, GlassConstantsStatus.InvalidIndex);
            var dispersion = nf - nc;
            if (Math.Abs(dispersion) <= 1e-12)
                return new(nf, nd, nc, null, GlassConstantsStatus.UndefinedAbbeNumber);
            var vd = (nd - 1) / dispersion;
            return double.IsFinite(vd)
                ? new(nf, nd, nc, vd, GlassConstantsStatus.Available)
                : new(nf, nd, nc, null, GlassConstantsStatus.UndefinedAbbeNumber);
        }
        catch (NotSupportedException) { return new(null, null, null, null, GlassConstantsStatus.UnsupportedModel); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        { return new(null, null, null, null, GlassConstantsStatus.InvalidIndex); }
    }
}
