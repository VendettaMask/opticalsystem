using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class WideFieldCompensationBenchmarks
{
    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--wide-field-native-audit <paired summary> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
        var results = new List<object>();
        foreach (var row in input.RootElement.GetProperty("Runs").EnumerateArray().Where(row => row.GetProperty("Mode").GetString() == "joint"
            || row.GetProperty("Seed").GetInt64() == 42 && row.GetProperty("Group").GetString() == "C"))
        {
            var id = row.GetProperty("Id").GetString()!;
            var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, id);
            var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(directory, "run.autonomous.json"));
            var candidate = state.Candidates.OrderByDescending(candidate => candidate.Assessment.MeetsTargets).ThenBy(candidate => candidate.Score).First();
            var path = Path.Combine(directory, candidate.Assessment.MeetsTargets ? candidate.Id + ".staropt" : "best-failed.staropt");
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(800_000);
            var imported = await AutonomousDesignFiles.ImportSeedAsync(path, state.Plan.Constraints);
            var actual = TopologyValidator.Validate(TopologyCompiler.Compile(imported, state.Plan.Constraints), state.Plan.Targets);
            if (!actual.Completed || actual.MeetsTargets != candidate.Assessment.MeetsTargets || actual.ProtocolFingerprint != candidate.Assessment.ProtocolFingerprint
                || !actual.Violations.Select(item => item.Code).SequenceEqual(candidate.Assessment.Violations.Select(item => item.Code))
                || actual.Fields.Zip(candidate.Assessment.Fields, (a, b) => Math.Abs(a.RmsRadiusMillimeters!.Value - b.RmsRadiusMillimeters!.Value)).Max() > 1e-10)
                throw new InvalidDataException("Native replay changed the original physical endpoint.");
            results.Add(new { Id = id, NativeFile = path, Assessment = actual, AdditionalAuditRays = quota.RayCount });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "native-audits.json"), JsonSerializer.Serialize(results, TopologySamplingBenchmarks.Json));
        return 0;
    }

    public static async Task<int> Repair(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--wide-field-repair <frozen protocol> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var bytes = await File.ReadAllBytesAsync(args[0]); using var protocol = JsonDocument.Parse(bytes); var root = protocol.RootElement;
        var checkpointPath = root.GetProperty("Checkpoint").GetString()!;
        if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(checkpointPath))).ToLowerInvariant() != root.GetProperty("Sha256").GetString()) throw new InvalidDataException("Frozen source changed.");
        await File.WriteAllBytesAsync(Path.Combine(output, "frozen-protocol.json"), bytes);
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(checkpointPath);
        var endpoint = state.Candidates.Where(candidate => candidate.Assessment.Completed).OrderBy(candidate => candidate.Score).First();
        var variables = state.Events.First(item => item.Optimization is not null).Optimization!.Variables;
        var source = TopologyCompiler.Compile(endpoint.Topology, state.Plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, variables);
        var budget = root.GetProperty("Budget");
        if (budget.GetProperty("MaximumTracedRays").GetInt64() != 2_000_000 || budget.GetProperty("FinalValidationRayReserve").GetInt64() != 800_000
            || budget.GetProperty("MaximumGradientEvaluations").GetInt32() != 100 || budget.GetProperty("MaximumFunctionEvaluations").GetInt32() != 600
            || budget.GetProperty("TimeLimitSeconds").GetInt32() != 60 || root.GetProperty("MaximumUpdates").GetInt32() != 8)
            throw new InvalidDataException("Frozen short repair caps changed.");
        var rows = new List<object>();
        foreach (var mode in root.GetProperty("Modes").EnumerateArray().Select(item => item.GetString()!))
        {
            var directory = Path.Combine(output, mode); Directory.CreateDirectory(directory);
            var options = state.Plan.Options.Full with
            {
                TransmissionPolicyVersion = mode == "joint" ? 4 : 3,
                Sampling = state.Plan.Options.Full.Sampling with { IncludeBoundaryMaximum = mode == "v3" ? null : true },
                MaximumUpdates = 8,
                MaximumFunctionEvaluations = 9,
                MaximumGradientEvaluations = 9,
                TimeLimit = TimeSpan.FromSeconds(60),
                PerformFinalAssessment = false
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "input.json"), JsonSerializer.Serialize(new { Variables = variables, Options = options, Source = endpoint.Topology, state.Plan.Targets, state.Plan.Constraints }, TopologySamplingBenchmarks.Json));
            var timer = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var quota = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
            TopologyAssessment initial;
            using (var initialQuota = SequentialTraceMeasurement.BeginCpuBudget(800_000)) initial = TopologyValidator.Validate(source, state.Plan.Targets, deadline.Token);
            var initialRays = quota.RayCount;
            if (!initial.Completed) throw new InvalidDataException("Fixed repair requires a completed initial assessment within the unchanged initial allocation.");
            FixedTopologyResult optimization;
            using (var trainingQuota = SequentialTraceMeasurement.BeginCpuBudget(Math.Min(400_000, 2_000_000 - initialRays - 800_000)))
                optimization = FixedTopologyOptimizer.Run(source, binding, state.Plan.Targets, options, deadline.Token);
            var trainingRays = quota.RayCount - initialRays;
            TopologyAssessment final;
            using (var finalQuota = SequentialTraceMeasurement.BeginCpuBudget(Math.Min(800_000, 2_000_000 - quota.RayCount)))
                final = TopologyValidator.Validate(TopologyCompiler.Compile(optimization.Topology, state.Plan.Constraints), state.Plan.Targets, deadline.Token);
            var rays = quota.RayCount;
            var candidate = new AutonomousSearchCandidate(mode, null, optimization.Topology, final,
                final.Violations.Length + (final.Fields.Select(field => field.RmsRadiusMillimeters ?? 1000).DefaultIfEmpty(1000).Max() / state.Plan.Targets.MaximumRmsRadiusMillimeters));
            if (final.Completed) await AutonomousDesignFiles.ExportAsync(candidate, state.Plan.Constraints, Path.Combine(directory, final.MeetsTargets ? "qualified.staropt" : "unqualified.staropt"));
            rows.Add(new
            {
                Id = mode,
                Initial = initial,
                Final = final,
                ActualRays = rays,
                InitialRays = initialRays,
                TrainingRays = trainingRays,
                FinalRays = rays - initialRays - trainingRays,
                FunctionEvaluations = optimization.Work.FunctionEvaluations + 2,
                optimization.Work.GradientEvaluations,
                ActualSeconds = timer.Elapsed.TotalSeconds,
                Optimization = optimization
            });
            await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { CpuProcessors = Environment.ProcessorCount, Priority = Process.GetCurrentProcess().PriorityClass.ToString(), Runs = rows }, TopologySamplingBenchmarks.Json));
            Console.WriteLine($"{mode}: {optimization.Termination}; qualified={final.MeetsTargets}; lost={final.Fields.Sum(field => field.CheckAttemptedRays - field.CheckValidRays)}; rays={rays}");
        }
        return 0;
    }

    public static async Task<int> Diagnose(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--wide-field-diagnose <retained checkpoint> <new directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var endpoint = state.Candidates.Where(candidate => candidate.Assessment.Completed).OrderBy(candidate => candidate.Score).First();
        var variables = state.Events.First(item => item.Optimization is not null).Optimization!.Variables;
        PupilSample[] checks = [new(0, 0, 1), .. Enumerable.Range(0, 32).Select(index => new PupilSample(Math.Cos(2 * Math.PI * index / 32), Math.Sin(2 * Math.PI * index / 32), 1))];
        var sampling = state.Plan.Options.Full.Sampling;
        var pupils = ApertureSampler.GenerateGaussianQuadrature(sampling.PupilRings, sampling.PupilArms).ToArray();
        var densePupils = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
        var results = new List<object>();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(1_000_000);
        foreach (var candidate in new[] { state.Candidates.Single(item => item.Id == "seed-0"), endpoint })
        {
            var compiled = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints); var optic = compiled.CreateOptic();
            var binding = TopologyParameterBinder.Bind(compiled, variables);
            var objective = new FixedTopologyObjective(compiled, binding, state.Plan.Targets, sampling);
            var evaluated = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true, includeConstraintTangents: true);
            if (!evaluated.Available) throw new InvalidDataException(evaluated.Failure);
            var fields = objective.Fields.Select(field => (field.X, field.Y)).ToArray();
            var integrated = DifferentiableOpticalMetrics.EvaluateAtFields(optic, binding.CoreParameters, fields, pupils, usePhysicalStop: true);
            var boundary = DifferentiableOpticalMetrics.EvaluateAtFields(optic, binding.CoreParameters, fields, checks, usePhysicalStop: true);
            var metrics = fields.Select((field, index) =>
            {
                var physical = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y, densePupils, checks, includeSurfaceTransmission: false);
                return new
                {
                    Field = field,
                    AngleDegrees = field.Y * 21,
                    GaussianMaximumUm = integrated.Fields[index].MaximumRadiusMillimeters.Value * 1000,
                    CommonCheckMaximumUm = DifferentiableOpticalMetrics.MaximumRadiusWithChecks(integrated.Fields[index], boundary.Fields[index]).Value * 1000,
                    TrainingRmsUm = integrated.Fields[index].RmsRadiusMillimeters.Value * 1000,
                    TrainingDistortionPercent = Math.Sqrt(integrated.Fields[index].DistortionSquaredFraction.Value) * 100,
                    FormalMaximumUm = physical.MaximumRadius * 1000,
                    FormalRmsUm = physical.Integration.Metrics?.RmsSpotRadius * 1000,
                    FormalDistortionPercent = DistortionMetrics.ParaxialChiefFractionAtField(optic, field.X, field.Y) * 100,
                    physical.Integration.RayCount,
                    physical.Integration.VignettedRayCount,
                    BoundaryLost = physical.Checks.VignettedRayCount
                };
            }).ToArray();
            var rays = new List<object>();
            foreach (var wave in optic.Wavelengths.Where(wave => wave.Weight > 0))
            {
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, wave.Micrometers, checks, aimAtStop: true);
                var margins = optic.SequentialRayTracer.DiagnoseCircularApertures(bundle);
                for (var index = 0; index < bundle.Rays.Count; index++)
                {
                    var physical = optic.SequentialRayTracer.DiagnoseApertures(bundle.Rays[index]); var margin = margins[index];
                    rays.Add(new
                    {
                        wave.Nanometers,
                        Index = index,
                        Pupil = checks[index],
                        physical.InsideAllApertures,
                        BlockedAt = physical.FirstBlockedSurfaceIndex is { } blocked ? compiled.SurfaceIds[blocked] : null,
                        LimitedAt = margin.LimitingSurfaceIndex is { } limited ? compiled.SurfaceIds[limited] : null,
                        margin.MinimumClearanceMillimeters
                    });
                }
            }
            var tangentRows = evaluated.ConstraintTangents!.Value.Select(row => new
            {
                Id = objective.ResidualIds[row.Row],
                row.Value,
                Norm = row.Derivatives.Aggregate(0.0, double.Hypot),
                row.Derivatives
            }).ToArray();
            results.Add(new
            {
                candidate.Id,
                candidate.Assessment,
                Metrics = metrics,
                Rays = rays,
                Variables = variables,
                TangentRows = tangentRows,
                Objective = evaluated,
                Steps = state.Events.Where(item => item.TrialId == candidate.Id).Select(item => item.Optimization?.Steps).ToArray()
            });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "diagnosis.json"), JsonSerializer.Serialize(new { SourceCheckpoint = Path.GetFullPath(args[0]), ActualDiagnosticRays = quota.RayCount, Results = results }, TopologySamplingBenchmarks.Json));
        return 0;
    }
}
