using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalOpdPointwiseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("0005-001-afocal-riflescope-c1-primary-f1-w2-optical-path-difference", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("0022-001-afocal-riflescope-c1-edge-f3-w1-optical-path-difference", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("0026-001-afocal-riflescope-c1-edge-f3-w2-optical-path-difference", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("0030-001-afocal-riflescope-c1-edge-f3-w3-optical-path-difference", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("0035-002-beam-expander-c1-primary-f1-w1-optical-path-difference", "Afocal/Beam Expander.ZMX", 1)]
    [InlineData("0052-002-beam-expander-c1-edge-f1-w1-optical-path-difference", "Afocal/Beam Expander.ZMX", 1)]
    [InlineData("0057-003-binocular_system-c1-primary-f1-w2-optical-path-difference", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("0074-003-binocular_system-c1-edge-f5-w1-optical-path-difference", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("0078-003-binocular_system-c1-edge-f5-w2-optical-path-difference", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("0082-003-binocular_system-c1-edge-f5-w3-optical-path-difference", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("0087-003-binocular_system-c2-primary-f1-w2-optical-path-difference", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("0104-003-binocular_system-c2-edge-f5-w1-optical-path-difference", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("0108-003-binocular_system-c2-edge-f5-w2-optical-path-difference", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("0112-003-binocular_system-c2-edge-f5-w3-optical-path-difference", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("0117-004-apochromat_prism-c1-primary-f1-w3-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("0134-004-apochromat_prism-c1-edge-f1-w1-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("0138-004-apochromat_prism-c1-edge-f1-w2-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("0142-004-apochromat_prism-c1-edge-f1-w3-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("0146-004-apochromat_prism-c1-edge-f1-w4-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("0150-004-apochromat_prism-c1-edge-f1-w5-optical-path-difference", "Miscellaneous/Apochromat_prism.zmx", 1)]
    public async Task OriginalCapturedOpdMeetsUnchangedPointwiseGate(string id, string lens, int configuration)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture, "manifest.json")));
        var archive = Path.Combine(fixture, "source-evidence.zip");
        using (var file = File.OpenRead(archive))
            Assert.Equal(manifest.RootElement.GetProperty("sourceArchiveSha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(file)));
        using var zip = ZipFile.OpenRead(archive);
        byte[] Read(string path)
        {
            using var input = zip.GetEntry(path)!.Open();
            using var memory = new MemoryStream(); input.CopyTo(memory); return memory.ToArray();
        }
        var prefix = "artifacts/validation/afocal-fft-energy-20261027/final-150-v2/" + id + "/workbench/";
        using var packet = JsonDocument.Parse(Read(prefix + "request.json"));
        var request = packet.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.Equal(64, request.PupilSampling);
        Assert.False(request.Polarization);
        Assert.Equal(20, request.RayCount);
        Assert.Equal(configuration, request.Configuration);
        var directory = Path.Combine(Path.GetTempPath(), "afocal-opd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var nativePrefix = "artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/";
            foreach (var name in new[] { "data.json", "model.json", "captured-settings.json" })
                File.WriteAllBytes(Path.Combine(directory, name), Read(nativePrefix + name));
            var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
                "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("Optical Path Difference", request.WorkbenchSettings);
            Assert.Equal(2, data.PlotPanes!.Count);
            Assert.All(data.PlotPanes, pane => Assert.Equal(41, Assert.Single(pane.Series).Points.Count));
            var workbench = Path.Combine(directory, "workbench.json"); JsonFiles.Write(workbench, data);
            var entry = AnalysisComparisonRegistry.Get("Optical Path Difference");
            var actual = ResultNormalizer.Workbench(workbench, entry, request);
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, request);
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses["Optical Path Difference"].Quantities["WavefrontError"];
            var summaries = new List<object>();
            var violations = 0;
            for (var i = 0; i < expected.Series.Count; i++)
            {
                var matched = ComparisonMetrics.Align(actual.Series[i], expected.Series[i], []);
                var errors = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).ToArray();
                var bad = errors.Where((e, k) => e > policy.Absolute + policy.Relative * Math.Abs(matched.Zemax[k])).Count();
                violations += bad;
                var metric = ComparisonMetrics.Calculate(actual.Series[i].Id, "Wave", matched, policy);
                Assert.Equal(Conclusion.Pass, metric.Conclusion);
                summaries.Add(new { curve = actual.Series[i].Id, violations = bad, metric });
                Assert.InRange(matched.Coverage, policy.MinimumCoverage, 1);
            }
            output.WriteLine(JsonSerializer.Serialize(new { id, request.Fingerprint, violations, curves = summaries }, JsonFiles.Options));
            OpdReferenceDiagnostics.WriteIfRequested(id, optic, request, actual, expected);
            Assert.Equal(0, violations);
        }
        finally { Directory.Delete(directory, true); }
    }
}
