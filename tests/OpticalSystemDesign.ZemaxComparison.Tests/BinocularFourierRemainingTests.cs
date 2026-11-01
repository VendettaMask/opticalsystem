using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class BinocularFourierRemainingTests
{
    [Theory]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 3)] [InlineData(1, 4)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)] [InlineData(2, 4)]
    public void RemainingFieldCurvesMeetOriginalGateAfterIndependentConvergenceChecks(int config, int frequencyIndex)
    {
        using var fixture = new Fixture();
        double[] frequencies = [.1, .2, .4, .8, 1.6];
        var frequency = frequencies[frequencyIndex];
        var optic = fixture.Optic(config);
        var data = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier, spatialFrequency: frequency,
            fieldPointCount: 10, spatialFrequencies: [frequency], wavelengthNumber: 0, removeVignettingFactors: true,
            settings: new(PupilSampling: 32, ImageSize: 32, ZemaxCompatible: true), zemaxCompatibleOutput: true).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(10, data.Values["FieldPointCount"]);
        Assert.Equal(2, data.PlotSeries.Count);
        var refined = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier, spatialFrequency: frequency,
            fieldPointCount: 10, spatialFrequencies: [frequency], wavelengthNumber: 0, removeVignettingFactors: true,
            settings: new(PupilSampling: 64, ImageSize: 64, ZemaxCompatible: true), zemaxCompatibleOutput: true).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, refined.Outcome);
        SaveEvidence(data, 32); SaveEvidence(refined, 64);
        void SaveEvidence(AnalysisData value, int sampling)
        {
            var root = Environment.GetEnvironmentVariable("OSD_BINOCULAR_FOURIER_EVIDENCE");
            if (string.IsNullOrWhiteSpace(root)) return;
            var hash = Hash(typeof(Optic).Assembly.Location);
            var folder = Path.Combine(root, "core-" + hash[..12], "n" + sampling);
            System.IO.Directory.CreateDirectory(folder);
            var id = $"c{config}-f{frequency.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p')}";
            JsonFiles.Write(Path.Combine(folder, id + ".json"), value);
            JsonFiles.Write(Path.Combine(folder, "identity.json"), new { coreSha256 = hash, sampling, nativeReferenceBasis = "ConvergedNativeResult" });
        }
        var gate = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
            .Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        for (var direction = 0; direction < 2; direction++)
        {
            var low = fixture.Native(config, 128, frequencyIndex, direction);
            var reference = fixture.Native(config, 256, frequencyIndex, direction);
            var convergence = ComparisonMetrics.Calculate("native-convergence", "Dimensionless", ComparisonMetrics.Align(low, reference, []), gate);
            Assert.True(convergence.Conclusion == Conclusion.Pass, convergence.ToString());
            var series = data.PlotSeries[direction];
            Assert.Equal(AnalysisAxisQuantity.NormalizedField, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
            Assert.Equal(AnalysisAxisQuantity.Modulation, series.YQuantity);
            var actual = reference with { DisplayName = "Core", X = series.Points.Select(p => p.X).ToArray(), Y = series.Points.Select(p => p.Y).ToArray() };
            var refinedSeries = refined.PlotSeries[direction];
            var refinedCurve = reference with { DisplayName = "Core64", X = refinedSeries.Points.Select(p => p.X).ToArray(), Y = refinedSeries.Points.Select(p => p.Y).ToArray() };
            var coreConvergence = ComparisonMetrics.Calculate("core-convergence", "Dimensionless", ComparisonMetrics.Align(actual, refinedCurve, []), gate);
            Assert.True(coreConvergence.Conclusion == Conclusion.Pass, coreConvergence.ToString());
            var result = ComparisonMetrics.Calculate("remaining-field", "Dimensionless", ComparisonMetrics.Align(actual, reference, []), gate);
            Assert.True(result.Conclusion == Conclusion.Pass, result.ToString());
        }
    }

    [Fact]
    public void DiagnosticDisplayNormalizationReproducesWholeIndependentNativeBuffers()
    {
        using var fixture = new Fixture();
        fixture.VerifyDisplayProof();
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class Fixture : IDisposable
    {
        private string Directory { get; } = Path.Combine(Path.GetTempPath(), "binocular-remaining-" + Guid.NewGuid().ToString("N"));
        private readonly JsonElement _contract;
        internal Fixture()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures/binocular-fourier-remaining");
            var manifest = JsonFiles.Read<JsonElement>(Path.Combine(folder, "manifest.json"));
            var archive = Path.Combine(folder, "reference.zip");
            Assert.Equal(manifest.GetProperty("sha256").GetString(), Hash(archive));
            ZipFile.ExtractToDirectory(archive, Directory);
            _contract = JsonFiles.Read<JsonElement>(Path.Combine(Directory, "contract.json"));
            Assert.Equal("ConvergenceCheckedNativeResult", _contract.GetProperty("referenceBasis").GetString());
            Assert.Equal(Hash(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")), _contract.GetProperty("policySha256").GetString());
        }
        internal Optic Optic(int config)
        {
            var input = _contract.GetProperty("inputs").EnumerateArray().Single(r => r.GetProperty("configuration").GetInt32() == config);
            var path = Path.Combine(Directory, input.GetProperty("snapshot").GetString()!);
            Assert.Equal(input.GetProperty("snapshotSha256").GetString(), Hash(path));
            Assert.Equal(input.GetProperty("sourceSha256").GetString(), Hash(Path.Combine(Directory, "inputs/Binocular_System.ZMX")));
            return Core.Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(path));
        }
        internal Series1DResult Native(int config, int sampling, int index, int direction)
        {
            var folder = Path.Combine(Directory, $"curves/c{config}-n{sampling}");
            var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
            Assert.True(model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean());
            Assert.Equal("Milliradians", model.GetProperty("units").GetProperty("AfocalModeUnits").GetString());
            var request = JsonFiles.Read<JsonElement>(Path.Combine(folder, "request.json"));
            Assert.Equal(config, request.GetProperty("configuration").GetInt32());
            Assert.False(request.GetProperty("captureScreenshots").GetBoolean());
            double[] frequencies = [.1, .2, .4, .8, 1.6, 3.2];
            var native = JsonFiles.Read<JsonElement>(Path.Combine(folder, "fourier-operands.json"));
            Assert.Equal(config, native.GetProperty("configuration").GetInt32());
            Assert.Equal(60, native.GetProperty("rows").GetArrayLength());
            foreach (var row in native.GetProperty("rows").EnumerateArray())
            {
                var input = row.GetProperty("input");
                Assert.Equal((int)Math.Log2(sampling / 16d), input.GetProperty("sampling").GetInt32());
                Assert.Equal(0, input.GetProperty("wavelength").GetInt32());
                Assert.Equal(0, input.GetProperty("grid").GetInt32());
                Assert.Contains(input.GetProperty("frequency").GetDouble(), frequencies);
            }
            var curve = JsonFiles.Read<JsonElement>(Path.Combine(folder, "curve.json"));
            Assert.Equal("NativeOperandsWithValidatedDisplayInterpolation", curve.GetProperty("provenance").GetString());
            Assert.Equal(Hash(Path.Combine(folder, "fourier-operands.json")), curve.GetProperty("rawNodesSha256").GetString());
            Assert.Equal(Hash(Path.Combine(Directory, "native-display-proof.json")), curve.GetProperty("splineProofSha256").GetString());
            Assert.False(curve.GetProperty("productCoreUsed").GetBoolean());
            Assert.Equal(10, curve.GetProperty("calculatedFieldCount").GetInt32());
            var all = curve.GetProperty("dataSeries");
            Assert.Equal(6, all.GetArrayLength());
            var series = all[index];
            Assert.Equal(300, series.GetProperty("x").GetArrayLength());
            return new("curve", "Zemax", series.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                series.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        internal void VerifyDisplayProof()
        {
            var checks = _contract.GetProperty("displayChecks").EnumerateArray().ToArray();
            Assert.Equal(6, checks.Length);
            var compared = 0;
            foreach (var check in checks)
            {
                var raw = Path.Combine(Directory, check.GetProperty("curve").GetString()!);
                var nodes = Path.Combine(Directory, check.GetProperty("nodes").GetString()!);
                Assert.Equal(check.GetProperty("curveSha256").GetString(), Hash(raw));
                Assert.Equal(check.GetProperty("nodesSha256").GetString(), Hash(nodes));
                var curves = JsonFiles.Read<JsonElement>(raw).GetProperty("dataSeries");
                var rows = JsonFiles.Read<JsonElement>(nodes).GetProperty("rows").EnumerateArray().ToArray();
                var frequencies = check.GetProperty("frequencies").EnumerateArray().Select(f => f.GetDouble()).ToArray();
                for (var index = 0; index < frequencies.Length; index++) for (var direction = 0; direction < 2; direction++)
                {
                    var curve = curves[index];
                    var x = curve.GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    var y = curve.GetProperty("y").EnumerateArray().Select(v => v[direction].GetDouble()).ToArray();
                    var nodeValues = Enumerable.Range(1, 10).Select(field => rows.Single(r => r.GetProperty("input").GetProperty("frequency").GetDouble() == frequencies[index]
                        && r.GetProperty("input").GetProperty("field").GetInt32() == field).GetProperty("values").GetProperty(direction == 0 ? "MTFT/0" : "MTFS/0").GetDouble()).ToArray();
                    var normalized = NativeOperandReference.Interpolate(Enumerable.Range(0, 10).Select(i => i / 9d).ToArray(), nodeValues, x);
                    Assert.InRange(y.Zip(normalized, (a, b) => Math.Abs(a - b)).Max(), 0, 1e-10);
                    compared += y.Length;
                }
            }
            Assert.Equal(12600, compared);
        }
        public void Dispose()
        {
            var path = Path.GetFullPath(Directory);
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "binocular-remaining-";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected fixture cleanup path");
            System.IO.Directory.Delete(path, recursive: true);
        }
    }
}
