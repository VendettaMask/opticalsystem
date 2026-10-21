using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

// Formal application recalculation against the unchanged policy. Repeated native
// captures are controls; frozen requests and native references are never edited.
internal static class Energy32Recheck
{
    public static void RunPerturbed(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-continuation-20261021";
        using var recipe = JsonDocument.Parse(File.ReadAllText(root + "/holdout-request.json"));
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        var rows = new List<object>();
        foreach (var job in recipe.RootElement.GetProperty("jobs").EnumerateArray().Where(j => j.TryGetProperty("analysisRequest", out _)))
        {
            var id = job.GetProperty("id").GetString()!; var lens = job.GetProperty("lens").GetString()!;
            var request = job.GetProperty("analysisRequest").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var source = File.ReadAllText(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens));
            var scale = job.GetProperty("apertureScale").GetDouble();
            source = System.Text.RegularExpressions.Regex.Replace(source, @"(?m)^ENPD ([^\r\n ]+)", m => "ENPD "
                + (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * scale).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var optic = OpticalFormatCatalog.Import(source, ".zmx");
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
            var folder = Path.Combine(output, id); Directory.CreateDirectory(folder);
            var dataPath = Path.Combine(folder, "data.json"); JsonFiles.Write(dataPath, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var actual = ResultNormalizer.Workbench(dataPath, entry, request);
            var native = ResultNormalizer.Zemax(root + "/native-holdout/capture/" + id + "/data.json", entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, folder);
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            rows.Add(new { id, lens, scale, run.Conclusion, metrics = run.Metrics });
            Console.WriteLine($"{id}: {run.Conclusion}");
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
    }

    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        const string root = "artifacts/validation/objectives32-repair-20261021";
        using var controls = JsonDocument.Parse(File.ReadAllText(root + "/coordinate-audit-v7/inputs.json"));
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        var rows = new List<object>();
        var repeats = new List<object>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-').ToLowerInvariant();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var source = Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
            var folder = Path.Combine(output, id);
            Directory.CreateDirectory(folder);
            var dataPath = Path.Combine(folder, "data.json");
            JsonFiles.Write(dataPath, data);
            var actual = ResultNormalizer.Workbench(dataPath, entry, request);
            var originalPath = Path.Combine(item.GetProperty("path").GetString()!, "native/data.json");
            var original = ResultNormalizer.Zemax(originalPath, entry, request);
            var repeatedPath = Path.Combine(root, "native-original32-repeat/capture", id, "data.json");
            foreach (var (capture, native) in File.Exists(repeatedPath)
                ? new[] { ("frozen", original), ("repeat", ResultNormalizer.Zemax(repeatedPath, entry, request)) }
                : [("frozen", original)])
            {
                var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
                var comparisonFolder = Path.Combine(folder, capture);
                Directory.CreateDirectory(comparisonFolder);
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, comparisonFolder);
                JsonFiles.Write(Path.Combine(comparisonFolder, "comparison.json"), run);
                rows.Add(new { lens, capture, run.Conclusion, metrics = run.Metrics });
                Console.WriteLine($"{lens} ({capture}): {run.Conclusion}");
                if (capture == "repeat")
                    repeats.Add(new { lens, series = native.Series.Select((s, i) => new { s.Id, count = s.Y.Length,
                        maxX = s.X.Zip(original.Series[i].X, (a, b) => Math.Abs(a - b)).Max(),
                        maxY = s.Y.Zip(original.Series[i].Y, (a, b) => Math.Abs(a - b)).Max() }) });
            }
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
        JsonFiles.Write(Path.Combine(output, "native-repeat-numeric-comparison.json"), repeats);
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { policySha256 = JsonFiles.Hash(File.ReadAllBytes(policyPath)),
            coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(OpticalSystemDesign.Core.Optic).Assembly.Location)),
            applicationSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(WorkbenchRuntime).Assembly.Location)) });
    }
}
