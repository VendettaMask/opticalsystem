using System.Numerics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class PupilShapeAndFoucaultPhysicsTests
{
    [Theory]
    [InlineData(FoucaultKnifeEdge.HorizontalAbove)]
    [InlineData(FoucaultKnifeEdge.HorizontalBelow)]
    [InlineData(FoucaultKnifeEdge.VerticalLeft)]
    [InlineData(FoucaultKnifeEdge.VerticalRight)]
    public void PhysicalFilterMatchesIndependentDirectFourierSum(FoucaultKnifeEdge knife)
    {
        var input = Pupil();
        var actual = FoucaultEngine.FilterPupil(input, 16, .4, .7, knife, .21);
        var expected = DirectReference(input, 16, .4, .7, knife, .21);
        for (var row = 0; row < 16; row++)
        for (var column = 0; column < 16; column++)
            Assert.InRange((actual[row, column] - expected[row, column]).Magnitude, 0, 2e-12);
        Assert.InRange(Power(actual), 0, Power(input) * (1 + 1e-12));
    }

    [Theory]
    [InlineData(FoucaultKnifeEdge.HorizontalAbove, 1)]
    [InlineData(FoucaultKnifeEdge.HorizontalBelow, -1)]
    [InlineData(FoucaultKnifeEdge.VerticalLeft, -1)]
    [InlineData(FoucaultKnifeEdge.VerticalRight, 1)]
    public void OpenAndClosedKnivesRecoverOriginalFieldOrZero(FoucaultKnifeEdge knife, int openSign)
    {
        var input = Pupil();
        var opened = FoucaultEngine.FilterPupil(input, 16, .4, .7, knife, openSign * 1e6);
        var closed = FoucaultEngine.FilterPupil(input, 16, .4, .7, knife, -openSign * 1e6);
        for (var row = 0; row < 16; row++)
        for (var column = 0; column < 16; column++)
        {
            var expected = row is >= 6 and < 10 && column is >= 6 and < 10 ? input[row - 6, column - 6] : Complex.Zero;
            Assert.InRange((opened[row, column] - expected).Magnitude, 0, 2e-12);
            Assert.Equal(Complex.Zero, closed[row, column]);
        }
        Assert.Equal(Power(input), Power(opened), 10);
    }

    [Theory]
    [InlineData(FoucaultKnifeEdge.HorizontalAbove, FoucaultKnifeEdge.HorizontalBelow)]
    [InlineData(FoucaultKnifeEdge.VerticalLeft, FoucaultKnifeEdge.VerticalRight)]
    public void ComplementaryKnivesSumAsFieldsNotIntensities(FoucaultKnifeEdge first, FoucaultKnifeEdge second)
    {
        var input = Pupil();
        var a = FoucaultEngine.FilterPupil(input, 16, .4, .7, first, .1);
        var b = FoucaultEngine.FilterPupil(input, 16, .4, .7, second, .1);
        for (var row = 0; row < 16; row++)
        for (var column = 0; column < 16; column++)
        {
            var expected = row is >= 6 and < 10 && column is >= 6 and < 10 ? input[row - 6, column - 6] : Complex.Zero;
            Assert.InRange((a[row, column] + b[row, column] - expected).Magnitude, 0, 2e-12);
        }
    }

    [Theory]
    [InlineData(1, FoucaultKnifeEdge.VerticalLeft, 1)]
    [InlineData(1, FoucaultKnifeEdge.VerticalRight, 0)]
    [InlineData(-1, FoucaultKnifeEdge.VerticalLeft, 0)]
    [InlineData(-1, FoucaultKnifeEdge.VerticalRight, 1)]
    public void LocalXKnifeDirectionHasPhysicalSign(int frequency, FoucaultKnifeEdge knife, int expectedPower)
    {
        var input = new Complex[8, 8];
        for (var row = 0; row < 8; row++)
        for (var column = 0; column < 8; column++)
            input[row, column] = Complex.FromPolarCoordinates(1, 2 * Math.PI * frequency * column / 8);
        Assert.Equal(expectedPower * 64, Power(FoucaultEngine.FilterPupil(input, 8, 1, 1, knife, 0)), 10);
    }

    [Fact]
    public void PhysicalSpacingAndPositionScaleTogetherWithoutChangingTheField()
    {
        var a = FoucaultEngine.FilterPupil(Pupil(), 16, .4, .7, FoucaultKnifeEdge.HorizontalAbove, .21);
        var b = FoucaultEngine.FilterPupil(Pupil(), 16, .8, 1.4, FoucaultKnifeEdge.HorizontalAbove, .42);
        Assert.Equal(a.Cast<Complex>(), b.Cast<Complex>());
    }

    [Fact]
    public void CommonPistonDoesNotChangeIntensityButLocalPhaseDoes()
    {
        var input = Pupil();
        var shifted = (Complex[,])input.Clone();
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++) shifted[row, column] *= Complex.FromPolarCoordinates(1, 1.2);
        var a = FoucaultEngine.FilterPupil(input, 16, .4, .7, FoucaultKnifeEdge.VerticalLeft, .1);
        var b = FoucaultEngine.FilterPupil(shifted, 16, .4, .7, FoucaultKnifeEdge.VerticalLeft, .1);
        for (var row = 0; row < 16; row++)
        for (var column = 0; column < 16; column++) Assert.Equal(a[row, column].Magnitude, b[row, column].Magnitude, 11);
        shifted[1, 1] *= Complex.ImaginaryOne;
        var changed = FoucaultEngine.FilterPupil(shifted, 16, .4, .7, FoucaultKnifeEdge.VerticalLeft, .1);
        Assert.Contains(a.Cast<Complex>().Zip(changed.Cast<Complex>()), pair => Math.Abs(pair.First.Magnitude - pair.Second.Magnitude) > 1e-4);
    }

    [Fact]
    public void UnpolarizedJonesImageIsIncoherentAverageOfTwoInputStates()
    {
        var optic = TransparentCooke();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        optic.Polarization = new(Unpolarized: true);
        var unpolarized = FoucaultEngine.Compute(optic, (0, 0), wave, 8, FoucaultKnifeEdge.VerticalLeft, 0, true);
        optic.Polarization = new(Unpolarized: false, Jx: 1, Jy: 0);
        var x = FoucaultEngine.Compute(optic, (0, 0), wave, 8, FoucaultKnifeEdge.VerticalLeft, 0, true);
        optic.Polarization = new(Unpolarized: false, Jx: 0, Jy: 1);
        var y = FoucaultEngine.Compute(optic, (0, 0), wave, 8, FoucaultKnifeEdge.VerticalLeft, 0, true);
        for (var row = 0; row < 8; row++)
        for (var column = 0; column < 8; column++)
            Assert.Equal((x.Intensity[row, column] + y.Intensity[row, column]) / 2, unpolarized.Intensity[row, column], 11);
        Assert.Equal((x.InputPower + y.InputPower) / 2, unpolarized.InputPower, 11);
        Assert.InRange(unpolarized.KnifeThroughput, 0, 1 + 1e-12);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoatingPowerIsAppliedOnceWithoutShadowgramPeakNormalization(bool polarized)
    {
        var optic = TransparentCooke();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var surface = optic.SurfaceGroup.Items[1];
        surface.CoatingModel = new SimpleCoatingModel(.5);
        var half = FoucaultEngine.Compute(optic, (0, 0), wave, 8, FoucaultKnifeEdge.HorizontalAbove, .2, polarized);
        surface.CoatingModel = new SimpleCoatingModel(.25);
        var quarter = FoucaultEngine.Compute(optic, (0, 0), wave, 8, FoucaultKnifeEdge.HorizontalAbove, .2, polarized);
        Assert.Equal(half.InputPower / 2, quarter.InputPower, 10);
        Assert.Equal(half.OutputPower / 2, quarter.OutputPower, 10);
        for (var row = 0; row < 8; row++)
        for (var column = 0; column < 8; column++)
            Assert.Equal(half.Intensity[row, column] / 2, quarter.Intensity[row, column], 10);
    }

    [Fact]
    public void UnsupportedAbsorbingJonesInterfaceDoesNotFallBackToScalar()
    {
        var optic = Optic.CreateCookeTriplet();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        Assert.Throws<NotSupportedException>(() => FoucaultEngine.Compute(optic, (0, 0), wave,
            8, FoucaultKnifeEdge.VerticalRight, 0, usePolarization: true));
    }

    [Fact]
    public void KnifeDiffractionOutsideInputApertureIsRetained()
    {
        var input = new Complex[8, 8];
        input[4, 4] = Complex.One;
        var output = FoucaultEngine.FilterPupil(input, 32, 1, 1, FoucaultKnifeEdge.VerticalRight, 0);
        Assert.True(output[16, 15].Magnitude > .01); // outside the one illuminated input cell
        Assert.InRange(Power(output), 0, 1);
    }

    [Fact]
    public void PhysicalFourierPropagationHonorsCancellation()
    {
        using var scope = ComputationCancellation.Push(new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(() => FoucaultEngine.FilterPupil(Pupil(), 16,
            .4, .7, FoucaultKnifeEdge.HorizontalAbove, 0));
    }

    [Fact]
    public void AfocalExitShapeExplicitlyUsesNormalizedCoordinatesRatherThanInventedFNumbers()
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = true;
        var result = new WavefrontAnalysis(optic, pupilSampling: 8, useExitPupilShape: true).GenerateData();
        Assert.Equal(1, result.PlotOptions!.SpatialDisplayScaleX);
        Assert.Equal(1, result.PlotOptions.SpatialDisplayScaleY);
        Assert.Contains("afocal", result.Values["ExitPupilDisplayModel"].ToString());
        Assert.Equal("Not evaluated", result.Values["ExitPupilWorkingFNumberX"]);
    }

    [Fact]
    public void DisplayModesPreservePhysicalValuesAndClosedKnifeStaysBlack()
    {
        var optic = Optic.CreateCookeTriplet();
        var linear = new FoucaultAnalysis(optic, sampling: 8).GenerateData();
        var log = new FoucaultAnalysis(optic, sampling: 8, type: "对数", displayAs: "伪彩色").GenerateData();
        Assert.Equal(linear.PlotSeries.Single().Points, log.PlotSeries.Single().Points);
        var dark = new FoucaultAnalysis(optic, sampling: 8, positionMicrometers: -1e6).GenerateData();
        Assert.All(dark.PlotSeries.Single().Points, p => Assert.Equal(0, p.Value));
        Assert.Equal(0d, dark.Values["KnifeThroughput"]);
        Assert.DoesNotContain("Qualitative", linear.Values["ComputationModel"].ToString());
    }

    [Fact]
    public void AfocalSystemAndUnimplementedDataSourcesAreExplicitlyRejected()
    {
        var optic = Optic.CreateCookeTriplet(); optic.ImageSpaceAfocal = true;
        Assert.Throws<NotSupportedException>(() => new FoucaultAnalysis(optic, sampling: 8).GenerateData());
        Assert.Throws<NotSupportedException>(() => new FoucaultAnalysis(optic, dataSource: "参考"));
        Assert.Throws<ArgumentException>(() => new FoucaultAnalysis(optic, type: "二次"));
        Assert.Throws<ArgumentException>(() => new FoucaultAnalysis(optic, knifeEdge: "unknown"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FoucaultAnalysis(optic, positionMicrometers: double.NaN));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExitPupilProjectionUsesDirectionalFNumbersAndPreservesPhysicalData(bool removeTilt)
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = true;
        var a = new WavefrontAnalysis(optic, pupilSampling: 16, useExitPupilShape: false, removeTilt: removeTilt).GenerateData();
        var b = new WavefrontAnalysis(optic, pupilSampling: 16, useExitPupilShape: true, removeTilt: removeTilt).GenerateData();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var axes = DiffractionEngine.WorkingFNumbers(optic, SpotAnalysisEngine.DefinedFields(optic).Last(), wave, true);
        Assert.Equal(a.PlotSeries.Single().Points, b.PlotSeries.Single().Points);
        Assert.Equal(axes.Tangential / axes.Sagittal, b.PlotOptions!.SpatialDisplayScaleX / b.PlotOptions.SpatialDisplayScaleY, 12);
        Assert.Equal(a.Values["RmsWaves"], b.Values["RmsWaves"]);
        Assert.Equal(a.Values["VignettedRayCount"], b.Values["VignettedRayCount"]);
        Assert.Equal(a.Values["ReferenceOpticalPathLength"], b.Values["ReferenceOpticalPathLength"]);
        Assert.True(b.PlotOptions.SpatialDisplayScaleX != 1 || b.PlotOptions.SpatialDisplayScaleY != 1);
    }

    internal static Optic TransparentCooke()
    {
        var optic = Optic.CreateCookeTriplet();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var materials = new Dictionary<string, IMaterial>();
        IMaterial Transparent(IMaterial source)
        {
            if (source is AirMaterial) return source;
            if (!materials.TryGetValue(source.Name, out var material))
            {
                material = new ConstantIndexMaterial("transparent test " + source.Name, source.RefractiveIndex(wave.Nanometers));
                materials.Add(source.Name, material); optic.Materials.Register(material);
            }
            return material;
        }
        foreach (var surface in optic.SurfaceGroup.Items)
        {
            surface.MaterialBefore = Transparent(surface.MaterialBefore);
            surface.MaterialAfter = Transparent(surface.MaterialAfter);
        }
        return optic;
    }

    private static Complex[,] Pupil()
    {
        var result = new Complex[4, 4];
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
            result[row, column] = Complex.FromPolarCoordinates(.2 + .1 * (row + column), .4 * row - .2 * column);
        result[0, 0] = Complex.Zero;
        return result;
    }

    private static double Power(Complex[,] grid) => grid.Cast<Complex>().Sum(v => v.Real * v.Real + v.Imaginary * v.Imaginary);

    // Test-only direct DFT reference, with no call to the product FFT or mask helper.
    private static Complex[,] DirectReference(Complex[,] pupil, int n, double dx, double dy, FoucaultKnifeEdge knife, double position)
    {
        var focal = new Complex[n, n]; var result = new Complex[n, n]; var offset = (n - 4) / 2;
        for (var ky = 0; ky < n; ky++)
        for (var kx = 0; kx < n; kx++)
        {
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                focal[ky, kx] += pupil[y, x] * Complex.FromPolarCoordinates(1, -2 * Math.PI * (kx * (x + offset) + ky * (y + offset)) / n);
            var horizontal = knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.HorizontalBelow;
            var index = horizontal ? ky : kx;
            var coordinate = (index < n / 2 ? index : index - n) * (horizontal ? dy : dx);
            var pitch = horizontal ? dy : dx;
            var lowerPart = Math.Max(0, Math.Min(coordinate + pitch / 2, position) - (coordinate - pitch / 2)) / pitch;
            lowerPart = Math.Min(1, lowerPart);
            focal[ky, kx] *= knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.VerticalRight ? lowerPart : 1 - lowerPart;
        }
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        for (var ky = 0; ky < n; ky++)
        for (var kx = 0; kx < n; kx++)
            result[y, x] += focal[ky, kx] * Complex.FromPolarCoordinates(1, 2 * Math.PI * (kx * x + ky * y) / n) / (n * n);
        return result;
    }
}
