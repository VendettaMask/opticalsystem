using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Raytrace;

public enum PupilSampling
{
    UniformGrid,
    Hexapolar,
    Random,
    Sobol,
    LineX,
    LineY,
    Ring
}

public sealed record PupilSample(double X, double Y, double Weight);

public static class ApertureSampler
{
    public static IReadOnlyList<PupilSample> GenerateGaussianQuadrature(
        int radialSamples,
        int azimuthalSamples = 6,
        double obscuration = 0)
    {
        radialSamples = Math.Clamp(radialSamples, 1, 32);
        azimuthalSamples = Math.Clamp(azimuthalSamples, 1, 72);
        obscuration = Math.Clamp(obscuration, 0, 0.999999);

        var lower = obscuration * obscuration;
        var span = 1 - lower;
        var radial = new (double Radius, double Weight)[radialSamples];
        var rootsToFind = (radialSamples + 1) / 2;
        for (var rootIndex = 0; rootIndex < rootsToFind; rootIndex++)
        {
            var root = Math.Cos(Math.PI * (rootIndex + 0.75) / (radialSamples + 0.5));
            double derivative = 0;
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var previous = 1.0;
                var current = root;
                for (var order = 2; order <= radialSamples; order++)
                {
                    var next = (((2 * order) - 1) * root * current - ((order - 1) * previous)) / order;
                    previous = current;
                    current = next;
                }

                derivative = radialSamples * ((root * current) - previous) / ((root * root) - 1);
                var nextRoot = root - (current / derivative);
                if (Math.Abs(nextRoot - root) <= 1e-15)
                {
                    root = nextRoot;
                    break;
                }

                root = nextRoot;
            }

            var p0 = 1.0;
            var p1 = root;
            for (var order = 2; order <= radialSamples; order++)
            {
                var next = (((2 * order) - 1) * root * p1 - ((order - 1) * p0)) / order;
                p0 = p1;
                p1 = next;
            }

            derivative = radialSamples * ((root * p1) - p0) / ((root * root) - 1);
            var legendreWeight = 2 / ((1 - (root * root)) * derivative * derivative);
            SetRadialSample(rootIndex, -root, legendreWeight);
            SetRadialSample(radialSamples - rootIndex - 1, root, legendreWeight);
        }

        var samples = new List<PupilSample>(radialSamples * azimuthalSamples);
        foreach (var radialSample in radial)
        {
            for (var azimuth = 0; azimuth < azimuthalSamples; azimuth++)
            {
                var angle = 2 * Math.PI * azimuth / azimuthalSamples;
                samples.Add(new PupilSample(
                    radialSample.Radius * Math.Cos(angle),
                    radialSample.Radius * Math.Sin(angle),
                    radialSample.Weight / azimuthalSamples));
            }
        }

        return samples;

        void SetRadialSample(int index, double node, double legendreWeight)
        {
            var normalizedRadiusSquared = (node + 1) / 2;
            radial[index] = (
                Math.Sqrt(lower + (span * normalizedRadiusSquared)),
                span * legendreWeight / 2);
        }
    }

    public static IReadOnlyList<PupilSample> GenerateHexapolarRings(int numRings)
    {
        numRings = Math.Max(0, numRings);
        var samples = new List<PupilSample> { new(0, 0, 1) };
        for (var ring = 1; ring <= numRings; ring++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var radius = ring / (double)numRings;
            var points = ring * 6;
            for (var index = 0; index < points; index++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var angle = 2 * Math.PI * index / points;
                samples.Add(new PupilSample(radius * Math.Cos(angle), radius * Math.Sin(angle), 1));
            }
        }

        return samples;
    }

    public static IReadOnlyList<PupilSample> Generate(int sampleCount, PupilSampling sampling, int seed = 1234)
    {
        sampleCount = Math.Max(1, sampleCount);
        return sampling switch
        {
            PupilSampling.Hexapolar => Hexapolar(sampleCount),
            PupilSampling.Random => Random(sampleCount, seed),
            PupilSampling.Sobol => SobolLike(sampleCount),
            PupilSampling.LineX => LineX(sampleCount),
            PupilSampling.LineY => LineY(sampleCount),
            PupilSampling.Ring => Ring(sampleCount),
            _ => UniformGrid(sampleCount)
        };
    }

    private static IReadOnlyList<PupilSample> UniformGrid(int sampleCount)
    {
        var side = (int)Math.Ceiling(Math.Sqrt(sampleCount));
        var samples = new List<PupilSample>();
        for (var iy = 0; iy < side; iy++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var ix = 0; ix < side; ix++)
            {
                var x = side == 1 ? 0 : -1 + (2.0 * ix / (side - 1));
                var y = side == 1 ? 0 : -1 + (2.0 * iy / (side - 1));
                if ((x * x) + (y * y) <= 1.0)
                {
                    samples.Add(new PupilSample(x, y, 1));
                }
            }
        }

        return samples.Take(sampleCount).ToArray();
    }

    private static IReadOnlyList<PupilSample> Hexapolar(int sampleCount)
    {
        var rings = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sampleCount / 6.0)));
        return GenerateHexapolarRings(rings).Take(sampleCount).ToArray();
    }

    private static IReadOnlyList<PupilSample> Random(int sampleCount, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, sampleCount)
            .Select(_ =>
            {
                var radius = Math.Sqrt(random.NextDouble());
                var angle = random.NextDouble() * 2 * Math.PI;
                return new PupilSample(radius * Math.Cos(angle), radius * Math.Sin(angle), 1);
            })
            .ToArray();
    }

    private static IReadOnlyList<PupilSample> SobolLike(int sampleCount)
    {
        return Enumerable.Range(0, sampleCount)
            .Select(index =>
            {
                var u = VanDerCorput(index + 1, 2);
                var v = VanDerCorput(index + 1, 3);
                var radius = Math.Sqrt(u);
                var angle = 2 * Math.PI * v;
                return new PupilSample(radius * Math.Cos(angle), radius * Math.Sin(angle), 1);
            })
            .ToArray();
    }

    private static IReadOnlyList<PupilSample> LineX(int sampleCount)
    {
        if (sampleCount == 1)
        {
            return new[] { new PupilSample(0, 0, 1) };
        }

        return Enumerable.Range(0, sampleCount)
            .Select(index => new PupilSample(-1 + (2.0 * index / (sampleCount - 1)), 0, 1))
            .ToArray();
    }

    private static IReadOnlyList<PupilSample> LineY(int sampleCount)
    {
        if (sampleCount == 1)
        {
            return new[] { new PupilSample(0, 0, 1) };
        }

        return Enumerable.Range(0, sampleCount)
            .Select(index => new PupilSample(0, -1 + (2.0 * index / (sampleCount - 1)), 1))
            .ToArray();
    }

    private static IReadOnlyList<PupilSample> Ring(int sampleCount)
    {
        return Enumerable.Range(0, sampleCount)
            .Select(index =>
            {
                var angle = 2 * Math.PI * index / sampleCount;
                return new PupilSample(Math.Cos(angle), Math.Sin(angle), 1);
            })
            .ToArray();
    }

    private static double VanDerCorput(int n, int basis)
    {
        var denominator = 1.0;
        var result = 0.0;
        while (n > 0)
        {
            denominator *= basis;
            result += (n % basis) / denominator;
            n /= basis;
        }

        return result;
    }
}
