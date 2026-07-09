namespace OptilandWorkbench.Core.Materials;

public sealed class MaterialRegistry
{
    private readonly Dictionary<string, IMaterial> _materials = new(StringComparer.OrdinalIgnoreCase);

    public MaterialRegistry()
    {
        Register(new AirMaterial());
        Register(new ConstantIndexMaterial("Vacuum", 1.0));
        Register(new AbbeMaterial("N-BK7", 1.5168, 64.17));
        Register(new AbbeMaterial("BK7", 1.5168, 64.17));
        Register(new AbbeMaterial("N-F2", 1.6200, 36.37));
        Register(new AbbeMaterial("F2", 1.6200, 36.37));
        Register(new SellmeierMaterial(
            "Fused Silica",
            new[] { 0.6961663, 0.4079426, 0.8974794 },
            new[] { 0.0684043 * 0.0684043, 0.1162414 * 0.1162414, 9.896161 * 9.896161 }));
    }

    public IReadOnlyCollection<string> Names => _materials.Keys.ToArray();

    public void Register(IMaterial material)
    {
        _materials[material.Name] = material;
    }

    public IMaterial Resolve(string name)
    {
        if (_materials.TryGetValue(name, out var material))
        {
            return material.Clone();
        }

        return new ConstantIndexMaterial(name, 1.5);
    }

    public void RegisterAbbeGlass(string name, double nd, double vd)
    {
        Register(new AbbeMaterial(name, nd, vd));
    }
}
