using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Continue reduction from an exported qualified source with unchanged physical gates and total caps.</summary>
internal static class ReductionContinuationBenchmarks
{
    public static async Task<int> Diagnose(string[] args)
    {
        if (args.Length is not (2 or 3) || args.Length == 3 && args[2] != "native-only")
            throw new ArgumentException("--reduction-diagnose <completed comparison directory> <new audit directory> [native-only]");
        var root = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var plan = await AutonomousDesignFiles.LoadPlanAsync(Path.Combine(root, "source.topology-plan.json"));
        var parent = TopologyCompiler.Compile(plan.Seeds.Single(), plan.Constraints);
        var rows = new List<object>();
        foreach (var element in args.Length == 3 ? [] : parent.Topology.Elements)
            foreach (var mode in new[] { "baseline", "geometry" })
            {
                var options = plan.Options.Recovery with { UseGeometryConstraintTangents = mode == "geometry" ? true : null };
                var recorded = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root, $"reduce-3-2-42-C-{mode}", "run.autonomous.json"));
                var previous = recorded.Events.FirstOrDefault(item => item.Recovery is not null && item.Change!.RemovedElementIds.Contains(element.Id));
                if (previous is not null)
                {
                    rows.Add(new
                    {
                        Removed = element.Id,
                        Mode = mode,
                        Reuses = recorded.RunId + "/" + previous.TrialId,
                        Recovery = previous.Recovery,
                        AdditionalRecoveryFunctions = 0,
                        AdditionalPhysicalRays = 0
                    });
                    continue;
                }
                var edited = TopologyOperators.Delete(parent.Topology, element.Id, plan.Constraints);
                if (!edited.Succeeded) { rows.Add(new { Removed = element.Id, Mode = mode, edited.Failure }); continue; }
                var child = TopologyCompiler.Compile(edited.Topology!, plan.Constraints);
                var variables = child.Topology.Elements.Where(item => !item.Protected).SelectMany(item => new[] { (item.FrontSurfaceId, item.Front), (item.BackSurfaceId, item.Back) })
                    .Where(face => face.Item2.Profile.Kind != TopologyProfileKind.Plane)
                    .Select(face => new TopologyVariableRequest(face.Item1, OpticalDesignParameterKind.Curvature,
                        -plan.Options.CurvatureBoundPerMillimeter, plan.Options.CurvatureBoundPerMillimeter, .01)).ToList();
                var axial = plan.Options.AxialCompensation!;
                foreach (var kept in child.Topology.Elements.Where(item => !item.Protected && (axial.ElementIds.IsEmpty || axial.ElementIds.Contains(item.Id))))
                    foreach (var face in new[] { (kept.FrontSurfaceId, kept.FrontZMillimeters), (kept.BackSurfaceId, kept.BackZMillimeters) })
                        variables.Add(new(face.Item1, OpticalDesignParameterKind.AxialPosition,
                            Math.Max(child.Topology.Reference.ZMillimeters, face.Item2 - axial.MaximumDisplacementMillimeters),
                            Math.Min(child.Topology.Image.ZMillimeters, face.Item2 + axial.MaximumDisplacementMillimeters), axial.CoordinateScaleMillimeters));
                using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
                var result = ParaxialRestorer.Run(parent, child, variables, plan.Targets, options);
                var assessment = result.Termination == ParaxialRecoveryTermination.Recovered
                    ? TopologyValidator.Validate(TopologyCompiler.Compile(result.Topology, plan.Constraints), plan.Targets) : null;
                rows.Add(new
                {
                    Removed = element.Id,
                    Mode = mode,
                    Recovery = result,
                    Assessment = assessment,
                    AdditionalRecoveryFunctions = result.Work.FunctionEvaluations,
                    AdditionalPhysicalRays = quota.RayCount
                });
                await File.WriteAllTextAsync(Path.Combine(output, "deletion-diagnosis.json"), JsonSerializer.Serialize(rows, TopologySamplingBenchmarks.Json));
            }
        await File.WriteAllTextAsync(Path.Combine(output, "deletion-diagnosis.json"), JsonSerializer.Serialize(rows, TopologySamplingBenchmarks.Json));
        var audits = new List<object>(); var seen = new Dictionary<string, string>();
        using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json")));
        foreach (var row in summary.RootElement.GetProperty("Runs").EnumerateArray())
        {
            var id = row.GetProperty("Id").GetString()!;
            var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(root, id, "run.autonomous.json"));
            foreach (var candidate in state.Candidates.Where(candidate => candidate.Topology.Elements.Length < parent.Topology.Elements.Length))
            {
                var fingerprint = candidate.Assessment.OpticFingerprint;
                if (seen.TryGetValue(fingerprint, out var reuse)) { audits.Add(new { Id = id, Reuses = reuse, AdditionalAuditRays = 0 }); continue; }
                seen.Add(fingerprint, id);
                var path = Path.Combine(root, id, candidate.Id + (candidate.Assessment.MeetsTargets ? "-qualified.staropt" : "-unqualified.staropt"));
                using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
                var native = await AutonomousDesignFiles.ImportSeedAsync(path, plan.Constraints);
                var actual = TopologyValidator.Validate(TopologyCompiler.Compile(native, plan.Constraints), plan.Targets);
                var expected = candidate.Assessment;
                var matched = actual.Completed && actual.MeetsTargets == expected.MeetsTargets && actual.ProtocolFingerprint == expected.ProtocolFingerprint
                    && Equal(actual.EffectiveFocalLengthMillimeters, expected.EffectiveFocalLengthMillimeters, 1e-9) && Equal(actual.FNumber, expected.FNumber, 1e-9)
                    && actual.Fields.Length == expected.Fields.Length && actual.Fields.Zip(expected.Fields, (a, b) => a.ValidRays == b.ValidRays
                        && a.CheckValidRays == b.CheckValidRays && Equal(a.RmsRadiusMillimeters, b.RmsRadiusMillimeters, 1e-10)
                        && Equal(a.MaximumRadiusMillimeters, b.MaximumRadiusMillimeters, 1e-10) && Equal(a.DistortionFraction, b.DistortionFraction, 1e-12)).All(value => value);
                audits.Add(new { Id = id, Matches = matched, Assessment = actual, AdditionalAuditRays = quota.RayCount });
                await File.WriteAllTextAsync(Path.Combine(output, "native-audits.json"), JsonSerializer.Serialize(audits, TopologySamplingBenchmarks.Json));
                if (!matched) throw new InvalidDataException("Native physical endpoint changed: " + id);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "native-audits.json"), JsonSerializer.Serialize(audits, TopologySamplingBenchmarks.Json));
        return 0;
        static bool Equal(double? a, double? b, double tolerance) => a is { } x && b is { } y ? Math.Abs(x - y) <= tolerance : a is null && b is null;
    }

    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--reduction-continuation <frozen protocol> <new evidence directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        var bytes = await File.ReadAllBytesAsync(args[0]);
        using var document = JsonDocument.Parse(bytes);
        var protocol = document.RootElement;
        var planPath = protocol.GetProperty("SourcePlan").GetString()!;
        var nativePath = protocol.GetProperty("SourceNative").GetString()!;
        if (Hash(await File.ReadAllBytesAsync(planPath)) != protocol.GetProperty("SourcePlanSha256").GetString()
            || Hash(await File.ReadAllBytesAsync(nativePath)) != protocol.GetProperty("SourceNativeSha256").GetString())
            throw new InvalidDataException("Frozen source plan or native changed.");
        var previous = await AutonomousDesignFiles.LoadPlanAsync(planPath);
        var sourceTopology = await AutonomousDesignFiles.ImportSeedAsync(nativePath, previous.Constraints);
        var elementCount = protocol.GetProperty("SourceElements").GetInt32();
        var firstDeletion = protocol.TryGetProperty("InitialDeletionOrdinal", out var ordinal) ? ordinal.GetInt32() : 0;
        if (firstDeletion < 0 || firstDeletion >= elementCount) throw new InvalidDataException("Invalid explicitly selected deletion ordinal.");
        if (sourceTopology.Elements.Length != elementCount) throw new InvalidDataException("Wrong source element count.");
        var policy = previous.Options;
        if (policy.MaximumTrials != 6 || policy.MaximumFunctionEvaluations != 600 || policy.MaximumGradientEvaluations != 100
            || policy.MaximumTracedRays != 2_000_000 || policy.MaximumStageTracedRays != 400_000
            || policy.FinalValidationRayReserve != 800_000 || policy.TimeLimit != TimeSpan.FromSeconds(60)
            || policy.Local.MaximumUpdates != 4 || policy.Full.MaximumUpdates != 99
            || policy.Recovery.MaximumFunctionEvaluations != 150 || policy.Recovery.MaximumAcceptedSteps != 30
            || policy.CompensateConic != true || policy.UseAxialVariablesInRecovery != true
            || policy.Recovery.UseAutomaticMatrixDerivatives != true || policy.Recovery.RestorePupilMagnification != true
            || policy.Expansion is not { EnableInsertion: false, EnableDeletion: true, EnableSplit: false, EnableMerge: false, EnableMaterial: false, EnableProfile: false })
            throw new InvalidDataException("Original source permissions, total caps or stage allocation changed.");
        var source = previous with { Seeds = [sourceTopology] };
        Directory.CreateDirectory(output);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        TopologyAssessment initialAssessment;
        long preparationRays;
        using (var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000))
        {
            initialAssessment = TopologyValidator.Validate(TopologyCompiler.Compile(sourceTopology, source.Constraints), source.Targets);
            preparationRays = quota.RayCount;
        }
        await File.WriteAllTextAsync(Path.Combine(output, "preparation.json"), JsonSerializer.Serialize(new
        { Native = Path.GetFullPath(nativePath), InitialAssessment = initialAssessment, Source = sourceTopology, AdditionalPreparationRays = preparationRays }, TopologySamplingBenchmarks.Json));
        if (!initialAssessment.MeetsTargets) throw new InvalidDataException("Source native no longer passes the unchanged full physical gates.");
        await AutonomousDesignFiles.SavePlanAsync(source, Path.Combine(output, "source.topology-plan.json"));
        var binaries = new[] { typeof(ParaxialRestorer).Assembly, typeof(DifferentiableParaxialMetrics).Assembly,
            typeof(AutonomousSearchPlan).Assembly, typeof(ReductionContinuationBenchmarks).Assembly }
            .ToDictionary(assembly => assembly.GetName().Name!, assembly => Hash(File.ReadAllBytes(assembly.Location)));
        var rows = new List<object>();
        foreach (var seed in protocol.GetProperty("Seeds").EnumerateArray().Select(value => value.GetInt64()))
            foreach (var group in protocol.GetProperty("Groups").EnumerateArray().Select(value => value.GetString()!))
                foreach (var mode in protocol.GetProperty("Modes").EnumerateArray().Select(value => value.GetString()!))
                {
                    if (group is not ("C" or "D") || mode is not ("baseline" or "geometry")) throw new InvalidDataException("Unknown frozen comparison cell.");
                    var plan = source with
                    {
                        Options = source.Options with
                        {
                            Seed = seed,
                            Expansion = source.Options.Expansion! with { EnableGradualDeletion = group == "D" },
                            Recovery = source.Options.Recovery with { UseGeometryConstraintTangents = mode == "geometry" ? true : null }
                        }
                    };
                    var id = $"reduce-{elementCount}-{elementCount - 1}-{seed}-{group}-{mode}";
                    var directory = Path.Combine(output, id);
                    Directory.CreateDirectory(directory);
                    var store = new AutonomousSearchCheckpointStore();
                    var checkpointPath = Path.Combine(directory, "run.autonomous.json");
                    await AutonomousDesignFiles.SavePlanAsync(plan, Path.Combine(directory, "task.topology-plan.json"));
                    var start = AutonomousSearchService.Create(id, plan) with { Revision = 1 };
                    await store.SaveAsync(start, checkpointPath, 0);
                    var revision = start.Revision;
                    try
                    {
                        if (firstDeletion > 0)
                        {
                            // An explicit experiment priority, not an automatic search improvement.
                            // Charge and persist the real seed assessment before changing only its operation cursor.
                            using var stopAfterSeed = new CancellationTokenSource();
                            start = AutonomousSearchService.Run(start, state =>
                            {
                                store.SaveAsync(state, checkpointPath, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision;
                                if (state.Candidates.Length == 1 && state.TrialCursor == 0 && state.Pending is null) stopAfterSeed.Cancel();
                            }, stopAfterSeed.Token);
                            if (start.TrialCursor != 0 || start.Reservation is not null || start.Pending is not null || start.Candidates.Length != 1
                                || !start.Candidates[0].Assessment.MeetsTargets) throw new InvalidDataException("Seed-only initialization did not stop at the safe boundary.");
                            start = start with { Revision = start.Revision + 1, Candidates = start.Candidates.SetItem(0, start.Candidates[0] with { OperationCursor = firstDeletion }) };
                            AutonomousSearchService.Validate(start);
                            await store.SaveAsync(start, checkpointPath, revision); revision = start.Revision;
                            await File.WriteAllTextAsync(Path.Combine(directory, "explicit-start.json"), JsonSerializer.Serialize(new
                            { InitialDeletionOrdinal = firstDeletion, SourceElement = sourceTopology.Elements[firstDeletion].Id, InitializationWork = start.Consumed }, TopologySamplingBenchmarks.Json));
                        }
                        var result = AutonomousSearchService.Run(start, state =>
                        { store.SaveAsync(state, checkpointPath, revision).AsTask().GetAwaiter().GetResult(); revision = state.Revision; });
                        if (ContentFingerprint.Compute(result) != ContentFingerprint.Compute(await store.LoadAsync(checkpointPath)))
                            throw new InvalidDataException("Stored physical endpoint changed.");
                        foreach (var candidate in result.Candidates)
                            await AutonomousDesignFiles.ExportAsync(candidate, plan.Constraints,
                                Path.Combine(directory, candidate.Id + (candidate.Assessment.MeetsTargets ? "-qualified.staropt" : "-unqualified.staropt")));
                        rows.Add(new
                        {
                            Id = id,
                            Seed = seed,
                            Group = group,
                            Mode = mode,
                            Status = result.Status.ToString(),
                            result.Consumed,
                            result.TrialCursor,
                            PendingPhase = result.Pending?.Phase.ToString(),
                            ReducedSuccess = result.AcceptedArchive.Any(candidate => candidate.Topology.Elements.Length < elementCount),
                            Candidates = result.Candidates.Select(candidate => new { candidate.Id, Elements = candidate.Topology.Elements.Length, candidate.Assessment }).ToArray(),
                            Events = result.Events.Select(item => new
                            {
                                item.Sequence,
                                item.TrialId,
                                item.Operation,
                                item.Phase,
                                item.Outcome,
                                item.Detail,
                                item.Work,
                                Removed = item.Change?.RemovedElementIds,
                                Recovery = item.Recovery is { } recovery ? new
                                {
                                    recovery.Termination,
                                    recovery.InitialJacobianRank,
                                    recovery.InitialAError,
                                    recovery.InitialFocalCError,
                                    recovery.InitialPupilError,
                                    recovery.EndpointAError,
                                    recovery.EndpointFocalCError,
                                    recovery.EndpointPupilError,
                                    AcceptedSteps = recovery.Steps.Length,
                                    recovery.Work,
                                    recovery.Diagnostics
                                } : null
                            }).ToArray()
                        });
                        Console.WriteLine($"{id}: {result.Status}; reduced={result.AcceptedArchive.Any(candidate => candidate.Topology.Elements.Length < elementCount)}; F/G/rays={result.Consumed.FunctionEvaluations}/{result.Consumed.GradientEvaluations}/{result.Consumed.TracedRays}");
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
}
