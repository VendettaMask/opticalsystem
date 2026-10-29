using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, Converters = { new JsonStringEnumConverter() } };
if (args.Length == 3 && args[0] == "--audit-preparation")
{
    using var file = JsonDocument.Parse(await File.ReadAllBytesAsync(args[1]));
    var result = file.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
    using var measured = SequentialTraceMeasurement.BeginCpuBudget(1);
    var metrics = new List<object>(); var requests = 0;
    foreach (var attempt in result.Attempts.Where(attempt => attempt.Endpoint is not null))
    {
        var source = TopologyCompiler.Compile(attempt.Endpoint!, result.SourcePlan.Constraints);
        var optic = source.CreateOptic();
        requests++; var focal = optic.Paraxial.EstimateEffectiveFocalLength();
        requests++; var fNumber = optic.Paraxial.EstimateFNumber();
        requests++; var a = optic.Paraxial.GetTransferMatrix(source.SurfaceIndices[source.Topology.Reference.Id], source.SurfaceIndices[source.Topology.Image.Id]).A;
        metrics.Add(new { attempt.Index, attempt.Outcome, attempt.Score, FocalMillimeters = focal, FNumber = fNumber, FocusA = a,
            FocalRelativeError = focal / result.SourcePlan.Targets.EffectiveFocalLengthMillimeters - 1,
            FNumberRelativeError = fNumber / result.SourcePlan.Targets.FNumber - 1,
            FocusPassed = Math.Abs(a) <= result.Options.FocusATolerance,
            FocalPassed = Math.Abs(focal / result.SourcePlan.Targets.EffectiveFocalLengthMillimeters - 1) <= result.SourcePlan.Targets.FocalLengthRelativeTolerance,
            FNumberPassed = Math.Abs(fNumber / result.SourcePlan.Targets.FNumber - 1) <= result.SourcePlan.Targets.FNumberRelativeTolerance });
    }
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new { AdditionalFirstOrderMetricRequests = requests,
        AdditionalTracedRays = measured.RayCount, Scope = "Read-only post-run first-order diagnosis, not optimization work", Metrics = metrics }, json));
    return;
}
if (args.Length != 2) throw new ArgumentException("<frozen protocol.json> <new evidence directory>");
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
Directory.CreateDirectory(output);
var protocolBytes = await File.ReadAllBytesAsync(args[0]);
using var protocol = JsonDocument.Parse(protocolBytes);
var root = protocol.RootElement;
if (root.TryGetProperty("PriorityClass", out var priority))
{
    if (!Enum.TryParse<ProcessPriorityClass>(priority.GetString(), out var selectedPriority)
        || selectedPriority is not (ProcessPriorityClass.Normal or ProcessPriorityClass.BelowNormal))
        throw new InvalidDataException("Only bounded background Normal/BelowNormal diagnostic priority is supported.");
    Process.GetCurrentProcess().PriorityClass = selectedPriority;
}
var sourcePath = root.GetProperty("SourcePlan").GetString()!;
if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))) != root.GetProperty("SourceSha256").GetString())
    throw new InvalidDataException("The frozen original task changed.");
var original = await AutonomousDesignFiles.LoadPlanAsync(sourcePath);
var common = root.GetProperty("TotalBudget").Deserialize<AutonomousSearchOptions>(json)!;
if (original.Options.MaximumFunctionEvaluations != common.MaximumFunctionEvaluations
    || original.Options.MaximumGradientEvaluations != common.MaximumGradientEvaluations
    || original.Options.MaximumTracedRays != common.MaximumTracedRays || original.Options.TimeLimit != common.TimeLimit
    || original.Options.MaximumStageTracedRays != common.MaximumStageTracedRays
    || original.Options.FinalValidationRayReserve != common.FinalValidationRayReserve)
    throw new InvalidDataException("Comparison envelope differs from the original budget.");
var preparationOptions = root.GetProperty("Preparation").Deserialize<ParaxialSeedSearchOptions>(json)!;
var persistenceReserve = root.TryGetProperty("PersistenceReserveSeconds", out var reserve)
    ? TimeSpan.FromSeconds(reserve.GetDouble()) : TimeSpan.Zero;
if (persistenceReserve < TimeSpan.Zero || persistenceReserve >= common.TimeLimit / 2)
    throw new InvalidDataException("Invalid persistence wall-time reserve.");
await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), protocolBytes);
await File.WriteAllBytesAsync(Path.Combine(output, "original-task.topology-plan.json"), await File.ReadAllBytesAsync(sourcePath));
await Write("runtime.json", new { Runtime = Environment.Version.ToString(), Environment.ProcessorCount,
    PriorityClass = Process.GetCurrentProcess().PriorityClass.ToString(),
    Assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name!.StartsWith("OpticalSystemDesign"))
        .Select(a => new { a.GetName().Name, Path = a.Location, Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(a.Location))) }).ToArray() });
var summaries = new List<object>();
foreach (var mode in root.GetProperty("Order").EnumerateArray().Select(value => value.GetString()!))
{
    var directory = Path.Combine(output, mode); Directory.CreateDirectory(directory);
    Console.WriteLine("START " + mode);
    var timer = Stopwatch.StartNew();
    ParaxialSeedSearchResult? preparation = null;
    var plan = original;
    var sourceFingerprint = ContentFingerprint.Compute(original);
    if (mode is "delano-population" or "bounded-candidates" or "thick-delano" or "image-ranked")
    {
        var bounded = mode is "bounded-candidates" or "thick-delano" or "image-ranked";
        var selectedPreparation = mode == "image-ranked" ? root.GetProperty("ImagePreparation").Deserialize<ParaxialSeedSearchOptions>(json)!
            : mode == "thick-delano" ? root.GetProperty("ThickPreparation").Deserialize<ParaxialSeedSearchOptions>(json)!
            : bounded ? root.GetProperty("ImprovedPreparation").Deserialize<ParaxialSeedSearchOptions>(json)! : preparationOptions;
        preparation = ParaxialSeedSearch.Run(original, selectedPreparation);
        await ParaxialSeedSearchFiles.SaveAsync(preparation, Path.Combine(directory, "preparation.seed-search.json"));
        // Same explicit fallback as the desktop: a failed preparation retains the original seed.
        plan = original with { Seeds = preparation.Seeds.IsEmpty ? original.Seeds : bounded
                ? original.Seeds.Concat(preparation.Seeds).DistinctBy(s => ContentFingerprint.Compute(s with { Name = "seed" })).Take(8).ToImmutableArray() : preparation.Seeds,
            Options = original.Options with { PopulationExploration = new() { Geometry = bounded ? new() : null }, PoolDiversity = new(),
                SeedPreparation = selectedPreparation,
                BoundedSearch = bounded ? root.GetProperty("BoundedPolicy").Deserialize<BoundedCandidateSearchOptions>(json)! : null } };
    }
    var preparationWork = preparation?.Work ?? new AutonomousSearchWork();
    var remaining = common.TimeLimit - timer.Elapsed - persistenceReserve;
    if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("Preparation consumed the complete original wall-clock envelope.");
    plan = plan with { Options = plan.Options with
    {
        MaximumFunctionEvaluations = common.MaximumFunctionEvaluations - preparationWork.FunctionEvaluations,
        MaximumGradientEvaluations = common.MaximumGradientEvaluations - preparationWork.GradientEvaluations,
        MaximumTracedRays = common.MaximumTracedRays - preparationWork.TracedRays,
        TimeLimit = remaining
    } };
    if (ContentFingerprint.Compute(original) != sourceFingerprint || plan.Targets != original.Targets
        || ContentFingerprint.Compute(plan.Constraints) != ContentFingerprint.Compute(original.Constraints))
        throw new InvalidDataException("The original specification was changed.");
    foreach (var seed in plan.Seeds)
    {
        var source = original.Seeds[0];
        if (seed.Reference != source.Reference || seed.Stop != source.Stop || seed.Image != source.Image
            || ContentFingerprint.Compute(seed.Fields) != ContentFingerprint.Compute(source.Fields)
            || ContentFingerprint.Compute(seed.Wavelengths) != ContentFingerprint.Compute(source.Wavelengths)
            || !seed.Elements.Select(e => (e.Material, e.Front.ClearSemiDiameterMillimeters, e.Front.MechanicalSemiDiameterMillimeters,
                e.Back.ClearSemiDiameterMillimeters, e.Back.MechanicalSemiDiameterMillimeters)).SequenceEqual(
                source.Elements.Select(e => (e.Material, e.Front.ClearSemiDiameterMillimeters, e.Front.MechanicalSemiDiameterMillimeters,
                    e.Back.ClearSemiDiameterMillimeters, e.Back.MechanicalSemiDiameterMillimeters))))
            throw new InvalidDataException("Generation changed the original physical aperture/material/task envelope.");
    }
    await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(directory, "search.topology-plan.json"));
    var store = new AutonomousSearchCheckpointStore();
    var state = AutonomousSearchService.Run(AutonomousSearchService.Create("wide21-" + mode, plan), current =>
        store.SaveAsync(current, Path.Combine(directory, "run.autonomous.json"), current.Revision - 1).AsTask().GetAwaiter().GetResult());
    var searchWall = timer.Elapsed;
    var best = plan.Options.BoundedSearch is null
        ? state.Candidates.OrderByDescending(c => c.Assessment.MeetsTargets).ThenBy(c => c.Score).FirstOrDefault()
        : state.AcceptedArchive.OrderBy(c => c, Comparer<AutonomousSearchCandidate>.Create(AutonomousSearchService.CompareQualified)).FirstOrDefault();
    var last = state.Events.LastOrDefault(e => e.Assessment is not null)?.Assessment;
    var initial = state.Candidates.Where(c => c.ParentId is null).ToArray();
    var endpoints = state.Events.Where(e => e.Endpoint is not null).Select(e => new
    { e.TrialId, e.Operation, e.Phase, e.Outcome, e.Detail, Elements = e.Endpoint!.Elements.Length,
        AcceptedUpdates = e.Optimization?.AcceptedUpdates, Assessment = e.Assessment is null ? null : Metrics(e.Assessment) }).ToArray();
    if (best is not null) await AutonomousDesignFiles.ExportAsync(best, plan.Constraints, Path.Combine(directory, "best-retained.staropt"));
    foreach (var candidate in state.Candidates.Where(c => c.ParentId is not null || c.Id != "seed-0"))
        await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(directory, candidate.Id + (candidate.Assessment.MeetsTargets ? "-qualified" : "-unqualified") + ".staropt"));
    var functions = preparationWork.FunctionEvaluations + state.Consumed.FunctionEvaluations;
    var gradients = preparationWork.GradientEvaluations + state.Consumed.GradientEvaluations;
    var rays = preparationWork.TracedRays + state.Consumed.TracedRays;
    if (functions > common.MaximumFunctionEvaluations || gradients > common.MaximumGradientEvaluations || rays > common.MaximumTracedRays)
        throw new InvalidOperationException("End-to-end work exceeded the original envelope.");
    var summary = new { Mode = mode, SourcePlanFingerprint = sourceFingerprint,
        Preparation = preparation is null ? null : new { preparation.Termination, Retained = preparation.Seeds.Length,
            FallbackToOriginal = preparation.Seeds.IsEmpty, preparation.Work, preparation.DiagramEvaluations, preparation.ImageScreens,
            Outcomes = preparation.Attempts.Select(a => new { a.Index, a.Outcome, a.FunctionEvaluations, a.Score }).ToArray() },
        InitialQuality = initial.Select(c => new { c.Id, Elements = c.Topology.Elements.Length, Assessment = Metrics(c.Assessment) }).ToArray(),
        BestRetained = best is null ? null : new { best.Id, Elements = best.Topology.Elements.Length, Assessment = Metrics(best.Assessment) },
        LastAssessment = last is null ? null : Metrics(last), Endpoints = endpoints,
        SearchWork = state.Consumed, CombinedFunctions = functions, CombinedGradients = gradients, CombinedRays = rays,
        ComputeAndSaveWallSeconds = searchWall.TotalSeconds, WallLimitSeconds = common.TimeLimit.TotalSeconds,
        PersistenceReserveSeconds = persistenceReserve.TotalSeconds,
        WithinWallLimit = searchWall <= common.TimeLimit, state.Status, state.TrialCursor,
        PopulationOperations = state.Events.Where(e => e.Operation.StartsWith("population-", StringComparison.Ordinal)).Select(e => new { e.Operation, e.Phase, e.Outcome }).ToArray(),
        QualifiedCandidates = state.AcceptedArchive.Length,
        BoundedConclusion = plan.Options.BoundedSearch is null ? null : AutonomousSearchService.BoundedSummary(state) };
    await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, json));
    summaries.Add(summary); await Write("summary.json", summaries);
    Console.WriteLine("DONE " + mode + " " + state.Status + " rays=" + rays);
}

object Metrics(TopologyAssessment a) => new { a.Completed, a.MeetsTargets,
    a.EffectiveFocalLengthMillimeters, a.FNumber, Fields = a.Fields.Length,
    RmsMicrometers = a.Fields.IsEmpty ? (double?)null : a.Fields.Max(f => f.RmsRadiusMillimeters) * 1000,
    MaximumMicrometers = a.Fields.IsEmpty ? (double?)null : a.Fields.Max(f => f.MaximumRadiusMillimeters) * 1000,
    DistortionPercent = a.Fields.IsEmpty ? (double?)null : a.Fields.Max(f => Math.Abs(f.DistortionFraction ?? double.NaN)) * 100,
    MinimumIntegrationValid = a.Fields.IsEmpty ? (int?)null : a.Fields.Min(f => f.ValidRays),
    MinimumChecksValid = a.Fields.IsEmpty ? (int?)null : a.Fields.Min(f => f.CheckValidRays),
    a.Violations, a.Failure, a.TracedRayCount };

Task Write(string name, object value) => File.WriteAllTextAsync(Path.Combine(output, name), JsonSerializer.Serialize(value, json));
