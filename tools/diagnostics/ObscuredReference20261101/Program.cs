using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

if (args.Length != 3) throw new ArgumentException("regressions|categories <repository> <fresh-output>");
var repository = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("A fresh output directory is required.");
Directory.CreateDirectory(output);
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
var corePath = typeof(Optic).Assembly.Location;
var coreHash = Hash(corePath);
if (args[0] == "testing-probe") { await TestingPropagationProbe.Run(repository, output, coreHash); return; }
if (args[0] == "testing-native") { await TestingNativeRays.Run(repository, output, coreHash); return; }
if (args[0] == "testing-rays") { await TestingRayControls.Run(repository, output, coreHash); return; }
if (args[0] == "testing-convergence") { await TestingConvergence.Run(repository, output, coreHash); return; }
if (args[0] == "testing-convergence-final") { await TestingConvergence.Run(repository, output, coreHash, 256); return; }
if (args[0] == "testing-phase") { await TestingPhaseControls.Run(repository, output, coreHash); return; }
if (args[0] == "energy-convergence") { await EnergyConvergence.Run(repository, output, coreHash); return; }
if (args[0] == "rays") { await ObscuredRayControls.Run(repository, output, coreHash); return; }
var baseline = Path.Combine(repository, "artifacts/validation/all-example-accuracy-20261031");
var rows = JsonFiles.Read<ReplayRow[]>(Path.Combine(baseline, "rows.json"));
var apertureLenses = rows.Select(r => r.Lens).Distinct().Where(lens =>
    File.ReadLines(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens)).Any(line =>
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 3 && (tokens[0] == "SQAP" || tokens[0] == "OBDC"
            && (double.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture) != 0
                || double.Parse(tokens[2], System.Globalization.CultureInfo.InvariantCulture) != 0));
    })).ToHashSet();
bool HasApertureRecords(string lens) => apertureLenses.Contains(lens);
var audit = JsonFiles.Read<JsonElement>(Path.Combine(repository,
    "validation/rechecks/2026-10-31/all-example-accuracy/prior-pass-audit.json"));
var regressionIds = audit.GetProperty("cases").EnumerateArray().Select(r => r.GetProperty("index").GetInt32()).ToHashSet();
var selected = rows.Where(r => r.Run.Request is not null && (args[0] == "regressions"
    ? regressionIds.Contains(r.Index)
    : args[0] == "categories" ? r.Lens.StartsWith("Telescopes/") || r.Lens.StartsWith("Optical testing/")
    : args[0] == "testing-three" ? TestingPropagationProbe.Lenses.Contains(r.Lens)
    : args[0] == "apertures" ? HasApertureRecords(r.Lens)
    : args[0] == "crystals" ? r.Lens.StartsWith("Birefringent prisms/")
    : args[0] == "toroid-annular" ? r.Lens is "Miscellaneous/Toroid.zmx" or "Objectives/Gaussian Quadrature over Annular Pupils.zmx"
    : throw new ArgumentException("Unknown selection"))).ToArray();
var snapshots = new Dictionary<(string, int), string>();
var results = new List<object>();
foreach (var row in selected)
{
    if (Hash(corePath) != coreHash) throw new InvalidDataException("Execution Core changed.");
    if (!snapshots.TryGetValue((row.Lens, row.Configuration), out var snapshot))
    {
        var source = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", row.Lens);
        var imported = await WorkbenchExecutor.Import(source);
        snapshot = Path.Combine(output, "models", $"{row.LensIndex:D3}-c{row.Configuration}.optic.json");
        JsonFiles.Write(snapshot, imported.Configurations[row.Configuration - 1].ToSnapshot());
        snapshots[(row.Lens, row.Configuration)] = snapshot;
    }
    var folder = Path.Combine(output, "analyses", row.Index.ToString("D4"));
    Directory.CreateDirectory(folder);
    var originalRequest = row.Run.Request!;
    var surfaceCount = JsonFiles.Read<JsonElement>(snapshot).GetProperty("surfaces").GetArrayLength();
    if (originalRequest.Surface >= 0 && surfaceCount != originalRequest.SurfaceCount)
        throw new InvalidDataException("An explicit historical surface needs an independent source mapping.");
    var job = new WorkbenchJob(snapshot, folder, originalRequest with { SurfaceCount = surfaceCount });
    JsonFiles.Write(Path.Combine(folder, "request.json"), job);
    var timer = Stopwatch.StartNew();
    string? error = null;
    var run = new AnalysisRun { Key = row.Key, Directory = folder, Request = job.Request,
        Support = row.Run.Support, Tolerances = row.Run.Tolerances };
    var native = Path.Combine(row.LegacyPath, "native");
    try
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(900));
        using var cancellation = ComputationCancellation.Push(deadline.Token);
        WorkbenchExecutor.Execute(job);
        if (JsonFiles.Read<JsonElement>(Path.Combine(native, "batch-result.json")).GetProperty("exitCode").GetInt32() != 0)
            throw new InvalidDataException("Original native capture failed.");
        var entry = AnalysisComparisonRegistry.Get(row.Key);
        var z = ResultNormalizer.Zemax(Path.Combine(native, "data.json"), entry, job.Request);
        var raw = JsonFiles.Read<JsonElement>(Path.Combine(folder, "data.json"));
        if (raw.TryGetProperty("outcome", out var outcome) && outcome.GetString() is "Unavailable" or "NotApplicable")
        {
            run.Conclusion = z.Scalars.Count + z.Series.Count + z.Grids.Count > 0 ? Conclusion.Error : Conclusion.Incomparable;
            run.Reason = "Core returned " + outcome.GetString();
            error = raw.TryGetProperty("outcomeReason", out var why) ? why.GetString() : null;
        }
        else
        {
            var w = ResultNormalizer.Workbench(Path.Combine(folder, "data.json"), entry, job.Request);
            ComparisonRunner.Compare(run, entry, row.Settings, w, z, folder);
        }
    }
    catch (InvalidDataException e) { run.Conclusion = Conclusion.Incomparable; run.Reason = e.Message; error = e.ToString(); }
    catch (Exception e) { run.Conclusion = Conclusion.Error; run.Reason = e.Message; error = e.ToString(); }
    JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
    var record = new { row.Index, row.Lens, row.Configuration, row.Key, run.Conclusion, run.Reason, error,
        run.Metrics, seconds = timer.Elapsed.TotalSeconds, requestFingerprint = job.Request.Fingerprint,
        originalRequestFingerprint = originalRequest.Fingerprint, originalSurfaceCount = originalRequest.SurfaceCount, effectiveSurfaceCount = surfaceCount,
        nativeReference = native, nativeDataSha256 = File.Exists(Path.Combine(native, "data.json")) ? Hash(Path.Combine(native, "data.json")) : null,
        coreSha256 = coreHash, historicalRegression = regressionIds.Contains(row.Index), referenceConvergence = "NotEstablishedByThisReplay" };
    results.Add(record);
    JsonFiles.Write(Path.Combine(output, "results.json"), results);
    Console.WriteLine($"{results.Count}/{selected.Length} ROW {row.Index} {row.Lens} {row.Key}: {run.Conclusion} ({timer.Elapsed.TotalSeconds:F2}s)");
}
if (Hash(corePath) != coreHash) throw new InvalidDataException("Execution Core changed.");
JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreSha256 = coreHash, baseline,
    baselineRowsSha256 = Hash(Path.Combine(baseline, "rows.json")),
    selection = args[0], selected = selected.Length, completed = results.Count,
    inputs = selected.Select(r => r.Lens).Distinct().Select(lens => new { lens,
        sha256 = Hash(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens)) }),
    completedUtc = DateTimeOffset.UtcNow, freshSourceImport = true, nativeExecuted = false,
    originalSettingsAndTolerances = true });

internal sealed record ReplayRow(int Index, int LensIndex, string Lens, int Configuration, string Key,
    string LegacyPath, AnalysisConfiguration Settings, AnalysisRun Run);
