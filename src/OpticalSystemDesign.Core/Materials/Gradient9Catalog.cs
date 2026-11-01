using System.Globalization;
using System.Text.Json;

namespace OpticalSystemDesign.Core.Materials;

/// <summary>Declared GRADIENT_9.DAT coefficients. Custom adjacent data takes precedence over the bundled catalog.</summary>
internal static class Gradient9Catalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, double[]>> Bundled = new(() =>
    {
        using var stream = typeof(Gradient9Catalog).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.Core.Materials.Data.gradient9-catalog.json") ?? throw new InvalidDataException("Missing Gradient 9 catalog.");
        using var data = JsonDocument.Parse(stream);
        return data.RootElement.GetProperty("materials").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!,
            m => new[] { "b", "c", "k0", "k1", "k2" }.Select(k => m.GetProperty(k).GetDouble()).ToArray(), StringComparer.OrdinalIgnoreCase);
    });
    internal static Gradient9IndexProfile? Resolve(string name, string? assetDirectory, double lengthScale)
    {
        var path = assetDirectory is null ? null : Path.Combine(assetDirectory, "GRADIENT_9.DAT");
        var table = path is not null && File.Exists(path) ? Read(path) : Bundled.Value;
        if (!table.TryGetValue(name, out var c)) return null;
        return new(c[0], c[1], c[2] / lengthScale, c[3] / lengthScale, c[4] / lengthScale);
    }
    private static IReadOnlyDictionary<string, double[]> Read(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Gradient 9 catalog is too large.");
        var lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines.Length % 6 != 0 || lines.Length > 25 * 6)
            throw new InvalidDataException("Gradient 9 catalog requires at most 25 complete six-line records.");
        var result = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length; i += 6)
        {
            var name = lines[i].Trim();
            if (name.Length is < 1 or >= 20 || name.Any(char.IsWhiteSpace) || result.ContainsKey(name))
                throw new InvalidDataException("Invalid or duplicated Gradient 9 material name.");
            var values = Enumerable.Range(1, 5).Select(j => double.Parse(lines[i + j], NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("Non-finite Gradient 9 coefficient.");
            result.Add(name, values);
        }
        return result;
    }
}
