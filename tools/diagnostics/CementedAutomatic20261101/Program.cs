using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.App.Laboratories.InitialStructure;
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
if (mode == "freeze")
{
    Require(!Directory.Exists(output), "Inputs already exist"); Directory.CreateDirectory(output);
    var source = "validation/rechecks/2026-11-01/spc-default-output/final-verified/formal/continuous/seed42/run.preparation.json";
    using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(source));
    var old = doc.RootElement.GetProperty("checkpoint").Deserialize<SeedPreparationCheckpoint>(json)!;
    var parent = old.Result!.Seeds.Single(t => t.Elements.Length == 1);
    var original = old.Plan; var local = new AutonomousSearchOptions().Local with
    { Sampling = original.Options.Local.Sampling, MaximumUpdates = 3, MaximumFunctionEvaluations = 20, MaximumGradientEvaluations = 20 };
    foreach (var seed in Enumerable.Range(42, 5))
    {
        var plan = original with { Seeds = [parent], Options = original.Options with { Seed = seed, SeedPreparation = null,
            Expansion = new() { Cemented = new() { ThicknessMultipliers = [1] }, EnableInsertion = true, EnableMaterial = true,
                EnableDeletion = false, EnableGradualDeletion = false, EnableSplit = false, EnableMerge = false, EnableProfile = false },
            InsertionMaterials = ["N-F2"], MaximumTrials = 3, BranchesPerParent = 4,
            MaximumFunctionEvaluations = 300, MaximumGradientEvaluations = 200, MaximumTracedRays = 3_000_000,
            MaximumStageTracedRays = 500_000, FinalValidationRayReserve = 100_000, TimeLimit = TimeSpan.FromSeconds(120),
            Recovery = original.Options.Recovery with { MaximumFunctionEvaluations = 24, TimeLimit = TimeSpan.FromSeconds(10) },
            Local = local, Full = local with { MaximumUpdates = 4 }, AxialCompensation = new() } };
        await Write(Path.Combine(output, $"seed{seed}.json"), plan);
        await Write(Path.Combine(output, $"control{seed}.json"), plan with { Options = plan.Options with { Expansion = plan.Options.Expansion with { Cemented = null } } });
    }
    await Write(Path.Combine(output, "protocol.json"), new { Seeds = Enumerable.Range(42, 5), Functions = 300, Gradients = 200, ActualCpuRays = 3_000_000,
        Seconds = 120, Pauses = new[] { "dispatch", "local", "full", "pool" }, FreshProcessPerSegment = true, NoGateChanges = true,
        Source = source, SourceSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source))),
        ParentSource = "One previously qualified single-lens endpoint; reused parent, not cold-start success", RecursiveSpc = false });
    return;
}
if (mode == "run")
{
    var plan = JsonSerializer.Deserialize<AutonomousSearchPlan>(await File.ReadAllBytesAsync(args[2]), json)!;
    var target = args[3]; Directory.CreateDirectory(output); var store = new AutonomousSearchCheckpointStore();
    var path = Path.Combine(output, "run.autonomous.json"); AutonomousSearchCheckpoint state;
    if (File.Exists(path)) state = await store.LoadAsync(path);
    else { state = AutonomousSearchService.Create("cemented-seed" + plan.Options.Seed, plan) with { Revision = 1 }; await store.SaveAsync(state, path, 0); }
    var before = state.Consumed; var revision = state.Revision; var firstRevision = revision; var paused = false;
    using (var pause = new CancellationTokenSource())
    using (var measured = SequentialTraceMeasurement.Begin())
    {
        void Stop(string point) { if (!paused && target == point) { paused = true; pause.Cancel(); } }
        state = AutonomousSearchService.Run(state, next =>
        {
            store.SaveAsync(next, path, revision).AsTask().GetAwaiter().GetResult(); revision = next.Revision;
            if (next.Pending is { Operation: "cemented-insert", Phase: AutonomousSearchPhase.Recovery } && next.Reservation is not null) Stop("dispatch");
            if (next.Reservation is null && next.Candidates.Any(c => c.Topology.CementedInterfaces is not null)) Stop("pool");
        }, pause.Token, (pending, _) =>
        { if (pending.Operation == "cemented-insert") Stop(pending.Phase == AutonomousSearchPhase.Local ? "local" : "full"); });
        Require(state.Consumed.TracedRays - before.TracedRays == measured.RayCount, "Segment ray ledger");
        Directory.CreateDirectory(Path.Combine(output, "segments"));
        await Write(Path.Combine(output, "segments", $"from-{firstRevision}.json"), new { Target = target, PausedAtTarget = paused, Previous = before,
            Consumed = state.Consumed, ActualNewRays = measured.RayCount, State = state.Status, Pending = state.Pending });
    }
    Require(target == "none" ? state.Status == AutonomousSearchStatus.Completed : paused && state.Status == AutonomousSearchStatus.Cancelled, "Requested endpoint");
    Console.WriteLine($"seed{plan.Options.Seed} {target}: {state.Status}; f={state.Consumed.FunctionEvaluations}, g={state.Consumed.GradientEvaluations}, rays={state.Consumed.TracedRays}");
    if (state.Status != AutonomousSearchStatus.Completed) return;
    var audits = new List<object>(); var native = new List<object>();
    foreach (var c in state.Candidates)
    {
        var qa = TopologyValidator.Validate(TopologyCompiler.Compile(c.Topology, plan.Constraints), plan.Targets);
        Require(ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(c.Assessment), "Independent original QA exactness");
        audits.Add(new { c.Id, Assessment = qa });
        if (c.Topology.CementedInterfaces is null) continue;
        var file = await AutonomousDesignFiles.ExportAsync(c, plan.Constraints, Path.Combine(output, c.Id + ".staropt"));
        var saved = await StarOptProjectStore.LoadAsync(file);
        var imported = TopologyCompiler.Import(saved.Configurations[saved.ActiveConfigurationIndex].ToSnapshot(), plan.Constraints);
        Require(imported.Topology.CementedInterfaces?.Interfaces.Length == c.Topology.CementedInterfaces.Interfaces.Length, "Shared-interface native identity");
        var readback = TopologyValidator.Validate(imported, plan.Targets);
        var difference = new { FocalLength = readback.EffectiveFocalLengthMillimeters - qa.EffectiveFocalLengthMillimeters,
            FNumber = readback.FNumber - qa.FNumber, Rms = readback.Fields.Zip(qa.Fields, (a,b) => Math.Abs((a.RmsRadiusMillimeters ?? 0) - (b.RmsRadiusMillimeters ?? 0))).DefaultIfEmpty().Max(),
            Maximum = readback.Fields.Zip(qa.Fields, (a,b) => Math.Abs((a.MaximumRadiusMillimeters ?? 0) - (b.MaximumRadiusMillimeters ?? 0))).DefaultIfEmpty().Max() };
        await Write(Path.Combine(output, c.Id + "-native-audit.json"), new { Original = qa, Readback = readback, Difference = difference });
        Require(readback.MeetsTargets == qa.MeetsTargets && readback.Completed && readback.Fields.Length == qa.Fields.Length, "Native original QA");
        native.Add(new { c.Id, Readback = readback, Difference = difference });
    }
    await Write(Path.Combine(output, "independent-audit.json"), new { SearchBudgetInput = false, Candidates = audits, Native = native });
    using (var reads = SequentialTraceMeasurement.Begin())
    {
        var study = AutonomousDesignStudy.Capture("Automatic cemented structures", state);
        await AutonomousDesignStudyFiles.SaveStudyAsync(study, Path.Combine(output, "study.design-study.json"));
        await AutonomousDesignReport.SaveMarkdownAsync(AutonomousDesignReview.Build(study), Path.Combine(output, "report.md"));
        Require(reads.RayCount == 0, "Saved report does not trace");
    }
    return;
}
if (mode == "compare")
{
    var rows = new List<object>();
    foreach (var seed in Enumerable.Range(42, 5))
    {
        var a = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(output, $"continuous/seed{seed}/run.autonomous.json"));
        var b = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(output, $"resumed/seed{seed}/run.autonomous.json"));
        Require(a.Status == AutonomousSearchStatus.Completed && b.Status == a.Status && a.RandomState == b.RandomState && a.TrialCursor == b.TrialCursor,
            "Scheduling/RNG exactness");
        Require(a.Consumed with { Elapsed = TimeSpan.Zero } == b.Consumed with { Elapsed = TimeSpan.Zero }, "All discrete work exact");
        Require(ContentFingerprint.Compute(a.Candidates) == ContentFingerprint.Compute(b.Candidates)
            && ContentFingerprint.Compute(a.AcceptedArchive) == ContentFingerprint.Compute(b.AcceptedArchive) && a.Pool.SequenceEqual(b.Pool), "Pool and qualified archive exact");
        var segments = Directory.GetFiles(Path.Combine(output, $"resumed/seed{seed}/segments"), "*.json")
            .Select(p => JsonDocument.Parse(File.ReadAllBytes(p))).ToArray();
        Require(segments.Length == 5 && segments.Sum(d => d.RootElement.GetProperty("actualNewRays").GetInt64()) == a.Consumed.TracedRays, "No replay across four pauses");
        rows.Add(new { Seed = seed, Work = a.Consumed, Candidates = a.Candidates.Length, Qualified = a.AcceptedArchive.Length,
            QualifiedCemented = a.AcceptedArchive.Count(c => c.Topology.CementedInterfaces is not null), DistinctPrescriptions = a.Candidates.Select(c => ContentFingerprint.Compute(c.Topology)).ToArray(),
            Exact = true, ReplayedRays = 0 });
        foreach (var d in segments) d.Dispose();
    }
    await Write(Path.Combine(output, "comparison.json"), new { Status = "verified", FreshProcess = true, Rows = rows }); return;
}
if (mode == "guards")
{
    Require(!Directory.Exists(output), "Original guard scope exists"); Directory.CreateDirectory(output); var rows = new List<object>();
    foreach (var seed in Enumerable.Range(42, 5))
    {
        foreach (var name in new[] { "F8", "F4", "half21" })
        {
            var path = name == "half21" ? $"validation/rechecks/2026-10-30/initial-structure-m2/formal/fixed-seed{seed}/original-half21.seed-search.json"
                : $"validation/rechecks/2026-11-01/initial-performance/inputs/competition-8-{name}-full25.input.json";
            using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var element = name == "half21" ? doc.RootElement.GetProperty("result").GetProperty("sourcePlan") : doc.RootElement.GetProperty("plan");
            var original = element.Deserialize<AutonomousSearchPlan>(json)!;
            var plan = original with { Options = original.Options with { Seed = seed,
                Expansion = (original.Options.Expansion ?? new()) with { Cemented = new() } } };
            using var rays = SequentialTraceMeasurement.Begin();
            var rejection = AutonomousSearchService.CementedGrowthRejection(plan, plan.Seeds[0]);
            Require(rejection == (name == "F8" ? "OriginalElementLimit" : name == "F4" ? "OriginalExternalStopApertureConflict" : "OriginalInsertionPermissionConflict"), "Original branch restriction");
            AutonomousSearchCheckpoint? state = name == "F4" ? AutonomousSearchService.Run(AutonomousSearchService.Create("original-f4-" + seed, plan)) : null;
            Require(rays.RayCount == 0 && (state is null || state.Consumed.FunctionEvaluations == 0 && state.AcceptedArchive.IsEmpty), "Zero optical work for guard");
            await Write(Path.Combine(output, name + "-" + seed + ".json"), new { Source = path, Original = original, Plan = plan,
                GrowthBranchRejection = rejection, TracedRays = rays.RayCount, MainConflict = state });
            rows.Add(new { Name = name, Seed = seed, Rejection = rejection, TracedRays = rays.RayCount, Scope = state is null ? "growth branch preflight only" : "full main specification guard" });
        }
        using var old = JsonDocument.Parse(await File.ReadAllBytesAsync($"validation/rechecks/2026-11-01/initial-performance/candidate/seed{seed}/run.preparation.json"));
        var stateOld = old.RootElement.GetProperty("checkpoint").Deserialize<SeedPreparationCheckpoint>(json)!; var cold = stateOld.Result!.ColdStart!;
        var qa = TopologyValidator.Validate(TopologyCompiler.Compile(cold.Endpoint!, cold.ColdPlan.Constraints), cold.ColdPlan.Targets);
        Require(qa.MeetsTargets && ContentFingerprint.Compute(qa) == ContentFingerprint.Compute(cold.Assessment), "Old F8 original QA exact");
        await Write(Path.Combine(output, $"F8-old-qualified-{seed}.json"), new { Original = cold.Assessment, Current = qa, Exact = true });
    }
    await Write(Path.Combine(output, "summary.json"), new { Guards = rows, OriginalF8ExactRechecks = 5 }); return;
}
if (mode == "reopen")
{
    var studies = Directory.GetFiles(output, "*.design-study.json", SearchOption.AllDirectories);
    using var reads = SequentialTraceMeasurement.Begin();
    foreach (var p in studies) { var s = await AutonomousDesignStudyFiles.LoadStudyAsync(p); _ = AutonomousDesignReport.BuildMarkdown(AutonomousDesignReview.Build(s)); }
    Require(reads.RayCount == 0, "New-process report readback");
    await Write(args[2], new { Studies = studies.Length, Rays = reads.RayCount }); return;
}
if (mode.StartsWith("product-", StringComparison.Ordinal))
{
    var session = new AutonomousDesktopSession(output);
    if (mode == "product-start")
    {
        Require(!Directory.Exists(output), "Product scope exists"); var plan = JsonSerializer.Deserialize<AutonomousSearchPlan>(await File.ReadAllBytesAsync(args[2]), json)!;
        var paused = false; await session.RunAsync(plan, false, _ => { }, (pending, _) =>
        { if (!paused && pending.Operation == "cemented-insert" && pending.Phase == AutonomousSearchPhase.Full) { paused = true; session.Cancel(); } });
        Require(paused && session.Checkpoint?.Status == AutonomousSearchStatus.Cancelled, "Product safe stop");
        await Write(Path.Combine(output, "stopped.json"), new { session.RecordPath, State = session.Checkpoint }); return;
    }
    var path = Directory.GetFiles(output, "*.autonomous.json").Single(); long load;
    using (var reads = SequentialTraceMeasurement.Begin()) { await session.LoadAsync(path); load = reads.RayCount; }
    using var rays = SequentialTraceMeasurement.Begin(); await session.RunAsync(session.Checkpoint!.Plan, true, _ => { });
    Require(session.Checkpoint!.Status == AutonomousSearchStatus.Completed && Directory.GetFiles(output, "*.autonomous.json").Length == 1, "One product main record");
    if (mode == "product-reopen") Require(rays.RayCount == 0, "Repeated completed execution");
    var study = AutonomousDesignStudy.Capture("Product cemented continuation", session.Checkpoint);
    await AutonomousDesignReport.SaveMarkdownAsync(AutonomousDesignReview.Build(study), Path.Combine(output, mode + "-report.md"));
    await Write(Path.Combine(output, mode + ".json"), new { LoadRays = load, RunRays = rays.RayCount, State = session.Checkpoint }); return;
}
throw new ArgumentException("Unknown mode");
async Task Write<T>(string path, T value)
{ Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); await using var file = new FileStream(path, FileMode.CreateNew); await JsonSerializer.SerializeAsync(file, value, json); }
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
