using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class FftPsfNormalizationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(2)]
    [InlineData(4)]
    public void UnaberratedGaussianUsesItsOwnIdealPeak(double factor)
    {
        // An afocal plane aperture has exactly constant phase. Gaussian illumination
        // changes its diffraction shape, not its Strehl ratio relative to itself.
        var optic = Disk(factor);
        var data = new PsfAnalysis(optic, numRays: 32, gridSize: 64, wavelengthNumber: 1,
            fieldNumber: 1, zemaxCompatible: true, normalize: false).GenerateData();
        Assert.InRange(Math.Abs(Convert.ToDouble(data.Values["StrehlRatio"]) - 1), 0, 2e-12);

        // Independent discrete Parseval and zero-phase sum on the prescribed
        // 32-node grid: no traced intensity or native values construct the expectation.
        var amplitudes = NativeGaussianAmplitudes(factor).ToArray();
        var coherent = amplitudes.Sum();
        var meanPower = amplitudes.Average(a => a * a);
        var idealRatio = coherent * coherent / (meanPower * amplitudes.Length * amplitudes.Length);
        Assert.InRange(Math.Abs(Convert.ToDouble(data.Values["UnaberratedPeakRatio"]) - idealRatio), 0, 2e-12);
        var expectedSum = 64d * 64 * amplitudes.Sum(a => a * a) / (coherent * coherent);
        var actualSum = data.PlotSeries.Single().Points.Sum(p => p.Value!.Value);
        Assert.InRange(Math.Abs(actualSum / expectedSum - 1), 0, 2e-12);
    }

    [Fact]
    public void AberratedGaussianReferenceDoesNotFollowTheActualPeak()
    {
        var optic = Disk(2);
        var wave = optic.Wavelengths[0];
        var pupil = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: true, zemaxCentered: true, referenceWavelength: wave);
        var aberrated = pupil with { Samples = pupil.Samples.Select(s => s with
            { OpdWaves = .2 * s.NormalizedPupilX }).ToArray() };
        var actual = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true, preparedWavefront: aberrated,
            referenceWavelength: wave);
        var coordinates = Coordinates().ToArray();
        var sum = coordinates.Sum(p => Math.Exp(-2 * (p.X * p.X + p.Y * p.Y)));
        var field = coordinates.Aggregate(Complex.Zero, (a, p) => a + Complex.FromPolarCoordinates(
            Math.Exp(-2 * (p.X * p.X + p.Y * p.Y)), -2 * Math.PI * .2 * p.X));
        var expectedStrehl = field.Magnitude * field.Magnitude / (sum * sum);
        Assert.InRange(Math.Abs(actual.StrehlRatio / actual.UnaberratedPeakRatio - expectedStrehl), 0, 2e-12);
        Assert.True(expectedStrehl < .95);
        Assert.InRange(Math.Abs(actual.UnaberratedPeakRatio - DiffractionEngine.ComputeFftPsf(
            optic, (0, 0), wave, 32, 64, cellCenteredPupil: true, zemaxFftSampling: true,
            preparedWavefront: pupil, referenceWavelength: wave).UnaberratedPeakRatio), 0, 2e-12);
    }

    [Fact]
    public void GeneralPurposeAnalysisRetainsItsExistingPowerScale()
    {
        var optic = Disk(2);
        var raw = DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 32, 64);
        var data = new PsfAnalysis(optic, numRays: 32, gridSize: 64, wavelengthNumber: 1,
            fieldNumber: 1).GenerateData();
        Assert.Equal(raw.StrehlRatio, Convert.ToDouble(data.Values["StrehlRatio"]), 12);
        Assert.True(raw.StrehlRatio < .8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeightedSpectrumAndCrossSectionKeepUnitIdealStrehl(bool normalized)
    {
        var optic = Disk(2, "WAVM 1 0.55 1\nWAVM 2 0.65 2");
        var image = new PsfAnalysis(optic, numRays: 32, gridSize: 64, wavelengthNumber: 0,
            fieldNumber: 1, zemaxCompatible: true, normalize: normalized).GenerateData();
        Assert.InRange(Math.Abs(Convert.ToDouble(image.Values["StrehlRatio"]) - 1), 0, 2e-12);
        var profile = new FftPsfCrossSectionAnalysis(optic, sampling: 32, gridSize: 64,
            wavelengthNumber: 0, fieldNumber: 1, normalize: normalized).GenerateData();
        Assert.InRange(Math.Abs(Convert.ToDouble(profile.Values["StrehlRatio"]) - 1), 0, 2e-12);
        Assert.InRange(Math.Abs(Convert.ToDouble(profile.Values["ProfileIdealPeak"]) - 1), 0, 2e-12);
    }

    private static Optic Disk(double factor, string wavelengths = "WAVM 1 0.55 1")
    {
        var optic = OpticalFormatCatalog.Import(FormattableString.Invariant($"MODE SEQ\nENPD 2\nGFAC {factor:R} 1\n{wavelengths}\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 1\nDISZ 10\nSURF 2\nDIAM 20"), ".zmx");
        optic.ImageSpaceAfocal = true;
        return optic;
    }

    private static IEnumerable<(double X, double Y)> Coordinates()
    {
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var px = (x - 16d) / 15; var py = (y - 16d) / 15;
            if (px * px + py * py <= 1) yield return (px, py);
        }
    }

    private static IEnumerable<double> NativeGaussianAmplitudes(double factor)
    {
        const double h = 1d / 15;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var px = (x - 16d) / 15; var py = (y - 16d) / 15;
            if (px * px + py * py > 1) continue;
            var amplitude = Math.Exp(-factor * (px * px + py * py));
            bool Inside(int ix, int iy) => Math.Pow((ix - 16d) / 15, 2) + Math.Pow((iy - 16d) / 15, 2) <= 1;
            if ((x + y) % 2 == 0 && Inside(x - 1, y) && Inside(x + 1, y)
                && Inside(x, y - 1) && Inside(x, y + 1))
                // Closed form for the prescribed Gaussian's four-point amplitude
                // stencil, independent of the traced powers and product helper.
                amplitude *= Math.Exp(-factor * h * h)
                    * (Math.Cosh(2 * factor * h * px) + Math.Cosh(2 * factor * h * py)) / 2;
            yield return amplitude;
        }
    }
}
