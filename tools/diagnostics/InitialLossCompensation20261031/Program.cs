using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

if (args.Length is < 5 or > 6) throw new ArgumentException("<repo> <control-root> <new-output> <seed> <gates|legacy>[-budget] [case]");
var root = Path.GetFullPath(args[0]); var control = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
var seed = int.Parse(args[3]); var arm = args[4];
if (arm is not ("gates" or "legacy" or "gates-budget" or "legacy-budget")) throw new ArgumentException("Unknown arm.");
if (Directory.Exists(output)) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
foreach (var name in new[] { "competition-8-F8-full25", "competition-8-F4-full25", "original-half21" })
{
    if (args.Length == 6 && name != args[5]) continue;
    var sourcePath = Path.Combine(control, "seed" + seed, name + ".input.json");
    using var input = JsonDocument.Parse(File.ReadAllBytes(sourcePath));
    var plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
    var options = input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
    var acquisition = input.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
    if (arm.StartsWith("gates", StringComparison.Ordinal)) options = options with { Evolution = options.Evolution! with { GateCompensationVersion = 1 } };
    if (arm.EndsWith("-budget", StringComparison.Ordinal)) options = options with
    {
        Evolution = options.Evolution! with { CompensationUpdatesPerBranch = 20, CompensationRaysPerBranch = 300_000, MaximumPhaseTime = TimeSpan.FromSeconds(10) },
        Competition = options.Competition! with { UpdatesPerStage = 20, MaximumStageRaysPerCandidate = 300_000, MaximumStageTimePerCandidate = TimeSpan.FromSeconds(10) }
    };
    File.WriteAllText(Path.Combine(output, name + ".input.json"), JsonSerializer.Serialize(new
    {
        plan, options, acquisition, sourcePath = Path.GetRelativePath(root, sourcePath),
        sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))),
        unifiedBudget = input.RootElement.GetProperty("unifiedBudget").Clone()
    }, json));
    var result = ParaxialSeedSearch.Run(plan, options);
    await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(output, name + ".seed-search.json"));
    var evolution = result.Evolution!;
    var assessments = evolution.Trials.Where(t => t.OriginalAssessment is { Completed: true }).Select(t => new
    {
        t.Id, t.Operation, elements = t.Endpoint!.Elements.Length, t.OriginalAssessment!.MeetsTargets,
        worstRms = t.OriginalAssessment.Fields.Max(f => f.RmsRadiusMillimeters),
        worstMaximum = t.OriginalAssessment.Fields.Max(f => f.MaximumRadiusMillimeters),
        minimumValid = t.OriginalAssessment.Fields.Min(f => f.ValidRays / (double)f.AttemptedRays),
        checks = t.OriginalAssessment.Fields.All(f => f.CheckValidRays == f.CheckAttemptedRays),
        violations = t.OriginalAssessment.Violations
    }).ToArray();
    File.WriteAllText(Path.Combine(output, name + ".summary.json"), JsonSerializer.Serialize(new
    {
        name, seed, arm, result.Termination, evolution.StopReason, evolution.Prechecks, result.Work, acquisition,
        inheritedQualified = evolution.QualifiedArchive.Count(q => q.ParentId is null),
        newQualified = evolution.QualifiedArchive.Count(q => q.ParentId is not null),
        assessments,
        trials = evolution.Trials.Select(t => new
        {
            t.Id, t.Operation, t.Outcome, t.Detail, t.Screen, t.Work,
            t.Compensation?.AcceptedUpdates, t.Compensation?.InitialObjective, t.Compensation?.EndpointObjective,
            diagnostics = t.Compensation?.Diagnostics, steps = t.Compensation?.Steps
        }),
        endToEnd = new
        {
            functions = result.Work.FunctionEvaluations + acquisition.FunctionEvaluations,
            gradients = result.Work.GradientEvaluations + acquisition.GradientEvaluations,
            rays = result.Work.TracedRays + acquisition.TracedRays,
            elapsed = result.Work.Elapsed + acquisition.Elapsed
        }
    }, json));
    Console.WriteLine($"{arm} {name} seed={seed}: parent={evolution.QualifiedArchive.Count(q => q.ParentId is null)}, new={evolution.QualifiedArchive.Count(q => q.ParentId is not null)}, rays={result.Work.TracedRays}, seconds={result.Work.Elapsed.TotalSeconds:F3}, {evolution.StopReason}");
    foreach (var a in assessments)
        Console.WriteLine($"  {a.Operation} {a.elements} elements: RMS={a.worstRms * 1000:F6}, max={a.worstMaximum * 1000:F6} um, transmission={a.minimumValid:F6}, edge={a.checks}, qualified={a.MeetsTargets}");
}
