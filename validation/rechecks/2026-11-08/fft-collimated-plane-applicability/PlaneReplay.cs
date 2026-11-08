using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var root = Path.GetFullPath(args[0]);
var work = Path.GetFullPath(args[1]);
var rows = JsonFiles.Read<JsonElement[]>(Path.Combine(root, "rows.json"));
var priorResults = JsonFiles.Read<JsonElement[]>(Path.Combine(root, "results.json"));
var findings = new List<object>();
foreach (var index in new[] { 1273, 1276, 2547, 2550, 2205, 2208 })
{
    var row = rows[index - 1];
    var job = JsonFiles.Read<WorkbenchJob>(row.GetProperty("job").GetString()!);
    var output = Path.Combine(work, "replay", index.ToString());
    WorkbenchExecutor.Execute(job with { OutputDirectory = output });
    var path = Path.Combine(output, "data.json");
    var data = JsonFiles.Read<AnalysisData>(path);
    var baseline = Path.Combine(job.OutputDirectory, "data.json");
    var sameWorkbenchPayload = File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(baseline));
    var expected = index == 2205 ? AnalysisOutcome.Success : AnalysisOutcome.NotApplicable;
    if (data.Outcome != expected || sameWorkbenchPayload != (index is 2205 or 2208))
        throw new InvalidDataException($"Unexpected original request outcome or payload change at {index}.");
    if (expected == AnalysisOutcome.NotApplicable &&
        (data.Values[FocalImageDomain.ResultKey].ToString() != FocalImageDomain.CollimatedPlaneTrain || data.PlotSeries.Count != 0))
        throw new InvalidDataException($"Invalid applicability result at {index}.");

    var nativeDirectory = Path.Combine(row.GetProperty("legacyPath").GetString()!, "native");
    var completion = JsonFiles.Read<JsonElement>(Path.Combine(nativeDirectory, "batch-result.json"));
    var nativeData = Path.Combine(nativeDirectory, "data.json");
    var comparison = Conclusion.Incomparable;
    var reason = "Original native capture failed; no valid numeric authority";
    ComparisonMetric[] metrics = [];
    if (completion.GetProperty("exitCode").GetInt32() == 0)
    {
        var entry = AnalysisComparisonRegistry.Get(job.Request.CanonicalAnalysisKey);
        try
        {
            var reference = ResultNormalizer.Zemax(nativeData, entry, job.Request, path);
            if (index != 2205)
                throw new InvalidOperationException("Expected the frozen native applicability diagnostic.");
            var current = ResultNormalizer.Workbench(path, entry, job.Request);
            var config = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
            metrics = reference.Series.Select(curve =>
            {
                var other = current.Series.Single(s => s.Id == curve.Id);
                return ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                    ComparisonMetrics.Align(other, curve, []), config.Quantities[curve.YAxis.Quantity]);
            }).ToArray();
            comparison = metrics.Length > 0 && metrics.All(m => m.Conclusion == Conclusion.Pass) ? Conclusion.Pass : Conclusion.Error;
            reason = "";
        }
        catch (InvalidDataException error) { reason = error.Message; }
    }
    var before = priorResults[index - 1].GetProperty("conclusion").GetString();
    if (comparison.ToString() != before)
        throw new InvalidDataException($"Frozen verdict changed at {index}: {before} -> {comparison}.");

    findings.Add(new { index, job.Request.CanonicalAnalysisKey, requestFingerprint = job.Request.Fingerprint,
        baselineWorkbenchSha256 = Hash(baseline), workbenchSha256 = Hash(path), sameWorkbenchPayload,
        data.Outcome, data.OutcomeReason, priorFrozenVerdict = before, frozenVerdict = comparison,
        nativeDataSha256 = Hash(File.Exists(nativeData) ? nativeData : Path.Combine(nativeDirectory, "error.json")), reason, metrics });
    Console.WriteLine($"{index}: {data.Outcome}, frozen {comparison}, same Workbench payload={sameWorkbenchPayload}");
}
JsonFiles.Write(Path.Combine(work, "entry-point-replay.json"), new
{
    coreSha256 = Hash(typeof(Optic).Assembly.Location),
    requests = findings
});
static string Hash(string path) => JsonFiles.Hash(File.ReadAllBytes(path));
