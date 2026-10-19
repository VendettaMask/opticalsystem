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

internal sealed record SequentialExampleGroup(string Name, IReadOnlyList<SequentialExample> Examples);

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
