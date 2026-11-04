using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.FileIO;

public sealed record ZemaxNscRecord(int Line, string Keyword, IReadOnlyList<string> Values, string Original);
public sealed record ZemaxNscObject(int Number, int Surface, string Type, int ReferenceObject, int InsideObject,
    IReadOnlyList<ZemaxNscRecord> Records, NonSequentialObjectDefinition? MappedObject);
public sealed record ZemaxNscDiagnostic(int Line, int? ObjectNumber, string Code, string Message, bool Blocking = true);
public sealed record ZemaxNscAsset(string Reference, bool Exists);
public sealed record ZemaxNscImportResult(string FileName, string SourceSha256, string OriginalFileBase64,
    string Mode, IReadOnlyList<ZemaxNscRecord> Records, IReadOnlyList<ZemaxNscObject> Objects,
    IReadOnlyList<ZemaxNscAsset> Assets, IReadOnlyList<ZemaxNscDiagnostic> Diagnostics, NonSequentialDocument? Document)
{
    public bool CanApply => Document is not null && !Diagnostics.Any(d => d.Blocking);
    public byte[] OriginalBytes() => Convert.FromBase64String(OriginalFileBase64);
}

/// <summary>Lossless NSC intake and strict, transactional native mappings. Unknown semantics remain
/// inspectable records and block scene publication; they are never substituted with placeholder solids.</summary>
public static class ZemaxNonSequentialReader
{
    public const int MaximumInputBytes = 16 * 1024 * 1024;
    public static ZemaxNscImportResult Read(string path, CancellationToken cancellationToken = default)
    {
        if (new FileInfo(path).Length > MaximumInputBytes) throw new InvalidDataException("ZMX 文件超过 16 MiB 读取限额。");
        var bytes = File.ReadAllBytes(path); cancellationToken.ThrowIfCancellationRequested();
        if (Path.GetExtension(path).Equals(".zar", StringComparison.OrdinalIgnoreCase)
            || bytes.Any(b => b == 0) && !(bytes.Length > 1 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == 0xfe && bytes[1] == 0xff)))
            throw new NotSupportedException("此入口读取文本 ZMX；ZAR/ZOS 二进制归档须先由原软件导出文本及完整外部资产。");
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return Parse(reader.ReadToEnd(), Path.GetFileName(path), Path.GetDirectoryName(Path.GetFullPath(path)), bytes, cancellationToken);
    }
    public static ZemaxNscImportResult Parse(string text, string fileName = "import.zmx", string? assetDirectory = null,
        byte[]? originalBytes = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text); originalBytes ??= Encoding.UTF8.GetBytes(text);
        if (originalBytes.Length > MaximumInputBytes || text.Length > MaximumInputBytes) throw new InvalidDataException("ZMX 文件超过读取限额。");
        var records = new List<ZemaxNscRecord>(); var objects = new List<ZemaxNscObject>(); var issues = new List<ZemaxNscDiagnostic>();
        var current = new List<ZemaxNscRecord>(); var surface = -1; var objectSurface = -1; var mode = "SEQ"; var unit = "MM";
        var waves = new SortedDictionary<int, (double Wave, double Weight)>(); var primary = 1; var name = Path.GetFileNameWithoutExtension(fileName);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var raw = lines[i].TrimEnd('\r'); var tokens = Tokens(raw);
            if (tokens.Count == 0) continue;
            var r = new ZemaxNscRecord(i + 1, tokens[0].ToUpperInvariant(), Array.AsReadOnly(tokens.Skip(1).ToArray()), raw); records.Add(r);
            if (records.Count > 200_000) throw new InvalidDataException("ZMX 记录数超过读取限额。");
            if (r.Keyword is "SURF" or "NSOH") Finish();
            if (r.Keyword == "SURF") surface = Integer(r.Values[0]);
            if (r.Keyword == "NSOH") { objectSurface = surface; current.Add(r); }
            else if (current.Count > 0 && (r.Keyword.StartsWith("NSO", StringComparison.Ordinal) || r.Keyword == "NSCS")) current.Add(r);
            else if (current.Count > 0) Finish();
            if (r.Keyword == "MODE") mode = r.Values[0].ToUpperInvariant();
            if (r.Keyword == "UNIT") unit = r.Values[0].ToUpperInvariant();
            if (r.Keyword == "UNIT" && r.Values.Count > 2 && r.Values[2] != "W")
                issues.Add(new(r.Line, null, "SourceUnits", "源功率单位不是 W，未猜测光度/辐射度转换。"));
            if (r.Keyword == "NAME") name = string.Join(" ", r.Values);
            if (r.Keyword == "PWAV") primary = Integer(r.Values[0]);
            if (r.Keyword == "WAVM") waves[Integer(r.Values[0])] = (Number(r.Values[1]) * 1000, Number(r.Values[2]));
            // Global execution/physics records must be mapped before a file is treated as executable.
            if (r.Keyword is "NSCD" or "POLS") issues.Add(new(r.Line, null, "UnmappedGlobalPhysics", $"{r.Keyword} 的原始追迹/偏振设置已保留，尚未完整映射。"));
            if (r.Keyword is "TOL" or "MNUM" or "MOFF" or "MCON")
                issues.Add(new(r.Line, null, "ExternalDesignDefinition", $"原文件的 {r.Keyword} 设计定义已保留，不能自动等价迁移。"));
            if (current.Count == 0 && r.Keyword is not ("VERS" or "MODE" or "NAME" or "NOTE" or "UNIT" or "WAVM" or "PWAV"
                or "NSCD" or "POLS" or "TOL" or "MNUM" or "MOFF" or "MCON" or "SURF" or "TYPE" or "CURV" or "HIDE"
                or "MIRR" or "SLAB" or "DISZ" or "DIAM" or "POPS" or "STOP" or "PARM" or "BLNK"
                or "GCAT" or "COFN"))
                issues.Add(new(r.Line, null, "UnmappedGlobalRecord", $"全局记录 {r.Keyword} 已保留，尚未核验其完整语义。"));
            if (r.Keyword == "PARM" && r.Values.Count > 1 && Number(r.Values[1]) != 0)
                issues.Add(new(r.Line, null, "GroupTransform", "NSC 组变换/端口参数尚未完整映射。"));
        }
        Finish();
        if (objects.Count == 0) issues.Add(new(0, null, "NoNscObjects", "文件未包含 NSOH 对象记录。"));
        if (mode != "NSC") issues.Add(new(0, null, "MixedModePorts", "混合模式的顺序入射/出射端口、变换与传播尚未完整映射；保持原场景。"));
        if (unit != "MM") issues.Add(new(0, null, "Units", $"当前直接映射只接受 MM；原单位 {unit} 已保留，未猜测转换。"));
        if (waves.Count == 0 || waves.Keys.Where((n, i) => n != i + 1).Any() || primary < 1 || primary > waves.Count)
            issues.Add(new(0, null, "Wavelengths", "波长表缺失、编号不连续或主波长无效。"));
        var refs = records.SelectMany(r => r.Values).Where(IsAsset).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => new ZemaxNscAsset(p, assetDirectory is not null && File.Exists(Path.Combine(assetDirectory, p)))).ToArray();
        foreach (var asset in refs.Where(a => !a.Exists)) issues.Add(new(0, null, "MissingAsset", $"缺少外部资产：{asset.Reference}"));
        NonSequentialDocument? document = null;
        if (!issues.Any(d => d.Blocking))
        {
            try
            {
                var ids = objects.ToDictionary(o => o.Number, o => o.MappedObject!.Id);
                var mapped = objects.Select(o => o.MappedObject! with
                { ReferenceObjectId = o.ReferenceObject == 0 ? null : ids[o.ReferenceObject], ContainingObjectId = o.InsideObject == 0 ? null : ids[o.InsideObject] }).ToArray();
                document = new(name, waves.Select(p => new NonSequentialWavelength($"λ{p.Key}", p.Value.Wave, p.Value.Weight, p.Key == primary)).ToArray(), mapped);
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
            { issues.Add(new(0, null, "SceneValidation", e.Message)); }
        }
        return new(fileName, Convert.ToHexString(SHA256.HashData(originalBytes)), Convert.ToBase64String(originalBytes), mode,
            Array.AsReadOnly(records.ToArray()), Array.AsReadOnly(objects.ToArray()), Array.AsReadOnly(refs), Array.AsReadOnly(issues.ToArray()), document);

        void Finish()
        {
            if (current.Count == 0) return; var rows = current.ToArray(); current.Clear(); var number = objects.Count + 1; var h = rows[0];
            if (number > NonSequentialDocument.MaximumObjectCount) throw new InvalidDataException("NSC 对象数量超过既有限额。");
            var type = h.Values.ElementAtOrDefault(0) ?? ""; int reference = 0, inside = 0; NonSequentialObjectDefinition? mapped = null;
            try
            {
                reference = Integer(h.Values[1]); inside = Integer(h.Values[2]);
                if (h.Values.Skip(3).Any(v => v.Length != 0)) issues.Add(new(h.Line, number, "ObjectHeader", "附加对象头字段已保留，尚未映射。"));
                if (reference != 0 || inside != 0) issues.Add(new(h.Line, number, "ObjectReferences", "非零参考/包含对象记录已保留，尚待原生定义核验。"));
                mapped = Map(number, type, rows, issues);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or IndexOutOfRangeException or InvalidDataException)
            { issues.Add(new(h.Line, number, "ObjectDefinition", e.Message)); }
            objects.Add(new(number, objectSurface, type, reference, inside, Array.AsReadOnly(rows), mapped));
        }
    }

    private static NonSequentialObjectDefinition? Map(int number, string type, ZemaxNscRecord[] rows, List<ZemaxNscDiagnostic> issues)
    {
        var p = new Dictionary<int, double>(); ZemaxNscRecord? pose = null;
        foreach (var r in rows.Skip(1))
        {
            if (r.Keyword == "NSOP") { if (pose is not null) throw new InvalidDataException("重复 NSOP 位姿。"); pose = r; continue; }
            if (r.Keyword == "NSOD")
            {
                var key = Integer(r.Values[0]); if (!p.TryAdd(key, Number(r.Values[1]))) throw new InvalidDataException("重复 NSOD 参数。");
                if (r.Values.Count > 2 && (r.Values[2] != "0" || r.Values.Count > 6 && r.Values[6] != "0"))
                    issues.Add(new(r.Line, number, "ParameterSolve", "参数求解/拾取关系尚未映射，未只取静态值代替。"));
                continue;
            }
            // Do not infer vendor private property indices from a particular sample's numeric zeros.
            issues.Add(new(r.Line, number, "UnmappedObjectRecord", $"{type} 的 {r.Keyword} 已无损保留，语义尚未完整映射。"));
        }
        if (pose is null || pose.Values.Count < 6) throw new InvalidDataException("NSC 对象缺少完整六自由度 NSOP。");
        var material = pose.Values.ElementAtOrDefault(6) ?? "";
        double P(int n, double? fallback = null) => p.TryGetValue(n, out var v) ? v : fallback ?? throw new InvalidDataException($"缺少参数 {n}。");
        int I(int n, int? fallback = null) => Integer(F(P(n, fallback)));
        var kind = NonSequentialObjectKind.PlaneRectangle; NonSequentialObjectParameters parameters; var max = 0;
        switch (type)
        {
            case "NSC_SRCR": case "NSC_SRCE":
                max = 15; kind = type == "NSC_SRCR" ? NonSequentialObjectKind.SourceRectangle : NonSequentialObjectKind.SourceEllipse;
                var distribution = P(9, 0) != 0 ? NonSequentialSurfaceSourceAngularDistribution.Cosine
                    : P(10, 0) != 0 || P(11, 0) != 0 ? NonSequentialSurfaceSourceAngularDistribution.Gaussian
                    : NonSequentialSurfaceSourceAngularDistribution.VirtualPoint;
                parameters = kind == NonSequentialObjectKind.SourceRectangle
                    ? new SourceRectangleParameters(2 * P(6), 2 * P(7), 0, P(3), I(4), I(1), I(2), distribution, P(8, 0), P(9, 0), P(10, 0), P(11, 0), P(12, 0), P(13, 0), P(14, 0), P(15, 0))
                    : new SourceEllipseParameters(2 * P(6), 2 * P(7), 0, P(3), I(4), I(1), I(2), distribution, P(8, 0), P(9, 0), P(10, 0), P(11, 0), P(12, 0), P(13, 0), P(14, 0), P(15, 0));
                if (material.Length != 0) issues.Add(new(pose.Line, number, "SourceMaterial", "非空光源材料尚未映射。"));
                break;
            case "NSC_RECT":
                max = 2;
                if (material is not ("" or "MIRROR" or "ABSORB")) throw new InvalidDataException("薄矩形的介质边界语义尚未完整映射。");
                parameters = new PlaneRectangleParameters(2 * P(1), 2 * P(2), material == "MIRROR" ? NonSequentialSurfaceBehavior.Reflective
                    : material == "ABSORB" ? NonSequentialSurfaceBehavior.Absorbing : NonSequentialSurfaceBehavior.Refractive);
                break;
            case "NSC_DETE":
                max = 17; kind = NonSequentialObjectKind.DetectorRectangle;
                if (material is not ("" or "ABSORB")) throw new InvalidDataException("反射式探测器材料尚未映射。");
                parameters = new DetectorRectangleParameters(2 * P(1), 2 * P(2), I(3), I(4), P(10, 0) != 0, material == "ABSORB");
                if (P(11, 0) != 0 || P(16, 0) != 0 || P(17, 0) != 0
                    || P(12, -90) != -90 || P(13, 90) != 90 || P(14, -90) != -90 || P(15, 90) != 90)
                    issues.Add(new(pose.Line, number, "DetectorModes", "Huygens PSF、分量筛选、镜像或角域设置需要额外映射。"));
                break;
            default:
                issues.Add(new(rows[0].Line, number, "ObjectType", $"尚未支持对象类型 {type}；保留全部记录，禁止替代几何。")); return null;
        }
        foreach (var key in p.Keys.Where(k => k < 1 || k > max)) issues.Add(new(rows[0].Line, number, "UnknownParameter", $"未知参数 {key} 已保留，不能忽略。"));
        return new(Guid.NewGuid(), $"{number} {type}", kind, true, true,
            new CoordinateSystem(new(Number(pose.Values[0]), Number(pose.Values[1]), Number(pose.Values[2])),
                Number(pose.Values[3]), Number(pose.Values[4]), Number(pose.Values[5])), null, null, parameters);
    }
    private static List<string> Tokens(string line)
    {
        var result = new List<string>(); var value = new StringBuilder(); var quote = false; var active = false;
        foreach (var c in line)
        {
            if (c == '"') { quote = !quote; active = true; }
            else if (!quote && char.IsWhiteSpace(c)) { if (active) { result.Add(value.ToString()); value.Clear(); active = false; } }
            else { value.Append(c); active = true; }
        }
        if (quote) throw new InvalidDataException("ZMX 中存在未闭合引号。"); if (active) result.Add(value.ToString()); return result;
    }
    private static bool IsAsset(string text) => new[] { ".dll", ".stl", ".step", ".stp", ".iges", ".igs", ".sat", ".pob", ".sdf", ".bsdf", ".brdf", ".spc", ".grd" }.Any(e => text.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static int Integer(string text) { var d = Number(text); return d == Math.Truncate(d) && d >= int.MinValue && d <= int.MaxValue ? (int)d : throw new FormatException("需要整数值。"); }
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new FormatException("需要有限数值。");
}
