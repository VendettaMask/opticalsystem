using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    // Second-order constraint correction: relinearize at the proposed point, rather
    // than repeatedly shortening a direction tangent to a curved physical bound.
    // A temporary epigraph keeps zero feasible for the shared Core QP; only the
    // caller's original nonlinear tests can retain a corrected point.
    private static double[] CorrectNonlinearTrial(double[] start, double[] trial, double[] lower, double[] upper,
        Func<double[], bool, (double[] Values, double[,]? Jacobian)> read, GeneralCounter work, CancellationToken token)
    {
        var radius = trial.Zip(start, (a, b) => Math.Abs(a - b)).DefaultIfEmpty().Max() / 2;
        if (radius == 0) return trial;
        var current = trial; var values = read(current, false).Values;
        var initialViolation = values.Max();
        if (!(initialViolation > 0) || !double.IsFinite(initialViolation)) return trial;
        var interior = initialViolation * GeneralConstraintInteriorGuard;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            token.ThrowIfCancellationRequested(); work.NonlinearCorrections++;
            var system = read(current, true); work.Jacobians++;
            var level = system.Values.Max() + interior;
            if (!(level > 0) || !double.IsFinite(level)) break;
            var n = current.Length; var m = system.Values.Length;
            var objective = new double[1, n + 1]; objective[0, n] = 1;
            var normals = new double[m + 2 * n + 2, n + 1]; var bounds = new double[normals.GetLength(0)];
            for (var i = 0; i < m; i++)
            {
                bounds[i] = Math.Max(0, level - system.Values[i] - interior); normals[i, n] = -1;
                for (var j = 0; j < n; j++) normals[i, j] = system.Jacobian![i, j];
            }
            for (var j = 0; j < n; j++)
            {
                normals[m + 2 * j, j] = 1; normals[m + 2 * j + 1, j] = -1;
                bounds[m + 2 * j] = Math.Max(0, Math.Min(radius, upper[j] - current[j]));
                bounds[m + 2 * j + 1] = Math.Max(0, Math.Min(radius, current[j] - lower[j]));
            }
            normals[m + 2 * n, n] = -1; bounds[^2] = level; normals[m + 2 * n + 1, n] = 1;
            var step = InequalityLeastSquaresStep.Solve(objective, [level], normals, bounds, GeneralConstraintInteriorGuard);
            work.InequalitySteps++; work.InequalityIterations += step.ActiveSetIterations; if (!step.Converged) work.InequalityLimits++;
            var improved = false;
            for (var fraction = 1.0; fraction >= 1.0 / 8; fraction /= 2)
            {
                var candidate = current.Select((v, j) => Math.Clamp(v + fraction * step.Step[j], lower[j], upper[j])).ToArray();
                var candidateValues = read(candidate, false).Values;
                if (!(candidateValues.Max() < values.Max())) continue;
                current = candidate; values = candidateValues; improved = true; break;
            }
            if (!improved || values.Max() <= -interior) break;
        }
        return current;
    }
}
