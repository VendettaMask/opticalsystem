namespace OptilandWorkbench.InitialStructure.Engine.Optimization;

/// <summary>
/// A thin, column-pivoted QR model. Retains small pivots for damped solves instead
/// of assigning every dependent coordinate a basic-solution zero. Damping uses
/// an augmented triangular matrix, never normal equations or new function calls.
/// </summary>
internal sealed class RegularizedLeastSquaresModel
{
    private readonly double[,] _upper;
    private readonly double[] _target;
    private readonly int[] _permutation;
    public int Rank { get; }
    public int Dimension => _permutation.Length;

    public RegularizedLeastSquaresModel(double[,] jacobian, IReadOnlyList<double> residuals)
    {
        var rows = jacobian.GetLength(0);
        var columns = jacobian.GetLength(1);
        if (rows != residuals.Count) throw new ArgumentException("Residual and Jacobian row counts differ.");
        var qr = (double[,])jacobian.Clone();
        var rhs = residuals.Select(value => -value).ToArray();
        _permutation = Enumerable.Range(0, columns).ToArray();
        var threshold = 0.0;
        for (var k = 0; k < Math.Min(rows, columns); k++)
        {
            var pivot = k;
            var largest = 0.0;
            for (var column = k; column < columns; column++)
            {
                var norm = 0.0;
                for (var row = k; row < rows; row++) norm = double.Hypot(norm, qr[row, column]);
                if (!double.IsFinite(norm)) throw new ArithmeticException("Nonfinite least-squares model.");
                if (norm > largest) { largest = norm; pivot = column; }
            }
            if (k == 0) threshold = largest * (PivotedLeastSquares.MachineEpsilon * Math.Max(rows, columns));
            if (largest == 0) break;
            if (largest > threshold) Rank++;
            if (pivot != k)
            {
                for (var row = 0; row < rows; row++) (qr[row, k], qr[row, pivot]) = (qr[row, pivot], qr[row, k]);
                (_permutation[k], _permutation[pivot]) = (_permutation[pivot], _permutation[k]);
            }
            var reflection = new double[rows - k];
            for (var row = k; row < rows; row++) reflection[row - k] = qr[row, k] / largest;
            var sign = Math.CopySign(1, reflection[0]);
            reflection[0] += sign;
            var scale = 2 / reflection.Sum(value => value * value);
            for (var column = k + 1; column < columns; column++)
            {
                var dot = 0.0;
                for (var row = k; row < rows; row++) dot += reflection[row - k] * qr[row, column];
                for (var row = k; row < rows; row++) qr[row, column] -= scale * dot * reflection[row - k];
            }
            var rhsDot = 0.0;
            for (var row = k; row < rows; row++) rhsDot += reflection[row - k] * rhs[row];
            for (var row = k; row < rows; row++) rhs[row] -= scale * rhsDot * reflection[row - k];
            qr[k, k] = -sign * largest;
            for (var row = k + 1; row < rows; row++) qr[row, k] = 0;
        }
        var retainedRows = Math.Min(rows, columns);
        _upper = new double[retainedRows, columns];
        _target = rhs.Take(retainedRows).ToArray();
        for (var row = 0; row < retainedRows; row++)
            for (var column = row; column < columns; column++) _upper[row, column] = qr[row, column];
    }

    public double[] Step(double radius, out int dampedFactorizations)
    {
        dampedFactorizations = 0;
        if (Rank == Dimension)
        {
            var unregularized = Solve(0);
            dampedFactorizations++;
            if (Norm(unregularized) <= radius) return unregularized;
        }
        var gradientNorm = 0.0;
        for (var column = 0; column < Dimension; column++)
        {
            var value = 0.0;
            for (var row = 0; row < _target.Length; row++) value += _upper[row, column] * _target[row];
            gradientNorm = double.Hypot(gradientNorm, value);
        }
        var lower = 0.0;
        var upper = Math.Max(1e-16, gradientNorm / radius);
        var step = Solve(upper);
        dampedFactorizations++;
        // Positive damping gives an SPD regularizer even when m < n. The small
        // matrices are cheap to refactor; all optical/Jacobian data stay fixed.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var norm = Norm(step);
            if (norm <= radius && norm >= .9 * radius) break;
            if (norm > radius) lower = upper;
            else
            {
                var trialDamping = (lower + upper) / 2;
                if (trialDamping <= 1e-16) break;
                var trial = Solve(trialDamping);
                dampedFactorizations++;
                if (Norm(trial) <= radius) { upper = trialDamping; step = trial; }
                else lower = trialDamping;
                continue;
            }
            upper *= 2;
            step = Solve(upper);
            dampedFactorizations++;
        }
        return step;
    }

    internal double[] Solve(double damping)
    {
        var rows = _target.Length;
        var augmented = new double[rows + Dimension, Dimension];
        var target = new double[rows + Dimension];
        for (var row = 0; row < rows; row++)
        {
            target[row] = _target[row];
            for (var column = 0; column < Dimension; column++) augmented[row, column] = _upper[row, column];
        }
        for (var column = 0; column < Dimension; column++) augmented[rows + column, column] = Math.Sqrt(damping);
        var ordered = PivotedLeastSquares.Solve(augmented, target);
        var result = new double[Dimension];
        for (var column = 0; column < Dimension; column++) result[_permutation[column]] = ordered[column];
        return result;
    }

    private static double Norm(double[] values) => values.Aggregate(0.0, double.Hypot);
}
