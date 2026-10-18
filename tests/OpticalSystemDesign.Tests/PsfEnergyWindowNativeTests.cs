using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class PsfEnergyWindowNativeTests
{
    public static IEnumerable<object[]> SamplingBoundaries()
    {
        using var manifest = ReadBoundaryEntry("manifest.json");
        return manifest.RootElement.GetProperty("Cases").EnumerateArray()
            .Select(value => new object[] { value.GetProperty("Name").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(SamplingBoundaries))]
    public void IntensitySamplingAvailabilityMatchesNativeIncludingDiscreteBandEdges(string name)
    {
        using var manifest = ReadBoundaryEntry("manifest.json");
        var capture = manifest.RootElement.GetProperty("Cases").EnumerateArray().Single(value => value.GetProperty("Name").GetString() == name);
        var size = capture.GetProperty("SourceSize").GetInt32();
        var source = new double[1, size, size];
        foreach (var p in capture.GetProperty("Points").EnumerateArray()) source[0, p.GetProperty("Y").GetInt32(), p.GetProperty("X").GetInt32()] = p.GetProperty("Intensity").GetDouble();
        var wave = capture.GetProperty("Wave").GetInt32();
        var fraction = capture.GetProperty("Fraction").GetDouble();
        var config = new ImageSimulationConfig
        {
            AberrationMode = "Partially Coherent",
            CoherenceFunction = Enum.Parse<SpatialCoherenceFunction>(capture.GetProperty("Function").GetString()!),
            CoherenceMethod = Enum.Parse<SpatialCoherenceMethod>(capture.GetProperty("Method").GetString()!),
            CoherenceAlphaMillimeters = capture.GetProperty("Alpha").GetDouble(),
            CoherenceSampling = 0,
            CoherenceFraction = fraction,
            PsfEnergyFraction = fraction,
            FileSizeMillimeters = capture.GetProperty("FileSize").GetDouble(),
            NumRays = capture.GetProperty("Sampling").GetInt32(),
            PsfSize = 256,
            Padding = size / 2,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            WavelengthsMicrometers = [new[] { .42, .44, .46 }[wave - 1]],
            UseRelativeIllumination = false,
            NormalizeImagePeak = true
        };
        if (!capture.GetProperty("Available").GetBoolean())
        {
            var error = Assert.Throws<AnalysisDataUnavailableException>(() => ImageSimulationEngine.Simulate(Primary(), new RgbImage(source), config));
            Assert.Contains("intensity sampling is too low", error.Message);
            return;
        }
        var actual = ImageSimulationEngine.Simulate(Primary(), new RgbImage(source), config);
        using var values = ReadBoundaryEntry(capture.GetProperty("Entry").GetString()!);
        double errorMaximum = 0;
        for (var y = 0; y < actual.Simulated.Height; y++) for (var x = 0; x < actual.Simulated.Width; x++)
            errorMaximum = Math.Max(errorMaximum, Math.Abs(actual.Simulated[0, y, x] - values.RootElement[actual.Simulated.Height - 1 - y][x].GetDouble()));
        Assert.True(errorMaximum < 1e-9, $"{name}: {errorMaximum:R}");
    }

    [Fact]
    public void WorkbenchReportsUndersampledIntensityAsUnavailableWithoutARaster()
    {
        var data = new WorkbenchRuntime(Primary()).BuildAnalysisData("Partially Coherent Image Analysis", new Dictionary<string, string>
        {
            ["ImageSize"] = "16",
            ["PupilSampling"] = "256 x 256",
            ["PsfSize"] = "256",
            ["CoherenceFunction"] = "Gaussian",
            ["CoherenceMethod"] = "FourierModes",
            ["FileSize"] = "0.016",
            ["PsfEnergyFraction"] = "0.98",
            ["CoherenceFraction"] = "0.98",
            ["CoherenceSampling"] = "0",
            ["RelativeIllumination"] = "false",
            ["WavelengthNumber"] = "2"
        });
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
        Assert.Contains("intensity sampling is too low", data.OutcomeReason);
        Assert.Empty(data.PlotSeries);
    }

    public static IEnumerable<object[]> NativeImages()
    {
        using var manifest = ReadEntry("manifest.json");
        return manifest.RootElement.GetProperty("Cases").EnumerateArray()
            .Select(value => new object[] { value.GetProperty("Name").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(NativeImages))]
    public void OriginalLensMatchesNativeWideImageWithEnergySelectedComplexSupport(string name)
    {
        using var manifest = ReadEntry("manifest.json");
        var entry = manifest.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == name).GetProperty("Entry").GetString()!;
        using var capture = ReadEntry(entry);
        var settings = capture.RootElement.GetProperty("Settings");
        var size = settings.GetProperty("SourceSize").GetInt32();
        var source = new double[1, size, size];
        foreach (var point in settings.GetProperty("Points").EnumerateArray())
            source[0, point.GetProperty("Y").GetInt32(), point.GetProperty("X").GetInt32()] = point.GetProperty("Intensity").GetDouble();
        var fraction = settings.GetProperty("Fraction").GetDouble();
        var actual = ImageSimulationEngine.Simulate(Primary(), new RgbImage(source), new ImageSimulationConfig
        {
            AberrationMode = "Partially Coherent",
            CoherenceFunction = Enum.Parse<SpatialCoherenceFunction>(settings.GetProperty("Function").GetString()!),
            CoherenceMethod = Enum.Parse<SpatialCoherenceMethod>(settings.GetProperty("Method").GetString()!),
            CoherenceAlphaMillimeters = settings.GetProperty("Alpha").GetDouble(),
            CoherenceFraction = fraction,
            CoherenceSampling = 0,
            PsfEnergyFraction = fraction,
            FileSizeMillimeters = settings.GetProperty("FileSize").GetDouble(),
            NumRays = settings.GetProperty("Sampling").GetInt32(),
            PsfSize = 256,
            Padding = settings.GetProperty("Padding").GetInt32(),
            PsfGridRows = 1,
            PsfGridColumns = 1,
            WavelengthsMicrometers = [settings.GetProperty("Wavelength").GetDouble()],
            NormalizeImagePeak = true,
            UseRelativeIllumination = false
        });
        var native = capture.RootElement.GetProperty("Values");
        double error = 0;
        for (var y = 0; y < actual.Simulated.Height; y++) for (var x = 0; x < actual.Simulated.Width; x++)
            error = Math.Max(error, Math.Abs(actual.Simulated[0, y, x] - native[actual.Simulated.Height - 1 - y][x].GetDouble()));
        Assert.True(error < 1e-9, $"{name}: maximum absolute normalized intensity difference {error:R}");
        var window = Assert.Single(actual.CoherentImaging!.KernelWindows);
        Assert.Equal(fraction, window.RequestedEnergyFraction);
        Assert.Equal(0, window.Size % 2);
        Assert.InRange(window.Size, 2, 256);
        Assert.True(window.SelectionCapturedPowerFraction >= fraction);
        Assert.InRange(window.CapturedPowerFraction, 0, 1);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void CompleteComplexResponseIntensityMatchesNativeTestPsfAtItsDeclaredAxisOrigin(int sampling)
    {
        using var native = ReadEntry($"psfs/{sampling}.json");
        var optic = Primary();
        var response = DiffractionEngine.ComputeCoherentImageKernel(optic, (0, 0), optic.Wavelengths[1], sampling, sampling, .0005, true);
        var peak = response.Values.Cast<Complex>().Max(value => value.Magnitude * value.Magnitude);
        double error = 0;
        for (var y = 0; y < sampling; y++) for (var x = 0; x < sampling; x++)
        {
            var value = response.Values[y, x];
            // Test PSF's ascending-Y lower-center origin differs from an image's source origin.
            var expected = native.RootElement[(2 * sampling - 2 - y) % sampling][x].GetDouble();
            error = Math.Max(error, Math.Abs(value.Magnitude * value.Magnitude / peak - expected));
        }
        Assert.InRange(error, 0, 1e-9);
    }

    [Fact]
    public void EvenEnergyWindowPreservesEveryComplexSampleAndReportsActualLostPower()
    {
        var full = new Complex[4, 4];
        full[1, 1] = new Complex(1, 1);
        full[1, 0] = new Complex(3, 4);
        full[2, 2] = new Complex(1, -1);
        full[3, 3] = new Complex(.1, .1);
        var actual = DiffractionEngine.CropCoherentImageKernelByEnergy(full, .0005, .9);
        Assert.Equal(2, actual.Values.GetLength(0));
        for (var y = 0; y < 2; y++) for (var x = 0; x < 2; x++) Assert.Equal(full[y + 1, x + 1], actual.Values[y, x]);
        Assert.Equal(29.02, actual.FullKernelPower, 12);
        Assert.Equal(4, actual.KernelPower, 12);
        Assert.Equal(29 / 29.02, actual.SelectionCapturedPowerFraction, 12);
        Assert.Equal(4 / 29.02, actual.CapturedPowerFraction, 12);
        Assert.True(actual.CapturedPowerFraction < actual.EnergyWindowFraction);
    }

    [Fact]
    public void RequestedEnergyCannotSilentlyExceedTheConfiguredSupportCap()
    {
        var full = new Complex[8, 8];
        for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++) full[y, x] = Complex.One;
        var error = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.CropCoherentImageKernelByEnergy(full, .001, .98, 4));
        Assert.Contains("needs 8 pixels", error.Message);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-.1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidEnergyFractionIsRejected(double fraction)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.CropCoherentImageKernelByEnergy(new Complex[4, 4], .001, fraction));

    [Fact]
    public void EmptyResponseCannotBecomeAnIdealPoint()
        => Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.CropCoherentImageKernelByEnergy(new Complex[4, 4], .001, .9));

    [Fact]
    public void EnergyWindowHonorsCancellationBeforeInspectingTheKernel()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => DiffractionEngine.CropCoherentImageKernelByEnergy(new Complex[4, 4], .001, .9));
    }

    [Theory]
    [InlineData("FiniteCorrelationWindow")]
    [InlineData("FourierModes")]
    public void WorkbenchForwardsEnergyWindowAndAutomaticCoherenceSampling(string method)
    {
        var runtime = new WorkbenchRuntime(Primary());
        var data = runtime.BuildAnalysisData("Partially Coherent Image Analysis", new Dictionary<string, string>
        {
            ["ImageSize"] = "16",
            ["PupilSampling"] = "128 x 128",
            ["PsfSize"] = "256",
            ["CoherenceFunction"] = "Gaussian",
            ["CoherenceMethod"] = method,
            ["Alpha"] = "0.001",
            ["FileSize"] = "0.008",
            ["PsfEnergyFraction"] = "0.95",
            ["CoherenceFraction"] = "0.95",
            ["CoherenceSampling"] = "0（随 PSF 尺寸）",
            ["RelativeIllumination"] = "false",
            ["WavelengthNumber"] = "2",
            ["NormalizeImagePeak"] = "true",
            ["GuardBand"] = "8"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(.95, data.Values["PsfEnergyFraction"]);
        Assert.Equal(0, data.Values["CoherenceSampling"]);
        var window = Assert.Single(Assert.IsType<CoherentKernelWindow[]>(data.Values["ComplexKernelWindows"]));
        Assert.Equal(18, window.Size);
        Assert.Equal(method == "FourierModes" ? 32 : 0, window.EffectiveCoherenceSampling);
        Assert.Contains("extracted even support", (string)data.Values["PsfWindowSelection"]);
    }

    [Fact]
    public void NonfiniteEnergyFractionIsRejectedBeforeOpticalWork()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ImageSimulationEngine.Simulate(Primary(), new RgbImage(new double[1, 1, 1]),
            new ImageSimulationConfig { AberrationMode = "Partially Coherent", PsfEnergyFraction = double.NaN }));

    [Fact]
    public void UniformModelDoesNotSilentlySelectADifferentPupilSamplingPath()
        => Assert.Throws<ArgumentException>(() => ImageSimulationEngine.Simulate(Primary(), new RgbImage(new double[1, 1, 1]),
            new ImageSimulationConfig { AberrationMode = "Partially Coherent", PsfEnergyFraction = .95 }));

    [Fact]
    public void CoherenceFourierWorkRemainsBoundedAcrossAllWavelengths()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResourceLimits.ValidateImageSimulation(new RgbImage(new double[1, 1, 1]),
            new ImageSimulationConfig
            {
                AberrationMode = "Partially Coherent",
                CoherenceFunction = SpatialCoherenceFunction.Gaussian,
                CoherenceMethod = SpatialCoherenceMethod.FourierModes,
                CoherenceSampling = 512,
                PsfGridRows = 1,
                PsfGridColumns = 1,
                Padding = 0,
                PsfSize = 256,
                NumRays = 256,
                WavelengthsMicrometers = Enumerable.Repeat(.44, 16).ToArray()
            }));
        Assert.Contains("Fourier work", error.Message);
    }

    private static OpticalSystemDesign.Core.Optic Primary() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");

    private static JsonDocument ReadEntry(string name)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "partial-coherence-psf-window-zemax-2026-r1.zip"));
        using var stream = zip.GetEntry(name)!.Open();
        return JsonDocument.Parse(stream);
    }

    private static JsonDocument ReadBoundaryEntry(string name)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "partial-coherence-sampling-boundary-zemax-2026-r1.zip"));
        using var stream = zip.GetEntry(name)!.Open();
        return JsonDocument.Parse(stream);
    }
}
