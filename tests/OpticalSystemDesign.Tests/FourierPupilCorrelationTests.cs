using System.Numerics;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class FourierPupilCorrelationTests
{
    [Theory]
    [InlineData(5, 16)]
    [InlineData(8, 32)]
    public void DirectionalFftMatchesIndependentLinearComplexCorrelation(int size, int transformSize)
    {
        var pupil = new Complex[size, size];
        for (var row = 0; row < size; row++) for (var column = 0; column < size; column++)
            if ((row + 2 * column) % 4 != 0)
                pupil[row, column] = new Complex(Math.Cos(.3 * row + column), Math.Sin(row - .7 * column));
        var power = pupil.Cast<Complex>().Sum(v => v.Magnitude * v.Magnitude);
        var actual = DiffractionEngine.FourierPupilAutocorrelation(pupil, transformSize);
        for (var shift = 0; shift < transformSize / 2; shift++)
        {
            var tangential = Complex.Zero; var sagittal = Complex.Zero;
            for (var row = 0; row < size; row++) for (var column = 0; column < size; column++)
            {
                if (row + shift < size) tangential += pupil[row, column] * Complex.Conjugate(pupil[row + shift, column]);
                if (column + shift < size) sagittal += pupil[row, column] * Complex.Conjugate(pupil[row, column + shift]);
            }
            Assert.InRange((actual.Tangential[shift] - tangential / power).Magnitude, 0, 3e-12);
            Assert.InRange((actual.Sagittal[shift] - sagittal / power).Magnitude, 0, 3e-12);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.FourierPupilAutocorrelation(pupil, size));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullPupilMtfRetainsTheExistingComplexOtfAndPhysicalFrequencyAxes(bool native)
    {
        var optic = Optic.CreateCookeTriplet(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, .6), wave, 32, 64,
            cellCenteredPupil: native, zemaxFftSampling: native, imageDelta: native ? -1 : 0);
        if (native) psf = psf with { FrequencySampleCount = 31 };
        var expected = DiffractionEngine.ComputeFftMtf(psf, optic, wave);
        var actual = DiffractionEngine.ComputeFftMtfFromPupil(optic, (0, .6), wave, 32, 64,
            cellCenteredPupil: native, zemaxFftSampling: native);
        Assert.Equal(expected.TangentialFrequency, actual.TangentialFrequency);
        Assert.Equal(expected.SagittalFrequency, actual.SagittalFrequency);
        Assert.Equal(expected.CutoffFrequency, actual.CutoffFrequency);
        for (var i = 0; i < expected.Frequency.Count; i++)
        {
            Assert.InRange((actual.TangentialOtf![i] - expected.TangentialOtf![i]).Magnitude, 0, 3e-12);
            Assert.InRange((actual.SagittalOtf![i] - expected.SagittalOtf![i]).Magnitude, 0, 3e-12);
        }
    }
}
