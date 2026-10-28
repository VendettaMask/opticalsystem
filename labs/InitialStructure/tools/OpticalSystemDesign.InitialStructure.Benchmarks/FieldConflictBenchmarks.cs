using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Engine.Optimization;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class FieldConflictBenchmarks
{
    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--field-conflict-audit <comparison directory> <new directory>");
        var root = Path.GetFullPath(args[0]); var output = NewDirectory(args[1]);
        using var protocol = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "frozen-protocol.json")));
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(protocol.RootElement.GetProperty("Checkpoint").GetString()!);
        using var summary = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "summary.json")));
        var rows = new List<object>();
        foreach (var row in summary.RootElement.GetProperty("Runs").EnumerateArray())
        {
            var version = row.GetProperty("Version").GetInt32();
            var expected = JsonSerializer.Deserialize<TopologyAssessment>(row.GetProperty("Final").GetRawText(), TopologySamplingBenchmarks.Json)!;
            var native = Path.Combine(root, "v" + version, expected.MeetsTargets ? "qualified.staropt" : "unqualified.staropt");
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
            var imported = await AutonomousDesignFiles.ImportSeedAsync(native, state.Plan.Constraints);
            var actual = TopologyValidator.Validate(TopologyCompiler.Compile(imported, state.Plan.Constraints), state.Plan.Targets);
            var matched = actual.Completed && actual.MeetsTargets == expected.MeetsTargets && actual.ProtocolFingerprint == expected.ProtocolFingerprint
                && Equal(actual.EffectiveFocalLengthMillimeters, expected.EffectiveFocalLengthMillimeters, 1e-9) && Equal(actual.FNumber, expected.FNumber, 1e-9)
                && actual.Fields.Length == expected.Fields.Length && actual.Fields.Zip(expected.Fields, (a, b) => a.ValidRays == b.ValidRays
                    && a.CheckValidRays == b.CheckValidRays && Equal(a.RmsRadiusMillimeters, b.RmsRadiusMillimeters, 1e-10)
                    && Equal(a.MaximumRadiusMillimeters, b.MaximumRadiusMillimeters, 1e-10) && Equal(a.DistortionFraction, b.DistortionFraction, 1e-12)).All(value => value);
            rows.Add(new { Version = version, Matches = matched, Assessment = actual, AdditionalAuditRays = quota.RayCount });
            await Write(output, "native-audits.json", rows);
            if (!matched) throw new InvalidDataException("Native physical endpoint changed: " + version);
        }
        return 0;
        static bool Equal(double? a, double? b, double tolerance) => a is { } x && b is { } y ? Math.Abs(x - y) <= tolerance : a is null && b is null;
    }

    public static async Task<int> Diagnose(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--field-conflict-diagnose <checkpoint> <new directory>");
        var output = NewDirectory(args[1]); var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var candidate = state.Candidates.Single(c => c.Topology.Elements.Length == 2);
        var variables = state.Events.Last(e => e.Optimization is not null).Optimization!.Variables;
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, variables);
        var objective = new FixedTopologyObjective(source, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(500_000);
        var q = binding.InitialCoordinates.ToArray();
        var before = objective.Evaluate(q, includeJacobian: true, includeConstraintTangents: true);
        if (!before.Available) throw new InvalidDataException(before.Failure);
        var rows = new List<object>();
        foreach (var version in new[] { 4, 5 })
            foreach (var fraction in new[] { 1.0, .5, .25, .125 })
            {
                var policy = new TopologyTransmissionCompensation(objective.ResidualIds, version);
                var proposal = policy.Propose(before, q, binding, Enumerable.Range(0, q.Length).ToArray(), fraction);
                var after = objective.Evaluate(proposal.Coordinates, includeConstraintTangents: true);
                var a = policy.Merit(before); var b = policy.Merit(after);
                rows.Add(new
                {
                    Version = version,
                    Fraction = fraction,
                    proposal.Coordinates,
                    proposal.Rank,
                    after.Available,
                    Accepted = policy.Accept(before, after),
                    BeforeOther = a.Other,
                    AfterOther = b.Other,
                    BeforeClearance = a.Clearance,
                    AfterClearance = b.Clearance,
                    after.Failure
                });
            }
        // Reconstruct version4's documented fallback before clipping to establish the actual bound conflict.
        var constraints = Enumerable.Range(0, objective.ResidualIds.Length).Where(row => !objective.ResidualIds[row].EndsWith(":x") && !objective.ResidualIds[row].EndsWith(":y")).ToArray();
        var matrix = new double[constraints.Length, q.Length]; var residuals = new double[constraints.Length];
        for (var row = 0; row < constraints.Length; row++)
        {
            residuals[row] = before.Residuals[constraints[row]] * (objective.ResidualIds[constraints[row]].EndsWith(":clearance") ? 1.05 : 1);
            for (var column = 0; column < q.Length; column++) matrix[row, column] = before.Jacobian[constraints[row]][column];
        }
        var step = new RegularizedLeastSquaresModel(matrix, residuals).Step(1, out _);
        var clipped = Enumerable.Range(0, q.Length).Where(i => q[i] + step[i] < binding.Slots[i].LowerCoordinate || q[i] + step[i] > binding.Slots[i].UpperCoordinate)
            .Select(i => new { variables[i].SurfaceId, variables[i].Kind, Current = q[i], RawProposal = q[i] + step[i], binding.Slots[i].LowerCoordinate, binding.Slots[i].UpperCoordinate }).ToArray();
        await Write(output, "diagnosis.json", new
        {
            SourceCheckpoint = Path.GetFullPath(args[0]),
            Variables = variables,
            Initial = candidate.Assessment,
            Before = before,
            ActualDiagnosticRays = quota.RayCount,
            ClippedFallbackVariables = clipped,
            Rows = rows
        });
        return 0;
    }

    public static async Task<int> Compare(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--field-conflict-compare <frozen protocol> <new directory>");
        var bytes = await File.ReadAllBytesAsync(args[0]); using var document = JsonDocument.Parse(bytes); var protocol = document.RootElement;
        var checkpoint = protocol.GetProperty("Checkpoint").GetString()!;
        if (Hash(await File.ReadAllBytesAsync(checkpoint)) != protocol.GetProperty("Sha256").GetString()) throw new InvalidDataException("Frozen source changed.");
        var output = NewDirectory(args[1]); await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(checkpoint);
        var candidate = state.Candidates.Single(c => c.Topology.Elements.Length == 2);
        var variables = state.Events.Last(e => e.Optimization is not null).Optimization!.Variables;
        var source = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
        var policy = state.Plan.Options;
        if (policy.MaximumFunctionEvaluations != 600 || policy.MaximumGradientEvaluations != 100 || policy.MaximumTracedRays != 2_000_000
            || policy.MaximumStageTracedRays != 400_000 || policy.FinalValidationRayReserve != 800_000 || policy.TimeLimit != TimeSpan.FromSeconds(60)
            || policy.Local.MaximumUpdates != 4 || policy.Full.MaximumUpdates != 99 || variables.Length != 12) throw new InvalidDataException("Frozen budget changed.");
        var rows = new List<object>();
        var binaries = new[] { typeof(ParaxialRestorer).Assembly, typeof(AutonomousSearchPlan).Assembly, typeof(SequentialTraceMeasurement).Assembly, typeof(FieldConflictBenchmarks).Assembly }
            .ToDictionary(a => a.GetName().Name!, a => Hash(File.ReadAllBytes(a.Location)));
        foreach (var version in protocol.GetProperty("Versions").EnumerateArray().Select(v => v.GetInt32()))
        {
            var directory = NewDirectory(Path.Combine(output, "v" + version));
            var localOptions = policy.Local with { TransmissionPolicyVersion = version, TimeLimit = policy.TimeLimit, PerformFinalAssessment = false };
            var fullOptions = policy.Full with
            {
                TransmissionPolicyVersion = version,
                TimeLimit = policy.TimeLimit,
                PerformFinalAssessment = false,
                MaximumGradientEvaluations = 100 - localOptions.MaximumGradientEvaluations
            };
            await Write(directory, "input.json", new { Source = source.Topology, Variables = variables, state.Plan.Targets, state.Plan.Constraints, Local = localOptions, Full = fullOptions });
            var timer = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(policy.TimeLimit);
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(policy.MaximumTracedRays);
            TopologyAssessment initial;
            using (var check = SequentialTraceMeasurement.BeginCpuBudget(800_000)) initial = TopologyValidator.Validate(source, state.Plan.Targets, deadline.Token);
            if (!initial.Completed) throw new InvalidDataException("Initial independent assessment incomplete.");
            var initialRays = quota.RayCount;
            FixedTopologyResult local;
            using (var phase = SequentialTraceMeasurement.BeginCpuBudget(400_000))
                local = FixedTopologyOptimizer.Run(source, TopologyParameterBinder.Bind(source, variables), state.Plan.Targets, localOptions, deadline.Token);
            var localRays = quota.RayCount - initialRays;
            var next = TopologyCompiler.Compile(local.Topology, state.Plan.Constraints);
            FixedTopologyResult full;
            using (var phase = SequentialTraceMeasurement.BeginCpuBudget(Math.Min(400_000, 2_000_000 - quota.RayCount - 800_000)))
                full = FixedTopologyOptimizer.Run(next, TopologyParameterBinder.Bind(next, variables), state.Plan.Targets, fullOptions, deadline.Token);
            var fullRays = quota.RayCount - initialRays - localRays;
            TopologyAssessment final;
            using (var check = SequentialTraceMeasurement.BeginCpuBudget(Math.Min(800_000, 2_000_000 - quota.RayCount)))
                final = TopologyValidator.Validate(TopologyCompiler.Compile(full.Topology, state.Plan.Constraints), state.Plan.Targets, deadline.Token);
            var elapsed = timer.Elapsed;
            if (final.Completed)
                await AutonomousDesignFiles.ExportAsync(new("v" + version, null, full.Topology, final, final.Violations.Length), state.Plan.Constraints,
                    Path.Combine(directory, final.MeetsTargets ? "qualified.staropt" : "unqualified.staropt"));
            await Write(directory, "local.json", local); await Write(directory, "full.json", full);
            rows.Add(new
            {
                Version = version,
                Initial = initial,
                Final = final,
                InitialRays = initialRays,
                LocalRays = localRays,
                FullRays = fullRays,
                FinalRays = quota.RayCount - initialRays - localRays - fullRays,
                ActualRays = quota.RayCount,
                FunctionEvaluations = 2 + local.Work.FunctionEvaluations + full.Work.FunctionEvaluations,
                GradientEvaluations = local.Work.GradientEvaluations + full.Work.GradientEvaluations,
                ActualSeconds = elapsed.TotalSeconds,
                AcceptedUpdates = local.AcceptedUpdates + full.AcceptedUpdates
            });
            await Write(output, "summary.json", new
            {
                ProtocolSha256 = Hash(bytes),
                Binaries = binaries,
                CpuProcessors = Environment.ProcessorCount,
                Priority = Process.GetCurrentProcess().PriorityClass.ToString(),
                Runs = rows
            });
            Console.WriteLine($"v{version}: qualified={final.MeetsTargets}; updates={local.AcceptedUpdates + full.AcceptedUpdates}; rays={quota.RayCount}");
        }
        return 0;
    }

    private static string NewDirectory(string path)
    {
        var result = Path.GetFullPath(path); if (Directory.Exists(result) && Directory.EnumerateFileSystemEntries(result).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(result); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; return result;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static Task Write(string directory, string name, object value) => File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value, TopologySamplingBenchmarks.Json));
}
