using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class FocalGuards
{
    internal static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string baseline = "artifacts/zemax-sequential-corpus/20261021/full-v1/analysis-results.json";
        const string previous1142 = "artifacts/validation/remaining-lenses-20261021/request-integrity-summary.json";
        const string objectives = "artifacts/validation/objectives-repair-20261023/final-frozen/summary.json";
        const string policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policyHash = JsonFiles.Hash(File.ReadAllBytes(policyPath));
        if (policyHash != "30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5")
            throw new InvalidDataException("Frozen focal policy changed.");
        var policy = ComparisonConfiguration.Load(policyPath);
        var protectedKeys = JsonFiles.Read<JsonElement>(previous1142).GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("key").GetString()!).ToHashSet();
        var newer = JsonFiles.Read<JsonElement>(objectives).EnumerateArray()
            .Where(r => r.GetProperty("conclusion").GetString() == "Pass")
            .ToDictionary(r => r.GetProperty("key").GetString()!);
        protectedKeys.UnionWith(newer.Keys);
        var rows = new List<object>();
        var found = new HashSet<string>();
        foreach (var item in JsonFiles.Read<JsonElement>(baseline).EnumerateArray())
        {
            var original = item.GetProperty("run");
            if (original.GetProperty("request").ValueKind != JsonValueKind.Object) continue;
            var request = original.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var lens = item.GetProperty("lens").GetString()!;
            var key = $"{lens}|{request.Configuration}|{item.GetProperty("scope").GetString()}|{request.CanonicalAnalysisKey}|{request.Field}|{request.Wavelength}|{request.PupilSampling}";
            if (!protectedKeys.Contains(key)) continue;
            found.Add(key);
            var source = Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
            var optic = WorkbenchExecutor.Import(source).GetAwaiter().GetResult().Configurations[request.Configuration - 1];
            if (optic.ImageSpaceAfocal) continue; // Afocal is separately recaptured, never counted as a focal guard.
            var folder = Path.Combine(output, original.GetProperty("directory").GetString()!);
            Directory.CreateDirectory(folder);
            var referencePath = newer.TryGetValue(key, out var latest) ? latest.GetProperty("referencePath").GetString()!
                : Path.Combine(item.GetProperty("path").GetString()!, "native/data.json");
            var referenceHash = JsonFiles.Hash(File.ReadAllBytes(referencePath));
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = Path.GetFileName(folder), Request = request };
            string? error = null;
            try
            {
                var dataPath = Path.Combine(folder, "data.json");
                JsonFiles.Write(dataPath, new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings));
                var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey],
                    ResultNormalizer.Workbench(dataPath, entry, request), ResultNormalizer.Zemax(referencePath, entry, request), folder);
            }
            catch (Exception e) { error = e.ToString(); run.Conclusion = Conclusion.Error; run.Reason = e.Message; }
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            rows.Add(new { key, previous = "Pass", conclusion = run.Conclusion.ToString(), requestFingerprint = request.Fingerprint,
                requestExactlyEqual = JsonFiles.CanonicalHash(original.GetProperty("request")) == request.Fingerprint,
                sourceSha256 = JsonFiles.Hash(File.ReadAllBytes(source)), referencePath, referenceHash,
                referenceUnchanged = referenceHash == JsonFiles.Hash(File.ReadAllBytes(referencePath)), metrics = run.Metrics, error });
            Console.WriteLine($"{rows.Count}: {key}: {run.Conclusion}");
        }
        if (!protectedKeys.SetEquals(found)) throw new InvalidDataException("A protected original request was not found.");
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { policyHash, baseline, previous1142, objectives,
            softwareRecomputed = true, afocalExcluded = true, originalKeysFound = found.Count, focalRequests = rows.Count,
            coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location)) });
    }
}
