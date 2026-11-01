using System.Text.Json;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record ReferenceExampleInfo(string Id, string SourceCommit, string OriginalName, string Notes,
    double SubstrateThicknessMm, string ExitMediumId, string SurfaceMode, string EvaluationMode);
public sealed record ReferenceExamplePreset(string Id, string MenuLabel);

public static class ReferenceExamples
{
    public const string SourceCommit = "8b942b3f956728a23cea2b35023c8b5d8de17428";
    public static IReadOnlyList<ReferenceExamplePreset> Presets { get; } = Array.AsReadOnly(new[]
    {
        new ReferenceExamplePreset("demo-bbar-4", "宽带减反膜（4 层 Ta₂O₅/SiO₂/MgF₂）"),
        new ReferenceExamplePreset("demo-edge", "边缘滤光片（四分之一波堆栈）"),
        new ReferenceExamplePreset("demo-hr-qw", "高反膜（TiO₂/SiO₂，15 层）"),
        new ReferenceExamplePreset("demo-metal-ag", "金属反射镜（Ag）"),
        new ReferenceExamplePreset("demo-mgf2-ar", "单层减反膜（MgF₂）")
    });
    public static Experiment Create(string id)
    {
        using var stream = typeof(ReferenceExamples).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.CoatingDesign.Engine.Data.reference-examples.json")!;
        var json = JsonSerializer.Deserialize<SourceData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var design = json.Examples.SingleOrDefault(d => d.Id == id) ?? throw new ArgumentException("未知镀膜参考示例。", nameof(id));
        var used = design.FrontLayers.Select(l => l.Material).Append(design.IncidentMedium).Append(design.Substrate.Material).Append(design.ExitMedium).ToHashSet();
        var materials = json.Materials.Where(m => used.Contains(m.Id)).Select(Capture).ToArray();
        var filmIds = design.FrontLayers.Select(l => l.Material).ToHashSet();
        var registry = new MaterialRegistry();
        var orderedFilms = materials.Where(m => filmIds.Contains(m.Id))
            .OrderBy(m => m.Resolve(registry).RefractiveIndex(design.ReferenceWavelength)).ToArray();
        var low = orderedFilms[0].Id; var high = orderedFilms[^1].Id;
        var kind = id is "demo-bbar-4" or "demo-mgf2-ar" ? DesignKind.Antireflection : DesignKind.HighReflector;
        // Reference designs declare no merit function or acceptance targets. Keep the product's
        // existing target defaults as editable local comparison targets, without asserting a pass.
        var baseline = Examples.Create(kind);
        var result = baseline with { Id = Guid.NewGuid(), Name = design.Name,
            Target = baseline.Target with { CenterNm = design.ReferenceWavelength, MaximumLayers = Math.Max(baseline.Target.MaximumLayers, design.FrontLayers.Length) },
            IncidentId = design.IncidentMedium, SubstrateId = design.Substrate.Material, LowId = low, HighId = high,
            Materials = materials, Layers = design.FrontLayers.Select(l => new FilmLayer(l.Material, l.Thickness, !l.Locked)).ToArray(),
            ReferenceExample = new(design.Id, SourceCommit, design.Name, design.Notes, design.Substrate.Thickness,
                design.ExitMedium, design.SurfaceMode, design.MfEvalMode) };
        result.Validate(); return result;
    }
    private static MaterialSnapshot Capture(SourceMaterial m)
    {
        IMaterial material;
        var tabulated = m.Table.Length > 0;
        if (m.ConstantNK is { Length: 2 } constant) material = new ConstantIndexMaterial(m.Name, constant[0], constant[1]);
        else material = new CatalogGlassMaterial(m.Name, "refractiveindex.info / TFStudio CC0", tabulated ? CatalogGlassMaterial.PchipTableFormula
                : m.KTable.Length > 0 ? CatalogGlassMaterial.PchipSellmeierFormula : "formula 2", m.RangeNm[0], m.RangeNm[1],
            coefficients: m.Coefficients, refractiveIndexWavelengthsNanometers: m.Table.Select(r => r[0]).ToArray(),
            refractiveIndices: m.Table.Select(r => r[1]).ToArray(),
            extinctionWavelengthsNanometers: (tabulated ? m.Table : m.KTable).Select(r => r[0]).ToArray(),
            extinctionCoefficients: (tabulated ? m.Table.Select(r => r[2]) : m.KTable.Select(r => r[1])).ToArray());
        var source = $"TFStudio {SourceCommit} / materialDatabase.js；{m.Description}";
        if (tabulated || m.KTable.Length > 0) source += "；PCHIP，域外延用端点（非实测）";
        return MaterialLibrary.Capture(material, source, m.RangeNm[0], m.RangeNm[1]) with
            { Id = m.Id, HoldTabulatedEndpoints = tabulated, DisplayColor = m.Color };
    }
    private sealed record SourceData(SourceMaterial[] Materials, SourceDesign[] Examples);
    private sealed record SourceMaterial(string Id, string Name, string Description, string Color, double[] RangeNm,
        double[]? ConstantNK, double[]? Coefficients, double[][] Table, double[][] KTable);
    private sealed record SourceLayer(string Material, double Thickness, bool Locked);
    private sealed record SourceSubstrate(string Material, double Thickness);
    private sealed record SourceDesign(string Id, string Name, string Notes, string IncidentMedium, SourceSubstrate Substrate,
        string ExitMedium, string SurfaceMode, string MfEvalMode, double ReferenceWavelength, SourceLayer[] FrontLayers);
}
