using System.Diagnostics;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class EnergyConvergence
{
    internal static async Task Run(string repository, string output, string coreHash)
    {
        using var lease = new FileStream(Path.Combine(repository, "artifacts/validation/.background-small-native-controls.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var row = JsonFiles.Read<ReplayRow[]>(Path.Combine(repository, "artifacts/validation/all-example-accuracy-20261031/rows.json")).Single(r => r.Index == 2278);
        var source = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", row.Lens);
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        var sourceHash = Hash(source);
        var imported = await WorkbenchExecutor.Import(source);
        var snapshot = Path.Combine(output, "source.optic.json"); JsonFiles.Write(snapshot, imported.Configurations[0].ToSnapshot());
        var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
        var entry = AnalysisComparisonRegistry.Get(row.Key);
        var executions = new List<object>();
        var comparisons = new List<object>();
        CanonicalAnalysisRequest? previous = null;
        string? previousFolder = null;
        foreach (var sampling in new[] { 32, 64, 128, 256, 512 })
        {
            var wb = new Dictionary<string, string>(row.Run.Request!.WorkbenchSettings);
            foreach (var key in new[] { "PupilSampling", "Sampling", "NumRays" }) wb[key] = sampling.ToString();
            wb["ImageSampling"] = (2 * sampling).ToString();
            var zs = new Dictionary<string, object>(row.Run.Request.ZemaxSettings) { ["SampleSize"] = $"S_{sampling}x{sampling}" };
            var request = row.Run.Request with { PupilSampling = sampling, ImageSampling = 2 * sampling, GridSize = 2 * sampling,
                WorkbenchSettings = wb, ZemaxSettings = zs };
            var folder = Path.Combine(output, sampling.ToString());
            var job = new WorkbenchJob(snapshot, Path.Combine(folder, "core"), request);
            JsonFiles.Write(Path.Combine(folder, "request.json"), job);
            WorkbenchExecutor.Execute(job);
            var engines = Process.GetProcessesByName("OpticStudio");
            var existing = engines.Length; foreach (var engine in engines) engine.Dispose();
            if (existing > 1) throw new InvalidOperationException("No independent native instance slot; no existing application was changed.");
            var result = await native.Capture(source, Path.Combine(folder, "native"), "2026 R1", "contract", entry.ZemaxAnalysisType,
                request, 1, 120, false, CancellationToken.None);
            executions.Add(new { sampling, result, sourceUnchanged = Hash(source) == sourceHash, budgetSeconds = 120, existingEngines = existing });
            JsonFiles.Write(Path.Combine(output, "executions.json"), executions);
            if (result.ExitCode != 0 || Hash(source) != sourceHash) throw new InvalidOperationException("Native convergence capture failed; raw failure retained.");
            Compare("same-sampling", Path.Combine(folder, "core/data.json"), false, request, Path.Combine(folder, "native/data.json"), true, request, sampling);
            if (previous is not null)
            {
                Compare("native-convergence", Path.Combine(previousFolder!, "native/data.json"), true, previous,
                    Path.Combine(folder, "native/data.json"), true, request, sampling);
                Compare("core-convergence", Path.Combine(previousFolder!, "core/data.json"), false, previous,
                    Path.Combine(folder, "core/data.json"), false, request, sampling);
            }
            previous = request; previousFolder = folder;
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreSha256 = coreHash, source, sourceSha256 = sourceHash,
            captureScreenshots = false, nativeExecuted = true, originalTolerances = row.Run.Tolerances, completedUtc = DateTimeOffset.UtcNow });

        void Compare(string kind, string pathA, bool nativeA, CanonicalAnalysisRequest requestA,
            string pathB, bool nativeB, CanonicalAnalysisRequest requestB, int sampling)
        {
            var folder = Path.Combine(output, "comparisons", kind + "-" + sampling);
            var a = nativeA ? ResultNormalizer.Zemax(pathA, entry, requestA) : ResultNormalizer.Workbench(pathA, entry, requestA);
            var b = nativeB ? ResultNormalizer.Zemax(pathB, entry, requestB) : ResultNormalizer.Workbench(pathB, entry, requestB);
            var run = new AnalysisRun { Key = row.Key, Directory = folder, Request = requestB, Support = row.Run.Support, Tolerances = row.Run.Tolerances };
            ComparisonRunner.Compare(run, entry, row.Settings, a, b, folder);
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            comparisons.Add(new { kind, sampling, run.Conclusion, run.Metrics, pathA, pathB, requestA = requestA.Fingerprint, requestB = requestB.Fingerprint });
            JsonFiles.Write(Path.Combine(output, "comparisons.json"), comparisons);
            Console.WriteLine($"{kind} {sampling}: {run.Conclusion}");
        }
    }
}
