using System.Numerics;
using OpticalSystemDesign.Core.Analysis;

namespace OpticalSystemDesign.Tests;

public sealed class FourierWindowTests
{
    [Theory]
    [InlineData(5, 16, -5, -2, 9, 7)]
    [InlineData(8, 64, 13, -21, 7, 11)]
    [InlineData(7, 32768, -512, 100, 15, 19)]
    public void OffCenterFourierWindowMatchesIndependentTwoDimensionalDft(int size, int logical,
        int firstX, int firstY, int width, int height)
    {
        var pupil = Pupil(size); var offset = (logical - size) / 2;
        var actual = DiffractionEngine.FourierTransformWindow(pupil, logical, firstX, firstY, width, height);
        for (var row = 0; row < height; row++) for (var column = 0; column < width; column++)
        {
            var expected = Complex.Zero;
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                expected += pupil[y, x] * Complex.FromPolarCoordinates(1, -2 * Math.PI
                    * ((firstX + column) * (double)(offset + x) + (firstY + row) * (double)(offset + y)) / logical);
            Assert.InRange((actual[row, column] - expected).Magnitude, 0, 2e-10);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.FourierTransformWindow(pupil, logical, -logical / 2, 0, 2, 2));
    }

    [Fact]
    public void ParsevalPowerAndMarginalCentroidIncludePowerOutsideRequestedWindow()
    {
        const int size = 5; const int logical = 16;
        var pupil = Pupil(size); var offset = (logical - size) / 2;
        var total = 0d; var xFirst = 0d; var yFirst = 0d;
        for (var ky = -logical / 2 + 1; ky <= logical / 2; ky++)
            for (var kx = -logical / 2 + 1; kx <= logical / 2; kx++)
            {
                var amplitude = Complex.Zero;
                for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                    amplitude += pupil[y, x] * Complex.FromPolarCoordinates(1, -2 * Math.PI * (kx * (x + offset) + ky * (y + offset)) / logical);
                var power = amplitude.Magnitude * amplitude.Magnitude;
                total += power; xFirst += kx * power; yFirst += ky * power;
            }
        var actual = DiffractionEngine.FourierImageMoments(pupil, logical);
        Assert.InRange(Math.Abs(actual.Total - total) / total, 0, 1e-13);
        Assert.InRange(Math.Abs(actual.X - xFirst / total), 0, 1e-13);
        Assert.InRange(Math.Abs(actual.Y - yFirst / total), 0, 1e-13);
    }

    [Fact]
    public void LargeLogicalImageCentroidMatchesIndependentDirectSpectralMarginals()
    {
        const int size = 5; const int logical = 32768;
        var pupil = Pupil(size); var actual = DiffractionEngine.FourierImageMoments(pupil, logical);
        foreach (var vertical in new[] { false, true })
        {
            var total = 0d; var moment = 0d;
            for (var k = -logical / 2 + 1; k <= logical / 2; k++)
            {
                var power = 0d;
                for (var line = 0; line < size; line++)
                {
                    var amplitude = Complex.Zero;
                    for (var i = 0; i < size; i++)
                        amplitude += (vertical ? pupil[i, line] : pupil[line, i])
                            * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * i / logical);
                    power += amplitude.Magnitude * amplitude.Magnitude;
                }
                total += power; moment += k * power;
            }
            Assert.InRange(Math.Abs((vertical ? actual.Y : actual.X) - moment / total), 0, 1e-8);
        }
    }

    private static Complex[,] Pupil(int size)
    {
        var pupil = new Complex[size, size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            if ((x + 2 * y) % 5 != 0) pupil[y, x] = new Complex(Math.Sin(.7 * x + y), Math.Cos(x - .3 * y));
        return pupil;
    }
}
