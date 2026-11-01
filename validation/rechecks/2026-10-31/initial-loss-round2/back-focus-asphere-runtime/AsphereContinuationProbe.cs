using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

internal static class AsphereContinuationProbe
{
    internal static async Task Run(string previousPath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Existing continuation evidence.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(previousPath));
        var previous = saved.RootElement;
        var inputPath = previous.GetProperty("inputPath").GetString()!;
        using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(inputPath));
        var plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var acquisition = previous.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
        var oldFunctions = previous.GetProperty("functions").GetInt32(); var oldGradients = previous.GetProperty("gradients").GetInt32();
        var oldRays = previous.GetProperty("tracedRays").GetInt64();
        var oldElapsed = previous.GetProperty("elapsed").Deserialize<TimeSpan>(json);
        var endpoint = previous.GetProperty("endpoint").Deserialize<DesignTopology>(json)!;
        var anchored = previous.GetProperty("phases")[0].GetProperty("variables").Deserialize<ImmutableArray<TopologyVariableRequest>>(json);
        var upgraded = StructureEvolutionSearch.ReleaseTransmissionProfiles(endpoint, plan);
        // Keep every old physical bound anchored to the original generated layout.
        // Only append the coefficients explicitly permitted by the original task.
        var variables = anchored.AddRange(StructureEvolutionSearch.EvolutionVariables(upgraded, plan.Options)
            .Where(v => v.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient));
        var source = TopologyCompiler.Compile(upgraded, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, variables, exactMechanicalFrame: true);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(10_000_000 - acquisition.TracedRays - oldRays);
        var watch = Stopwatch.StartNew(); var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var functionBudget = Math.Min(200, 600 - acquisition.FunctionEvaluations - oldFunctions);
        var gradientBudget = Math.Min(functionBudget - 1, 600 - acquisition.GradientEvaluations - oldGradients);
        var result = FixedTopologyOptimizer.Run(source, binding, plan.Targets, new()
        {
            GateCompensationVersion = 2, MaximumUpdates = functionBudget - 2, MaximumFunctionEvaluations = functionBudget,
            MaximumGradientEvaluations = gradientBudget, TimeLimit = TimeSpan.FromSeconds(300), ComputeMode = DesignComputeMode.Cpu,
            Sampling = new() { FieldsPerDirection = 3, PupilRings = 10, PupilArms = 32,
                IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true, ProfileRadialSamples = 64 }
        });
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            DiagnosticOnly = true, ReusedSelectedDevelopmentEndpoint = true, previousPath, inputPath,
            OriginalTargets = plan.Targets, OriginalConstraints = plan.Constraints, OriginalPermissions = plan.Options,
            UnchangedOldVariables = anchored, AddedPermittedCoefficientVariables = variables.Length - anchored.Length,
            ExtraWallAllocationSeconds = 300, PreviousActiveElapsed = oldElapsed,
            ActiveElapsedIncludingAcquisition = oldElapsed + acquisition.Elapsed + watch.Elapsed,
            FunctionsIncludingAcquisition = oldFunctions + acquisition.FunctionEvaluations + result.Work.FunctionEvaluations,
            GradientsIncludingAcquisition = oldGradients + acquisition.GradientEvaluations + result.Work.GradientEvaluations,
            RaysIncludingAcquisition = oldRays + acquisition.TracedRays + rays.RayCount,
            Elapsed = watch.Elapsed, Cpu = Process.GetCurrentProcess().TotalProcessorTime - cpu, result
        }, json));
        if (result.FinalAssessment is { Completed: true, MeetsTargets: true })
        {
            var cold = TopologyValidator.Validate(TopologyCompiler.Compile(result.Topology, plan.Constraints), plan.Targets);
            if (!cold.Completed || !cold.MeetsTargets) throw new InvalidDataException("Cold original replay failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new("back-focus-asphere", null, result.Topology, cold, 0), plan.Constraints,
                Path.Combine(output, "qualified.staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, "cold-export.json"), JsonSerializer.Serialize(new { SearchBudgetInput = false, exported, cold }, json));
        }
        Console.WriteLine($"Asphere continuation {result.Termination}, accepted={result.AcceptedUpdates}, qualified={result.FinalAssessment?.MeetsTargets}");
    }
}
