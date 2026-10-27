using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class TopologyReductionBenchmarks
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static async Task<int> VerifyExports(string[] args)
    {
        if (args.Length != 2 || File.Exists(args[1])) throw new ArgumentException("--topology-reduction-export-check <paired runs directory> <new result file>");
        var checks = new List<object>();
        foreach (var seed in new[] { 42, 101 })
            foreach (var group in new[] { "C", "D" })
            {
                var id = $"repair-{seed}-{group}-both";
                var directory = Path.Combine(args[0], id);
                var checkpoint = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(directory, "run.autonomous.json"));
                var candidate = checkpoint.AcceptedArchive.OrderBy(item => item.Topology.Elements.Length).ThenBy(item => item.Score).First();
                using var quota = SequentialTraceMeasurement.BeginCpuBudget(200_000);
                var imported = await AutonomousDesignFiles.ImportSeedAsync(Path.Combine(directory, candidate.Id + ".staropt"), checkpoint.Plan.Constraints);
                var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(imported, checkpoint.Plan.Constraints), checkpoint.Plan.Targets);
                if (!assessment.MeetsTargets || imported.Elements.Length != candidate.Topology.Elements.Length) throw new InvalidDataException("Export re-import failed original targets or element count.");
                checks.Add(new { Run = id, candidate.Id, Imported = imported, Assessment = assessment, AdditionalAuditRays = quota.RayCount });
                Console.WriteLine($"{id}: native export re-import {imported.Elements.Length} elements; all gates passed; additional audit rays={quota.RayCount}");
            }
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(checks, Json));
        return 0;
    }
    public static async Task<int> Compare(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("--topology-reduction-compare <frozen protocol> <M6 evidence> <new output>");
        var directory = Path.GetFullPath(args[2]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use a new output directory.");
        var bytes = await File.ReadAllBytesAsync(args[0]); using var protocol = JsonDocument.Parse(bytes);
        var root = protocol.RootElement;
        if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException("Unsupported reduction repair protocol.");
        Directory.CreateDirectory(directory); await File.WriteAllBytesAsync(Path.Combine(directory, "frozen-protocol.json"), bytes);
        var binaries = new[] { typeof(AutonomousSearchService).Assembly, typeof(AutonomousSearchPlan).Assembly, typeof(OpticalSystemDesign.Core.Optic).Assembly, Assembly.GetExecutingAssembly() }
            .ToDictionary(item => item.GetName().Name!, item => Hash(File.ReadAllBytes(item.Location)));
        var runs = new List<object>();
        foreach (var seed in root.GetProperty("Seeds").EnumerateArray().Select(item => item.GetInt32()))
            foreach (var group in root.GetProperty("Groups").EnumerateArray().Select(item => item.GetString()!))
            {
                var input = Path.Combine(args[1], $"m6-{root.GetProperty("Task").GetString()}-{seed}-{group}", "run.autonomous.json");
                var previous = await new AutonomousSearchCheckpointStore().LoadAsync(input);
                foreach (var mode in root.GetProperty("Modes").EnumerateArray().Select(item => item.GetString()!))
                {
                    var descent = mode is "descent-only" or "both"; var feasibility = mode is "feasibility-only" or "both";
                    var plan = previous.Plan with
                    {
                        Options = previous.Plan.Options with
                        {
                            CheckStageFeasibility = feasibility ? true : null,
                            Local = previous.Plan.Options.Local with { RequireDescent = descent ? true : null },
                            Full = previous.Plan.Options.Full with { RequireDescent = descent ? true : null }
                        }
                    };
                    var id = $"repair-{seed}-{group}-{mode}"; var output = Path.Combine(directory, id); Directory.CreateDirectory(output);
                    await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(output, "task.topology-plan.json"));
                    var store = new AutonomousSearchCheckpointStore(); var path = Path.Combine(output, "run.autonomous.json");
                    var initial = AutonomousSearchService.Create(id, plan) with { Revision = 1 }; await store.SaveAsync(initial, path, 0);
                    var revision = initial.Revision;
                    var endpoint = await Task.Run(() => AutonomousSearchService.Run(initial, state =>
                    { store.SaveAsync(state, path, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision; }));
                    var restored = await store.LoadAsync(path);
                    if (ContentFingerprint.Compute(endpoint) != ContentFingerprint.Compute(restored)) throw new InvalidDataException("Stored endpoint mismatch.");
                    foreach (var candidate in endpoint.AcceptedArchive) await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(output, candidate.Id + ".staropt"));
                    runs.Add(new
                    {
                        Seed = seed,
                        Group = group,
                        Mode = mode,
                        Id = id,
                        SourceRecordSha256 = Hash(await File.ReadAllBytesAsync(input)),
                        SourceFingerprint = ContentFingerprint.Compute(plan.Seeds),
                        endpoint.PlanFingerprint,
                        Status = endpoint.Status.ToString(),
                        endpoint.TrialCursor,
                        endpoint.Consumed,
                        Accepted = endpoint.AcceptedArchive.Length,
                        MinimumAcceptedElements = endpoint.AcceptedArchive.IsEmpty ? (int?)null : endpoint.AcceptedArchive.Min(item => item.Topology.Elements.Length),
                        Archive = endpoint.AcceptedArchive.Select(item => new
                        {
                            item.Id,
                            item.ParentId,
                            Elements = item.Topology.Elements.Length,
                            item.Assessment.EffectiveFocalLengthMillimeters,
                            item.Assessment.FNumber,
                            WorstRmsMillimeters = item.Assessment.Fields.Max(field => field.RmsRadiusMillimeters),
                            MinimumValidFraction = item.Assessment.Fields.Min(field => (double)field.ValidRays / field.AttemptedRays),
                            item.Assessment.OpticFingerprint
                        }).ToArray(),
                        Outcomes = endpoint.Events.GroupBy(item => item.Outcome).ToDictionary(item => item.Key, item => item.Count()),
                        DescentRejections = endpoint.Events.Sum(item => item.Optimization?.Steps.Count(step => !step.Accepted && step.Failure?.StartsWith("Non-descending", StringComparison.Ordinal) == true) ?? 0)
                    });
                    await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new { ProtocolSha256 = Hash(bytes), Binaries = binaries, Runs = runs }, Json));
                    Console.WriteLine($"{id}: {endpoint.Status}; min accepted elements={endpoint.AcceptedArchive.Select(item => item.Topology.Elements.Length).DefaultIfEmpty().Min()}; F/G/rays={endpoint.Consumed.FunctionEvaluations}/{endpoint.Consumed.GradientEvaluations}/{endpoint.Consumed.TracedRays}");
                }
            }
        return 0;
        static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    }
    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-reduction-audit <M6 evidence directory> <new audit directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        foreach (var seed in new[] { 42, 101 })
            foreach (var group in new[] { "C", "D" })
            {
                var id = $"m6-m2-endpoint-{seed}-{group}";
                var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(args[0], id, "run.autonomous.json"));
                foreach (var item in state.Events.Where(item => item.Endpoint is not null && item.Operation is "delete" or "gradual-delete"
                    && item.Phase is AutonomousSearchPhase.Recovery or AutonomousSearchPhase.Local or AutonomousSearchPhase.Full))
                {
                    var constraints = item.GradualDeletion is { } gradual ? TopologyOperators.DeletionConstraints(gradual, state.Plan.Constraints, state.Plan.Targets) : state.Plan.Constraints;
                    using var quota = SequentialTraceMeasurement.BeginCpuBudget(200_000);
                    var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(item.Endpoint!, constraints), state.Plan.Targets);
                    results.Add(new
                    {
                        Run = id,
                        item.Sequence,
                        item.TrialId,
                        item.ParentId,
                        item.Operation,
                        Phase = item.Phase.ToString(),
                        item.Outcome,
                        item.Endpoint,
                        item.GradualDeletion,
                        item.Recovery,
                        item.Optimization,
                        Assessment = assessment,
                        AuditRays = quota.RayCount
                    });
                    Console.WriteLine($"{id}/{item.TrialId}/{item.Phase}: f={assessment.EffectiveFocalLengthMillimeters:F6}; RMS={assessment.Fields.Select(field => field.RmsRadiusMillimeters).DefaultIfEmpty().Max() * 1000:F4}um; pass={assessment.MeetsTargets}; gates={string.Join(',', assessment.Violations.Select(item => item.Code).Distinct())}");
                }
            }
        await File.WriteAllTextAsync(Path.Combine(directory, "phase-audit.json"), JsonSerializer.Serialize(results, Json));
        return 0;
    }
}
