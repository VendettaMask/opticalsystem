using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class SlantedEdgeSfrTests
{
    [Theory]
    [InlineData(1, .2)] [InlineData(-1, -.31)] [InlineData(1, .45)]
    public void IndependentGaussianEdgeRecoversAngleMtfAndPhysicalFrequency(int polarity, double phase)
    {
        const double sigma = 1.2;
        var pixels = Edge(128, sigma, polarity, phase);
        var result = SlantedEdgeSfr.Measure(pixels, 128, 128, new(12, 8, 104, 112), 3);
        Assert.Equal(Math.Tan(5 * Math.PI / 180), result.EdgeSlope, 7);
        Assert.True(result.EdgeFitRmsPixels < 1e-5);
        foreach (var p in result.Curve)
        {
            var expected = Math.Exp(-2 * Math.PI * Math.PI * sigma * sigma * p.CyclesPerPixel * p.CyclesPerPixel);
            Assert.InRange(Math.Abs(p.Response - expected), 0, .006);
            Assert.Equal(p.CyclesPerPixel * 1000 / 3, p.CyclesPerMillimeter);
        }
        Assert.InRange(Math.Abs(result.Mtf50CyclesPerPixel!.Value - Math.Sqrt(2 * Math.Log(2)) / (2 * Math.PI * sigma)), 0, .001);
        Assert.Equal(1, result.Curve[0].Response, 12);
    }

    [Fact]
    public void FlatInvalidRoiAndNonlinearInvalidValuesFailExplicitly()
    {
        var pixels = Edge(96, 1.2, 1, .2);
        Assert.Throws<InvalidOperationException>(() => SlantedEdgeSfr.Measure(Enumerable.Repeat(.5, 96 * 96).ToArray(), 96, 96, new(0, 0, 96, 96), 3));
        Assert.Throws<ArgumentException>(() => SlantedEdgeSfr.Measure(pixels, 96, 96, new(80, 0, 32, 96), 3));
        pixels[0] = double.NaN;
        Assert.Throws<ArgumentException>(() => SlantedEdgeSfr.Measure(pixels, 96, 96, new(0, 0, 96, 96), 3));
    }

    [Fact]
    public void SquarePixelIntegrationAndDeltaPsfPreserveAnalyticEdge()
    {
        var settings = new SlantedEdgeSettings(PupilSampling: 32, ImageSamplesPerPixel: 4, PsfRadiusPixels: 4);
        var values = new double[33, 33]; values[16, 16] = 1 / (.75 * .75);
        var frame = MonochromeSlantedEdge.RenderPsf(new(values, 32, 33, 4, .75), settings);
        Assert.InRange(frame.LinearPixels.Zip(frame.Source, (a, b) => Math.Abs(a - b)).Max(), 0, 1e-10);
        Assert.All(frame.OpticalMtf, p => Assert.Equal(1, p.Response, 10));
        Assert.InRange(frame.PixelIntegratedMtf[^1].Response, .63, .65);
        Assert.Null(frame.Measurement.Mtf50CyclesPerPixel); // A full square pixel retains >0.5 at Nyquist.
        Assert.InRange(frame.MaximumSfrReferenceDifference, 0, .02);
    }

    [Fact]
    public void DirectionalFourierSectionMatchesIndependentAnisotropicGaussian()
    {
        const int n = 101; const double spacing = .5;
        var grid = new double[n, n];
        for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
            grid[y, x] = Math.Exp(-.5 * (Math.Pow((x - 50) * spacing / 2, 2) + Math.Pow((y - 50) * spacing / 4, 2)));
        var psf = new PsfResult(grid, 32, n, 4, spacing);
        var frequencies = new[] { 0d, 10, 30, 50, 100 };
        var normal = 1 / Math.Sqrt(2);
        var actual = DiffractionEngine.ComputePsfMtfAlongDirection(psf, frequencies, normal, normal);
        foreach (var (f, m) in frequencies.Zip(actual))
            Assert.Equal(Math.Exp(-2 * Math.PI * Math.PI * f * f * (4 + 16) / 2e6), m, 8);
        Assert.Throws<ArgumentException>(() => DiffractionEngine.ComputePsfMtfAlongDirection(psf, frequencies, 1, 1));
    }

    [Fact]
    public void ExistingLensConvergesWithoutChangingPrescriptionAndInsufficientChecksCannotPass()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(ReferenceLens), ".zmx");
        var before = optic.ToSnapshot();
        var run = MonochromeSlantedEdge.Run(optic, new());
        Assert.True(run.SamplingConverged);
        Assert.Equal(3, run.Sampling.Count); Assert.All(run.Sampling, c => Assert.True(c.WithinCriteria));
        Assert.InRange(run.Baseline.MaximumSfrReferenceDifference, 0, .006);
        Assert.InRange(run.Baseline.Measurement.Mtf50CyclesPerPixel!.Value, .130, .133);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot()));
        var incomplete = MonochromeSlantedEdge.Run(optic, new(ImageSamplesPerPixel: 16));
        Assert.False(incomplete.SamplingConverged);
        Assert.Contains(incomplete.Sampling, c => c.Axis == "ImagePlane" && c.Refined is null && !c.WithinCriteria);
    }

    [Fact]
    public void LocalCellIntegralAgreesWithSharedFftTimesAnalyticCellFootprint()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(ReferenceLens), ".zmx");
        var wave = new OpticalSystemDesign.Core.Domain.Wavelength { Micrometers = .55 };
        var fft = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, 128, 256, cellCenteredPupil: true);
        // Cell-centered nodes have spacing 2/N. Use that exact Fourier lattice;
        // the existing general FFT display metadata retains its historical (N-1) scale.
        var local = DiffractionEngine.ComputeLocalIncoherentPsf(optic, (0, 0), wave, 128, 129, wave.Micrometers * fft.WorkingFNumber / 2);
        var peak1 = fft.Values.Cast<double>().Max(); var peak2 = local.Values.Cast<double>().Max();
        for (var y = 0; y < 129; y++) for (var x = 0; x < 129; x++)
        {
            double Sinc(double v) => v == 0 ? 1 : Math.Sin(Math.PI * v) / (Math.PI * v);
            var footprint = Sinc((x - 64) / 256d) * Sinc((y - 64) / 256d);
            Assert.InRange(Math.Abs(fft.Values[y + 64, x + 64] / peak1 * footprint * footprint - local.Values[y, x] / peak2), 0, 1e-8);
        }
    }

    internal static string ReferenceLens => Path.Combine(RepositoryRoot,
        "artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX");

    internal static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpticalSystemDesign.slnx"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }

    private static double[] Edge(int n, double sigma, int polarity, double phase) =>
        Enumerable.Range(0, n * n).Select(i =>
        {
            var a = 5 * Math.PI / 180;
            var d = ((i % n + .5 - n / 2d - phase) * Math.Cos(a) - (i / n + .5 - n / 2d) * Math.Sin(a)) / sigma;
            return .5 + .3 * polarity * Erf(d / Math.Sqrt(2));
        }).ToArray();
    private static double Erf(double x)
    {
        var t = 1 / (1 + .3275911 * Math.Abs(x));
        return Math.Sign(x) * (1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - .284496736) * t + .254829592) * t * Math.Exp(-x * x));
    }
}
