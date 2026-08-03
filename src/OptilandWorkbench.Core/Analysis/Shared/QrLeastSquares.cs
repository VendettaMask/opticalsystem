namespace OptilandWorkbench.Core.Analysis;

internal static class QrLeastSquares
{
    public static double[] Solve(double[,] matrix, double[] target)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(target);

        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        if (target.Length != rows)
        {
            throw new ArgumentException("Target length must match the matrix row count.", nameof(target));
        }

        var q = new double[rows, columns];
        var r = new double[columns, columns];
        var work = (double[,])matrix.Clone();
        for (var column = 0; column < columns; column++)
        {
            var norm = 0.0;
            for (var row = 0; row < rows; row++)
            {
                norm += work[row, column] * work[row, column];
            }

            norm = Math.Sqrt(norm);
            if (norm <= 1e-14)
            {
                continue;
            }

            r[column, column] = norm;
            for (var row = 0; row < rows; row++)
            {
                q[row, column] = work[row, column] / norm;
            }

            for (var next = column + 1; next < columns; next++)
            {
                var projection = 0.0;
                for (var row = 0; row < rows; row++)
                {
                    projection += q[row, column] * work[row, next];
                }

                r[column, next] = projection;
                for (var row = 0; row < rows; row++)
                {
                    work[row, next] -= projection * q[row, column];
                }
            }
        }

        var projectedTarget = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                projectedTarget[column] += q[row, column] * target[row];
            }
        }

        var result = new double[columns];
        for (var row = columns - 1; row >= 0; row--)
        {
            var value = projectedTarget[row];
            for (var column = row + 1; column < columns; column++)
            {
                value -= r[row, column] * result[column];
            }

            result[row] = Math.Abs(r[row, row]) <= 1e-14 ? 0 : value / r[row, row];
        }

        return result;
    }
}
