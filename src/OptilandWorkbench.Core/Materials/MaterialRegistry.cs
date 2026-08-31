namespace OptilandWorkbench.Core.Materials;

public sealed class MaterialRegistry
{
    private static readonly IReadOnlyList<string> DefaultGlassCatalogPriority = new[]
    {
        "SCHOTT",
        "OHARA",
        "HOYA",
        "HIKARI",
        "NIKON-HIKARI",
        "SUMITA",
        "CDGM-ZEMAX202309",
        "CDGM",
        "CHENGDU",
        "GBJ",
        "NHG",
        "555CHINESES"
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, IMaterial> _materials = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _preferredGlassCatalogs =
        DefaultGlassCatalogPriority.ToArray();
    private static GlassCatalogDatabase Catalog => GlassCatalogDatabase.Instance;

    public MaterialRegistry()
    {
        BundledZemaxGlassCatalogDatabase.EnsureLoaded();
        Register(new AirMaterial());
        Register(new ConstantIndexMaterial("Vacuum", 1.0));
        Register(new SellmeierMaterial(
            "Fused Silica",
            new[] { 0.6961663, 0.4079426, 0.8974794 },
            new[] { 0.0684043 * 0.0684043, 0.1162414 * 0.1162414, 9.896161 * 9.896161 }));

        RegisterAlias("BK7", Resolve("N-BK7"));
        RegisterAlias("Silica", Resolve("Fused Silica"));
    }

    public IReadOnlyCollection<string> Names
    {
        get
        {
            string[] registeredNames;
            lock (_gate)
            {
                registeredNames = _materials.Keys.ToArray();
            }

            return registeredNames
                .Concat(ExternalGlassCatalogDatabase.Names)
                .Concat(Catalog.Names)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyCollection<string> GlassManufacturers => ExternalGlassCatalogDatabase.Manufacturers
        .Concat(Catalog.Manufacturers)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> PreferredGlassCatalogs
    {
        get
        {
            lock (_gate)
            {
                return _preferredGlassCatalogs.ToArray();
            }
        }
    }

    public int CatalogGlassCount => Catalog.Count;

    public void SetPreferredGlassCatalogs(IEnumerable<string>? catalogs)
    {
        var normalized = (catalogs ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_gate)
        {
            _preferredGlassCatalogs = normalized.Length > 0
                ? normalized
                : DefaultGlassCatalogPriority.ToArray();
        }
    }

    public void Register(IMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        lock (_gate)
        {
            _materials[material.Name] = material.Clone();
        }
    }

    public IMaterial Resolve(string name)
    {
        return Resolve(name, preferredManufacturers: null);
    }

    public IMaterial Resolve(string name, IReadOnlyList<string>? preferredManufacturers)
    {
        if (TryResolve(name, preferredManufacturers, out var material))
        {
            return material;
        }

        var matches = ExternalGlassCatalogDatabase.MatchingManufacturers(name)
            .Concat(Catalog.MatchingManufacturers(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"Glass '{name}' is ambiguous; specify one of these catalogs: {string.Join(", ", matches)}.");
        }

        throw new KeyNotFoundException($"Optical material '{name}' was not found in the local glass catalog.");
    }

    public bool TryResolve(
        string name,
        IReadOnlyList<string>? preferredManufacturers,
        out IMaterial material)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? "Air" : name.Trim();
        var effectiveManufacturers = preferredManufacturers is { Count: > 0 }
            ? preferredManufacturers
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray()
            : PreferredGlassCatalogs.ToArray();
        lock (_gate)
        {
            if (_materials.TryGetValue(normalized, out var registered))
            {
                material = registered.Clone();
                return true;
            }
        }

        if (Catalog.TryResolve(normalized, effectiveManufacturers, out var catalogMaterial))
        {
            lock (_gate)
            {
                _materials[catalogMaterial.Name] = catalogMaterial.Clone();
                if (!catalogMaterial.Name.Contains(':', StringComparison.Ordinal))
                {
                    _materials[normalized] = catalogMaterial.Clone();
                }
            }

            material = catalogMaterial.Clone();
            return true;
        }

        if (TryResolveExternalGlass(normalized, effectiveManufacturers, out var externalMaterial))
        {
            material = externalMaterial.Clone();
            return true;
        }

        material = null!;
        return false;
    }

    public bool TryResolveExternalGlass(
        string name,
        IReadOnlyList<string>? preferredManufacturers,
        out CatalogGlassMaterial material)
    {
        return ExternalGlassCatalogDatabase.TryResolve(name, preferredManufacturers, out material);
    }

    public void RegisterAbbeGlass(string name, double nd, double vd)
    {
        Register(new AbbeMaterial(name, nd, vd));
    }

    public MaterialRegistry CreateSnapshot() => Clone();

    internal MaterialRegistry Clone()
    {
        var clone = new MaterialRegistry();
        KeyValuePair<string, IMaterial>[] entries;
        string[] preferredGlassCatalogs;
        lock (_gate)
        {
            entries = _materials.ToArray();
            preferredGlassCatalogs = _preferredGlassCatalogs.ToArray();
        }

        foreach (var (key, material) in entries)
        {
            clone.RegisterAlias(key, material);
        }

        clone.SetPreferredGlassCatalogs(preferredGlassCatalogs);
        return clone;
    }

    private void RegisterAlias(string alias, IMaterial material)
    {
        lock (_gate)
        {
            _materials[alias] = material.Clone();
        }
    }
}
