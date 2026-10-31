using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

internal static class DenseProbe
{
    internal static async Task Run(string sourcePath, string output)
    {
        if (File.Exists(output)) throw new IOException("Probe evidence exists.");
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }, WriteIndented = true };
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(sourcePath));
        var previous = saved.RootElement.GetProperty("result").Deserialize<ParaxialSeedSearchResult>(json)!;
        var trial = previous.Evolution!.Trials.Where(t => t.Endpoint is not null && t.Endpoint.Elements.Length == 2
            && t.OriginalAssessment is { Completed: true }).OrderBy(t => t.OriginalAssessment!.Fields.Max(f => f.RmsRadiusMillimeters)).First();
        var source = TopologyCompiler.Compile(trial.Endpoint!, previous.SourcePlan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, trial.AnchoredVariables, exactMechanicalFrame: true);
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
        var options = new FixedTopologyOptions { GateCompensationVersion = 2, MaximumUpdates = 5,
            MaximumFunctionEvaluations = 7, MaximumGradientEvaluations = 6, TimeLimit = TimeSpan.FromSeconds(45),
            ComputeMode = DesignComputeMode.Cpu, Sampling = new() { FieldsPerDirection = 3, PupilRings = 10,
                PupilArms = 32, ProfileRadialSamples = 64, IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true } };
        var result = FixedTopologyOptimizer.Run(source, binding, previous.SourcePlan.Targets, options);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { DiagnosticOnly = true, ReusedSelectedEndpoint = true,
            sourcePath, SourceWork = previous.Work, SourceTrial = trial.Id, Options = options, Result = result }, json));
        Console.WriteLine($"Probe {result.Termination}: accepted={result.AcceptedUpdates}, rays={result.Work.TracedRays}");
        foreach (var diagnostic in result.Diagnostics) Console.WriteLine(diagnostic);
    }
}
