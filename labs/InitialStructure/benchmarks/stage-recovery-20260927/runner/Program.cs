using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

var json = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = { new JsonStringEnumConverter() }
};
if (args.Length > 2 && args[2] == "--joint") { JointProbe.Run(args, json); return; }
var source = JsonSerializer.Deserialize<ProbeSource>(File.ReadAllText(args[0]), json)!;
var rows = new List<object>();
var bridge = args.Length > 2 && args[2] == "--bridge";
foreach (var trial in source.Trials)
    foreach (var joint in new[] { false, true })
    {
        var previous = trial.PreviousStage;
        var problem = new FlatStartDesignProblem(source.Specification, trial.Family.ElementCount, trial.Template, trial.Family);
        var vector = problem.Vector(trial.Start);
        // Recalculate the recorded entry at its original accounting position.
        var count = trial.EvaluationNumber - 1;
        var target = trial.Stage;
        var stage = target;
        var evaluation = Evaluate(vector, stage);
        if (evaluation.HasContinuousSearchResiduals) throw new InvalidDataException("Expected frozen propagation failure.");
        var probes = new List<object>();
        for (var i = 0; i < 8 && !evaluation.HasContinuousSearchResiduals; i++)
        {
            if (bridge)
                stage = new((previous.PupilFraction + stage.PupilFraction) / 2, (previous.FieldFraction + stage.FieldFraction) / 2, target.AllWavelengths);
            else
                for (var surface = 0; surface < 2 * trial.Family.ElementCount; surface++) vector[surface] *= .5;
            evaluation = Evaluate(vector, stage);
            probes.Add(new { stage, evaluation.HasContinuousSearchResiduals, evaluation.GeometryFeasible });
        }
        if (evaluation.HasContinuousSearchResiduals)
        {
            var remaining = trial.AllocatedEvaluations - count - 2;
            if (joint)
            {
                var map = problem.SolverCoordinates;
                var latest = evaluation;
                BoundedTrustRegionLeastSquares.Solve(map.Encode(vector), map.Lower, map.Upper,
                    (point, _) => { latest = Evaluate(map.Decode(point), stage); return new(latest.Residuals, latest.HasContinuousSearchResiduals); },
                    new() { MaximumEvaluations = remaining, InitialRadius = .2, StepMethod = LeastSquaresStepMethod.Regularized, ScaleByJacobian = true, MaximumSecantUpdates = 4 },
                    acceptedStep: (point, _) => vector = map.Decode(point));
            }
            else
            {
                var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, evaluation, remaining,
                    values => Evaluate(values, stage), true, () => false, default, (_, _, _) => { }, (_, _) => { });
                vector = result.Variables;
            }
        }
        var final = Evaluate(vector, target, true);
        rows.Add(new
        {
            trial.TrialId,
            trial.Family,
            Joint = joint,
            Probes = probes,
            Evaluations = count,
            final.HasContinuousSearchResiduals,
            final.IsFeasible,
            final.MeetsTargets,
            final.Merit,
            Rms = final.Fields.Max(f => f.RmsRadiusMillimeters),
            final.Violations
        });
        File.WriteAllText(args[1], JsonSerializer.Serialize(rows, json));
        Console.WriteLine($"{trial.TrialId}, joint={joint}: {stage}, continuous={final.HasContinuousSearchResiduals}, feasible={final.IsFeasible}, RMS={final.Fields.Max(f => f.RmsRadiusMillimeters):G6}, evals={count}");
        DesignEvaluation Evaluate(double[] values, DesignStage current, bool independent = false)
        {
            count++;
            return problem.Evaluate(problem.CreateOptic(values, current), current, true, default, independent);
        }
    }

internal sealed record ProbeSource(InitialStructureSpecification Specification, ProbeTrial[] Trials);
internal sealed record ProbeTrial(string TrialId, FlatStartFamily Family, OpticSnapshot Template,
    OpticSnapshot Start, DesignStage Stage, DesignStage PreviousStage, int EvaluationNumber, int AllocatedEvaluations);
