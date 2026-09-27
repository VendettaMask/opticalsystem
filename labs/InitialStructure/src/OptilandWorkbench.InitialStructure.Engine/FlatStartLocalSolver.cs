using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine.Optimization;

namespace OptilandWorkbench.InitialStructure.Engine;

internal enum OpticalSolvePhase { Geometry, RealRay }

internal sealed record OpticalLocalResult(double[] Variables, DesignEvaluation Evaluation, int Evaluations,
    LeastSquaresTermination Termination);

/// <summary>Restore geometry, then minimize one ray objective with a continuous Core evaluation domain.</summary>
internal static class FlatStartLocalSolver
{
    public static OpticalLocalResult Solve(FlatStartSolverCoordinates coordinates,
        double[] initial, DesignEvaluation initialEvaluation, int maximumEvaluations,
        Func<double[], DesignEvaluation> evaluate, bool improveBeyondTargets,
        Func<bool> stopRequested, CancellationToken cancellationToken,
        Action<double[], DesignEvaluation, OpticalSolvePhase> accepted,
        Action<OpticalSolvePhase, LeastSquaresResult> statistics,
        Func<long>? tracedRayCount = null)
    {
        var vector = initial.ToArray();
        var current = initialEvaluation;
        var count = 0;
        var termination = LeastSquaresTermination.EvaluationLimit;
        while (count < maximumEvaluations && !stopRequested())
        {
            // The area objective already includes finite Core geometry constraints.
            // Keep optical correction active across manufacturing bounds whenever
            // every required ray propagates; geometry-only restoration is for an
            // unavailable optical domain (and the historical separate objective).
            var joint = current.HasContinuousSearchResiduals
                && current.Objective?.Kind == FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2;
            var phase = !current.GeometryFeasible && !joint ? OpticalSolvePhase.Geometry : OpticalSolvePhase.RealRay;
            if (current.MeetsTargets && !improveBeyondTargets) break;
            var latest = current;
            var result = BoundedTrustRegionLeastSquares.Solve(coordinates.Encode(vector), coordinates.Lower, coordinates.Upper,
                (point, _) =>
                {
                    var before = tracedRayCount?.Invoke() ?? 0;
                    latest = evaluate(coordinates.Decode(point));
                    count++;
                    // The V2 joint objective includes Core geometry constraints, so
                    // finite coordinates across a manufacturing bound provide derivatives.
                    // Missing propagation data (including TIR) never does.
                    var valid = phase == OpticalSolvePhase.Geometry
                        || latest.HasContinuousSearchResiduals && (latest.GeometryFeasible
                            || latest.Objective?.Kind == FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2);
                    var residuals = phase == OpticalSolvePhase.Geometry ? latest.GeometryResiduals : latest.Residuals;
                    return new(residuals, valid, (tracedRayCount?.Invoke() ?? 0) - before);
                }, new LeastSquaresOptions
                {
                    MaximumEvaluations = maximumEvaluations - count,
                    InitialRadius = .2,
                    StepMethod = phase == OpticalSolvePhase.RealRay ? LeastSquaresStepMethod.Regularized : LeastSquaresStepMethod.Dogleg,
                    ScaleByJacobian = phase == OpticalSolvePhase.RealRay,
                    MaximumSecantUpdates = phase == OpticalSolvePhase.RealRay
                        && current.Objective?.Kind == FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2 ? 4 : 0
                },
                cancellationToken,
                stopRequested: () => stopRequested() || Completed(phase, current, improveBeyondTargets),
                acceptedStep: (point, _) =>
                {
                    vector = coordinates.Decode(point);
                    current = latest;
                    accepted(vector, current, phase);
                });
            statistics(phase, result);
            termination = result.Termination;
            if (!Completed(phase, current, improveBeyondTargets)) break;
        }
        return new(vector, current, count, termination);
    }

    private static bool Completed(OpticalSolvePhase phase, DesignEvaluation evaluation, bool improveBeyondTargets) => phase switch
    {
        OpticalSolvePhase.Geometry => evaluation.GeometryFeasible,
        _ => !improveBeyondTargets && evaluation.MeetsTargets
    };
}
