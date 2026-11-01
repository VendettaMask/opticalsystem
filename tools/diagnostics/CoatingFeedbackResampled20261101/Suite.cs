using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class Suite
{
    public static async Task Main(string[] args)
    {
        var service = new DesignService(); var rows = new List<object>();
        if (args[0] == "prepare")
        {
            using var recipe = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
            Directory.CreateDirectory(args[2]);
            foreach (var item in recipe.RootElement.EnumerateArray())
            {
                var source = JsonSerializer.Deserialize<Experiment>(await File.ReadAllTextAsync(item.GetProperty("Source").GetString()!), Experiment.JsonOptions)!;
                var replacements = item.GetProperty("Replacements").Deserialize<MaterialSnapshot[]>(Experiment.JsonOptions)!;
                var low = replacements[0]; var high = replacements[1];
                var oldMaterials = source.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
                var snapshots = source.Materials.Where(m => m.Id != source.LowId && m.Id != source.HighId).Concat(replacements).DistinctBy(m => m.Id).ToArray();
                var materials = snapshots.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
                var clamps = 0;
                FilmLayer[] Transfer(FilmLayer[] layers) => layers.Select(layer =>
                {
                    var id = layer.MaterialId == source.LowId ? low.Id : layer.MaterialId == source.HighId ? high.Id : layer.MaterialId;
                    var oldUnit = CoherentThinFilmSolver.CharacteristicThickness(oldMaterials[layer.MaterialId], oldMaterials[source.IncidentId], source.Target.CenterNm, source.Target.AngleDegrees);
                    var unit = CoherentThinFilmSolver.CharacteristicThickness(materials[id], materials[source.IncidentId], source.Target.CenterNm, source.Target.AngleDegrees);
                    var proposed = layer.ThicknessNm * unit / oldUnit;
                    var thickness = Math.Clamp(proposed, source.Settings.MinimumThicknessNm, source.Settings.MaximumThicknessNm);
                    if (thickness != proposed) clamps++;
                    return layer with { MaterialId = id, ThicknessNm = thickness };
                }).ToArray();
                var d = source with { Id = Guid.NewGuid(), Name = item.GetProperty("Name").GetString()!, LowId = low.Id, HighId = high.Id,
                    Materials = snapshots, Layers = Transfer(source.Layers), Result = null, Tolerance = null,
                    Candidates = source.Candidates?.Select(c => c with { Layers = Transfer(c.Layers), Run = null, Passed = false }).ToArray() };
                d = service.Evaluate(d);
                await ExperimentStore.SaveAsync(d, Path.Combine(args[2], d.Name + ".coating.json"));
                rows.Add(new { d.Name, Source = source.Name, Low = low.Id, High = high.Id, BoundClamps = clamps, d.Target, d.Settings, d.Result!.Metrics, d.Result.Checks, d.Result.Passed, Fingerprint = d.Fingerprint() });
                Console.WriteLine(d.Name + ": prepared; pass=" + d.Result.Passed);
            }
        }
        else if (args[0] is "search" or "search-feedback")
        {
            Directory.CreateDirectory(args[2]);
            foreach (var path in Directory.GetFiles(args[1], "*.coating.json").Order(StringComparer.Ordinal))
            {
                var input = await ExperimentStore.OpenAsync(path);
                using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var value = args[0] == "search" ? service.Search(input, token: limit.Token)
                    : (Experiment)typeof(DesignService).GetMethod("SearchMatchedFeedback")!.Invoke(service, [input, null, limit.Token])!;
                var result = value.Result!;
                await ExperimentStore.SaveAsync(value, Path.Combine(args[2], Path.GetFileName(path)));
                var od = (input.Target.StopBands ?? []).Select(b => result.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray();
                rows.Add(new { input.Name, Fingerprint = input.Fingerprint(), result.Passed, result.Metrics, Od = od, result.Checks, result.Run, Seconds = watch.Elapsed.TotalSeconds });
                await File.WriteAllTextAsync(Path.Combine(args[2], "summary.json"), JsonSerializer.Serialize(rows, Experiment.JsonOptions));
                Console.WriteLine(input.Name + ": search pass=" + result.Passed + "; T=" + result.Metrics.PeakTransmittance);
            }
        }
        else if (args[0] == "evaluate")
        {
            Directory.CreateDirectory(args[2]);
            foreach (var path in Directory.GetFiles(args[1], "*.coating.json").Order(StringComparer.Ordinal))
            {
                var raw = JsonSerializer.Deserialize<Experiment>(await File.ReadAllTextAsync(path), Experiment.JsonOptions)!;
                var d = await ExperimentStore.OpenAsync(path); var result = service.Evaluate(d);
                var same = JsonSerializer.Serialize(d.Result!.Metrics, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result!.Metrics, Experiment.JsonOptions)
                    && JsonSerializer.Serialize(d.Result.Spectrum, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result.Spectrum, Experiment.JsonOptions)
                    && JsonSerializer.Serialize(d.Result.Checks, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result.Checks, Experiment.JsonOptions);
                await ExperimentStore.SaveAsync(result, Path.Combine(args[2], Path.GetFileName(path)));
                var rawSame = JsonSerializer.Serialize(raw.Result!.Metrics, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result!.Metrics, Experiment.JsonOptions)
                    && JsonSerializer.Serialize(raw.Result.Spectrum, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result.Spectrum, Experiment.JsonOptions)
                    && JsonSerializer.Serialize(raw.Result.Checks, Experiment.JsonOptions) == JsonSerializer.Serialize(result.Result.Checks, Experiment.JsonOptions);
                rows.Add(new { d.Name, File = Path.GetFileName(path), Identical = same, RawIdentical = rawSame,
                    MetadataIdentical = JsonSerializer.Serialize(raw.Result.Run, Experiment.JsonOptions) == JsonSerializer.Serialize(d.Result!.Run, Experiment.JsonOptions),
                    result.Result.Passed, result.Result.Metrics, result.Result.Checks, result.Result.SamplingProtocol });
            }
        }
        else throw new ArgumentException("Unknown mode");
        await File.WriteAllTextAsync(Path.Combine(args[2], "summary.json"), JsonSerializer.Serialize(rows, Experiment.JsonOptions));
    }
}
