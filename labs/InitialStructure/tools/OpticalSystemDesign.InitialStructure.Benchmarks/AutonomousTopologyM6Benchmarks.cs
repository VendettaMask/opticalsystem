using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class AutonomousTopologyM6Benchmarks
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-m6 <frozen comparison-m6.json> <new output directory>");
        var protocolPath = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        var protocolBytes = await File.ReadAllBytesAsync(protocolPath);
        using var protocol = JsonDocument.Parse(protocolBytes);
        var root = protocol.RootElement;
        if (root.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException("Unsupported M6 protocol.");
        Directory.CreateDirectory(output); await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), protocolBytes);
        var protocolSha = Convert.ToHexString(SHA256.HashData(protocolBytes)).ToLowerInvariant();
        var binaries = new[] { typeof(AutonomousSearchService).Assembly, typeof(AutonomousSearchPlan).Assembly, typeof(OpticalSystemDesign.Core.Optic).Assembly, Assembly.GetExecutingAssembly() }
            .ToDictionary(item => item.GetName().Name!, item => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Location))).ToLowerInvariant());
        var runs = new List<object>();
        foreach (var task in root.GetProperty("Tasks").EnumerateArray())
            foreach (var seed in root.GetProperty("Seeds").EnumerateArray().Select(item => item.GetInt64()))
            {
                var source = task.GetProperty("Source").Deserialize<DesignTopology>(Json)!;
                var random = (ulong)seed;
                double Factor() { random ^= random << 13; random ^= random >> 7; random ^= random << 17; return 1 + ((random >> 11) * (1.0 / (1UL << 53)) * 2 - 1) * .001; }
                source = source with
                {
                    Elements = source.Elements.Select(element => element with
                    {
                        Front = element.Front with { Profile = element.Front.Profile with { CurvaturePerMillimeter = element.Front.Profile.CurvaturePerMillimeter * Factor() } },
                        Back = element.Back with { Profile = element.Back.Profile with { CurvaturePerMillimeter = element.Back.Profile.CurvaturePerMillimeter * Factor() } }
                    }).ToImmutableArray()
                };
                var budget = root.GetProperty("Budget");
                var sampling = root.GetProperty("TrainingSampling").Deserialize<TopologyTrainingSampling>(Json)!;
                var stage = new FixedTopologyOptions { ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false, Sampling = sampling };
                foreach (var group in root.GetProperty("Groups").EnumerateArray().Select(item => item.GetString()!))
                {
                    var plan = new AutonomousSearchPlan
                    {
                        Seeds = [source],
                        Options = new()
                        {
                            Seed = seed,
                            MaximumTrials = budget.GetProperty("MaximumTrials").GetInt32(),
                            MaximumFunctionEvaluations = budget.GetProperty("MaximumFunctionEvaluations").GetInt32(),
                            MaximumGradientEvaluations = budget.GetProperty("MaximumGradientEvaluations").GetInt32(),
                            MaximumTracedRays = budget.GetProperty("MaximumTracedRays").GetInt64(),
                            MaximumStageTracedRays = budget.GetProperty("MaximumStageTracedRays").GetInt64(),
                            FinalValidationRayReserve = budget.GetProperty("FinalValidationRayReserve").GetInt64(),
                            TimeLimit = TimeSpan.FromSeconds(budget.GetProperty("TimeLimitSeconds").GetInt32()),
                            FixedTopologyOnly = group == "A" ? true : null,
                            SkipParaxialRecovery = group == "B" ? true : null,
                            Expansion = new() { EnableInsertion = group != "A", EnableDeletion = group != "A", EnableGradualDeletion = group == "D", EnableSplit = false, EnableMerge = false, EnableMaterial = false, EnableProfile = false },
                            Recovery = new() { MaximumFunctionEvaluations = root.GetProperty("RecoveryFunctions").GetInt32(), ProfileRadialSamples = sampling.ProfileRadialSamples },
                            Local = Stage(root.GetProperty("LocalUpdates").GetInt32()),
                            Full = Stage(root.GetProperty("FullUpdates").GetInt32())
                        }
                    };
                    var id = $"m6-{task.GetProperty("Id").GetString()}-{seed}-{group}";
                    var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
                    await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(directory, "task.topology-plan.json"));
                    var store = new AutonomousSearchCheckpointStore(); var record = Path.Combine(directory, "run.autonomous.json");
                    var initial = AutonomousSearchService.Create(id, plan) with { Revision = 1 };
                    await store.SaveAsync(initial, record, 0); var revision = initial.Revision;
                    var endpoint = await Task.Run(() => AutonomousSearchService.Run(initial, state =>
                    { store.SaveAsync(state, record, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision; }));
                    var loaded = await store.LoadAsync(record);
                    if (ContentFingerprint.Compute(loaded) != ContentFingerprint.Compute(endpoint)) throw new InvalidDataException("Saved endpoint mismatch.");
                    foreach (var candidate in endpoint.AcceptedArchive) await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(directory, candidate.Id + ".staropt"));
                    var best = endpoint.Candidates.Where(item => item.Assessment.Completed).OrderByDescending(item => item.Assessment.MeetsTargets).ThenBy(item => item.Score).FirstOrDefault();
                    var fields = best?.Assessment.Fields ?? [];
                    runs.Add(new
                    {
                        Id = id,
                        Task = task.GetProperty("Id").GetString(),
                        Seed = seed,
                        Group = group,
                        SourceFingerprint = ContentFingerprint.Compute(source),
                        endpoint.PlanFingerprint,
                        Status = endpoint.Status.ToString(),
                        endpoint.TrialCursor,
                        endpoint.Consumed,
                        Accepted = endpoint.AcceptedArchive.Length,
                        MinimumAcceptedElements = endpoint.AcceptedArchive.IsEmpty ? (int?)null : endpoint.AcceptedArchive.Min(item => item.Topology.Elements.Length),
                        Best = best is null ? null : new
                        {
                            best.Id,
                            Elements = best.Topology.Elements.Length,
                            best.Assessment.MeetsTargets,
                            best.Assessment.EffectiveFocalLengthMillimeters,
                            best.Assessment.FNumber,
                            WorstRmsMillimeters = fields.IsEmpty || fields.Any(item => item.RmsRadiusMillimeters is null) ? null : fields.Max(item => item.RmsRadiusMillimeters),
                            MinimumValidFraction = fields.IsEmpty ? (double?)null : fields.Min(item => (double)item.ValidRays / item.AttemptedRays),
                            best.Assessment.Violations
                        },
                        Outcomes = endpoint.Events.GroupBy(item => item.Outcome).ToDictionary(item => item.Key, item => item.Count()),
                        RecoveryAttempts = endpoint.Events.Count(item => item.Recovery is not null),
                        ProgressiveEvents = endpoint.Events.Count(item => item.GradualDeletion is not null),
                        CandidateCount = endpoint.Candidates.Length,
                        ArchiveIds = endpoint.AcceptedArchive.Select(item => item.Id).ToArray()
                    });
                    await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { ProtocolSha256 = protocolSha, Binaries = binaries, Runs = runs }, Json));
                    Console.WriteLine($"{id}: {endpoint.Status}; archive={endpoint.AcceptedArchive.Length}; F={endpoint.Consumed.FunctionEvaluations}; G={endpoint.Consumed.GradientEvaluations}; rays={endpoint.Consumed.TracedRays}; sec={endpoint.Consumed.Elapsed.TotalSeconds:F2}");
                }
                FixedTopologyOptions Stage(int updates) => stage with { MaximumUpdates = updates, MaximumFunctionEvaluations = updates + 1, MaximumGradientEvaluations = updates + 1 };
            }
        return 0;
    }
}
