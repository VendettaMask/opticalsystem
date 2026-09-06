using System.Text.Json;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Reporting;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class ExpansionContractTests
{
    [Fact]
    public void WindowsMetricNamesProduceOrdinaryFilesAndPlots()
    {
        var directory = Path.Combine(Path.GetTempPath(), "zemax-filenames-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var slug = JsonFiles.Slug("curve:0/phase:sin");
            Assert.DoesNotContain(':', slug);
            Assert.DoesNotContain('/', slug);
            var values = new OptilandWorkbench.ZemaxComparison.Metrics.MatchedValues([0, 1], null, [0, 1], [0, 1], 1);
            ReportWriter.Values(Path.Combine(directory, slug + "-values.csv"), values);
            PlotWriter.Curves(directory, slug, values, "Pupil", "Phase sine");
            var files = Directory.GetFiles(directory).Select(Path.GetFileName).ToArray();
            Assert.Contains(slug + "-values.csv", files);
            Assert.Contains(files, f => f!.EndsWith(".png", StringComparison.Ordinal));
            Assert.All(files, f => Assert.True(new FileInfo(Path.Combine(directory, f!)).Length > 0));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Vignetting Diagram")]
    [InlineData("Foucault Analysis")]
    [InlineData("Partially Coherent Image Analysis")]
    public void DifferentPhysicalDefinitionsCannotBecomeNumericPasses(string key)
    {
        var entry = AnalysisComparisonRegistry.Get(key);
        Assert.Equal(SupportStatus.PhysicalDefinitionMismatch, entry.Support);
        Assert.Equal("capability-audit", entry.ZemaxSettingsMapper);
        Assert.Empty(entry.DefaultMetrics);
        var request = ExtendedAnalysisContracts.Configure(entry, new()
        { CanonicalAnalysisKey = key, Apodization = "none", WorkbenchSettings = [] }, 1);
        Assert.Equal("NativeCapabilityInspectionNotAligned", request.SettingsOrigin);
    }

    [Fact]
    public void ChannelAuditIncludesScatterRgbRaysAndSpotData()
    {
        using var doc = JsonDocument.Parse("""{"dataGridsRgb":[{}],"dataScatterPoints":[{},{}],"rayData":[{}],"spotMetrics":[{}]}""");
        var counts = NativeResultChannels.Count(doc.RootElement);
        Assert.Equal(8, counts.Count);
        Assert.Equal(2, counts["dataScatterPoints"]);
        Assert.Equal(1, counts["dataGridsRgb"]);
        Assert.Equal(1, counts["rayData"]);
        Assert.Equal(1, counts["spotMetrics"]);
        Assert.Equal(0, counts["dataSeries"]);
    }

    [Fact]
    public void NativeFixtureManifestDetectsAnyAlteredCapture()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis-expansion");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        Assert.Equal(manifest.RootElement.GetProperty("sourceSha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(Path.Combine(root, "source.ZMX"))));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(root, item.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(item.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(path)));
        }
    }
}
