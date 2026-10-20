using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class PetzvalNativeRepairTests
{
    [Theory]
    [InlineData("relative-illumination")]
    [InlineData("rms-vs-field")]
    public void FreshCalculationPassesTheUnchangedOriginalNativeReferenceAndTolerance(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root required.");
        var captures = Path.Combine(root, "validation/repairs/2026-10-20/petzval-illumination-rms");
        var manifest = JsonFiles.Read<JsonElement>(Path.Combine(captures, "manifest.json"));
        var source = Path.Combine(root, manifest.GetProperty("source").GetString()!);
        Assert.Equal(manifest.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        foreach (var file in manifest.GetProperty("files").EnumerateArray())
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(captures, file.GetProperty("path").GetString()!)))));

        var capture = Path.Combine(captures, name);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(capture, "request.json"));
        var policy = ComparisonConfiguration.Load(Path.Combine(root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "petzval-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var file = Path.Combine(output, "data.json");
            JsonFiles.Write(file, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var reference = ResultNormalizer.Zemax(Path.Combine(capture, "data.json"), entry, request);
            var actual = ResultNormalizer.Workbench(file, entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = name, Request = request };
            ComparisonRunner.Compare(run, entry, policy.Analyses[run.Key], actual, reference, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{run.Key}: {run.Conclusion}; "
                + string.Join("; ", run.Metrics.Select(metric => $"{metric.Id}: max={metric.MaxAbsolute:R}, NRMSE={metric.Nrmse:R}")));
        }
        finally { Directory.Delete(output, recursive: true); }
    }
}
