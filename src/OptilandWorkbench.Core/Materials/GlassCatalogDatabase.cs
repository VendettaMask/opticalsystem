using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.Core.Materials;

internal sealed class GlassCatalogDatabase
{
    private const string ResourceName = "OptilandWorkbench.Core.Materials.Data.glass-catalog.json";
    private readonly Dictionary<string, GlassCatalogDefinition> _qualified;
    private readonly Dictionary<string, GlassCatalogDefinition[]> _byName;

    private GlassCatalogDatabase(IReadOnlyList<GlassCatalogDefinition> definitions)
    {
        var counts = definitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            definition.ResolvedName = counts[definition.Name] == 1
                ? definition.Name
                : $"{definition.Manufacturer}:{definition.Name}";
        }

        _qualified = definitions.ToDictionary(
            definition => QualifiedKey(definition.Manufacturer, definition.Name),
            StringComparer.OrdinalIgnoreCase);
        _byName = definitions
            .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        Names = definitions
            .Select(definition => definition.ResolvedName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Manufacturers = definitions
            .Select(definition => definition.Manufacturer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static GlassCatalogDatabase Instance { get; } = Load();

    public IReadOnlyList<string> Names { get; }

    public IReadOnlyList<string> Manufacturers { get; }

    public int Count => _qualified.Count;

    public bool TryResolve(
        string name,
        IReadOnlyList<string>? preferredManufacturers,
        out CatalogGlassMaterial material)
    {
        var separator = name.IndexOf(':');
        if (separator > 0 && separator < name.Length - 1)
        {
            var manufacturer = name[..separator];
            var glassName = name[(separator + 1)..];
            if (_qualified.TryGetValue(QualifiedKey(manufacturer, glassName), out var qualified))
            {
                material = qualified.CreateMaterial();
                return true;
            }
        }

        if (!_byName.TryGetValue(name, out var matches))
        {
            material = null!;
            return false;
        }

        foreach (var preferred in preferredManufacturers ?? Array.Empty<string>())
        {
            var normalized = NormalizeManufacturer(preferred);
            var match = matches.FirstOrDefault(candidate =>
                candidate.Manufacturer.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                material = match.CreateMaterial();
                return true;
            }
        }

        if (matches.Length == 1)
        {
            material = matches[0].CreateMaterial();
            return true;
        }

        material = null!;
        return false;
    }

    public IReadOnlyList<string> MatchingManufacturers(string name)
    {
        return _byName.TryGetValue(name, out var matches)
            ? matches.Select(match => match.Manufacturer).ToArray()
            : Array.Empty<string>();
    }

    private static GlassCatalogDatabase Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded glass catalog '{ResourceName}' was not found.");
        var resource = JsonSerializer.Deserialize<GlassCatalogResource>(stream)
            ?? throw new InvalidDataException("The embedded glass catalog is empty or invalid.");
        return new GlassCatalogDatabase(resource.Entries);
    }

    private static string QualifiedKey(string manufacturer, string name) =>
        $"{NormalizeManufacturer(manufacturer)}:{name.Trim()}";

    private static string NormalizeManufacturer(string manufacturer)
    {
        var value = Path.GetFileNameWithoutExtension(manufacturer.Trim());
        return value.ToUpperInvariant();
    }

    private sealed class GlassCatalogResource
    {
        [JsonPropertyName("entries")]
        public List<GlassCatalogDefinition> Entries { get; init; } = new();
    }

    private sealed class GlassCatalogDefinition
    {
        [JsonPropertyName("manufacturer")]
        public string Manufacturer { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("formula")]
        public string Formula { get; init; } = string.Empty;

        [JsonPropertyName("min_um")]
        public double MinimumMicrometers { get; init; }

        [JsonPropertyName("max_um")]
        public double MaximumMicrometers { get; init; }

        [JsonPropertyName("coefficients")]
        public double[] Coefficients { get; init; } = Array.Empty<double>();

        [JsonPropertyName("n_um")]
        public double[] RefractiveIndexMicrometers { get; init; } = Array.Empty<double>();

        [JsonPropertyName("n")]
        public double[] RefractiveIndices { get; init; } = Array.Empty<double>();

        [JsonPropertyName("k_um")]
        public double[] ExtinctionMicrometers { get; init; } = Array.Empty<double>();

        [JsonPropertyName("k")]
        public double[] ExtinctionCoefficients { get; init; } = Array.Empty<double>();

        [JsonIgnore]
        public string ResolvedName { get; set; } = string.Empty;

        public CatalogGlassMaterial CreateMaterial() => new(
            ResolvedName,
            Manufacturer,
            Formula,
            MinimumMicrometers * 1000.0,
            MaximumMicrometers * 1000.0,
            Coefficients,
            RefractiveIndexMicrometers.Select(value => value * 1000.0).ToArray(),
            RefractiveIndices,
            ExtinctionMicrometers.Select(value => value * 1000.0).ToArray(),
            ExtinctionCoefficients);
    }
}
