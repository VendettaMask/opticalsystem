using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using System.Numerics;

namespace OpticalSystemDesign.Tests;

public sealed class BoundedCumulativeEnergyTests
{
    [Theory]
    [InlineData("encircled")]
    [InlineData("ensquared")]
    public void UniformKnotRangeUpdatesMatchIndependentDistanceBinSums(string type)
    {
        var image = new Complex[11, 11]; var total = 0d;
        for (var y = 0; y < 11; y++) for (var x = 0; x < 11; x++)
        {
            image[y, x] = new Complex(Math.Sin(x + .7 * y), Math.Cos(.4 * x - y));
            total += image[y, x].Magnitude * image[y, x].Magnitude;
        }
        total *= 2; var center = (X: .13, Y: -.21); const double maximum = 2.3; const double bin = .7;
        var grid = new FftCumulativeKnotGrid(image, -5, -5, 1, center, total, bin, maximum, 129, type, 32);
        for (var i = 0; i < 129; i++)
        {
            var radius = maximum * i / 128; var sum = 0d;
            for (var y = 0; y < 11; y++) for (var x = 0; x < 11; x++)
            {
                var dx = Math.Abs(x - 5 - center.X); var dy = Math.Abs(y - 5 - center.Y);
                var distance = type == "encircled" ? double.Hypot(dx, dy) : Math.Max(dx, dy);
                sum += image[y, x].Magnitude * image[y, x].Magnitude * Math.Clamp((radius - distance) / bin + .5, 0, 1);
            }
            Assert.InRange(Math.Abs(grid.Fraction(radius) - (i == 0 ? 0 : sum / total)), 0, 1e-13);
        }
        Assert.Equal(bin, grid.BinWidth);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.Fraction(2.4));
        Assert.Throws<InvalidOperationException>(() => grid.RadiusContaining(.99));
    }

    [Theory]
    [InlineData("encircled")]
    [InlineData("ensquared")]
    public void PrefixDistanceBinsAndBoundedReadoutRetainIndependentFullImageEnergy(string type)
    {
        var center = (X: .13, Y: -.21);
        var samples = Enumerable.Range(-10, 21).SelectMany(y => Enumerable.Range(-10, 21)
            .Select(x => new EnergySample(x, y, 1 + Math.Pow(Math.Sin(.3 * x + y), 2)))).ToArray();
        var full = new FftCumulativeEnergyGrid(samples, center, type);
        var window = samples.Where(s => Math.Abs(s.X) <= 4 && Math.Abs(s.Y) <= 4).ToArray();
        var bounded = new FftCumulativeEnergyGrid(window, center, type, samples.Sum(s => s.Weight), 2.3);
        for (var i = 0; i <= 100; i++)
        {
            var radius = 2.3 * i / 100;
            var expected = radius == 0 ? 0 : samples.Sum(s => s.Weight * Math.Clamp((radius - Distance(s)) + .5, 0, 1)) / samples.Sum(s => s.Weight);
            Assert.InRange(Math.Abs(full.Fraction(radius) - expected), 0, 1e-13);
            Assert.InRange(Math.Abs(bounded.Fraction(radius) - expected), 0, 1e-13);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => bounded.Fraction(2.4));
        Assert.Throws<InvalidOperationException>(() => bounded.RadiusContaining(.99));
        Assert.Throws<ArgumentException>(() => new FftCumulativeEnergyGrid(window, center, type, 1, 2.3));
        double Distance(EnergySample s) => type == "encircled" ? double.Hypot(s.X - center.X, s.Y - center.Y)
            : Math.Max(Math.Abs(s.X - center.X), Math.Abs(s.Y - center.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactPupilWindowMatchesExistingDenseImageWithFullCentroidAndNormalization(bool ignoreOpd)
    {
        var optic = Optic.CreateBlank();
        optic.Aperture.Value = .4;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 2 },
            new OpticalSurface { Thickness = 2, SemiDiameter = .2, IsStop = true },
            new OpticalSurface { SemiDiameter = 2 }
        ]);
        var wave = optic.Wavelengths[0]; const int n = 256; const int size = 2 * n; const double radius = 5;
        var result = DiffractionEngine.ComputeFftCumulativeEnergySpectrum(optic, (0, 0), new[] { wave }, n, ignoreOpd)[0].Result;
        var pitch = result.SampleSpacingMicrometers;
        var samples = Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size).Select(x =>
            new EnergySample((x - size / 2d + 1) * pitch, (y - size / 2d + 1) * pitch,
                result.Values[(y + 1) % size, (x + 1) % size]))).ToArray();
        var power = samples.Sum(s => s.Weight);
        var center = (samples.Sum(s => s.X * s.Weight) / power, samples.Sum(s => s.Y * s.Weight) / power);
        var full = new FftCumulativeEnergyGrid(samples, center, "encircled");
        var bounded = DiffractionEngine.ComputeBoundedCumulativeEnergy(optic, (0, 0), wave, n, "centroid", "encircled", radius, ignoreOpd);
        for (var i = 0; i <= 100; i++)
            Assert.InRange(Math.Abs(full.Fraction(radius * i / 100) - bounded.Fraction(radius * i / 100)), 0, 1e-10);
        var knots = DiffractionEngine.ComputeBoundedCumulativeEnergyKnots(optic, (0, 0), wave, n,
            "centroid", "encircled", radius, ignoreOpd);
        for (var i = 0; i < n; i++)
            Assert.InRange(Math.Abs(full.Fraction(radius * i / (n - 1d)) - knots.Fraction(radius * i / (n - 1d))), 0, 1e-10);
    }
}
