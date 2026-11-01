using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    // Correct only a trial point; the caller retains its original optical gates.
    private static double[] CorrectNonlinearTrial(double[] start, double[] trial, double[] lower, double[] upper,
        Func<double[], bool, (double[] Values, double[,]? Jacobian)> read, GeneralCounter work, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = NonlinearInequalityCorrection.Correct(start, trial, lower, upper, (point, derivatives) =>
        { token.ThrowIfCancellationRequested(); var s = read(point, derivatives); return new(s.Values, s.Jacobian); });
        work.NonlinearCorrections += result.Iterations; work.Jacobians += result.JacobianEvaluations;
        work.InequalitySteps += result.Subproblems; work.InequalityIterations += result.ActiveSetIterations; work.InequalityLimits += result.SubproblemLimits;
        return result.Point;
    }
}
