using System.Numerics;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;

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
        HuygensFourierSum.ValidateCandidateWork(2048, 32);
        var clippedPlan = HuygensFourierSum.CreatePlan(1_200_000, 32);
        Assert.NotNull(clippedPlan);
        Assert.InRange(clippedPlan.Work, 1, AnalysisResourceLimits.MaximumDirectPsfOperations);
        Assert.InRange(clippedPlan.Remainder, 0, 1e-10);
        Assert.Null(HuygensFourierSum.CreatePlan(3_290_000, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuygensFourierSum.ValidateCandidateWork(8192, 1024));
        Assert.Null(HuygensFourierSum.CreatePlan(int.MaxValue, 1024));
        Assert.Throws<ArgumentException>(() => HuygensFourierSum.Evaluate([new(Complex.One, double.NaN, 0)], 8));
    }

    [Fact]
    public void DenseSourcesUseOnlyTheirRequiredMomentsWithoutChangingTheField()
    {
        const int count = 700_000;
        const int size = 64;
        HuygensFourierEmitter[] pattern =
        [
            new(new(.3, .2), Math.PI / 16, .004),
            new(new(-.3, -.2), Math.PI / 16, .004),
            new(new(.2, -.15), .47, -.071),
            new(new(.4, .3), -.31, .22)
        ];
        var emitters = Enumerable.Range(0, count).Select(i =>
        {
            var source = pattern[i % pattern.Length];
            return source with { Weight = source.Weight * (pattern.Length / (double)count) };
        }).ToArray();
        Assert.Null(HuygensFourierSum.CreatePlan(count, size));
        var actual = HuygensFourierSum.Evaluate(emitters, size);
        Assert.True(actual.Diagnostics.UsesAdaptiveSourceOrders);
        Assert.InRange(actual.Diagnostics.EstimatedOperations, 1, AnalysisResourceLimits.MaximumDirectPsfOperations);
        for (var row = 0; row < size; row++)
            for (var column = 0; column < size; column++)
            {
                var expected = pattern.Aggregate(Complex.Zero, (sum, e) => sum + e.Weight * Complex.Exp(
                    Complex.ImaginaryOne * (e.PhaseStepX * (column - size / 2) + e.PhaseStepY * (row - size / 2))));
                Assert.InRange((expected - actual.Field[row, column]).Magnitude, 0, actual.Diagnostics.AbsoluteFieldErrorBound);
            }
    }

    [Fact]
    public void DenseWorstCaseResidualsStillRespectTheBudget()
    {
        var emitters = Enumerable.Repeat(new HuygensFourierEmitter(Complex.One, .02454, .02454), 700_000).ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => HuygensFourierSum.Evaluate(emitters, 64));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.45)]
    public void BatchedPlanarPupilPreservesTheFullImageAndItsNormalization(double hy)
    {
        var optic = new Optic("Planar propagation control");
        optic.Aperture.Value = 4;
        optic.Fields.Add(new FieldPoint());
        optic.Fields.Add(new FieldPoint { YAngleDegrees = 3 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 2.2, Thickness = 4 },
            new OpticalSurface { Radius = 50, SemiDiameter = 5, Thickness = 4, Material = "N-BK7" },
            new OpticalSurface { Radius = -50, SemiDiameter = 5, Thickness = 47, Material = "Air" },
            new OpticalSurface { SemiDiameter = 10 }
        ]);
        var wave = optic.Wavelengths[0];
        const int pupil = 32, image = 16;
        const double pitch = .00025;
        var plan = Assert.IsType<HuygensFourierSum.Plan>(HuygensFourierSum.CreateBatchedPlan(pupil, image, hy != 0));
        var batched = DiffractionEngine.ComputeBatchedPlanarHuygens(optic, (0, hy), wave, wave, pupil, image, pitch, plan);
        var direct = DiffractionEngine.ComputeHuygensPsf(optic, (0, hy), wave, pupil, image, pitch,
            aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
        var checkedSum = Assert.Single(batched.HuygensFourierChecks!);
        Assert.Equal(HuygensFourierSum.PupilBatchSize, checkedSum.PupilBatchSize);
        Assert.False(checkedSum.CertifiesPupilSampling);
        Assert.Equal(direct.SampleSpacingMicrometers, batched.SampleSpacingMicrometers);
        for (var row = 0; row < image; row++)
            for (var column = 0; column < image; column++)
                Assert.InRange(Math.Abs(direct.Values[row, column] - batched.Values[row, column]), 0, 1e-8);
        Assert.InRange(Math.Abs(direct.PeakStrehlRatio - batched.PeakStrehlRatio), 0, 1e-10);
    }

    [Fact]
    public void BatchedPlansHaveSeparateAggregateTraceAndWorkingSetLimits()
    {
        Assert.NotNull(HuygensFourierSum.CreateBatchedPlan(4096, 32, separateNormalizer: false));
        Assert.Null(HuygensFourierSum.CreateBatchedPlan(4096, 32, separateNormalizer: true));
        Assert.Null(HuygensFourierSum.CreateBatchedPlan(4096, 256, separateNormalizer: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => HuygensFourierSum.ValidateCandidateWork(4096, 32));
    }

    [Theory]
    [InlineData("Huygens PSF", "4096 x 4096")]
    [InlineData("Huygens PSF Cross Section", "4096")]
    public void FiniteImageAnalysisOffersTheHighPupilSetting(string analysis, string option)
    {
        var optic = Optic.CreateBlank();
        var runtime = new WorkbenchRuntime(optic);
        IReadOnlyList<string> PupilOptions() => runtime.GetAnalysisParameters(analysis).Single(p => p.Key == "PupilSampling").Choices!;
        Assert.Contains(option, PupilOptions());
        optic.ImageSpaceAfocal = true;
        Assert.DoesNotContain(option, PupilOptions());
    }
}
