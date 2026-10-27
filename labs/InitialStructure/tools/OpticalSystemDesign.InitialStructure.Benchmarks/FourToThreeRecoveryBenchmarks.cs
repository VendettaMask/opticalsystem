using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class FourToThreeRecoveryBenchmarks
{
    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--four-to-three-audit <evidence root> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var rows = new List<object>(); var seen = new Dictionary<string, string>();
        foreach (var batch in new[] { "paired-runs", "pupil-runs", "conic-runs", "allocation-runs" })
        {
            var root = Path.Combine(Path.GetFullPath(args[0]), batch);
            using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json")));
            foreach (var row in summary.RootElement.GetProperty("Runs").EnumerateArray().Where(row => row.GetProperty("Seed").GetInt64() == 42 && row.GetProperty("Group").GetString() == "C"
                || row.TryGetProperty("ReducedSuccess", out var reduced) && reduced.GetBoolean()))
            {
                var id = row.GetProperty("Id").GetString()!; var directory = Path.Combine(root, id);
                var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(directory, "run.autonomous.json"));
                var candidate = state.Candidates.Where(candidate => candidate.Topology.Elements.Length == 3 && candidate.Assessment.Completed)
                    .OrderByDescending(candidate => candidate.Assessment.MeetsTargets).ThenBy(candidate => candidate.Score).FirstOrDefault();
                if (candidate is null) { rows.Add(new { Id = id, Available = false, Failure = "No completed three-element physical endpoint." }); continue; }
                var fingerprint = candidate.Assessment.OpticFingerprint;
                if (seen.TryGetValue(fingerprint, out var previous)) { rows.Add(new { Id = id, Reuses = previous, AdditionalAuditRays = 0 }); continue; }
                seen.Add(fingerprint, id);
                var path = Path.Combine(directory, candidate.Assessment.MeetsTargets ? candidate.Id + ".staropt" : "best-failed.staropt");
                using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
                var imported = await AutonomousDesignFiles.ImportSeedAsync(path, state.Plan.Constraints);
                var actual = TopologyValidator.Validate(TopologyCompiler.Compile(imported, state.Plan.Constraints), state.Plan.Targets);
                var expected = candidate.Assessment;
                var matched = actual.Completed && actual.MeetsTargets == expected.MeetsTargets && actual.ProtocolFingerprint == expected.ProtocolFingerprint
                    && actual.Fields.Length == expected.Fields.Length && Equal(actual.EffectiveFocalLengthMillimeters, expected.EffectiveFocalLengthMillimeters, 1e-9)
                    && Equal(actual.FNumber, expected.FNumber, 1e-9)
                    && actual.Fields.Zip(expected.Fields, (a, b) => a.ValidRays == b.ValidRays && a.CheckValidRays == b.CheckValidRays
                        && Equal(a.RmsRadiusMillimeters, b.RmsRadiusMillimeters, 1e-10) && Equal(a.MaximumRadiusMillimeters, b.MaximumRadiusMillimeters, 1e-10)
                        && Equal(a.DistortionFraction, b.DistortionFraction, 1e-12)).All(value => value);
                rows.Add(new
                {
                    Id = id,
                    Available = actual.Completed,
                    NativeFile = path,
                    MatchesRecordedPhysicalEndpoint = matched,
                    RecordedAssessment = expected,
                    Assessment = actual,
                    AdditionalAuditRays = quota.RayCount
                });
                await File.WriteAllTextAsync(Path.Combine(output, "native-audits.json"), JsonSerializer.Serialize(rows, TopologySamplingBenchmarks.Json));
                if (!matched) throw new InvalidDataException("Native three-element replay changed the recorded physical endpoint: " + id);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "native-audits.json"), JsonSerializer.Serialize(rows, TopologySamplingBenchmarks.Json));
        return 0;
        static bool Equal(double? a, double? b, double tolerance) => a is { } x && b is { } y ? Math.Abs(x - y) <= tolerance : a is null && b is null;
    }

    public static async Task<int> FilterAudit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--four-to-three-filter <checkpoint> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var full = state.Events.First(item => item.Phase == AutonomousSearchPhase.Full && item.Optimization is not null);
        var local = state.Events.Last(item => item.TrialId == full.TrialId && item.Phase == AutonomousSearchPhase.Local && item.Optimization is not null);
        var source = TopologyCompiler.Compile(local.Endpoint!, state.Plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, full.Optimization!.Variables);
        var objective = new FixedTopologyObjective(source, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling);
        var before = full.Optimization.EndpointEvaluation!;
        var current = full.Optimization.Continuation!.Adam.Coordinates;
        var policy = new TopologyTransmissionCompensation(objective.ResidualIds, 4);
        var constraints = Enumerable.Range(0, objective.ResidualIds.Length).Where(row => !objective.ResidualIds[row].EndsWith(":x", StringComparison.Ordinal)
            && !objective.ResidualIds[row].EndsWith(":y", StringComparison.Ordinal)).ToArray();
        var rows = new List<object>();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(500_000);
        foreach (var fraction in new[] { 1.0, .5, .25, .125, .0625, .03125, .015625, .0078125 })
        {
            var proposal = policy.Propose(before, current, binding, Enumerable.Range(0, binding.Slots.Length).ToArray(), fraction);
            var after = objective.Evaluate(proposal.Coordinates, includeConstraintTangents: true);
            if (!after.Available) { rows.Add(new { Fraction = fraction, Available = false, after.Failure }); continue; }
            var a = policy.Merit(before); var b = policy.Merit(after);
            var blockers = constraints.Where(row => Violation(after, row) > Math.Max(Violation(before, row), 1e-8))
                .Select(row => new { Id = objective.ResidualIds[row], Before = Violation(before, row), After = Violation(after, row), PreviouslySatisfied = Violation(before, row) <= 1e-8 }).ToArray();
            var overallDescent = a.Other > 1e-16 ? b.Other < a.Other && b.Clearance <= Math.Max(a.Clearance, 1e-18) : b.Other <= 1e-16 && b.Clearance < a.Clearance;
            rows.Add(new
            {
                Fraction = fraction,
                Available = true,
                AcceptedV4 = policy.Accept(before, after),
                OverallDescent = overallDescent,
                BeforeOther = a.Other,
                AfterOther = b.Other,
                BeforeClearance = a.Clearance,
                AfterClearance = b.Clearance,
                Blockers = blockers
            });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "filter-audit.json"), JsonSerializer.Serialize(new { ActualDiagnosticRays = quota.RayCount, Rows = rows }, TopologySamplingBenchmarks.Json));
        return 0;

        double Violation(TopologyObjectiveEvaluation value, int row) => row < 2 ? Math.Max(0, Math.Abs(value.Residuals[row]) - .95)
            : objective.ResidualIds[row].EndsWith(":rms", StringComparison.Ordinal) ? Math.Max(0, value.ConstraintTangents!.Value.Single(item => item.Row == row).Value + .02) : value.Residuals[row];
    }

    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--four-to-three-compare <frozen protocol> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var bytes = await File.ReadAllBytesAsync(args[0]); using var protocol = JsonDocument.Parse(bytes); var root = protocol.RootElement;
        var sourcePath = root.GetProperty("SourcePlan").GetString()!;
        if (Hash(await File.ReadAllBytesAsync(sourcePath)) != root.GetProperty("SourcePlanSha256").GetString()
            || Hash(await File.ReadAllBytesAsync(root.GetProperty("SourceNative").GetString()!)) != root.GetProperty("SourceNativeSha256").GetString())
            throw new InvalidDataException("Frozen qualified four-element source changed.");
        var source = await AutonomousDesignFiles.LoadPlanAsync(sourcePath);
        if (source.Options.MaximumTrials != 6 || source.Options.MaximumTracedRays != 2_000_000 || source.Options.MaximumFunctionEvaluations != 600
            || source.Options.MaximumGradientEvaluations != 100 || source.Options.TimeLimit != TimeSpan.FromSeconds(60)
            || source.Options.MaximumStageTracedRays != 400_000 || source.Options.FinalValidationRayReserve != 800_000
            || source.Options.Recovery.MaximumFunctionEvaluations != 150 || source.Options.Full.MaximumUpdates != 8)
            throw new InvalidDataException("Original short-run caps changed.");
        await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        var binaries = new[] { typeof(ParaxialRestorer).Assembly, typeof(DifferentiableParaxialMetrics).Assembly, typeof(AutonomousSearchPlan).Assembly,
            typeof(FourToThreeRecoveryBenchmarks).Assembly }.ToDictionary(assembly => assembly.GetName().Name!, assembly => Hash(File.ReadAllBytes(assembly.Location)));
        var rows = new List<object>();
        foreach (var seed in root.GetProperty("Seeds").EnumerateArray().Select(item => item.GetInt64()))
            foreach (var group in root.GetProperty("Groups").EnumerateArray().Select(item => item.GetString()!))
                foreach (var mode in root.GetProperty("Modes").EnumerateArray().Select(item => item.GetString()!))
                {
                    var plan = source with
                    {
                        Options = source.Options with
                        {
                            Seed = seed,
                            ProtectPostRecoveryRayBudget = mode == "control" ? null : true,
                            UseAxialVariablesInRecovery = mode is "axial" or "pupil" or "conic" or "allocated" ? true : null,
                            CompensateConic = mode is "conic" or "allocated" ? true : null,
                            Recovery = source.Options.Recovery with
                            {
                                UseAutomaticMatrixDerivatives = mode is "automatic" or "axial" or "pupil" or "conic" or "allocated" ? true : null,
                                RestorePupilMagnification = mode is "pupil" or "conic" or "allocated" ? true : null
                            },
                            Full = mode == "allocated" ? source.Options.Full with { MaximumUpdates = 99, MaximumFunctionEvaluations = 100, MaximumGradientEvaluations = 100 } : source.Options.Full,
                            Expansion = source.Options.Expansion! with { EnableGradualDeletion = group == "D" }
                        }
                    };
                    var id = $"four-to-three-{seed}-{group}-{mode}"; var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "run.autonomous.json"); var store = new AutonomousSearchCheckpointStore();
                    await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(directory, "task.topology-plan.json"));
                    var initial = AutonomousSearchService.Create(id, plan) with { Revision = 1 }; await store.SaveAsync(initial, path, 0); var revision = initial.Revision;
                    try
                    {
                        var result = AutonomousSearchService.Run(initial, state => { store.SaveAsync(state, path, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision; });
                        if (ContentFingerprint.Compute(result) != ContentFingerprint.Compute(await store.LoadAsync(path))) throw new InvalidDataException("Stored endpoint changed.");
                        foreach (var candidate in result.AcceptedArchive) await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints, Path.Combine(directory, candidate.Id + ".staropt"));
                        var failed = result.Candidates.Where(candidate => !candidate.Assessment.MeetsTargets).OrderBy(candidate => candidate.Score).FirstOrDefault();
                        if (failed is not null) await AutonomousDesignFiles.ExportAsync(failed, plan.Constraints, Path.Combine(directory, "best-failed.staropt"));
                        rows.Add(new
                        {
                            Id = id,
                            Seed = seed,
                            Group = group,
                            Mode = mode,
                            Status = result.Status.ToString(),
                            result.Consumed,
                            PendingPhase = result.Pending?.Phase.ToString(),
                            result.TrialCursor,
                            ReducedSuccess = result.AcceptedArchive.Any(candidate => candidate.Topology.Elements.Length < 4),
                            Archive = result.AcceptedArchive.Select(candidate => new { candidate.Id, Elements = candidate.Topology.Elements.Length, candidate.Assessment }).ToArray(),
                            FailedCandidates = result.Candidates.Where(candidate => !candidate.Assessment.MeetsTargets).Select(candidate => new { candidate.Id, Elements = candidate.Topology.Elements.Length, candidate.Assessment }).ToArray(),
                            Events = result.Events.Select(item => new
                            {
                                item.Sequence,
                                item.TrialId,
                                item.Operation,
                                item.Phase,
                                item.Outcome,
                                item.Detail,
                                item.Work,
                                Recovery = item.Recovery is { } recovery ? new
                                {
                                    recovery.Termination,
                                    recovery.EndpointAError,
                                    recovery.EndpointFocalCError,
                                    recovery.InitialPupilError,
                                    recovery.EndpointPupilError,
                                    recovery.Work,
                                    recovery.Diagnostics
                                } : null
                            }).ToArray()
                        });
                        Console.WriteLine($"{id}: {result.Status}; reduced={result.AcceptedArchive.Any(candidate => candidate.Topology.Elements.Length < 4)}; F/G/rays={result.Consumed.FunctionEvaluations}/{result.Consumed.GradientEvaluations}/{result.Consumed.TracedRays}");
                    }
                    catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or ArithmeticException)
                    { rows.Add(new { Id = id, Seed = seed, Group = group, Mode = mode, Status = "Failed", Failure = error.ToString() }); Console.WriteLine($"{id}: FAILED {error.Message}"); }
                    await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
                    {
                        ProtocolSha256 = Hash(bytes),
                        Binaries = binaries,
                        CpuProcessors = Environment.ProcessorCount,
                        Priority = Process.GetCurrentProcess().PriorityClass.ToString(),
                        Runs = rows
                    }, TopologySamplingBenchmarks.Json));
                }
        return 0;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static async Task<int> Prepare(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--four-to-three-prepare <qualified native> <new evidence directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var previous = await new AutonomousSearchCheckpointStore().LoadAsync("validation/rechecks/2026-10-27/wide21-gates-repair/paired-runs/repr-new-wide4-3-42-C-joint/run.autonomous.json");
        var topology = await AutonomousDesignFiles.ImportSeedAsync(args[0], previous.Plan.Constraints);
        var source = TopologyCompiler.Compile(topology, previous.Plan.Constraints);
        TopologyAssessment assessment;
        using var measurement = SequentialTraceMeasurement.BeginCpuBudget(800_000);
        assessment = TopologyValidator.Validate(source, previous.Plan.Targets);
        if (!assessment.MeetsTargets || topology.Elements.Length != 4) throw new InvalidDataException("The exact requested four-element native source must pass the original targets.");
        var plan = previous.Plan with
        {
            Seeds = [topology],
            Options = previous.Plan.Options with
            { Expansion = previous.Plan.Options.Expansion! with { EnableInsertion = false, EnableGradualDeletion = false } }
        };
        await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(output, "baseline.topology-plan.json"));
        var diagnostics = new List<object>();
        foreach (var element in topology.Elements)
        {
            var edited = TopologyOperators.Delete(topology, element.Id, plan.Constraints);
            if (!edited.Succeeded) { diagnostics.Add(new { Removed = element.Id, edited.Failure }); continue; }
            var child = TopologyCompiler.Compile(edited.Topology!, plan.Constraints);
            var bound = plan.Options.CurvatureBoundPerMillimeter;
            var variables = child.Topology.Elements.Where(item => !item.Protected).SelectMany(item => new[] { (item.FrontSurfaceId, item.Front), (item.BackSurfaceId, item.Back) })
                .Where(face => face.Item2.Profile.Kind != TopologyProfileKind.Plane)
                .Select(face => new TopologyVariableRequest(face.Item1, OpticalDesignParameterKind.Curvature, -bound, bound, .01)).ToArray();
            var recovery = ParaxialRestorer.Run(source, child, variables, plan.Targets, plan.Options.Recovery);
            diagnostics.Add(new { Removed = element.Id, Change = edited.Change, Recovery = recovery });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "preparation.json"), JsonSerializer.Serialize(new
        {
            SourceNative = Path.GetFullPath(args[0]),
            NativeSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(args[0]))).ToLowerInvariant(),
            SourceTopology = topology,
            Assessment = assessment,
            AdditionalPreparationRays = measurement.RayCount,
            Deletions = diagnostics
        }, TopologySamplingBenchmarks.Json));
        return 0;
    }
}
