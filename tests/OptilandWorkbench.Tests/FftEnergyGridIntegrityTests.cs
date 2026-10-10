using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class FftEnergyGridIntegrityTests
{
    private static AnalysisPoint[] IsolatedBrightPixel() => Enumerable.Range(0, 5)
        .SelectMany(y => Enumerable.Range(0, 5).Select(x =>
            new AnalysisPoint((x - 2) * .25, (y - 2) * .25, Value: x == 2 && y == 2 ? 1 : 0))).ToArray();

    [Fact]
    public void BlackPixelsRetainTheOriginalFftWindowGeometry()
    {
        var points = IsolatedBrightPixel();
        var samples = DiffractionEncircledEnergyAnalysis.SelectFftWindowSamples(points, 5, 5);
        Assert.Equal(25, samples.Length);
        Assert.Equal(24, samples.Count(s => s.Weight == 0));
        Assert.Equal(points.Select(p => (p.X, p.Y)), samples.Select(s => (s.X, s.Y)));
    }

    [Theory]
    [InlineData("encircled", EnergyRegion.Circle, Math.PI / 4)]
    [InlineData("ensquared", EnergyRegion.Square, 1)]
    [InlineData("X", EnergyRegion.XSlit, 1)]
    [InlineData("Y", EnergyRegion.YSlit, 1)]
    public void SparsePlotMatchesOperandPixelAreasAndIndependentShapes(string type, EnergyRegion region, double expected)
    {
        var points = IsolatedBrightPixel();
        var samples = DiffractionEncircledEnergyAnalysis.SelectFftWindowSamples(points, 5, 5);
        var plot = new PsfPixelEnergyGrid(samples, (0, 0), type);
        var operand = new DiffractionEnergyDistribution(points.Select(p =>
            new WeightedEnergyPoint(p.X, p.Y, p.Value!.Value)).ToArray(), (0, 0), region);
        // The illuminated square has side 0.25 µm. A radius 0.125 µm circle
        // occupies pi/4 of it; either slit and the square cover it exactly.
        Assert.Equal(expected, plot.Fraction(.125), 12);
        Assert.Equal(operand.FractionAtDistance(.125), plot.Fraction(.125), 12);
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("missing")]
    public void InvalidIntensitiesAreRejectedRatherThanRemoved(string fault)
    {
        var points = IsolatedBrightPixel();
        points[0] = points[0] with { Value = fault switch
        {
            "negative" => -1, "nan" => double.NaN, "infinity" => double.PositiveInfinity, _ => null
        } };
        Assert.Throws<InvalidOperationException>(() =>
            DiffractionEncircledEnergyAnalysis.SelectFftWindowSamples(points, 5, 5));
    }

    [Fact]
    public void AllBlackWindowHasGeometryButNoEnergy()
    {
        var samples = DiffractionEncircledEnergyAnalysis.SelectFftWindowSamples(
            IsolatedBrightPixel().Select(p => p with { Value = 0 }).ToArray(), 5, 5);
        Assert.Equal(25, samples.Length);
        Assert.Equal(0, new PsfPixelEnergyGrid(samples, (0, 0), "encircled").Fraction(.125));
    }
}
