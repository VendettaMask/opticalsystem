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
    internal static async Task Run(string previousPath, string output, bool sixthOrder = false)
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
        var acquisition = input.RootElement.GetProperty("acquisition").Deserialize<AutonomousSearchWork>(json)!;
        var oldFunctions = sixthOrder ? previous.GetProperty("functionsIncludingAcquisition").GetInt32() - acquisition.FunctionEvaluations
            : previous.GetProperty("functions").GetInt32();
        var oldGradients = sixthOrder ? previous.GetProperty("gradientsIncludingAcquisition").GetInt32() - acquisition.GradientEvaluations
            : previous.GetProperty("gradients").GetInt32();
        var oldRays = sixthOrder ? previous.GetProperty("raysIncludingAcquisition").GetInt64() - acquisition.TracedRays
            : previous.GetProperty("tracedRays").GetInt64();
        var oldElapsed = sixthOrder ? previous.GetProperty("activeElapsedIncludingAcquisition").Deserialize<TimeSpan>(json) - acquisition.Elapsed
            : previous.GetProperty("elapsed").Deserialize<TimeSpan>(json);
        var endpoint = (sixthOrder ? previous.GetProperty("result").GetProperty("topology") : previous.GetProperty("endpoint")).Deserialize<DesignTopology>(json)!;
        var anchored = (sixthOrder ? previous.GetProperty("result").GetProperty("variables") : previous.GetProperty("phases")[0].GetProperty("variables"))
            .Deserialize<ImmutableArray<TopologyVariableRequest>>(json);
        var upgraded = StructureEvolutionSearch.ReleaseTransmissionProfiles(endpoint, plan);
        if (sixthOrder)
        {
            if (plan.Options.Expansion is not { EnableProfile: true, MaximumEvenPower: >= 6 }) throw new ArgumentException("Original sixth-order permission is required.");
            var faces = upgraded.Elements.Where(e => !e.Protected).SelectMany(e => new[] { (e.FrontSurfaceId, e.Front), (e.BackSurfaceId, e.Back) })
                .Where(f => f.Item2.Profile.MaximumEvenPower < 6).Select(f => f.Item1).ToArray();
            foreach (var face in faces)
            {
                var edit = TopologyOperators.UpgradeProfile(upgraded, face, 6, plan.Constraints);
                if (!edit.Succeeded) throw new ArgumentException(edit.Failure!.Message);
                upgraded = edit.Topology!;
            }
        }
        // Keep every old physical bound anchored to the original generated layout.
        // Only append the coefficients explicitly permitted by the original task.
        var variables = anchored.AddRange(StructureEvolutionSearch.EvolutionVariables(upgraded, plan.Options)
            .Where(v => v.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient
                && !anchored.Any(old => old.SurfaceId == v.SurfaceId && old.Kind == v.Kind && old.Power == v.Power)));
        var source = TopologyCompiler.Compile(upgraded, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, variables, exactMechanicalFrame: true);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(10_000_000 - acquisition.TracedRays - oldRays);
        var watch = Stopwatch.StartNew(); var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var functionBudget = Math.Min(sixthOrder ? 120 : 200, 600 - acquisition.FunctionEvaluations - oldFunctions - (sixthOrder ? 1 : 0));
        var gradientBudget = Math.Min(functionBudget - 1, 600 - acquisition.GradientEvaluations - oldGradients);
        var result = FixedTopologyOptimizer.Run(source, binding, plan.Targets, new()
        {
            GateCompensationVersion = 2, MaximumUpdates = functionBudget - 2, MaximumFunctionEvaluations = functionBudget,
            MaximumGradientEvaluations = gradientBudget, TimeLimit = TimeSpan.FromSeconds(sixthOrder ? 270 : 300), ComputeMode = DesignComputeMode.Cpu,
            PerformFinalAssessment = !sixthOrder,
            Sampling = new() { FieldsPerDirection = 3, PupilRings = 10, PupilArms = 32,
                IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true, ProfileRadialSamples = 64 }
        });
        var assessment = sixthOrder ? TopologyValidator.Validate(TopologyCompiler.Compile(result.Topology, plan.Constraints), plan.Targets)
            : result.FinalAssessment;
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            DiagnosticOnly = true, ReusedSelectedDevelopmentEndpoint = true, previousPath, inputPath,
            OriginalTargets = plan.Targets, OriginalConstraints = plan.Constraints, OriginalPermissions = plan.Options,
            UnchangedOldVariables = anchored, AddedPermittedCoefficientVariables = variables.Length - anchored.Length,
            ExtraWallAllocationSeconds = 300, IntroducedMaximumEvenPower = sixthOrder ? 6 : 4, PreviousActiveElapsed = oldElapsed,
            ActiveElapsedIncludingAcquisition = oldElapsed + acquisition.Elapsed + watch.Elapsed,
            FunctionsIncludingAcquisition = oldFunctions + acquisition.FunctionEvaluations + result.Work.FunctionEvaluations + (sixthOrder ? 1 : 0),
            GradientsIncludingAcquisition = oldGradients + acquisition.GradientEvaluations + result.Work.GradientEvaluations,
            RaysIncludingAcquisition = oldRays + acquisition.TracedRays + rays.RayCount,
            Elapsed = watch.Elapsed, Cpu = Process.GetCurrentProcess().TotalProcessorTime - cpu, result, assessment
        }, json));
        if (assessment is { Completed: true, MeetsTargets: true })
        {
            var cold = TopologyValidator.Validate(TopologyCompiler.Compile(result.Topology, plan.Constraints), plan.Targets);
            if (!cold.Completed || !cold.MeetsTargets) throw new InvalidDataException("Cold original replay failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new("back-focus-asphere", null, result.Topology, cold, 0), plan.Constraints,
                Path.Combine(output, "qualified.staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, "cold-export.json"), JsonSerializer.Serialize(new { SearchBudgetInput = false, exported, cold }, json));
        }
        Console.WriteLine($"Asphere continuation {result.Termination}, accepted={result.AcceptedUpdates}, qualified={assessment?.MeetsTargets}");
    }
}
