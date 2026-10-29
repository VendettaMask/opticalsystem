using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class HuygensThroughFocusPointwiseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("0015-001-afocal-riflescope-c1-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("0045-002-beam-expander-c1-primary-f1-w1-huygens-through-focus-mtf", "Afocal/Beam Expander.ZMX", 1)]
    [InlineData("0067-003-binocular_system-c1-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("0097-003-binocular_system-c2-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("0127-004-apochromat_prism-c1-primary-f1-w3-huygens-through-focus-mtf", "Miscellaneous/Apochromat_prism.zmx", 1)]
    [InlineData("fixed-0015-001-afocal-riflescope-c1-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("fixed-0045-002-beam-expander-c1-primary-f1-w1-huygens-through-focus-mtf", "Afocal/Beam Expander.ZMX", 1)]
    [InlineData("fixed-0067-003-binocular_system-c1-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Binocular_System.ZMX", 1)]
    [InlineData("fixed-0097-003-binocular_system-c2-primary-f1-w2-huygens-through-focus-mtf", "Afocal/Binocular_System.ZMX", 2)]
    [InlineData("fixed-0127-004-apochromat_prism-c1-primary-f1-w3-huygens-through-focus-mtf", "Miscellaneous/Apochromat_prism.zmx", 1)]
    public async Task OriginalCapturedThroughFocusMeetsUnchangedPointwiseGate(string id, string lens, int configuration)
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
        var fixedGrid = id.StartsWith("fixed-");
        var prefix = "artifacts/validation/afocal-fft-energy-20261027/" + (fixedGrid ? "final-fixed-v2/" : "final-150-v2/") + id + "/workbench/";
        using var packet = JsonDocument.Parse(Read(prefix + "request.json"));
        var request = packet.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.Equal(fixedGrid ? 64 : 32, request.PupilSampling);
        Assert.Equal(fixedGrid ? 128 : 32, request.ImageSampling);
        Assert.False(request.Polarization);
        Assert.Equal(configuration, request.Configuration);
        var directory = Path.Combine(Path.GetTempPath(), "huygens-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var nativePrefix = "artifacts/validation/afocal-recompare-20261026/" + (fixedGrid ? "fixed-grid-controls/" : "expanded/") + id + "/native/";
            foreach (var name in new[] { "data.json", "model.json", "captured-settings.json" })
                File.WriteAllBytes(Path.Combine(directory, name), Read(nativePrefix + name));
            var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
                "Fixtures/afocal-repair/sources", lens))).Configurations[configuration - 1];
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens Through Focus MTF", request.WorkbenchSettings);
            var workbench = Path.Combine(directory, "workbench.json"); JsonFiles.Write(workbench, data);
            // The generic diffraction adapter assumes focal millimeters.
            // These frozen afocal through-focus axes are explicitly diopters.
            var actual = new NumericResult();
            var expected = new NumericResult();
            using var capture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "data.json")));
            var native = Assert.Single(capture.RootElement.GetProperty("dataSeries").EnumerateArray());
            var x = native.GetProperty("x").Deserialize<double[]>()!;
            Assert.Equal(2, data.PlotSeries.Count);
            Assert.Equal(101, x.Length);
            Assert.Equal(5, Convert.ToInt32(data.Values["Steps"]));
            for (var component = 0; component < 2; component++)
            {
                var curve = data.PlotSeries[component];
                Assert.Equal(AnalysisAxisUnit.Diopter, curve.XUnit);
                Assert.Equal(AnalysisAxisUnit.Dimensionless, curve.YUnit);
                Assert.Equal(101, curve.Points.Count);
                var idCurve = "curve:" + component;
                var axis = new Axis("Defocus", "Diopter");
                var value = new Axis("Modulation", "Dimensionless");
                actual.Series.Add(new(idCurve, curve.Name, curve.Points.Select(p => p.X).ToArray(),
                    curve.Points.Select(p => p.Y).ToArray(), axis, value));
                expected.Series.Add(new(idCurve, "Frozen native", x,
                    native.GetProperty("y").EnumerateArray().Select(y => y[component].GetDouble()).ToArray(), axis, value));
            }
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses["Huygens Through Focus MTF"].Quantities["Modulation"];
            var summaries = new List<object>();
            var violations = 0;
            for (var i = 0; i < expected.Series.Count; i++)
            {
                var matched = ComparisonMetrics.Align(actual.Series[i], expected.Series[i], []);
                var errors = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).ToArray();
                var bad = errors.Where((e, k) => e > policy.Absolute + policy.Relative * Math.Abs(matched.Zemax[k])).Count();
                violations += bad;
                var metric = ComparisonMetrics.Calculate(actual.Series[i].Id, "Dimensionless", matched, policy);
                summaries.Add(new { curve = actual.Series[i].Id, violations = bad, metric });
                Assert.InRange(matched.Coverage, policy.MinimumCoverage, 1);
            }
            output.WriteLine(JsonSerializer.Serialize(new { id, request.Fingerprint, violations, curves = summaries }, JsonFiles.Options));
            HuygensFocusDiagnostics.WriteIfRequested(id, optic, request, data, actual, expected);
            Assert.Equal(0, violations);
        }
        finally { Directory.Delete(directory, true); }
    }
}
