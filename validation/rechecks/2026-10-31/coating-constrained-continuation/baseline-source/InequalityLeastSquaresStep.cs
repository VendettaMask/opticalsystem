using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Optimization;

public sealed record InequalityStepResult(double[] Step, bool Converged, int ActiveSetIterations, double MaximumViolation);

/// <summary>A damped linear least-squares step with A*step &lt;= upper. Zero must be feasible.</summary>
public static class InequalityLeastSquaresStep
{
    public const string Version = "active-set-inequality-step/1";

    /// <summary>Orthogonal projector onto the common tangent space of finite constraint normals.</summary>
    public static double[,] TangentProjector(double[,] normals)
    {
        ArgumentNullException.ThrowIfNull(normals);
        var m = normals.GetLength(0); var n = normals.GetLength(1);
        if (n > 512 || m > 8192) throw new ArgumentException("Tangent space exceeds its bounded dense workspace.");
        var normalized = new double[m, n];
        for (var i = 0; i < m; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var scale = 0.0;
            for (var j = 0; j < n; j++)
            {
                if (!double.IsFinite(normals[i, j])) throw new ArgumentException("Non-finite constraint normal.");
                scale = Math.Max(scale, Math.Abs(normals[i, j]));
            }
            if (scale == 0) continue;
            var norm = Math.Sqrt(Enumerable.Range(0, n).Sum(j => Math.Pow(normals[i, j] / scale, 2)));
            for (var j = 0; j < n; j++) normalized[i, j] = normals[i, j] / scale / norm;
        }
        var factor = JacobiSvd.Factor(normalized, Math.Sqrt(Math.BitIncrement(1.0) - 1));
        var projector = new double[n, n];
        for (var j = 0; j < n; j++) projector[j, j] = 1;
        for (var k = 0; k < n; k++) if (factor.SingularSquared[k] > factor.Threshold)
            for (var i = 0; i < n; i++) for (var j = 0; j < n; j++)
                projector[i, j] -= factor.RightVectors[i, k] * factor.RightVectors[j, k];
        return projector;
    }

    public static InequalityStepResult Solve(double[,] jacobian, double[] residuals, double[,] normals, double[] upper, double damping)
    {
        ArgumentNullException.ThrowIfNull(jacobian); ArgumentNullException.ThrowIfNull(residuals);
        ArgumentNullException.ThrowIfNull(normals); ArgumentNullException.ThrowIfNull(upper);
        var rows = jacobian.GetLength(0); var n = jacobian.GetLength(1); var m = normals.GetLength(0);
        if (residuals.Length != rows || normals.GetLength(1) != n || upper.Length != m)
            throw new ArgumentException("Inconsistent least-squares/inequality dimensions.");
        if (!double.IsFinite(damping) || damping <= 0) throw new ArgumentOutOfRangeException(nameof(damping));
        if (n > 512 || m > 8192) throw new ArgumentException("Inequality step exceeds its bounded dense workspace.");
        var h = new double[n, n]; var q = new double[n];
        for (var i = 0; i < rows; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (!double.IsFinite(residuals[i])) throw new ArgumentException("Non-finite residual.");
            for (var j = 0; j < n; j++)
            {
                if (!double.IsFinite(jacobian[i, j])) throw new ArgumentException("Non-finite Jacobian.");
                q[j] += jacobian[i, j] * residuals[i];
                for (var k = 0; k <= j; k++) h[j, k] += jacobian[i, j] * jacobian[i, k];
            }
        }
        var scale = Enumerable.Range(0, n).Select(j => h[j, j]).DefaultIfEmpty(0).Max();
        if (scale == 0) scale = 1;
        var precision = Math.Sqrt(Math.BitIncrement(1.0) - 1);
        for (var j = 0; j < n; j++)
        {
            // Auxiliary-level models can have columns absent from the LS objective but
            // present in the inequalities. They still need damping; otherwise increasing
            // lambda cannot shorten a step in those columns at all.
            h[j, j] += damping * (h[j, j] > 0 ? h[j, j] : scale) + precision * scale;
            for (var k = 0; k < j; k++) h[k, j] = h[j, k];
        }
        var a = new double[m, n]; var b = new double[m];
        for (var i = 0; i < m; i++)
        {
            if (!double.IsFinite(upper[i]) || upper[i] < 0) throw new ArgumentException("Zero must satisfy every finite inequality.");
            var norm = 0.0;
            for (var j = 0; j < n; j++)
            {
                if (!double.IsFinite(normals[i, j])) throw new ArgumentException("Non-finite constraint normal.");
                norm += normals[i, j] * normals[i, j];
            }
            if (!double.IsFinite(norm)) throw new ArgumentException("Constraint normal norm exceeds the finite workspace.");
            norm = Math.Sqrt(norm); if (norm == 0) { b[i] = upper[i]; continue; }
            b[i] = upper[i] / norm;
            for (var j = 0; j < n; j++) a[i, j] = normals[i, j] / norm;
        }
        var l = new double[n, n];
        for (var i = 0; i < n; i++) for (var j = 0; j <= i; j++)
        {
            var value = h[i, j]; for (var k = 0; k < j; k++) value -= l[i, k] * l[j, k];
            if (i == j)
            {
                if (!double.IsFinite(value) || value <= 0) throw new InvalidOperationException("Damped normal matrix is not positive definite.");
                l[i, j] = Math.Sqrt(value);
            }
            else l[i, j] = value / l[j, j];
        }
        double[] Forward(double[] target)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var v = target.ToArray();
            for (var i = 0; i < n; i++) { for (var j = 0; j < i; j++) v[i] -= l[i, j] * v[j]; v[i] /= l[i, i]; }
            return v;
        }
        double[] Backward(double[] target)
        {
            var v = target.ToArray();
            for (var i = n - 1; i >= 0; i--) { for (var j = i + 1; j < n; j++) v[i] -= l[j, i] * v[j]; v[i] /= l[i, i]; }
            return v;
        }
        double Dot(int i, double[] v) { var sum = 0.0; for (var j = 0; j < n; j++) sum += a[i, j] * v[j]; return sum; }
        var whiteUnconstrained = Forward(q.Select(v => -v).ToArray());
        var unconstrained = Backward(whiteUnconstrained);
        var whiteNormals = Enumerable.Range(0, m).Select(i => Forward(Enumerable.Range(0, n).Select(j => a[i, j]).ToArray())).ToArray();
        var step = new double[n]; var active = new List<int>(); var converged = false; var iteration = 0;
        var epsilon = Math.BitIncrement(1.0) - 1;
        var visited = new Dictionary<string, double>();
        for (; iteration < Math.Max(16, 8 * n); iteration++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var model = 0.0;
            for (var j = 0; j < n; j++)
            {
                model += q[j] * step[j];
                for (var k = 0; k < n; k++) model += .5 * step[j] * h[j, k] * step[k];
            }
            var signature = string.Join(',', active.Order());
            if (visited.TryGetValue(signature, out var previous) && model >= previous - 64 * epsilon * (1 + Math.Abs(previous))) break;
            visited[signature] = model;
            var optimum = unconstrained.ToArray(); var multipliers = Array.Empty<double>();
            if (active.Count > 0)
            {
                // Work with the whitened normals, not their Gram matrix: forming A H^-1 A^T
                // squares the condition number of nearly dependent active optical constraints.
                var transpose = new double[n, active.Count]; var target = new double[active.Count];
                for (var i = 0; i < active.Count; i++)
                {
                    target[i] = Dot(active[i], unconstrained) - b[active[i]];
                    for (var j = 0; j < n; j++) transpose[j, i] = whiteNormals[active[i]][j];
                }
                // Factor the tall transpose, with one column per active normal. Factoring
                // the wide matrix would spend sweeps on n-rank null columns. The same
                // singular triplets provide both projection and Lagrange multipliers.
                var factor = JacobiSvd.Factor(transpose, precision);
                var correction = new double[n]; multipliers = new double[active.Count];
                for (var component = 0; component < active.Count; component++)
                {
                    if (factor.SingularSquared[component] <= factor.Threshold) continue;
                    var coefficient = Enumerable.Range(0, active.Count).Sum(i => factor.RightVectors[i, component] * target[i]) / factor.SingularSquared[component];
                    for (var j = 0; j < n; j++) correction[j] += factor.Rotated[j, component] * coefficient;
                    for (var i = 0; i < active.Count; i++) multipliers[i] += factor.RightVectors[i, component] * coefficient;
                }
                optimum = Backward(whiteUnconstrained.Zip(correction, (v, c) => v - c).ToArray());
            }
            var direction = optimum.Zip(step, (v, p) => v - p).ToArray();
            var norm = Math.Sqrt(direction.Sum(v => v * v));
            if (norm <= precision * (1 + Math.Sqrt(step.Sum(v => v * v))))
            {
                var drop = Enumerable.Range(0, multipliers.Length).OrderBy(i => multipliers[i]).FirstOrDefault(-1);
                if (drop < 0 || multipliers[drop] >= -precision * (1 + multipliers.Max(Math.Abs))) { converged = true; break; }
                active.RemoveAt(drop); continue;
            }
            var alpha = 1.0; var blocking = -1;
            for (var i = 0; i < m; i++) if (!active.Contains(i))
            {
                var slope = Dot(i, direction);
                if (slope <= precision * norm) continue;
                var fraction = Math.Max(0, b[i] - Dot(i, step)) / slope;
                if (fraction < alpha) { alpha = fraction; blocking = i; }
            }
            for (var j = 0; j < n; j++) step[j] += alpha * direction[j];
            if (blocking >= 0)
            {
                // At most n independent normals are needed. A further blocker indicates numerical
                // degeneracy; return the feasible inexact step instead of growing a singular basis.
                if (active.Count >= n) break;
                active.Add(blocking);
            }
        }
        var violation = Enumerable.Range(0, m).Select(i => Math.Max(0, Dot(i, step) - b[i])).DefaultIfEmpty(0).Max();
        if (step.Any(v => !double.IsFinite(v)) || !double.IsFinite(violation)) throw new InvalidOperationException("Non-finite inequality step.");
        return new(step, converged, iteration, violation);
    }
}
