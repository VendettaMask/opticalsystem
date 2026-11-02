using System.Numerics;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core;

namespace OpticalSystemDesign.Tests;

public sealed class HuygensFourierSumTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LargerImagePreservesEverySharedPhysicalPixelOfTheDirectImage(bool afocal)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.ImageSpaceAfocal = afocal;
        var pitch = afocal ? .002 : .0005;
        var large = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 512, 32, pitch);
        var direct = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 512, 3, pitch);
        Assert.NotEmpty(large.HuygensFourierChecks!);
        Assert.Null(large.HuygensSamplingChecks);
        Assert.Null(direct.HuygensFourierChecks);
        for (var row = 0; row < 3; row++) for (var column = 0; column < 3; column++)
            Assert.InRange(Math.Abs(large.Values[15 + row, 15 + column] - direct.Values[row, column]), 0, 1e-9);
    }

    [Fact]
    public void NonseparableSphericalPropagationRetainsTheDirectGuard()
    {
        var optic = Optic.CreateCookeTriplet();
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputeHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 512, 32, .1));
    }
    [Theory]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(32)]
    public void CompleteComplexFieldAgreesWithIndependentFiniteSum(int size)
    {
        var random = new Random(9041);
        var emitters = Enumerable.Range(0, 271).Select(i => new HuygensFourierEmitter(
            Complex.FromPolarCoordinates(.1 + random.NextDouble(), random.NextDouble() * 300 - 150),
            (random.NextDouble() - .5) * 60, (random.NextDouble() - .5) * 60)).ToArray();
        var result = HuygensFourierSum.Evaluate(emitters, size);
        double maximum = 0;
        for (var row = 0; row < size; row++) for (var column = 0; column < size; column++)
        {
            var expected = emitters.Aggregate(Complex.Zero, (sum, e) => sum + e.Weight * Complex.Exp(
                Complex.ImaginaryOne * (e.PhaseStepX * (column - size / 2) + e.PhaseStepY * (row - size / 2))));
            maximum = Math.Max(maximum, (expected - result.Field[row, column]).Magnitude);
        }
        Assert.InRange(maximum, 0, result.Diagnostics.AbsoluteFieldErrorBound);
        Assert.False(result.Diagnostics.CertifiesPupilSampling);
        Assert.InRange(result.Diagnostics.EstimatedOperations, 1, AnalysisResourceLimits.MaximumDirectPsfOperations);
    }

    [Fact]
    public void DestructiveInterferenceAndFrequencyAliasingDoNotBecomePeakNormalization()
    {
        HuygensFourierEmitter[] emitters = [new(new(1, 0), 20 * Math.PI + .17, -8 * Math.PI - .3), new(new(-1, 0), .17, -.3)];
        var result = HuygensFourierSum.Evaluate(emitters, 16);
        Assert.All(result.Field.Cast<Complex>(), value => Assert.InRange(value.Magnitude, 0, 1e-11));
    }

    [Fact]
    public void TheOriginalWorkLimitIsCheckedAgainstTheChosenAlgorithm()
    {
        HuygensFourierSum.ValidateCandidateWork(512, 32);
        HuygensFourierSum.ValidateCandidateWork(1024, 32);
        Assert.Throws<ArgumentOutOfRangeException>(() => HuygensFourierSum.ValidateCandidateWork(8192, 1024));
        Assert.Null(HuygensFourierSum.CreatePlan(int.MaxValue, 1024));
        Assert.Throws<ArgumentException>(() => HuygensFourierSum.Evaluate([new(Complex.One, double.NaN, 0)], 8));
    }
}
