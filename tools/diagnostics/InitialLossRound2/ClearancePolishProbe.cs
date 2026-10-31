using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

// Inward parameter correction, never an aperture/acceptance tolerance change.
internal static class ClearancePolishProbe
{
    internal static async Task Run(string previousPath, string output)
    {
        if (Directory.Exists(output)) throw new IOException("Existing inward correction evidence.");
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(previousPath)); var previous = saved.RootElement;
        var inputPath = previous.GetProperty("inputPath").GetString()!;
        using var input = JsonDocument.Parse(await File.ReadAllBytesAsync(inputPath));
        var plan = input.RootElement.GetProperty("plan").Deserialize<AutonomousSearchPlan>(json)!;
        var topology = previous.GetProperty("result").GetProperty("topology").Deserialize<DesignTopology>(json)!;
        var variables = previous.GetProperty("result").GetProperty("variables").Deserialize<ImmutableArray<TopologyVariableRequest>>(json);
        var source = TopologyCompiler.Compile(topology, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, variables, exactMechanicalFrame: true);
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(90)); var token = tokenSource.Token;
        using var measurement = SequentialTraceMeasurement.BeginCpuBudget(2_000_000);
        var watch = Stopwatch.StartNew();
        var initial = TopologyValidator.Validate(source, plan.Targets, token);
        if (!initial.Completed || initial.Violations.Any(v => !v.Code.EndsWith(":check-lost", StringComparison.Ordinal)))
            throw new InvalidDataException("Inward polish requires all original non-rim gates already passed.");
        var objective = new FixedTopologyObjective(source, binding, plan.Targets, new()
        {
            FieldsPerDirection = 3, PupilRings = 10, PupilArms = 32, ProfileRadialSamples = 64,
            IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true
        });
        var evaluated = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true, cancellationToken: token, includeConstraintTangents: true);
        if (!evaluated.Available) throw new InvalidDataException(evaluated.Failure);
        var signed = evaluated.ConstraintTangents!.Value.ToDictionary(v => v.Row);
        var clipped = signed.Values.Where(v => objective.ResidualIds[v.Row].EndsWith(":clearance", StringComparison.Ordinal) && v.Value > 1e-12).ToArray();
        if (clipped.Length == 0) throw new InvalidDataException("No positive moving-aperture deficit is present in the Core model.");
        var n = variables.Length; var j = new double[clipped.Length, n]; var residuals = new double[clipped.Length];
        for (var row = 0; row < clipped.Length; row++)
        {
            residuals[row] = clipped[row].Value + 1e-4;
            for (var column = 0; column < n; column++) j[row, column] = clipped[row].Derivatives[column];
        }
        const double radius = 1e-3;
        var constraints = new List<(double[] Normal, double Upper)>();
        foreach (var item in signed.Values)
        {
            if (item.Row < 2)
            {
                constraints.Add((item.Derivatives.ToArray(), Math.Max(0, 1 - item.Value)));
                constraints.Add((item.Derivatives.Select(v => -v).ToArray(), Math.Max(0, 1 + item.Value)));
            }
            else if (item.Value <= 0) constraints.Add((item.Derivatives.ToArray(), -item.Value));
        }
        var epsilon = Math.BitIncrement(1.0) - 1;
        constraints.RemoveAll(c => c.Normal.Sum(Math.Abs) * radius <= 64 * epsilon * Math.Max(1, c.Upper));
        for (var column = 0; column < n; column++)
        {
            var positive = new double[n]; positive[column] = 1; var negative = new double[n]; negative[column] = -1;
            constraints.Add((positive, Math.Min(radius, binding.Slots[column].UpperCoordinate - binding.InitialCoordinates[column])));
            constraints.Add((negative, Math.Min(radius, binding.InitialCoordinates[column] - binding.Slots[column].LowerCoordinate)));
        }
        var normals = new double[constraints.Count, n]; var upper = new double[constraints.Count];
        for (var row = 0; row < constraints.Count; row++)
        {
            upper[row] = Math.Max(0, constraints[row].Upper);
            for (var column = 0; column < n; column++) normals[row, column] = constraints[row].Normal[column];
        }
        using var cancellation = ComputationCancellation.Push(token);
        var step = InequalityLeastSquaresStep.Solve(j, residuals, normals, upper, .001);
        var trials = new List<object>(); DesignTopology? qualified = null; TopologyAssessment? final = null;
        foreach (var fraction in new[] { 1.0, .5, .1, .01 })
        {
            var coordinates = binding.InitialCoordinates.Select((v, c) => Math.Clamp(v + fraction * step.Step[c],
                binding.Slots[c].LowerCoordinate, binding.Slots[c].UpperCoordinate)).ToArray();
            var trial = TopologyParameterBinder.Apply(source, binding, coordinates);
            var assessment = TopologyValidator.Validate(trial, plan.Targets, token);
            trials.Add(new { fraction, coordinates, assessment });
            if (assessment.Completed && assessment.MeetsTargets) { qualified = trial.Topology; final = assessment; break; }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            DiagnosticOnly = true, previousPath, inputPath, OriginalTargets = plan.Targets, OriginalConstraints = plan.Constraints,
            UnchangedVariables = variables, initial, Deficits = clipped.Select(v => new { v.Row, Id = objective.ResidualIds[v.Row], v.Value }),
            step, trials, qualified, final, ExtraFunctions = 2 + trials.Count, ExtraRays = measurement.RayCount, Elapsed = watch.Elapsed,
            FunctionsIncludingAcquisition = previous.GetProperty("functionsIncludingAcquisition").GetInt32() + 2 + trials.Count,
            RaysIncludingAcquisition = previous.GetProperty("raysIncludingAcquisition").GetInt64() + measurement.RayCount
        }, json));
        if (qualified is not null)
        {
            var cold = TopologyValidator.Validate(TopologyCompiler.Compile(qualified, plan.Constraints), plan.Targets, token);
            if (!cold.Completed || !cold.MeetsTargets) throw new InvalidDataException("Repeated original replay failed.");
            var exported = await AutonomousDesignFiles.ExportAsync(new("back-focus-inward", null, qualified, cold, 0), plan.Constraints,
                Path.Combine(output, "qualified.staropt"));
            await File.WriteAllTextAsync(Path.Combine(output, "cold-export.json"), JsonSerializer.Serialize(new { SearchBudgetInput = false, exported, cold }, json));
        }
        Console.WriteLine($"Inward correction qualified={final?.MeetsTargets}, Core deficit rows={clipped.Length}, trials={trials.Count}");
    }
}
