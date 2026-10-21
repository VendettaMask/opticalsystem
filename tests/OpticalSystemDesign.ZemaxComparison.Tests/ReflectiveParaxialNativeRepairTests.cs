using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ReflectiveParaxialNativeRepairTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpticalSystemDesign.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root required.");
        }
    }
    private static string Fixtures => Path.Combine(Root, "validation/zemax/2026-r1/reflective-paraxial-repair-2026-10-21");
    private static string Policy => Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");

    public static IEnumerable<object[]> Cases()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "cases.json")));
        foreach (var row in cases.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("id").GetString()!, row.GetProperty("lens").GetString()!, row.GetProperty("sourceSha256").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ElevenRepairedFirstOrderRequestsPassTheirOriginalNativePolicy(string id, string lens, string sourceSha256)
    {
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
        Assert.Equal(sourceSha256, Hash(source));
        var folder = Path.Combine(Fixtures, id);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder, "request.json"));
        var optic = (await WorkbenchExecutor.Import(source)).Configurations[request.Configuration - 1];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "reflective-paraxial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var path = Path.Combine(output, "data.json"); JsonFiles.Write(path, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var actual = ResultNormalizer.Workbench(path, entry, request);
            var native = ResultNormalizer.Zemax(Path.Combine(folder, "data.json"), entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            ComparisonRunner.Compare(run, entry, ComparisonConfiguration.Load(Policy).Analyses[request.CanonicalAnalysisKey], actual, native, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{id}: {run.Conclusion}; " + string.Join("; ", run.Metrics.Select(m => $"{m.Id}: max={m.MaxAbsolute:R}, NRMSE={m.Nrmse:R}")));
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public void CapturedRequestsSourcesAndOriginalTolerancesRemainExact()
    {
        Assert.Equal("30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5", Hash(Policy));
        Assert.Equal(11, Cases().Count());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Fixtures, file.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Fixtures) + Path.DirectorySeparatorChar, path);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(), Hash(path));
        }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
