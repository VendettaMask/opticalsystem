using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

// Development diagnostic. It never replaces a cold, bounded original-task run.
internal static class EnvelopeStartsProbe
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
        var parent = plan.Seeds[0]; var source = TopologyCompiler.Compile(parent, plan.Constraints); var optic = source.CreateOptic();
        var wave = parent.Wavelengths.Single(w => w.IsPrimary).Nanometers;
        var segments = parent.Elements.Select(e => new ThickDelanoElement(e.FrontZMillimeters, e.ThicknessMillimeters,
            optic.Materials.Resolve(e.Material).RefractiveIndex(wave))).ToArray();
        var random = new DeterministicRandom(plan.Options.Seed); var watch = Stopwatch.StartNew();
        var history = new List<object>(); var functions = 0; var matrices = 0; long probes = 0;
        var best = double.PositiveInfinity; DesignTopology? endpoint = null;
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        using var serial = ComputationParallelism.SuppressNestedParallelism();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        for (var restart = 0; restart < 8 && !cancel.IsCancellationRequested; restart++)
        {
            var shape = Enumerable.Range(0, 2 * segments.Length - 1).Select(i => restart == 0 ? .1 + .8 * i / (2 * segments.Length - 2)
                : 3.5 * Math.Cos((restart + 1) / 2 * Math.PI * i / (2 * segments.Length - 2)) * (restart % 2 == 0 ? 1 : -1)).ToArray();
            var current = Evaluate(shape);
            for (var iteration = 0; iteration < 600 && !cancel.IsCancellationRequested; iteration++)
            {
                var next = shape.ToArray();
                // Local changes preserve useful neighbouring slopes instead of redrawing every degree.
                var index = random.NextInt32(next.Length);
                var width = iteration % 4 == 0 ? next.Length : 1;
                for (var j = 0; j < width; j++)
                {
                    var k = (index + j) % next.Length;
                    next[k] = Math.Clamp(next[k] + .6 * (2 * random.NextUnitDouble() - 1), -4, 4);
                }
                var score = Evaluate(next);
                var temperature = .1 * (1 - iteration / 600.0);
                if (score < current || double.IsFinite(score) && random.NextUnitDouble() < Math.Exp(Math.Min(0, (current - score) / temperature)))
                { current = score; shape = next; }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(output, "construction.json"), JsonSerializer.Serialize(new
        { DiagnosticOnly = true, inputPath, functions, matrices, probes, elapsed = watch.Elapsed, best, endpoint, history }, json));
        if (endpoint is null) return;
        var assessment = TopologyValidator.Validate(TopologyCompiler.Compile(endpoint, plan.Constraints), plan.Targets);
        await File.WriteAllTextAsync(Path.Combine(output, "original-assessment.json"), JsonSerializer.Serialize(assessment, json));
        Console.WriteLine($"Envelope probe functions={functions} best={best:G8} qualified={assessment.MeetsTargets}");

        double Evaluate(double[] shape)
        {
            functions++;
            try
            {
                var built = FixedStopDelano.Construct(segments, plan.Targets.EffectiveFocalLengthMillimeters / (2 * plan.Targets.FNumber),
                    parent.Stop.ZMillimeters, parent.Stop.ClearSemiDiameterMillimeters, parent.Image.ZMillimeters,
                    plan.Targets.EffectiveFocalLengthMillimeters, shape);
                var curvature = built.FrontCurvaturesPerMillimeter.Concat(built.BackCurvaturesPerMillimeter).ToArray();
                var score = curvature.Sum(c => Math.Pow(Math.Max(0, Math.Abs(c) / plan.Options.CurvatureBoundPerMillimeter - 1), 2));
                if (curvature.Any(c => Math.Abs(c) > .5)) return 1e4 + score;
                var candidate = parent with { Elements = parent.Elements.Select((e, i) => e with
                {
                    Front = e.Front with { Profile = e.Front.Profile with { CurvaturePerMillimeter = built.FrontCurvaturesPerMillimeter[i] } },
                    Back = e.Back with { Profile = e.Back.Profile with { CurvaturePerMillimeter = built.BackCurvaturesPerMillimeter[i] } }
                }).ToImmutableArray() };
                var compiled = TopologyCompiler.Compile(candidate, plan.Constraints); var evaluated = compiled.CreateOptic();
                var feasible = score == 0;
                foreach (var row in TopologyEvaluationPlan.Geometry(compiled, plan.Targets, 64))
                {
                    probes++; var value = TopologyEvaluationPlan.FormalMeasure(evaluated, row);
                    var violation = row.Upper ? value - row.Limit : row.Limit - value;
                    if (!double.IsFinite(violation)) return 1e4 + score;
                    feasible &= violation <= 0; score += Math.Pow(Math.Max(0, violation) / .1, 2);
                }
                var deficit = 0.0;
                foreach (var wavelength in parent.Wavelengths.Where(w => w.Weight > 0))
                {
                    var envelope = DifferentiablePupilEnvelopes.Evaluate(evaluated,
                        [new(compiled.SurfaceIndices[candidate.Elements[0].FrontSurfaceId], OpticalDesignParameterKind.Curvature)],
                        compiled.SurfaceIndices[parent.Reference.Id], [0, 1], wavelength.Nanometers);
                    matrices += envelope.MatrixEvaluations;
                    foreach (var sample in envelope.Samples)
                    {
                        var loss = Math.Max(0, -sample.ClearanceMillimeters.Value);
                        deficit = Math.Max(deficit, loss); score += loss * loss;
                    }
                }
                if (feasible && score < best)
                { best = score; endpoint = candidate; history.Add(new { functions, score, deficit }); }
                return score;
            }
            catch (Exception error) when (error is ArgumentException or ArithmeticException or InvalidOperationException)
            { return double.PositiveInfinity; }
        }
    }
}
