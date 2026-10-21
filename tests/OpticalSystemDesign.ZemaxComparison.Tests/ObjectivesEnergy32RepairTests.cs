using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class ObjectivesEnergy32RepairTests
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

    private static string Fixtures => Path.Combine(Root, "validation/zemax/2026-r1/objectives-energy32-repair-2026-10-21");
    private static string Policy => Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");

    public static IEnumerable<object[]> Cases()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "cases.json")));
        foreach (var item in manifest.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString()!, item.GetProperty("lens").GetString()!, item.GetProperty("sourceSha256").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original32FormalDisplayRepairsThreeDifferencesAndPreservesSevenPasses(string id, string lens, string sourceSha256)
    {
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
        Assert.Equal(sourceSha256, Hash(source));
        var capture = Path.Combine(Fixtures, id);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(capture, "request.json"));
        Assert.Equal(32, request.PupilSampling);
        Assert.Equal(64, request.ImageSampling);
        Assert.Equal("32", request.WorkbenchSettings["PupilSampling"]);
        Assert.Equal("64", request.WorkbenchSettings["ImageSampling"]);
        Assert.Equal("401", request.WorkbenchSettings["NumPoints"]);
        Assert.Equal("S_32x32", request.ZemaxSettings["SampleSize"].ToString());
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "objectives-energy32-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var path = Path.Combine(output, "data.json");
            JsonFiles.Write(path, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var actual = ResultNormalizer.Workbench(path, entry, request);
            var native = ResultNormalizer.Zemax(Path.Combine(capture, "data.json"), entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            var policy = ComparisonConfiguration.Load(Policy);
            ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{id}: {run.Conclusion}; " + string.Join("; ",
                run.Metrics.Select(m => $"{m.Id}: max={m.MaxAbsolute:R}, NRMSE={m.Nrmse:R}")));
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public void SellmeierFormula2MatchesAll136IndependentNativeIndicesExactly()
    {
        var count = 0;
        foreach (var item in Cases())
        {
            var id = (string)item[0];
            var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", (string)item[1]);
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
            using var indices = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, id, "refractive-indices.json")));
            foreach (var (surface, index) in optic.SurfaceGroup.Items.Select((s, i) => (s, i)))
            {
                if (surface.MaterialAfter is not CatalogGlassMaterial { Formula: "zemax formula 2" } glass) continue;
                for (var w = 0; w < optic.Wavelengths.Count; w++)
                {
                    var expected = Math.Abs(indices.RootElement[index].GetProperty("indices")[w].GetDouble());
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(glass.RefractiveIndex(optic.Wavelengths[w].Nanometers)));
                    count++;
                }
            }
        }
        Assert.Equal(136, count);
    }

    [Fact]
    public void OriginalPolicyAndFrozenNativeCaptureBytesAreUnchanged()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "cases.json")));
        Assert.Equal("30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5", Hash(Policy));
        Assert.Equal(Hash(Policy), cases.RootElement.GetProperty("originalComparisonPolicySha256").GetString());
        var rows = cases.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Count(r => r.GetProperty("original32Conclusion").GetString() == "Difference"));
        Assert.Equal(7, rows.Count(r => r.GetProperty("original32Conclusion").GetString() == "Pass"));
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
