using System.Diagnostics;
using System.Security.Cryptography;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class Convergence
{
    internal static async Task Run(string repo, string output)
    {
        using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
        var all = JsonFiles.Read<ReplayRow[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
        var results = new List<object>();
        var coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
        foreach (var index in new[] { 2596, 2597, 2602 })
        {
            var row = all.Single(r => r.Index == index); var entry = AnalysisComparisonRegistry.Get(row.Key);
            var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples", row.Lens);
            var optic = (await WorkbenchExecutor.Import(source)).Configurations[row.Configuration - 1];
            var snapshot = Path.Combine(output, index.ToString(), "source.optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
            CanonicalAnalysisRequest? previous = null; string? previousCore = null, previousNative = null;
            foreach (var sampling in new[] { row.Run.Request.PupilSampling, 64, 128, 256 }.Distinct().Order())
            {
                var original = row.Run.Request; var settings = new Dictionary<string, string>(original.WorkbenchSettings);
                foreach (var key in new[] { "Sampling", "PupilSampling", "NumRays" }) if (settings.ContainsKey(key)) settings[key] = sampling.ToString();
                var zSettings = new Dictionary<string, object>(original.ZemaxSettings);
                if (zSettings.ContainsKey("SampleSize")) zSettings["SampleSize"] = $"S_{sampling}x{sampling}";
                var request = original with { PupilSampling = sampling, GridSize = sampling, SurfaceCount = optic.SurfaceGroup.Items.Count, WorkbenchSettings = settings, ZemaxSettings = zSettings };
                var folder = Path.Combine(output, index.ToString(), sampling.ToString());
                var core = Path.Combine(folder, "core"); var job = new WorkbenchJob(snapshot, core, request);
                JsonFiles.Write(Path.Combine(folder, "request.json"), job); WorkbenchExecutor.Execute(job);
                var reference = Path.Combine(row.LegacyPath, "native");
                if (sampling != original.PupilSampling)
                {
                    var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
                    if (count > 1) throw new InvalidOperationException("Independent native slot unavailable");
                    reference = Path.Combine(folder, "native");
                    var execution = await native.Capture(source, reference, "2026 R1", entry.ZemaxSettingsMapper, entry.ZemaxAnalysisType, request, row.Configuration, 120, false, CancellationToken.None);
                    JsonFiles.Write(Path.Combine(folder, "execution.json"), new { execution, coreHash });
                    if (execution.ExitCode != 0) throw new InvalidOperationException("Native convergence capture failed; retained");
                }
                Compare("same-sampling", core, false, request, reference, true, request);
                if (previous is not null)
                {
                    Compare("core-convergence", previousCore!, false, previous, core, false, request);
                    Compare("native-convergence", previousNative!, true, previous, reference, true, request);
                }
                previous = request; previousCore = core; previousNative = reference;
                void Compare(string kind, string a, bool nativeA, CanonicalAnalysisRequest ra, string b, bool nativeB, CanonicalAnalysisRequest rb)
                {
                    var x = nativeA ? ResultNormalizer.Zemax(Path.Combine(a, "data.json"), entry, ra) : ResultNormalizer.Workbench(Path.Combine(a, "data.json"), entry, ra);
                    var y = nativeB ? ResultNormalizer.Zemax(Path.Combine(b, "data.json"), entry, rb) : ResultNormalizer.Workbench(Path.Combine(b, "data.json"), entry, rb);
                    var destination = Path.Combine(output, "comparisons", $"{index}-{sampling}-{kind}");
                    var run = new AnalysisRun { Key = row.Key, Directory = destination, Request = rb, Tolerances = row.Run.Tolerances, Support = row.Run.Support };
                    ComparisonRunner.Compare(run, entry, row.Settings, x, y, destination);
                    JsonFiles.Write(Path.Combine(destination, "comparison.json"), run);
                    results.Add(new { index, sampling, kind, run.Conclusion, run.Metrics, a, b, coreHash });
                    JsonFiles.Write(Path.Combine(output, "results.json"), results); Console.WriteLine($"{index} N{sampling} {kind}: {run.Conclusion}");
                }
            }
        }
    }
    private sealed record ReplayRow(int Index, string Key, string Lens, int Configuration, string LegacyPath, AnalysisConfiguration Settings, AnalysisRun Run);
}
