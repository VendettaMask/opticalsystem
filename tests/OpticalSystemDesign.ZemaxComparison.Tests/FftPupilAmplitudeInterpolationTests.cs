using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class FftPupilAmplitudeInterpolationTests(ITestOutputHelper output)
{
    private static WavefrontResult Interpolate(WavefrontResult source, PupilGridSpecification grid)
        => (WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilAmplitude",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [source, grid])!;

    [Fact]
    public void FrozenComplexControlConfirmsAmplitudeStencilAndUnchangedMask()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var path = Path.Combine(directory, "source-evidence.zip");
        using (var file = File.OpenRead(path))
            Assert.Equal(manifest.RootElement.GetProperty("sourceArchiveSha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(file)));
        using var zip = ZipFile.OpenRead(path);
        const string prefix = "artifacts/validation/afocal-fft-energy-20261027/native-energy-grid-v2/beam-expander-";
        JsonDocument Read(string mode, string name)
        {
            using var stream = zip.GetEntry(prefix + mode + "/" + name)!.Open();
            return JsonDocument.Parse(stream);
        }
        using var real = Read("Real", "data.json");
        using var imaginary = Read("Imaginary", "data.json");
        using var settings = Read("Real", "captured-settings.json");
        Assert.Equal("PsfS_32x32", settings.RootElement.GetProperty("properties").GetProperty("SampleSize").GetString());
        Assert.Equal("PsfS_64x64", settings.RootElement.GetProperty("properties").GetProperty("OutputSize").GetString());
        Assert.Equal(1, settings.RootElement.GetProperty("selectors").GetProperty("Wavelength").GetInt32());
        Assert.False(settings.RootElement.GetProperty("properties").GetProperty("Normalize").GetBoolean());
        var realGrid = real.RootElement.GetProperty("dataGrids")[0];
        var imaginaryGrid = imaginary.RootElement.GetProperty("dataGrids")[0];
        Assert.Equal(64, realGrid.GetProperty("nx").GetInt32());
        Assert.Equal(realGrid.GetProperty("dx").GetDouble(), imaginaryGrid.GetProperty("dx").GetDouble());
        var field = new Complex[64, 64];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            field[(y + 33) % 64, (x + 33) % 64] = Complex.Conjugate(new Complex(
                realGrid.GetProperty("values")[y][x].GetDouble(), imaginaryGrid.GetProperty("values")[y][x].GetDouble()));
        typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [field, true]);
        var maximum = field.Cast<Complex>().Max(c => c.Magnitude);
        // This separate captured request uses the explicit 32-node DEE interval.
        // It does not redefine the original 64-node PSF grids.
        var step = 1 / (2 * Math.Sqrt(32));
        var grid = new PupilGridSpecification(32, true, true, 15 * step);
        var samples = new List<WavefrontSample>();
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var px = (x - 16) * step; var py = (y - 16) * step;
            if ((x - 16) * (x - 16) + (y - 16) * (y - 16) <= 128)
                samples.Add(new(px, py, 0, 0, 0, 0, Math.Exp(-4 * (px * px + py * py))));
        }
        var source = new WavefrontResult(samples, 1, 0, 0);
        var actual = Interpolate(source, grid);
        var expectedMask = new HashSet<(int X, int Y)>();
        var actualMaximum = actual.Samples.Max(s => Math.Sqrt(s.Intensity));
        var maximumError = 0d;
        foreach (var sample in actual.Samples)
        {
            var x = (int)Math.Round(sample.NormalizedPupilX / step) + 32;
            var y = (int)Math.Round(sample.NormalizedPupilY / step) + 32;
            expectedMask.Add((x, y));
            var native = field[(y + 32) % 64, (x + 32) % 64].Magnitude / maximum;
            maximumError = Math.Max(maximumError, Math.Abs(Math.Sqrt(sample.Intensity) / actualMaximum - native));
        }
        var nativeMask = new HashSet<(int X, int Y)>();
        for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            if (field[(y + 32) % 64, (x + 32) % 64].Magnitude / maximum > 1e-8) nativeMask.Add((x, y));
        Assert.Equal(405, expectedMask.Count);
        Assert.True(expectedMask.SetEquals(nativeMask));
        Assert.InRange(maximumError, 0, 1e-7);
        output.WriteLine($"Frozen 32/64 control: {expectedMask.Count} unchanged nodes, max amplitude difference={maximumError:R}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StencilPreservesInputsPhaseAndBlockedOrMissingNeighbors(bool blocked)
    {
        var grid = new PupilGridSpecification(32, true, true, 1);
        WavefrontSample Sample(int x, int y, double intensity) => new(grid.Coordinate(x), grid.Coordinate(y),
            0, 0, 0, .123, intensity) { LaunchIntensity = intensity };
        var samples = new[] { Sample(16, 16, 1), Sample(15, 16, blocked ? 0 : 4),
            Sample(17, 16, 16), Sample(16, 15, 36), Sample(16, 17, 64), Sample(14, 16, 7) };
        var before = samples.ToArray();
        var actual = Interpolate(new WavefrontResult(samples, 1, 0, 0), grid);
        Assert.Equal(blocked ? 1 : 25, actual.Samples[0].Intensity);
        Assert.Equal(before, samples);
        Assert.Equal(samples.Length, actual.Samples.Count);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i].OpdWaves, actual.Samples[i].OpdWaves);
            Assert.Equal(samples[i].LaunchIntensity, actual.Samples[i].LaunchIntensity);
            if (i != 0) Assert.Equal(samples[i], actual.Samples[i]);
        }
    }
}
