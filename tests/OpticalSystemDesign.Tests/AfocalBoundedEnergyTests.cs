using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalBoundedEnergyTests
{
    [Theory]
    [InlineData(false, "chief", "encircled", false)]
    [InlineData(false, "centroid", "ensquared", true)]
    [InlineData(true, "chief", "ensquared", true)]
    [InlineData(true, "centroid", "encircled", false)]
    public void AngularWindowMatchesTheCompleteDenseFourierSum(bool monochromatic, string reference, string type, bool offAxis)
    {
        var field = (Hx: 0d, Hy: offAxis ? .7 : 0);
        DenseWindowAgrees(AfocalEnergyWorkflowTests.Riflescope(), field, monochromatic, reference, type, .5);
    }

    [Theory]
    [InlineData("chief")]
    [InlineData("centroid")]
    public void FiniteSpectralWindowRetainsTheDenseNormalization(string reference)
        => DenseWindowAgrees(Optic.CreateCookeTriplet(), (0, .5), false, reference, "encircled", 10);

    private static void DenseWindowAgrees(Optic optic, (double Hx, double Hy) field,
        bool monochromatic, string reference, string type, double maximum)
    {
        var waves = (monochromatic ? optic.Wavelengths.Take(1) : optic.Wavelengths).ToArray();
        const int n = 256; const int size = 2 * n;
        // This is a same-node finite-sum equivalence check, not a convergence
        // certificate. The separate guard test retains rejection of aliased OPD.
        var results = DiffractionEngine.ComputeFftCumulativeEnergySpectrum(optic, field, waves, n, false);
        var pitch = results[0].Result.SampleSpacingMicrometers;
        var weights = waves.Select(w => w.Weight / waves.Max(w => w.Weight)
            * Math.Pow(waves.Min(w => w.Micrometers) / w.Micrometers, 2)).ToArray();
        var samples = Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x =>
            new EnergySample((x - size / 2d + 1) * pitch, (y - size / 2d + 1) * pitch,
                results.Select((r, i) => weights[i] * r.Result.Values[(y + 1) % size, (x + 1) % size]).Sum()))).ToArray();
        var power = samples.Sum(s => s.Weight);
        var center = reference == "centroid"
            ? (samples.Sum(s => s.X * s.Weight) / power, samples.Sum(s => s.Y * s.Weight) / power) : (0d, 0d);
        var full = new FftCumulativeEnergyGrid(samples, center, type);
        var bounded = DiffractionEngine.ComputeBoundedSpectralCumulativeEnergy(
            optic, field, waves, n, reference, type, maximum, false);
        Assert.Equal(pitch, bounded.ImagePitch, 12);
        for (var i = 0; i < n; i++)
        {
            var radius = maximum * i / (n - 1d);
            Assert.InRange(Math.Abs(full.Fraction(radius) - bounded.Fraction(radius)), 0, 1e-10);
        }
        Assert.InRange(bounded.Fraction(maximum), .5, 1);
    }

    [Fact]
    public void BoundedAngularReadoutRetainsThePupilPhaseGuard()
    {
        var optic = AfocalEnergyWorkflowTests.Riflescope();
        using var required = FftPupilSampling.RequireResolvedResults();
        var rejection = Assert.Throws<FftPupilSamplingException>(() =>
            DiffractionEngine.ComputeBoundedSpectralCumulativeEnergy(optic, (0, 0), optic.Wavelengths.ToArray(),
                256, "centroid", "encircled", .5, false));
        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase, rejection.Diagnostics.Status);
        Assert.True(rejection.Diagnostics.MaximumStepWaves > FftPupilSampling.MaximumAdjacentStepWaves);
    }

    [Fact]
    public void AngularWindowIsIndependentOfTheReadoutPlanePosition()
    {
        var optic = AfocalEnergyWorkflowTests.Riflescope();
        IBoundedFftEnergyImageGrid Run() => DiffractionEngine.ComputeBoundedSpectralCumulativeEnergy(
            optic, (0, 0), optic.Wavelengths.ToArray(), 256, "centroid", "encircled", .5, true);
        var before = Run();
        optic.SurfaceGroup.Items[^2].Thickness += 100;
        var after = Run();
        Assert.Equal(before.ImagePitch, after.ImagePitch, 12);
        for (var i = 0; i <= 100; i++)
            Assert.Equal(before.Fraction(.5 * i / 100), after.Fraction(.5 * i / 100), 9);
    }
}
