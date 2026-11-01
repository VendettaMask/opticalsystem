using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalCentroidEnergyPointwiseTests(ITestOutputHelper output)
{
    private static JsonElement Manifest() => JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-centroid-energy-requests.json"));
    public static IEnumerable<object[]> Cases() => Manifest().GetProperty("cases").EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });
    public static IEnumerable<object[]> PhysicalCases() => Manifest().GetProperty("cases").EnumerateArray()
        .Where(r => r.GetProperty("originalConclusion").GetString() == "Difference").Select(r => new object[] { r.GetProperty("id").GetString()! });
    private static readonly Lazy<bool> ArchiveVerified = new(() =>
    {
        using var file = File.OpenRead(ArchivePath);
        Assert.Equal(Manifest().GetProperty("sourceArchiveSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(file)));
        return true;
    });
    private static string ArchivePath => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract/source-evidence.zip");
    private static (Optic Optic, CanonicalAnalysisRequest Request, JsonElement Row) Load(string id)
    {
        Assert.True(ArchiveVerified.Value);
        var row = Manifest().GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.Equal(row.GetProperty("fingerprint").GetString(), request.Fingerprint);
        using var zip = ZipFile.OpenRead(ArchivePath);
        var model = Regex.Match(id, @"^\d{4}-(?<model>.+?)-primary-").Groups["model"].Value;
        using var input = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/" + model + ".json")!.Open();
        using var bytes = new MemoryStream(); input.CopyTo(bytes);
        Assert.Equal(row.GetProperty("snapshotSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        return (Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(bytes.ToArray(), JsonFiles.Options)!), request, row);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OriginalCentroidPlotMeetsEveryOriginalPointwiseGate(string id)
    {
        var (optic, request, row) = Load(id);
        var directory = Path.Combine(Path.GetTempPath(), "afocal-centroid-energy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var zip = ZipFile.OpenRead(ArchivePath);
            foreach (var name in new[] { "data.json", "data.txt", "model.json", "request.json", "captured-settings.json" })
            {
                using var source = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/expanded/" + id + "/native/" + name)!.Open();
                using var destination = File.Create(Path.Combine(directory, name)); source.CopyTo(destination);
            }
            using (var native = File.OpenRead(Path.Combine(directory, "data.json")))
                Assert.Equal(row.GetProperty("nativeDataSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(native)));
            var data = new WorkbenchRuntime(optic).BuildAnalysisData("Encircled Energy", request.WorkbenchSettings);
            Assert.Equal("centroid", data.Values["Reference"]);
            Assert.Equal("Native2026R1AfocalReferenceRay", data.Values["ReferenceConvention"]);
            Assert.Equal("chief", data.Values["EffectiveAngularReference"]);
            Assert.Equal("OpenBoundary", data.Values["ZeroRadiusPlotConvention"]);
            var path = Path.Combine(directory, "workbench.json"); JsonFiles.Write(path, data);
            var entry = AnalysisComparisonRegistry.Get("Encircled Energy");
            var actual = ResultNormalizer.Workbench(path, entry, request);
            var expected = ResultNormalizer.Zemax(Path.Combine(directory, "data.json"), entry, request);
            var gate = row.GetProperty("effectiveTolerances").GetProperty("EnergyFraction").Deserialize<Tolerances>(JsonFiles.Options)!;
            var count = 0; var maximum = 0d;
            foreach (var curve in expected.Series)
            {
                var matched = ComparisonMetrics.Align(actual.Series.Single(s => s.Id == curve.Id), curve, []);
                Assert.Equal(Conclusion.Pass, ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit, matched, gate).Conclusion);
                foreach (var pair in matched.Workbench.Zip(matched.Zemax))
                {
                    var error = Math.Abs(pair.First - pair.Second); maximum = Math.Max(maximum, error); count++;
                    Assert.InRange(error, 0, gate.Absolute + gate.Relative * Math.Abs(pair.Second));
                }
            }
            output.WriteLine(JsonSerializer.Serialize(new { id, request.Fingerprint, comparedValues = count, violations = 0, maximum }));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [MemberData(nameof(PhysicalCases))]
    public void NativePlotReferenceIsExplicitWhilePhysicalAngularCentroidKeepsItsDefinition(string id)
    {
        var (optic, request, _) = Load(id);
        var runtime = new WorkbenchRuntime(optic);
        var native = runtime.BuildAnalysisData("Encircled Energy", request.WorkbenchSettings);
        var settings = new Dictionary<string, string>(request.WorkbenchSettings) { ["UseNativeAfocalReferenceRay"] = "False" };
        var physical = runtime.BuildAnalysisData("Encircled Energy", settings);
        Assert.Equal("PhysicalCoordinates", physical.Values["ReferenceConvention"]);
        Assert.Equal("ClosedCumulative", physical.Values["ZeroRadiusPlotConvention"]);
        var direct = new EncircledEnergyAnalysis(optic, 32, "uniform", 401, request.Wavelength, "centroid",
            double.Parse(settings["MaximumDistanceMicrometers"], CultureInfo.InvariantCulture), false)
            { ZemaxCompatibleOutput = true }.GenerateData();
        var difference = native.PlotSeries.Zip(physical.PlotSeries, (a, b) => a.Points.Zip(b.Points, (x, y) => Math.Abs(x.Y - y.Y)).Max()).Max();
        Assert.True(difference > .1, "This physical counterexample must retain the distinct angular centroid.");
        for (var field = 0; field < physical.PlotSeries.Count; field++) Assert.Equal(direct.PlotSeries[field].Points, physical.PlotSeries[field].Points);
        foreach (var reference in new[] { "chief", "vertex" })
        {
            var selected = new Dictionary<string, string>(request.WorkbenchSettings) { ["Reference"] = reference };
            var plotted = runtime.BuildAnalysisData("Encircled Energy", selected);
            Assert.Equal(reference, plotted.Values["Reference"]);
            for (var field = 0; field < plotted.PlotSeries.Count; field++) Assert.Equal(native.PlotSeries[field].Points, plotted.PlotSeries[field].Points);
        }
    }
    [Fact]
    public void ABlockedReferenceRayCannotAcquireACentroidDirectionInNativeMode()
    {
        var (optic, _, _) = Load("0013-001-afocal-riflescope-c1-primary-f1-w2-encircled-energy");
        optic.RayAimingEnabled = false;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new AnnularAperture(9, .05);
        var physical = new EncircledEnergyAnalysis(optic, 32, "uniform", 100, 2,
            multiplyByDiffractionLimit: false).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, physical.Outcome);
        var native = new EncircledEnergyAnalysis(optic, 32, "uniform", 100, 2,
            multiplyByDiffractionLimit: false) { ZemaxCompatibleOutput = true, UseNativeAfocalReferenceRay = true };
        Assert.Throws<AnalysisDataUnavailableException>(() => native.GenerateData());
    }
}
