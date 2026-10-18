using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FiniteSpatialCoherenceNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using var fixture = Fixture();
        return fixture.RootElement.GetProperty("Cases").EnumerateArray()
            .Select(value => new object[] { value.GetProperty("Name").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void OriginalLensMatchesCompleteNativeFiniteImage(string name)
    {
        using var fixture = Fixture();
        var capture = fixture.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == name);
        var rows = capture.GetProperty("Source");
        var source = new double[1, rows.GetArrayLength(), rows[0].GetArrayLength()];
        for (var y = 0; y < source.GetLength(1); y++) for (var x = 0; x < source.GetLength(2); x++)
            source[0, y, x] = rows[y][x].GetDouble();
        var method = Enum.Parse<SpatialCoherenceMethod>(capture.GetProperty("Method").GetString()!);
        var actual = ImageSimulationEngine.Simulate(Primary(), new RgbImage(source), new ImageSimulationConfig
        {
            AberrationMode = "Partially Coherent",
            CoherenceFunction = Enum.Parse<SpatialCoherenceFunction>(capture.GetProperty("Function").GetString()!),
            CoherenceAlphaMillimeters = capture.GetProperty("Alpha").GetDouble(),
            CoherenceMethod = method,
            CoherenceFraction = capture.GetProperty("Fraction").GetDouble(),
            CoherenceSampling = capture.GetProperty("CoherenceSampling").GetInt32(),
            FileSizeMillimeters = capture.GetProperty("FileSize").GetDouble(),
            NormalizeImagePeak = true,
            UseRelativeIllumination = false,
            WavelengthsMicrometers = [.44],
            NumRays = capture.GetProperty("Sampling").GetInt32(),
            PsfSize = 65,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            Padding = capture.GetProperty("Padding").GetInt32()
        });
        var native = capture.GetProperty("Values");
        double error = 0;
        for (var y = 0; y < actual.Simulated.Height; y++) for (var x = 0; x < actual.Simulated.Width; x++)
            error = Math.Max(error, Math.Abs(actual.Simulated[0, y, x] - native[actual.Simulated.Height - 1 - y][x].GetDouble()));
        Assert.True(error < 1e-9, $"{name}: maximum absolute normalized intensity difference {error:R}");
        Assert.Equal(method, actual.CoherentImaging!.Method);
        Assert.Equal(1, actual.MaximumValue);
    }

    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian)]
    [InlineData(SpatialCoherenceFunction.Sinc)]
    public void FiniteOrderedBoundaryMagnitudeIsExplicitAndDoesNotReplaceTheExactCovariance(SpatialCoherenceFunction function)
    {
        var source = new double[,] { { 1, 0, 0, 0, 0, 1 } };
        var phase = new double[,] { { 0, 0, 0, 0, 0, Math.PI / 2 } };
        var kernel = new Complex[1, 11];
        for (var x = 0; x < 11; x++) kernel[0, x] = Complex.One;
        var exact = MutualIntensityEngine.PropagateSpatialCoherence(source, kernel, function, .001, .0005, phase);
        var finite = MutualIntensityEngine.PropagateSpatialCoherence(source, kernel, function, .001, .0005, phase,
            SpatialCoherenceMethod.FiniteCorrelationWindow);
        var gamma = MutualIntensityEngine.EvaluateSpatialCoherence(function, .0025, 0, .001);
        Assert.Equal(2, exact[0, 0], 13);
        Assert.Equal(Math.Sqrt(4 + gamma * gamma), finite[0, 0], 13);
        Assert.Equal(SpatialCoherenceMethod.ExactSourcePairs, new ImageSimulationConfig().CoherenceMethod);
    }

    [Theory]
    [InlineData(SpatialCoherenceFunction.Gaussian)]
    [InlineData(SpatialCoherenceFunction.Sinc)]
    public void FourierCovarianceAgreesWithIndependentIncoherentSumOfCoherentModes(SpatialCoherenceFunction function)
    {
        const int size = 16;
        const double alpha = .001, pitch = .0005, fraction = .98;
        var spectrum = new Complex[size, size];
        for (var ky = 0; ky < size; ky++) for (var kx = 0; kx < size; kx++)
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var dx = (x < size / 2 ? x : x - size) * pitch / alpha;
                var dy = (y < size / 2 ? y : y - size) * pitch / alpha;
                var gamma = function == SpatialCoherenceFunction.Gaussian ? Math.Exp(-dx * dx - dy * dy)
                    : Sinc(dx) * Sinc(dy);
                spectrum[ky, kx] += gamma * Complex.FromPolarCoordinates(1, -2 * Math.PI * (kx * x + ky * y) / size);
            }
        var total = spectrum.Cast<Complex>().Sum(value => value.Magnitude);
        var peak = spectrum.Cast<Complex>().Max(value => value.Real);
        var radius = 0;
        for (; radius < size / 2; radius++)
        {
            double captured = 0;
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                if (Math.Abs(x < size / 2 ? x : x - size) <= radius && Math.Abs(y < size / 2 ? y : y - size) <= radius)
                    captured += spectrum[y, x].Magnitude;
            if (captured >= fraction * total) break;
        }
        var points = new[] { (Y: 0, X: 0, I: 1d, Phase: .2), (Y: 0, X: 2, I: .3, Phase: .7), (Y: 1, X: 1, I: .7, Phase: -.4) };
        var source = new double[2, 3];
        var phase = new double[2, 3];
        foreach (var p in points) { source[p.Y, p.X] = p.I; phase[p.Y, p.X] = p.Phase; }
        var kernel = new Complex[,] { { new(.2, .1), new(.7, -.3), new(.1, .4) }, { new(.3, -.1), new(1, .2), new(.4, .3) }, { new(.1, .2), new(.6, .1), new(.2, -.2) } };
        var actual = MutualIntensityEngine.PropagateSpatialCoherence(source, kernel, function, alpha, pitch, phase,
            SpatialCoherenceMethod.FourierModes, fraction, size);
        var expected = new double[2, 3];
        double weights = 0;
        for (var ky = 0; ky < size; ky++) for (var kx = 0; kx < size; kx++)
        {
            if (Math.Abs(kx < size / 2 ? kx : kx - size) > radius || Math.Abs(ky < size / 2 ? ky : ky - size) > radius
                || spectrum[ky, kx].Real < .001 * peak) continue;
            var weight = spectrum[ky, kx].Real;
            weights += weight;
            for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
            {
                Complex field = 0;
                foreach (var p in points)
                {
                    var hy = y - p.Y + 1;
                    var hx = x - p.X + 1;
                    if (hy < 0 || hy >= 3 || hx < 0 || hx >= 3) continue;
                    field += Math.Sqrt(p.I) * Complex.FromPolarCoordinates(1, p.Phase + 2 * Math.PI * (kx * p.X + ky * p.Y) / size) * kernel[hy, hx];
                }
                expected[y, x] += weight * field.Magnitude * field.Magnitude;
            }
        }
        for (var y = 0; y < 2; y++) for (var x = 0; x < 3; x++)
            Assert.Equal(expected[y, x] / weights, actual[y, x], 12);
    }

    [Theory]
    [InlineData("FiniteCorrelationWindow")]
    [InlineData("FourierModes")]
    public void WorkbenchForwardsFiniteMethodAndReportsItsActualTruncation(string method)
    {
        var runtime = new WorkbenchRuntime(Primary());
        var data = runtime.BuildAnalysisData("Partially Coherent Image Analysis", new Dictionary<string, string>
        {
            ["ImageSize"] = "16",
            ["PupilSampling"] = "128 x 128",
            ["PsfSize"] = "65",
            ["CoherenceFunction"] = "Gaussian",
            ["Alpha"] = "0.001",
            ["FileSize"] = "0.008",
            ["CoherenceMethod"] = method,
            ["CoherenceFraction"] = "0.96",
            ["CoherenceSampling"] = "32 x 32",
            ["NormalizeImagePeak"] = "true",
            ["WavelengthNumber"] = "2",
            ["RelativeIllumination"] = "false",
            ["GuardBand"] = "8"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(method, data.Values["CoherenceMethod"]);
        Assert.Equal(.96, data.Values["CoherenceFraction"]);
        Assert.Equal(32, data.Values["CoherenceSampling"]);
        Assert.Contains(method == "FourierModes" ? "periodic Gamma" : "compatibility quadrature", (string)data.Values["CoherenceTruncation"]);
    }

    [Theory]
    [InlineData(double.NaN, 64)]
    [InlineData(1, 64)]
    [InlineData(0, 64)]
    [InlineData(.98, 63)]
    [InlineData(.98, 1024)]
    public void InvalidFiniteSamplingIsRejectedBeforeTracing(double fraction, int sampling)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.Simulate(Primary(),
            new RgbImage(new double[1, 2, 2]), new ImageSimulationConfig
            {
                AberrationMode = "Partially Coherent",
                CoherenceFunction = SpatialCoherenceFunction.Gaussian,
                CoherenceMethod = SpatialCoherenceMethod.FourierModes,
                CoherenceFraction = fraction,
                CoherenceSampling = sampling
            }));

    private static double Sinc(double value) => value == 0 ? 1 : Math.Sin(Math.PI * value) / (Math.PI * value);
    private static OpticalSystemDesign.Core.Optic Primary() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "partial-coherence-finite-zemax-2026-r1.json")));
}
