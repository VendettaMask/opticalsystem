using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalPupilScaleTests
{
    [Theory]
    [InlineData("PSF")]
    [InlineData("Huygens PSF")]
    public async Task CapturedPrismAutomaticGridMatchesNativeTangentialPupil(string key)
    {
        var fixture = JsonFiles.Read<JsonElement>(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-pupil-scale/automatic-psf.json"));
        Assert.True(fixture.GetProperty("originalReferenceBytesUnchanged").GetBoolean());
        var row = fixture.GetProperty("cases").EnumerateArray().Single(r =>
            r.GetProperty("request").GetProperty("canonicalAnalysisKey").GetString() == key);
        var request = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        Assert.Equal("0", request.WorkbenchSettings["ImageDeltaMicrometers"]);
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources", row.GetProperty("lens").GetString()!))).Configurations[0];
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(key, request.WorkbenchSettings);
        var temporary = Path.GetTempFileName();
        try
        {
            JsonFiles.Write(temporary, data);
            var actual = ResultNormalizer.Workbench(temporary, AnalysisComparisonRegistry.Get(key), request).Grids.Single();
            var grid = row.GetProperty("grid");
            var scale = PhysicalNormalization.UnitScale(request.NativeAfocalAngleUnit!, "Milliradian");
            double[] Axis(string minimum, string delta, string count) => Enumerable.Range(0, grid.GetProperty(count).GetInt32())
                .Select(i => (grid.GetProperty(minimum).GetDouble() + i * grid.GetProperty(delta).GetDouble()) * scale).ToArray();
            var expected = new Grid2DResult("native", Axis("minX", "dx", "nx"), Axis("minY", "dy", "ny"),
                grid.GetProperty("values").Deserialize<double?[][]>()!,
                new("IncidentAngle", "Milliradian"), new("IncidentAngle", "Milliradian"), new("Irradiance", "Dimensionless"));
            var matched = ComparisonMetrics.Align(actual, expected, []);
            var policy = ComparisonConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "comparison-settings.json"))
                .Analyses[key].Quantities["Irradiance"];
            var metric = ComparisonMetrics.Calculate("automatic-psf", "Dimensionless", matched, policy);
            Assert.Equal(1, matched.Coverage);
            Assert.Equal(Conclusion.Pass, metric.Conclusion);
        }
        finally { File.Delete(temporary); }
    }
}
