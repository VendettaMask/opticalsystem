using System.Collections.Concurrent;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Analysis;

public sealed record ZernikeCoefficient(int Number, int RadialOrder, int AzimuthalOrder, double Value);

public static class ZernikeFitEngine
{
    public const int MaximumFringeTerm = 37;
    public const int MaximumStandardTerm = 231;
    public const long MaximumFitMatrixValues = 20_000_000;
    public const long MaximumFitWork = 500_000_000;

    private const int MaximumAnnularCacheEntries = 512;

    private static readonly (int N, int M)[] ZemaxFringeIndices =
    {
        (0, 0),
        (1, 1), (1, -1),
        (2, 0), (2, 2), (2, -2),
        (3, 1), (3, -1),
        (4, 0),
        (3, 3), (3, -3),
        (4, 2), (4, -2),
        (5, 1), (5, -1),
        (6, 0),
        (4, 4), (4, -4),
        (5, 3), (5, -3),
        (6, 2), (6, -2),
        (7, 1), (7, -1),
        (8, 0),
        (5, 5), (5, -5),
        (6, 4), (6, -4),
        (7, 3), (7, -3),
        (8, 2), (8, -2),
        (9, 1), (9, -1),
        (10, 0),
        (12, 0)
    };

    private static readonly ConcurrentDictionary<(int N, int M, long Obscuration), AnnularRecurrence>
        AnnularRadialCache = new();

    public static IReadOnlyList<ZernikeCoefficient> FitFringe(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms, bool requireFullRank = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return Fit(
            samples,
            FringeIndices(numTerms),
            (n, m, radius, angle) => Basis(n, m, radius, angle, standardNormalization: false), requireFullRank: requireFullRank);
    }

    public static int ResolveFringeTermCount(int requestedTermCount)
    {
        if (requestedTermCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedTermCount));
        }

        return Math.Min(requestedTermCount, MaximumFringeTerm);
    }

    public static IReadOnlyList<ZernikeCoefficient> FitStandard(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms, bool requireFullRank = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ValidateTermCount(numTerms, MaximumStandardTerm);
        return Fit(
            samples,
            StandardIndices(numTerms),
            (n, m, radius, angle) => Basis(n, m, radius, angle, standardNormalization: true), requireFullRank: requireFullRank);
    }

    public static IReadOnlyList<ZernikeCoefficient> FitAnnular(
        IReadOnlyList<WavefrontSample> samples,
        int numTerms,
        double obscurationRatio, bool requireFullRank = false)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ValidateTermCount(numTerms, MaximumStandardTerm);
        if (!double.IsFinite(obscurationRatio) || obscurationRatio is < 0 or > 0.95)
        {
            throw new ArgumentOutOfRangeException(nameof(obscurationRatio));
        }

        TrimAnnularCache();
        var obscuration = obscurationRatio;
        return Fit(
            samples,
            StandardIndices(numTerms),
            (n, m, radius, angle) => AnnularBasis(n, m, radius, angle, obscuration),
            obscuration, requireFullRank);
    }

    private static IReadOnlyList<ZernikeCoefficient> Fit(
        IReadOnlyList<WavefrontSample> samples,
        IReadOnlyList<(int Number, int N, int M)> indices,
        Func<int, int, double, double, double> basis,
        double minimumRadius = 0, bool requireFullRank = false)
    {
        foreach (var sample in samples)
            if (!double.IsFinite(sample.Intensity) || sample.Intensity < 0 || (sample.Intensity > 0
                && (!double.IsFinite(sample.OpdWaves) || !double.IsFinite(sample.NormalizedPupilX) || !double.IsFinite(sample.NormalizedPupilY))))
                throw new InvalidOperationException("Zernike 有效样本必须有限，强度必须为有限非负值。");
        var valid = samples.Where(sample =>
        {
            var radiusSquared = (sample.NormalizedPupilX * sample.NormalizedPupilX)
                + (sample.NormalizedPupilY * sample.NormalizedPupilY);
            return sample.Intensity > 0
                && radiusSquared >= (minimumRadius * minimumRadius) - 1e-12;
        }).ToArray();
        if (requireFullRank && valid.Length < indices.Count)
            throw new InvalidOperationException("有效瞳孔样本不足以拟合所请求的 Zernike 系数。");
        if (checked((long)valid.Length * indices.Count * indices.Count) > MaximumFitWork)
            throw new ArgumentOutOfRangeException(nameof(samples), "Zernike 拟合运算量超过共享资源上限。");
        if (checked((long)valid.Length * indices.Count) > MaximumFitMatrixValues)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samples),
                $"Zernike fit matrix cannot exceed {MaximumFitMatrixValues:N0} values.");
        }
        var design = new double[valid.Length, indices.Count];
        var target = new double[valid.Length];
        for (var row = 0; row < valid.Length; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sample = valid[row];
            if (!double.IsFinite(sample.OpdWaves) || !double.IsFinite(sample.NormalizedPupilX)
                || !double.IsFinite(sample.NormalizedPupilY) || !double.IsFinite(sample.Intensity))
                throw new InvalidOperationException("Zernike 拟合样本必须有限。");
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

        var coefficients = QrLeastSquares.Solve(design, target, requireFullRank);
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
        ArgumentNullException.ThrowIfNull(coefficients);
        if (!double.IsFinite(obscurationRatio) || obscurationRatio is < 0 or > 0.95)
        {
            throw new ArgumentOutOfRangeException(nameof(obscurationRatio));
        }

        TrimAnnularCache();
        var radius = Math.Sqrt((x * x) + (y * y));
        var angle = Math.Atan2(y, x);
        TrimAnnularCache();
        var obscuration = obscurationRatio;
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
        count = ResolveFringeTermCount(count);
        return ZemaxFringeIndices
            .Take(count)
            .Select((index, position) => (position + 1, index.N, index.M))
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

                // Noll/OpticStudio Standard numbering: even j is cosine, odd j is sine.
                var signedM = (result.Count + 1) % 2 == 0 ? absoluteM : -absoluteM;
                result.Add((result.Count + 1, n, signedM));
                if (result.Count < count)
                {
                    result.Add((result.Count + 1, n, -signedM));
                }
            }
        }

        return result;
    }

    private static void ValidateTermCount(int count, int maximum)
    {
        if (count < 1 || count > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private static void TrimAnnularCache()
    {
        if (AnnularRadialCache.Count >= MaximumAnnularCacheEntries)
        {
            AnnularRadialCache.Clear();
        }
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
        var absoluteM = Math.Abs(m);
        var recurrence = AnnularRadialCache.GetOrAdd(
            (n, absoluteM, BitConverter.DoubleToInt64Bits(obscuration)),
            _ => BuildAnnularRecurrence(n, absoluteM, obscuration));
        var u = (2 * radius * radius - 1 - obscuration * obscuration) / (1 - obscuration * obscuration);
        var previous = 0.0;
        var current = 1 / recurrence.InitialNorm;
        for (var k = 0; k < recurrence.Alpha.Length; k++)
        {
            var next = ((u - recurrence.Alpha[k]) * current - recurrence.Beta[k] * previous) / recurrence.Beta[k + 1];
            previous = current;
            current = next;
        }
        var radial = Math.Pow(radius, absoluteM) * current;
        return m >= 0 ? radial * Math.Cos(m * angle) : radial * Math.Sin(absoluteM * angle);
    }

    private sealed record AnnularRecurrence(double InitialNorm, double[] Alpha, double[] Beta);

    private static AnnularRecurrence BuildAnnularRecurrence(int n, int absoluteM, double obscuration)
    {
        // Stieltjes recurrence in scaled radius-squared avoids subtracting large monomial
        // moments on a thin annulus. 32-point Gauss integration is exact for the polynomial
        // degrees needed by all 231 terms (n <= 20), up to floating-point roundoff.
        var samples = ApertureSampler.GenerateGaussianQuadrature(32, 1, obscuration);
        var span = 1 - obscuration * obscuration;
        var u = samples.Select(s => (2 * s.X * s.X - 1 - obscuration * obscuration) / span).ToArray();
        var weights = samples.Select(s => s.Weight / span * Math.Pow(s.X, 2 * absoluteM)
            * (absoluteM == 0 ? 1 : .5)).ToArray();
        var initialNorm = Math.Sqrt(weights.Sum());
        var degree = (n - absoluteM) / 2;
        var alpha = new double[degree];
        var beta = new double[degree + 1];
        var previous = new double[samples.Count];
        var current = Enumerable.Repeat(1 / initialNorm, samples.Count).ToArray();
        for (var k = 0; k < degree; k++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var i = 0; i < u.Length; i++) alpha[k] += weights[i] * u[i] * current[i] * current[i];
            var next = new double[u.Length];
            for (var i = 0; i < u.Length; i++)
            {
                next[i] = (u[i] - alpha[k]) * current[i] - beta[k] * previous[i];
                beta[k + 1] += weights[i] * next[i] * next[i];
            }
            beta[k + 1] = Math.Sqrt(beta[k + 1]);
            if (!double.IsFinite(beta[k + 1]) || beta[k + 1] <= 1e-14)
                throw new InvalidOperationException("环形 Zernike 基底无法稳定归一化。");
            for (var i = 0; i < u.Length; i++) next[i] /= beta[k + 1];
            previous = current;
            current = next;
        }
        return new(initialNorm, alpha, beta);
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
