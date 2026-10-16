using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Shared one-sided Jacobi SVD; an explicit relative cutoff selects a finite numerical profile.</summary>
internal static class JacobiSvd
{
    internal static Factorization Factor(double[,] matrix, double? relativeCutoff = null)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (relativeCutoff is { } cutoff && (!double.IsFinite(cutoff) || cutoff < 0 || cutoff >= 1))
            throw new ArgumentOutOfRangeException(nameof(relativeCutoff));
        ComputationCancellation.ThrowIfCancellationRequested();
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                if (!double.IsFinite(matrix[row, column]))
                    throw new InvalidOperationException("SVD input contains a non-finite value.");
        var rotated = (double[,])matrix.Clone();
        var rightVectors = new double[columns, columns];
        for (var index = 0; index < columns; index++)
        {
            rightVectors[index, index] = 1;
        }

        var maximumSweeps = Math.Max(12, Math.Min(64, columns * 4));
        for (var sweep = 0; sweep < maximumSweeps; sweep++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var changed = false;
            for (var left = 0; left < columns - 1; left++)
            {
                for (var right = left + 1; right < columns; right++)
                {
                    var alpha = ColumnDot(rotated, left, left);
                    var beta = ColumnDot(rotated, right, right);
                    var gamma = ColumnDot(rotated, left, right);
                    if (Math.Abs(gamma) <= 1e-14 * Math.Sqrt(Math.Max(0, alpha * beta)))
                    {
                        continue;
                    }

                    var zeta = (beta - alpha) / (2 * gamma);
                    var tangent = Math.CopySign(1, zeta)
                        / (Math.Abs(zeta) + Math.Sqrt(1 + (zeta * zeta)));
                    var cosine = 1 / Math.Sqrt(1 + (tangent * tangent));
                    var sine = cosine * tangent;
                    RotateColumns(rotated, left, right, cosine, sine);
                    RotateColumns(rightVectors, left, right, cosine, sine);
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var singularSquared = Enumerable.Range(0, columns)
            .Select(column => ColumnDot(rotated, column, column))
            .ToArray();
        var maximum = singularSquared.DefaultIfEmpty(0).Max();
        var relativeTolerance = relativeCutoff ?? Math.Max(rows, columns) * 1e-12;
        var threshold = maximum * relativeTolerance * relativeTolerance;
        return new Factorization(rotated, rightVectors, singularSquared, threshold);
    }

    internal static double[] Solve(Factorization factor, IReadOnlyList<double> rightHandSide)
    {
        ArgumentNullException.ThrowIfNull(factor);
        ArgumentNullException.ThrowIfNull(rightHandSide);
        if (rightHandSide.Count != factor.Rotated.GetLength(0))
            throw new ArgumentException("Target length must match the factor row count.", nameof(rightHandSide));
        var columns = factor.RightVectors.GetLength(0);
        var result = new double[columns];
        for (var component = 0; component < columns; component++)
        {
            var sigmaSquared = factor.SingularSquared[component];
            if (sigmaSquared <= factor.Threshold)
            {
                continue;
            }

            var projection = 0.0;
            for (var row = 0; row < rightHandSide.Count; row++)
            {
                projection += factor.Rotated[row, component] * rightHandSide[row];
            }

            var coefficient = projection / sigmaSquared;
            for (var row = 0; row < columns; row++)
            {
                result[row] += factor.RightVectors[row, component] * coefficient;
            }
        }

        return result;
    }

    private static double ColumnDot(double[,] matrix, int left, int right)
    {
        var total = 0.0;
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            total += matrix[row, left] * matrix[row, right];
        }

        return total;
    }

    private static void RotateColumns(
        double[,] matrix,
        int left,
        int right,
        double cosine,
        double sine)
    {
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            var leftValue = matrix[row, left];
            var rightValue = matrix[row, right];
            matrix[row, left] = (cosine * leftValue) - (sine * rightValue);
            matrix[row, right] = (sine * leftValue) + (cosine * rightValue);
        }
    }

    internal sealed record Factorization(
        double[,] Rotated,
        double[,] RightVectors,
        double[] SingularSquared,
        double Threshold);
}
