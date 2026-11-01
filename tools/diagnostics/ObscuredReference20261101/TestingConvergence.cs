using System.Diagnostics;
using System.Security.Cryptography;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class TestingConvergence
{
    internal static async Task Run(string repository, string output, string coreHash, int maximumSampling = 512)
    {
        using var lease = new FileStream(Path.Combine(repository, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var rows = JsonFiles.Read<ReplayRow[]>(Path.Combine(repository, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
        var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
        var executions = new List<object>(); var comparisons = new List<object>();
        foreach (var index in new[] { 2068, 2090, 2156, 2165, 2187 })
        {
            var row = rows.Single(r => r.Index == index); var entry = AnalysisComparisonRegistry.Get(row.Key);
            var source = Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", row.Lens);
            string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
            var sourceHash = Hash(source);
            var optic = (await WorkbenchExecutor.Import(source)).Configurations[row.Configuration - 1];
            var snapshot = Path.Combine(output, index.ToString(), "source.optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
            CanonicalAnalysisRequest? previous = null; string? previousFolder = null; string? previousNativeFolder = null;
            foreach (var sampling in (row.Key == "Wavefront" ? new[] { 128, 256 } : new[] { 128, 256, 512 }).Where(s => s <= maximumSampling))
            {
                var wb = new Dictionary<string, string>(row.Run.Request!.WorkbenchSettings);
                foreach (var key in new[] { "PupilSampling", "Sampling", "NumRays" }) if (wb.ContainsKey(key)) wb[key] = sampling.ToString();
                var zs = new Dictionary<string, object>(row.Run.Request.ZemaxSettings) { [row.Key == "Wavefront" ? "Sampling" : "PupilSampleSize"] = $"S_{sampling}x{sampling}" };
                var request = row.Run.Request with { PupilSampling = sampling, GridSize = sampling, SurfaceCount = optic.SurfaceGroup.Items.Count, WorkbenchSettings = wb, ZemaxSettings = zs };
                var folder = Path.Combine(output, index.ToString(), sampling.ToString());
                var job = new WorkbenchJob(snapshot, Path.Combine(folder, "core"), request); JsonFiles.Write(Path.Combine(folder, "request.json"), job);
                WorkbenchExecutor.Execute(job);
                var engines = Process.GetProcessesByName("OpticStudio"); var existing = engines.Length; foreach (var e in engines) e.Dispose();
                if (existing > 1) throw new InvalidOperationException("No independent native instance slot.");
                var referenceFolder = Path.Combine(folder, "native");
                var oldFolder = Path.Combine(repository, "validation/rechecks/2026-11-01/testing-three/convergence", index.ToString(), sampling.ToString(), "native");
                if (!File.Exists(Path.Combine(oldFolder, "data.json")))
                    oldFolder = Path.Combine(repository, "validation/rechecks/2026-11-01/testing-three/convergence-virtual", index.ToString(), sampling.ToString(), "native");
                var reused = File.Exists(Path.Combine(oldFolder, "data.json"));
                var result = reused ? new ProcessResult(0, false, false, "Reused unchanged native capture", "") : await native.Capture(source, referenceFolder, "2026 R1", "contract", entry.ZemaxAnalysisType, request, row.Configuration, 120, false, CancellationToken.None);
                if (reused) referenceFolder = oldFolder;
                executions.Add(new { index, sampling, result, reused, referenceFolder, unchanged = sourceHash == Hash(source), coreSha256 = coreHash });
                JsonFiles.Write(Path.Combine(output, "executions.json"), executions);
                if (result.ExitCode != 0 || sourceHash != Hash(source)) throw new InvalidOperationException("Native convergence capture failed; original failure retained.");
                Compare("same-sampling", Path.Combine(folder, "core/data.json"), false, request, Path.Combine(referenceFolder, "data.json"), true, request);
                if (previous is not null)
                {
                    Compare("core-convergence", Path.Combine(previousFolder!, "core/data.json"), false, previous, Path.Combine(folder, "core/data.json"), false, request);
                    Compare("native-convergence", Path.Combine(previousNativeFolder!, "data.json"), true, previous, Path.Combine(referenceFolder, "data.json"), true, request);
                }
                previous = request; previousFolder = folder; previousNativeFolder = referenceFolder;
                void Compare(string kind, string a, bool na, CanonicalAnalysisRequest ra, string b, bool nb, CanonicalAnalysisRequest rb)
                {
                    var comparisonFolder = Path.Combine(output, "comparisons", $"{index}-{kind}-{sampling}");
                    var first = na ? ResultNormalizer.Zemax(a, entry, ra) : ResultNormalizer.Workbench(a, entry, ra);
                    var second = nb ? ResultNormalizer.Zemax(b, entry, rb) : ResultNormalizer.Workbench(b, entry, rb);
                    var run = new AnalysisRun { Key = row.Key, Directory = comparisonFolder, Request = rb, Support = row.Run.Support, Tolerances = row.Run.Tolerances };
                    if (na == nb && first.Grids.Count == 1 && second.Grids.Count == 1)
                    {
                        var low = first.Grids.Single(); var high = second.Grids.Single();
                        var projected = low.Y.Select(y => low.X.Select(x => Interpolate(high, x, y)).ToArray()).ToArray();
                        var transformed = high with { X = low.X, Y = low.Y, Z = projected };
                        var log = new List<string> { "Explicit convergence diagnostic: bilinear interpolation of the finer grid at every coarser physical knot; no extrapolation, no mask filling or normalization; original result captures and gates retained." };
                        var matched = ComparisonMetrics.Align(low, transformed, log);
                        var metric = ComparisonMetrics.Calculate(low.Id, low.ValueAxis.Unit, matched, row.Run.Tolerances[low.ValueAxis.Quantity]);
                        run.Metrics.Add(metric); run.Conclusion = metric.Conclusion;
                        JsonFiles.Write(Path.Combine(comparisonFolder, "alignment.json"), log);
                    }
                    else ComparisonRunner.Compare(run, entry, row.Settings, first, second, comparisonFolder);
                    JsonFiles.Write(Path.Combine(comparisonFolder, "comparison.json"), run);
                    comparisons.Add(new { index, sampling, kind, run.Conclusion, run.Metrics, first = a, second = b });
                    JsonFiles.Write(Path.Combine(output, "comparisons.json"), comparisons);
                    Console.WriteLine($"{index} {kind} {sampling}: {run.Conclusion}");
                }
            }
        }
    }

    private static double? Interpolate(Grid2DResult grid, double x, double y)
    {
        var ix = Array.BinarySearch(grid.X, x); var iy = Array.BinarySearch(grid.Y, y);
        if (ix >= 0 && iy >= 0) return grid.Z[iy][ix];
        var right = ix >= 0 ? Math.Min(ix + 1, grid.X.Length - 1) : ~ix;
        var top = iy >= 0 ? Math.Min(iy + 1, grid.Y.Length - 1) : ~iy;
        if (right < 1 || right >= grid.X.Length || top < 1 || top >= grid.Y.Length) return null;
        var a = grid.Z[top - 1][right - 1]; var b = grid.Z[top - 1][right]; var c = grid.Z[top][right - 1]; var d = grid.Z[top][right];
        if (a is null || b is null || c is null || d is null) return null;
        var tx = (x - grid.X[right - 1]) / (grid.X[right] - grid.X[right - 1]);
        var ty = (y - grid.Y[top - 1]) / (grid.Y[top] - grid.Y[top - 1]);
        return (1 - ty) * ((1 - tx) * a + tx * b) + ty * ((1 - tx) * c + tx * d);
    }
}
