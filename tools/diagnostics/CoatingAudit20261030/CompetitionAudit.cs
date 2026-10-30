using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class CompetitionAudit
{
    internal static async Task Run(string[] args)
    {
        var directory = Path.GetFullPath(args[0]); Directory.CreateDirectory(directory);
        var name = args[1]; var iterations = args.Length > 2 ? int.Parse(args[2]) : 24;
        var modern = args.Length < 4 || args[3] != "legacy"; var search = args.Length > 4 && args[4] == "search";
        var source = args.Length > 5 ? await ExperimentStore.OpenAsync(args[5]) : Examples.Create(name switch { "hr" => DesignKind.HighReflector, "ar" => DesignKind.Antireflection, _ => DesignKind.NarrowBand });
        if (name.StartsWith("ideal", StringComparison.Ordinal))
        {
            var low = MaterialLibrary.Capture(new ConstantIndexMaterial("Reference L", 1.45), "Synthetic constant-index control, not real deposited-film data", 300, 1000);
            var high = MaterialLibrary.Capture(new ConstantIndexMaterial("Reference H", 2.1), "Synthetic constant-index control, not real deposited-film data", 300, 1000);
            var substrate = MaterialLibrary.Capture(new ConstantIndexMaterial("Reference S", 1.52), "Synthetic constant-index control, not real substrate data", 300, 1000);
            source = source with { LowId = low.Id, HighId = high.Id, SubstrateId = substrate.Id, Materials = source.Materials.Concat([low, high, substrate]).ToArray() };
        }
        source = source with
        {
            Target = source.Target with { Structure = args.Length > 5 ? source.Target.Structure : name.EndsWith('2') ? new(2) : null },
            Settings = source.Settings with
            {
                MaximumIterations = iterations,
                Search = new(4),
                DesignProtocolVersion = args.Length > 3 && args[3] == "general" ? 4 : source.Target.Kind != DesignKind.NarrowBand ? 2 : modern ? 3 : null
            }
        };
        await ExperimentStore.SaveAsync(source, Path.Combine(directory, "input.coating.json"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var service = new DesignService(); var watch = Stopwatch.StartNew(); var stages = new List<object>(); string? lastStage = null;
        var progress = new Immediate(p =>
        {
            if (p.Stage == lastStage || p.Stage.StartsWith("优化厚度", StringComparison.Ordinal)) return;
            lastStage = p.Stage; stages.Add(new { p.Stage, p.Evaluations, p.BestMerit, Seconds = watch.Elapsed.TotalSeconds });
            Console.WriteLine($"{watch.Elapsed.TotalSeconds:F1}s {p.Stage}");
        });
        var generated = search ? null : service.Generate(source, progress, deadline.Token);
        var output = search ? service.Search(source, progress, deadline.Token) : service.Optimize(generated!, progress, deadline.Token);
        await ExperimentStore.SaveAsync(output, Path.Combine(directory, "result.coating.json"));
        var result = output.Result!;
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new
        {
            Name = name,
            SourceInput = source,
            SourceFingerprint = source.Fingerprint(),
            Modern = modern,
            Workflow = search ? "Search" : "Generate+Optimize",
            GenerationRun = generated?.Result?.Run,
            GenerationMetrics = generated?.Result?.Metrics,
            GenerationPassed = generated?.Result?.Passed,
            Result = result,
            output.Layers,
            output.Candidates,
            Seconds = watch.Elapsed.TotalSeconds,
            Stages = stages
        }, Experiment.JsonOptions));
        var materials = output.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var solver = new CoherentThinFilmSolver(materials[output.IncidentId], materials[output.SubstrateId], output.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var points = result.Spectrum.Where((_, i) => i % Math.Max(1, result.Spectrum.Length / 16) == 0).ToArray();
        var comparisons = points.SelectMany(point => new[] { ThinFilmPolarization.S, ThinFilmPolarization.P, ThinFilmPolarization.Unpolarized }.Select(pol => new
        {
            Name = name,
            Lambda = point.WavelengthNanometers,
            Angle = source.Target.AngleDegrees,
            Pol = pol.ToString(),
            N0 = new[] { materials[output.IncidentId].RefractiveIndex(point.WavelengthNanometers), materials[output.IncidentId].ExtinctionCoefficient(point.WavelengthNanometers) },
            Ns = new[] { materials[output.SubstrateId].RefractiveIndex(point.WavelengthNanometers), materials[output.SubstrateId].ExtinctionCoefficient(point.WavelengthNanometers) },
            Layers = output.Layers.Select(l => new { N = new[] { materials[l.MaterialId].RefractiveIndex(point.WavelengthNanometers), materials[l.MaterialId].ExtinctionCoefficient(point.WavelengthNanometers) }, D = l.ThicknessNm }),
            Core = solver.Evaluate(point.WavelengthNanometers, source.Target.AngleDegrees, pol)
        }));
        await File.WriteAllTextAsync(Path.Combine(directory, "core-cases.json"), JsonSerializer.Serialize(comparisons, Experiment.JsonOptions));
        Console.WriteLine($"DONE {name} protocol={source.Settings.DesignProtocolVersion}: T={result.Metrics.PeakTransmittance:P6}, width={result.Metrics.FwhmNanometers:G10}, passed={result.Passed}; {watch.Elapsed.TotalSeconds:F2}s");
    }
    private sealed class Immediate(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
}
