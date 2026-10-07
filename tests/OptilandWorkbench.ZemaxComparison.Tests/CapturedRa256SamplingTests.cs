using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Normalization;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Workbench;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class CapturedRa256SamplingTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ra256-field-sampling");
    public static IEnumerable<object[]> Captures()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        return manifest.RootElement.GetProperty("cases").EnumerateArray()
            .Select(c => new object[] { c.GetProperty("name").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void SixNativeRa256CurvesPassTheUnchangedRadiusGate(string name)
    {
        var directory = Path.Combine(Root, name);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(directory, "canonical-request.json"));
        Assert.Equal("RA", request.WorkbenchSettings["Method"]);
        Assert.Equal("256", request.WorkbenchSettings["RayDensity"]);
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
        Assert.Equal(16, metric.Count);
    }

    [Fact]
    public void AllRa256CaptureFilesKeepTheirOriginalHashesAndSources()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        Assert.Equal(6, manifest.RootElement.GetProperty("cases").GetArrayLength());
        foreach (var item in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, item.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(item.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(path)));
        }
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task FreshOfficialSourceImportsMatchNativeRa256Curves(string name)
    {
        var directory = Path.Combine(Root, name);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(directory, "canonical-request.json"));
        var imported = await WorkbenchExecutor.Import(Path.Combine(directory, "source.zmx"));
        var optic = imported.Configurations[request.Configuration - 1];
        var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey,
            AnalysisComparisonRegistry.MapWorkbench(entry, request));
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var wb = ExtendedResultNormalizer.Workbench(data, request);
        var native = ExtendedResultNormalizer.Zemax(Path.Combine(directory, "data.json"), request);
        var metric = ComparisonMetrics.Calculate("curve:0", "Micrometer",
            ComparisonMetrics.Align(wb.Series.Single(), native.Series.Single(), []), entry.DefaultTolerances["Radius"]);
        Assert.Equal(Conclusion.Pass, metric.Conclusion);
        Assert.Equal(16, metric.Count);
        if (name == "tessar-lens-using-vignetting-factors-ra-256-remove")
        {
            var stop = Assert.Single(optic.SurfaceGroup.Items, s => s.IsStop);
            Assert.False(stop.SemiDiameterFixed);
            Assert.Null(stop.PhysicalAperture);
            // The old imported stop mask rejected 46 native accepted rays and
            // caused a 0.111891 micrometer error. Native data and gate stay frozen.
            Assert.InRange(metric.MaxAbsolute, 0, 1e-6);
        }
    }
}
