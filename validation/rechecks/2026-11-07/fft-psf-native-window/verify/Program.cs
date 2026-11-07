using System.Text.Json;
using System.Text.Json.Nodes;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

var root = args[0];
var rows = new[] { "rows.json", "rows-remaining.json" }.Select(At).Where(File.Exists)
    .SelectMany(file => Read(file).EnumerateArray()).OrderBy(row => row.GetProperty("id").GetInt32()).ToArray();
var entry = AnalysisComparisonRegistry.Get("PSF");
var reports = new List<object>();
foreach (var row in rows)
{
    var id = row.GetProperty("id").GetInt32();
    var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
    var settings = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
    var tolerance = settings.Quantities["Irradiance"];
    var corePath = At($"core/{id}/data.json");
    var core = File.Exists(corePath) ? Read(corePath) : default;
    var coreLog = At($"core/{id}.log");
    var coreOutcome = core.ValueKind != JsonValueKind.Undefined ? core.GetProperty("outcome").GetString()
        : File.Exists(coreLog) ? "NoData" : "NotEvaluated";
    var coreReason = core.ValueKind != JsonValueKind.Undefined ? core.GetProperty("outcomeReason").GetString()
        : File.Exists(coreLog) ? FirstLogLine(coreLog) : "Adaptive Core has not been run for this native result.";
    var coreSampling = 0;
    JsonElement? certificate = null;
    if (core.ValueKind != JsonValueKind.Undefined && core.GetProperty("values").TryGetProperty("FourierSamplingValidation", out var value))
    {
        certificate = value;
        if (value.GetProperty("validated").GetBoolean()) coreSampling = value.GetProperty("returnedPupilSampling").GetInt32();
    }
    object? coreRefinement = null;
    var refinedPath = At($"core/{id}-refined/data.json");
    if (File.Exists(refinedPath))
    {
        var refined = Read(refinedPath);
        var validation = refined.GetProperty("values").GetProperty("FourierSamplingValidation");
        coreRefinement = new { originalCoreDataSha256 = Hash(corePath), originalCoreOutcome = coreOutcome, originalCoreReason = coreReason,
            outcome = refined.GetProperty("outcome").GetString(), reason = refined.GetProperty("outcomeReason").GetString(),
            dataSha256 = Hash(refinedPath), certificate = validation };
        if (coreSampling == 0 && validation.GetProperty("validated").GetBoolean())
        {
            coreSampling = validation.GetProperty("returnedPupilSampling").GetInt32();
            coreOutcome = refined.GetProperty("outcome").GetString();
            coreReason = refined.GetProperty("outcomeReason").GetString();
            corePath = refinedPath;
        }
    }
    var identity = At($"frozen/{id}");
    var captures = new List<Stage>();
    var measurements = new List<(int Pupil, Grid2DResult Grid)>();
    foreach (var n in new[] { 256, 512, 1024 }.Where(n => File.Exists(At($"{id}-{n}/batch-result.json"))))
    {
        var directory = At($"{id}-{n}");
        try
        {
            var dataPath = Path.Combine(directory, "data.json");
            var exit = Read(Path.Combine(directory, "batch-result.json")).GetProperty("exitCode").GetInt32();
            if (exit != 0) throw new InvalidDataException($"Native capture returned {exit}.");
            var captured = Read(Path.Combine(directory, "captured-settings.json"));
            var originalCapture = Read(Path.Combine(identity, "captured-settings.json"));
            var requestReadback = Node(captured.GetProperty("request"));
            var originalNative = JsonNode.Parse(File.ReadAllText(Path.Combine(identity, "request.json")))!;
            var properties = Node(captured.GetProperty("properties"));
            var originalProperties = Node(originalCapture.GetProperty("properties"));
            if (requestReadback["pupilSampling"]!.GetValue<int>() != n || properties["SampleSize"]!.GetValue<string>() != $"PsfS_{n}x{n}")
                throw new InvalidDataException("Native requested pupil sampling did not read back.");
            requestReadback["pupilSampling"] = originalNative["pupilSampling"]!.DeepClone();
            properties["SampleSize"] = originalProperties["SampleSize"]!.DeepClone();
            var originalModel = JsonNode.Parse(File.ReadAllText(Path.Combine(identity, "model.json")))!.AsObject();
            var modelPath = Path.Combine(directory, "model.json");
            var model = JsonNode.Parse(File.ReadAllText(modelPath))!.AsObject();
            if (!originalModel.ContainsKey("advanced")) model.Remove("advanced");
            if (!JsonNode.DeepEquals(requestReadback, originalNative) || !JsonNode.DeepEquals(properties, originalProperties)
                || !JsonNode.DeepEquals(originalModel, model) || !JsonElement.DeepEquals(captured.GetProperty("selectors"), originalCapture.GetProperty("selectors")))
                throw new InvalidDataException("Native settings or model differ beyond the pupil sampling change.");
            var diagnostics = Read(Path.Combine(directory, "result-diagnostics.json"));
            if (diagnostics.GetProperty("messages").GetArrayLength() > 0 || diagnostics.GetProperty("gridCount").GetInt32() != 1)
                throw new InvalidDataException(diagnostics.GetRawText());
            var grid = ResultNormalizer.Zemax(dataPath, entry, request with { PupilSampling = n }).Grids.Single();
            if (grid.X.Length != request.ImageSampling || grid.Y.Length != request.ImageSampling
                || grid.X.Zip(grid.X.Skip(1)).Concat(grid.Y.Zip(grid.Y.Skip(1)))
                    .Any(p => Math.Abs(p.Second - p.First - request.ImageDeltaMicrometers) > 1e-12))
                throw new InvalidDataException("Actual native image grid size or pitch differs.");
            captures.Add(new(n, "Valid", Hash(dataPath), Hash(modelPath), Hash(Path.Combine(directory, "captured-settings.json")),
                [grid.X[0], grid.X[^1], grid.Y[0], grid.Y[^1]]));
            measurements.Add((n, grid));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or InvalidDataException)
        { captures.Add(new(n, error.Message, null, null, null, null)); }
    }
    ComparisonMetric? nativeConvergence = null;
    ComparisonMetric? cross = null;
    string? comparisonIssue = null;
    try
    {
        if (measurements.Count >= 2)
        {
            var a = measurements[^2].Grid;
            var b = measurements[^1].Grid;
            nativeConvergence = Compare(a, b);
        }
        if (coreSampling > 0 && measurements.Count > 0)
        {
            var grid = ResultNormalizer.Workbench(corePath, entry, request with { PupilSampling = coreSampling }).Grids.Single();
            cross = Compare(grid, measurements[^1].Grid);
        }
    }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { comparisonIssue = error.Message; }
    var outcome = nativeConvergence is { Conclusion: Conclusion.Pass, Coverage: 1 } && cross is { Conclusion: Conclusion.Pass, Coverage: 1 }
        ? "SupplementalPass" : "Open";
    var evidenceStatus = outcome == "SupplementalPass" ? "IndependentNumericalPass" : measurements.Count switch
    {
        0 => "NativeNoValidGrid",
        1 => "NativeSingleValidGrid",
        _ => nativeConvergence?.Conclusion switch
        {
            Conclusion.Pass => "CoreUnresolvedAfterNativeConvergence",
            Conclusion.Close => "NativeClose",
            Conclusion.Difference => "NativeDifference",
            _ => "NativeScopeUnresolved"
        }
    };
    reports.Add(new { id, row, coreOutcome, coreReason, coreSampling, coreDataSha256 = File.Exists(corePath) ? Hash(corePath) : null,
        certificate, coreRefinement, captures, nativeConvergence, cross, comparisonIssue, outcome, evidenceStatus });
    Console.WriteLine($"#{id} native={nativeConvergence?.Conclusion} core={coreOutcome}/{coreSampling} cross={cross?.Conclusion} scope={outcome}");
    ComparisonMetric Compare(Grid2DResult a, Grid2DResult b)
    {
        var aligned = ComparisonMetrics.Align(a, b, []);
        return ComparisonMetrics.Calculate("grid", b.ValueAxis.Unit, aligned, tolerance);
    }
}
JsonFiles.Write(At("psf-results.json"), new { coreSha256 = Hash(typeof(Optic).Assembly.Location), reports });
string At(string file) => Path.Combine(root, file);
static JsonElement Read(string file) => JsonFiles.Read<JsonElement>(file);
static string Hash(string file) => JsonFiles.Hash(File.ReadAllBytes(file));
static JsonNode Node(JsonElement value) => JsonNode.Parse(value.GetRawText())!;
static string? FirstLogLine(string path)
{
    try { return File.ReadLines(path).FirstOrDefault(); }
    catch (IOException) { return "Adaptive Core is still writing its execution log."; }
}
sealed record Stage(int Pupil, string Status, string? DataSha256, string? ModelSha256, string? ReadbackSha256, double[]? Domain);
