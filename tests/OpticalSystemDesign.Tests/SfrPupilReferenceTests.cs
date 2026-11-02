using System.Numerics;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class SfrPupilReferenceTests
{
    [Theory]
    [InlineData(.023, -.017)] [InlineData(-.031, .011)]
    public void PhysicalChirpWindowMatchesIndependentComplexSum(double dx, double dy)
    {
        const int n = 7, m = 17;
        var pupil = new Complex[n, n]; var random = new Random(754);
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) pupil[y, x] = new(random.NextDouble(), random.NextDouble() - .5);
        var actual = DiffractionEngine.PhysicalFourierWindow(pupil, m, dx, dy);
        for (var y = 0; y < m; y++) for (var x = 0; x < m; x++)
        {
            Complex expected = 0;
            for (var j = 0; j < n; j++) for (var k = 0; k < n; k++)
                expected += pupil[j, k] * Complex.FromPolarCoordinates(1, -2 * Math.PI * ((k - 3) * (x - 8) * dx + (j - 3) * (y - 8) * dy));
            Assert.InRange((expected - actual[y, x]).Magnitude, 0, 1e-10);
        }
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(0, -1)] [InlineData(.6, -.8)]
    public void FullSquarePupilHasIndependentTriangularOverlap(double nx, double ny)
    {
        const int n = 8;
        var pupil = new Complex[n, n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) pupil[y, x] = Complex.FromPolarCoordinates(1, .73);
        var f = new[] { 0d, .1, .5, 1, 2, 8 };
        var mtf = DiffractionEngine.ComputeCellPupilMtf(pupil, f, nx, ny, 8, 6);
        for (var i = 0; i < f.Length; i++)
            Assert.Equal(Math.Max(0, 1 - Math.Abs(f[i] * nx)) * Math.Max(0, 1 - Math.Abs(f[i] * ny * .75)), mtf[i], 12);
    }

    [Fact]
    public void FftPupilCorrelationMatchesIndependentSparseComplexCellOverlap()
    {
        const int n = 256;
        var pupil = new Complex[n, n];
        var cells = new[] { (X: 110, Y: 121, Z: new Complex(1, 2)), (X: 111, Y: 121, Z: new Complex(.4, -.7)),
            (X: 95, Y: 130, Z: new Complex(-.2, .8)), (X: 140, Y: 110, Z: new Complex(.5, .3)) };
        foreach (var c in cells) pupil[c.Y, c.X] = c.Z;
        var frequencies = Enumerable.Range(0, 500).Select(i => i / 250d).ToArray();
        var actual = DiffractionEngine.ComputeCellPupilMtf(pupil, frequencies, .6, -.8, 128, 128);
        var power = cells.Sum(c => c.Z.Magnitude * c.Z.Magnitude);
        for (var i = 0; i < frequencies.Length; i++)
        {
            var sx = frequencies[i] * .6 * 128; var sy = frequencies[i] * -.8 * 128; Complex sum = 0;
            foreach (var a in cells) foreach (var b in cells)
                sum += a.Z * Complex.Conjugate(b.Z) * Math.Max(0, 1 - Math.Abs(sx - (a.X - b.X))) * Math.Max(0, 1 - Math.Abs(sy - (a.Y - b.Y)));
            Assert.InRange(Math.Abs(sum.Magnitude / power - actual[i]), 0, 1e-11);
        }
    }

    [Fact]
    public void LargeWindowMtfChirpMatchesIndependentPointPhasors()
    {
        const int n = 1025;
        var pixels = new[] { (X: 500, Y: 520, Power: 2d), (X: 600, Y: 450, Power: .7), (X: 510, Y: 512, Power: 1d) };
        var grid = new double[n, n]; foreach (var p in pixels) grid[p.Y, p.X] = p.Power;
        var frequencies = Enumerable.Range(0, 129).Select(i => i * 1.5).ToArray();
        var actual = DiffractionEngine.ComputePsfMtfAlongDirection(new(grid, 256, n, 4, .375), frequencies, .6, -.8);
        for (var i = 0; i < frequencies.Length; i++)
        {
            var expected = pixels.Aggregate(Complex.Zero, (s, p) => s + p.Power * Complex.FromPolarCoordinates(1,
                -2 * Math.PI * frequencies[i] * .000375 * ((p.X - 512) * .6 - (p.Y - 512) * .8)));
            Assert.InRange(Math.Abs(actual[i] - expected.Magnitude / 3.7), 0, 1e-10);
        }
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputePsfMtfAlongDirection(new(grid, 256, n, 4, .375),
            frequencies.Select((f, i) => i == 3 ? f + .1 : f).ToArray(), .6, -.8));
    }

    [Fact]
    public void FullPupilReferenceRejectsInvalidDataAndPreservesBounds()
    {
        var pupil = new Complex[4, 4]; pupil[0, 0] = 1;
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputeCellPupilMtf(pupil, [0], 1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputeCellPupilMtf(pupil, [double.NaN], 1, 0, 1, 1));
        Assert.Throws<InvalidOperationException>(() => DiffractionEngine.ComputeCellPupilMtf(new Complex[4, 4], [0], 1, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.PhysicalFourierWindow(pupil, 2048, .1, .1));
        Assert.Throws<ArgumentException>(() => DiffractionEngine.PhysicalFourierWindow(new Complex[512, 512], 1025, .1, .1));
    }

    [Theory]
    [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)] [InlineData(false, 4)] [InlineData(false, 5)]
    public void ActualLensFieldsAgreeWithIndependentFullPupilAndConverge(bool gauss, int field)
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(gauss ? DoubleGaussSfrTests.StandardLens : SlantedEdgeSfrTests.ReferenceLens), ".zmx");
        var run = MonochromeSlantedEdge.Run(optic, new(FieldNumber: field));
        Assert.True(run.SamplingConverged, string.Join("; ", run.Sampling.Select(c => $"{c.Axis} {c.WithinCriteria} {c.Message}")));
        Assert.InRange(run.Baseline.MaximumSfrFullPupilDifference!.Value, 0, .01);
        Assert.InRange(run.Baseline.MaximumWindowFullPupilDifference!.Value, 0, .01);
        Assert.All(run.Sampling, c => Assert.InRange(c.MaximumFullPupilMtfDifference!.Value, 0, .01));
        var dir = Environment.GetEnvironmentVariable("OPTICAL_SFR_DIRECTIONAL_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{(gauss ? "double-gauss" : "123456")}-field-{field}.json"),
                System.Text.Json.JsonSerializer.Serialize(new { run.Baseline.Settings, run.SamplingConverged,
                    run.Baseline.Measurement.Mtf50CyclesPerPixel, run.Baseline.MaximumSfrReferenceDifference,
                    run.Baseline.MaximumSfrFullPupilDifference, run.Baseline.MaximumWindowFullPupilDifference, run.Baseline.RelativePsfReadoutIntegral,
                    Curve = run.Baseline.Measurement.Curve.Select((p, i) => new { p.CyclesPerPixel, p.CyclesPerMillimeter, Sfr = p.Response,
                        WindowMtf = run.Baseline.OpticalMtf[i].Response, WindowPixelMtf = run.Baseline.PixelIntegratedMtf[i].Response,
                        FullMtf = run.Baseline.FullPupilMtf![i].Response, FullPixelMtf = run.Baseline.FullPupilPixelIntegratedMtf![i].Response }),
                    Sampling = run.Sampling.Select(c => new { c.Axis, c.WithinCriteria, c.MaximumImageDifference, c.MaximumSfrDifference,
                        c.MaximumMtfDifference, c.MaximumFullPupilMtfDifference, c.RelativeReadoutIntegralDifference, c.Message }) },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    [Fact]
    public void FinitePsfSelfConsistencyDoesNotHideMissingFullPupilTails()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(DoubleGaussSfrTests.StandardLens), ".zmx");
        var small = MonochromeSlantedEdge.Render(optic, new(PupilSampling: 128, PsfRadiusPixels: 16));
        Assert.InRange(small.MaximumSfrReferenceDifference, 0, .005);
        Assert.True(small.MaximumSfrFullPupilDifference > .01);
        Assert.True(small.MaximumWindowFullPupilDifference > .01);
    }
}
