using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.FileIO;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalApertureResultTests(ITestOutputHelper output)
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-aperture-state");
    private static JsonElement Manifest => JsonFiles.Read<JsonElement>(Path.Combine(Root, "manifest.json"));
    [Theory]
    [InlineData("inactive32")]
    [InlineData("inactive64")]
    [InlineData("active32")]
    [InlineData("active64")]
    [InlineData("active-mtf")]
    public void PublicAfocalResultsMatchNativeForInactiveParametersAndExplicitAnnuli(string id)
    {
        var row = Manifest.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        foreach (var file in Manifest.GetProperty("files").EnumerateArray().Where(f => f.GetProperty("path").GetString()!.StartsWith(id + "/", StringComparison.Ordinal)))
            Assert.Equal(file.GetProperty("sha256").GetString(), JsonFiles.Hash(File.ReadAllBytes(Path.Combine(Root, file.GetProperty("path").GetString()!))));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(Root, id, "source.ZMX")), ".zmx");
        var active = row.GetProperty("active").GetBoolean();
        if (active) { var aperture = Assert.IsType<AnnularAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture); Assert.Equal(.31, aperture.InnerRadius); }
        else Assert.Null(optic.SurfaceGroup.Items[1].PhysicalAperture);
        var name = row.GetProperty("name").GetString()!;
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(name, new Dictionary<string, string>
        {
            ["PupilSampling"] = row.GetProperty("n").GetInt32().ToString(), ["ImageSampling"] = "32", ["ImageDeltaMilliradians"] = ".02",
            ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["UseCentroid"] = "False", ["MaximumFrequency"] = "10"
        });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        var gates = row.GetProperty("gates").Deserialize<Dictionary<string, Tolerances>>(JsonFiles.Options)!;
        var native = JsonFiles.Read<JsonElement>(Path.Combine(Root, id, "data.json"));
        double maximum = 0; var count = 0; var violations = 0;
        void Compare(double actual, double expected, Tolerances gate)
        {
            var error = Math.Abs(actual - expected); maximum = Math.Max(maximum, error); count++;
            if (error > gate.Absolute + gate.Relative * Math.Abs(expected)) violations++;
        }
        if (name == "Huygens PSF")
        {
            var grid = native.GetProperty("dataGrids")[0];
            Assert.Equal(32, grid.GetProperty("nx").GetInt32()); Assert.Equal(.02, grid.GetProperty("dx").GetDouble());
            var points = data.PlotSeries.Single().Points; Assert.Equal(1024, points.Count);
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
            {
                var p = points[y * 32 + x];
                Assert.InRange(Math.Abs(p.X - (x - 16) * .02), 0, 1e-12);
                Assert.InRange(Math.Abs(p.Y - (y - 16) * .02), 0, 1e-12);
                Compare(p.Value!.Value, grid.GetProperty("values")[y][x].GetDouble(), gates.Values.Single());
            }
        }
        else
        {
            var series = native.GetProperty("dataSeries")[0]; var xs = series.GetProperty("x"); var ys = series.GetProperty("y");
            Assert.Equal(2, data.PlotSeries.Count);
            for (var axis = 0; axis < 2; axis++)
            {
                var points = data.PlotSeries[axis].Points; Assert.Equal(xs.GetArrayLength(), points.Count);
                for (var i = 0; i < points.Count; i++)
                {
                    Assert.InRange(Math.Abs(points[i].X - xs[i].GetDouble()), 0, 1e-12);
                    Compare(points[i].Y, ys[i][axis].GetDouble(), gates.Values.Single());
                }
            }
        }
        Assert.Equal(0, violations);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "native-afocal-aperture", id, name, count, maximum, violations, gates }));
    }
}
