using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;

internal static class StageRecheck
{
    public static async Task Main(string[] args)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
        var rows = new List<object>(); var service = new DesignService(); Directory.CreateDirectory(args[2]);
        foreach (var row in json.RootElement.EnumerateArray())
        {
            var name = row.GetProperty("Name").GetString()!; var source = row.GetProperty("Source").GetString()!;
            var input = await ExperimentStore.OpenAsync(Path.Combine(args[0], source + ".coating.json"));
            var layers = row.GetProperty("Layers").Deserialize<FilmLayer[]>(Experiment.JsonOptions)!;
            var value = service.Evaluate(input with { Layers = layers, Result = null, Candidates = null, Tolerance = null }); var r = value.Result!;
            var od = (input.Target.StopBands ?? []).Select(b => r.Spectrum.Where(s => s.WavelengthNanometers >= b.MinimumNm && s.WavelengthNanometers <= b.MaximumNm).Min(s => s.Power.OpticalDensity)).ToArray();
            rows.Add(new { Name = name, Source = source, Arm = row.GetProperty("Arm").GetString(), Stage = row.GetProperty("Stage").GetString(), Round = row.GetProperty("Round").GetInt32(),
                r.Metrics, Od = od, r.Checks, r.Passed, r.SamplingConverged, r.SamplingProtocol, r.ExtremaRefinementEvaluations, r.ExtremaRefinementComplete });
            if (row.GetProperty("Stage").GetString() == "full" || r.Passed && !input.Result!.Passed)
                await ExperimentStore.SaveAsync(value, Path.Combine(args[2], name + ".coating.json"));
        }
        await File.WriteAllTextAsync(Path.Combine(args[2], "summary.json"), JsonSerializer.Serialize(rows, Experiment.JsonOptions));
        Console.WriteLine($"Rechecked {rows.Count} actual short/full prescriptions; full/new-pass spectra retained.");
    }
}
