using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Metrics;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class CapturedRmsFieldSamplingTests
{
    public static IEnumerable<object[]> Captures()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        return manifest.RootElement.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray();
    }
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "rms-field-sampling");

    [Theory]
    [MemberData(nameof(Captures))]
    public void ActualNativeReferencesRetainTheStrictGateAfterTessarIntegralRepair(string name)
    {
        var directory = Path.Combine(Root, name);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(directory, "canonical-request.json"));
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(Path.Combine(directory, "snapshot.json")));
        var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey,
            AnalysisComparisonRegistry.MapWorkbench(entry, request));
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var wb = ExtendedResultNormalizer.Workbench(data, request);
        var native = ExtendedResultNormalizer.Zemax(Path.Combine(directory, "data.json"), request);
        var metric = ComparisonMetrics.Calculate("curve:0", "Micrometer",
            ComparisonMetrics.Align(wb.Series.Single(), native.Series.Single(), []), entry.DefaultTolerances["Radius"]);
        Assert.Equal(Conclusion.Pass, metric.Conclusion);
        Assert.Equal(1, metric.Coverage);
    }

    [Fact]
    public void EveryCapturedFileAndOfficialSourceMatchesItsManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, item.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(item.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(path)));
        }
        foreach (var item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            Assert.Equal(item.GetProperty("sourceSha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(
                Path.Combine(Root, item.GetProperty("name").GetString()!, "source.zmx"))));
    }
}
