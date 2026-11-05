using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class CroppedFourierTests
{
    [Theory]
    [InlineData(5, 16, 4, false)]
    [InlineData(8, 32, 6, true)]
    [InlineData(7, 16, 5, true)]
    public void PrunedTransformMatchesIndependentComplexDftAtEveryRequestedPhysicalFrequency(int sourceSize, int transformSize, int outputSize, bool native)
    {
        var pupil = new Complex[sourceSize, sourceSize];
        for (var y = 0; y < sourceSize; y++) for (var x = 0; x < sourceSize; x++)
            if ((x + 2 * y) % 5 != 0) pupil[y, x] = new Complex(Math.Sin(.7 * x + y), Math.Cos(x - .3 * y));
        var actual = DiffractionEngine.FourierTransformCropped(pupil, transformSize, outputSize, native);
        var center = native ? (outputSize - 1) / 2 : outputSize / 2;
        var padding = (transformSize - sourceSize) / 2;
        for (var row = 0; row < outputSize; row++) for (var column = 0; column < outputSize; column++)
        {
            var expected = Complex.Zero;
            for (var y = 0; y < sourceSize; y++) for (var x = 0; x < sourceSize; x++)
                expected += pupil[y, x] * Complex.FromPolarCoordinates(1,
                    -2 * Math.PI * ((row - center) * (y + padding) + (column - center) * (x + padding)) / transformSize);
            Assert.InRange((actual[row, column] - expected).Magnitude, 0, 3e-12);
        }
    }

    [Fact]
    public void CroppedPsfRetainsAbsoluteIntensityAndNeverWrapsTheReadoutBoundary()
    {
        var optic = Optic.CreateCookeTriplet(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var full = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64);
        var crop = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64, croppedOutputSize: 8);
        Assert.Equal(full.UnaberratedPeakRatio, crop.UnaberratedPeakRatio);
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
            Assert.InRange(Math.Abs(crop.Values[y, x] - full.Values[y + 28, x + 28]), 0, 1e-10);
        Assert.Equal(0, PsfAnalysis.BilinearSample(crop, 4 * crop.SampleSpacingMicrometers, 0));
        Assert.Equal(crop.Values[4, 7], PsfAnalysis.BilinearSample(crop, 3 * crop.SampleSpacingMicrometers, 0));
        var nativeCrop = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            zemaxFftSampling: true, cellCenteredPupil: true, croppedOutputSize: 8);
        Assert.Equal(nativeCrop.Values[3, 3] / 100, nativeCrop.StrehlRatio);
        Assert.Throws<InvalidOperationException>(() => nativeCrop.PeakStrehlRatio);
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputeFftMtf(nativeCrop, optic, wave));
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputePsfMtf(nativeCrop));
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputePsfMtfAtFrequencies(nativeCrop, new[] { 0d, 10 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ValidateCroppedFft(2048, 4096, 2048));
        Assert.Equal(2048, AnalysisResourceLimits.MaximumFftGridSize);
        var displayed = new PsfAnalysis(optic, 32, 4096, displaySize: 8).GenerateData();
        Assert.IsType<string>(displayed.Values["PeakStrehlRatio"]);
        Assert.True(Convert.ToDouble(displayed.Values["DisplayedWindowPeakRatio"]) > 0);
        Assert.Throws<AnalysisDataUnavailableException>(() => new PsfAnalysis(optic, 32, 4096,
            normalize: true, displaySize: 8).GenerateData());
    }

    [Fact]
    public void DenseSeparatedWindowMatchesTheFullTransformAtTheSamePhysicalPixels()
    {
        var optic = Optic.CreateCookeTriplet();
        var wave = optic.Wavelengths[0];
        var scout = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true);
        var pitch = scout.WorkingFNumber * wave.Micrometers / 10;
        const int samples = 64, logicalTransform = 512, output = 16;
        var analysis = new PsfAnalysis(optic, samples, logicalTransform, 1, 1,
            imageDeltaMicrometers: pitch, zemaxCompatible: true, displaySize: output);
        var full = analysis.GenerateData();
        var dense = analysis.GenerateDensePhysicalPupil(output);
        Assert.Equal(false, full.Values["CroppedFourierReadout"]);
        Assert.Equal(true, dense.Values["CroppedFourierReadout"]);
        var actual = Assert.Single(dense.PlotSeries).Points;
        var expected = Assert.Single(full.PlotSeries).Points;
        Assert.Equal(output * output, actual.Count);
        Assert.Equal(expected.Select(p => (p.X, p.Y)), actual.Select(p => (p.X, p.Y)));
        foreach (var (a, e) in actual.Zip(expected))
            Assert.InRange(Math.Abs(a.Value!.Value - e.Value!.Value), 0, 1e-12);
        Assert.Equal(Convert.ToDouble(full.Values["StrehlRatio"]), Convert.ToDouble(dense.Values["StrehlRatio"]), 12);
        Assert.Equal(Convert.ToDouble(full.Values["DisplayedWindowPeakRatio"]),
            Convert.ToDouble(dense.Values["DisplayedWindowPeakRatio"]), 12);
    }
}
