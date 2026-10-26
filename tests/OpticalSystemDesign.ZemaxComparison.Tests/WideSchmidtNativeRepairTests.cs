using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class WideSchmidtNativeRepairTests
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
    private static string Fixtures => Path.Combine(Root, "validation/zemax/2026-r1/wide-schmidt-repair-2026-10-21");
    private static string Policy => Path.Combine(Root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");

    public static IEnumerable<object[]> Cases()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "cases.json")));
        foreach (var item in cases.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("id").GetString()!, item.GetProperty("lens").GetString()!, item.GetProperty("sourceSha256").GetString()!];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void All29PreviouslyPassingRequestsPassTheirUnchangedNativePolicy(string id, string lens, string sourceSha256)
    {
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
        Assert.Equal(sourceSha256, Hash(source));
        var folder = Path.Combine(Fixtures, id);
        var request = JsonFiles.Read<CanonicalAnalysisRequest>(Path.Combine(folder, "request.json"));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
        var output = Path.Combine(Path.GetTempPath(), "wide-schmidt-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var path = Path.Combine(output, "data.json"); JsonFiles.Write(path, data);
            var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
            var actual = ResultNormalizer.Workbench(path, entry, request);
            var native = ResultNormalizer.Zemax(Path.Combine(folder, "data.json"), entry, request);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = id, Request = request };
            var policy = ComparisonConfiguration.Load(Policy);
            ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, native, output);
            Assert.True(run.Conclusion == Conclusion.Pass, $"{id}: {run.Conclusion}; " + string.Join("; ", run.Metrics.Select(m => $"{m.Id}: max={m.MaxAbsolute:R}, NRMSE={m.Nrmse:R}")));
        }
        finally { Directory.Delete(output, recursive: true); }
    }

    [Fact]
    public void ConicGaussianPupilContinuationPreservesThePreviouslyPassingGregorianRms()
    {
        using var guard = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "gregorian-rms-guard/source.json")));
        All29PreviouslyPassingRequestsPassTheirUnchangedNativePolicy("gregorian-rms-guard",
            guard.RootElement.GetProperty("lens").GetString()!, guard.RootElement.GetProperty("sourceSha256").GetString()!);
    }

    [Fact]
    public void SchmidtAnalyticAsphereNormalsAndAllFourImageMarginalsMatchIndependentNativeRays()
    {
        var source = Path.Combine(Root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Telescopes/Flat-field Schmidt.zmx");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "schmidt-marginals/ray-audit.json")));
        using var bits = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "schmidt-marginals/ray-audit.json.bits.json")));
        var surfaces = native.RootElement.GetProperty("surfaces");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, optic.Wavelengths[1].Micrometers,
            new PupilSample[] { new(0, 1, 1), new(0, -1, 1), new(1, 0, 1), new(-1, 0, 1) }, aimAtStop: false);
        var traced = optic.SequentialRayTracer.Trace(bundle).RayHistories;
        for (var i = 1; i <= 4; i++)
        {
            var point = new Vector3D(Value(1, i, "x"), Value(1, i, "y"), Value(1, i, "z"));
            var normal = optic.SurfaceGroup.Items[2].Geometry.SurfaceNormal(point);
            var expected = new Vector3D(Value(1, i, "nx"), Value(1, i, "ny"), Value(1, i, "nz"));
            Assert.InRange((normal + expected).Length, 0, 5e-16);
            var image = traced[i - 1][^1];
            Assert.Equal(optic.SurfaceGroup.Items.Count - 1, image.SurfaceNumber);
            Assert.False(image.Vignetted);
            // Native signed-index directions reverse after the explicit MIRROR
            // row. Core retains physical propagation in its global frame.
            var direction = new Vector3D(Value(surfaces.GetArrayLength() - 1, i, "l"), Value(surfaces.GetArrayLength() - 1, i, "m"), Value(surfaces.GetArrayLength() - 1, i, "n"));
            Assert.InRange((image.Direction + direction).Length, 0, 1e-14);
        }
        double Value(int surface, int pupil, string key) => BitConverter.Int64BitsToDouble(
            unchecked((long)Convert.ToUInt64(bits.RootElement.GetProperty($"/surfaces/{surface}/rays/{pupil}/{key}").GetString(), 16)));
    }

    [Fact]
    public void Exact29RequestUnionAndOriginalPolicyAndCapturesRemainFrozen()
    {
        Assert.Equal("30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5", Hash(Policy));
        var cases = Cases().ToArray();
        Assert.Equal(29, cases.Length);
        Assert.Equal(15, cases.Count(c => ((string)c[1]).Contains("200 degree")));
        Assert.Equal(13, cases.Count(c => ((string)c[1]).Contains("210 degree")));
        Assert.Single(cases, c => ((string)c[1]).Contains("Schmidt"));
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
