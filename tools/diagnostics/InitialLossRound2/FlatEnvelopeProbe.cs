using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;

internal static class FlatEnvelopeProbe
{
    internal static async Task Run(string inputPath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Existing evidence.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, WriteIndented = true };
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(inputPath));
        var plan = saved.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var parent = plan.Seeds[0]; var source = TopologyCompiler.Compile(parent, plan.Constraints);
        var flat = parent with { Elements = parent.Elements.Select(e => e.Protected ? e : e with
        {
            Front = e.Front with { Profile = e.Front.Profile with { CurvaturePerMillimeter = 0 } },
            Back = e.Back with { Profile = e.Back.Profile with { CurvaturePerMillimeter = 0 } }
        }).ToImmutableArray() };
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        var variables = StructureEvolutionSearch.EvolutionVariables(parent, plan.Options);
        var result = ParaxialRestorer.Run(source, TopologyCompiler.Compile(flat, plan.Constraints),
            variables.Where(v => v.Kind == OpticalSystemDesign.Core.Differentiation.OpticalDesignParameterKind.Curvature), plan.Targets,
            new() { FixedStopEnvelopeVersion = 1, UseAutomaticMatrixDerivatives = true, RestorePupilMagnification = true,
                UseGeometryConstraintTangents = true, MaximumFunctionEvaluations = 300, MaximumAcceptedSteps = 290,
                AScale = .001, FocalCScale = .001, ProfileRadialSamples = 64, TimeLimit = TimeSpan.FromSeconds(60) });
        await File.WriteAllTextAsync(Path.Combine(output, "recovery.json"), JsonSerializer.Serialize(new { DiagnosticOnly = true, inputPath, result }, json));
        Console.WriteLine($"Flat envelope {result.Termination} deficit={result.EndpointEnvelopeDeficitMillimeters:G8} fn={result.Work.FunctionEvaluations}");
        var compiled = TopologyCompiler.Compile(result.Topology, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(compiled, variables, exactMechanicalFrame: true);
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(5_000_000);
        var polished = FixedTopologyOptimizer.Run(compiled, binding, plan.Targets, new()
        {
            GateCompensationVersion = 2, MaximumUpdates = 100, MaximumFunctionEvaluations = 200,
            MaximumGradientEvaluations = 200, TimeLimit = TimeSpan.FromSeconds(120), ComputeMode = DesignComputeMode.Cpu,
            Sampling = new() { FieldsPerDirection = 3, PupilRings = 2, PupilArms = 8, ProfileRadialSamples = 64,
                IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
        });
        await File.WriteAllTextAsync(Path.Combine(output, "compensation.json"), JsonSerializer.Serialize(polished, json));
        Console.WriteLine($"Flat polish {polished.Termination} accepted={polished.AcceptedUpdates} qualified={polished.FinalAssessment?.MeetsTargets}");
    }
}
