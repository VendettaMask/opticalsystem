using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

// Post-search audits only. This process never changes a search endpoint or adds search budget.
if (args.Length is not (3 or 4) || args.Length == 4 && args[3] != "completed-only")
    throw new ArgumentException("<study-root> <new-audit-output> <original-input> [completed-only]");
var completeStudy = args.Length == 3;
var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Existing audit evidence is never overwritten.");
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
using var original = JsonDocument.Parse(await File.ReadAllBytesAsync(args[2]));
var originalPlan = original.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
var originalPreparation = original.RootElement.GetProperty("options").Deserialize<ParaxialSeedSearchOptions>(json)!;
var sourceHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(args[2])));
using var serial = ComputationParallelism.SuppressNestedParallelism();
var errors = new List<string>(); var rows = new List<object>(); var replayCount = 0;
string? spec = null;
using var protocol = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "protocol.json")));
foreach (var arm in new[] { "continuous", "paused" }) foreach (var seed in Enumerable.Range(42, 5))
{
    var label = $"{arm}/seed{seed}"; var directory = Path.Combine(root, arm, "seed" + seed);
    if (!completeStudy && !File.Exists(Path.Combine(directory, "run.preparation.json"))) continue;
    var checkpoint = await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(directory,"run.preparation.json"));
    using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"source.json")));
    var result = SeedPreparationSearch.Result(checkpoint).ColdStart!;
    Check(input.RootElement.GetProperty("sourceSha256").GetString() == sourceHash,"original input hash");
    spec ??= ContentFingerprint.Compute(result.ColdPlan with { Options = result.ColdPlan.Options with { Seed = 0, SeedPreparation = null } });
    Check(!input.RootElement.GetProperty("importedEndpoint").GetBoolean() && !input.RootElement.GetProperty("importedCheckpoint").GetBoolean(),"cold imports");
    Check(result.ColdPlan.Options.Seed==seed && result.Policy.Version==6,"seed and cold version");
    var expected = ColdStartDesignSearch.ColdPlan(originalPlan with { Options = originalPlan.Options with { Seed = seed, SeedPreparation = checkpoint.Options } });
    Check(ContentFingerprint.Compute(expected)==ContentFingerprint.Compute(result.ColdPlan),"unchanged original task");
    Check(checkpoint.Options.MaximumFunctionEvaluations==600 && result.Policy.MaximumGradientEvaluations==600
        && checkpoint.Options.MaximumTracedRays==10000000 && checkpoint.Options.TimeLimit==TimeSpan.FromSeconds(600),"same declared budget");
    Check(checkpoint.Status==SeedPreparationStatus.Completed && checkpoint.LostReservations==0,"normal completed continuation");
    var segment=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"segment.json")));
    var actual=segment.RootElement.GetProperty("actualSegmentRays").GetInt64();
    if(arm=="paused")
    {
        var suffix=JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory,"resumed-segment.json")));
        actual+=suffix.RootElement.GetProperty("actualSegmentRays").GetInt64();
        var prefix=await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(directory,"suspended.preparation.json"));
        Check(prefix.Status==SeedPreparationStatus.Suspended && prefix.Consumed.TracedRays==segment.RootElement.GetProperty("actualSegmentRays").GetInt64(),"saved exact prefix");
        Check(prefix.RandomState==checkpoint.RandomState || prefix.Stage=="Construction","random state continuation");
    }
    Check(actual==checkpoint.Consumed.TracedRays,"independent measured prefix plus suffix ray settlement");
    var work = result.Construction.Select(c => c.Work).Concat(result.Phases.Select(p => p.Work)).ToArray();
    Check(work.Sum(w => w.FunctionEvaluations) == result.Work.FunctionEvaluations, "function settlement");
    Check(work.Sum(w => w.GradientEvaluations) == result.Work.GradientEvaluations, "gradient settlement");
    Check(work.Sum(w => w.ValidationEvaluations) == result.Work.ValidationEvaluations, "validation settlement");
    Check(work.Sum(w => w.MatrixEvaluations) == result.Work.MatrixEvaluations, "matrix settlement");
    Check(work.Sum(w => w.GeometryProbes) == result.Work.GeometryProbes, "geometry settlement");
    Check(work.Sum(w => w.TracedRays) == result.Work.TracedRays, "ray settlement");
    var withinBudget = result.Work.FunctionEvaluations <= 600 && result.Work.GradientEvaluations <= 600
        && result.Work.TracedRays <= 10_000_000 && result.Work.Elapsed <= TimeSpan.FromSeconds(600);
    Check(result.Qualified && withinBudget,"original complete qualification within unchanged caps");
    var snapshots = result.Construction.Where(c => c.Topology is not null).Select(c => (Name: "construction-" + c.Index, Topology: c.Topology!, c.Assessment))
        .Concat(result.Phases.Select(p => (p.Name, p.Topology, p.Assessment))).ToList();
    if (result.Endpoint is not null) snapshots.Add(("endpoint", result.Endpoint, result.Assessment));
    var replays = new Dictionary<string, TopologyAssessment>();
    foreach (var item in snapshots)
    {
        Physical(item.Topology);
        if (item.Assessment is not { Completed: true }) continue;
        var compiled = TopologyCompiler.Compile(item.Topology, originalPlan.Constraints);
        Check(item.Assessment.OpticFingerprint == compiled.OpticFingerprint, item.Name + " assessment identity");
        if (!replays.TryGetValue(compiled.OpticFingerprint, out var replay))
        {
            replay = TopologyValidator.Validate(compiled, originalPlan.Targets); replays.Add(compiled.OpticFingerprint, replay); replayCount++;
        }
        Check(ContentFingerprint.Compute(item.Assessment) == ContentFingerprint.Compute(replay), item.Name + " exact independent replay");
    }
    ImmutableArray<TopologyVariableRequest> previous = [];
    if (!result.Phases.IsEmpty)
        Check(result.Construction.Where(c => c.Topology is not null).Any(c =>
            StructureEvolutionSearch.EvolutionVariables(c.Topology!, result.ColdPlan.Options)
                .SequenceEqual(result.Phases[0].AnchoredVariables)), "first bounds anchored on a fresh construction");
    foreach (var phase in result.Phases)
    {
        Check(previous.All(v => phase.AnchoredVariables.Contains(v)), phase.Name + " original bounds retained");
        previous = phase.AnchoredVariables;
        _ = TopologyParameterBinder.Bind(TopologyCompiler.Compile(phase.Topology, originalPlan.Constraints), phase.AnchoredVariables, exactMechanicalFrame: true);
        foreach (var v in phase.AnchoredVariables.Where(v => v.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient))
        {
            var face = Faces(phase.Topology).Single(f => f.Id == v.SurfaceId).Face;
            var limit = originalPlan.Options.Expansion!.CoefficientCoordinateSagMillimeters / Math.Pow(face.MechanicalSemiDiameterMillimeters ?? face.ClearSemiDiameterMillimeters, v.Power);
            Check(v.LowerBound == -limit && v.UpperBound == limit, phase.Name + " original coefficient bounds");
        }
    }
    var replayDirectory = Path.Combine(output, arm, "seed" + seed);
    Directory.CreateDirectory(replayDirectory);
    await File.WriteAllTextAsync(Path.Combine(replayDirectory, "independent-replays.json"),
        JsonSerializer.Serialize(new { SearchBudgetInput = false, Replays = replays }, json));
    string? exported = null;
    if (result.Qualified)
    {
        Check(withinBudget, "qualified endpoint within total budget");
        var compiled = TopologyCompiler.Compile(result.Endpoint!, originalPlan.Constraints);
        var replay = TopologyValidator.Validate(compiled, originalPlan.Targets); replayCount++;
        Check(replay is { Completed: true, MeetsTargets: true }, "repeated endpoint qualification");
        var destination = Path.Combine(output, arm, "seed" + seed, "qualified.staropt");
        exported = await AutonomousDesignFiles.ExportAsync(new(label, null, result.Endpoint!, replay, 0), originalPlan.Constraints, destination);
        var restored = await StarOptProjectStore.LoadAsync(exported);
        var imported = TopologyCompiler.Import(restored.Configurations[restored.ActiveConfigurationIndex].ToSnapshot(), originalPlan.Constraints);
        Check(imported.Topology.Reference.ZMillimeters==originalPlan.Seeds[0].Reference.ZMillimeters
            && imported.Topology.Stop.ZMillimeters==originalPlan.Seeds[0].Stop.ZMillimeters
            && imported.Topology.Stop.ClearSemiDiameterMillimeters==originalPlan.Seeds[0].Stop.ClearSemiDiameterMillimeters
            && imported.Topology.Image.ZMillimeters==originalPlan.Seeds[0].Image.ZMillimeters,"native fixed frame/stop");
        Check(imported.Topology.Elements.Length==originalPlan.Seeds[0].Elements.Length,"native element count");
        for(var i=0;i<imported.Topology.Elements.Length;i++)
        {
            var restoredElement=imported.Topology.Elements[i];var originalElement=originalPlan.Seeds[0].Elements[i];
            foreach(var faces in new[]{(restoredElement.Front,originalElement.Front),(restoredElement.Back,originalElement.Back)})
                Check(faces.Item1.ClearSemiDiameterMillimeters==faces.Item2.ClearSemiDiameterMillimeters
                    && faces.Item1.MechanicalSemiDiameterMillimeters==faces.Item2.MechanicalSemiDiameterMillimeters,"native original clear/mechanical apertures");
        }
        var readback = TopologyValidator.Validate(TopologyCompiler.Compile(imported.Topology, originalPlan.Constraints), originalPlan.Targets); replayCount++;
        Check(readback is { Completed: true, MeetsTargets: true }, "native readback qualification");
    }
    var final = result.Assessment;
    rows.Add(new { arm, seed, result.Qualified, WithinBudget = withinBudget, result.StopReason, result.Work,
        UniqueReplays = replays.Count, EndpointFingerprint = result.Endpoint is null ? null : TopologyCompiler.Compile(result.Endpoint, originalPlan.Constraints).OpticFingerprint,
        final?.Completed, Fields = final?.Fields.Length, MinimumValid = final is { Fields.IsEmpty: false } a ? a.Fields.Min(f => (double)f.ValidRays / f.AttemptedRays) : (double?)null,
        LostBoundary = final?.Fields.Sum(f => f.CheckAttemptedRays - f.CheckValidRays),
        WorstRms = final is { Completed: true } b ? b.Fields.Max(f => f.RmsRadiusMillimeters) : null,
        WorstMaximum = final is { Completed: true } c ? c.Fields.Max(f => f.MaximumRadiusMillimeters) : null,
        final?.Violations, exported });
    Console.WriteLine($"Audited {label}: qualified={result.Qualified}, budget={withinBudget}, replays={replays.Count}");

    void Check(bool condition, string message) { if (!condition) errors.Add(label + ": " + message); }
    void Physical(DesignTopology topology)
    {
        var source = originalPlan.Seeds[0];
        Check(topology.Reference == source.Reference && topology.Stop == source.Stop && topology.Image == source.Image, "fixed frame");
        Check(ContentFingerprint.Compute(topology.Fields) == ContentFingerprint.Compute(source.Fields)
            && ContentFingerprint.Compute(topology.Wavelengths) == ContentFingerprint.Compute(source.Wavelengths), "fields/spectrum");
        Check(topology.Elements.Length == source.Elements.Length, "element count");
        for (var i = 0; i < topology.Elements.Length; i++)
        {
            var e = topology.Elements[i]; var old = source.Elements[i];
            Check(e.Material == old.Material && e.Protected == old.Protected && !e.Transition, "material/protection");
            foreach (var pair in new[] { (e.Front, old.Front), (e.Back, old.Back) })
            {
                Check(pair.Item1.ClearSemiDiameterMillimeters == pair.Item2.ClearSemiDiameterMillimeters
                    && pair.Item1.MechanicalSemiDiameterMillimeters == pair.Item2.MechanicalSemiDiameterMillimeters, "clear/mechanical aperture");
                var p = pair.Item1.Profile;
                Check(Math.Abs(p.CurvaturePerMillimeter) <= originalPlan.Options.CurvatureBoundPerMillimeter && p.Conic == 0, "curvature/conic permission");
                Check(p.MaximumEvenPower <= originalPlan.Options.Expansion!.MaximumEvenPower, "profile power permission");
                foreach (var term in p.Terms)
                    Check(Math.Abs(term.Coefficient) <= originalPlan.Options.Expansion!.CoefficientCoordinateSagMillimeters / Math.Pow(pair.Item1.MechanicalSemiDiameterMillimeters ?? pair.Item1.ClearSemiDiameterMillimeters, term.Power), "coefficient permission");
            }
        }
    }
}
await File.WriteAllTextAsync(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(new { SearchBudgetInput = false,
    CompleteStudy = completeStudy, SourceSha256 = sourceHash, SpecFingerprint = spec, ReplayCount = replayCount, Errors = errors, Runs = rows }, json));
if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
static IEnumerable<(string Id, TopologyFace Face)> Faces(DesignTopology t) => t.Elements.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) });
