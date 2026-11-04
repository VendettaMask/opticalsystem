using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.NonSequential;

/// <summary>One bounded archive of the real scene, embedded geometry, complete referenced material
/// models, immutable request and optional results. Loading/replaying never changes a workspace.</summary>
public static class NonSequentialDesignArchiveStore
{
    public const string Format = "OpticalSystemDesign.NscExperiment";
    public const int Version = 1;
    public const long MaximumBytes = BoundedFile.MaximumExportBytes;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, MaxDepth = 64, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static async Task SaveAsync(string path, StarOptProjectDocument sourceProject, MaterialRegistry materials,
        NonSequentialDesignExperiment plan, NonSequentialDesignRunKind kind,
        NonSequentialDesignResult? optimization = null, NonSequentialToleranceResult? tolerance = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        materials = materials.CreateSnapshot();
        sourceProject = sourceProject with { Configurations = sourceProject.Configurations.Select(c => c.Clone()).ToArray(),
            NonSequentialDocument = sourceProject.NonSequentialDocument?.Clone() };
        var doc = sourceProject.NonSequentialDocument
            ?? throw new ArgumentException("实验需要完整的非序列场景。");
        ValidatePlan(doc, materials, plan, kind);
        if (optimization is not null && (kind != NonSequentialDesignRunKind.Optimization || optimization.SourceSceneSha256 != plan.SourceSceneSha256
                || Encode(optimization.Settings).AsSpan().SequenceEqual(Encode(plan.Settings)) == false
                || !optimization.Variables.SequenceEqual(plan.Variables)))
            throw new InvalidDataException("优化结果与实验原始场景/变量/设置不一致。");
        if (tolerance is not null && (kind != NonSequentialDesignRunKind.Tolerance || tolerance.SourceSceneSha256 != plan.SourceSceneSha256
                || tolerance.Seed != plan.ToleranceSettings?.ManufacturingSeed || tolerance.Samples.Count != plan.ToleranceSettings.Samples))
            throw new InvalidDataException("公差结果与实验原始场景/制造抽样不一致。");
        var modelRecords = NonSequentialSceneHasher.ReferencedMaterialNames(doc).Select(alias =>
            new NonSequentialDesignMaterial(alias, ComponentSnapshotFactory.FromMaterial(materials.Resolve(alias)))).ToArray();
        _ = RestoreMaterials(doc, modelRecords); // Reject models that would need the original user's catalogue.
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["plan.json"] = Encode(plan), ["materials.json"] = Encode(modelRecords)
        };
        if (optimization is not null || tolerance is not null)
            data.Add("result.json", Encode(new NonSequentialDesignRecordedRun(optimization is null ? null : NonSequentialOptimizationRecord.From(optimization), tolerance)));
        foreach (var pair in data) if (pair.Value.Length > (pair.Key == "result.json" ? MaximumBytes : BoundedFile.MaximumSettingsBytes))
            throw new InvalidDataException($"实验 {pair.Key} 超过既有文件预算。");
        var temporaryProject = Path.Combine(Path.GetTempPath(), $"nsc-experiment-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(sourceProject, temporaryProject, cancellationToken).ConfigureAwait(false);
            var length = new FileInfo(temporaryProject).Length;
            if (length + data.Values.Sum(b => (long)b.Length) > MaximumBytes) throw new InvalidDataException("完整实验超过既有 256 MiB 导出预算；未省略场景或资产。");
            string projectHash;
            await using (var input = BoundedFile.OpenRead(temporaryProject, MaximumBytes, "NSC experiment source"))
                projectHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            var entries = data.Select(p => new NonSequentialArchiveEntry(p.Key, p.Value.Length, Hash(p.Value)))
                .Append(new("source.staropt", length, projectHash)).OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
            var manifest = new NonSequentialDesignArchiveManifest(Format, Version, kind, NonSequentialDesignEngine.Algorithm,
                NonSequentialToleranceEngine.Algorithm, plan.SourceSceneSha256, Array.AsReadOnly(entries));
            var manifestBytes = Encode(manifest);
            await BoundedFile.WriteAtomicAsync(path, MaximumBytes, "NSC experiment", async (stream, token) =>
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var pair in data.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    await using var output = Entry(zip, pair.Key, CompressionLevel.Optimal).Open();
                    await output.WriteAsync(pair.Value, token).ConfigureAwait(false);
                }
                await using (var output = Entry(zip, "source.staropt", CompressionLevel.NoCompression).Open())
                await using (var input = BoundedFile.OpenRead(temporaryProject, MaximumBytes, "NSC experiment source"))
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                await using (var output = Entry(zip, "manifest.json", CompressionLevel.Optimal).Open())
                    await output.WriteAsync(manifestBytes, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { File.Delete(temporaryProject); }
    }

    public static async Task<NonSequentialDesignArchiveInfo> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        (await LoadAsync(path, cancellationToken).ConfigureAwait(false)).Info;

    public static async Task<NonSequentialDesignReplayResult> ReplayAsync(string path,
        IProgress<NonSequentialDesignProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(path, cancellationToken).ConfigureAwait(false); var info = loaded.Info; var p = info.Plan;
        if (info.Manifest.OptimizationProtocol != NonSequentialDesignEngine.Algorithm || info.Manifest.ToleranceProtocol != NonSequentialToleranceEngine.Algorithm)
            throw new NotSupportedException("实验计算协议与当前版本不同，不能按另一套算法悄悄复算。");
        var doc = loaded.Project.NonSequentialDocument!;
        NonSequentialDesignResult? optimization = null; NonSequentialToleranceResult? tolerance = null;
        if (info.Manifest.Kind == NonSequentialDesignRunKind.Optimization)
            optimization = NonSequentialDesignEngine.Optimize(doc, loaded.Materials, p.Variables, p.Targets, p.Settings, progress, cancellationToken);
        else
        {
            var s = p.ToleranceSettings!;
            tolerance = NonSequentialToleranceEngine.Run(doc, loaded.Materials, p.Tolerances!, p.Targets, s.Samples, s.ManufacturingSeed,
                s.Trace ?? p.Settings, p.Compensators, s.CompensationTraceEvaluations, progress, cancellationToken);
        }
        var record = new NonSequentialDesignRecordedRun(optimization is null ? null : NonSequentialOptimizationRecord.From(optimization), tolerance);
        var matches = info.Recorded is null ? (bool?)null : Encode(record).AsSpan().SequenceEqual(Encode(info.Recorded));
        return new(info, optimization, tolerance, matches);
    }

    private sealed record Loaded(NonSequentialDesignArchiveInfo Info, StarOptProjectDocument Project, MaterialRegistry Materials);
    private static async Task<Loaded> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = BoundedFile.OpenRead(path, MaximumBytes, "NSC experiment");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count is < 4 or > 5 || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new InvalidDataException("实验条目缺失、重复或数量无效。");
        var manifest = Decode<NonSequentialDesignArchiveManifest>(await ReadEntryAsync(zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("实验缺少 manifest.json。"), BoundedFile.MaximumSettingsBytes, cancellationToken).ConfigureAwait(false));
        if (manifest.Format != Format || manifest.Version != Version || !Enum.IsDefined(manifest.Kind) || manifest.Entries is null)
            throw new InvalidDataException("实验格式/版本无效。");
        var required = new[] { "source.staropt", "materials.json", "plan.json" };
        var allowed = required.Append("result.json").ToHashSet(StringComparer.Ordinal);
        if (manifest.Entries.Count is < 3 or > 4 || manifest.Entries.Any(e => e is null)
            || manifest.Entries.Select(e => e.Name).Distinct(StringComparer.Ordinal).Count() != manifest.Entries.Count
            || required.Any(n => !manifest.Entries.Any(e => e.Name == n)) || manifest.Entries.Any(e => !allowed.Contains(e.Name) || e.Bytes <= 0 || e.Bytes > MaximumBytes)
            || manifest.Entries.Sum(e => e.Bytes) > MaximumBytes || zip.Entries.Any(e => e.FullName != "manifest.json" && !manifest.Entries.Any(m => m.Name == e.FullName)))
            throw new InvalidDataException("实验包含未知路径、无效长度或超过既有展开预算。");
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var expected in manifest.Entries)
        {
            var entry = zip.GetEntry(expected.Name) ?? throw new InvalidDataException("实验缺少 " + expected.Name);
            if (entry.Length != expected.Bytes) throw new InvalidDataException("实验条目长度不匹配。");
            var bytes = await ReadEntryAsync(entry, expected.Name is "materials.json" or "plan.json" ? BoundedFile.MaximumSettingsBytes : MaximumBytes, cancellationToken).ConfigureAwait(false);
            if (Hash(bytes) != expected.Sha256) throw new InvalidDataException("实验条目 SHA-256 不匹配：" + expected.Name);
            data.Add(expected.Name, bytes);
        }
        var project = StarOptProjectStore.Deserialize(data["source.staropt"]);
        var doc = project.NonSequentialDocument ?? throw new InvalidDataException("实验没有非序列场景。");
        var materials = RestoreMaterials(doc, Decode<NonSequentialDesignMaterial[]>(data["materials.json"]));
        var plan = Decode<NonSequentialDesignExperiment>(data["plan.json"]); ValidatePlan(doc, materials, plan, manifest.Kind);
        if (manifest.SourceSceneSha256 != plan.SourceSceneSha256) throw new InvalidDataException("实验场景摘要不匹配。");
        var recorded = data.TryGetValue("result.json", out var result) ? Decode<NonSequentialDesignRecordedRun>(result) : null;
        if (recorded is not null && (manifest.Kind == NonSequentialDesignRunKind.Optimization
                ? recorded.Optimization?.SourceSceneSha256 != plan.SourceSceneSha256 || recorded.Tolerance is not null
                : recorded.Tolerance?.SourceSceneSha256 != plan.SourceSceneSha256 || recorded.Optimization is not null))
            throw new InvalidDataException("实验结果种类或来源不匹配。");
        return new(new(manifest, plan, recorded), project, materials);
    }

    private static MaterialRegistry RestoreMaterials(NonSequentialDocument doc, IReadOnlyList<NonSequentialDesignMaterial> records)
    {
        var expected = NonSequentialSceneHasher.ReferencedMaterialNames(doc);
        if (records.Count != expected.Count || records.Any(r => r is null || r.Model is null)
            || records.Select(r => r.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Count
            || records.Any(r => !expected.Contains(r.Alias, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("实验没有完整保存实际引用的材料模型。");
        var registry = new MaterialRegistry();
        foreach (var record in records)
        {
            if (record.Model.Kind is "catalog" or "unresolved") throw new NotSupportedException("实验材料仍依赖外部目录或未解析模型。");
            var material = ComponentSnapshotFactory.ToMaterial(record.Model, record.Alias, registry);
            if (JsonSerializer.Serialize(record.Model) != JsonSerializer.Serialize(ComponentSnapshotFactory.FromMaterial(material)))
                throw new InvalidDataException("实验材料存在未知或被隐式替换的字段。");
            registry.Register(record.Alias, material);
        }
        return registry;
    }
    private static void ValidatePlan(NonSequentialDocument doc, MaterialRegistry materials, NonSequentialDesignExperiment p, NonSequentialDesignRunKind kind)
    {
        if (p.FormatVersion != 1 || p.Settings is null || p.Targets is null || p.Variables is null
            || p.Variables.Count > 64 || p.Compensators?.Count > 64 || !Enum.IsDefined(kind)
            || p.SourceSceneSha256 != NonSequentialSceneHasher.Compute(doc, materials)) throw new InvalidDataException("实验方案或源场景/材料身份无效。");
        var trace = kind == NonSequentialDesignRunKind.Tolerance ? p.ToleranceSettings?.Trace ?? p.Settings : p.Settings;
        NonSequentialDesignEngine.Validate(doc, p.Targets, trace);
        if (kind == NonSequentialDesignRunKind.Optimization) NonSequentialDesignParameters.Validate(doc, p.Variables);
        else
        {
            if (p.Tolerances is not { Count: > 0 and <= 64 } || p.ToleranceSettings is not { Samples: >= 0 and <= 10000, CompensationTraceEvaluations: >= 2 and <= 256 }
                || p.Tolerances.Any(t => t is null || !double.IsFinite(t.Scale) || t.Scale <= 0 || !Enum.IsDefined(t.Distribution)))
                throw new InvalidDataException("实验制造公差/样本设置无效。");
            NonSequentialDesignParameters.Validate(doc, p.Tolerances.Select(t => t.Variable).ToArray());
        }
        if (p.Compensators is { Count: > 0 }) NonSequentialDesignParameters.Validate(doc, p.Compensators);
    }
    private static ZipArchiveEntry Entry(ZipArchive zip, string name, CompressionLevel level)
    { var entry = zip.CreateEntry(name, level); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); return entry; }
    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long limit, CancellationToken token)
    {
        if (entry.Length <= 0 || entry.Length > limit) throw new InvalidDataException("实验条目超过文件限额。");
        await using var input = entry.Open(); var data = new byte[checked((int)entry.Length)];
        await input.ReadExactlyAsync(data, token).ConfigureAwait(false);
        if (input.ReadByte() != -1) throw new InvalidDataException("实验条目的展开长度不一致。");
        return data;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    private static T Decode<T>(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 64 });
            CheckDuplicates(document.RootElement);
            return JsonSerializer.Deserialize<T>(data, Json) ?? throw new InvalidDataException("实验条目为空。");
        }
        catch (JsonException e) { throw new InvalidDataException("实验 JSON 字段或格式无效。", e); }
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("实验 JSON 包含重复字段。"); CheckDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }
}
