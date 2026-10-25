using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Pinned, explicitly namespaced dispersion data redistributed with AutoLens e58 under its Apache notice.</summary>
public static class AutoLensReferenceMaterials
{
    private const string Prefix = "AutoLensE58:";
    private static readonly Lazy<Data> Source = new(() =>
    {
        using var stream = typeof(AutoLensReferenceMaterials).Assembly.GetManifestResourceStream("OpticalSystemDesign.Core.Materials.Data.autolens-e58-materials.json")!;
        return JsonSerializer.Deserialize<Data>(stream)!;
    });
    public static IReadOnlyList<string> InitialNames => Source.Value.InitialNames.Select(name => Prefix + name).ToArray();
    public static IReadOnlyList<string> ProjectionNames => Source.Value.ProjectionNames.Select(name => Prefix + name).ToArray();

    public static void Register(MaterialRegistry registry)
    {
        foreach (var entry in Source.Value.Entries)
        {
            var definition = new OpticalGlassDefinition
            {
                Name = Prefix + entry.Name, ReferenceIndexD = entry.Index, ReferenceAbbeNumber = entry.Abbe,
                DispersionFormulaNumber = entry.Formula, DispersionCoefficients = entry.Coefficients.ToList(),
                MinimumWavelengthMicrometers = entry.MinimumNm / 1000, MaximumWavelengthMicrometers = entry.MaximumNm / 1000,
                Comment = $"AutoLens e58 snapshot of {entry.Catalog}.AGF; source hashes in embedded asset; upstream redistribution, no manufacturer certification."
            };
            registry.Register(new CatalogGlassMaterial(Prefix + entry.Name, $"AutoLens e58 / {entry.Catalog}",
                $"zemax formula {entry.Formula}", entry.MinimumNm, entry.MaximumNm, entry.Coefficients, zemaxData: definition));
        }
    }

    public static (double Index, double Abbe) NominalCoordinates(IMaterial material) => material is CatalogGlassMaterial { ZemaxData: { } data }
        ? (data.ReferenceIndexD, data.ReferenceAbbeNumber) : OpticalDesignParameters.RelaxedGlassCoordinates(material);

    public static (CatalogGlassMaterial Material, DesignGlassMatch Match) Match(MaterialRegistry registry, IMaterial relaxed,
        IReadOnlyList<string> allowedNames, IReadOnlyList<double> wavelengths)
    {
        var source = NominalCoordinates(relaxed);
        (CatalogGlassMaterial Material, DesignGlassMatch Match)? best = null;
        // Keep source table order on ties, as Python min() does.
        foreach (var name in allowedNames)
        {
            if (registry.Resolve(name) is not CatalogGlassMaterial material) throw new ArgumentException("Reference projection requires real catalog materials.");
            if (wavelengths.Any(wave => wave < material.MinimumWavelengthNanometers || wave > material.MaximumWavelengthNanometers)) continue;
            var entry = Source.Value.Entries.FirstOrDefault(entry => (Prefix + entry.Name).Equals(name, StringComparison.OrdinalIgnoreCase));
            var target = entry is { MatchIndex: > 0 } ? (entry.MatchIndex, entry.MatchAbbe) : NominalCoordinates(material);
            var distance = Math.Abs(target.Item1 - source.Index) / .4 + Math.Abs(target.Item2 - source.Abbe) / 40;
            if (best is null || distance < best.Value.Match.Distance)
                best = (material, new(relaxed.Name, name, source.Index, source.Abbe, target.Item1, target.Item2, distance));
        }
        return best ?? throw new InvalidOperationException("No permitted reference catalog material covers the active wavelengths.");
    }

    private sealed record Data(string[] InitialNames, string[] ProjectionNames, Entry[] Entries);
    private sealed record Entry(string Name, string Catalog, int Formula, double Index, double Abbe,
        double MinimumNm, double MaximumNm, double[] Coefficients, double MatchIndex, double MatchAbbe);
}
