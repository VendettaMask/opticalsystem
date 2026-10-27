using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class TopologyEdgeLossBenchmarks
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> CurrentPlan(string[] args)
    {
        if (args.Length != 2 || File.Exists(args[1])) throw new ArgumentException("--topology-transmission-plan <prototype plan> <new plan file>");
        using var measured = SequentialTraceMeasurement.Begin();
        var plan = await AutonomousDesignFiles.LoadPlanAsync(args[0]);
        var current = plan with { Options = plan.Options with { Local = plan.Options.Local with { TransmissionPolicyVersion = 3 }, Full = plan.Options.Full with { TransmissionPolicyVersion = 3 } } };
        await AutonomousDesignFiles.SavePlanAsync(current, args[1]);
        if (ContentFingerprint.Compute(current) != ContentFingerprint.Compute(await AutonomousDesignFiles.LoadPlanAsync(args[1])) || measured.RayCount != 0)
            throw new InvalidDataException("Current plan identity or zero-work export failed.");
        return 0;
    }

    public static async Task<int> Sensitivity(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-clearance-sensitivity <checkpoint> <new output directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(directory);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var recovery = state.Events.First(item => item.Endpoint?.Elements.Length == 3 && item.Recovery?.Termination.ToString() == "Recovered");
        var full = state.Events.First(item => item.TrialId == recovery.TrialId && item.Optimization is not null && item.Phase!.Value.ToString() == "Full");
        var source = TopologyCompiler.Compile(recovery.Endpoint!, state.Plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, full.Optimization!.Variables);
        var objective = new FixedTopologyObjective(source, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(100_000);
        var value = objective.Evaluate(binding.InitialCoordinates, DesignComputeMode.Cpu, includeJacobian: true);
        if (!value.Available) throw new InvalidOperationException(value.Failure);
        var rows = Enumerable.Range(0, objective.ResidualIds.Length)
            .Where(index => objective.ResidualIds[index].Contains(":boundary:", StringComparison.Ordinal) && value.Residuals[index] > 1e-8).ToArray();
        await File.WriteAllTextAsync(Path.Combine(directory, "sensitivity.json"), JsonSerializer.Serialize(new
        {
            ActualRays = quota.RayCount,
            Stage = "recovered triple before compensation",
            ActiveBoundaryRows = rows.Length,
            Parameters = binding.Slots.Select((slot, index) => new
            {
                slot.Request,
                ClearanceGradient = rows.Sum(row => 2 * value.Residuals[row] * value.Jacobian[row][index]),
                ClearanceColumnNorm = Math.Sqrt(rows.Sum(row => value.Jacobian[row][index] * value.Jacobian[row][index])),
                PowerDerivative = value.Jacobian[0][index],
                FNumberDerivative = value.Jacobian[1][index]
            }),
            ResidualIds = objective.ResidualIds,
            Evaluation = value
        }, Json));
        return 0;
    }

    public static async Task<int> DiagnoseDeletion(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-edge-loss-deletion <checkpoint> <new output directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(directory);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var source = state.Candidates.Single(item => item.Id == "seed-0");
        var reduced = state.Candidates.First(item => item.Topology.Elements.Length == source.Topology.Elements.Length - 1);
        var removed = source.Topology.Elements.ExceptBy(reduced.Topology.Elements.Select(element => element.Id), element => element.Id).Single();
        var deletion = TopologyOperators.Delete(source.Topology, removed.Id, state.Plan.Constraints);
        if (deletion.Topology is null) throw new InvalidOperationException(deletion.Failure!.Message);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(100_000);
        var results = new List<object>();
        PupilSample[] checks = [new(0, 0, 1), .. Enumerable.Range(0, 32)
            .Select(index => new PupilSample(Math.Cos(2 * Math.PI * index / 32), Math.Sin(2 * Math.PI * index / 32), 1))];
        var phases = new[] { ("qualified-source", source.Topology), ("deletion-only", deletion.Topology) }
            .Concat(state.Events.Where(item => item.TrialId == reduced.Id && item.Endpoint is not null
                && item.Phase is OpticalSystemDesign.InitialStructure.Contracts.AutonomousSearchPhase.Recovery
                    or OpticalSystemDesign.InitialStructure.Contracts.AutonomousSearchPhase.Local
                    or OpticalSystemDesign.InitialStructure.Contracts.AutonomousSearchPhase.Full)
                .Select(item => (item.Phase!.Value.ToString(), item.Endpoint!)));
        foreach (var (role, topology) in phases)
        {
            var compiled = TopologyCompiler.Compile(topology, state.Plan.Constraints);
            var diagnostic = SampledApertureDiagnostics.Evaluate(compiled.CreateOptic(), 0, 1, checks);
            results.Add(new
            {
                Role = role,
                Elements = topology.Elements.Length,
                compiled.OpticFingerprint,
                diagnostic.IsComplete,
                Rays = diagnostic.Rays.Select(ray => new
                {
                    Ray = ray,
                    LimitingSurfaceId = ray.LimitingSurfaceIndex is { } index ? compiled.SurfaceIds[index] : null
                })
            });
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "direct-deletion.json"), JsonSerializer.Serialize(new
        {
            RemovedElement = removed,
            deletion.Change,
            ActualRays = quota.RayCount,
            Scope = "deletion-only changes only element membership. Other phase inputs are the exact retained endpoints; no new optimization is run. All diagnostics use Core boundary rays at 21 degrees.",
            Results = results
        }, Json));
        return 0;
    }

    public static async Task<int> Diagnose(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--topology-edge-loss <checkpoint> <new output directory>");
        var directory = Path.GetFullPath(args[1]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Existing evidence is never overwritten.");
        Directory.CreateDirectory(directory);
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        var state = await new AutonomousSearchCheckpointStore().LoadAsync(args[0]);
        var source = state.Candidates.Single(item => item.Id == "seed-0");
        var reduced = state.Candidates.Where(item => item.Topology.Elements.Length < source.Topology.Elements.Length && item.Assessment.Completed)
            .OrderBy(item => item.Score).First();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(250_000);
        var results = new List<object>();
        foreach (var candidate in new[] { source, reduced })
        {
            var compiled = TopologyCompiler.Compile(candidate.Topology, state.Plan.Constraints);
            var optic = compiled.CreateOptic();
            PupilSample[] checks = [new(0, 0, 1), .. Enumerable.Range(0, 32)
                .Select(index => new PupilSample(Math.Cos(2 * Math.PI * index / 32), Math.Sin(2 * Math.PI * index / 32), 1))];
            foreach (var (name, pupils) in new[] { ("training", ApertureSampler.GenerateGaussianQuadrature(1, 8).ToArray()),
                ("checks", checks), ("integration", ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray()) })
                foreach (var wave in optic.Wavelengths.Where(wave => wave.Weight > 0))
                {
                    var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, wave.Micrometers,
                        pupils, aimAtStop: true);
                    var clearance = optic.SequentialRayTracer.DiagnoseCircularApertures(bundle);
                    var rows = bundle.Rays.Select((ray, index) =>
                    {
                        var physical = optic.SequentialRayTracer.DiagnoseApertures(ray);
                        var limit = clearance[index].LimitingSurfaceIndex;
                        var blocked = physical.FirstBlockedSurfaceIndex;
                        return new
                        {
                            Index = index,
                            Pupil = pupils[index],
                            physical.InsideAllApertures,
                            ReachedImage = physical.UnclippedImage is not null,
                            FirstBlockedIndex = blocked,
                            FirstBlockedId = blocked is { } first ? compiled.SurfaceIds[first] : null,
                            MinimumSurfaceIndex = limit,
                            MinimumSurfaceId = limit is { } last ? compiled.SurfaceIds[last] : null,
                            clearance[index].MinimumClearanceMillimeters
                        };
                    }).ToArray();
                    results.Add(new
                    {
                        candidate.Id,
                        Elements = candidate.Topology.Elements.Length,
                        Sampling = name,
                        wave.Nanometers,
                        FieldYDegrees = 21,
                        Rays = rows
                    });
                }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "ray-diagnostics.json"), JsonSerializer.Serialize(new
        {
            SourceCheckpoint = Path.GetFullPath(args[0]),
            ActualRays = quota.RayCount,
            Results = results
        }, Json));
        Console.WriteLine($"Edge-loss diagnostics complete; {quota.RayCount} actual CPU rays.");
        return 0;
    }
}
