using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalAnalysisRepairTests
{
    private static string Sources => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair/sources");
    private static JsonElement Fixture(string name) => JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-analysis-repair", name));
    public static IEnumerable<object[]> OpdCases() => Fixture("opd.json").GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    public static IEnumerable<object[]> GaussianCases() => Fixture("gaussian-spot.json").GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("factor").GetDouble(), row.GetProperty("density").GetInt32() });

    [Theory]
    [MemberData(nameof(OpdCases))]
    public async Task SelectedWavelengthOpdFansMatchIndependentNativeCurves(string id)
    {
        Assert.Equal(260127, Fixture("opd.json").GetProperty("version").GetInt32());
        var row = Fixture("opd.json").GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, row.GetProperty("lens").GetString()!))).Configurations[row.GetProperty("configuration").GetInt32() - 1];
        var data = new OpticalPathDifferenceAnalysis(optic, wavelengthNumber: row.GetProperty("wave").GetInt32(), fieldNumber: row.GetProperty("field").GetInt32()).GenerateData();
        Assert.Equal(2, data.PlotPanes!.Count);
        for (var fan = 0; fan < 2; fan++)
        {
            var native = row.GetProperty("fans")[fan];
            var actual = Assert.Single(data.PlotPanes[fan].Series).Points;
            Assert.Equal(native.GetProperty("x").GetArrayLength(), actual.Count);
            var actualValues = new List<double>();
            var expectedValues = new List<double>();
            for (var i = 0; i < actual.Count; i++)
            {
                Assert.Equal(native.GetProperty("x")[i].GetDouble(), actual[i].X, 10);
                var value = native.GetProperty("y")[i];
                if (value.ValueKind == JsonValueKind.Null) { Assert.False(double.IsFinite(actual[i].Y)); continue; }
                actualValues.Add(actual[i].Y);
                expectedValues.Add(value.GetDouble());
            }
            var gate = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json")).Analyses["Optical Path Difference"].Quantities["WavefrontError"];
            var metric = ComparisonMetrics.Calculate("opd", "Wave", new MatchedValues(Enumerable.Range(0, actualValues.Count).Select(i => (double)i).ToArray(), null, actualValues.ToArray(), expectedValues.ToArray(), 1), gate);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
    }

    [Theory]
    [MemberData(nameof(GaussianCases))]
    public async Task GaussianImportanceSampledSpotsMatchNativeAcrossFactorsAndDensities(double factor, int density)
    {
        Assert.Equal(260127, Fixture("gaussian-spot.json").GetProperty("version").GetInt32());
        var expected = Fixture("gaussian-spot.json").GetProperty("cases").EnumerateArray().Single(r => r.GetProperty("factor").GetDouble() == factor && r.GetProperty("density").GetInt32() == density);
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, "Afocal/Beam Expander.ZMX"))).ActiveOptic;
        optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, factor);
        var data = new SpotDiagramAnalysis(optic, new SpotDiagramSettings(RayDensity: density, WavelengthNumber: 1, FieldNumber: 1, Reference: "chief")).GenerateData();
        var metrics = Assert.Single(data.PlotPanes!).Metrics!;
        Assert.InRange(Math.Abs(metrics[0].Value - expected.GetProperty("rmsMilliradian").GetDouble()), 0, 1e-10);
        Assert.InRange(Math.Abs(metrics[1].Value - expected.GetProperty("geoMilliradian").GetDouble()), 0, 1e-10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationHuygensSpacingKeepsThePhysicalUnit(bool afocal)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, "Afocal/Beam Expander.ZMX"))).ActiveOptic;
        optic.ImageSpaceAfocal = afocal;
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens PSF", new Dictionary<string, string>
        {
            ["PupilSampling"] = "8", ["ImageSampling"] = "8", ["ImageDeltaMicrometers"] = "0.05", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1"
        });
        Assert.Equal(.05, Convert.ToDouble(data.Values[afocal ? "PixelPitchMilliradians" : "PixelPitchMicrometers"]), 12);
        Assert.All(data.PlotSeries, series => Assert.Equal(afocal ? AnalysisAxisUnit.Milliradian : AnalysisAxisUnit.Micrometer, series.XUnit));
    }

    [Fact]
    public async Task AfocalHuygensThroughFocusActuallyChangesWithDefocus()
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, "Afocal/Beam Expander.ZMX"))).ActiveOptic;
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens Through Focus MTF", new Dictionary<string, string>
        {
            ["PupilSampling"] = "16", ["ImageSampling"] = "32", ["ImageDeltaMicrometers"] = "0.005", ["SpatialFrequency"] = "1.7188733853924696",
            ["DeltaFocus"] = "0.01", ["Steps"] = "5", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1"
        });
        var curve = data.PlotSeries[0];
        Assert.Equal(AnalysisAxisUnit.Diopter, curve.XUnit);
        Assert.True(curve.Points.Max(p => p.Y) - curve.Points.Min(p => p.Y) > .01);
    }

    [Theory]
    [InlineData("Encircled Energy")]
    [InlineData("Diffraction Encircled Energy")]
    [InlineData("FFT PSF Cross Section")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("RMS vs Field")]
    public async Task AfocalDerivedAnalysisRetainsAngularAxes(string key)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, "Afocal/Beam Expander.ZMX"))).ActiveOptic;
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(key, new Dictionary<string, string>
        {
            ["PupilSampling"] = "32", ["ImageSampling"] = "64", ["Sampling"] = "32", ["NumRays"] = "32", ["FieldDensity"] = "3",
            ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["MaximumDistanceMicrometers"] = "0.1", ["Data"] = "spot"
        });
        Assert.NotEmpty(data.PlotSeries);
        Assert.All(data.PlotSeries, series => Assert.Equal(AnalysisAxisUnit.Milliradian, key == "RMS vs Field" ? series.YUnit : series.XUnit));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MixedModeNumericTraceSurvivesUnavailableLayout(int configuration)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(Sources, "Afocal/Binocular_System.ZMX"))).Configurations[configuration - 1];
        var data = new SingleRayTraceAnalysis(optic, py: .8, wavelengthNumber: 2).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.NotNull(data.Table);
        Assert.NotEmpty((double[][])data.Values["RealRayData"]);
        Assert.False((bool)data.Values["LayoutAvailable"]);
        Assert.NotEmpty((string)data.Values["LayoutUnavailableReason"]);
        Assert.Empty(data.PlotSeries);
        Assert.False((bool)new FirstOrderAnalysis(optic).GenerateData().Values["CardinalParametersApplicable"]);
    }
}
