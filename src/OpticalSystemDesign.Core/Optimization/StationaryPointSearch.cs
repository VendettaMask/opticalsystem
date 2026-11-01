namespace OpticalSystemDesign.Core.Optimization;

public sealed record StationaryValue(double Value, double[] Gradient);
public sealed record StationarySearchResult(double[] Coordinates, StationaryValue Evaluation, int Iterations, string StopReason, bool InteriorStationary);
public sealed record StationarySpectrum(double[][] Hessian, double[] Eigenvalues, double[][] Eigenvectors, double SymmetryError);
public sealed record StationaryContinuation(double[] Coordinates, StationaryValue Evaluation, double[][] Inverse,
    int Iterations, string? StopReason = null);
public sealed record StationaryCurvatureContinuation(int NextColumn, double[][] Raw);

/// <summary>Bounded generic BFGS and exact-gradient finite-difference curvature diagnostics.
/// This layer has no optical formulas and a Gauss-Newton PSD matrix is never used to certify a saddle.</summary>
public static class StationaryPointSearch
{
    public static StationarySearchResult Minimize(Func<double[], StationaryValue> evaluate, double[] initial,
        double[] lower, double[] upper, int maximumIterations, double gradientTolerance, CancellationToken token = default, double[][]? initialInverse = null,
        StationaryContinuation? continuation = null, Action<StationaryContinuation>? checkpoint = null)
    {
        Validate(initial, lower, upper);
        if (maximumIterations < 0 || maximumIterations > 400 || !double.IsFinite(gradientTolerance) || gradientTolerance <= 0)
            throw new ArgumentException("Invalid stationary search policy.");
        var n = initial.Length;
        if (initialInverse is not null && (initialInverse.Length != n || initialInverse.Any(row => row.Length != n || row.Any(v => !double.IsFinite(v)))))
            throw new ArgumentException("Invalid initial inverse curvature scale.");
        double[][] Reset() => initialInverse?.Select(row => row.ToArray()).ToArray() ?? Identity(n);
        if (continuation is not null)
        {
            Validate(continuation.Coordinates, lower, upper);
            if (continuation.Iterations < 0 || continuation.Iterations > maximumIterations || !double.IsFinite(continuation.Evaluation.Value)
                || continuation.Evaluation.Gradient.Length != n || continuation.Evaluation.Gradient.Any(v => !double.IsFinite(v))
                || continuation.Inverse.Length != n || continuation.Inverse.Any(row => row.Length != n || row.Any(v => !double.IsFinite(v)))
                || continuation.StopReason is not (null or "IterationLimit" or "InteriorStationary" or "LineSearchStalled"))
                throw new ArgumentException("Invalid stationary continuation.");
        }
        var x = continuation?.Coordinates.ToArray() ?? initial.ToArray();
        var value = continuation?.Evaluation ?? Evaluate(x); var inverse = continuation?.Inverse.Select(r => r.ToArray()).ToArray() ?? Reset();
        var iterations = continuation?.Iterations ?? 0; var reason = continuation?.StopReason ?? "IterationLimit";
        if (continuation?.StopReason is not null) return new(x, value, iterations, reason, Stationary(x, value));
        if (continuation is null) Save(null);
        for (; iterations < maximumIterations;)
        {
            token.ThrowIfCancellationRequested();
            if (Stationary(x, value)) { reason = "InteriorStationary"; break; }
            var direction = Multiply(inverse, value.Gradient).Select(v => -v).ToArray();
            if (Dot(direction, value.Gradient) >= -1e-16 * Norm(value.Gradient)) { inverse = Reset(); direction = value.Gradient.Select(v => -v).ToArray(); }
            var scale = Math.Max(1, direction.Max(Math.Abs) / .5); direction = direction.Select(v => v / scale).ToArray();
            double[]? next = null; StationaryValue? measured = null;
            for (var alpha = 1d; alpha >= 1d / 1048576; alpha *= .5)
            {
                token.ThrowIfCancellationRequested();
                var trial = x.Select((v, j) => Math.Clamp(v + alpha * direction[j], lower[j], upper[j])).ToArray();
                var delta = trial.Select((v, j) => v - x[j]).ToArray();
                if (delta.Max(Math.Abs) < 1e-14) continue;
                try
                {
                    var result = Evaluate(trial);
                    if (result.Value <= value.Value + 1e-4 * Dot(value.Gradient, delta)) { next = trial; measured = result; break; }
                }
                catch (Exception error) when (error is ArithmeticException or ArgumentException) { }
            }
            if (next is null || measured is null) { reason = "LineSearchStalled"; break; }
            var s = next.Select((v, j) => v - x[j]).ToArray(); var y = measured.Gradient.Select((v, j) => v - value.Gradient[j]).ToArray();
            var sy = Dot(s, y);
            if (sy > 1e-12 * Math.Sqrt(Dot(s, s) * Dot(y, y)))
            {
                var hy = Multiply(inverse, y); var factor = (sy + Dot(y, hy)) / (sy * sy);
                for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) inverse[i][j] += factor * s[i] * s[j] - (hy[i] * s[j] + s[i] * hy[j]) / sy;
            }
            else inverse = Reset();
            x = next; value = measured; iterations++; Save(null);
        }
        Save(reason);
        return new(x, value, iterations, reason, Stationary(x, value));
        void Save(string? stop) => checkpoint?.Invoke(new(x.ToArray(), new(value.Value, value.Gradient.ToArray()), inverse.Select(r => r.ToArray()).ToArray(), iterations, stop));
        bool Stationary(double[] at, StationaryValue sample) => Norm(sample.Gradient) <= gradientTolerance * Math.Max(1, Math.Abs(sample.Value))
            && at.Select((v, j) => v > lower[j] + 1e-7 && v < upper[j] - 1e-7).All(v => v);
        StationaryValue Evaluate(double[] at)
        {
            token.ThrowIfCancellationRequested(); var result = evaluate(at);
            if (!double.IsFinite(result.Value) || result.Gradient.Length != n || result.Gradient.Any(v => !double.IsFinite(v))) throw new ArithmeticException("Non-finite objective gradient.");
            return result;
        }
    }

    public static StationarySpectrum Curvature(Func<double[], StationaryValue> evaluate, double[] point, double[] lower,
        double[] upper, double step, CancellationToken token = default, StationaryCurvatureContinuation? continuation = null,
        Action<StationaryCurvatureContinuation>? checkpoint = null)
    {
        Validate(point, lower, upper);
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        var n = point.Length;
        if (continuation is not null && (continuation.NextColumn < 0 || continuation.NextColumn > n || continuation.Raw.Length != n
            || continuation.Raw.Any(row => row.Length != n || row.Any(v => !double.IsFinite(v))))) throw new ArgumentException("Invalid curvature continuation.");
        var raw = continuation?.Raw.Select(r => r.ToArray()).ToArray() ?? Matrix(n); var hessian = Matrix(n); var asymmetry = 0d;
        for (var j = continuation?.NextColumn ?? 0; j < n; j++)
        {
            token.ThrowIfCancellationRequested();
            if (point[j] - step <= lower[j] || point[j] + step >= upper[j]) throw new ArithmeticException("Central curvature requires an interior point.");
            var plus = point.ToArray(); var minus = point.ToArray(); plus[j] += step; minus[j] -= step;
            var a = evaluate(plus); var b = evaluate(minus);
            if (!double.IsFinite(a.Value) || !double.IsFinite(b.Value) || a.Gradient.Length != n || b.Gradient.Length != n
                || a.Gradient.Concat(b.Gradient).Any(v => !double.IsFinite(v))) throw new ArithmeticException("Invalid curvature gradient.");
            for (var i = 0; i < n; i++) raw[i][j] = (a.Gradient[i] - b.Gradient[i]) / (2 * step);
            checkpoint?.Invoke(new(j + 1, raw.Select(r => r.ToArray()).ToArray()));
        }
        for (var i = 0; i < n; i++) for (var j = 0; j < n; j++)
        { hessian[i][j] = .5 * (raw[i][j] + raw[j][i]); asymmetry = Math.Max(asymmetry, Math.Abs(raw[i][j] - raw[j][i])); }
        var (eigenvalues, vectors) = Diagonalize(hessian, token);
        return new(hessian, eigenvalues, vectors, asymmetry);
    }
    private static (double[], double[][]) Diagonalize(double[][] matrix, CancellationToken token)
    {
        var n = matrix.Length; var a = matrix.Select(r => r.ToArray()).ToArray(); var v = Identity(n);
        if (a.SelectMany(r => r).Any(x => !double.IsFinite(x))) throw new ArithmeticException("Non-finite curvature.");
        for (var sweep = 0; sweep < 100 * n * n; sweep++)
        {
            token.ThrowIfCancellationRequested(); var largest = 0d; var p = 0; var q = 0;
            for (var i = 0; i < n; i++) for (var j = i + 1; j < n; j++) if (Math.Abs(a[i][j]) > largest) { largest = Math.Abs(a[i][j]); p = i; q = j; }
            if (largest <= 1e-13 * Math.Max(1, Enumerable.Range(0, n).Max(i => Math.Abs(a[i][i]))))
            {
                var order = Enumerable.Range(0, n).OrderBy(i => a[i][i]).ToArray();
                return (order.Select(i => a[i][i]).ToArray(), order.Select(j => Enumerable.Range(0, n).Select(i => v[i][j]).ToArray()).ToArray());
            }
            var angle = .5 * Math.Atan2(2 * a[p][q], a[q][q] - a[p][p]); var c = Math.Cos(angle); var s = Math.Sin(angle);
            var pp = a[p][p]; var qq = a[q][q]; var pq = a[p][q];
            for (var k = 0; k < n; k++) if (k != p && k != q)
            { var kp = a[k][p]; var kq = a[k][q]; a[k][p] = a[p][k] = c * kp - s * kq; a[k][q] = a[q][k] = s * kp + c * kq; }
            a[p][p] = c * c * pp - 2 * c * s * pq + s * s * qq; a[q][q] = s * s * pp + 2 * c * s * pq + c * c * qq; a[p][q] = a[q][p] = 0;
            for (var k = 0; k < n; k++) { var kp = v[k][p]; var kq = v[k][q]; v[k][p] = c * kp - s * kq; v[k][q] = s * kp + c * kq; }
        }
        throw new ArithmeticException("Symmetric eigensolver did not converge.");
    }
    public static double Norm(IReadOnlyList<double> gradient) => gradient.Max(Math.Abs);
    private static double Dot(double[] a, double[] b) => a.Zip(b, (x, y) => x * y).Sum();
    private static double[] Multiply(double[][] a, double[] b) => a.Select(row => Dot(row, b)).ToArray();
    private static double[][] Matrix(int n) => Enumerable.Range(0, n).Select(_ => new double[n]).ToArray();
    private static double[][] Identity(int n) { var a = Matrix(n); for (var i = 0; i < n; i++) a[i][i] = 1; return a; }
    private static void Validate(double[] x, double[] lower, double[] upper)
    {
        if (x.Length is < 1 or > 32 || lower.Length != x.Length || upper.Length != x.Length || x.Select((v, j) =>
            !double.IsFinite(v) || !double.IsFinite(lower[j]) || !double.IsFinite(upper[j]) || lower[j] >= upper[j] || v < lower[j] || v > upper[j]).Any(v => v))
            throw new ArgumentException("Invalid bounded stationary problem.");
    }
}
