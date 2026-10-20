using System.Text.Json;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Two explicitly bounded material datasets, not a general MIL dispersion approximation.</summary>
internal static class ZemaxMilMaterialProfiles
{
    private static readonly Lazy<IReadOnlyDictionary<string, TabulatedMaterial>> Profiles = new(Load);

    internal static bool TryResolve(string name, out IMaterial material)
    {
        if (Profiles.Value.TryGetValue(name, out var profile))
        {
            material = profile.Clone();
            return true;
        }
        material = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, TabulatedMaterial> Load()
    {
        using var stream = typeof(ZemaxMilMaterialProfiles).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.Core.Materials.Data.zemax-mil-profiles.json")
            ?? throw new InvalidDataException("Bundled MIL material profiles are missing.");
        using var document = JsonDocument.Parse(stream);
        var provenance = document.RootElement.GetProperty("provenance").GetString()!;
        return document.RootElement.GetProperty("materials").EnumerateArray().ToDictionary(
            item => item.GetProperty("name").GetString()!,
            item => new TabulatedMaterial(item.GetProperty("name").GetString()!,
                item.GetProperty("wavelengthsNanometers").Deserialize<double[]>()!,
                item.GetProperty("indices").Deserialize<double[]>()!, provenance), StringComparer.Ordinal);
    }
}
