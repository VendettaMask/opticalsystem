using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class TopologySamplingBenchmarks
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-sampling-audit <repair summary> <new output directory>");
        var output = Path.GetFullPath(args[1]); if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(args[0])); var results = new List<object>();
        foreach (var row in document.RootElement.GetProperty("Runs").EnumerateArray())
        {
            var id = row.GetProperty("Id").GetString()!; var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, id);
            var recorded = row.GetProperty("Final").Deserialize<TopologyAssessment>(Json)!;
            var path = Path.Combine(directory, recorded.MeetsTargets ? "qualified.staropt" : "unqualified.staropt");
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
            var imported = await AutonomousDesignFiles.ImportSeedAsync(path, recorded.StructuralConstraints);
            var source = TopologyCompiler.Compile(imported, recorded.StructuralConstraints);
            var physical = TopologyValidator.Validate(source, recorded.Targets);
            if (!physical.Completed || physical.MeetsTargets != recorded.MeetsTargets || physical.ProtocolFingerprint != recorded.ProtocolFingerprint
                || !physical.Violations.Select(item => item.Code).SequenceEqual(recorded.Violations.Select(item => item.Code))
                || physical.Fields.Zip(recorded.Fields, (a, b) => Math.Abs(a.RmsRadiusMillimeters!.Value - b.RmsRadiusMillimeters!.Value)).Max() > 1e-10)
                throw new InvalidDataException("Native roundtrip changed the physical endpoint.");
            var physicalRays = quota.RayCount;
            using var input = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "input.json")));
            var requests = input.RootElement.GetProperty("Variables").Deserialize<TopologyVariableRequest[]>(Json)!;
            var binding = TopologyParameterBinder.Bind(source, requests);
            var sampling = input.RootElement.GetProperty("Options").GetProperty("Sampling").Deserialize<TopologyTrainingSampling>(Json)!;
            var a = new FixedTopologyObjective(source, binding, recorded.Targets, sampling with { PupilRings = 4, PupilArms = 16 });
            var b = new FixedTopologyObjective(source, binding, recorded.Targets, sampling with { PupilRings = 10, PupilArms = 32 });
            var av = a.Evaluate(binding.InitialCoordinates, includeConstraintTangents: true);
            var bv = b.Evaluate(binding.InitialCoordinates, includeConstraintTangents: true);
            if (!av.Available || !bv.Available) throw new InvalidDataException(av.Failure ?? bv.Failure);
            var fields = a.Fields.Select((field, index) =>
            {
                var ar = av.ConstraintTangents!.Value.Single(item => item.Row == a.ResidualIds.IndexOf($"field:{index}:rms"));
                var br = bv.ConstraintTangents!.Value.Single(item => item.Row == b.ResidualIds.IndexOf($"field:{index}:rms"));
                var small = (ar.Value + 1) * recorded.Targets.MaximumRmsRadiusMillimeters;
                var large = (br.Value + 1) * recorded.Targets.MaximumRmsRadiusMillimeters;
                var exact = physical.Fields.Single(item => Math.Abs(item.Field.X - field.X) < 1e-12 && Math.Abs(item.Field.Y - field.Y) < 1e-12);
                return new
                {
                    Field = field,
                    AccurateRmsUm = small * 1000,
                    ReferenceRmsUm = large * 1000,
                    IntegrationDifferenceUm = Math.Abs(small - large) * 1000,
                    PhysicalRmsUm = exact.RmsRadiusMillimeters * 1000,
                    exact.ValidRays,
                    exact.AttemptedRays
                };
            }).ToArray();
            if (fields.Max(field => field.IntegrationDifferenceUm) > .05) throw new InvalidDataException("The final endpoint did not retain the calibrated sampling accuracy.");
            results.Add(new
            {
                Id = id,
                NativeFile = path,
                NativeSha256 = Hash(await File.ReadAllBytesAsync(path)),
                Assessment = physical,
                Precision = fields,
                PhysicalRecheckRays = physicalRays,
                AdditionalAuditRays = quota.RayCount
            });
            await File.WriteAllTextAsync(Path.Combine(output, "native-and-precision-audits.json"), JsonSerializer.Serialize(results, Json));
            Console.WriteLine($"{id}: native pass={physical.MeetsTargets}, maximum integration error={fields.Max(field => field.IntegrationDifferenceUm):G5} um; additional audit rays={quota.RayCount}");
        }
        return 0;
    }

    public static async Task<int> Repair(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-sampling-repair <frozen protocol> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        using var process = Process.GetCurrentProcess(); process.PriorityClass = ProcessPriorityClass.BelowNormal;
        var bytes = await File.ReadAllBytesAsync(args[0]); using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        var caps = root.GetProperty("Budget");
        if (caps.GetProperty("MaximumTracedRays").GetInt64() != 2_000_000 || caps.GetProperty("MaximumGradientEvaluations").GetInt32() != 100
            || caps.GetProperty("MaximumFunctionEvaluations").GetInt32() != 600 || caps.GetProperty("TimeLimitSeconds").GetInt32() != 60
            || caps.GetProperty("FinalValidationRayReserve").GetInt64() != 800_000 || root.GetProperty("MaximumUpdates").GetInt32() != 99)
            throw new InvalidDataException("Frozen total repair caps changed.");
        Directory.CreateDirectory(output); await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        var binaries = new[] { typeof(AutonomousSearchService).Assembly, typeof(AutonomousSearchPlan).Assembly, typeof(OpticalSystemDesign.Core.Optic).Assembly, typeof(TopologySamplingBenchmarks).Assembly }
            .ToDictionary(item => item.GetName().Name!, item => Hash(File.ReadAllBytes(item.Location)));
        var runs = new List<object>();
        foreach (var task in root.GetProperty("Cases").EnumerateArray())
        {
            var path = task.GetProperty("SourceCheckpoint").GetString()!;
            if (Hash(await File.ReadAllBytesAsync(path)) != task.GetProperty("SourceSha256").GetString()) throw new InvalidDataException("Frozen repair source changed.");
            var prior = await new AutonomousSearchCheckpointStore().LoadAsync(path);
            var parent = Reduced(prior);
            var modes = root.TryGetProperty("Modes", out var configuredModes) ? configuredModes.EnumerateArray().Select(item => item.GetString()!).ToArray() : ["coarse", "accurate"];
            if (modes.Any(item => item is not ("coarse" or "accurate" or "accurate-axial"))) throw new InvalidDataException("Unknown repair mode.");
            foreach (var mode in modes)
            {
                var id = task.GetProperty("Id").GetString() + "-" + mode;
                var directory = Path.Combine(output, id); Directory.CreateDirectory(directory);
                var high = mode is "accurate" or "accurate-axial";
                var sampling = prior.Plan.Options.Full.Sampling with { PupilRings = high ? 4 : 1, PupilArms = high ? 16 : 8 };
                var options = prior.Plan.Options.Full with
                {
                    Sampling = sampling,
                    TransmissionFirst = true,
                    TransmissionPolicyVersion = 3,
                    ActiveParameterLimit = null,
                    MaximumUpdates = 99,
                    MaximumFunctionEvaluations = 100,
                    MaximumGradientEvaluations = 100,
                    TimeLimit = TimeSpan.FromSeconds(50),
                    PerformFinalAssessment = false
                };
                var source = TopologyCompiler.Compile(parent.Topology, prior.Plan.Constraints); var binding = Bind(source, prior.Plan.Options.CurvatureBoundPerMillimeter);
                if (mode == "accurate-axial")
                {
                    if (root.GetProperty("AxialWindowMillimeters").GetDouble() != .25) throw new InvalidDataException("Frozen axial bounds changed.");
                    var element = source.Topology.Elements.Single(item => item.Id == root.GetProperty("AxialElementId").GetString());
                    var front = element.FrontZMillimeters; var back = front + element.ThicknessMillimeters;
                    binding = TopologyParameterBinder.Bind(source, [..binding.Slots.Select(item=>item.Request),
                        new(element.FrontSurfaceId,OpticalDesignParameterKind.AxialPosition,front-.25,front+.25,.1),
                        new(element.BackSurfaceId,OpticalDesignParameterKind.AxialPosition,back-.25,back+.25,.1)]);
                }
                await File.WriteAllTextAsync(Path.Combine(directory, "input.json"), JsonSerializer.Serialize(new { parent.Topology, prior.Plan.Targets, prior.Plan.Constraints, Options = options, Variables = binding.Slots.Select(item => item.Request) }, Json));
                var timer = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                long initialRays = 0, trainingRays = 0, finalRays = 0; TopologyAssessment? initial = null, final = null; FixedTopologyResult? optimized = null;
                using var total = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
                try
                {
                    using (var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000))
                    { initial = TopologyValidator.Validate(source, prior.Plan.Targets, deadline.Token); initialRays = quota.RayCount; }
                    if (!initial.Completed || initial.OpticFingerprint != parent.Assessment.OpticFingerprint
                        || !initial.Violations.Select(item => item.Code).SequenceEqual(parent.Assessment.Violations.Select(item => item.Code)))
                        throw new InvalidDataException("Fresh initial physical check did not reproduce the frozen source.");
                    var allowance = 2_000_000 - total.RayCount - 800_000;
                    using (var quota = SequentialTraceMeasurement.BeginCpuBudget(allowance))
                    {
                        var dispatched = options with { TimeLimit = TimeSpan.FromSeconds(Math.Max(.001, 50 - timer.Elapsed.TotalSeconds)) };
                        await File.WriteAllTextAsync(Path.Combine(directory, "phase-options.json"), JsonSerializer.Serialize(dispatched, Json));
                        optimized = FixedTopologyOptimizer.Run(source, binding, prior.Plan.Targets, dispatched, deadline.Token);
                        trainingRays = quota.RayCount;
                    }
                    using (var quota = SequentialTraceMeasurement.BeginCpuBudget(Math.Min(800_000, 2_000_000 - total.RayCount)))
                    { final = TopologyValidator.Validate(TopologyCompiler.Compile(optimized.Topology, prior.Plan.Constraints), prior.Plan.Targets, deadline.Token); finalRays = quota.RayCount; }
                    if (initial.ProtocolFingerprint != final.ProtocolFingerprint || total.RayCount != initialRays + trainingRays + finalRays
                        || trainingRays != optimized.Work.TracedRays || optimized.Work.GradientEvaluations > 100 || optimized.Work.FunctionEvaluations + 2 > 600)
                        throw new InvalidDataException("Repair ledger or independent protocol changed.");
                    var candidate = new AutonomousSearchCandidate(id, parent.Id, optimized.Topology, final,
                        final.Violations.Length + final.Fields.Select(field => field.RmsRadiusMillimeters ?? 1000).DefaultIfEmpty(1000).Max() / prior.Plan.Targets.MaximumRmsRadiusMillimeters);
                    await AutonomousDesignFiles.ExportAsync(candidate, prior.Plan.Constraints, Path.Combine(directory, final.MeetsTargets ? "qualified.staropt" : "unqualified.staropt"));
                    runs.Add(new
                    {
                        Id = id,
                        Case = task.GetProperty("Id").GetString(),
                        Mode = mode,
                        ParentCheckpoint = path,
                        source.OpticFingerprint,
                        SourcePreparationCost = "Retained earlier search; not folded into this fresh fixed-three-element repair budget.",
                        Status = optimized.Termination.ToString(),
                        ActualSeconds = timer.Elapsed.TotalSeconds,
                        FunctionEvaluations = optimized.Work.FunctionEvaluations + 2,
                        optimized.Work.GradientEvaluations,
                        InitialRays = initialRays,
                        TrainingRays = trainingRays,
                        FinalRays = finalRays,
                        ActualRays = total.RayCount,
                        MeetsTargets = final.MeetsTargets,
                        Initial = initial,
                        Optimization = optimized,
                        Final = final
                    });
                    Console.WriteLine($"{id}: pass={final.MeetsTargets}; RMS={final.Fields.Max(field => field.RmsRadiusMillimeters) * 1000:F3} um; boundary lost={final.Fields.Max(field => field.CheckAttemptedRays - field.CheckValidRays)}; G/rays={optimized.Work.GradientEvaluations}/{total.RayCount}");
                }
                catch (Exception error) when (error is InvalidOperationException or IOException or ArithmeticException or ArgumentException or OperationCanceledException)
                { runs.Add(new { Id = id, Status = "Failed", Failure = error.ToString(), Initial = initial, Optimization = optimized, Final = final, ActualRays = total.RayCount }); }
                await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
                { CpuProcessors = Environment.ProcessorCount, Priority = process.PriorityClass.ToString(), Binaries = binaries, ProtocolSha256 = Hash(bytes), Runs = runs }, Json));
            }
        }
        return 0;
    }

    public static async Task<int> Calibrate(string[] args)
    {
        if (args.Length is not (3 or 4) || args.Length == 4 && args[3] != "holdout") throw new ArgumentException("--topology-sampling-calibrate <signed checkpoint> <backtracking checkpoint> <new output directory> [holdout]");
        var holdout = args.Length == 4;
        var output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var signed = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var aggressive = await new AutonomousSearchCheckpointStore().LoadAsync(args[1]);
        var recovered = aggressive.Events.First(item => item.Recovery?.Termination == ParaxialRecoveryTermination.Recovered && item.Endpoint!.Elements.Length == 3).Endpoint!;
        var cases = new[] { ("qualified-four", signed.Candidates.Single(item => item.Id == "seed-0").Topology),
            ("recovered-three", recovered), ("signed-three", Reduced(signed).Topology), ("underestimated-three", Reduced(aggressive).Topology) };
        if (holdout) cases = cases.Where(item => item.Item1 is "signed-three" or "underestimated-three")
            .SelectMany(item => new[] { 42, 101, 2026 }.Select(seed => ($"{item.Item1}-perturbed-{seed}", Perturb(item.Item2, signed.Plan, seed)))).ToArray();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(holdout ? 900_000 : 2_000_000);
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu);
        var rows = new List<object>();
        try
        {
            foreach (var (name, topology) in cases)
            {
                var source = TopologyCompiler.Compile(topology, signed.Plan.Constraints);
                var binding = Bind(source, signed.Plan.Options.CurvatureBoundPerMillimeter);
                foreach (var (rings, arms) in holdout ? new[] { (4, 16), (10, 32) } : new[] { (1, 8), (2, 8), (3, 12), (4, 16), (6, 24), (10, 32) })
                {
                    var sampling = signed.Plan.Options.Full.Sampling with { PupilRings = rings, PupilArms = arms };
                    var objective = new FixedTopologyObjective(source, binding, signed.Plan.Targets, sampling);
                    var clock = Stopwatch.StartNew();
                    var value = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
                    var seconds = clock.Elapsed.TotalSeconds;
                    if (!value.Available) throw new InvalidOperationException(value.Failure);
                    using var physicalWork = SequentialTraceMeasurement.Begin();
                    var optic = source.CreateOptic();
                    var pupils = ApertureSampler.GenerateGaussianQuadrature(rings, arms).ToArray();
                    var physical = objective.Fields.Select((field, index) =>
                    {
                        var row = objective.ResidualIds.IndexOf($"field:{index}:rms");
                        var raw = value.ConstraintTangents!.Value.Single(item => item.Row == row);
                        var result = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y, pupils, TopologyEvaluationPlan.Checks(), includeSurfaceTransmission: false);
                        var angle = FieldCoordinates.Denormalize(optic.Fields, field.X, field.Y);
                        return new
                        {
                            FieldIndex = index,
                            Field = field,
                            XDegrees = angle.X,
                            YDegrees = angle.Y,
                            TrainingRmsMillimeters = (raw.Value + 1) * signed.Plan.Targets.MaximumRmsRadiusMillimeters,
                            PhysicalRmsMillimeters = result.Integration.Metrics?.RmsSpotRadius,
                            result.Integration.RayCount,
                            result.Integration.VignettedRayCount,
                            CheckLost = result.Checks.VignettedRayCount
                        };
                    }).ToArray();
                    rows.Add(new
                    {
                        Case = name,
                        source.OpticFingerprint,
                        Rings = rings,
                        Arms = arms,
                        value.TracedRayCount,
                        ObjectiveSeconds = seconds,
                        PhysicalDiagnosticRays = physicalWork.RayCount,
                        Fields = physical
                    });
                    await File.WriteAllTextAsync(Path.Combine(output, "calibration.json"), JsonSerializer.Serialize(new
                    { ActualRays = quota.RayCount, CpuProcessors = Environment.ProcessorCount, Priority = Process.GetCurrentProcess().PriorityClass.ToString(), Rows = rows }, Json));
                    Console.WriteLine($"{name} {rings}x{arms}: objective rays={value.TracedRayCount}, seconds={seconds:F3}");
                }
            }
        }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error))
        {
            await File.WriteAllTextAsync(Path.Combine(output, "INCOMPLETE.json"), JsonSerializer.Serialize(new
            { Reason = "Diagnostic hard quota reached; incomplete rows are not successful comparisons.", ActualRays = quota.RayCount, CompletedRows = rows.Count, Error = error.Message }, Json));
            return 2;
        }
        return 0;
    }

    internal static AutonomousSearchCandidate Reduced(AutonomousSearchCheckpoint state) => state.Candidates.Where(item => item.Topology.Elements.Length == 3 && item.Assessment.Completed).OrderBy(item => item.Score).First();

    private static DesignTopology Perturb(DesignTopology topology, AutonomousSearchPlan plan, int seed)
    {
        var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var binding = Bind(source, plan.Options.CurvatureBoundPerMillimeter);
        var state = (uint)seed;
        var values = binding.InitialCoordinates.Select((value, index) =>
        {
            state = unchecked(1664525 * state + 1013904223);
            return Math.Clamp(value + .1 * (2 * ((double)state / uint.MaxValue) - 1), binding.Slots[index].LowerCoordinate, binding.Slots[index].UpperCoordinate);
        }).ToArray();
        return TopologyParameterBinder.Apply(source, binding, values).Topology;
    }

    internal static BoundTopologyParameters Bind(CompiledTopology source, double bound) => TopologyParameterBinder.Bind(source,
        source.Topology.Elements.Where(element => !element.Protected)
            .SelectMany(element => new[] { (element.FrontSurfaceId, element.Front), (element.BackSurfaceId, element.Back) })
            .Where(face => face.Item2.Profile.Kind != TopologyProfileKind.Plane)
            .Select(face => new TopologyVariableRequest(face.Item1, OpticalDesignParameterKind.Curvature, -bound, bound, .01)));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
