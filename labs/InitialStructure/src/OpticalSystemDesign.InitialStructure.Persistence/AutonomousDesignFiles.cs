using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Explicit plan/import/export operations. Reading a plan never starts its search.</summary>
public static class AutonomousDesignFiles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task SavePlanAsync(AutonomousSearchPlan plan, string path, CancellationToken token = default)
    {
        _ = AutonomousSearchService.Create("plan-check", plan);
        await BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "autonomous plan",
            (stream, cancellation) => JsonSerializer.SerializeAsync(stream, new PlanFile(1, plan, ContentFingerprint.Compute(plan)), Json, cancellation), token);
    }

    public static async Task<AutonomousSearchPlan> LoadPlanAsync(string path, CancellationToken token = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "autonomous plan", token);
        var file = JsonSerializer.Deserialize<PlanFile>(bytes, Json) ?? throw new InvalidDataException("Empty autonomous plan.");
        if (file.Version != 1 || file.Plan is null || file.Sha256 != ContentFingerprint.Compute(file.Plan))
            throw new InvalidDataException("Autonomous plan version or checksum mismatch.");
        _ = AutonomousSearchService.Create("plan-check", file.Plan);
        return file.Plan;
    }

    public static async Task<DesignTopology> ImportSeedAsync(string path, TopologyConstraints constraints, CancellationToken token = default)
    {
        if (string.Equals(Path.GetExtension(path), StarOptProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        {
            var project = await StarOptProjectStore.LoadAsync(path, token);
            return TopologyCompiler.Import(project.Configurations[project.ActiveConfigurationIndex].ToSnapshot(), constraints).Topology;
        }
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "topology seed", token);
        if (path.EndsWith(".optic.json", StringComparison.OrdinalIgnoreCase))
            return TopologyCompiler.Import(JsonSerializer.Deserialize<OpticSnapshot>(bytes, Json) ?? throw new InvalidDataException("Empty optical snapshot."), constraints).Topology;
        var topology = JsonSerializer.Deserialize<DesignTopology>(bytes, Json) ?? throw new InvalidDataException("Empty topology seed.");
        return TopologyCompiler.Compile(topology, constraints).Topology;
    }

    public static async Task<string> ExportAsync(AutonomousSearchCandidate candidate, TopologyConstraints constraints, string path, CancellationToken token = default)
    {
        if (candidate.Topology.HasTransitionElements || !candidate.Assessment.Completed)
            throw new InvalidDataException("Only an independently assessed, non-transition candidate can be exported.");
        var compiled = TopologyCompiler.Compile(candidate.Topology, constraints);
        if (candidate.Assessment.OpticFingerprint != compiled.OpticFingerprint)
            throw new InvalidDataException("Candidate assessment does not describe its current physical prescription.");
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.EndsWith(StarOptProjectStore.Extension, StringComparison.OrdinalIgnoreCase)) fullPath += StarOptProjectStore.Extension;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await StarOptProjectStore.SaveAsync(new([compiled.CreateOptic()], 0), temporary, token);
            var restored = await StarOptProjectStore.LoadAsync(temporary, token);
            if (ContentFingerprint.Compute(restored.Configurations[restored.ActiveConfigurationIndex].ToSnapshot()) != compiled.OpticFingerprint)
                throw new InvalidDataException("STAROPT export did not reproduce the selected prescription.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
            return fullPath;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record PlanFile(int Version, AutonomousSearchPlan Plan, string Sha256);
}
