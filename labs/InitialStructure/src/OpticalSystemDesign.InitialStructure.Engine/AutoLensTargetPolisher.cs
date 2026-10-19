using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Product integration handoff, distinct from the automatically differentiated Adam stages.</summary>
internal static class AutoLensTargetPolisher
{
    public static OpticSnapshot Polish(InitialStructureSpecification specification, int elements, FlatStartFamily? family,
        OpticSnapshot start, int maximumEvaluations, Func<bool> stop, Action chargeEvaluation, Action<long> chargeRays,
        Action<SearchDiagnostic> diagnostic, CancellationToken cancellationToken)
    {
        if (maximumEvaluations < 2 || stop()) return start;
        var problem = new FlatStartDesignProblem(specification, elements, start, family);
        var vector = problem.Vector(start);
        var used = 0;
        var initial = Evaluate(vector);
        var best = initial;
        if (initial.HasContinuousSearchResiduals)
            FlatStartLocalSolver.Solve(problem.SolverCoordinates, vector, initial, maximumEvaluations - 1,
                Evaluate, false, () => used >= maximumEvaluations || stop(), cancellationToken,
                (_, evaluation, _) =>
                {
                    if (Rank(evaluation) < Rank(best) || Rank(evaluation) == Rank(best)
                        && FlatStartCandidateArchive.Score(evaluation.Objective) < FlatStartCandidateArchive.Score(best.Objective)) best = evaluation;
                },
                (phase, statistics) => diagnostic(new("design.autolens-native-target-polish",
                    $"{phase}: {statistics.Termination}; {statistics.EvaluationCount} evaluations, {statistics.JacobianBuildCount} native numeric Jacobians. Asphere coefficients and real glasses remain fixed; Core residuals and manufacturing gates are unchanged.")),
                () => problem.TracedRayCount);
        diagnostic(new("design.autolens-native-target-polish-work", $"{used} separately charged native full-target evaluations; not part of automatic-gradient Adam step counts."));
        return best.EvaluatedOptic!;

        DesignEvaluation Evaluate(double[] values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (used >= maximumEvaluations) throw new InvalidOperationException("Native target-polish evaluation quota ended.");
            used++;
            chargeEvaluation();
            var before = problem.TracedRayCount;
            try { return problem.Evaluate(problem.CreateOptic(values, FlatStartDesignProblem.FullStage), FlatStartDesignProblem.FullStage, true, cancellationToken); }
            finally { chargeRays(problem.TracedRayCount - before); }
        }

        static int Rank(DesignEvaluation evaluation) => evaluation.MeetsTargets ? 0 : evaluation.IsFeasible ? 1 : 2;
    }
}
