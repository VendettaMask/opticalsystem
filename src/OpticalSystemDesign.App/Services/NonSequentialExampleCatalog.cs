using System.Text.Json;

namespace OpticalSystemDesign.App.Services;

internal sealed record NonSequentialExample(string RelativePath, string Sha256)
{
    public string Category => RelativePath[..RelativePath.IndexOf('/')];
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
    public string ActionId => "example-nsc-" + RelativePath;
    public string FilePath => Path.Combine(AppContext.BaseDirectory, "Examples", "NonSequential",
        RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

internal sealed record NonSequentialExampleGroup(string Name, IReadOnlyList<NonSequentialExample> Examples)
{
    public string DisplayName => Name switch
    {
        "Birefringence" => "双折射",
        "Coherence Interference and Diffraction" => "相干、干涉与衍射",
        "Colorimetry" => "色度学",
        "Diffractives" => "衍射光学",
        "Fibers" => "光纤",
        "Fresnel Lenses" => "菲涅耳透镜",
        "Geometry Creation" => "几何建模",
        "GRINs" => "梯度折射率系统",
        "LightningTrace" => "快速照明追迹",
        "Miscellaneous" => "其他系统",
        "Phosphors and Fluorescence" => "荧光粉与荧光",
        "Prisms" => "棱镜",
        "Ray splitting" => "光线分裂",
        "Reflectors" => "反射器",
        "Scattering" => "散射",
        "Sources" => "光源",
        _ => Name
    };
}

internal static class NonSequentialExampleCatalog
{
    public static IReadOnlyList<NonSequentialExample> Examples { get; } = Load();

    public static IReadOnlyList<NonSequentialExampleGroup> Groups { get; } = Array.AsReadOnly(Examples
        .GroupBy(example => example.Category, StringComparer.Ordinal)
        .Select(group => new NonSequentialExampleGroup(group.Key, Array.AsReadOnly(group.ToArray())))
        .ToArray());

    private static IReadOnlyList<NonSequentialExample> Load()
    {
        using var stream = typeof(NonSequentialExampleCatalog).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.App.NonSequentialExamples.index.json")
            ?? throw new InvalidOperationException("缺少非序列模式示例目录。");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("非序列模式示例目录无效。");
        return Array.AsReadOnly(catalog.Files);
    }

    private sealed record Catalog(NonSequentialExample[] Files);
}
