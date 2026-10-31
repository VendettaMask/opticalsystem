using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class BinocularFourierConvergedTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void HighFrequencyFieldCurvesMeetOriginalGateAgainstConvergedNativeReference(int config)
    {
        using var fixture = new Fixture();
        var optic = fixture.Optic(config);
        var data = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier, spatialFrequency: 3.2,
            fieldPointCount: 10, spatialFrequencies: [3.2], wavelengthNumber: 0, removeVignettingFactors: true,
            settings: new(PupilSampling: 32, ImageSize: 32, ZemaxCompatible: true), zemaxCompatibleOutput: true).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(10, data.Values["FieldPointCount"]);
        Assert.Equal(2, data.PlotSeries.Count);
        var gate = Policy().Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        for (var direction = 0; direction < 2; direction++)
        {
            var low = fixture.Native(config, 128, direction);
            var reference = fixture.Native(config, 256, direction);
            var convergence = ComparisonMetrics.Calculate("native-convergence", "Dimensionless",
                ComparisonMetrics.Align(low, reference, []), gate);
            Assert.True(convergence.Conclusion == Conclusion.Pass, convergence.ToString());
            var series = data.PlotSeries[direction];
            Assert.Equal(AnalysisAxisQuantity.NormalizedField, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
            Assert.Equal(AnalysisAxisQuantity.Modulation, series.YQuantity);
            var actual = reference with { DisplayName = "Core", X = series.Points.Select(p => p.X).ToArray(), Y = series.Points.Select(p => p.Y).ToArray() };
            var result = ComparisonMetrics.Calculate("high-frequency-field", "Dimensionless",
                ComparisonMetrics.Align(actual, reference, []), gate);
            Assert.True(result.Conclusion == Conclusion.Pass, result.ToString());
        }
    }

    [Fact]
    public void ShiftedPupilRaysRetainNativeOpdAndAcceptance()
    {
        using var fixture = new Fixture();
        var optic = fixture.Optic(1);
        var request = JsonFiles.Read<JsonElement>(Path.Combine(fixture.Directory, "phase/request.json"));
        var points = request.GetProperty("pointInputs").EnumerateArray()
            .Select(p => (p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble())).ToArray();
        var wave = optic.Wavelengths[1];
        var result = DiffractionEngine.GenerateDefocusedWavefront(optic, (0, 1), wave, points, 0, wave, optic.RayAimingEnabled);
        var native = JsonFiles.Read<JsonElement>(Path.Combine(fixture.Directory, "phase/reference.json"));
        var rays = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays").EnumerateArray().ToArray();
        Assert.Equal(704, points.Length);
        Assert.Equal(points.Length, result.Samples.Count);
        Assert.Equal(points.Length, rays.Length);
        var gate = Policy().Analyses["Wavefront"].Quantities["WavefrontError"];
        for (var i = 0; i < points.Length; i++)
        {
            Assert.Equal(0, rays[i].GetProperty("error").GetInt32());
            Assert.Equal(0, rays[i].GetProperty("vignette").GetInt32());
            Assert.True(result.Samples[i].Intensity > 0);
            Assert.InRange(Math.Abs(result.Samples[i].OpdWaves - rays[i].GetProperty("opdWaves").GetDouble()), 0, gate.Absolute);
        }
    }

    private static ComparisonConfiguration Policy() => ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));

    private sealed class Fixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "binocular-fourier-" + Guid.NewGuid().ToString("N"));
        internal Fixture()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures/binocular-fourier-converged");
            var manifest = JsonFiles.Read<JsonElement>(Path.Combine(folder, "manifest.json"));
            var archive = Path.Combine(folder, "reference.zip");
            Assert.Equal(manifest.GetProperty("sha256").GetString(), Hash(archive));
            ZipFile.ExtractToDirectory(archive, Directory);
        }
        internal Optic Optic(int config)
        {
            var contract = JsonFiles.Read<JsonElement>(Path.Combine(Directory, "contract.json"));
            Assert.Equal("ConvergenceCheckedNativeResult", contract.GetProperty("referenceBasis").GetString());
            Assert.Equal(Hash(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")), contract.GetProperty("policySha256").GetString());
            var input = contract.GetProperty("inputs").EnumerateArray().Single(r => r.GetProperty("configuration").GetInt32() == config);
            var path = Path.Combine(Directory, input.GetProperty("snapshot").GetString()!);
            Assert.Equal(input.GetProperty("snapshotSha256").GetString(), Hash(path));
            Assert.Equal(input.GetProperty("sourceSha256").GetString(), Hash(Path.Combine(Directory, "inputs/Binocular_System.ZMX")));
            return Core.Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(path));
        }
        internal Series1DResult Native(int config, int sampling, int direction)
        {
            var folder = Path.Combine(Directory, $"curves/c{config}-n{sampling}");
            var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
            Assert.True(model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean());
            Assert.Equal("Milliradians", model.GetProperty("units").GetProperty("AfocalModeUnits").GetString());
            var capture = JsonFiles.Read<JsonElement>(Path.Combine(folder, "captured-settings.json"));
            Assert.Equal(config, capture.GetProperty("request").GetProperty("configuration").GetInt32());
            Assert.Equal($"S_{sampling}x{sampling}", capture.GetProperty("properties").GetProperty("SampleSize").GetString());
            Assert.Equal(3.2, capture.GetProperty("properties").GetProperty("Freq_1").GetDouble());
            Assert.Equal(10, capture.GetProperty("properties").GetProperty("FieldDensity").GetInt32());
            Assert.Equal("Plus_Y", capture.GetProperty("properties").GetProperty("ScanType").GetString());
            Assert.Equal(0, capture.GetProperty("selectors").GetProperty("Wavelength").GetInt32());
            var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, "data.json")).GetProperty("dataSeries");
            Assert.Equal(1, data.GetArrayLength());
            var series = data[0];
            return new("curve", "Zemax", series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                series.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose()
        {
            var path = Path.GetFullPath(Directory);
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "binocular-fourier-";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected fixture cleanup path");
            System.IO.Directory.Delete(path, recursive: true);
        }
    }
}
