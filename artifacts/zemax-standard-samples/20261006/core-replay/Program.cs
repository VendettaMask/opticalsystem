using System.Diagnostics;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Configuration;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Workbench;

if (args.Length is < 2 or > 3 || args.Length == 3 && args[2] != "--import-source") throw new ArgumentException("original-summary.json fresh-output [--import-source]");
var importSource = args.Length == 3;
var summaryPath = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("Fresh output required; original captures must never be overwritten");
Directory.CreateDirectory(output);
using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
var immutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
void Retain(string path)
{
    if (File.Exists(path)) immutable.TryAdd(path, JsonFiles.Hash(File.ReadAllBytes(path)));
}
Retain(summaryPath);
var records = new List<object>();
var runs = new List<AnalysisRun>();
var changes = new List<object>();
foreach (var row in summary.RootElement.GetProperty("rows").EnumerateArray())
{
    var nativePath = row.GetProperty("rawZemaxPath").GetString()!;
    var capture = Directory.GetParent(nativePath)!.Parent!.Parent!.Parent!.FullName;
    var manifestPath = Path.Combine(capture, "manifest.json");
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var source = manifest.RootElement.GetProperty("inputPath").GetString()!;
    var sourceHash = manifest.RootElement.GetProperty("sourceSha256").GetString()!;
    if (JsonFiles.Hash(File.ReadAllBytes(source)) != sourceHash)
        throw new InvalidDataException("Original source changed: " + source);
    var copy = Path.Combine(capture, "input", manifest.RootElement.GetProperty("fileName").GetString()!);
    if (JsonFiles.Hash(File.ReadAllBytes(copy)) != sourceHash)
        throw new InvalidDataException("Captured source copy changed: " + copy);
    var config = Path.Combine(capture, "comparison-settings.json");
    if (JsonFiles.Hash(File.ReadAllBytes(config)) != manifest.RootElement.GetProperty("configurationSha256").GetString())
        throw new InvalidDataException("Captured comparison configuration changed");
    var previousPath = row.GetProperty("originalComparisonPath").GetString()!;
    var original = JsonFiles.Read<AnalysisRun>(previousPath);
    var request = original.Request ?? throw new InvalidDataException("Original canonical request missing");
    var snapshot = Path.Combine(capture, "input", $"configuration-{request.Configuration}.json");
    var snapshotHash = JsonFiles.Hash(File.ReadAllBytes(snapshot));
    var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(snapshot));
    var executionSnapshot = snapshot;
    string? importSnapshotHash = null;
    if (importSource)
    {
        var imported = await WorkbenchExecutor.Import(copy);
        optic = imported.Configurations[request.Configuration - 1];
        executionSnapshot = Path.Combine(output, "imports", sourceHash, $"configuration-{request.Configuration}.json");
        if (!File.Exists(executionSnapshot)) JsonFiles.Write(executionSnapshot, optic.ToSnapshot());
        importSnapshotHash = JsonFiles.Hash(File.ReadAllBytes(executionSnapshot));
    }
    if (request.UseRayAiming != optic.RayAimingEnabled)
        throw new InvalidDataException("Captured snapshot and request disagree about ray aiming");
    var nativeRequest = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(Path.GetDirectoryName(nativePath)!, "request.json"));
    if (request.Fingerprint != nativeRequest.Fingerprint)
        throw new InvalidDataException("Native request fingerprint mismatch");
    var oldWorkbench = row.GetProperty("rawWorkbenchPath").GetString()!;
    var oldProvenance = Path.Combine(Path.GetDirectoryName(oldWorkbench)!, "provenance.json");
    if (File.Exists(oldProvenance))
    {
        using var provenance = JsonDocument.Parse(File.ReadAllText(oldProvenance));
        if (provenance.RootElement.GetProperty("snapshotSha256").GetString() != snapshotHash
            || provenance.RootElement.GetProperty("requestFingerprint").GetString() != request.Fingerprint)
            throw new InvalidDataException("Original Workbench snapshot/request mismatch");
    }
    var nativeDirectory = Path.GetDirectoryName(nativePath)!;
    var environmentPath = Path.Combine(nativeDirectory, "environment.json");
    using var environment = JsonDocument.Parse(File.ReadAllText(environmentPath));
    var nativeEnvironment = environment.RootElement.Clone();
    foreach (var path in Directory.EnumerateFiles(nativeDirectory)) Retain(path);
    foreach (var path in new[] { source, copy, manifestPath, previousPath, snapshot, config, oldWorkbench, oldProvenance }) Retain(path);
    var batch = row.GetProperty("batch").GetString()!;
    var lens = row.GetProperty("lens").GetString()!;
    var caseRoot = Path.Combine(output, batch, lens);
    var workbenchRoot = Path.Combine(caseRoot, "raw", "workbench", original.Directory);
    var freshPath = Path.Combine(workbenchRoot, "data.json");
    var run = original with { Metrics = [], Normalizations = [], Reason = "", WorkbenchStatus = CaptureStatus.Skipped };
    var timer = Stopwatch.StartNew();
    try
    {
        WorkbenchExecutor.Execute(new WorkbenchJob(executionSnapshot, workbenchRoot, request));
        run.WorkbenchStatus = CaptureStatus.Captured;
    }
    catch (Exception ex)
    {
        run.WorkbenchStatus = CaptureStatus.Failed;
        run.Conclusion = Conclusion.Error;
        run.Reason = "Current formal Workbench computation: " + ex.Message;
        JsonFiles.Write(Path.Combine(caseRoot, "errors", original.Directory + ".json"), new { ex.Message, Exception = ex.ToString() });
    }
    if (run.WorkbenchStatus == CaptureStatus.Captured)
    {
        try
        {
            var entry = AnalysisComparisonRegistry.Get(run.Key);
            var z = ResultNormalizer.Zemax(nativePath, entry, request);
            var w = ResultNormalizer.Workbench(freshPath, entry, request);
            JsonFiles.Write(Path.Combine(caseRoot, "normalized", "zemax", run.Directory + ".json"), z);
            JsonFiles.Write(Path.Combine(caseRoot, "normalized", "workbench", run.Directory + ".json"), w);
            run.Normalizations.AddRange(w.Transformations);
            run.Normalizations.AddRange(z.Transformations);
            ComparisonRunner.Compare(run, entry, new AnalysisConfiguration { Quantities = original.Tolerances }, w, z, caseRoot);
        }
        catch (InvalidDataException ex)
        {
            run.Conclusion = Conclusion.Incomparable;
            run.Reason = "Original physical/reference gate: " + ex.Message;
        }
    }
    timer.Stop();
    run.ElapsedMilliseconds = timer.ElapsedMilliseconds;
    var comparisonPath = Path.Combine(caseRoot, "comparisons", run.Directory, "comparison.json");
    JsonFiles.Write(comparisonPath, run);
    var originalConclusion = Enum.Parse<Conclusion>(row.GetProperty("conclusion").GetString()!);
    var priorMetricCount = row.GetProperty("numericMetricCount").GetInt32();
    var record = new
    {
        Batch = batch, Lens = lens, Key = run.Key, OriginalConclusion = originalConclusion, CurrentConclusion = run.Conclusion,
        OriginalWorstNrmse = row.GetProperty("worstNrmse"),
        OriginalWorstMaxAbsolute = row.GetProperty("worstMaxAbsolute"),
        CurrentWorstNrmse = run.Metrics.Count == 0 ? (double?)null : run.Metrics.Max(m => m.Nrmse),
        CurrentWorstMaxAbsolute = run.Metrics.Count == 0 ? (double?)null : run.Metrics.Max(m => m.MaxAbsolute),
        OriginalMetricCount = priorMetricCount, CurrentMetricCount = run.Metrics.Count,
        run.WorkbenchStatus, run.ZemaxStatus, run.Reason, run.Request, run.Tolerances,
        OriginalComparison = previousPath, CurrentComparison = comparisonPath,
        Snapshot = snapshot, SnapshotSha256 = snapshotHash, Source = source, SourceSha256 = sourceHash,
        ModelOrigin = importSource ? "Fresh import of unchanged original source" : "Unchanged captured snapshot",
        ExecutionSnapshot = executionSnapshot, ExecutionSnapshotSha256 = importSnapshotHash ?? snapshotHash,
        NativeRaw = nativePath, NativeRawSha256 = JsonFiles.Hash(File.ReadAllBytes(nativePath)),
        NativeEnvironment = nativeEnvironment, NativeRequestFingerprint = nativeRequest.Fingerprint,
        OriginalWorkbenchRaw = oldWorkbench, CurrentWorkbenchRaw = freshPath,
        OriginalWorkbenchRawSha256 = File.Exists(oldWorkbench) ? JsonFiles.Hash(File.ReadAllBytes(oldWorkbench)) : null,
        CurrentWorkbenchRawSha256 = File.Exists(freshPath) ? JsonFiles.Hash(File.ReadAllBytes(freshPath)) : null,
        RequestChanged = false, TolerancesChanged = false, NativeRecaptured = false
    };
    records.Add(record);
    runs.Add(run);
    if (originalConclusion != run.Conclusion) changes.Add(record);
    JsonFiles.Write(Path.Combine(output, "progress.json"), new { Completed = records.Count, Total = summary.RootElement.GetProperty("attempted").GetInt32(), Changes = changes });
    Console.WriteLine($"{records.Count}: {batch}/{lens}/{run.Key}: {originalConclusion} -> {run.Conclusion}");
}
foreach (var (path, hash) in immutable)
    if (JsonFiles.Hash(File.ReadAllBytes(path)) != hash) throw new InvalidDataException("Immutable evidence changed: " + path);
JsonFiles.Write(Path.Combine(output, "immutable-evidence.json"), immutable);
JsonFiles.Write(Path.Combine(output, "summary.json"), new
{
    CreatedUtc = DateTimeOffset.UtcNow, Mode = importSource ? "Fresh original-source import against immutable original OpticStudio 2026 R1 captures" : "Current formal shared Core/Application recalculation of unchanged captured snapshots against immutable original OpticStudio 2026 R1 captures",
    OriginalSummary = summaryPath, OriginalSummarySha256 = immutable[summaryPath],
    Total = runs.Count, Counts = runs.GroupBy(r => r.Conclusion).ToDictionary(g => g.Key.ToString(), g => g.Count()),
    ChangedConclusions = changes.Count, Changes = changes, Rows = records,
    OriginalPassRegressions = records.Count(record =>
    {
        var json = JsonSerializer.SerializeToElement(record, JsonFiles.Options);
        return json.GetProperty("originalConclusion").GetString() == "Pass" && json.GetProperty("currentConclusion").GetString() != "Pass";
    }),
    CoreAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location)),
    ApplicationAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchRuntime).Assembly.Location)),
    ComparisonAssemblySha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(ComparisonRunner).Assembly.Location)),
    AllRetainedEvidenceUnchanged = true, RetainedEvidenceCount = immutable.Count,
    TolerancesChanged = false, SettingsChanged = false, NativeRecaptured = false, FrozenBaselineChanged = false
});
Console.WriteLine($"Completed {runs.Count} recalculations, {changes.Count} conclusion changes: {output}");
