using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

if (args.Length > 2) { await SearchVerification.Run(args); return; }

var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
var inputs = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(args[0]), json)!;
var rows = new List<object>();
foreach (var input in inputs)
    foreach (var updates in new[] { 0, 4 })
    {
        var spec = input.Specification with
        { FlatStart = input.Specification.FlatStart! with { SamplingPolicy = FlatStartSamplingPolicy.UniformAreaGaussianV1 } };
        var problem = new FlatStartDesignProblem(spec, input.Family.ElementCount, input.Initial, input.Family);
        var map = problem.SolverCoordinates;
        var initial = problem.Vector(input.Initial);
        var entry = Evaluate(initial);
        var best = entry;
        var latest = entry;
        var bestVector = initial.ToArray();
        var result = BoundedTrustRegionLeastSquares.Solve(map.Encode(initial), map.Lower, map.Upper,
            (point, _) =>
            {
                var before = problem.TracedRayCount;
                latest = Evaluate(map.Decode(point));
                return new(latest.Residuals, latest.HasContinuousSearchResiduals, problem.TracedRayCount - before);
            }, new()
            {
                MaximumEvaluations = 600,
                InitialRadius = .2,
                StepMethod = LeastSquaresStepMethod.Regularized,
                ScaleByJacobian = true,
                MaximumSecantUpdates = updates
            },
            acceptedStep: (point, _) =>
            {
                if (Rank(latest) < Rank(best) || Rank(latest) == Rank(best) && latest.Merit < best.Merit)
                { best = latest; bestVector = map.Decode(point); }
            });
        var fresh = Evaluate(bestVector);
        if (ContentFingerprint.Compute(fresh) != ContentFingerprint.Compute(best)) throw new Exception("Fresh solve evaluation mismatch.");
        var validation = problem.Evaluate(Optic.FromSnapshot(best.EvaluatedOptic!), FlatStartDesignProblem.FullStage, true, default, true);
        rows.Add(new
        {
            input.Id,
            input.Family.ElementCount,
            MaximumSecantUpdates = updates,
            InputHash = ContentFingerprint.Compute(input.Initial),
            Entry = Summary(entry),
            Final = Summary(validation),
            Snapshot = validation.EvaluatedOptic,
            result.EvaluationCount,
            result.DifferenceEvaluationCount,
            result.JacobianBuildCount,
            result.SecantUpdateCount,
            result.WorkUnits,
            Termination = result.Termination.ToString(),
            AcceptedSteps = result.Trials.Count(t => t.Accepted),
            RejectedSteps = result.Trials.Count(t => !t.Accepted),
            RecalculationIdentical = true
        });
        File.WriteAllText(args[1], JsonSerializer.Serialize(rows, json));
        Console.WriteLine($"F/{spec.FNumber}, n={input.Family.ElementCount}, updates={updates}: RMS {entry.Fields.Max(f => f.RmsRadiusMillimeters):G6}->{validation.Fields.Max(f => f.RmsRadiusMillimeters):G6}; pass={validation.MeetsTargets}; accepted={result.Trials.Count(t => t.Accepted)}, J={result.JacobianBuildCount}, B={result.SecantUpdateCount}");
        DesignEvaluation Evaluate(double[] vector) => problem.Evaluate(problem.CreateOptic(vector, FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, default);
    }
static int Rank(DesignEvaluation e) => e.MeetsTargets ? 0 : e.IsFeasible ? 1 : 2;
static object Summary(DesignEvaluation e) => new
{
    e.Merit,
    e.MeetsTargets,
    e.GeometryFeasible,
    e.HasContinuousSearchResiduals,
    e.EffectiveFocalLengthMillimeters,
    e.FNumber,
    e.Fields,
    e.Violations,
    e.Objective
};
internal sealed record Input(string Id, InitialStructureSpecification Specification, FlatStartFamily Family, OpticSnapshot Initial);
