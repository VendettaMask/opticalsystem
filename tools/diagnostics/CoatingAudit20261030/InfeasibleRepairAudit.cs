using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

internal static class InfeasibleRepairAudit
{
    internal static async Task Export(string directory, string output)
    {
        var cases = new List<object>();
        foreach (var path in Directory.GetFiles(directory, "*.coating.json").Order(StringComparer.Ordinal))
        {
            var d = await ExperimentStore.OpenAsync(path);
            var t = d.Target;
            var waves = ThinFilmSpectrum.Grid(t.MinimumNm, t.MaximumNm, 120)
                .Concat(new[] { t.CenterNm, t.CenterNm - t.FwhmNm / 2, t.CenterNm + t.FwhmNm / 2 })
                .Concat((t.StopBands ?? []).SelectMany(b => ThinFilmSpectrum.Grid(b.MinimumNm, b.MaximumNm, 8)))
                .Where(w => w >= t.MinimumNm && w <= t.MaximumNm).Distinct().Order().ToArray();
            cases.Add(new
            {
                Input = d,
                Materials = d.Materials.Select(m =>
                {
                    var physical = m.Resolve(new MaterialRegistry());
                    return new { m.Id, m.Name, Values = waves.Select(w => new[] { w, physical.RefractiveIndex(w), physical.ExtinctionCoefficient(w) }) };
                })
            });
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(cases, Experiment.JsonOptions));
    }

    internal static async Task ValidateReference(string inputDirectory, string referenceFile, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(referenceFile));
        var summary = new List<object>(); var service = new DesignService();
        foreach (var c in json.RootElement.EnumerateArray())
        {
            var name = c.GetProperty("Name").GetString()!;
            var source = await ExperimentStore.OpenAsync(Path.Combine(inputDirectory, c.GetProperty("Source").GetString()! + ".coating.json"));
            var layers = c.GetProperty("Layers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)!;
            var evaluated = service.Evaluate(source with { Layers = layers, Result = null, Candidates = null, Tolerance = null });
            await ExperimentStore.SaveAsync(evaluated, Path.Combine(outputDirectory, name + ".coating.json"));
            summary.Add(new { Name = name, c = c.Clone(), evaluated.Result!.Metrics, evaluated.Result.Checks, evaluated.Result.Passed });
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.json"), JsonSerializer.Serialize(summary, Experiment.JsonOptions));
    }
}
