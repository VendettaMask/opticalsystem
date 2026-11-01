using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class BasinAudit
{
    internal static async Task Run(string inputs, string retained, string output, bool search)
    {
        Directory.CreateDirectory(output); var service = new DesignService(); var rows = new List<object>();
        foreach (var path in Directory.GetFiles(inputs, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var original = await ExperimentStore.OpenAsync(path); var current = await ExperimentStore.OpenAsync(Path.Combine(retained, Path.GetFileName(path)));
            var d = original with { Layers = current.Layers, Result = null, Candidates = null, Tolerance = null };
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var watch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"{(search ? "Search" : "Generate")} start {d.Name}");
            var result = search ? service.Search(d, token: limit.Token) : service.Generate(d, token: limit.Token); watch.Stop();
            await ExperimentStore.SaveAsync(result, Path.Combine(output, Path.GetFileName(path)));
            rows.Add(new { d.Name, Operation = search ? "Search" : "Generate", Seconds = watch.Elapsed.TotalSeconds,
                StartFingerprint = d.Fingerprint(), result.Result!.Passed, result.Result.Metrics, result.Result.Run });
            await File.WriteAllTextAsync(Path.Combine(output,"summary.json"), JsonSerializer.Serialize(rows, Experiment.JsonOptions));
            Console.WriteLine($"{d.Name}: passed={result.Result.Passed}; T={result.Result.Metrics.PeakTransmittance:G10}; W={result.Result.Metrics.FwhmNanometers:G10}; layers={result.Layers.Length}; {watch.Elapsed.TotalSeconds:F2}s");
        }
    }

    internal static async Task Export(string inputs, string retained, string output)
    {
        var rows = new List<object>();
        foreach (var path in Directory.GetFiles(inputs, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var input = await ExperimentStore.OpenAsync(path); var d = await ExperimentStore.OpenAsync(Path.Combine(retained, Path.GetFileName(path)));
            var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
            var waves = ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, 16).Append(d.Target.CenterNm).Distinct().Order().ToArray();
            rows.Add(new { Input = input, Retained = d.Layers, d.Result!.Metrics,
                Units = d.Layers.Select(l => CoherentThinFilmSolver.CharacteristicThickness(materials[l.MaterialId], materials[d.IncidentId], d.Target.CenterNm, d.Target.AngleDegrees)).ToArray(),
                Materials = materials.Select(m => new { Id = m.Key, Values = waves.Select(w => new[] { w, m.Value.RefractiveIndex(w), m.Value.ExtinctionCoefficient(w) }).ToArray() }).ToArray() });
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(rows, Experiment.JsonOptions));
        Console.WriteLine($"Exported {rows.Count} exact Core phase scales and material tables.");
    }
}
