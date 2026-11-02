using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class IlluminationControl
{
    internal static async Task Run(string repo, string output, string source, Optic optic, string coreHash)
    {
        using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
        var row = JsonFiles.Read<Row[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json")).Single(r => r.Index == 2166);
        var entry = AnalysisComparisonRegistry.Get(row.Key);
        var snapshot = Path.Combine(output, "source.optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
        var records = new List<object>();
        foreach (var density in new[] { 20, 40 })
        {
            var request = row.Run.Request! with { SurfaceCount = optic.SurfaceGroup.Items.Count,
                WorkbenchSettings = new Dictionary<string, string>(row.Run.Request!.WorkbenchSettings) { ["RayDensity"] = density.ToString() },
                ZemaxSettings = new Dictionary<string, object>(row.Run.Request.ZemaxSettings) { ["RayDensity"] = density } };
            var folder = Path.Combine(output, density.ToString());
            var job = new WorkbenchJob(snapshot, Path.Combine(folder, "core"), request);
            JsonFiles.Write(Path.Combine(folder, "request.json"), job); WorkbenchExecutor.Execute(job);
            var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
            if (count > 1) throw new InvalidOperationException("Independent native slot unavailable.");
            var nativeFolder = Path.Combine(folder, "native");
            var capture = await native.Capture(source, nativeFolder, "2026 R1", entry.ZemaxSettingsMapper, entry.ZemaxAnalysisType, request, 1, 120, false, CancellationToken.None);
            var run = new AnalysisRun { Key = row.Key, Directory = folder, Request = request, Tolerances = row.Run.Tolerances, Support = row.Run.Support };
            if (capture.ExitCode == 0 && File.Exists(Path.Combine(nativeFolder, "data.json")))
                ComparisonRunner.Compare(run, entry, row.Settings, ResultNormalizer.Workbench(Path.Combine(folder, "core/data.json"), entry, request),
                    ResultNormalizer.Zemax(Path.Combine(nativeFolder, "data.json"), entry, request), folder);
            else { run.Conclusion = Conclusion.Incomparable; run.Reason = "Native numerical reference unavailable; Core normalized identical-domain result is not native certification."; }
            JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            records.Add(new { index = 2166, density, run.Conclusion, run.Reason, run.Metrics, capture, coreHash,
                sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))) });
            JsonFiles.Write(Path.Combine(output, "results.json"), records);
            Console.WriteLine($"RI density {density}: {run.Conclusion}, native exit {capture.ExitCode}");
            if (capture.ExitCode != 0) break; // Preserve the failure; do not repeat a failed native request.
        }
    }
}
