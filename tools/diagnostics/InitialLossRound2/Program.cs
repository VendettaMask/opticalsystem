using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;
using System.Collections.Immutable;

if (args.Length == 3 && args[0] == "--probe") { await DenseProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--verify-one") { await QualifiedExport.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--envelope-starts") { await EnvelopeStartsProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--flat-envelope") { await FlatEnvelopeProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--back-focus") { await BackFocusProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--asphere-continuation") { await AsphereContinuationProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--sixth-continuation") { await AsphereContinuationProbe.Run(args[1], args[2], sixthOrder: true); return; }
if (args.Length == 3 && args[0] == "--validate-probe-endpoint") { await ProbeEndpointValidation.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--polish-clearance") { await ClearancePolishProbe.Run(args[1], args[2]); return; }
if (args.Length == 3 && args[0] == "--engine-polish") { await ProbeEndpointValidation.Run(args[1], args[2], optimize: true); return; }
if (args.Length is < 5 or > 6) throw new ArgumentException("<repo> <control-root> <new-output> <seed> <gates|legacy>[-budget] [case]");
var root = Path.GetFullPath(args[0]); var control = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
var seed = int.Parse(args[3]); var arm = args[4];
if (arm is not ("joint" or "gates" or "legacy" or "joint-budget" or "gates-budget" or "legacy-budget" or "joint-extended" or "legacy-extended" or "joint-focused" or "legacy-focused" or "joint-profile" or "legacy-profile" or "joint-final" or "legacy-final")) throw new ArgumentException("Unknown arm.");
if (args.Length == 6 && args[5] is not ("competition-8-F8-full25" or "original-half21")) throw new ArgumentException("Unknown original case.");
if (Directory.Exists(output) && (args.Length != 6 || Directory.EnumerateFiles(output, args[5] + ".*").Any()))
    throw new IOException("Existing case evidence is never overwritten.");
Directory.CreateDirectory(output);
// One headless computation at a time. BelowNormal can be starved by other
// active background jobs and spend the complete wall budget on parent validation.
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
foreach (var name in new[] { "competition-8-F8-full25", "original-half21" })
{
    if (args.Length == 6 && name != args[5]) continue;
    var sourcePath = Path.Combine(control, "seed" + seed, name + ".input.json");
    using var input = JsonDocument.Parse(File.ReadAllBytes(sourcePath));
    var plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
    var options = input.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
    var acquisition = input.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
    if (arm.StartsWith("gates", StringComparison.Ordinal)) options = options with { Evolution = options.Evolution! with { GateCompensationVersion = 1 } };
    if (arm.StartsWith("joint", StringComparison.Ordinal)) options = options with { Evolution = options.Evolution! with { GateCompensationVersion = 2 } };
    if (arm.EndsWith("-budget", StringComparison.Ordinal)) options = options with
    {
        Evolution = options.Evolution! with { CompensationUpdatesPerBranch = 20, CompensationRaysPerBranch = 300_000, MaximumPhaseTime = TimeSpan.FromSeconds(10) },
        Competition = options.Competition! with { UpdatesPerStage = 20, MaximumStageRaysPerCandidate = 300_000, MaximumStageTimePerCandidate = TimeSpan.FromSeconds(10) }
    };
    var increasedBudget = arm.EndsWith("-extended", StringComparison.Ordinal) || arm.EndsWith("-focused", StringComparison.Ordinal) || arm.EndsWith("-profile", StringComparison.Ordinal) || arm.EndsWith("-final", StringComparison.Ordinal);
    if (increasedBudget) options = options with
    {
        MaximumFunctionEvaluations = 600 - acquisition.FunctionEvaluations,
        MaximumTracedRays = 10_000_000 - acquisition.TracedRays,
        TimeLimit = TimeSpan.FromSeconds(300) - acquisition.Elapsed,
        MaximumEvaluationsPerStart = 200,
        Evolution = options.Evolution! with { CompensationUpdatesPerBranch = 40, CompensationRaysPerBranch = 1_000_000,
            MaximumPhaseTime = TimeSpan.FromSeconds(30) },
        Competition = options.Competition! with { UpdatesPerStage = 40, MaximumGradientEvaluations = 600 - acquisition.GradientEvaluations,
            MaximumStageRaysPerCandidate = 1_000_000, MaximumStageTimePerCandidate = TimeSpan.FromSeconds(30) }
    };
    if (arm.EndsWith("-focused", StringComparison.Ordinal) || arm.EndsWith("-profile", StringComparison.Ordinal) || arm.EndsWith("-final", StringComparison.Ordinal)) options = options with
    {
        MaximumStarts = 1, RetainedSeeds = 1, MaximumEvaluationsPerStart = 400,
        Evolution = options.Evolution! with { OptimizeParents = name != "original-half21", MaximumBranches = name == "original-half21" ? 3 : 1,
            Operations = name == "original-half21" ? [TopologyOperationKind.Delete] : ImmutableArray<TopologyOperationKind>.Empty,
            MaximumPhaseTime = TimeSpan.FromSeconds(60) },
        Competition = options.Competition! with { MaximumFinalists = 1, MaximumStageRaysPerCandidate = 5_000_000,
            ScreeningRaysPerCandidate = 50_000, MaximumStageTimePerCandidate = TimeSpan.FromSeconds(name == "original-half21" ? 120 : 60) }
    };
    if (arm.EndsWith("-profile", StringComparison.Ordinal) || arm.EndsWith("-final", StringComparison.Ordinal) && name == "competition-8-F8-full25") options = options with { Evolution = options.Evolution! with
    {
        Operations = [TopologyOperationKind.Profile],
        TransmissionProfileVersion = arm.StartsWith("joint", StringComparison.Ordinal) ? 1 : null
    } };
    File.WriteAllText(Path.Combine(output, name + ".input.json"), JsonSerializer.Serialize(new
    {
        plan, options, acquisition, sourcePath = Path.GetRelativePath(root, sourcePath),
        sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sourcePath))),
        unifiedBudget = increasedBudget
            ? JsonSerializer.SerializeToElement(new { functions = 600, gradients = 600, rays = 10_000_000, seconds = 300 }, json)
            : input.RootElement.GetProperty("unifiedBudget").Clone()
    }, json));
    var cpuStarted = Process.GetCurrentProcess().TotalProcessorTime;
    var result = ParaxialSeedSearch.Run(plan, options);
    var processCpu = Process.GetCurrentProcess().TotalProcessorTime - cpuStarted;
    await ParaxialSeedSearchFiles.SaveAsync(result, Path.Combine(output, name + ".seed-search.json"));
    var evolution = result.Evolution!;
    var assessments = evolution.Trials.Where(t => t.OriginalAssessment is { Completed: true }).Select(t => new
    {
        t.Id, t.Operation, elements = t.Endpoint!.Elements.Length, t.OriginalAssessment!.MeetsTargets,
        metricsComplete = t.OriginalAssessment.Fields.All(f => f.RmsRadiusMillimeters is not null && f.MaximumRadiusMillimeters is not null),
        worstRms = t.OriginalAssessment.Fields.All(f => f.RmsRadiusMillimeters is not null) ? t.OriginalAssessment.Fields.Max(f => f.RmsRadiusMillimeters) : null,
        worstMaximum = t.OriginalAssessment.Fields.All(f => f.MaximumRadiusMillimeters is not null) ? t.OriginalAssessment.Fields.Max(f => f.MaximumRadiusMillimeters) : null,
        minimumValid = t.OriginalAssessment.Fields.Min(f => f.ValidRays / (double)f.AttemptedRays),
        checks = t.OriginalAssessment.Fields.All(f => f.CheckValidRays == f.CheckAttemptedRays),
        violations = t.OriginalAssessment.Violations
    }).ToArray();
    File.WriteAllText(Path.Combine(output, name + ".summary.json"), JsonSerializer.Serialize(new
    {
        name, seed, arm, result.Termination, evolution.StopReason, evolution.Prechecks, result.Work, acquisition, processCpu,
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
