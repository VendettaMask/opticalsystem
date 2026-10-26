using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalRepairNativeTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair");
    public static IEnumerable<object[]> OpdCases() => JsonFiles.Read<JsonElement>(Path.Combine(Root, "opd-manifest.json"))
        .EnumerateArray().Select(r => new object[] { r.GetProperty("id").GetString()! });

    [Theory]
    [MemberData(nameof(OpdCases))]
    public async Task AfocalOpdUsesTheCapturedPrimaryChiefPlaneAndOriginalWaveTolerance(string id)
    {
        var row = JsonFiles.Read<JsonElement>(Path.Combine(Root, "opd-manifest.json")).EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var file = Path.Combine(Root, "opd", id, "point-reference.json");
        Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        var native = JsonFiles.Read<JsonElement>(file);
        Assert.Equal("CurrentAndChief", native.GetProperty("opdMode").GetString());
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Root, "sources", row.GetProperty("lens").GetString()!))).Configurations[row.GetProperty("configuration").GetInt32() - 1];
        var inputs = native.GetProperty("inputs").EnumerateArray().ToArray();
        var pupils = inputs.Select(p => (p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble())).ToArray();
        var result = WavefrontEngine.GenerateChiefRaySamples(optic, (inputs[0].GetProperty("hx").GetDouble(), inputs[0].GetProperty("hy").GetDouble()),
            optic.Wavelengths[row.GetProperty("wavelength").GetInt32() - 1], pupils, aimAtStop: optic.RayAimingEnabled);
        var expected = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
        var tolerance = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses["Optical Path Difference"].Quantities["WavefrontError"];
        Assert.Equal(13, result.Samples.Count);
        for (var i = 0; i < result.Samples.Count; i++)
        {
            Assert.Equal(0, expected[i].GetProperty("error").GetInt32()); Assert.Equal(0, expected[i].GetProperty("vignette").GetInt32());
            Assert.True(result.Samples[i].Intensity > 0);
            var value = expected[i].GetProperty("opdWaves").GetDouble();
            Assert.InRange(Math.Abs(result.Samples[i].OpdWaves - value), 0, tolerance.Absolute + tolerance.Relative * Math.Abs(value));
        }
    }
    [Theory]
    [InlineData("Afocal/Afocal Riflescope.ZMX", 1, "MTF", false)]
    [InlineData("Afocal/Beam Expander.ZMX", 1, "MTF", false)]
    [InlineData("Afocal/Binocular_System.ZMX", 1, "MTF", false)]
    [InlineData("Afocal/Binocular_System.ZMX", 2, "MTF", false)]
    [InlineData("Miscellaneous/Apochromat_prism.zmx", 1, "MTF", false)]
    [InlineData("Afocal/Afocal Riflescope.ZMX", 1, "Field Curvature and Distortion", false)]
    [InlineData("Afocal/Beam Expander.ZMX", 1, "Field Curvature and Distortion", false)]
    [InlineData("Afocal/Binocular_System.ZMX", 1, "Field Curvature and Distortion", false)]
    [InlineData("Afocal/Binocular_System.ZMX", 2, "Field Curvature and Distortion", false)]
    [InlineData("Miscellaneous/Apochromat_prism.zmx", 1, "Field Curvature and Distortion", false)]
    [InlineData("Afocal/Afocal Riflescope.ZMX", 1, "MTF", true)]
    [InlineData("Afocal/Beam Expander.ZMX", 1, "MTF", true)]
    [InlineData("Afocal/Binocular_System.ZMX", 1, "MTF", true)]
    [InlineData("Afocal/Binocular_System.ZMX", 2, "MTF", true)]
    [InlineData("Miscellaneous/Apochromat_prism.zmx", 1, "MTF", true)]
    public async Task FreshSharedCoreMatchesLicensedNativeArraysWithoutChangingFocalTolerances(string lens, int configuration, string key, bool fullband)
    {
        var row = JsonFiles.Read<JsonElement>(Path.Combine(Root, fullband ? "fullband-manifest.json" : "manifest.json")).EnumerateArray()
            .Single(r => r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration && r.GetProperty("key").GetString() == key);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var native = Path.Combine(Root, row.GetProperty("id").GetString()!, "data.json");
        Assert.Equal(row.GetProperty("nativeSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(native))));
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Root, "sources", lens))).Configurations[configuration - 1];
        var folder = Path.Combine(Path.GetTempPath(), "afocal-native-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var data = Path.Combine(folder, "data.json"); JsonFiles.Write(data, new WorkbenchRuntime(optic).BuildAnalysisData(key, request.WorkbenchSettings));
            var entry = AnalysisComparisonRegistry.Get(key);
            var actual = ResultNormalizer.Workbench(data, entry, request); var expected = ResultNormalizer.Zemax(native, entry, request);
            var policy = ComparisonConfiguration.Load(Path.Combine(Root, "scoped-policy.json"));
            var settings = AfocalAnalysisContracts.ComparisonSettings(policy.Analyses[key], request);
            var run = new AnalysisRun { Key = key, Directory = folder, Request = request };
            ComparisonRunner.Compare(run, entry, settings, actual, expected, folder);
            Assert.Equal(Conclusion.Pass, run.Conclusion);
            Assert.All(run.Metrics, metric => Assert.Equal(Conclusion.Pass, metric.Conclusion));
            if (key == "Field Curvature and Distortion") Assert.Contains("diopters", run.Reason, StringComparison.Ordinal);
            if (key == "Field Curvature and Distortion")
            {
                Assert.Equal("0.0001", request.WorkbenchSettings["ParabasalDelta"]);
                Assert.Equal("False", request.WorkbenchSettings["ChiefRayParabasal"], ignoreCase: true);
                var defaults = new WorkbenchRuntime(optic).BuildAnalysisData("Field Curvature");
                Assert.Equal(1e-4, defaults.Values["ParabasalDelta"]);
                Assert.Equal("Symmetric", defaults.Values["ParabasalRayPair"]);
            }
            if (key == "MTF")
                Assert.Equal(ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses[key].Quantities["Modulation"], settings.Quantities["Modulation"]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AfocalCurvatureOperandsMatchIndependentNativeOperandReadback(int configuration)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Root, "sources/Afocal/Binocular_System.ZMX"))).Configurations[configuration - 1];
        var native = JsonFiles.Read<JsonElement>(Path.Combine(Root, "native-curvature-operands.json"));
        Assert.Equal(260127, native.GetProperty("version").GetInt32());
        foreach (var row in native.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("configuration").GetInt32() == configuration))
        {
            var actual = OpticalSystemDesign.Core.Services.FieldCurvatureMetrics.Evaluate(optic, optic.Wavelengths[1].Micrometers, 0,
                row.GetProperty("hy").GetDouble(), row.GetProperty("code").GetString() == "FCGS");
            Assert.InRange(Math.Abs(actual - row.GetProperty("value").GetDouble()), 0, 1e-6);
        }
    }

    [Theory]
    [InlineData("ArcSecond", "Milliradian", 0.00484813681109536)]
    [InlineData("Microradian", "Milliradian", 0.001)]
    [InlineData("CyclesPerArcSecond", "CyclesPerMilliradian", 206.26480624709637)]
    [InlineData("CyclesPerMicroradian", "CyclesPerMilliradian", 1000)]
    public void AddedAngularUnitsConvertOnlyWithinTheirPhysicalQuantity(string from, string to, double scale)
        => Assert.Equal(scale, PhysicalNormalization.UnitScale(from, to), 10);

    [Theory]
    [InlineData("Afocal/Afocal Riflescope.ZMX", 1)]
    [InlineData("Afocal/Beam Expander.ZMX", 1)]
    [InlineData("Afocal/Binocular_System.ZMX", 1)]
    [InlineData("Afocal/Binocular_System.ZMX", 2)]
    [InlineData("Miscellaneous/Apochromat_prism.zmx", 1)]
    public async Task AngularPercentageDistortionPassesTheOriginalPercentageGate(string lens, int configuration)
    {
        const string key = "Field Curvature and Distortion";
        var row = JsonFiles.Read<JsonElement>(Path.Combine(Root, "manifest.json")).EnumerateArray().Single(r => r.GetProperty("lens").GetString() == lens && r.GetProperty("configuration").GetInt32() == configuration && r.GetProperty("key").GetString() == key);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Root, "sources", lens))).Configurations[configuration - 1];
        var folder = Path.Combine(Path.GetTempPath(), "afocal-distortion-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "data.json"); JsonFiles.Write(file, new WorkbenchRuntime(optic).BuildAnalysisData(key, request.WorkbenchSettings));
            var entry = AnalysisComparisonRegistry.Get(key);
            var actual = ResultNormalizer.Workbench(file, entry, request).Series.Single(s => s.Id == "distortion");
            var expected = ResultNormalizer.Zemax(Path.Combine(Root, row.GetProperty("id").GetString()!, "data.json"), entry, request).Series.Single(s => s.Id == "distortion");
            var gate = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses[key].Quantities["Distortion"];
            var metric = ComparisonMetrics.Calculate("distortion", "Percent", ComparisonMetrics.Align(actual, expected, []), gate);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void DiopterGateIsExplicitAndCannotReplaceTheFocalDefocusGate()
    {
        var focal = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        var scoped = ComparisonConfiguration.Load(Path.Combine(Root, "scoped-policy.json")); scoped.Validate();
        foreach (var pair in focal.Analyses["Field Curvature and Distortion"].Quantities)
            Assert.Equal(pair.Value, scoped.Analyses["Field Curvature and Distortion"].Quantities[pair.Key]);
        Assert.Null(focal.Analyses["Field Curvature and Distortion"].AfocalQuantities);
        Assert.Equal(1e-6, scoped.Analyses["Field Curvature and Distortion"].AfocalQuantities!["Defocus"].Absolute);
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.UnitScale("Millimeter", "Diopter"));
    }
}
