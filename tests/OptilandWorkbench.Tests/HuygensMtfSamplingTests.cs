using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class HuygensMtfSamplingTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensMtfSampling");

    [Theory]
    [InlineData("ms-psf", "ms-mtf")]
    [InlineData("ms-image64-psf", "ms-image64-mtf")]
    [InlineData("primary-psf", "primary-mtf")]
    public void CapturedPsfReconstructsEveryNativeMtfPoint(string psfDirectory, string mtfDirectory)
    {
        var psf = ReadPsf(psfDirectory);
        var mtf = DiffractionEngine.ComputePsfMtf(psf, doubleTransformSize: true);
        using var capture = Read(mtfDirectory, "data.json");
        var series = capture.RootElement.GetProperty("dataSeries")[0];
        var x = series.GetProperty("x");
        var y = series.GetProperty("y");
        Assert.Equal(300, x.GetArrayLength());
        for (var i = 0; i < x.GetArrayLength(); i++)
        {
            var actual = MtfMethodEvaluator.SampleHuygensMtf(mtf, 2 * psf.GridSize, x[i].GetDouble(), true);
            Near(y[i][0].GetDouble(), actual.Tangential);
            Near(y[i][1].GetDouble(), actual.Sagittal);
        }
    }

    [Theory]
    [InlineData("ms-psf", "ms-focus-50", 50)]
    [InlineData("ms-psf", "focus-125", 125)]
    [InlineData("ms-psf", "focus-250", 250)]
    [InlineData("ms-psf", "focus-500", 500)]
    [InlineData("primary-psf", "primary-focus", 50)]
    public void CapturedPsfReconstructsNativeThroughFocusAtZeroDefocus(string psfDirectory, string focusDirectory, double frequency)
    {
        var psf = ReadPsf(psfDirectory);
        var mtf = DiffractionEngine.ComputePsfMtf(psf);
        var actual = MtfMethodEvaluator.SampleHuygensMtf(mtf, psf.GridSize, frequency, true);
        using var capture = Read(focusDirectory, "data.json");
        var series = capture.RootElement.GetProperty("dataSeries")[0];
        Assert.Equal(101, series.GetProperty("x").GetArrayLength());
        Near(0, series.GetProperty("x")[50].GetDouble());
        var expected = series.GetProperty("y")[50];
        Near(expected[0].GetDouble(), actual.Tangential);
        Near(expected[1].GetDouble(), actual.Sagittal);
    }

    [Fact]
    public void CapturedFieldScanUsesTheFrequencyPlotTransformAtItsAxisField()
    {
        var psf = ReadPsf("ms-psf");
        var mtf = DiffractionEngine.ComputePsfMtf(psf, doubleTransformSize: true);
        using var capture = Read("ms-field", "data.json");
        var series = capture.RootElement.GetProperty("dataSeries");
        Assert.Equal(6, series.GetArrayLength());
        for (var i = 0; i < 6; i++)
        {
            var actual = MtfMethodEvaluator.SampleHuygensMtf(mtf, 2 * psf.GridSize, 10 * (i + 1), true);
            Near(0, series[i].GetProperty("x")[0].GetDouble());
            var expected = series[i].GetProperty("y")[0];
            Near(expected[0].GetDouble(), actual.Tangential);
            Near(expected[1].GetDouble(), actual.Sagittal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaddingPreservesPhysicalPixelPitchAndTwoPointSourceModulation(bool padded)
    {
        var values = new double[8, 8];
        values[2, 3] = 1;
        values[4, 3] = 1;
        var result = DiffractionEngine.ComputePsfMtf(new PsfResult(values, 8, 8, 1, 1), padded);
        Assert.Equal(padded ? 8 : 4, result.Frequency.Count);
        Near(padded ? 62.5 : 125, result.Frequency[1]);
        for (var i = 0; i < result.Frequency.Count; i++)
        {
            Near(Math.Abs(Math.Cos(2 * Math.PI * result.Frequency[i] * 0.001)), result.Tangential[i]);
            Near(1, result.Sagittal[i]);
        }
    }

    [Fact]
    public void GeneralSamplingKeepsLinearInterpolationOnTheDftPeriod()
    {
        var result = new MtfResult([0, 100, 200], [1, 0.6, 0.2], [1, 0.8, 0.6], 200);
        var actual = MtfMethodEvaluator.SampleHuygensMtf(result, 32, 50, false);
        Near(0.8, actual.Tangential);
        Near(0.9, actual.Sagittal);
    }

    [Fact]
    public void PaddedTransformRejectsOversizedWorkBeforeAllocatingComplexGrids()
    {
        var size = (AnalysisResourceLimits.MaximumFftGridSize / 2) + 1;
        var psf = new PsfResult(new double[size, size], 32, size, 1, 0.25);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiffractionEngine.ComputePsfMtf(psf, true));
    }

    [Fact]
    public void FrozenCapturesAndLensFilesMatchTheirManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.Combine(Root, file.GetProperty("path").GetString()!);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    private static PsfResult ReadPsf(string directory)
    {
        using var capture = Read(directory, "data.json");
        var grid = capture.RootElement.GetProperty("dataGrids")[0];
        var rows = grid.GetProperty("values");
        var size = rows.GetArrayLength();
        var values = new double[size, size];
        for (var y = 0; y < size; y++)
        {
            Assert.Equal(size, rows[y].GetArrayLength());
            for (var x = 0; x < size; x++)
            {
                values[y, x] = rows[y][x].GetDouble();
            }
        }

        using var settings = Read(directory, "captured-settings.json");
        var request = settings.RootElement.GetProperty("request");
        Near(0.25, request.GetProperty("imageDeltaMicrometers").GetDouble());
        Assert.Equal(size, request.GetProperty("imageSampling").GetInt32());
        return new PsfResult(values, request.GetProperty("pupilSampling").GetInt32(), size, 1, 0.25);
    }

    private static JsonDocument Read(string directory, string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, directory, file)));

    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(actual - expected), 0, 1e-10);
}
