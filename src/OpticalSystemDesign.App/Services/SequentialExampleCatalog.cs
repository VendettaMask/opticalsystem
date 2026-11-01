using System.Text.Json;

namespace OpticalSystemDesign.App.Services;

internal sealed record SequentialExample(string RelativePath, string Sha256)
{
    public string Category => RelativePath[..RelativePath.IndexOf('/')];
    public string Name => Path.GetFileNameWithoutExtension(RelativePath);
    public string ActionId => "example-" + RelativePath;
    public string FilePath => Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential",
        RelativePath.Replace('/', Path.DirectorySeparatorChar));
}

internal sealed record SequentialExampleGroup(string Name, IReadOnlyList<SequentialExample> Examples)
{
    public string DisplayName => Name switch
    {
        "Afocal" => "无焦系统",
        "Birefringent prisms" => "双折射棱镜",
        "Diffractive components" => "衍射元件",
        "GRINs" => "梯度折射率系统",
        "Image Simulation" => "图像模拟",
        "Interconnects" => "光学互连",
        "Manufacturing" => "制造",
        "Miscellaneous" => "其他系统",
        "Objectives" => "物镜",
        "Optical testing" => "光学检测",
        "Polarization" => "偏振",
        "Telescopes" => "望远镜",
        "Tilted systems & prisms" => "倾斜系统与棱镜",
        "Zoom systems" => "变焦与扫描",
        _ => Name
    };
}

internal static class SequentialExampleCatalog
{
    public static IReadOnlyList<SequentialExample> Examples { get; } = Load();

    public static IReadOnlyList<SequentialExampleGroup> Groups { get; } = Array.AsReadOnly(Examples
        .GroupBy(example => example.Category, StringComparer.Ordinal)
        .Select(group => new SequentialExampleGroup(group.Key, Array.AsReadOnly(group.ToArray())))
        .ToArray());

    private static IReadOnlyList<SequentialExample> Load()
    {
        using var stream = typeof(SequentialExampleCatalog).Assembly.GetManifestResourceStream(
            "OpticalSystemDesign.App.SequentialExamples.index.json")
            ?? throw new InvalidOperationException("缺少顺序模式示例目录。");
        var catalog = JsonSerializer.Deserialize<Catalog>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("顺序模式示例目录无效。");
        return Array.AsReadOnly(catalog.Files);
    }

    private sealed record Catalog(SequentialExample[] Files);
}
