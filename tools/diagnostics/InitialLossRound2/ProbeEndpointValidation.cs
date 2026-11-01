using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class ProbeEndpointValidation
{
    internal static async Task Run(string sourcePath, string output, bool optimize = false)
    {
        if (Directory.Exists(output)) throw new IOException("Existing cold endpoint evidence.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(sourcePath));
        var root = saved.RootElement; var result = root.GetProperty("result");
        var topology = result.GetProperty("topology").Deserialize<DesignTopology>(json)!;
        var variables = result.GetProperty("variables").Deserialize<ImmutableArray<TopologyVariableRequest>>(json);
        using var originalInput = JsonDocument.Parse(await File.ReadAllBytesAsync(root.GetProperty("inputPath").GetString()!));
        var plan = originalInput.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var compiled = TopologyCompiler.Compile(topology, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(compiled, variables, exactMechanicalFrame: true);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
        if (optimize)
        {
            var polished = FixedTopologyOptimizer.Run(compiled, binding, plan.Targets, new()
            {
                GateCompensationVersion = 2, MaximumUpdates = 3, MaximumFunctionEvaluations = 7, MaximumGradientEvaluations = 6,
                TimeLimit = TimeSpan.FromSeconds(45), ComputeMode = DesignComputeMode.Cpu,
                Sampling = new() { FieldsPerDirection = 3, PupilRings = 10, PupilArms = 32, ProfileRadialSamples = 64,
                    IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
            }, deadline.Token);
            topology = polished.Topology; compiled = TopologyCompiler.Compile(topology, plan.Constraints);
            await File.WriteAllTextAsync(Path.Combine(output, "engine-polish.json"), JsonSerializer.Serialize(new
            { sourcePath, polished.Algorithm, polished.Termination, polished.AcceptedUpdates, polished.Work, polished.Diagnostics,
                polished.Steps, polished.Variables, polished.Topology, polished.FinalAssessment }, json));
        }
        var assessment = TopologyValidator.Validate(compiled, plan.Targets, deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(output, "topology.json"), JsonSerializer.Serialize(topology, json));
        await File.WriteAllTextAsync(Path.Combine(output, "cold-validation.json"), JsonSerializer.Serialize(new
        { DiagnosticOnly = true, SearchBudgetInput = false, sourcePath, OriginalTargets = plan.Targets,
            OriginalConstraints = plan.Constraints, OriginalAnchoredBoundsChecked = true, assessment }, json));
        if (assessment is { Completed: true, MeetsTargets: true })
        {
            var replay = TopologyValidator.Validate(compiled, plan.Targets, deadline.Token);
            if (!replay.Completed || !replay.MeetsTargets) throw new InvalidDataException("Repeated cold validation failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new("back-focus-asphere", null, topology, replay, 0), plan.Constraints,
                Path.Combine(output, "qualified.staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, "native-export.json"), JsonSerializer.Serialize(new { exported, replay }, json));
        }
        Console.WriteLine($"Cold endpoint complete={assessment.Completed}, qualified={assessment.MeetsTargets}, rays={assessment.TracedRayCount}");
        if (!assessment.Fields.IsEmpty)
            Console.WriteLine($"Min valid={assessment.Fields.Min(f => (double)f.ValidRays / f.AttemptedRays):G8}, boundary lost={assessment.Fields.Sum(f => f.CheckAttemptedRays - f.CheckValidRays)}, RMS={assessment.Fields.Max(f => f.RmsRadiusMillimeters) * 1000:G8}, max={assessment.Fields.Max(f => f.MaximumRadiusMillimeters) * 1000:G8} um");
    }
}
