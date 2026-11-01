using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

internal static class NonlinearCorrectionAudit
{
    // Offline stage isolation. The preceding stages are byte-for-byte unchanged;
    // full Optimize results must independently confirm the same output layers.
    internal static async Task Phase(string inputDirectory, string baselineDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory); var records = new List<object>(); var service = new DesignService();
        var method = typeof(DesignService).GetMethod("RefinePassbandQuality", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var path in Directory.GetFiles(inputDirectory, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var input = await ExperimentStore.OpenAsync(path); if (input.Target.Kind != DesignKind.NarrowBand) continue;
            var baselinePath = Path.Combine(baselineDirectory, Path.GetFileName(path));
            var baseline = await ExperimentStore.OpenAsync(baselinePath);
            var materials = baseline.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            Console.WriteLine("Continuation start: " + input.Name);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var watch = Stopwatch.StartNew();
            var output = (Experiment)method.Invoke(service, [baseline, materials, null, timeout.Token, true])!;
            watch.Stop(); await ExperimentStore.SaveAsync(output, Path.Combine(outputDirectory, Path.GetFileName(path)));
            records.Add(new { input.Name, Stage = "Isolated passband direction comparison and joint polish; not a fresh full optimization", InputFingerprint = input.Fingerprint(),
                Baseline = baselinePath.Replace('\\','/'), Seconds = watch.Elapsed.TotalSeconds,
                Iterations = output.Result!.Run.GeneralWork!.PassbandQualityIterations - baseline.Result!.Run.GeneralWork!.PassbandQualityIterations,
                Corrections = output.Result.Run.GeneralWork.NonlinearConstraintCorrections,
                PeakBefore = baseline.Result.Metrics.PeakTransmittance, PeakAfter = output.Result.Metrics.PeakTransmittance,
                WidthBefore = baseline.Result.Metrics.FwhmNanometers, WidthAfter = output.Result.Metrics.FwhmNanometers });
            await File.WriteAllTextAsync(Path.Combine(outputDirectory,"phase-summary.json"), JsonSerializer.Serialize(records, Experiment.JsonOptions));
            Console.WriteLine($"{input.Name}: T {baseline.Result.Metrics.PeakTransmittance:G12} -> {output.Result.Metrics.PeakTransmittance:G12}; {watch.Elapsed.TotalSeconds:F3}s");
        }
    }
}
