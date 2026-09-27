using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), json)!;
var rows = new List<object>();
foreach (var input in inputs)
{
    var problem = new FlatStartDesignProblem(input.Specification, input.Family.ElementCount, input.Initial, input.Family);
    var coordinates = problem.SolverCoordinates;
    var vector = coordinates.Encode(problem.Vector(input.Initial));
    var entry = Evaluate(vector);
    var changes = input.Initial.Surfaces.Zip(entry.EvaluatedOptic!.Surfaces).Select((pair, index) => new
    {
        Surface = index,
        RadiusDelta = pair.Second.Radius - pair.First.Radius,
        ThicknessDelta = pair.Second.Thickness == pair.First.Thickness ? 0 : pair.Second.Thickness - pair.First.Thickness,
        SemiDiameterDelta = pair.Second.SemiDiameter - pair.First.SemiDiameter
    }).ToArray();
    var unavailable = new List<object>();
    for (var column = 0; column < vector.Length; column++)
    {
        var attempts = new List<object>();
        var delta = 6.055454452393343e-6 * Math.Max(1, Math.Abs(vector[column]));
        var available = false;
        for (var attempt = 0; attempt < 4; attempt++, delta *= .25)
        {
            var plus = vector.ToArray(); var minus = vector.ToArray();
            plus[column] = Math.Min(coordinates.Upper[column], plus[column] + delta);
            minus[column] = Math.Max(coordinates.Lower[column], minus[column] - delta);
            var up = plus[column] > vector[column] ? Evaluate(plus) : null;
            var down = minus[column] < vector[column] ? Evaluate(minus) : null;
            available = Valid(up) || Valid(down);
            attempts.Add(new
            {
                Attempt = attempt + 1,
                Delta = delta,
                UpValid = Valid(up),
                DownValid = Valid(down),
                UpGeometryViolations = up?.Violations.Where(v => v.Code.StartsWith("geometry.")).ToArray(),
                DownGeometryViolations = down?.Violations.Where(v => v.Code.StartsWith("geometry.")).ToArray()
            });
            if (available) break;
        }
        if (!available) unavailable.Add(new { Column = column, Attempts = attempts });
    }
    var sampling = new List<object>();
    // All metrics are returned by formal Core; only request settings and comparisons live here.
    foreach (var mode in new[] { "legacy-97", "hexapolar-16", "gaussian-10x32" })
    {
        IReadOnlyList<PupilSample> pupils = mode switch
        {
            "legacy-97" => FlatStartDesignProblem.Pupils(true).ToArray(),
            "hexapolar-16" => ApertureSampler.GenerateHexapolarRings(16),
            _ => ApertureSampler.GenerateGaussianQuadrature(10, 32)
        };
        var optic = Optic.FromSnapshot(input.Initial); // No resizing or optimization during validation.
        var fields = Enumerable.Range(0, 41).Select(i =>
        {
            var normalized = i / 40.0;
            var spot = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, normalized, pupils, includeSurfaceTransmission: false);
            return new { NormalizedField = normalized, spot.RayCount, spot.VignettedRayCount, spot.Metrics };
        }).ToArray();
        sampling.Add(new
        {
            Mode = mode,
            PupilSamples = pupils.Count,
            FieldCount = fields.Length,
            WorstRms = fields.Max(f => f.Metrics?.RmsSpotRadius),
            WorstMaximumRadius = fields.Max(f => f.Metrics?.MaximumSpotRadius),
            TotalLost = fields.Sum(f => f.VignettedRayCount),
            PassSpotAndThroughput = fields.All(f => f.Metrics is { } m && m.RmsSpotRadius <= input.Specification.MaximumRmsSpotRadiusMillimeters
                && m.MaximumSpotRadius <= input.Specification.MaximumSpotRadiusMillimeters
                && (double)(f.RayCount - f.VignettedRayCount) / f.RayCount >= input.Specification.FlatStart!.MinimumValidRayFraction),
            Fields = fields
        });
    }
    rows.Add(new
    {
        input.Id,
        input.Family.ElementCount,
        EntryMeetsTargets = entry.MeetsTargets,
        ReconstructedParameterChanges = changes,
        UnavailableDerivativeColumns = unavailable,
        Sampling = sampling
    });
    File.WriteAllText(args[1], JsonSerializer.Serialize(rows, json));
    Console.WriteLine($"{input.Id}: {unavailable.Count} unavailable columns, accepted={entry.MeetsTargets}");

    DesignEvaluation Evaluate(IReadOnlyList<double> point) => problem.Evaluate(
        problem.CreateOptic(coordinates.Decode(point), FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, default);
}

static bool Valid(DesignEvaluation? e) => e is { GeometryFeasible: true, HasContinuousSearchResiduals: true };
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
