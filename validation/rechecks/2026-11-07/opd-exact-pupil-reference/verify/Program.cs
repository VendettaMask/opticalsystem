using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

var root = args[0];
var prior = args[1];
var rows = Read("opd-rows.json").EnumerateArray().ToDictionary(row => Int(row, "id"));
var baseResult = JsonFiles.Read<JsonElement>(Path.Combine(prior, "results.json")).EnumerateArray()
    .Where(row => row.TryGetProperty("index", out _)).ToDictionary(row => Int(row, "index"));
var summary = JsonFiles.Read<JsonElement>(Path.Combine(prior, "summary.json"));
var coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
if (summary.GetProperty("coreSha256").GetString() != coreHash) throw new InvalidDataException("Baseline Core assembly differs");
var previousOpd = Read("explicit-recheck.json").EnumerateArray()
    .Where(row => row.GetProperty("key").GetString() == "Optical Path Difference").ToDictionary(row => Int(row, "index"));
var entry = AnalysisComparisonRegistry.Get("Optical Path Difference");
var reports = new List<object>();
foreach (var id in summary.GetProperty("opdScopeNotPassed").EnumerateArray().Select(x => x.GetInt32()))
{
    var row = rows[id];
    var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
    var configuration = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
    var tolerance = configuration.Quantities["WavefrontError"];
    var result = previousOpd[id];
    if (baseResult[id].GetProperty("physical").GetString() != result.GetProperty("physical").GetString())
        throw new InvalidDataException($"Frozen explicit batch verdict was not reproduced at {id}.");
    var coreFile = At($"core/{id}/data.json");
    var native = At($"frozen/{id}");
    var baselinePath = Path.Combine(native, "data.json");
    var baseline = ResultNormalizer.Zemax(baselinePath, entry, request, coreFile);
    var actual = ResultNormalizer.Workbench(coreFile, entry, request);
    var expanded = new Dictionary<int, NumericResult>();
    var sampleStages = new List<object>();
    var baseCaptured = JsonFiles.Read<JsonElement>(Path.Combine(native, "captured-settings.json"));
    var originalNativeRequest = Path.Combine(native, "request.json");
    foreach (var factor in new[] { 2, 4, 8, 16, 32 }.Where(factor => Directory.Exists(At($"opd-{id}-x{factor}"))))
    {
        var dir = At($"opd-{id}-x{factor}");
        var captured = JsonFiles.Read<JsonElement>(Path.Combine(dir, "captured-settings.json"));
        var originalSettings = JsonNode.Parse(File.ReadAllText(originalNativeRequest))!;
        var denseSettings = JsonNode.Parse(captured.GetProperty("request").GetRawText())!;
        var rayCount = factor * request.RayCount;
        if (denseSettings["rayCount"]!.GetValue<int>() != rayCount || Int(captured.GetProperty("properties"), "NumberOfRays") != rayCount)
            throw new InvalidDataException($"Dense ray count did not read back for {id} x{factor}.");
        denseSettings["rayCount"] = originalSettings["rayCount"]!.DeepClone();
        denseSettings["workbenchSettings"]!["NumberOfRays"] = originalSettings["workbenchSettings"]!["NumberOfRays"]!.DeepClone();
        var denseProperties = JsonNode.Parse(captured.GetProperty("properties").GetRawText())!;
        var originalProperties = JsonNode.Parse(baseCaptured.GetProperty("properties").GetRawText())!;
        denseProperties["NumberOfRays"] = originalProperties["NumberOfRays"]!.DeepClone();
        var baseModel = JsonNode.Parse(File.ReadAllText(Path.Combine(native, "model.json")))!.AsObject();
        var denseModel = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "model.json")))!.AsObject();
        var referenceMode = denseModel["advanced"]!["ReferenceOPD"]!.GetValue<string>();
        if (!baseModel.ContainsKey("advanced")) denseModel.Remove("advanced");
        if (referenceMode != "ExitPupil" || !JsonNode.DeepEquals(originalSettings, denseSettings)
            || !JsonNode.DeepEquals(originalProperties, denseProperties)
            || !JsonElement.DeepEquals(baseCaptured.GetProperty("selectors"), captured.GetProperty("selectors"))
            || !JsonNode.DeepEquals(baseModel, denseModel))
            throw new InvalidDataException($"Different native settings, model or selected reference for {id} x{factor}.");
        var dataFile = Path.Combine(dir, "data.json");
        expanded.Add(factor, ResultNormalizer.Zemax(dataFile, entry, request with { RayCount = rayCount }, coreFile));
        sampleStages.Add(new { factor, rayCount, referenceMode, nativeDataSha256 = Hash(dataFile),
            nativeModelSha256 = Hash(Path.Combine(dir, "model.json")), settingsReadbackSha256 = Hash(Path.Combine(dir, "captured-settings.json")) });
    }
    var axes = new List<object>();
    var failures = new List<object>();
    var pointwiseDeviations = new List<object>();
    var metrics = new List<ComparisonMetric>();
    foreach (var axis in new[] { "tangential", "sagittal" })
    {
        var original = baseline.Series.Single(s => s.Id == axis);
        var workbench = actual.Series.Single(s => s.Id == axis);
        var dense = expanded.ToDictionary(p => p.Key, p => p.Value.Series.Single(s => s.Id == axis));
        var mask = result.GetProperty("masks").EnumerateArray().Single(a => a.GetProperty("id").GetString() == axis);
        var omitted = mask.GetProperty("omitted").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        if (mask.GetProperty("physicalMismatches").GetArrayLength() != 0 || mask.GetProperty("extra").GetArrayLength() != 0
            || !mask.GetProperty("allOmissionsAtSegmentStart").GetBoolean() || original.X.Length != workbench.X.Length
            || !workbench.X.Zip(original.X).All(x => CloseCoordinate(x.First, x.Second)))
            throw new InvalidDataException($"Physical pupil identity was not proven for {id} {axis}.");
        var matched = new List<(double X, double Core, double Reference)>();
        var added = new List<object>();
        var repeatedMax = 0d;
        foreach (var i in Enumerable.Range(0, original.X.Length))
        {
            var x = original.X[i];
            var core = workbench.Y[i];
            var frozen = original.Y[i];
            var values = dense.Select(p => {
                var j = i * p.Key;
                if (p.Value.X.Length != p.Key * (original.X.Length - 1) + 1 || !CloseCoordinate(p.Value.X[j], x))
                    throw new InvalidDataException($"Non-nested pupil grid for {id} {axis} x{p.Key}.");
                return (factor: p.Key, x: p.Value.X[j], value: p.Value.Y[j]);
            }).Where(p => double.IsFinite(p.value)).ToArray();
            if (double.IsFinite(frozen))
            {
                matched.Add((x, core, frozen));
                if (values.Length != dense.Count) failures.Add(new { axis, x, issue = "A previously reported physical node was omitted by a denser capture" });
                foreach (var p in values)
                {
                    var change = Math.Abs(frozen - p.value);
                    repeatedMax = Math.Max(repeatedMax, change);
                    if (!WithinTolerance(p.value, frozen)) failures.Add(new { axis, x, p.factor, change, issue = "Repeated frozen node differs" });
                }
                continue;
            }
            if (!omitted.Contains(i))
            {
                if (values.Length != 0) failures.Add(new { axis, x, issue = "Dense plot reported a physically clipped old node" });
                continue;
            }
            if (values.Length < 2)
            {
                failures.Add(new { axis, x, issue = "Insufficient independent exact-pupil readbacks", readbacks = values });
                continue;
            }
            var reference = values[^1];
            var converged = values.All(p => WithinTolerance(p.value, reference.value));
            var addedNodePass = WithinTolerance(core, reference.value);
            added.Add(new { knot = i, x, core, readbacks = values.Select(p => new { p.factor, p.x, p.value }),
                absoluteErrorWaves = Math.Abs(core - reference.value), converged, addedNodePass });
            if (!converged) failures.Add(new { axis, x, issue = "Repeated native readbacks did not converge" });
            if (!addedNodePass) pointwiseDeviations.Add(new { axis, x, core, native = reference.value,
                absoluteErrorWaves = Math.Abs(core - reference.value) });
            matched.Add((x, core, reference.value));
        }
        var physicalNodes = workbench.Y.Count(double.IsFinite);
        var metric = ComparisonMetrics.Calculate(axis, "Wave", new MatchedValues(matched.Select(p => p.X).ToArray(), null,
            matched.Select(p => p.Core).ToArray(), matched.Select(p => p.Reference).ToArray(), (double)matched.Count / physicalNodes), tolerance);
        metrics.Add(metric);
        axes.Add(new { axis, physicalNodes, originalReported = original.Y.Count(double.IsFinite), recovered = added.Count, repeatedMax, added });
    }
    var originalWave = row.GetProperty("wavelengthIdentity");
    var conclusion = failures.Count == 0 && metrics.All(m => m.Coverage == 1 && m.Conclusion == Conclusion.Pass)
        ? "Pass" : "NeedsReview";
    reports.Add(new { id, lens = row.GetProperty("lens").GetString(), request.Configuration, request.Field, request.Wavelength,
        primaryWavelength = Int(originalWave, "primary"), originalWave, rawCurrentAndChief = result.GetProperty("physical"),
        rawBatchUse = "Physical mask and separate OPD diagnostic. A non-primary CurrentAndChief difference is insufficient to establish a monochromatic plot implementation error.",
        originalReportedKnots = result.GetProperty("reportedKnots"), originalNativeSha256 = Hash(baselinePath),
        coreDataSha256 = Hash(coreFile), sampleStages, conclusion, failures, metrics, axes,
        addedPointwiseConclusion = pointwiseDeviations.Count == 0 ? "Pass" : "Difference", pointwiseDeviations });
    Console.WriteLine($"{id} {conclusion} coverageFailures={failures.Count} addedPointwiseDeviations={pointwiseDeviations.Count}");
    bool WithinTolerance(double a, double b) => Math.Abs(a - b) <= tolerance.Absolute + tolerance.Relative * Math.Abs(b);
}
JsonFiles.Write(At("dense-results.json"), new { coreSha256 = coreHash, scope = "Original physical pupil lattice; immutable reported values and separately recorded exact nested sample readbacks.", reports });
SpectrometerControl.Run(root, rows);

string At(string relative) => Path.Combine(root, relative);
JsonElement Read(string relative) => JsonFiles.Read<JsonElement>(At(relative));
static int Int(JsonElement element, string key) => element.GetProperty(key).GetInt32();
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static bool CloseCoordinate(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-12;
