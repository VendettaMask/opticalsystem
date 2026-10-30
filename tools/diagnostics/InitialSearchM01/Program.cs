using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

if (args.Length != 4) throw new ArgumentException("<repository-root> <new-output-directory> <seed> <off|signs|quick|combined|diagnose>");
try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
using var serial = ComputationParallelism.SuppressNestedParallelism();
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); var seed = int.Parse(args[2]);
if (Directory.Exists(output)) throw new IOException("Frozen evidence cannot be overwritten.");
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};
var arm = args[3]; if (arm is not ("off" or "signs" or "quick" or "combined" or "diagnose")) throw new ArgumentException("Unknown arm.");
var inputs = new List<(string Name, AutonomousSearchPlan Plan, ParaxialSeedSearchOptions Options)>();
foreach (var name in new[] { "competition-3-F8-full10", "competition-8-F8-full25", "competition-8-F4-full25" })
{
    using var file = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
        "validation/rechecks/2026-10-30/structure-competition-v1/acceptance", name + ".seed-search.json")));
    var baseline = file.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
    inputs.Add((name, baseline.SourcePlan, baseline.Options));
}
using (var file = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
    "validation/rechecks/2026-10-28/three-to-two-continuation/selected-runs/reduce-3-2-42-C-geometry/task.topology-plan.json"))))
{
    var original = file.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
    var t = original.Seeds[0];
    var generation = inputs[0].Options.Generation! with
    {
        StopAfterElement = 2,
        FeasibleLayouts = new() { StopAfterElementCandidates = [2] },
        MinimumThicknessMillimeters = .5,
        MaximumThicknessMillimeters = 6.5,
        MinimumBackFocusMillimeters = original.Constraints.MinimumBackFocusMillimeters,
        Elements = t.Elements.Select(e => new StructureElementDescriptor
        {
            Materials = [e.Material],
            Profile = e.Front.Profile.Kind,
            FrontAperture = new(e.Front.ClearSemiDiameterMillimeters, e.Front.MechanicalSemiDiameterMillimeters),
            BackAperture = new(e.Back.ClearSemiDiameterMillimeters, e.Back.MechanicalSemiDiameterMillimeters)
        }).ToImmutableArray()
    };
    inputs.Add(("original-half21", original, inputs[0].Options with { Generation = generation }));
}
foreach (var input in inputs)
{
    var plan = input.Plan with { Options = input.Plan.Options with { Seed = seed } };
    var strategy = arm is "off" or "diagnose" ? null : new StructureSearchStrategy
    {
        PowerSigns = arm is "signs" or "combined" ? new() : null,
        QuickEvaluation = arm is "quick" or "combined" ? new() : null,
        FinalRayRecoveryFactor = input.Name == "original-half21" ? 12 : 2
    };
    var options = input.Options with { Competition = input.Options.Competition! with { SearchStrategy = strategy } };
    File.WriteAllText(Path.Combine(output, input.Name + ".input.json"), JsonSerializer.Serialize(new { plan, options }, json));
    if (arm == "diagnose")
    {
        var topology = input.Name == "original-half21" ? plan.Seeds[0] : ReadCandidate(input.Name);
        var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var before = ContentFingerprint.Compute(source.Topology);
        var diagnostics = StructureSearchDiagnostics.Analyze(source);
        using var time = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(500000);
        var watch = Stopwatch.StartNew(); var acceptance = TopologyValidator.Validate(source, plan.Targets, time.Token);
        File.WriteAllText(Path.Combine(output, input.Name + ".diagnostic.json"), JsonSerializer.Serialize(new
        {
            diagnostics,
            acceptance,
            acceptanceElapsed = watch.Elapsed,
            originalProtocolEstimatedRays = StructureSearchDiagnostics.EstimateFinalRays(source, 2),
            prechecks = StructureSearchDiagnostics.Precheck(plan, options.Generation!),
            sourceUnchanged = before == ContentFingerprint.Compute(source.Topology)
        }, json));
        Console.WriteLine($"diagnose {input.Name}: {diagnostics.StopReason}, failures={diagnostics.AttemptedSamples - diagnostics.ReachedImage}, validation={acceptance.Completed}");
        continue;
    }
    TimeSpan? firstQualified = null;
    using var observe = StructureCompetitionObservation.ObserveFirstQualified(elapsed => firstQualified ??= elapsed);
    var result = ParaxialSeedSearch.Run(plan, options);
    File.WriteAllText(Path.Combine(output, input.Name + ".json"), JsonSerializer.Serialize(result, json));
    File.WriteAllText(Path.Combine(output, input.Name + ".timing.json"), JsonSerializer.Serialize(new
    {
        firstOriginalQualifiedElapsed = firstQualified,
        totalElapsed = result.Work.Elapsed,
        found = firstQualified is not null,
        backend = "CPU/double",
        requestedSeed = seed,
        algorithm = result.Algorithm
    }, json));
    Console.WriteLine($"{arm} {input.Name} seed={seed} qualified={result.Competition!.QualifiedIds.Length} rays={result.Work.TracedRays} time={result.Work.Elapsed.TotalSeconds:F3}");
}
DesignTopology ReadCandidate(string name)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,
        "validation/rechecks/2026-10-30/structure-competition-v1/acceptance", name + ".seed-search.json")));
    return document.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!.Competition!.Candidates.First(c => c.Topology is not null).Topology!;
}
