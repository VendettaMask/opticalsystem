using System.Text.Json;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;

internal static class JointProbe
{
    public static void Run(string[] args, JsonSerializerOptions json)
    {
        var source = JsonSerializer.Deserialize<Source>(File.ReadAllText(args[0]), json)!;
        var rows = new List<object>();
        foreach (var input in source.Inputs)
        {
            var problem = new FlatStartDesignProblem(source.Specification, input.Family.ElementCount, input.Template, input.Family);
            var vector = problem.Vector(input.Start);
            var entry = Evaluate(vector);
            var statistics = new List<object>();
            var result = FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, entry, 90, Evaluate,
                true, () => false, default, (_, _, _) => { }, (phase, stats) => statistics.Add(new
                {
                    Phase = phase.ToString(),
                    stats.EvaluationCount,
                    stats.JacobianBuildCount,
                    stats.SecantUpdateCount,
                    Termination = stats.Termination.ToString()
                }));
            var final = problem.Evaluate(problem.CreateOptic(result.Variables, FlatStartDesignProblem.FullStage),
                FlatStartDesignProblem.FullStage, true, default, true);
            rows.Add(new
            {
                input.Index,
                input.Stage,
                EntryMerit = entry.Merit,
                FinalStageMerit = result.Evaluation.Merit,
                Statistics = statistics,
                FullTarget = new
                {
                    final.MeetsTargets,
                    final.HasContinuousSearchResiduals,
                    final.IsFeasible,
                    final.Merit,
                    Rms = final.Fields.Max(field => field.RmsRadiusMillimeters),
                    final.Violations
                }
            });
            Console.WriteLine($"Root {input.Index}: same-stage merit {entry.Merit:G6} -> {result.Evaluation.Merit:G6}; full continuous={final.HasContinuousSearchResiduals}; accepted={final.MeetsTargets}");
            DesignEvaluation Evaluate(double[] values) => problem.Evaluate(problem.CreateOptic(values, input.Stage),
                input.Stage, input.Stage == FlatStartDesignProblem.FullStage, default);
        }
        File.WriteAllText(args[1], JsonSerializer.Serialize(rows, json));
    }

    private sealed record Source(InitialStructureSpecification Specification, Input[] Inputs);
    private sealed record Input(int Index, FlatStartFamily Family, OpticSnapshot Template, OpticSnapshot Start, DesignStage Stage);
}
