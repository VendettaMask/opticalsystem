using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class ObjectivesRepair
{
    internal static void SamplingControls(string output, string nativeRoot)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(nativeRoot, "request-manifest.json")));
        var rows = new List<object>();
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var nativeDirectory = item.GetProperty("output").GetString()!;
            var path = item.GetProperty("request").GetString()!;
            var request = JsonFiles.Read<CanonicalAnalysisRequest>(path);
            using var raw = JsonDocument.Parse(File.ReadAllText(path));
            var optic = WorkbenchExecutor.Import(raw.RootElement.GetProperty("input").GetString()!).GetAwaiter().GetResult().Configurations[request.Configuration - 1];
            var name = Path.GetFileName(nativeDirectory); var folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = name, Request = request };
            string? error = null;
            try
            {
                var dataPath = Path.Combine(folder, "data.json");
                JsonFiles.Write(dataPath, new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings));
                var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
                var a = ResultNormalizer.Workbench(dataPath, entry, request);
                var b = ResultNormalizer.Zemax(Path.Combine(nativeDirectory, "data.json"), entry, request);
                run.Normalizations.AddRange(a.Transformations.Concat(b.Transformations));
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], a, b, folder);
            }
            catch (Exception e)
            {
                error = e.ToString(); run.Conclusion = e is InvalidDataException or AnalysisDataUnavailableException ? Conclusion.Incomparable : Conclusion.Error;
                run.Reason = e.Message; File.WriteAllText(Path.Combine(folder, "error.txt"), error);
            }
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            rows.Add(new { originalKey = item.GetProperty("originalKey").GetString(), requestFingerprint = request.Fingerprint,
                request.PupilSampling, request.ImageSampling, conclusion = run.Conclusion, run.Reason, metrics = run.Metrics, error });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine($"{rows.Count}: {name}: {run.Conclusion}");
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { nativeRoot, softwareRecomputed = true,
            coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location)),
            policySha256 = JsonFiles.Hash(File.ReadAllBytes(policyPath)) });
    }

    // Reuse is an explicit offline adapter diagnostic. A final Core replay must
    // omit cachedData, import every original lens and calculate fresh data.
    internal static void Run(string output, string? cachedData, string? nativeRoot)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required");
        Directory.CreateDirectory(output);
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        using var all = JsonDocument.Parse(File.ReadAllText(WideSchmidtRepair.Baseline + "/analysis-results.json"));
        var native = new Dictionary<string, string>();
        if (nativeRoot is not null)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(nativeRoot, "request-manifest.json")));
            foreach (var row in manifest.RootElement.EnumerateArray())
                native.Add(row.GetProperty("key").GetString()!, row.GetProperty("output").GetString()!);
        }
        var rows = new List<object>();
        foreach (var item in all.RootElement.EnumerateArray())
        {
            var lens = item.GetProperty("lens").GetString()!;
            if (!lens.StartsWith("Objectives/", StringComparison.Ordinal)) continue;
            var original = item.GetProperty("run");
            var request = original.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var key = $"{lens}|{request.Configuration}|{item.GetProperty("scope").GetString()}|{request.CanonicalAnalysisKey}|{request.Field}|{request.Wavelength}|{request.PupilSampling}";
            if (cachedData is not null && nativeRoot is not null && !native.ContainsKey(key)) continue;
            var name = original.GetProperty("directory").GetString()!;
            var folder = Path.Combine(output, name); Directory.CreateDirectory(folder);
            var dataPath = Path.Combine(folder, "data.json");
            var referencePath = Path.Combine(native.GetValueOrDefault(key) ?? Path.Combine(item.GetProperty("path").GetString()!, "native"), "data.json");
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = name, Request = request };
            string? error = null;
            try
            {
                if (cachedData is not null)
                    File.Copy(Path.Combine(cachedData, name, "data.json"), dataPath);
                else
                {
                    var optic = WorkbenchExecutor.Import(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens))
                        .GetAwaiter().GetResult().Configurations[request.Configuration - 1];
                    JsonFiles.Write(dataPath, new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings));
                }
                var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
                var actual = ResultNormalizer.Workbench(dataPath, entry, request);
                var reference = ResultNormalizer.Zemax(referencePath, entry, request);
                run.Normalizations.AddRange(actual.Transformations.Concat(reference.Transformations));
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, reference, folder);
            }
            catch (Exception e)
            {
                error = e.ToString();
                File.WriteAllText(Path.Combine(folder, "error.txt"), error);
                var nativeError = Path.Combine(Path.GetDirectoryName(referencePath)!, "error.json");
                var invalidNative = File.Exists(nativeError) && File.ReadAllText(nativeError).Contains("NativeInvalidResult", StringComparison.Ordinal);
                run.Conclusion = e is AnalysisDataUnavailableException || File.Exists(dataPath) && (e is InvalidDataException || invalidNative)
                    ? Conclusion.Incomparable : Conclusion.Error;
                run.Reason = e.Message;
            }
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            rows.Add(new { key, conclusion = run.Conclusion, run.Reason, metrics = run.Metrics, error,
                dataSha256 = File.Exists(dataPath) ? JsonFiles.Hash(File.ReadAllBytes(dataPath)) : null,
                referencePath, referenceSha256 = File.Exists(referencePath) ? JsonFiles.Hash(File.ReadAllBytes(referencePath)) : null });
            JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
            Console.WriteLine($"{rows.Count}: {key}: {run.Conclusion}");
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { cachedData, nativeRoot,
            coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location)),
            policySha256 = JsonFiles.Hash(File.ReadAllBytes(policyPath)) });
    }
}
