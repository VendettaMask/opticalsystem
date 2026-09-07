using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;
if (args.Length != 2) throw new ArgumentException("Supply the representative specification template and a new output directory.");
if (Directory.Exists(args[1]) && Directory.EnumerateFileSystemEntries(args[1]).Any())
    throw new IOException("Existing output is never overwritten.");
var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var template = JsonSerializer.Deserialize<InitialStructureSpecification>(File.ReadAllText(args[0]), json)!;
foreach (var n in new[] { 1, 2, 3 })
{
    var spec = template with { Name = $"external-50mm-{n}-element", MinimumElementCount = n, MaximumElementCount = n };
    var directory = Path.Combine(args[1], $"{n}-element"); Directory.CreateDirectory(directory);
    var result = new FlatStartDesignService().Solve(spec, n);
    await File.WriteAllTextAsync(Path.Combine(directory, "generation.json"), JsonSerializer.Serialize(result, json));
    await File.WriteAllTextAsync(Path.Combine(directory, "candidate.json"), JsonSerializer.Serialize(result.Candidate, json));
    await new CandidateExportService().ExportStarOptAsync(result.Candidate!, Path.Combine(directory, "candidate.staropt"));
    Console.WriteLine($"{n}: {result.State}, RMS={result.Candidate!.Evaluation.RmsSpotRadiusMillimeters:R}, Max={result.Candidate.Evaluation.MaximumSpotRadiusMillimeters:R}");
}
