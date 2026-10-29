using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalFftProfilePointwiseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("0011-001-afocal-riflescope-c1-primary-f1-w2-fft-psf-cross-section", "001-afocal-riflescope-c1")]
    [InlineData("0041-002-beam-expander-c1-primary-f1-w1-fft-psf-cross-section", "002-beam-expander-c1")]
    [InlineData("0063-003-binocular_system-c1-primary-f1-w2-fft-psf-cross-section", "003-binocular_system-c1")]
    [InlineData("0093-003-binocular_system-c2-primary-f1-w2-fft-psf-cross-section", "003-binocular_system-c2")]
    [InlineData("0123-004-apochromat_prism-c1-primary-f1-w3-fft-psf-cross-section", "004-apochromat_prism-c1")]
    public void OriginalCapturedFftProfileMeetsUnchangedPointwiseGate(string id, string model)
    {
        var archive = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract/source-evidence.zip");
        using (var file = File.OpenRead(archive))
            Assert.Equal("235f113b4633fce7341e837da03850ac3c8cff14a48c32f4b4731e7250d7f4b1", Convert.ToHexStringLower(SHA256.HashData(file)));
        using var zip = ZipFile.OpenRead(archive);
        byte[] Read(string member)
        {
            using var input = zip.GetEntry(member)!.Open();
            using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray();
        }
        using var packet = JsonDocument.Parse(Read("artifacts/validation/afocal-fft-energy-20261027/final-150-v2/" + id + "/workbench/request.json"));
        var request = packet.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var ledger = JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory, "Fixtures/final-core-170.json"));
        var original = ledger.GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        Assert.Equal(original.GetProperty("requestFingerprint").GetString(), request.Fingerprint);
        var gate = original.GetProperty("effectiveTolerances").GetProperty("Irradiance").Deserialize<Tolerances>(JsonFiles.Options)!;
        Assert.Equal(1e-5, gate.Absolute);
        Assert.Equal(.002, gate.Relative);
        Assert.Equal(32, request.PupilSampling);
        var optic = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(Read(
            "artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json"), JsonFiles.Options)!);
        var directory = Path.Combine(Path.GetTempPath(), "afocal-fft-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var nativePrefix = "artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/";
            foreach (var name in new[] { "data.json", "data.txt" })
                File.WriteAllBytes(Path.Combine(directory, name), Read(nativePrefix + name));
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("FFT PSF Cross Section", request.WorkbenchSettings);
            var actual = ExtendedResultNormalizer.Workbench(data, request);
            var expected = ExtendedResultNormalizer.Zemax(Path.Combine(directory, "data.json"), request);
            var matched = ComparisonMetrics.Align(actual.Series.Single(), expected.Series.Single(), []);
            var metric = ComparisonMetrics.Calculate("curve:0", "Dimensionless", matched, gate);
            var violations = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a-b) > gate.Absolute+gate.Relative*Math.Abs(b)).Count(b => b);
            output.WriteLine(JsonSerializer.Serialize(new { id, request.Fingerprint, violations, metric }, JsonFiles.Options));
            FftProfileDiagnostics.WriteIfRequested(id, optic, request, data, actual, expected, zip);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
            Assert.InRange(matched.Coverage, gate.MinimumCoverage, 1);
            Assert.Equal(0, violations);
        }
        finally { Directory.Delete(directory, true); }
    }
}
