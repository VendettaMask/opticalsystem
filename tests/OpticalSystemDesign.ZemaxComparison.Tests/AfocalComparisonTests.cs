using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalComparisonTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-riflescope");
    private static string Source => File.ReadAllText(Path.Combine(Root, "source.ZMX"));
    private static string Data(string id, string side) => Path.Combine(Root, id, side, "data.json");
    private static CanonicalAnalysisRequest Request(string id) => AfocalAnalysisContracts.Configure(
        JsonFiles.Read<WorkbenchJob>(Path.Combine(Root, id, "workbench/request.json")).Request, true, Source);

    [Fact]
    public void FocalRequestFingerprintAndDefaultPolicyRemainUnchanged()
    {
        var file = Path.Combine(Root, "ray-fan-axis-w1/workbench/request.json");
        using var old = JsonDocument.Parse(File.ReadAllText(file));
        var request = JsonFiles.Read<WorkbenchJob>(file).Request;
        Assert.Equal(JsonFiles.CanonicalHash(old.RootElement.GetProperty("request")), request.Fingerprint);
        var json = JsonSerializer.Serialize(request, JsonFiles.RequestOptions);
        Assert.DoesNotContain("imageSpaceAfocal", json);
        Assert.DoesNotContain("nativeAfocalAngleUnit", json);
        var config = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        config.Validate();
        Assert.Null(config.Analyses["Ray Fan"].AfocalQuantities);
        Assert.Same(config.Analyses["MTF"], AfocalAnalysisContracts.ComparisonSettings(config.Analyses["MTF"], request));
    }

    [Theory]
    [InlineData("ArcMinute", "Milliradian", 0.2908882086657216)]
    [InlineData("ArcMinute", "Degree", 0.016666666666666666)]
    [InlineData("CyclesPerArcMinute", "CyclesPerMilliradian", 3.437746770784939)]
    public void ConvertsOnlyCompatibleTypedAngularUnits(string from, string to, double scale)
        => Assert.Equal(scale, PhysicalNormalization.UnitScale(from, to), 12);

    [Fact]
    public void AfocalGateIsPerAnalysisAndPowerCannotBecomeLength()
    {
        Assert.Null(AfocalAnalysisContracts.BlockReason("MM", true, "Ray Fan"));
        Assert.Null(AfocalAnalysisContracts.BlockReason("MM", true, "MTF"));
        Assert.NotNull(AfocalAnalysisContracts.BlockReason("MM", true, "PSF"));
        Assert.NotNull(AfocalAnalysisContracts.BlockReason("IN", false, "MTF"));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.UnitScale("Millimeter", "Diopter"));
        Assert.Throws<InvalidDataException>(() => PhysicalNormalization.UnitScale("Micrometer", "Degree"));
    }

    [Fact]
    public void AngularRayFanRequiresSeparateExplicitAngleTolerance()
    {
        var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        var settings = policy.Analyses["Ray Fan"];
        Assert.Throws<InvalidDataException>(() => AfocalAnalysisContracts.ComparisonSettings(settings, Request("ray-fan-axis-w1")));
        var angle = policy.Analyses["Single Ray Trace"].Quantities["IncidentAngle"];
        var explicitSettings = settings with { AfocalQuantities = new() { ["IncidentAngle"] = angle } };
        var mapped = AfocalAnalysisContracts.ComparisonSettings(explicitSettings, Request("ray-fan-axis-w1"));
        Assert.Equal(angle, Assert.Single(mapped.Quantities).Value);
        Assert.Same(settings.Quantities, explicitSettings.Quantities);
    }

    [Theory]
    [InlineData("ray-fan-axis-w1")]
    [InlineData("ray-fan-edge-w1")]
    public void ActualRiflescopeRayFanMatchesIndependentNativeAngularCapture(string id)
    {
        var request = Request(id);
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");
        var fresh = new WorkbenchRuntime(optic).BuildAnalysisData("Ray Fan", request.WorkbenchSettings);
        var file = Path.Combine(Path.GetTempPath(), "afocal-fan-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonFiles.Write(file, fresh);
            var entry = AnalysisComparisonRegistry.Get("Ray Fan");
            var actual = ResultNormalizer.Workbench(file, entry, request);
            var reference = ResultNormalizer.Zemax(Data(id, "native"), entry, request);
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
            var tolerance = policy.Analyses["Single Ray Trace"].Quantities["IncidentAngle"];
            Assert.Equal(2, reference.Series.Count);
            foreach (var curve in reference.Series)
            {
                Assert.Equal(new Axis("IncidentAngle", "Degree"), curve.YAxis);
                var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit,
                    ComparisonMetrics.Align(actual.Series.Single(item => item.Id == curve.Id), curve, []), tolerance);
                Assert.Equal(Conclusion.Pass, metric.Conclusion);
            }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PolychromaticMtfRetainsAllFieldsAndCanonicalFrequency()
    {
        var request = Request("fft-mtf-all-fields-poly64");
        var entry = AnalysisComparisonRegistry.Get("MTF");
        var actual = ResultNormalizer.Workbench(Data("fft-mtf-all-fields-poly64", "workbench"), entry, request);
        var native = ResultNormalizer.Zemax(Data("fft-mtf-all-fields-poly64", "native"), entry, request);
        Assert.Equal(6, actual.Series.Count);
        Assert.Equal(actual.Series.Select(item => item.Id), native.Series.Select(item => item.Id));
        Assert.All(native.Series, curve =>
        {
            Assert.Equal("CyclesPerMilliradian", curve.XAxis.Unit);
            Assert.Equal(request.MaximumFrequency, curve.X[^1], 12);
        });
        var payload = new Dictionary<string, object?>();
        AfocalAnalysisContracts.MapNativePayload(payload, request);
        Assert.Equal(0.5, Assert.IsType<double>(payload["maximumFrequency"]), 12);
        Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(Data("fft-mtf-all-fields-poly64", "native"), entry,
            request with { NativeAfocalAngleUnit = "Milliradian" }));
        Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(Data("fft-mtf-all-fields-poly64", "native"), entry,
            request with { MaximumFrequency = request.MaximumFrequency * 2 }));
        Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(Data("fft-mtf-all-fields-poly64", "native"), entry,
            request with { Wavelength = 1 }));
    }

    [Fact]
    public void NativeAfocalFieldCurvatureRejectsFrozenLengthDataAndAcceptsFreshPowerMetadata()
    {
        var request = Request("field-curvature-distortion-w1");
        var entry = AnalysisComparisonRegistry.Get("Field Curvature and Distortion");
        Assert.Throws<InvalidDataException>(() => ResultNormalizer.Workbench(Data("field-curvature-distortion-w1", "workbench"), entry, request));
        var native = ResultNormalizer.Zemax(Data("field-curvature-distortion-w1", "native"), entry, request);
        Assert.Equal("Diopter", native.Series[0].YAxis.Unit);
        var config = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"));
        Assert.Equal(config.Analyses[entry.CanonicalAnalysisKey].Quantities["Distortion"],
            AfocalAnalysisContracts.ComparisonSettings(config.Analyses[entry.CanonicalAnalysisKey], request).Quantities["Distortion"]);
        var output = Path.Combine(Path.GetTempPath(), "afocal-coverage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(output);
            var path = Path.Combine(output, "data.json");
            var optic = OpticalFormatCatalog.Import(Source, ".zmx");
            JsonFiles.Write(path, new WorkbenchRuntime(optic).BuildAnalysisData(entry.CanonicalAnalysisKey, request.WorkbenchSettings));
            var actual = ResultNormalizer.Workbench(path, entry, request);
            Assert.Equal("Diopter", actual.Series[0].YAxis.Unit);
            Assert.Equal("Diopter", actual.Series[1].YAxis.Unit);
            Assert.Equal("Percent", actual.Series[2].YAxis.Unit);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }
}
