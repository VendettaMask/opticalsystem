using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Optimization;

public sealed record InequalityLinearization(double[] Values, double[,]? Jacobian);
public sealed record NonlinearCorrectionResult(double[] Point, int Iterations, int ValueEvaluations,
    int JacobianEvaluations, int Subproblems, int ActiveSetIterations, int SubproblemLimits,
    double InitialViolation, double FinalViolation);

/// <summary>Bounded correction of a proposed step against nonlinear inequalities g(x) &lt;= 0.</summary>
public static class NonlinearInequalityCorrection
{
    public const string Version = "trial-constraint-correction/1";

    // The first projection measures its nonlinear remainder. A second projection
    // uses that observed remainder as an inward buffer. This generates a trial,
    // not an acceptance test or a certificate of constraint feasibility.
    public static NonlinearCorrectionResult Correct(double[] start, double[] trial, double[] lower, double[] upper,
        Func<double[], bool, InequalityLinearization> read)
    {
        ArgumentNullException.ThrowIfNull(start); ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(lower); ArgumentNullException.ThrowIfNull(upper); ArgumentNullException.ThrowIfNull(read);
        var n = start.Length;
        if (n == 0 || n > 511 || trial.Length != n || lower.Length != n || upper.Length != n
            || Enumerable.Range(0, n).Any(j => !double.IsFinite(start[j]) || !double.IsFinite(trial[j])
                || !double.IsFinite(lower[j]) || !double.IsFinite(upper[j]) || lower[j] > upper[j]
                || start[j] < lower[j] || start[j] > upper[j] || trial[j] < lower[j] || trial[j] > upper[j]))
            throw new ArgumentException("Invalid bounded nonlinear-correction coordinates.");
        var precision = Math.Sqrt(Math.BitIncrement(1.0) - 1);
        var radius = trial.Zip(start, (a, b) => Math.Abs(a - b)).Max() / 2;
        var current = trial.ToArray(); var iterations = 0; var valueCalls = 0; var jacobianCalls = 0;
        var subproblems = 0; var activeIterations = 0; var limits = 0;
        InequalityLinearization Read(double[] point, bool derivatives)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var s = read(point, derivatives);
            if (derivatives) jacobianCalls++; else valueCalls++;
            if (s.Values.Length == 0 || s.Values.Length + 2 * n + 2 > 8192
                || derivatives && (s.Jacobian is null || s.Jacobian.GetLength(0) != s.Values.Length || s.Jacobian.GetLength(1) != n))
                throw new ArgumentException("Invalid nonlinear-correction linearization.");
            return s;
        }
        var values = Read(current, false).Values; var initialViolation = values.Max();
        NonlinearCorrectionResult Result() => new(current, iterations, valueCalls, jacobianCalls, subproblems,
            activeIterations, limits, initialViolation, values.Max());
        if (radius == 0 || !(initialViolation > 0) || !double.IsFinite(initialViolation)) return Result();
        var interior = initialViolation * precision;
        for (; iterations < 2;)
        {
            iterations++;
            var system = Read(current, true); var level = system.Values.Max() + interior;
            if (!(level > 0) || !double.IsFinite(level)) break;
            var rows = Enumerable.Range(0, system.Values.Length).Where(i => double.IsFinite(system.Values[i])).ToArray();
            var m = rows.Length; var objective = new double[1, n + 1]; objective[0, n] = 1;
            var normals = new double[m + 2 * n + 2, n + 1]; var bounds = new double[normals.GetLength(0)];
            for (var i = 0; i < m; i++)
            {
                bounds[i] = Math.Max(0, level - system.Values[rows[i]] - interior); normals[i, n] = -1;
                for (var j = 0; j < n; j++) normals[i, j] = system.Jacobian![rows[i], j];
            }
            for (var j = 0; j < n; j++)
            {
                normals[m + 2 * j, j] = 1; normals[m + 2 * j + 1, j] = -1;
                bounds[m + 2 * j] = Math.Max(0, Math.Min(radius, upper[j] - current[j]));
                bounds[m + 2 * j + 1] = Math.Max(0, Math.Min(radius, current[j] - lower[j]));
            }
            normals[m + 2 * n, n] = -1; bounds[^2] = level; normals[m + 2 * n + 1, n] = 1;
            var step = InequalityLeastSquaresStep.Solve(objective, [level], normals, bounds, precision);
            subproblems++; activeIterations += step.ActiveSetIterations; if (!step.Converged) limits++;
            var improved = false;
            for (var fraction = 1.0; fraction >= 1.0 / 8; fraction /= 2)
            {
                var candidate = current.Select((v, j) => Math.Clamp(v + fraction * step.Step[j], lower[j], upper[j])).ToArray();
                var candidateValues = Read(candidate, false).Values;
                if (!(candidateValues.Max() < values.Max())) continue;
                current = candidate; values = candidateValues; improved = true; break;
            }
            if (!improved || values.Max() <= -interior) break;
            interior = Math.Max(interior, values.Max());
        }
        return Result();
    }
}
