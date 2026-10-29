using System.Numerics;
using System.Reflection;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class FftMtfSpectrumInterpolationTests
{
    private static MtfResult Spectrum((Wavelength Wavelength, MtfResult Result)[] input,
        FftMtfDataType type, int count = 5) => (MtfResult)typeof(MtfAnalysis)
        .GetMethod("ResampleSpectrum", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, [input, 2d, type, count])!;

    private static MtfResult Resample(MtfResult source, FftMtfDataType type, int count = 5)
        => (MtfResult)typeof(MtfAnalysis).GetMethod("Resample", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, 2d, type, count])!;

    private static MtfResult Source(double step, Complex middle) => new(
        new[] { 0d, step, 2 * step }, new[] { 1d, middle.Magnitude, 0 },
        new[] { 1d, middle.Magnitude, 0 }, 2 * step,
        new[] { Complex.One, middle, Complex.Zero },
        new[] { Complex.One, Complex.Conjugate(middle), Complex.Zero });

    [Theory]
    [InlineData(FftMtfDataType.Modulation)]
    [InlineData(FftMtfDataType.Real)]
    [InlineData(FftMtfDataType.Imaginary)]
    [InlineData(FftMtfDataType.Phase)]
    public void DistinctWavelengthAxesMatchIndependentThreeKnotNaturalSpline(FftMtfDataType type)
    {
        var first = Source(1, new(.2, .3));
        var second = Source(.75, new(.5, -.2));
        var actual = Spectrum([(new Wavelength { Nanometers = 500, Weight = 1 }, first),
            (new Wavelength { Nanometers = 650, Weight = 3 }, second)], type);
        for (var i = 0; i < actual.Frequency.Count; i++)
        {
            var f = i * .5;
            Assert.Equal(f, actual.Frequency[i]);
            var expected = (ThreeKnots(1, new(.2, .3), f) + 3 * ThreeKnots(.75, new(.5, -.2), f)) / 4;
            Assert.InRange(Math.Abs(actual.Tangential[i] - Response(expected, type)), 0, 2e-14);
            Assert.InRange(Math.Abs(actual.Sagittal[i] - Response(Complex.Conjugate(expected), type)), 0, 2e-14);
        }
    }

    [Theory]
    [InlineData(FftMtfDataType.Modulation)]
    [InlineData(FftMtfDataType.Real)]
    [InlineData(FftMtfDataType.Imaginary)]
    [InlineData(FftMtfDataType.Phase)]
    [InlineData(FftMtfDataType.SquareWave)]
    public void SingleWavelengthRetainsItsEstablishedResponse(FftMtfDataType type)
    {
        var source = Source(1, new(.2, .3));
        var expected = Resample(source, type, 101);
        var actual = Spectrum([(new Wavelength { Weight = 1 }, source)], type, 101);
        Assert.Equal(expected.Frequency, actual.Frequency);
        foreach (var (a, b) in expected.Tangential.Zip(actual.Tangential)) Assert.InRange(Math.Abs(a - b), 0, 2e-14);
        foreach (var (a, b) in expected.Sagittal.Zip(actual.Sagittal)) Assert.InRange(Math.Abs(a - b), 0, 2e-14);
    }

    [Fact]
    public void ZeroWeightAndUnspecifiedWeightsRetainTheirSelectionRules()
    {
        var first = Source(1, new(.2, .3));
        var second = Source(.75, new(.5, -.2));
        var only = Spectrum([(new Wavelength { Weight = 1 }, first)], FftMtfDataType.Modulation);
        var zero = Spectrum([(new Wavelength { Weight = 1 }, first),
            (new Wavelength { Weight = 0 }, second)], FftMtfDataType.Modulation);
        Assert.Equal(only.Tangential, zero.Tangential);
        var equal = Spectrum([(new Wavelength { Weight = 1 }, first),
            (new Wavelength { Weight = 1 }, second)], FftMtfDataType.Modulation);
        var unspecified = Spectrum([(new Wavelength { Weight = 0 }, first),
            (new Wavelength { Weight = 0 }, second)], FftMtfDataType.Modulation);
        Assert.Equal(equal.Tangential, unspecified.Tangential);
    }

    // Independent closed form: M0=M2=0, M1=3(y0-2y1+y2)/(2h²).
    private static Complex ThreeKnots(double h, Complex middle, double f)
    {
        if (f > 2 * h) return Complex.Zero;
        var m = 1.5 * (Complex.One - 2 * middle) / (h * h);
        var upper = f <= h ? 1 : 2;
        var a = (upper * h - f) / h; var b = 1 - a;
        var lo = upper == 1 ? Complex.One : middle;
        var hi = upper == 1 ? middle : Complex.Zero;
        return a * lo + b * hi + h * h / 6
            * ((a * a * a - a) * (upper == 1 ? Complex.Zero : m)
                + (b * b * b - b) * (upper == 1 ? m : Complex.Zero));
    }

    private static double Response(Complex value, FftMtfDataType type) => type switch
    {
        FftMtfDataType.Real => value.Real,
        FftMtfDataType.Imaginary => value.Imaginary,
        FftMtfDataType.Phase => value.Phase,
        _ => Math.Clamp(value.Magnitude, 0, 1)
    };
}
