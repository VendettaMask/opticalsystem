using OptilandWorkbench.Core.Propagation;

namespace OptilandWorkbench.Core.Materials;

/// <summary>A named glass whose dispersion data is unavailable. Never substitutes an optical model.</summary>
public sealed class UnresolvedMaterial(string name, string catalogs = "") : IMaterial
{
    public string Name { get; } = name;
    public string Catalogs { get; } = catalogs;
    public IPropagationModel PropagationModel { get; } = new HomogeneousPropagationModel();

    public double RefractiveIndex(double wavelengthNanometers) => throw MissingGlass();
    public double ExtinctionCoefficient(double wavelengthNanometers) => throw MissingGlass();
    public IMaterial Clone() => new UnresolvedMaterial(Name, Catalogs);

    private InvalidOperationException MissingGlass() => new($"找不到玻璃“{Name}”，请匹配材料后再计算。");
}
