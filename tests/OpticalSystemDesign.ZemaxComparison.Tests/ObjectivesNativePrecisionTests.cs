using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ObjectivesNativePrecisionTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root is required for native controls.");
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "validation/zemax/2026-r1/objectives-2026-10-20/captures/cases.json")));
        return cases.RootElement.EnumerateArray().Where(item => item.GetProperty("requiredPass").GetBoolean())
            .Select(item => new object[] { item.GetProperty("id").GetString()!,
            item.GetProperty("lens").GetString()!, item.GetProperty("sourceSha256").GetString()! }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RecalculationMatchesIndependentNativeCaptureAtOriginalTolerances(string id, string lens, string sourceSha256)
    {
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
        Assert.Equal(sourceSha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        var capture = Path.Combine(Root, "validation/zemax/2026-r1/objectives-2026-10-20/captures", id);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(capture, "request.json"));
        var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
        var policy = ComparisonConfiguration.Load(Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "objectives-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var file = Path.Combine(output, "data.json");
            JsonFiles.Write(file, data);
            var native = ResultNormalizer.Zemax(Path.Combine(capture, "data.json"), entry, request);
            var actual = ResultNormalizer.Workbench(file, entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{lens}/{request.CanonicalAnalysisKey}: {run.Conclusion}; {run.Reason}; "
                + string.Join("; ", run.Metrics.Select(metric => $"{metric.Id}: max={metric.MaxAbsolute:R}, NRMSE={metric.Nrmse:R}")));
        }
        finally { Directory.Delete(output, recursive: true); }
    }
}
