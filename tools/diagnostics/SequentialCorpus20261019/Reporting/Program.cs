using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2) throw new ArgumentException("<completed-run-directory> <fresh-report-directory>");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh report directory.");
var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
JsonElement Read(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
string Full(string path) => Path.Combine(root, path);
void Write(string path, object value) => File.WriteAllText(Path.Combine(output, path), JsonSerializer.Serialize(value, options));
string Text(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind != JsonValueKind.Null ? item.ToString() : "";
int Int(JsonElement value, string key) => value.GetProperty(key).GetInt32();
double Number(JsonElement value, string key)
{
    var number = value.GetProperty(key).GetDouble();
    return double.IsFinite(number) ? number : throw new InvalidDataException("Non-finite comparison coordinate.");
}
bool Bool(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
Dictionary<string, int> Counts(IEnumerable<string> values) => values.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
bool Equivalent(JsonElement a, JsonElement b)
{
    if (a.ValueKind != b.ValueKind) return false;
    return a.ValueKind switch
    {
        JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
        JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
            && a.EnumerateObject().All(property => b.TryGetProperty(property.Name, out var value) && Equivalent(property.Value, value)),
        JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair => Equivalent(pair.First, pair.Second)),
        _ => a.ToString() == b.ToString()
    };
}
var sources = Read(Full("sources.json")).EnumerateArray().ToArray();
var models = Read(Full("models.json")).EnumerateArray().ToArray();
var items = Read(Full("analysis-results.json")).EnumerateArray().ToArray();
var jobs = Read(Full("native-jobs.json")).EnumerateArray().ToArray();
var identity = Read(Full("identity.json"));
var nativeReferenceFormat = Text(identity, "nativeReferenceFormat") is "zos" ? "zos" : "zmx";
var missing = jobs.Where(job => !File.Exists(Path.Combine(Text(job, "output"), "batch-result.json"))).ToArray();
var missingWorkers = items.Where(item => Text(item, "workbenchJob") != "" && !File.Exists(Path.Combine(Text(item, "path"), "workbench/worker-result.json"))).ToArray();
if (missing.Length != 0 || missingWorkers.Length != 0) throw new InvalidDataException($"Unfinished jobs: native={missing.Length}, Workbench={missingWorkers.Length}.");
Directory.CreateDirectory(output);
var inventory = Read(Full("inventory.json"));
var integrity = inventory.GetProperty("files").EnumerateArray().Select(file => new
{
    id = Text(file, "id"),
    sha256 = Text(file, "sha256"),
    originalMatches = Hash(Text(file, "path")) == Text(file, "sha256"),
    copyMatches = Text(file, "extension") == ".zda" ? (bool?)null : File.Exists(Full("sources/" + Text(file, "id"))) && Hash(Full("sources/" + Text(file, "id"))) == Text(file, "sha256")
}).ToArray();
if (integrity.Any(file => !file.originalMatches || file.copyMatches == false)) throw new InvalidDataException("A source or workspace copy changed.");
Write("source-integrity.json", integrity);
var rayResults = new List<RayResult>();
foreach (var directory in Directory.EnumerateDirectories(Full("rays")).Order(StringComparer.Ordinal))
{
    var id = Path.GetFileName(directory);
    var modelId = id[..id.LastIndexOf("-w", StringComparison.Ordinal)];
    var model = models.FirstOrDefault(value => Path.GetFileNameWithoutExtension(Text(value, "snapshot")) == modelId);
    var row = new RayResult { Id = id, Lens = model.ValueKind == JsonValueKind.Undefined ? "Unknown" : Text(model, "id") };
    rayResults.Add(row);
    try
    {
        if (File.Exists(Path.Combine(directory, "error.json"))) throw new InvalidDataException(File.ReadAllText(Path.Combine(directory, "error.json")));
        var completion = Read(Path.Combine(directory, "native/batch-result.json"));
        if (Int(completion, "exitCode") != 0) throw new InvalidDataException(File.ReadAllText(Path.Combine(directory, "native/error.json")));
        var core = Read(Path.Combine(directory, "core.json"));
        var native = Read(Path.Combine(directory, "native/ray-audit.json"));
        var inputs = Read(Path.Combine(directory, "inputs.json"));
        if (!Equivalent(inputs, native.GetProperty("inputs"))) throw new InvalidDataException("Native inputs differ from formal Core sample inputs.");
        var surfaces = native.GetProperty("surfaces").EnumerateArray().ToArray();
        row.Inputs = inputs.GetArrayLength();
        row.SurfaceResults = row.Inputs * surfaces.Length;
        var coreSurfaceCount = Int(model, "surfaceCount");
        if (surfaces.Length != coreSurfaceCount - 1)
        {
            row.Conclusion = "Incomparable";
            row.Reason = "Coordinate-break row folding requires an explicit native/Core surface correspondence.";
            continue;
        }
        var scale = Text(model, "unit") switch { "MM" => 1d, "CM" => 10d, "IN" => 25.4d, "M" => 1000d, _ => throw new InvalidDataException("Unknown native length unit.") };
        var physical = core.EnumerateArray().SelectMany(field => field.GetProperty("rays").EnumerateArray()).ToArray();
        if (physical.Length != row.Inputs) throw new InvalidDataException("Incomplete Core ray coverage.");
        var histories = physical.Select(ray => ray.GetProperty("samples").EnumerateArray().ToDictionary(sample => Int(sample, "surfaceNumber"))).ToArray();
        row.ReflectionDirectionConventionReview = histories.Any(history => history.Values.Any(sample => Text(sample, "interactionKind") == "Reflected"));
        var image = surfaces[^1].GetProperty("rays");
        for (var i = 0; i < row.Inputs; i++)
        {
            var nativeRay = image[i];
            var last = physical[i].GetProperty("samples").EnumerateArray().Last();
            var accepted = Int(nativeRay, "error") == 0 && Int(nativeRay, "vignette") == 0;
            var coreAccepted = Int(last, "surfaceNumber") == coreSurfaceCount - 1 && !Bool(last, "vignetted") && Number(last, "intensity") > 0;
            if (accepted != coreAccepted) row.AcceptanceDifferences++;
            if (Int(nativeRay, "error") == 0 && Int(nativeRay, "vignette") > 0 && Bool(last, "vignetted") && Bool(last, "hit")
                && Int(nativeRay, "vignette") != Int(last, "surfaceNumber")) row.FirstClipDifferences++;
        }
        foreach (var surface in surfaces)
        {
            var si = Int(surface, "surface");
            var rays = surface.GetProperty("rays");
            if (rays.GetArrayLength() != row.Inputs) throw new InvalidDataException("Incomplete native pupil coverage.");
            for (var i = 0; i < row.Inputs; i++)
            {
                var nativeRay = rays[i];
                if (Int(nativeRay, "number") != i + 1) throw new InvalidDataException("Native ray order differs.");
                if (Int(nativeRay, "error") != 0 || Int(nativeRay, "vignette") != 0 || !histories[i].TryGetValue(si, out var sample)
                    || Bool(sample, "vignetted") || !Bool(sample, "hit")) continue;
                row.PhysicalSurfaceComparisons++;
                foreach (var key in new[] { "x", "y", "z" })
                {
                    var expected = Number(nativeRay, key) * scale;
                    var delta = Math.Abs(Number(sample.GetProperty("position"), key) - expected);
                    row.MaximumCoordinateMillimeters = Math.Max(row.MaximumCoordinateMillimeters, delta);
                    if (delta > 1e-6 + 1e-6 * Math.Abs(expected)) row.CoordinateFailures++;
                }
                foreach (var (key, nativeKey) in new[] { ("x", "l"), ("y", "m"), ("z", "n") })
                {
                    var expected = Number(nativeRay, nativeKey);
                    var delta = Math.Abs(Number(sample.GetProperty("direction"), key) - expected);
                    row.MaximumDirection = Math.Max(row.MaximumDirection, delta);
                    if (delta > 1e-8 + 1e-6 * Math.Abs(expected)) row.DirectionFailures++;
                }
            }
        }
        row.Conclusion = row.AcceptanceDifferences + row.FirstClipDifferences + row.CoordinateFailures + row.DirectionFailures > 0
            ? "Difference" : row.PhysicalSurfaceComparisons > 0 ? "Pass" : "Incomparable";
        if (model.GetProperty("preflight").GetArrayLength() != 0)
        {
            row.CalculatedConclusion = row.Conclusion;
            row.Conclusion = "Incomparable";
            row.Reason = "Unsupported imported capability; diagnostic rays do not certify the model.";
        }
        else if (row.ReflectionDirectionConventionReview && row.DirectionFailures != 0)
            row.Reason = "Raw reflection-direction convention requires alignment before interpreting direction errors as physical tracing defects.";
    }
    catch (Exception exception) { row.Conclusion = "Error"; row.Reason = exception.Message; }
}
Write("ray-results.json", rayResults);
var lensRows = new List<object>();
var csv = new List<string> { "Lens,CoreConfigurations,NativeConfigurations,ImportErrors,Pass,Close,Difference,Incomparable,Error,Skipped,RayPass,RayDifference,RayIncomparable,RayError,FormatModelReadback,WorstNRMSE,Notes" };
var markdown = new StringBuilder("# Sequential 全目录逐镜头对比\n\n日期：2026-10-19。所有源文件逐字节保持；保留原比较策略、采样及容差。原生数据为本机新采集，Core 由正式 Application 执行。\n\n");
markdown.AppendLine("| 镜头 | Core/原生配置 | 导入错误 | Pass | Close | Difference | 不可比 | Error | 光线 P/D/I/E | ZMX/ZOS 模型读回 | 最大 NRMSE |\n|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|");
string Csv(object? value) => '"' + Convert.ToString(value, CultureInfo.InvariantCulture)!.Replace("\"", "\"\"") + '"';
var nativeProbes = jobs.Where(job => Text(job, "id").StartsWith("probe:", StringComparison.Ordinal)).ToArray();
var formatProbes = jobs.Where(job => Text(job, "id").StartsWith("format-probe:", StringComparison.Ordinal)).ToArray();
foreach (var sourceRecord in sources)
{
    var source = sourceRecord.GetProperty("source");
    var lens = Text(source, "id");
    var lensModels = models.Where(model => Text(model, "id") == lens).ToArray();
    var imported = lensModels.Where(model => Text(model, "error") == "").ToArray();
    var importErrors = lensModels.Where(model => Text(model, "error") != "").Select(model => Text(model, "error")).ToArray();
    var lensItems = items.Where(item => Text(item, "lens") == lens).ToArray();
    var analysisCounts = Counts(lensItems.Select(item => Text(item.GetProperty("run"), "conclusion")));
    int Count(string key) => analysisCounts.GetValueOrDefault(key);
    var rays = rayResults.Where(ray => ray.Lens == lens).ToArray();
    var rayCounts = Counts(rays.Select(ray => ray.Conclusion));
    var copyPath = Full("sources/" + lens);
    JsonElement? Probe(IEnumerable<JsonElement> candidates) => candidates.Select(job => new { Job = job, Request = Read(Text(job, "request")) })
        .Where(value => Path.ChangeExtension(Text(value.Request, "input"), ".zmx").Equals(Path.ChangeExtension(copyPath, ".zmx"), StringComparison.OrdinalIgnoreCase)
            && Int(value.Request, "configuration") == 1).Select(value => (JsonElement?)value.Job).FirstOrDefault();
    var nativeProbe = Probe(nativeProbes);
    var formatProbe = Probe(formatProbes);
    var nativePath = nativeProbe.HasValue ? Path.Combine(Text(nativeProbe.Value, "output"), "model.json") : "";
    var zosModelPath = formatProbe.HasValue ? Path.Combine(Text(formatProbe.Value, "output"), "model.json") : "";
    var nativeConfigurations = File.Exists(nativePath) ? Int(Read(nativePath), "configurationCount") : 0;
    var format = "Unavailable";
    var notes = new List<string>();
    if (File.Exists(nativePath) && File.Exists(zosModelPath))
    {
        var left = JsonNode.Parse(File.ReadAllText(nativePath))!.AsObject();
        var right = JsonNode.Parse(File.ReadAllText(zosModelPath))!.AsObject();
        left.Remove("warnings"); right.Remove("warnings");
        format = Equivalent(JsonSerializer.SerializeToElement(left), JsonSerializer.SerializeToElement(right)) ? "Equal" : "Difference";
    }
    if (importErrors.Length != 0) notes.AddRange(importErrors.Select(error => error.Split('\n')[0].Trim()));
    notes.AddRange(imported.SelectMany(model => model.GetProperty("preflight").EnumerateArray()).Select(issue => issue.GetRawText()).Distinct());
    if (nativeConfigurations != imported.Length) notes.Add($"Native configurations={nativeConfigurations}, completed Core snapshots={imported.Length}; review missing import/configuration coverage.");
    if (rays.Any(ray => ray.ReflectionDirectionConventionReview)) notes.Add("Reflection raw local direction convention requires review.");
    var metrics = lensItems.SelectMany(item => item.GetProperty("run").GetProperty("metrics").EnumerateArray()).ToArray();
    double? worst = metrics.Length == 0 ? null : metrics.Max(metric => Number(metric, "nrmse"));
    lensRows.Add(new
    {
        lens,
        coreConfigurations = imported.Length,
        nativeConfigurations,
        importErrors,
        analysisCounts,
        rayCounts,
        formatModelReadback = format,
        worstNrmse = worst,
        notes,
        analysisRequests = lensItems.Length
    });
    csv.Add(string.Join(',', new object?[] { lens, imported.Length, nativeConfigurations, importErrors.Length, Count("Pass"), Count("Close"), Count("Difference"), Count("Incomparable"), Count("Error"), Count("Skipped"),
        rayCounts.GetValueOrDefault("Pass"), rayCounts.GetValueOrDefault("Difference"), rayCounts.GetValueOrDefault("Incomparable"), rayCounts.GetValueOrDefault("Error"), format, worst, string.Join(" | ", notes) }.Select(Csv)));
    markdown.AppendLine($"| {lens.Replace("|", "\\|")} | {imported.Length}/{nativeConfigurations} | {importErrors.Length} | {Count("Pass")} | {Count("Close")} | {Count("Difference")} | {Count("Incomparable")} | {Count("Error")} | {rayCounts.GetValueOrDefault("Pass")}/{rayCounts.GetValueOrDefault("Difference")}/{rayCounts.GetValueOrDefault("Incomparable")}/{rayCounts.GetValueOrDefault("Error")} | {format} | {worst?.ToString("G6", CultureInfo.InvariantCulture) ?? "—"} |");
}
var summary = new
{
    date = "2026-10-19",
    nativeReferenceFormat,
    sourceLenses = sources.Length,
    sourceFiles = integrity.Length,
    originalSourceFilesUnchanged = integrity.Count(file => file.originalMatches),
    nativeJobs = jobs.Length,
    nativeFailures = jobs.Count(job => Int(Read(Path.Combine(Text(job, "output"), "batch-result.json")), "exitCode") != 0),
    completedNativeAttemptsIncludingRetainedFailures = Directory.EnumerateFiles(root, "batch-result.json", SearchOption.AllDirectories).Count(),
    completedWorkbenchAttemptsIncludingRetainedFailures = Directory.EnumerateFiles(root, "worker-result.json", SearchOption.AllDirectories).Count(),
    analysisRequests = items.Length,
    analysisCounts = Counts(items.Select(item => Text(item.GetProperty("run"), "conclusion"))),
    rayJobs = rayResults.Count,
    rayCounts = Counts(rayResults.Select(ray => ray.Conclusion)),
    physicalSurfaceComparisons = rayResults.Sum(ray => (long)ray.PhysicalSurfaceComparisons),
    comparedValues = rayResults.Sum(ray => (long)ray.PhysicalSurfaceComparisons * 6),
    comparisonPolicySha256 = Hash(Full("comparison-settings.json")),
    inventorySha256 = Hash(Full("inventory.json")),
    completeExecutionAttempts = true,
    scope = "18 primary analyses plus 4 edge-field controls at each positive-weight wavelength and GQ6 rays for every successfully prepared configuration. Not all field/wavelength/sampling combinations. ZOS/ZMX equality covers captured configuration-1 model properties only, not all numerical outputs.",
    lensRows
};
Write("summary.json", summary);
File.WriteAllLines(Path.Combine(output, "lens-matrix.csv"), csv, new UTF8Encoding(true));
var analysesCsv = new List<string> { "Lens,Configuration,Scope,Analysis,Conclusion,Field,Wavelength,PupilSampling,WorstNRMSE,MaximumAbsolute,Reason,Evidence" };
foreach (var item in items)
{
    var run = item.GetProperty("run");
    var request = run.TryGetProperty("request", out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    var metrics = run.GetProperty("metrics").EnumerateArray().ToArray();
    analysesCsv.Add(string.Join(',', new object?[] { Text(item, "lens"), Int(item, "configuration"), Text(item, "scope"), Text(run, "key"), Text(run, "conclusion"),
        request.ValueKind == JsonValueKind.Object ? Int(request, "field") : null, request.ValueKind == JsonValueKind.Object ? Int(request, "wavelength") : null,
        request.ValueKind == JsonValueKind.Object ? Int(request, "pupilSampling") : null, metrics.Length == 0 ? null : metrics.Max(metric => Number(metric, "nrmse")),
        metrics.Length == 0 ? null : metrics.Max(metric => Number(metric, "maxAbsolute")), Text(run, "reason"), Text(item, "path") }.Select(Csv)));
}
File.WriteAllLines(Path.Combine(output, "analysis-matrix.csv"), analysesCsv, new UTF8Encoding(true));
markdown.AppendLine("\nP/D/I/E 分别为 Pass、Difference、Incomparable、Error。Close 不计 Pass；Error 和不可比保留在分母。反射局部方向约定、坐标断点面号对应、单位/无焦适配器与未支持面型另列限制，原始差值不自动等同于已确认的光学缺陷。任何单项通过不代表整镜头认证。完整误差、原因与证据目录见相邻 CSV/JSON。\n");
markdown.AppendLine($"分析结论：{JsonSerializer.Serialize(summary.analysisCounts)}；逐面光线：{JsonSerializer.Serialize(summary.rayCounts)}。源文件 {summary.originalSourceFilesUnchanged}/{summary.sourceFiles} 保持。\n");
File.WriteAllText(Path.Combine(output, "LENS_COMPARISON.md"),
    $"原生参考格式：{nativeReferenceFormat.ToUpperInvariant()}。ZOS 补充控制的 Core 输入为原生导出的 ZMX；不代表产品直接支持 ZOS 导入。\n\n" + markdown);
Console.WriteLine(JsonSerializer.Serialize(new { summary.sourceLenses, summary.analysisRequests, summary.analysisCounts, summary.rayJobs, summary.rayCounts, summary.nativeFailures }, options));

sealed class RayResult
{
    public string Id { get; set; } = "";
    public string Lens { get; set; } = "";
    public string Conclusion { get; set; } = "Error";
    public string? CalculatedConclusion { get; set; }
    public string? Reason { get; set; }
    public int Inputs { get; set; }
    public int SurfaceResults { get; set; }
    public int PhysicalSurfaceComparisons { get; set; }
    public int AcceptanceDifferences { get; set; }
    public int FirstClipDifferences { get; set; }
    public int CoordinateFailures { get; set; }
    public int DirectionFailures { get; set; }
    public double MaximumCoordinateMillimeters { get; set; }
    public double MaximumDirection { get; set; }
    public bool ReflectionDirectionConventionReview { get; set; }
}
