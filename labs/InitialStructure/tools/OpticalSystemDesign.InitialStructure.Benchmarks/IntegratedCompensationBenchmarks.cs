using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class IntegratedCompensationBenchmarks
{
    // Diagnostic only: no proposal, search continuation or acceptance override.
    public static async Task<int> Audit(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("--integrated-sampling-audit <paired summary> <new output directory>");
        var output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output); Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(args[0]));
        var json = TopologySamplingBenchmarks.Json; var rows = new List<object>(); var seen = new Dictionary<string, string>();
        var complete = true;
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
        try
        {
            foreach (var run in input.RootElement.GetProperty("Runs").EnumerateArray().Where(row => row.GetProperty("Seed").GetInt64() == 42 && row.GetProperty("Group").GetString() == "C"))
            {
                var id = run.GetProperty("Id").GetString()!;
                var state = await new AutonomousSearchCheckpointStore().LoadAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, id, "run.autonomous.json"));
                var candidates = new[] { (Name: "source", Topology: state.Plan.Seeds[0]),
                    (Name: "best-physical-endpoint", Topology: state.Candidates.OrderByDescending(candidate => candidate.Assessment.MeetsTargets).ThenBy(candidate => candidate.Score).FirstOrDefault()?.Topology) };
                foreach (var candidate in candidates)
                {
                    var label = id + ":" + candidate.Name;
                    if (candidate.Topology is not { } topology) { rows.Add(new { Id = label, Available = false, Failure = "No completed physical candidate within original search quota." }); continue; }
                    var fingerprint = ContentFingerprint.Compute(topology);
                    if (seen.TryGetValue(fingerprint, out var previous)) { rows.Add(new { Id = label, Reuses = previous, TopologyFingerprint = fingerprint }); continue; }
                    seen.Add(fingerprint, label);
                    var compiled = TopologyCompiler.Compile(topology, state.Plan.Constraints);
                    var face = topology.Elements.Where(element => !element.Protected).SelectMany(element => new[] { (Id: element.FrontSurfaceId, Face: element.Front), (Id: element.BackSurfaceId, Face: element.Back) })
                        .First(pair => pair.Face.Profile.Kind != TopologyProfileKind.Plane);
                    var curvature = face.Face.Profile.CurvaturePerMillimeter;
                    var binding = TopologyParameterBinder.Bind(compiled, [new(face.Id, OpticalDesignParameterKind.Curvature, curvature - .01, curvature + .01, .01)]);
                    var before = quota.RayCount;
                    var a = new FixedTopologyObjective(compiled, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling);
                    var b = new FixedTopologyObjective(compiled, binding, state.Plan.Targets, state.Plan.Options.Full.Sampling with { PupilRings = 10, PupilArms = 32 });
                    var av = a.Evaluate(binding.InitialCoordinates, includeConstraintTangents: true);
                    var bv = b.Evaluate(binding.InitialCoordinates, includeConstraintTangents: true);
                    double[]? errors = null;
                    if (av.Available && bv.Available)
                        errors = a.Fields.Select((_, index) => Math.Abs(av.ConstraintTangents!.Value.Single(row => row.Row == a.ResidualIds.IndexOf($"field:{index}:rms")).Value
                            - bv.ConstraintTangents!.Value.Single(row => row.Row == b.ResidualIds.IndexOf($"field:{index}:rms")).Value) * state.Plan.Targets.MaximumRmsRadiusMillimeters * 1000).ToArray();
                    rows.Add(new
                    {
                        Id = label,
                        TopologyFingerprint = fingerprint,
                        Available = errors is not null,
                        Failure = av.Failure ?? bv.Failure,
                        DifferenceUm = errors,
                        MaximumDifferenceUm = errors?.Max(),
                        AccuracyWithinPointZeroFiveUm = errors?.Max() <= .05,
                        DiagnosticRays = quota.RayCount - before
                    });
                    Console.WriteLine($"{label}: available={errors is not null}; max difference um={errors?.Max():G8}; rays={quota.RayCount - before}");
                }
            }
        }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { complete = false; }
        await File.WriteAllTextAsync(Path.Combine(output, "precision-audit.json"), JsonSerializer.Serialize(new { Complete = complete, MaximumDiagnosticRays = 2_000_000, ActualDiagnosticRays = quota.RayCount, Rows = rows }, json));
        return complete ? 0 : 2;
    }
}
