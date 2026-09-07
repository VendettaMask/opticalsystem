namespace OptilandWorkbench.InitialStructure.Engine.Optimization;

/// <summary>
/// Small dense least-squares solve by column-pivoted Householder QR, without forming J-transpose J.
/// A deficient system returns a basic least-squares solution, not a claimed minimum-norm solution.
/// </summary>
internal static class PivotedLeastSquares
{
    internal const double MachineEpsilon = 2.2204460492503131e-16;

    public static double[] Solve(double[,] matrix, IReadOnlyList<double> target)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        if (target.Count != rows) throw new ArgumentException("The target and matrix row counts differ.", nameof(target));
        var qr = (double[,])matrix.Clone();
        var rhs = target.ToArray();
        var permutation = Enumerable.Range(0, columns).ToArray();
        var rank = 0;
        var threshold = 0.0;
        for (var k = 0; k < Math.Min(rows, columns); k++)
        {
            var pivot = k;
            var largest = 0.0;
            for (var column = k; column < columns; column++)
            {
                var norm = ColumnNorm(qr, column, k);
                if (!double.IsFinite(norm)) throw new ArithmeticException("The least-squares matrix is not finite.");
                if (norm > largest) { largest = norm; pivot = column; }
            }
            if (k == 0) threshold = largest * (MachineEpsilon * Math.Max(rows, columns));
            if (largest <= threshold) break;
            if (pivot != k)
            {
                for (var row = 0; row < rows; row++) (qr[row, k], qr[row, pivot]) = (qr[row, pivot], qr[row, k]);
                (permutation[k], permutation[pivot]) = (permutation[pivot], permutation[k]);
            }

            // Normalize first so forming the Householder vector does not square a large column.
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
            rank++;
        }

        var solution = new double[columns];
        for (var row = rank - 1; row >= 0; row--)
        {
            var value = rhs[row];
            for (var column = row + 1; column < rank; column++) value -= qr[row, column] * solution[column];
            solution[row] = value / qr[row, row];
        }
        var ordered = new double[columns];
        for (var column = 0; column < columns; column++) ordered[permutation[column]] = solution[column];
        return ordered;
    }

    private static double ColumnNorm(double[,] values, int column, int start)
    {
        var norm = 0.0;
        for (var row = start; row < values.GetLength(0); row++) norm = double.Hypot(norm, values[row, column]);
        return norm;
    }
}
