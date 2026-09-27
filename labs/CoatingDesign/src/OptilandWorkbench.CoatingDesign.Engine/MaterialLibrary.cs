using System.Text.Json;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.CoatingDesign.Engine;

public sealed class MaterialLibrary
{
    private readonly MaterialRegistry _registry = new();
    public MaterialSnapshot[] Films { get; }
    public IEnumerable<string> Names => Films.Select(m => m.Name).Concat(_registry.Names).Distinct().Order();
    public MaterialLibrary()
    {
        using var stream = typeof(MaterialLibrary).Assembly.GetManifestResourceStream(
            "OptilandWorkbench.CoatingDesign.Engine.Data.films.json")!;
        var data = JsonSerializer.Deserialize<FilmData[]>(stream)!;
        Films = data.Select(d => Capture(new CatalogGlassMaterial(d.Name, "refractiveindex.info / CC0", "tabulated nk",
            d.WavelengthNm[0], d.WavelengthNm[^1], refractiveIndexWavelengthsNanometers: d.WavelengthNm,
            refractiveIndices: d.N, extinctionWavelengthsNanometers: d.WavelengthNm, extinctionCoefficients: d.K),
            d.Source, d.WavelengthNm[0], d.WavelengthNm[^1])).ToArray();
    }
    public MaterialSnapshot Select(string name)
    {
        var film = Films.FirstOrDefault(x => x.Name == name);
        if (film is not null) return film;
        var material = _registry.Resolve(name);
        if (material is UnresolvedMaterial) throw new ArgumentException($"找不到材料：{name}");
        return Capture(material, "正式 Core 材料目录快照；无 k 数据的模型按 k=0 计算",
            material is CatalogGlassMaterial c ? c.MinimumWavelengthNanometers : 300,
            material is CatalogGlassMaterial g ? g.MaximumWavelengthNanometers : 2500);
    }
    public static MaterialSnapshot Capture(IMaterial material, string source, double minimumNm, double maximumNm) =>
        new(material.Name, material.Name, source, minimumNm, maximumNm, ComponentSnapshotFactory.FromMaterial(material));
    private sealed record FilmData(string Name, string Source, double[] WavelengthNm, double[] N, double[] K);
}
