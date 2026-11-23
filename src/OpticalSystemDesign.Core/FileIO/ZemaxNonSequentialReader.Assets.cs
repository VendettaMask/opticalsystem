using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.NonSequential;

namespace OpticalSystemDesign.Core.FileIO;

public static partial class ZemaxNonSequentialReader
{
    private static string? FindAsset(string? directory, string reference)
    {
        if (directory is null || string.IsNullOrWhiteSpace(reference) || Path.GetFileName(reference) != reference
            || reference.Any(c => c < 32 || "/\\:<>\"|?*".Contains(c))) return null;
        var relativeFolders = Path.GetExtension(reference).ToLowerInvariant() switch
        {
            ".pob" => new[] { "Objects/Polygon Objects" },
            ".uda" => new[] { "Objects/Apertures" },
            ".ies" => new[] { "Objects/Sources/IESNA", "Sources/IESNA" },
            ".dat" or ".ray" => new[] { "Objects/Sources/Source Files", "Sources/Source Files" },
            ".bmp" or ".png" or ".jpg" or ".jpeg" => new[] { "Objects/Bitmaps", "Images" },
            _ => Array.Empty<string>()
        };
        var current = new DirectoryInfo(directory);
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            foreach (var folder in relativeFolders.Select(folder => Path.Combine(current.FullName, folder)).Prepend(current.FullName).Distinct())
            {
                var path = Path.Combine(folder, reference);
                if (OperatingSystem.IsWindows() && File.Exists(path)) return path;
                // Native resource references are case insensitive, including on a case-sensitive host.
                if (Directory.Exists(folder))
                {
                    var matches = Directory.EnumerateFiles(folder).Take(4097)
                        .Where(p => string.Equals(Path.GetFileName(p), reference, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                    if (matches.Length > 1) throw new InvalidDataException($"资源名称存在大小写冲突：{reference}");
                    if (matches.Length == 1) return matches[0];
                }
            }
        return null;
    }

    private static (NonSequentialMeshAsset Mesh, NonSequentialSurfaceBehavior Behavior) ReadPolygon(
        string path, double scale, bool volume, NonSequentialSurfaceBehavior materialBehavior)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidDataException("多边形缩放必须为正有限值。");
        var text = BoundedFile.ReadAllText(path, 1024 * 1024, "Polygon object");
        var indices = new Dictionary<int, int>(); var vertices = new List<Vector3D>();
        var triangles = new List<NonSequentialMeshTriangle>();
        var behaviors = new HashSet<NonSequentialSurfaceBehavior>();
        foreach (var line in text.Split('\n'))
        {
            var t = Tokens(line.Split('!', 2)[0]); if (t.Count == 0) continue;
            switch (t[0].ToUpperInvariant())
            {
                case "V" when t.Count == 5:
                    var number = Integer(t[1]);
                    if (number < 1 || !indices.TryAdd(number, vertices.Count)) throw new InvalidDataException("多边形顶点编号无效或重复。");
                    vertices.Add(new(Number(t[2]) * scale, Number(t[3]) * scale, Number(t[4]) * scale));
                    break;
                case "T":
                case "R":
                    var count = t[0].Equals("T", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
                    if (t.Count < count + 1 || t.Count > count + 3) throw new InvalidDataException("多边形面记录长度无效。");
                    // Older shipped POB resources omit the interaction flag (the default is zero).
                    var flag = t.Count > count + 1 ? Integer(t[count + 1]) : 0;
                    var behavior = flag switch
                    {
                        -1 => NonSequentialSurfaceBehavior.Absorbing,
                        0 => materialBehavior,
                        1 => NonSequentialSurfaceBehavior.Reflective,
                        _ => throw new InvalidDataException("多边形面的交互标记无效。")
                    };
                    behaviors.Add(behavior);
                    var nativeFace = t.Count == count + 3 ? Integer(t[count + 2]) : 0;
                    if (nativeFace < 0 || nativeFace == int.MaxValue) throw new InvalidDataException("多边形面编号无效。");
                    // STARMESH reserves positive face identities; native zero is represented by one.
                    int Vertex(int i) => indices.TryGetValue(Integer(t[i]), out var index) ? index
                        : throw new InvalidDataException("多边形面引用了未定义顶点。");
                    triangles.Add(new(Vertex(1), Vertex(2), Vertex(3), nativeFace + 1));
                    if (count == 4) triangles.Add(new(Vertex(1), Vertex(3), Vertex(4), nativeFace + 1));
                    break;
                case "I": case "C": case "N": break;
                default: throw new InvalidDataException($"未知多边形资源记录：{t[0]}");
            }
            if (vertices.Count > 16384 || triangles.Count > 8192) throw new InvalidDataException("多边形资源超过几何预算。");
        }
        if (behaviors.Count != 1) throw new InvalidDataException("多边形的混合逐面交互需要单独映射。");
        var selected = behaviors.Single();
        if (!volume && selected == NonSequentialSurfaceBehavior.Refractive) throw new InvalidDataException("开放多边形不能作为体折射介质。");
        var mesh = NonSequentialStlImporter.FromGeometry(vertices, triangles, Path.GetFileName(path), "POB/triangles/1");
        if (volume && (!mesh.IsClosed || !mesh.IsManifold || !mesh.IsOrientable || mesh.HasSelfIntersections))
            throw new InvalidDataException("多边形体未形成有效闭合边界。");
        return (mesh, selected);
    }
}
