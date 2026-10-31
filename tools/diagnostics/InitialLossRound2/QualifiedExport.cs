using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class QualifiedExport
{
    internal static async Task Run(string path, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Export evidence exists.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, WriteIndented = true };
        using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        var result = file.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
        if (ContentFingerprint.Compute(result) != file.RootElement.GetProperty("sha256").GetString()) throw new InvalidDataException("Saved hash mismatch.");
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        foreach (var q in result.Evolution!.QualifiedArchive.Where(q => q.ParentId is not null))
        {
            var source = TopologyCompiler.Compile(q.Topology, result.SourcePlan.Constraints);
            var replay = TopologyValidator.Validate(source, result.SourcePlan.Targets);
            if (!replay.Completed || !replay.MeetsTargets) throw new InvalidDataException("Independent original replay failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new(q.Id, q.ParentId, q.Topology, replay, 0),
                result.SourcePlan.Constraints, Path.Combine(output, q.Id + ".staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, q.Id + ".topology.json"), JsonSerializer.Serialize(q.Topology, json));
            await File.WriteAllTextAsync(Path.Combine(output, q.Id + ".replay.json"), JsonSerializer.Serialize(new { DiagnosticOnly = true,
                SearchBudgetInput = false, SourcePath = path, Exported = exported, replay }, json));
            Console.WriteLine($"Cold replay and native export passed: {q.Id}, {q.Topology.Elements.Length} elements, {replay.Fields.Length} fields.");
        }
    }
}
