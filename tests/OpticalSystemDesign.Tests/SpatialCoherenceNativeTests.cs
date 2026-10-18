using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class SpatialCoherenceNativeTests
{
    [Theory]
    [InlineData("two-g0-a0.001-t6")]
    [InlineData("two-g0-a0.002-t6")]
    [InlineData("two-g0-a0.004-t6")]
    [InlineData("two-g1-a0.001-t6")]
    [InlineData("two-g1-a0.002-t6")]
    [InlineData("two-g1-a0.004-t6")]
    [InlineData("os2-g0-a0.001")]
    [InlineData("os2-g0-a0.002")]
    [InlineData("os2-g0-a0.004")]
    [InlineData("os2-g1-a0.001")]
    [InlineData("os2-g1-a0.002")]
    [InlineData("os2-g1-a0.004")]
    [InlineData("negative-g0")]
    [InlineData("negative-g1")]
    [InlineData("wave1-g0")]
    [InlineData("wave1-g1")]
    [InlineData("wave3-g0")]
    [InlineData("wave3-g1")]
    public void OriginalLensMatchesNativeMostlyCoherentAtCapturedSettings(string name)
    {
        using var fixture = Fixture();
        var capture = fixture.RootElement.GetProperty("Cases").EnumerateArray().Single(value => value.GetProperty("Name").GetString() == name);
        var oversampling = capture.GetProperty("Oversampling").GetInt32();
        var function = Enum.Parse<SpatialCoherenceFunction>(capture.GetProperty("Function").GetString()!);
        var negative = capture.GetProperty("SourceKind").GetString() == "Negative";
        var source = new double[1, 16, 16];
        source[0, 8, negative ? 6 : 7] = 1;
        source[0, 8, negative ? 9 : 8] = 1d / 3;
        var actual = ImageSimulationEngine.Simulate(Primary(), new RgbImage(source), new ImageSimulationConfig
        {
            AberrationMode = "Partially Coherent",
            CoherenceFunction = function,
            CoherenceAlphaMillimeters = capture.GetProperty("Alpha").GetDouble(),
            FileSizeMillimeters = .008,
            NormalizeImagePeak = true,
            UseRelativeIllumination = false,
            WavelengthsMicrometers = [capture.GetProperty("Wavelength").GetDouble()],
            NumRays = 256,
            PsfSize = oversampling == 1 ? 65 : 129,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            Oversampling = oversampling,
            Padding = 8 * oversampling
        });
        var values = capture.GetProperty("Values");
        var size = 32 * oversampling;
        double error = 0;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            error = Math.Max(error, Math.Abs(actual.Simulated[0, y, x] - values[size - 1 - y][x].GetDouble()));
        Assert.True(error < 1e-9, $"{name}: max absolute normalized intensity difference {error:R}");
        Assert.Equal(.0005 / oversampling, actual.CoherentImaging!.DetectorPixelPitchMillimeters);
        Assert.Equal(function, actual.CoherentImaging.Function);
        Assert.Equal(1, actual.MaximumValue);
    }

    [Fact]
    public void FourierConjugateComplexKernelMatchesNativePsfWithoutFittedGainOrShift()
    {
        using var fixture = Fixture();
        var optic = Primary();
        var kernel = DiffractionEngine.ComputeCoherentImageKernel(optic, (0, 0), optic.Wavelengths[1], 256, 65, .0005, true);
        var peak = kernel.Values.Cast<Complex>().Max(value => value.Magnitude * value.Magnitude);
        var native = fixture.RootElement.GetProperty("Psf");
        double error = 0;
        for (var y = 0; y < 65; y++) for (var x = 0; x < 65; x++)
            error = Math.Max(error, Math.Abs(kernel.Values[y, x].Magnitude * kernel.Values[y, x].Magnitude / peak - native[64 - y][x].GetDouble()));
        Assert.InRange(error, 0, 1e-9);
        Assert.Equal(256, kernel.FourierGridSize);
    }

    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian, .002, 0, .004, .778800783071405)]
    [InlineData(SpatialCoherenceFunction.Gaussian, .004, 0, .004, .367879441171442)]
    [InlineData(SpatialCoherenceFunction.Sinc, .002, 0, .004, .636619772367581)]
    [InlineData(SpatialCoherenceFunction.Sinc, .006, 0, .004, -.212206590789194)]
    public void CorrelationMatchesCapturedNativeGammaIncludingNegativeSinc(SpatialCoherenceFunction function, double x, double y, double alpha, double expected)
        => Assert.Equal(expected, MutualIntensityEngine.EvaluateSpatialCoherence(function, x, y, alpha), 14);

    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian)]
    [InlineData(SpatialCoherenceFunction.Sinc)]
    public void SpatialPropagationMatchesIndependentComplexDoubleSum(SpatialCoherenceFunction function)
    {
        var source = new double[,] { { 1, .3, 0 }, { .7, .2, .1 } };
        var phase = new double[,] { { .1, -.7, 0 }, { .3, 1.2, -.2 } };
        var h = new Complex[,] { { new(.1, .2), new(.4, -.1) }, { new(-.2, .1), new(.3, .2) } };
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(source, h, function, .001, .0007, phase);
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
        {
            Complex expected = 0;
            for (var iy = 0; iy < 2; iy++) for (var ix = 0; ix < 3; ix++)
                for (var jy = 0; jy < 2; jy++) for (var jx = 0; jx < 3; jx++)
                {
                    var dx = (ix - jx) * .7; var dy = (iy - jy) * .7;
                    var g = function == SpatialCoherenceFunction.Gaussian ? Math.Exp(-dx * dx - dy * dy) : Sinc(dx) * Sinc(dy);
                    var a = Complex.FromPolarCoordinates(Math.Sqrt(source[iy, ix]), phase[iy, ix]);
                    var b = Complex.FromPolarCoordinates(Math.Sqrt(source[jy, jx]), phase[jy, jx]);
                    expected += a * Complex.Conjugate(b) * g * At(y - iy, x - ix) * Complex.Conjugate(At(y - jy, x - jx));
                }
            Assert.Equal(expected.Real, actual[y, x], 13);
            Assert.InRange(Math.Abs(expected.Imaginary), 0, 1e-14);
        }
        Complex At(int y, int x) => y is >= 0 and < 2 && x is >= 0 and < 2 ? h[y, x] : Complex.Zero;
        static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAlphaCannotSilentlySelectAnotherPhysicalModel(double alpha)
        => Assert.Throws<ArgumentOutOfRangeException>(() => MutualIntensityEngine.EvaluateSpatialCoherence(SpatialCoherenceFunction.Gaussian, 0, 0, alpha));

    [Fact]
    public void SourcePairBudgetRejectsDenseOversizedWorkBeforeCovarianceAllocation()
    {
        var source = new double[100, 100];
        for (var y = 0; y < 100; y++) for (var x = 0; x < 100; x++) source[y, x] = 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => MutualIntensityEngine.PropagateSpatialCoherence(source,
            new Complex[,] { { 1 } }, SpatialCoherenceFunction.Gaussian, .001, .001));
    }

    [Fact]
    public void WorkbenchForwardsPhysicalCoherenceScaleImageWidthAndSelectedWavelength()
    {
        var runtime = new WorkbenchRuntime(Primary());
        var data = runtime.BuildAnalysisData("Partially Coherent Image Analysis", new Dictionary<string, string>
        {
            ["ImageSize"] = "16",
            ["PupilSampling"] = "128 x 128",
            ["PsfSize"] = "32",
            ["CoherenceFunction"] = "Gaussian",
            ["Alpha"] = "0.001",
            ["FileSize"] = "0.008",
            ["NormalizeImagePeak"] = "true",
            ["WavelengthNumber"] = "2",
            ["RelativeIllumination"] = "false",
            ["GuardBand"] = "8"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("Gaussian", data.Values["CoherenceFunction"]);
        Assert.Equal(.001, data.Values["CoherenceAlphaMillimeters"]);
        Assert.Equal(.008, data.Values["FileSizeMillimeters"]);
        Assert.Equal(.0005, data.Values["DetectorPixelPitchMillimeters"]);
        Assert.Equal(new[] { .44 }, Assert.IsType<double[]>(data.Values["WavelengthsMicrometers"]));
        Assert.DoesNotContain("Coherence", data.Values.Keys);
    }

    [Fact]
    public void TwoCompetingPhysicalScaleInputsAreRejectedBeforeOpticalWork()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.Simulate(Primary(),
            new RgbImage(new double[1, 16, 16]), new ImageSimulationConfig
            {
                AberrationMode = "Partially Coherent",
                PsfGridRows = 1,
                PsfGridColumns = 1,
                FileSizeMillimeters = .008,
                PixelSizeMillimeters = .0005
            }));

    private static OpticalSystemDesign.Core.Optic Primary() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "partial-coherence-zemax-2026-r1.json")));
}
