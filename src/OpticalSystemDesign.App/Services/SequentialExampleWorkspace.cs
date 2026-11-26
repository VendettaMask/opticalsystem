using System.Text.Json;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.App.Services;

internal sealed record SequentialExampleWorkspace(string SourceSha256, ExampleWorkspacePane Layout)
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential");
    private static readonly Lazy<Dictionary<string, SequentialExampleWorkspace>> Catalog = new(Load);

    internal static SequentialExampleWorkspace? ForDocument(string? path)
    {
        if (path is null || !Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase)) return null;
        var relative = Path.GetRelativePath(DirectoryPath, path).Replace(Path.DirectorySeparatorChar, '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
        return Catalog.Value.GetValueOrDefault(relative);
    }

    private static Dictionary<string, SequentialExampleWorkspace> Load()
    {
        using var file = File.OpenRead(Path.Combine(DirectoryPath, "workspaces.json"));
        var layouts = JsonSerializer.Deserialize<Dictionary<string, SequentialExampleWorkspace>>(file,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException();
        return new(layouts, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }
}

internal sealed record ExampleWorkspacePane(
    string? Direction = null, ExampleWorkspacePane[]? Children = null, ExampleWorkspaceWindow[]? Windows = null,
    double Weight = 1, int Selected = 0);

internal sealed record ExampleWorkspaceWindow(string Kind, Guid? Id = null, string? Title = null,
    string? Suffix = null, bool Focused = false, Dictionary<string, string>? Settings = null)
{
    internal WorkspaceDocumentDescriptor Descriptor(HashSet<string> used)
    {
        var (type, key, caption) = Kind switch
        {
            "lens-editor" => (Kind, WorkspaceDockFactory.LensDocumentId, "镜头数据"),
            "viewer-2d" => (Kind, WorkspaceDockFactory.Viewer2DDocumentId, "二维视图"),
            "viewer-3d" => (Kind, "document:viewer-3d", "三维视图"),
            "solid-model" => (Kind, "document:solid-model", "实体模型"),
            "optimization" => (Kind, "document:optimization", "优化"),
            "multi-configuration" => (Kind, "document:multi-configuration", "多配置"),
            "tolerancing" => (Kind, "document:tolerancing", "公差数据编辑器"),
            "optical-drawing" => (Kind, $"sample-drawing:{Id:N}", "光学制图"),
            "material-internal-transmission" => (WorkspaceDocumentTypes.MaterialAnalysis,
                "document:material-analysis:InternalTransmission", "内部透过率"),
            "unavailable" => (WorkspaceDocumentTypes.UnavailableSampleWindow, $"sample-window:{Id:N}", Title!),
            _ => (WorkspaceDocumentTypes.Analysis, $"analysis:{Kind}", WorkbenchAnalysisCatalog.DisplayName(Kind))
        };
        if (!used.Add(key)) key += $":{Id:N}";
        var settings = new Dictionary<string, string>(Settings ?? []);
        if (Kind == "material-internal-transmission") settings["MaterialAnalysisKind"] = "InternalTransmission";
        if (Kind == "optical-drawing") settings["standard"] = "Iso10110";
        if (settings.TryGetValue("CoatingFile", out var coating))
            settings["CoatingFile"] = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential", "Coatings", Path.GetFileName(coating));
        if (settings.TryGetValue("SourceFile", out var source))
            settings["SourceFile"] = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential", "Images", Path.GetFileName(source));
        return new(key, type, Suffix is null ? caption : $"{caption} {Suffix}",
            type == WorkspaceDocumentTypes.Analysis ? Kind : null, Id, settings, IsExampleWindow: true);
    }
}
