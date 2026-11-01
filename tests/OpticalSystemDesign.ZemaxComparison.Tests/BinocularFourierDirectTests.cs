using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Metrics;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class BinocularFourierDirectTests
{
    private static readonly double[] Frequencies = [.1, .2, .4, .8, 1.6, 3.2];
    [Theory]
    [InlineData(1, 0)] [InlineData(1, 1)] [InlineData(1, 2)] [InlineData(1, 3)] [InlineData(1, 4)] [InlineData(1, 5)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)] [InlineData(2, 4)] [InlineData(2, 5)]
    public void CoreAndDirectNativeFieldCurvesMeetOriginalConvergenceAndResultGates(int config, int frequencyIndex)
    {
        using var fixture = new Fixture();
        var optic = fixture.Optic(config);
        AnalysisData Compute(int n) => new MtfVsFieldAnalysis(optic, MtfComputationMethod.Fourier,
            spatialFrequency: Frequencies[frequencyIndex], spatialFrequencies: [Frequencies[frequencyIndex]], fieldPointCount: 10,
            wavelengthNumber: 0, removeVignettingFactors: true, settings: new(PupilSampling: n, ImageSize: n, ZemaxCompatible: true),
            zemaxCompatibleOutput: true).GenerateData();
        var actual = Compute(32); var refined = Compute(64);
        Assert.Equal(AnalysisOutcome.Success, actual.Outcome); Assert.Equal(AnalysisOutcome.Success, refined.Outcome);
        Assert.Equal(10, actual.Values["FieldPointCount"]);
        var root = Environment.GetEnvironmentVariable("OSD_BINOCULAR_FOURIER_DIRECT_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(root)) foreach (var (n, data) in new[] { (32, actual), (64, refined) })
        {
            var hash = FftFieldCaptureAudit.Hash(typeof(Optic).Assembly.Location);
            var folder = Path.Combine(root, "core-" + hash[..12], "n" + n); Directory.CreateDirectory(folder);
            var id = $"c{config}-f{Frequencies[frequencyIndex].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', 'p')}";
            JsonFiles.Write(Path.Combine(folder, id + ".json"), data);
            JsonFiles.Write(Path.Combine(folder, "identity.json"), new { coreSha256 = hash, sampling = n, referenceBasis = "DirectNativeBuffers" });
        }
        var low = fixture.Native(config, 128); var high = fixture.Native(config, 256);
        var gate = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
            .Analyses["Fourier MTF vs Field"].Quantities["Modulation"];
        for (var d = 0; d < 2; d++)
        {
            var reference = high.Curves[2 * frequencyIndex + d];
            var series = actual.PlotSeries[d]; var refinedSeries = refined.PlotSeries[d];
            Assert.Equal(AnalysisAxisQuantity.NormalizedField, series.XQuantity);
            Assert.Equal(AnalysisAxisUnit.Dimensionless, series.XUnit);
            Assert.Equal(AnalysisAxisQuantity.Modulation, series.YQuantity);
            var core = reference with { X = series.Points.Select(p => p.X).ToArray(), Y = series.Points.Select(p => p.Y).ToArray() };
            var core64 = reference with { X = refinedSeries.Points.Select(p => p.X).ToArray(), Y = refinedSeries.Points.Select(p => p.Y).ToArray() };
            void Pass(string id, Series1DResult a, Series1DResult b)
            {
                var metric = ComparisonMetrics.Calculate(id, "Dimensionless", ComparisonMetrics.Align(a, b, []), gate);
                Assert.True(metric.Conclusion == Conclusion.Pass, metric.ToString());
            }
            Pass("Native128To256", low.Curves[2 * frequencyIndex + d], reference);
            Pass("Core32To64", core, core64); Pass("Core32VsDirectNative256", core, reference);
        }
    }

    [Fact]
    public void DirectBuffersIndependentlyConfirmPreviousDerivedReferences()
    {
        using var fixture = new Fixture(); var count = 0;
        foreach (var config in new[] { 1, 2 }) foreach (var n in new[] { 128, 256 })
        {
            var native = fixture.Native(config, n);
            var derived = JsonFiles.Read<JsonElement>(Path.Combine(fixture.Root, $"previous-derived/c{config}-n{n}.json")).GetProperty("dataSeries");
            for (var f = 0; f < 6; f++) for (var d = 0; d < 2; d++)
            {
                var direct = native.Curves[2 * f + d];
                var x = derived[f].GetProperty("x").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                Assert.Equal(direct.X, x);
                var y = derived[f].GetProperty("y").EnumerateArray().Select(v => v[d].GetDouble()).ToArray();
                Assert.InRange(y.Zip(direct.Y, (a, b) => Math.Abs(a - b)).Max(), 0, 1e-10);
                count += y.Length;
            }
        }
        Assert.Equal(14400, count);
    }

    [Fact]
    public void FinalSuccessfulExecutionOverridesAnEarlierNullPoll()
    {
        using var fixture = new Fixture();
        var audit = FftFieldCaptureAudit.Inspect(Path.Combine(fixture.Root, "audit/late"), "full");
        Assert.Equal(CaptureStatus.Captured, audit.Status);
        Assert.NotNull(audit.DataSha256);
    }

    [Fact]
    public void TimedOutAndUnstartedJobsAreNeverPromotedToCaptured()
    {
        using var fixture = new Fixture(); var folder = Path.Combine(fixture.Root, "audit/timeout");
        Assert.Equal(CaptureStatus.TimedOut, FftFieldCaptureAudit.Inspect(folder, "c1-n128").Status);
        Assert.Equal(CaptureStatus.Skipped, FftFieldCaptureAudit.Inspect(folder, "c1-n256").Status);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "binocular-direct-" + Guid.NewGuid().ToString("N"));
        private readonly JsonElement _contract;
        internal Fixture()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures/binocular-fourier-direct");
            var manifest = JsonFiles.Read<JsonElement>(Path.Combine(folder, "manifest.json"));
            var archive = Path.Combine(folder, "reference.zip");
            Assert.Equal(manifest.GetProperty("sha256").GetString(), FftFieldCaptureAudit.Hash(archive));
            ZipFile.ExtractToDirectory(archive, Root);
            _contract = JsonFiles.Read<JsonElement>(Path.Combine(Root, "contract.json"));
            Assert.Equal("ConvergenceCheckedDirectNativeBuffers", _contract.GetProperty("referenceBasis").GetString());
            Assert.Equal(FftFieldCaptureAudit.Hash(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")), _contract.GetProperty("policySha256").GetString());
        }
        internal Optic Optic(int config)
        {
            var input = _contract.GetProperty("inputs").EnumerateArray().Single(r => r.GetProperty("configuration").GetInt32() == config);
            var file = Path.Combine(Root, input.GetProperty("snapshot").GetString()!);
            Assert.Equal(input.GetProperty("snapshotSha256").GetString(), FftFieldCaptureAudit.Hash(file));
            Assert.Equal(input.GetProperty("sourceSha256").GetString(), FftFieldCaptureAudit.Hash(Path.Combine(Root, "inputs/Binocular_System.ZMX")));
            return Core.Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(file));
        }
        internal FftFieldCapture Native(int config, int n)
        {
            Assert.Equal(CaptureStatus.Captured, FftFieldCaptureAudit.Inspect(Path.Combine(Root, "direct"), $"c{config}-n{n}").Status);
            return FftFieldCaptureAudit.Read(Path.Combine(Root, $"direct/c{config}-n{n}"), config, n, Frequencies,
                FftFieldCaptureAudit.Hash(Path.Combine(Root, "inputs/Binocular_System.ZMX")));
        }
        public void Dispose()
        {
            var path = Path.GetFullPath(Root);
            var prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "binocular-direct-";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected fixture cleanup path");
            Directory.Delete(path, recursive: true);
        }
    }
}
