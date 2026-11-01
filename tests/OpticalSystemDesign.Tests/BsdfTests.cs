using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Scattering;

namespace OpticalSystemDesign.Tests;

public sealed class BsdfTests
{
    [Theory]
    [InlineData(0)] [InlineData(.25)] [InlineData(1)]
    public void HemisphericalReflectancePdfNormalizationAndReciprocityMatchIndependentIntegrals(double rho)
    {
        IBsdf model = new LambertianReflectionBsdf(rho);
        var context = new BsdfContext(new(.3, 0, 1), new(0, 0, 1), 550);
        double flux = 0, probability = 0;
        const int bands = 256;
        for (var i = 0; i < bands; i++)
        {
            var cosine = (i + .5) / bands; var outgoing = new Vector3D(Math.Sqrt(1 - cosine * cosine), 0, cosine);
            flux += model.Evaluate(context, outgoing) * cosine * 2 * Math.PI / bands;
            probability += model.Pdf(context, outgoing) * 2 * Math.PI / bands;
            Assert.Equal(model.Evaluate(context, outgoing), model.Evaluate(new(outgoing, new(0, 0, 1), 550), context.IncomingDirection));
        }
        Assert.InRange(Math.Abs(flux - rho), 0, 2e-14); Assert.InRange(Math.Abs(probability - 1), 0, 2e-14);
        Assert.Equal(0, model.Evaluate(context, new(0, 0, -1))); Assert.Equal(0, model.Pdf(context, new(0, 0, -1)));
    }

    [Theory]
    [InlineData(0, 0, 1)] [InlineData(0, 0, -1)] [InlineData(.3, -.4, .8660254037844386)]
    public void CosineSamplesHaveTheAnalyticalCdfMomentsAzimuthAndPowerWeight(double nx, double ny, double nz)
    {
        var normal = new Vector3D(nx, ny, nz); normal /= normal.Length;
        var context = new BsdfContext(normal, normal, 650); var model = new LambertianReflectionBsdf(.7);
        var random = new Random(817); const int count = 65536;
        var cdf = new int[9]; double mean = 0, second = 0; var directionMean = new Vector3D(0, 0, 0);
        var azimuth = new int[16];
        var tangent = Math.Abs(nz) > .9 ? new Vector3D(1, 0, 0) : new Vector3D(ny, -nx, 0);
        tangent /= tangent.Length;
        var b = new Vector3D(normal.Y * tangent.Z - normal.Z * tangent.Y, normal.Z * tangent.X - normal.X * tangent.Z, normal.X * tangent.Y - normal.Y * tangent.X);
        for (var i = 0; i < count; i++)
        {
            var s = model.Sample(context, random.NextDouble(), random.NextDouble());
            var cosine = Dot(s.Direction, normal); Assert.InRange(Math.Abs(s.Direction.Length - 1), 0, 2e-14);
            Assert.True(cosine > 0); Assert.Equal(model.Pdf(context, s.Direction), s.Pdf);
            Assert.InRange(Math.Abs(s.Value * cosine / s.Pdf - .7), 0, 2e-14);
            mean += cosine; second += cosine * cosine; directionMean += s.Direction;
            for (var k = 1; k < 9; k++) if (cosine <= k / 10d) cdf[k]++;
            var phi = Math.Atan2(Dot(s.Direction, b), Dot(s.Direction, tangent));
            azimuth[Math.Min(15, (int)((phi + Math.PI) / (2 * Math.PI) * 16))]++;
        }
        Assert.InRange(Math.Abs(mean / count - 2d / 3), 0, 6 * Math.Sqrt(1d / (18 * count)));
        Assert.InRange(Math.Abs(second / count - .5), 0, 6 * Math.Sqrt(1d / (12 * count)));
        Assert.InRange((directionMean / count - normal * (2d / 3)).Length, 0, .015);
        for (var k = 1; k < 9; k++)
        {
            var p = Math.Pow(k / 10d, 2);
            Assert.InRange(Math.Abs(cdf[k] / (double)count - p), 0, 6 * Math.Sqrt(p * (1 - p) / count));
        }
        Assert.All(azimuth, n => Assert.InRange(Math.Abs(n - count / 16d), 0, 6 * Math.Sqrt(count / 16d * 15 / 16)));
        NonSequentialBsdfTests.Record($"samples-{nx}-{ny}-{nz}", new { Normal = new[] { nx, ny, nz }, Count = count,
            Seed = 817, MeanCosine = mean / count, CosineSecondMoment = second / count, CdfCounts = cdf, AzimuthCounts = azimuth,
            ExpectedMeanCosine = 2d / 3, ExpectedCosineSecondMoment = .5, SigmaMultiplier = 6 });
    }

    [Theory]
    [InlineData(-.1)] [InlineData(1.1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidAlbedoIsRejectedWithoutClamping(double rho) => Assert.Throws<ArgumentOutOfRangeException>(() => new LambertianReflectionBsdf(rho));

    [Fact]
    public void EndpointSamplesAreFiniteAndInvalidDomainsAreExplicit()
    {
        var m = new LambertianReflectionBsdf(1); var c = new BsdfContext(new(0, 0, 1), new(0, 0, 1), 550);
        foreach (var u in new[] { 0d, Math.BitDecrement(1d) }) Assert.True(m.Sample(c, u, 0).Pdf > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Sample(c, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Sample(c, 0, double.NaN));
        Assert.Throws<ArgumentException>(() => new BsdfContext(new(0, 0, -1), new(0, 0, 1), 550));
        Assert.Throws<ArgumentException>(() => m.Evaluate(c, new(0, 0, 0)));
    }
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
