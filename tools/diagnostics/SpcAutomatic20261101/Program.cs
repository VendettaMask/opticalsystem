using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
using var serial = ComputationParallelism.SuppressNestedParallelism();
var mode = args[0]; var output = Path.GetFullPath(args[1]);
if (mode.StartsWith("product-", StringComparison.Ordinal)) { await Product.Run(mode, output, args.Length > 2 ? args[2] : null); return; }
if (mode == "freeze")
{
    if (Directory.Exists(output)) throw new IOException("Frozen inputs exist."); Directory.CreateDirectory(output);
    using var old = JsonDocument.Parse(await File.ReadAllBytesAsync("validation/rechecks/2026-11-01/spc-branches/formal-inputs/spc-seed42.json"));
    var original = old.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
    foreach (var seed in Enumerable.Range(42, 5)) await Write(Path.Combine(output, $"seed{seed}.json"), Spc(original, seed));
    await Write(Path.Combine(output, "protocol.json"), new { Seeds = Enumerable.Range(42, 5), Functions = 600, Gradients = 600, ActualCpuRays = 2_000_000, Seconds = 120,
        Arms = new[] { "continuous", "resumed" }, Pauses = new[] { "parent", "hessian", "minus", "minus-physical", "plus", "plus-physical" },
        FreshProcessForEveryResume = true, NoGateChanges = true, TimeAndPersistenceReportedSeparately = true,
        CheckpointUpdates = 1, SafePoints = "Complete BFGS/fixed-topology update or central-gradient Hessian column; original validation",
        NumericEnvironment = new { Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), VectorWidth = System.Numerics.Vector<double>.Count,
            Avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported, Fma = System.Runtime.Intrinsics.X86.Fma.IsSupported, AdvSimd = System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported },
        Scope = "Existing authored M3.1 parent; repeated seeds are not independent design evidence; no automatic aperture/cemented/recursive scheduling." });
    return;
}
if (mode == "run")
{
    var plan = JsonSerializer.Deserialize<AutonomousSearchPlan>(await File.ReadAllBytesAsync(args[2]), json)!;
    var target = args[3]; Directory.CreateDirectory(output);
    var path = Path.Combine(output, "run.preparation.json"); var store = new SeedPreparationCheckpointStore(); SeedPreparationCheckpoint state;
    if (File.Exists(path)) state = await store.LoadAsync(path);
    else
    {
        state = SeedPreparationSearch.Create("spc-seed" + plan.Options.Seed, plan, true) with { Revision = 1 };
        await store.SaveAsync(state, path, 0); File.Copy(args[2], Path.Combine(output, "input.json"));
    }
    var before = state.Consumed; var revision = state.Revision; var startingRevision = revision;
    using var pause = new CancellationTokenSource(); var pausedAt = false;
    using var rays = SequentialTraceMeasurement.Begin(); var wall = System.Diagnostics.Stopwatch.StartNew();
    state = SeedPreparationSearch.Run(state, next =>
    {
        store.SaveAsync(next, path, revision).AsTask().GetAwaiter().GetResult(); revision = next.Revision;
        if (!pausedAt && target != "none" && At(next.Spc!, target)) { pausedAt = true; pause.Cancel(); }
    }, pause.Token);
    var actual = rays.RayCount;
    Require(state.Consumed.TracedRays - before.TracedRays == actual, "Resume ray settlement includes exactly the new physical requests");
    Require(target == "none" || pausedAt, "Requested pause point was reached");
    Require(state.Consumed.FunctionEvaluations <= 600 && state.Consumed.GradientEvaluations <= 600 && state.Consumed.TracedRays <= 2_000_000
        && state.Consumed.Elapsed <= TimeSpan.FromSeconds(120) && state.LostReservations == 0, "Original total caps retained across processes");
    Directory.CreateDirectory(Path.Combine(output, "segments"));
    await Write(Path.Combine(output, "segments", $"from-{startingRevision}.json"), new { Target = target, StartRevision = startingRevision,
        state.Revision, state.Status, state.Stage, state.RandomState, state.Consumed, ActualNewRays = actual, Previous = before,
        ObservedSeconds = wall.Elapsed.TotalSeconds, PersistenceSeconds = state.PersistenceElapsed.TotalSeconds });
    if (state.Status == SeedPreparationStatus.Suspended) File.Copy(path, Path.Combine(output, "segments", target + ".preparation.json"));
    if (state.Stage == "Finished")
    {
        Require(state.Status == SeedPreparationStatus.Completed && state.Result!.Seeds.Length > 0, "Completed original-qualified seed pool");
        await ParaxialSeedSearchFiles.SaveAsync(state.Result!, Path.Combine(output, "result.seed-search.json"));
        var audit = new List<object>();
        foreach (var candidate in state.Spc!.Pool)
        {
            var compiled = TopologyCompiler.Compile(candidate.Topology, plan.Constraints); var qa = TopologyValidator.Validate(compiled, plan.Targets);
            Require(ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(candidate.Assessment), "Independent exact original candidate QA");
            audit.Add(new { candidate.Id, Assessment = qa });
        }
        var native = new List<object>(); var i = 0;
        foreach (var seed in state.Result!.Seeds)
        {
            var candidate = state.Spc.Pool.Single(c => ContentFingerprint.Compute(c.Topology) == ContentFingerprint.Compute(seed));
            var destination = await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(output, "qualified-" + i++ + ".staropt"));
            var saved = await StarOptProjectStore.LoadAsync(destination);
            var imported = TopologyCompiler.Import(saved.Configurations[saved.ActiveConfigurationIndex].ToSnapshot(), plan.Constraints);
            CheckFrame(seed, imported.Topology);
            var qa = TopologyValidator.Validate(imported, plan.Targets); Require(qa.MeetsTargets, "Native readback original QA");
            native.Add(new { candidate.Id, Readback = qa, File = Path.GetFileName(destination) });
        }
        await Write(Path.Combine(output, "independent-audit.json"), new { SearchBudgetInput = false, Candidates = audit, NativeReadbacks = native });
        using var reads = SequentialTraceMeasurement.Begin();
        var study = AutonomousDesignStudy.Capture("SPC automatic preparation", preparation: state);
        await AutonomousDesignStudyFiles.SaveStudyAsync(study, Path.Combine(output, "study.design-study.json"));
        await AutonomousDesignReport.SaveMarkdownAsync(AutonomousDesignReview.Build(study), Path.Combine(output, "report.md"));
        _ = await AutonomousDesignStudyFiles.LoadStudyAsync(Path.Combine(output, "study.design-study.json")); Require(reads.RayCount == 0, "Read-only evidence does not trace");
    }
    Console.WriteLine($"seed{plan.Options.Seed} {target}: {state.Status}; f={state.Consumed.FunctionEvaluations}, g={state.Consumed.GradientEvaluations}, rays={state.Consumed.TracedRays}, new={actual}");
    return;
}
if (mode == "compare")
{
    var rows = new List<object>();
    foreach (var seed in Enumerable.Range(42, 5))
    {
        var a = await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(output, "continuous", "seed" + seed, "run.preparation.json"));
        var b = await new SeedPreparationCheckpointStore().LoadAsync(Path.Combine(output, "resumed", "seed" + seed, "run.preparation.json"));
        Require(a.Status == SeedPreparationStatus.Completed && b.Status == SeedPreparationStatus.Completed, "Both arms complete");
        Require(a.RandomState == b.RandomState && Equivalent(a.Spc!) == Equivalent(b.Spc!), "Exact optimizer/curvature/branch/pool continuation");
        Require(a.Consumed with { Elapsed = TimeSpan.Zero } == b.Consumed with { Elapsed = TimeSpan.Zero }, "Exact discrete work and actual CPU rays");
        Require(ContentFingerprint.Compute(a.Result!.Seeds) == ContentFingerprint.Compute(b.Result!.Seeds), "Exact selected physical seed portfolio");
        rows.Add(new { Seed = seed, ExactStateAndWork = true, Continuous = a.Consumed, Resumed = b.Consumed, b.RandomState,
            Pool = b.Spc!.Pool.Length, QualifiedBranches = b.Result.Spc!.QualifiedBranchIds.Length, Selected = b.Result.Seeds.Length,
            Endpoints = b.Result.Seeds.Select(t => TopologyCompiler.Compile(t, b.Plan.Constraints).OpticFingerprint),
            SelectedWorstRmsMillimeters = b.Result.Seeds.Select(t => b.Spc.Pool.Single(c => ContentFingerprint.Compute(c.Topology) == ContentFingerprint.Compute(t)).Score) });
    }
    await Write(Path.Combine(output, "comparison.json"), new { Status = "verified", NormalizedOnlyElapsed = true, FreshProcessResume = true, Rows = rows });
    return;
}
if (mode == "guards")
{
    if (Directory.Exists(output)) throw new IOException("Original task evidence exists."); Directory.CreateDirectory(output);
    var rows = new List<object>();
    foreach (var seed in Enumerable.Range(42, 5))
    {
        foreach (var name in new[] { "F8", "F4", "half21" })
        {
            var path = name == "half21" ? $"validation/rechecks/2026-10-30/initial-structure-m2/formal/fixed-seed{seed}/original-half21.seed-search.json"
                : $"validation/rechecks/2026-11-01/initial-performance/inputs/competition-8-{name}-full25.input.json";
            using var source = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var element = name == "half21" ? source.RootElement.GetProperty("result").GetProperty("sourcePlan") : source.RootElement.GetProperty("plan");
            var plan = Spc(element.Deserialize<AutonomousSearchPlan>(json)!, seed); var fingerprint = ContentFingerprint.Compute(plan);
            var state = SeedPreparationSearch.Run(SeedPreparationSearch.Create("guard-" + name + seed, plan, true), _ => { });
            Require(state.Consumed.FunctionEvaluations == 0 && state.Consumed.TracedRays == 0 && state.Result!.Seeds.IsEmpty
                && ContentFingerprint.Compute(state.Plan) == fingerprint, "Original restriction, zero optical work and no fallback");
            var reason = name == "F8" ? state.Spc!.Parents.Single().Outcome : state.StopReason;
            Require(reason == (name == "F8" ? "OriginalElementLimit" : name == "F4" ? "OriginalExternalStopApertureConflict" : "OriginalInsertionPermissionConflict"), "Original guard outcome");
            await Write(Path.Combine(output, $"{name}-{seed}.json"), new { Source = path, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))), State = state });
            rows.Add(new { name, seed, reason, state.Consumed });
        }
        var previous = await new SeedPreparationCheckpointStore().LoadAsync($"validation/rechecks/2026-11-01/initial-performance/candidate/seed{seed}/run.preparation.json");
        var cold = previous.Result!.ColdStart!;
        var qa = TopologyValidator.Validate(TopologyCompiler.Compile(cold.Endpoint!, cold.ColdPlan.Constraints), cold.ColdPlan.Targets);
        Require(qa.MeetsTargets && ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(cold.Assessment), "Original five F8 complete QA unchanged");
        await Write(Path.Combine(output, $"F8-old-qualified-{seed}.json"), new { Original = cold.Assessment, Current = qa, Exact = true });
    }
    await Write(Path.Combine(output, "summary.json"), new { Guards = rows, OriginalF8ExactRechecks = 5 }); return;
}
if (mode == "reopen")
{
    using var rays = SequentialTraceMeasurement.Begin(); var count = 0;
    foreach (var path in Directory.GetFiles(output, "*.design-study.json", SearchOption.AllDirectories))
    { var study = await AutonomousDesignStudyFiles.LoadStudyAsync(path); _ = AutonomousDesignReview.Build(study); count++; }
    Require(count > 0 && rays.RayCount == 0, "Fresh process read-only study reopen");
    await Write(args[2], new { Studies = count, Rays = rays.RayCount, NewProcess = true }); return;
}
throw new ArgumentException("freeze/run/compare/guards/reopen");

AutonomousSearchPlan Spc(AutonomousSearchPlan plan, long seed) => plan with { Options = plan.Options with { Seed = seed,
    SeedPreparation = new() { Spc = new(), MaximumFunctionEvaluations = 600, MaximumTracedRays = 2_000_000, TimeLimit = TimeSpan.FromSeconds(120) } } };
async Task Write(string path, object value) { await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); await JsonSerializer.SerializeAsync(file, value, json); }
void Require(bool condition, string text) { if (!condition) throw new InvalidDataException(text); }
bool At(SpcPreparationState s, string target) => target switch
{
    "parent" => s.Phase == SpcPreparationPhase.ParentOptimization && s.Optimizer?.Iterations >= 1,
    "hessian" => s.Phase == SpcPreparationPhase.SaddleCurvature && s.Coarse is null && s.Curvature?.NextColumn == 1,
    "minus" => s.Sign == -1 && s.Phase == SpcPreparationPhase.BranchOptimization && s.BranchMinimum?.Iterations >= 1,
    "plus" => s.Sign == 1 && s.Phase == SpcPreparationPhase.BranchOptimization && s.BranchMinimum?.Iterations >= 1,
    "minus-physical" => s.Sign == -1 && s.Phase == SpcPreparationPhase.PhysicalOptimization && s.PhysicalContinuation?.Steps.Length >= 1,
    "plus-physical" => s.Sign == 1 && s.Phase == SpcPreparationPhase.PhysicalOptimization && s.PhysicalContinuation?.Steps.Length >= 1,
    _ => false
};
string Equivalent(object value)
{
    var node = JsonSerializer.SerializeToNode(value, json)!; Strip(node); return node.ToJsonString();
    void Strip(JsonNode n)
    {
        if (n is JsonObject o) foreach (var key in o.Select(p => p.Key).ToArray()) { if (key == "elapsed") o.Remove(key); else if (o[key] is { } child) Strip(child); }
        else if (n is JsonArray a) foreach (var child in a) if (child is not null) Strip(child);
    }
}
void CheckFrame(DesignTopology expected, DesignTopology actual)
{
    Require(expected.Reference.ZMillimeters == actual.Reference.ZMillimeters && expected.Stop.ZMillimeters == actual.Stop.ZMillimeters
        && expected.Stop.ClearSemiDiameterMillimeters == actual.Stop.ClearSemiDiameterMillimeters && expected.Stop.MechanicalSemiDiameterMillimeters == actual.Stop.MechanicalSemiDiameterMillimeters
        && expected.Image.ZMillimeters == actual.Image.ZMillimeters && expected.Elements.Length == actual.Elements.Length, "Native fixed frame and stop");
    foreach (var pair in expected.Elements.Zip(actual.Elements)) foreach (var faces in new[] { (pair.First.Front, pair.Second.Front), (pair.First.Back, pair.Second.Back) })
        Require(faces.Item1.ClearSemiDiameterMillimeters == faces.Item2.ClearSemiDiameterMillimeters && faces.Item1.MechanicalSemiDiameterMillimeters == faces.Item2.MechanicalSemiDiameterMillimeters, "Native original clear/mechanical apertures");
}
