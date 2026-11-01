using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
if (args.Length == 3 && args[0] == "prepare-current")
{
    CurrentInputs.Prepare(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    return;
}
if (args.Length == 3 && args[0] == "probe-source")
{
    SourceProbe.Run(args[1], args[2]);
    return;
}
if (args.Length != 2) throw new ArgumentException("compute|audit <new run root> or prepare-current <repository> <new run root>");
var root = Path.GetFullPath(args[1]);
var rows = JsonFiles.Read<List<Row>>(Path.Combine(root, "items.json"));
var identity = JsonFiles.Read<JsonElement>(Path.Combine(root, "input-identity.json"));
var coreHash = JsonFiles.Hash(File.ReadAllBytes(typeof(Optic).Assembly.Location));
if (coreHash != identity.GetProperty("expectedCoreSha256").GetString()) throw new InvalidDataException("Final Core identity changed");
if (rows.Count != 170 || rows.Select(r => r.Id).Distinct().Count() != 170) throw new InvalidDataException("Original 150 + fixed 20 identities required");
foreach (var row in rows)
{
    if (row.Request.Fingerprint != row.ExpectedFingerprint) throw new InvalidDataException("Request changed: " + row.Id);
    if (JsonFiles.Hash(File.ReadAllBytes(row.Source)) != row.SourceSha256) throw new InvalidDataException("Source changed: " + row.Id);
}
string Token(Row row) => "c" + (rows.IndexOf(row) + 1).ToString("D4");
string Workbench(Row row) => Path.Combine(root, "workbench", Token(row));
if (args[0] == "compute")
{
    JsonFiles.Write(Path.Combine(root, "runtime-identity.json"), new
    {
        coreSha256 = coreHash, corePath = typeof(Optic).Assembly.Location,
        applicationSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchRuntime).Assembly.Location)),
        comparisonSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchExecutor).Assembly.Location)),
        startedUtc = DateTimeOffset.UtcNow, requestCount = rows.Count
    });
    foreach (var row in rows)
    {
        var directory = Workbench(row);
        if (Directory.Exists(directory)) throw new IOException("Fresh worker output required: " + directory);
        Directory.CreateDirectory(directory);
        JsonFiles.Write(Path.Combine(directory, "request.json"), new WorkbenchJob(row.Snapshot, directory, row.Request));
    }
    var completed = 0;
    var results = new System.Collections.Concurrent.ConcurrentBag<object>();
    await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (row, ct) =>
    {
        var directory = Workbench(row);
        var started = DateTimeOffset.UtcNow;
        var result = await ProcessIsolation.Run("dotnet", [typeof(WorkbenchExecutor).Assembly.Location,
            "--workbench-worker", Path.Combine(directory, "request.json")], Environment.CurrentDirectory, 180, ct);
        File.WriteAllText(Path.Combine(directory, "process.log"), result.StandardOutput + result.StandardError);
        JsonFiles.Write(Path.Combine(directory, "worker-result.json"), result);
        var item = new { row.Id, row.Set, token = Token(row), result.ExitCode, result.TimedOut, result.Cancelled,
            startedUtc = started, completedUtc = DateTimeOffset.UtcNow };
        JsonFiles.Write(Path.Combine(directory, "execution.json"), item);
        results.Add(item);
        Console.WriteLine($"{Interlocked.Increment(ref completed)}/170 {row.Set} {row.Id}: {result.ExitCode}");
    });
    JsonFiles.Write(Path.Combine(root, "worker-results.json"), results);
    return;
}
if (args[0] != "audit") throw new ArgumentException("Unknown mode");
var policy = ComparisonConfiguration.Load(identity.GetProperty("policyPath").GetString()!);
var angular = JsonFiles.Read<Tolerances>(identity.GetProperty("angularPolicyPath").GetString()!);
_ = AnalysisComparisonRegistry.Entries.Count; // Fail once on missing runner assets, before classifying optical cases.
var summary = new List<object>();
foreach (var row in rows)
{
    var before = Evaluate(row, row.BaselineWorkbench, "before");
    var after = Evaluate(row, Path.Combine(Workbench(row), "data.json"), "after");
    var result = new { row.Set, row.Id, row.Lens, row.Configuration, row.Key, outputToken = Token(row),
        requestFingerprint = row.Request.Fingerprint, row.PreviousConclusion, before, after };
    JsonFiles.Write(Path.Combine(root, "cases", Token(row) + ".json"), result);
    summary.Add(result);
    Console.WriteLine($"AUDIT {summary.Count}/170 {row.Id}: {before.Run.Conclusion} -> {after.Run.Conclusion}; pointwise {before.Points.Sum(p => p.Violations)} -> {after.Points.Sum(p => p.Violations)}");
}
JsonFiles.Write(Path.Combine(root, "comparison.json"), summary);
var evaluated = summary.Select(s => JsonSerializer.SerializeToElement(s, JsonFiles.Options)).ToArray();
var covered = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
JsonFiles.Write(Path.Combine(root, "analysis-inventory.json"), new
{
    scope = "Original four afocal files / five configurations / original 150 plus fixed-angle 20 requests",
    coreSha256 = coreHash, allRequiredAnalysisResultsCertified = false,
    analyses = rows.GroupBy(r => r.Key).Select(g => new
    {
        key = g.Key, requests = g.Count(), cases = evaluated.Where(e => e.GetProperty("key").GetString() == g.Key)
            .Select(e => new { id = e.GetProperty("id").GetString(), conclusion = e.GetProperty("after").GetProperty("run").GetProperty("conclusion").GetString(),
                violations = e.GetProperty("after").GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("violations").GetInt32()) }).ToArray()
    }).ToArray(),
    catalogEntriesOutsideThisBatch = new AnalysisCatalog(new Optic()).Names.Where(n => !covered.Contains(n))
        .Select(n => new { key = n, status = "NotValidatedByThisAfocalBatch", applicability = "RequiresSeparateAssessment" }).ToArray()
});

Evaluation Evaluate(Row row, string? path, string phase)
{
    var run = new AnalysisRun { Key = row.Key, Directory = Token(row), Request = row.Request };
    var points = new List<PointAudit>();
    var gaps = new List<object>();
    string? error = null;
    try
    {
        if (path is null || !File.Exists(path)) throw new InvalidDataException("Core calculation unavailable; see worker and original availability records");
        var native = Path.Combine(row.Native, "data.json");
        if (!File.Exists(native) || File.Exists(Path.Combine(row.Native, "error.json")))
            throw new InvalidDataException("Missing valid native reference; existing capture error retained");
        var entry = AnalysisComparisonRegistry.Get(row.Key);
        // The old matrix used explicitly declared afocal per-quantity gates.
        // Restore those exact effective gates, not the generic focal defaults.
        var originalSettings = policy.Analyses[row.Key] with
        {
            Quantities = new(row.ExpectedEffectiveTolerances),
            AfocalQuantities = new(row.ExpectedEffectiveTolerances)
        };
        var decoded = ScopedAfocalNormalizer.Read(path, native, entry, row.Request, originalSettings, angular, row.Source);
        ComparisonRunner.Compare(run, entry, decoded.Settings, decoded.Actual, decoded.Expected,
            Path.Combine(root, phase, Token(row)));
        if (run.Tolerances.Count != row.ExpectedEffectiveTolerances.Count || run.Tolerances.Any(pair =>
            !row.ExpectedEffectiveTolerances.TryGetValue(pair.Key, out var expected) || pair.Value != expected))
            throw new InvalidDataException("Effective per-quantity tolerance changed: " + row.Id);
        var w = decoded.Actual;
        var z = decoded.Expected;
        Tolerances T(string quantity) => run.Tolerances[quantity];
        foreach (var scalar in z.Scalars)
        {
            var actual = w.Scalars.Single(s => s.Id == scalar.Id);
            Add(scalar.Id, "scalar", scalar.Id, scalar.Unit,
                new MatchedValues([0], null, [actual.Value * PhysicalNormalization.UnitScale(actual.Unit, scalar.Unit)], [scalar.Value], 1), T(scalar.Id));
        }
        foreach (var curve in z.Series)
        {
            try { Add(curve.Id, "series", curve.YAxis.Quantity, curve.YAxis.Unit,
                ComparisonMetrics.Align(w.Series.Single(s => s.Id == curve.Id), curve, []), T(curve.YAxis.Quantity)); }
            catch (InvalidDataException ex) { gaps.Add(new { kind = "series", curve.Id, curve.YAxis.Quantity, reason = ex.Message }); }
        }
        foreach (var grid in z.Grids)
        {
            var actual = w.Grids.Single(s => s.Id == grid.Id);
            try { Add(grid.Id, "grid", grid.ValueAxis.Quantity, grid.ValueAxis.Unit,
                ComparisonMetrics.Align(actual, grid, []), T(grid.ValueAxis.Quantity)); }
            catch (InvalidDataException ex)
            {
                double AxisError(double[] a, double[] b) => a.Length == b.Length ? a.Zip(b, (x, y) => Math.Abs(x - y)).Max() : double.PositiveInfinity;
                gaps.Add(new { kind = "grid", grid.Id, grid.ValueAxis.Quantity, reason = ex.Message,
                    maximumXAxisDifference = AxisError(actual.X, grid.X), maximumYAxisDifference = AxisError(actual.Y, grid.Y),
                    actualXCount = actual.X.Length, actualYCount = actual.Y.Length, expectedXCount = grid.X.Length, expectedYCount = grid.Y.Length });
            }
        }
    }
    catch (Exception ex)
    {
        error = ex.ToString();
        run.Conclusion = Conclusion.Incomparable;
        run.Reason = ex.Message;
    }
    JsonFiles.Write(Path.Combine(root, phase, Token(row), "comparison.json"), run);
    return new(run, points, gaps, error);

    void Add(string id, string kind, string quantity, string unit, MatchedValues values, Tolerances tolerance)
    {
        var errors = values.Workbench.Zip(values.Zemax, (a, b) => Math.Abs(a - b)).ToArray();
        var limits = values.Zemax.Select(b => tolerance.Absolute + tolerance.Relative * Math.Abs(b)).ToArray();
        var bad = Enumerable.Range(0, errors.Length).Where(i => errors[i] > limits[i]).ToArray();
        var worst = Enumerable.Range(0, errors.Length).MaxBy(i => errors[i] - limits[i]);
        var metric = run.Metrics.Single(m => m.Id == id);
        if (metric.Count != errors.Length || metric.MaxAbsolute != errors.Max()) throw new InvalidDataException("Point audit differs from formal metric");
        points.Add(new(id, kind, quantity, unit, tolerance, errors.Length, values.Coverage, bad.Length,
            errors.Max(), Math.Max(0, errors[worst] - limits[worst]), values.X[worst], values.Y?[worst],
            values.Workbench[worst], values.Zemax[worst], limits[worst], metric.Conclusion.ToString(),
            metric.Conclusion == Conclusion.Pass && bad.Length > 0));
    }
}

internal sealed record Row(string Set, string Id, string Lens, int Configuration, string Key,
    string Snapshot, string Native, string Source, string SourceSha256, string? BaselineWorkbench,
    string PreviousConclusion, string ExpectedFingerprint, Dictionary<string, Tolerances> ExpectedEffectiveTolerances,
    CanonicalAnalysisRequest Request);
internal sealed record Evaluation(AnalysisRun Run, List<PointAudit> Points, List<object> Gaps, string? Error);
internal sealed record PointAudit(string Id, string Kind, string Quantity, string Unit, Tolerances Gate,
    int Count, double Coverage, int Violations, double MaxAbsolute, double MaximumGateExcess,
    double WorstExcessX, double? WorstExcessY, double ActualAtWorstExcess, double NativeAtWorstExcess,
    double LimitAtWorstExcess, string FormalConclusion, bool AcceptedOnlyByNrmse);
