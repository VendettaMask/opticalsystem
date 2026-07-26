using System.Collections.Concurrent;

namespace OptilandWorkbench.Core.Analysis;

public sealed record ZernikeCoefficient(int Number, int RadialOrder, int AzimuthalOrder, double Value);

public static class ZernikeFitEngine
{
    private static readonly ConcurrentDictionary<(int N, int M, long Obscuration), double[]>
        AnnularRadialCache = new();

    public static IReadOnlyList<ZernikeCoefficient> FitFringe(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms)
    {
        return Fit(
            samples,
            FringeIndices(numTerms),
            (n, m, radius, angle) => Basis(n, m, radius, angle, standardNormalization: false));
    }

    public static IReadOnlyList<ZernikeCoefficient> FitStandard(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms)
    {
        return Fit(
            samples,
            StandardIndices(numTerms),
            (n, m, radius, angle) => Basis(n, m, radius, angle, standardNormalization: true));
    }

    public static IReadOnlyList<ZernikeCoefficient> FitAnnular(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms,
        double obscurationRatio)
    {
        var obscuration = Math.Clamp(obscurationRatio, 0, 0.95);
        return Fit(
            samples,
            StandardIndices(numTerms),
            (n, m, radius, angle) => AnnularBasis(n, m, radius, angle, obscuration),
            obscuration);
    }

    private static IReadOnlyList<ZernikeCoefficient> Fit(
        IReadOnlyList<WavefrontSample> samples,
        IReadOnlyList<(int Number, int N, int M)> indices,
        Func<int, int, double, double, double> basis,
        double minimumRadius = 0)
    {
        var valid = samples.Where(sample =>
        {
            var radiusSquared = (sample.NormalizedPupilX * sample.NormalizedPupilX)
                + (sample.NormalizedPupilY * sample.NormalizedPupilY);
            return sample.Intensity > 0
                && radiusSquared >= (minimumRadius * minimumRadius) - 1e-12;
        }).ToArray();
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
                design[row, column] = basis(
                    indices[column].N,
                    indices[column].M,
                    radius,
                    angle);
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
            coefficient.Value * Basis(
                coefficient.RadialOrder,
                coefficient.AzimuthalOrder,
                radius,
                angle,
                standardNormalization: false));
    }

    public static double EvaluateStandard(
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double x,
        double y)
    {
        var radius = Math.Sqrt((x * x) + (y * y));
        var angle = Math.Atan2(y, x);
        return coefficients.Sum(coefficient =>
            coefficient.Value * Basis(
                coefficient.RadialOrder,
                coefficient.AzimuthalOrder,
                radius,
                angle,
                standardNormalization: true));
    }

    public static double EvaluateAnnular(
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double x,
        double y,
        double obscurationRatio)
    {
        var radius = Math.Sqrt((x * x) + (y * y));
        var angle = Math.Atan2(y, x);
        var obscuration = Math.Clamp(obscurationRatio, 0, 0.95);
        return coefficients.Sum(coefficient =>
            coefficient.Value * AnnularBasis(
                coefficient.RadialOrder,
                coefficient.AzimuthalOrder,
                radius,
                angle,
                obscuration));
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

    private static IReadOnlyList<(int Number, int N, int M)> StandardIndices(int count)
    {
        var result = new List<(int Number, int N, int M)>(count);
        for (var n = 0; result.Count < count; n++)
        {
            for (var absoluteM = n % 2; absoluteM <= n && result.Count < count; absoluteM += 2)
            {
                if (absoluteM == 0)
                {
                    result.Add((result.Count + 1, n, 0));
                    continue;
                }

                result.Add((result.Count + 1, n, absoluteM));
                if (result.Count < count)
                {
                    result.Add((result.Count + 1, n, -absoluteM));
                }
            }
        }

        return result;
    }

    private static double Basis(
        int n,
        int m,
        double radius,
        double angle,
        bool standardNormalization)
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

        var angular = m >= 0
            ? radial * Math.Cos(m * angle)
            : radial * Math.Sin(absoluteM * angle);
        if (!standardNormalization)
        {
            return angular;
        }

        var normalization = m == 0
            ? Math.Sqrt(n + 1)
            : Math.Sqrt(2 * (n + 1));
        return normalization * angular;
    }

    private static double AnnularBasis(
        int n,
        int m,
        double radius,
        double angle,
        double obscuration)
    {
        var radialCoefficients = AnnularRadialCoefficients(n, Math.Abs(m), obscuration);
        var radial = 0.0;
        for (var power = 0; power < radialCoefficients.Length; power++)
        {
            radial += radialCoefficients[power] * Math.Pow(radius, power);
        }

        return m >= 0
            ? radial * Math.Cos(m * angle)
            : radial * Math.Sin(Math.Abs(m) * angle);
    }

    private static double[] AnnularRadialCoefficients(int n, int absoluteM, double obscuration)
    {
        var key = (n, absoluteM, BitConverter.DoubleToInt64Bits(obscuration));
        return AnnularRadialCache.GetOrAdd(
            key,
            _ => CalculateAnnularRadialCoefficients(n, absoluteM, obscuration));
    }

    private static double[] CalculateAnnularRadialCoefficients(int n, int absoluteM, double obscuration)
    {
        var polynomial = new double[n + 1];
        polynomial[n] = 1;
        for (var lowerOrder = absoluteM; lowerOrder <= n - 2; lowerOrder += 2)
        {
            var previous = AnnularRadialCoefficients(lowerOrder, absoluteM, obscuration);
            var projection = AnnularInnerProduct(polynomial, previous, absoluteM, obscuration);
            for (var power = 0; power < previous.Length; power++)
            {
                polynomial[power] -= projection * previous[power];
            }
        }

        var norm = Math.Sqrt(Math.Max(
            1e-30,
            AnnularInnerProduct(polynomial, polynomial, absoluteM, obscuration)));
        for (var power = 0; power < polynomial.Length; power++)
        {
            polynomial[power] /= norm;
        }

        return polynomial;
    }

    private static double AnnularInnerProduct(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right,
        int absoluteM,
        double obscuration)
    {
        var radialIntegral = 0.0;
        for (var leftPower = 0; leftPower < left.Count; leftPower++)
        {
            if (Math.Abs(left[leftPower]) <= 1e-30)
            {
                continue;
            }

            for (var rightPower = 0; rightPower < right.Count; rightPower++)
            {
                if (Math.Abs(right[rightPower]) <= 1e-30)
                {
                    continue;
                }

                var exponent = leftPower + rightPower + 2;
                radialIntegral += left[leftPower] * right[rightPower]
                    * (1 - Math.Pow(obscuration, exponent))
                    / exponent;
            }
        }

        var angularFactor = absoluteM == 0 ? 2.0 : 1.0;
        return angularFactor * radialIntegral / (1 - (obscuration * obscuration));
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
