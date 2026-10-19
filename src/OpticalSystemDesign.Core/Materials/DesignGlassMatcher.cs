using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.Core.Materials;

public sealed record DesignGlassMatch(string SourceName, string CatalogName, double RelaxedIndex, double RelaxedAbbe,
    double CatalogIndex, double CatalogAbbe, double Distance);

/// <summary>Discrete material projection. Only explicitly permitted real catalog materials participate.</summary>
public static class DesignGlassMatcher
{
    public static (CatalogGlassMaterial Material, DesignGlassMatch Match) Match(MaterialRegistry registry, IMaterial relaxed,
        IReadOnlyList<string> allowedNames, IReadOnlyList<double> wavelengthsNanometers)
    {
        if (allowedNames.Count is < 1 or > 64 || wavelengthsNanometers.Count == 0)
            throw new ArgumentException("A bounded real glass catalog and active wavelengths are required.");
        var source = OpticalDesignParameters.RelaxedGlassCoordinates(relaxed);
        var candidates = new List<(CatalogGlassMaterial Material, DesignGlassMatch Match)>();
        foreach (var name in allowedNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!registry.TryResolve(name, registry.PreferredGlassCatalogs, out var material) || material is not CatalogGlassMaterial catalog)
                throw new ArgumentException($"Permitted glass '{name}' is not an unambiguous real catalog material.");
            if (wavelengthsNanometers.Any(wavelength => wavelength < catalog.MinimumWavelengthNanometers
                    || wavelength > catalog.MaximumWavelengthNanometers || !double.IsFinite(catalog.RefractiveIndex(wavelength)))) continue;
            var target = OpticalDesignParameters.RelaxedGlassCoordinates(catalog);
            var distance = Math.Abs(target.Nd - source.Nd) / .4 + Math.Abs(target.Vd - source.Vd) / 40;
            candidates.Add((catalog, new(relaxed.Name, name, source.Nd, source.Vd, target.Nd, target.Vd, distance)));
        }
        return candidates.OrderBy(candidate => candidate.Match.Distance).ThenBy(candidate => candidate.Match.CatalogName, StringComparer.Ordinal).FirstOrDefault()
            is { Material: not null } best ? best : throw new InvalidOperationException("No allowed real glass covers all active wavelengths.");
    }
}
