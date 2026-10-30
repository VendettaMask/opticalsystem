using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length is < 4 or > 5) throw new ArgumentException("<repository-root> <new-output> <seed> <fixed|direct|recover|compete|power|combined> [case]");
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); var seed = int.Parse(args[2]); var arm = args[3];
if (arm is not ("fixed" or "direct" or "recover" or "compete" or "power" or "combined")) throw new ArgumentException("Unknown arm.");
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
foreach (var name in new[] { "competition-8-F8-full25", "competition-8-F4-full25", "original-half21" })
{
    if (args.Length == 5 && name != args[4]) continue;
    AutonomousSearchPlan plan; ParaxialSeedSearchOptions baseline; AutonomousSearchWork acquisition;
    string sourcePath;
    if (name == "original-half21")
    {
        sourcePath = "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json";
        using var input = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, sourcePath)));
        plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
            "validation/rechecks/2026-10-30/initial-structure-m0-m1/formal/off-seed42/original-half21.input.json")));
        baseline = fixture.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
        acquisition = new();
    }
    else
    {
        sourcePath = $"validation/rechecks/2026-10-30/initial-structure-m0-m1/formal/off-seed{seed}/{name}.json";
        var previous = JsonSerializer.Deserialize<ParaxialSeedSearchResult>(File.ReadAllBytes(Path.Combine(root, sourcePath)), json)!;
        var best = previous.Competition!.Candidates.Where(c => c.OriginalAssessment is { Completed: true })
            .OrderByDescending(c => c.OriginalAssessment!.Fields.Min(f => f.ValidRays / (double)f.AttemptedRays))
            .ThenBy(c => c.OriginalAssessment!.Fields.Max(f => f.RmsRadiusMillimeters ?? double.MaxValue)).First();
        plan = previous.SourcePlan with { Seeds = [best.Topology!] }; baseline = previous.Options; acquisition = previous.Work;
    }
    plan = plan with { Options = plan.Options with { Seed = seed } };
    var operations = arm is "fixed" or "power" ? ImmutableArray<TopologyOperationKind>.Empty
        : ImmutableArray.Create(TopologyOperationKind.Delete, TopologyOperationKind.Insert, TopologyOperationKind.Split,
            TopologyOperationKind.Merge, TopologyOperationKind.Material, TopologyOperationKind.Profile);
    var evolution = new StructureEvolutionOptions
    {
        Mode = arm == "direct" ? StructureEvolutionMode.DirectCompensation : arm == "recover"
            ? StructureEvolutionMode.RecoveryAndCompensation : StructureEvolutionMode.RecoveryAndCompetition,
        MaximumParents = 1,
        MaximumBranches = arm == "fixed" ? 1 : arm == "power" ? 2 : arm == "combined" ? 8 : 7,
        Operations = operations,
        PowerAllocation = arm is "power" or "combined" ? new() { ClosureFunctionsPerAttempt = 80 } : null,
        RecoveryFunctionsPerBranch = 24,
        CompensationUpdatesPerBranch = 3,
        MaximumPhaseTime = TimeSpan.FromSeconds(4)
    };
    var options = baseline with
    {
        Evolution = evolution,
        MaximumStarts = 4,
        RetainedSeeds = 2,
        MaximumFunctionEvaluations = 600 - acquisition.FunctionEvaluations,
        MaximumEvaluationsPerStart = 160,
        MaximumTracedRays = 2000000 - acquisition.TracedRays,
        TimeLimit = TimeSpan.FromSeconds(Math.Max(1, 110 - acquisition.Elapsed.TotalSeconds)),
        Competition = baseline.Competition! with
        {
            UpdatesPerStage = 4,
            MaximumStageTimePerCandidate = TimeSpan.FromSeconds(3),
            MaximumStageRaysPerCandidate = 30000,
            MaximumGradientEvaluations = 200 - acquisition.GradientEvaluations,
            SearchStrategy = new() { QuickEvaluation = new(), FinalRayRecoveryFactor = name == "original-half21" ? 12 : 2 }
        }
    };
    File.WriteAllText(Path.Combine(output, name + ".input.json"), JsonSerializer.Serialize(new
    {
        plan,
        options,
        acquisition,
        sourcePath,
        sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, sourcePath)))),
        unifiedBudget = new { functions = 600, gradients = 200, rays = 2000000, seconds = 110 }
    }, json));
    var result = ParaxialSeedSearch.Run(plan, options);
    await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(output, name + ".seed-search.json"));
    var e = result.Evolution!;
    File.WriteAllText(Path.Combine(output, name + ".summary.json"), JsonSerializer.Serialize(new
    {
        seed,
        arm,
        name,
        result.Algorithm,
        e.StopReason,
        result.Termination,
        e.FirstNewQualifiedElapsed,
        inheritedQualified = e.QualifiedArchive.Count(q => q.ParentId is null),
        newQualified = e.QualifiedArchive.Count(q => q.ParentId is not null),
        bestInheritedCount = e.QualifiedArchive.Where(q => q.ParentId is null).Select(q => (int?)q.Complexity.Elements).Min(),
        bestNewCount = e.QualifiedArchive.Where(q => q.ParentId is not null).Select(q => (int?)q.Complexity.Elements).Min(),
        archive = e.QualifiedArchive.Select(q => new { q.Id, q.ParentId, q.Complexity, q.WorstRmsMillimeters, q.WorstMaximumMillimeters }),
        work = result.Work,
        acquisition,
        endToEnd = new
        {
            functions = result.Work.FunctionEvaluations + acquisition.FunctionEvaluations,
            gradients = result.Work.GradientEvaluations + acquisition.GradientEvaluations,
            rays = result.Work.TracedRays + acquisition.TracedRays,
            elapsed = result.Work.Elapsed + acquisition.Elapsed
        }
    }, json));
    Console.WriteLine($"{arm} {name} seed={seed}: parent-qualified={e.QualifiedArchive.Count(q => q.ParentId is null)}, new-qualified={e.QualifiedArchive.Count(q => q.ParentId is not null)}, trials={e.Trials.Length}, rays={result.Work.TracedRays}, seconds={result.Work.Elapsed.TotalSeconds:F3}, {e.StopReason}");
}
