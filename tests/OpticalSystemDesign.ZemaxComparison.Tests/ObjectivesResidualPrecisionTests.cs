using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ObjectivesResidualPrecisionTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpticalSystemDesign.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root required.");
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["double-gauss5-psf32"];
        yield return ["double-gauss5-psf-cross32"];
        using var acceptance = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
            "validation/zemax/2026-r1/objectives-energy128-acceptance-2026-10-21/acceptance.json")));
        foreach (var item in acceptance.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FormalDisplayMatchesIndependentNativeCaptureAtOriginalTolerances(string id)
    {
        var folder = Path.Combine(Root, "validation/zemax/2026-r1/objectives-residuals-2026-10-21");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "cases.json")));
        var selected = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", selected.GetProperty("lens").GetString()!);
        Assert.Equal(selected.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
        var capture = Path.Combine(folder, id);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(capture, "request.json"));
        if (request.CanonicalAnalysisKey == "Diffraction Encircled Energy")
        {
            Assert.Equal(128, request.PupilSampling);
            Assert.Equal(256, request.ImageSampling);
            Assert.Equal("128", request.WorkbenchSettings["PupilSampling"]);
            Assert.Equal("256", request.WorkbenchSettings["ImageSampling"]);
            Assert.Equal("S_128x128", request.ZemaxSettings["SampleSize"].ToString());
        }
        var policy = ComparisonConfiguration.Load(Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json"));
        var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "objectives-residual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            JsonFiles.Write(Path.Combine(output, "data.json"), data);
            var native = ResultNormalizer.Zemax(Path.Combine(capture, "data.json"), entry, request);
            var actual = ResultNormalizer.Workbench(Path.Combine(output, "data.json"), entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{id}: {run.Conclusion}; " + string.Join("; ",
                run.Metrics.Select(m => $"{m.Id}: max={m.MaxAbsolute:R}, NRMSE={m.Nrmse:R}")));
            if (request.CanonicalAnalysisKey == "PSF")
            {
                var values = actual.Grids.Single().Z.SelectMany(row => row).Select(v => v!.Value).ToArray();
                var expected = native.Grids.Single().Z.SelectMany(row => row).Select(v => v!.Value).ToArray();
                var relativeL2 = Math.Sqrt(values.Zip(expected, (a, b) => (a - b) * (a - b)).Sum() / expected.Sum(v => v * v));
                Assert.True(relativeL2 <= 1e-6, $"Native scalar FFT amplitude limit 1e-6: {relativeL2:R}");
            }
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public void IndependentNativeCaptureBytesRetainTheirRecordedHashes()
    {
        using var acceptance = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
            "validation/zemax/2026-r1/objectives-energy128-acceptance-2026-10-21/acceptance.json")));
        Assert.Equal("新增 128 档验收，32 档继续记为未解决", acceptance.RootElement.GetProperty("userDecision").GetString());
        Assert.False(acceptance.RootElement.GetProperty("replacesOriginal32").GetBoolean());
        Assert.Equal(3, acceptance.RootElement.GetProperty("cases").GetArrayLength());
        var policy = Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
        Assert.Equal(acceptance.RootElement.GetProperty("originalComparisonPolicySha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(policy))));
        foreach (var item in acceptance.RootElement.GetProperty("cases").EnumerateArray())
        {
            Assert.Equal("Unresolved", item.GetProperty("original32Status").GetString());
            Assert.Equal("Difference", item.GetProperty("original32Conclusion").GetString());
        }
        var folder = Path.Combine(Root, "validation/zemax/2026-r1/objectives-residuals-2026-10-21");
        Assert.Equal(acceptance.RootElement.GetProperty("preservedCaptureManifestSha256").GetString(),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "manifest.json")))));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(folder, file.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, path);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }
}
