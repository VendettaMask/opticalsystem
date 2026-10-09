using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class FftPupilPhaseParityTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Validation/Zemax/FftPupilPhase");

    [Theory]
    [InlineData("ms-l7-32", 32)]
    [InlineData("ms-l7-64", 64)]
    [InlineData("cooke-32", 32)]
    [InlineData("tessar-32", 32)]
    public void CapturedNativeRayInputsReproduceNativeFftIntensity(string name, int sampling)
    {
        var directory = Path.Combine(Root, name);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(directory, "source.ZMX")), ".zmx");
        using var model = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "probe/model.json")));
        var aiming = model.RootElement.GetProperty("rayAiming").GetProperty("RayAiming").GetString() != "Off";
        optic.RayAimingEnabled = aiming;
        var wave = optic.Wavelengths[0];
        var stretch = Math.Sqrt(sampling / 32d);
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, sampling,
            cellCentered: true, aimAtStop: aiming, pupilGridStretch: stretch, zemaxCentered: true, referenceWavelength: wave);
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"pupil-{sampling}/pupil-grid.json")));
        var inputs = native.RootElement.GetProperty("inputs"); var rows = native.RootElement.GetProperty("rows");
        Assert.Equal(wavefront.Samples.Count, rows.GetArrayLength());
        var captured = Enumerable.Range(0, rows.GetArrayLength()).ToDictionary(i =>
            (inputs[i].GetProperty("px").GetDouble(), inputs[i].GetProperty("py").GetDouble()), i => rows[i]);
        Assert.All(captured.Values, ray => { Assert.Equal(0, ray.GetProperty("error").GetInt32()); Assert.Equal(0, ray.GetProperty("vignette").GetInt32()); });
        var prepared = wavefront with { Samples = wavefront.Samples.Select(sample => sample with {
            OpdWaves = captured[(sample.NormalizedPupilX, sample.NormalizedPupilY)].GetProperty("opd").GetDouble(),
            Intensity = captured[(sample.NormalizedPupilX, sample.NormalizedPupilY)].GetProperty("intensity").GetDouble()
        }).ToArray() };
        var original = prepared.Samples.ToArray();
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, sampling, 2 * sampling,
            cellCenteredPupil: true, zemaxFftSampling: true, preparedWavefront: prepared,
            aimAtStop: aiming, referenceWavelength: wave);
        Assert.Equal(original, prepared.Samples);
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, $"psf-{sampling}/data.json")));
        var grid = expected.RootElement.GetProperty("dataGrids")[0];
        var width = grid.GetProperty("nx").GetInt32(); var height = grid.GetProperty("ny").GetInt32();
        var dx = grid.GetProperty("dx").GetDouble(); var dy = grid.GetProperty("dy").GetDouble();
        var xmin = grid.GetProperty("minX").GetDouble(); var ymin = grid.GetProperty("minY").GetDouble();
        var squared = 0d; var referenceSquared = 0d; var maximum = 0d;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var reference = grid.GetProperty("values")[y][x].GetDouble();
            var actual = PsfAnalysis.BilinearSample(psf, xmin + x * dx, ymin + y * dy) / 100;
            var difference = actual - reference;
            squared += difference * difference; referenceSquared += reference * reference;
            maximum = Math.Max(maximum, Math.Abs(difference));
        }
        var relativeL2 = Math.Sqrt(squared / referenceSquared);
        Assert.True(relativeL2 <= 1e-6 && maximum <= 1e-6,
            $"{name}: native-pupil FFT relative L2={relativeL2:R}, max={maximum:R}; no fitting or peak normalization.");
    }

    [Fact]
    public void FourierBoundarySamplingRetainsRoundoffAtEitherNyquistEdge()
    {
        var values = new double[64, 64];
        values[0, 0] = 7;
        var psf = new PsfResult(values, 32, 64, 4, .3);
        // Independently captured pitch can differ by a few rounding units.
        // Both endpoints represent the same Fourier sample.
        Assert.Equal(7, PsfAnalysis.BilinearSample(psf, (32 + 2e-12) * .3, (32 + 2e-12) * .3));
        Assert.Equal(7, PsfAnalysis.BilinearSample(psf, (-32 - 2e-12) * .3, (-32 - 2e-12) * .3));
    }

    [Fact]
    public void FourierBoundarySamplingStillRejectsCoordinatesOutsideTheGrid()
    {
        var values = new double[64, 64];
        values[0, 0] = 7;
        var psf = new PsfResult(values, 32, 64, 4, .3);
        Assert.Equal(0, PsfAnalysis.BilinearSample(psf, (32 + 1e-5) * .3, 0));
        Assert.Equal(0, PsfAnalysis.BilinearSample(psf, (-32 - 1e-5) * .3, 0));
    }

    [Fact]
    public void NewRawCaptureManifestPreservesAllNativeChannels()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, file.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }
}
