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
        Register(new SellmeierMaterial(
            "N-F2",
            new[] { 1.34533359, 0.209073176, 0.937357162 },
            new[] { 0.00997743871, 0.0470450767, 111.886764 },
            extinctionWavelengthsNanometers: new[] { 480.0, 486.1327, 550.0, 587.5618, 650.0, 656.2725 },
            extinctionCoefficients: new[] { 6.796800000000003e-09, 6.178930475000003e-09, 3.504894117647059e-09, 3.744287570500001e-09, 5.72175e-09, 6.0919843125e-09 }));
        Register(new SellmeierMaterial(
            "F2",
            new[] { 1.34533359, 0.209073176, 0.937357162 },
            new[] { 0.00997743871, 0.0470450767, 111.886764 },
            extinctionWavelengthsNanometers: new[] { 480.0, 486.1327, 550.0, 587.5618, 650.0, 656.2725 },
            extinctionCoefficients: new[] { 6.796800000000003e-09, 6.178930475000003e-09, 3.504894117647059e-09, 3.744287570500001e-09, 5.72175e-09, 6.0919843125e-09 }));
        Register(new SellmeierMaterial(
            "N-SK15",
            new[] { 1.30417786, 0.285841160, 0.974781572 },
            new[] { 0.00695051276, 0.0232023703, 99.0168840 },
            extinctionWavelengthsNanometers: new[] { 486.1327, 587.5618, 656.2725 },
            extinctionCoefficients: new[] { 1.967083450000001e-08, 1.3514016735000003e-08, 1.6778762375e-08 }));
        Register(new SellmeierMaterial(
            "K10",
            new[] { 1.15687082, 0.0642625444, 0.872376139 },
            new[] { 0.00809424251, 0.0386051284, 104.747730 },
            extinctionWavelengthsNanometers: new[] { 486.1327, 587.5618, 656.2725 },
            extinctionCoefficients: new[] { 1.4502365177500001e-08, 1.3138006230000001e-08, 1.2756688749999999e-08 }));
        Register(new PolynomialDispersionMaterial(
            "SK16",
            new[]
            {
                2.592001,
                -0.01540969, 2.0,
                0.01022680, -2.0,
                0.001581559, -4.0,
                -0.0001877149, -6.0,
                0.00001012515, -8.0
            },
            extinctionWavelengthsNanometers: new[] { 480.0, 550.0, 650.0 },
            extinctionCoefficients: new[] { 1.1476e-08, 4.379e-09, 5.1751e-09 }));
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
