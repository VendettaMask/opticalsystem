namespace OptilandWorkbench.Core.Materials;

public sealed class MaterialRegistry
{
    private readonly Dictionary<string, IMaterial> _materials = new(StringComparer.OrdinalIgnoreCase);
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

    public IReadOnlyCollection<string> Names => _materials.Keys
        .Concat(ExternalGlassCatalogDatabase.Names)
        .Concat(Catalog.Names)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyCollection<string> GlassManufacturers => ExternalGlassCatalogDatabase.Manufacturers
        .Concat(Catalog.Manufacturers)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public int CatalogGlassCount => Catalog.Count;

    public void Register(IMaterial material)
    {
        _materials[material.Name] = material;
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
        if (_materials.TryGetValue(normalized, out var registered))
        {
            material = registered.Clone();
            return true;
        }

        if (Catalog.TryResolve(normalized, preferredManufacturers, out var catalogMaterial))
        {
            _materials[catalogMaterial.Name] = catalogMaterial;
            if (!catalogMaterial.Name.Contains(':', StringComparison.Ordinal))
            {
                _materials[normalized] = catalogMaterial;
            }

            material = catalogMaterial;
            return true;
        }

        if (TryResolveExternalGlass(normalized, preferredManufacturers, out var externalMaterial))
        {
            material = externalMaterial;
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

    private void RegisterAlias(string alias, IMaterial material)
    {
        _materials[alias] = material.Clone();
    }
}
