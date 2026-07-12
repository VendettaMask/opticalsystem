namespace OptilandWorkbench.Core.Analysis;

public sealed record ZernikeCoefficient(int Number, int RadialOrder, int AzimuthalOrder, double Value);

public static class ZernikeFitEngine
{
    public static IReadOnlyList<ZernikeCoefficient> FitFringe(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms)
    {
        var valid = samples.Where(sample => sample.Intensity > 0).ToArray();
        var indices = FringeIndices(numTerms);
        var design = new double[valid.Length, indices.Count];
        var target = new double[valid.Length];
        for (var row = 0; row < valid.Length; row++)
        {
            var sample = valid[row];
            var radius = Math.Sqrt(
                (sample.NormalizedPupilX * sample.NormalizedPupilX)
                + (sample.NormalizedPupilY * sample.NormalizedPupilY));
            var angle = Math.Atan2(sample.NormalizedPupilY, sample.NormalizedPupilX);
            target[row] = sample.OpdWaves;
            for (var column = 0; column < indices.Count; column++)
            {
                design[row, column] = Basis(indices[column].N, indices[column].M, radius, angle);
            }
        }

        var coefficients = SolveLeastSquares(design, target);
        return indices.Select((index, position) => new ZernikeCoefficient(
            index.Number,
            index.N,
            index.M,
            coefficients[position])).ToArray();
    }

    public static double Evaluate(
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double x,
        double y)
    {
        var radius = Math.Sqrt((x * x) + (y * y));
        var angle = Math.Atan2(y, x);
        return coefficients.Sum(coefficient =>
            coefficient.Value * Basis(coefficient.RadialOrder, coefficient.AzimuthalOrder, radius, angle));
    }

    private static IReadOnlyList<(int Number, int N, int M)> FringeIndices(int count)
    {
        var byNumber = new Dictionary<int, (int N, int M)>();
        for (var n = 0; byNumber.Count < count; n++)
        {
            for (var m = -n; m <= n; m++)
            {
                if ((n - m) % 2 != 0)
                {
                    continue;
                }

                var sign = Math.Sign(m);
                var number = (int)(Math.Pow(1 + ((n + Math.Abs(m)) / 2.0), 2)
                    - (2 * Math.Abs(m))
                    + ((1 - sign) / 2.0));
                if (number >= 1 && number <= count)
                {
                    byNumber[number] = (n, m);
                }
            }
        }

        return Enumerable.Range(1, count)
            .Select(number => (number, byNumber[number].N, byNumber[number].M))
            .ToArray();
    }

    private static double Basis(int n, int m, double radius, double angle)
    {
        var radial = 0.0;
        var absoluteM = Math.Abs(m);
        var maximum = (n - absoluteM) / 2;
        for (var k = 0; k <= maximum; k++)
        {
            var coefficient = Math.Pow(-1, k) * Factorial(n - k)
                / (Factorial(k)
                    * Factorial(((n + absoluteM) / 2) - k)
                    * Factorial(((n - absoluteM) / 2) - k));
            radial += coefficient * Math.Pow(radius, n - (2 * k));
        }

        return m >= 0
            ? radial * Math.Cos(m * angle)
            : radial * Math.Sin(absoluteM * angle);
    }

    private static double[] SolveLeastSquares(double[,] matrix, double[] target)
    {
        var rows = matrix.GetLength(0);
        var columns = matrix.GetLength(1);
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

    private static double Factorial(int value)
    {
        var result = 1.0;
        for (var number = 2; number <= value; number++)
        {
            result *= number;
        }

        return result;
    }
}
