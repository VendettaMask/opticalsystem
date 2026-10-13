using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed class SharedCalculationConvergenceTests(ITestOutputHelper output)
{
    [Fact]
    public void ManufacturingUsesCompleteAsphereForSagThicknessSlopeAndDrawing()
    {
        var geometry = new EvenAsphereGeometry(0, 0, [0, 0.001]);
        var front = Row(1, geometry);
        var back = Row(2, new PlaneGeometry()) with { Material = "Air", Thickness = 0 };
        var element = Assert.Single(OpticalManufacturingModel.BuildElements([front, back]));
        Assert.Equal(0.256, OpticalManufacturingModel.Sag(front, 4)!.Value, 12);
        Assert.Equal(4.744, OpticalManufacturingModel.MinimumEdgeThickness(element)!.Value, 12);
        var expectedSlope = Math.Atan(4 * 0.001 * Math.Pow(4, 3)) * 180 / Math.PI;
        Assert.InRange(Math.Abs(ManufacturingGeometryService.SlopeDegrees(front, 4)!.Value - expectedSlope), 0, 1e-5);
        var profile = OpticalDrawingRendererCore.BuildManufacturingComponentProfile(element, 0);
        Assert.All(profile.Front, point => Assert.Equal(geometry.Sag(0, point.Y), point.Z, 12));
        var report = OpticalManufacturingModel.Evaluate([front, back], new ManufacturabilitySettings(MaximumEdgeSlopeDegrees: 1));
        Assert.Contains(report.Findings, finding => finding.Check == "前表面边缘斜率");
    }

    [Fact]
    public void GeometryDefinitionIsFrozenAndEditedBaseParametersDoNotUseStaleRadius()
    {
        var numbers = new Dictionary<string, double> { ["radius"] = 50, ["conic"] = 0, ["c0"] = 0.001 };
        var definition = new SurfaceGeometryDefinitionDto("even_asphere", numbers);
        numbers["c0"] = 100;
        Assert.Equal(0.001, definition.Numbers["c0"]);
        var row = Row(1, new EvenAsphereGeometry(50, 0, [0.001])) with { Radius = 100 };
        Assert.Equal(new EvenAsphereGeometry(100, 0, [0.001]).Sag(0, 4), ManufacturingGeometryService.Sag(row, 4));
    }

    [Fact]
    public void ManufacturingAndTracingShareTheConicDomainTolerance()
    {
        var geometry = new StandardGeometry(50);
        var surface = Row(1, geometry);
        var height = 50 * (1 + 2e-14);
        Assert.Equal(geometry.Sag(0, height), OpticalManufacturingModel.Sag(surface, height));
        Assert.Null(OpticalManufacturingModel.Sag(surface, 51));
        Assert.Null(OpticalManufacturingModel.Sag(surface, double.NaN));
    }

    [Fact]
    public void MissingAndNonRotationalGeometryCannotSilentlyBecomeASphere()
    {
        var asphere = Row(1, new EvenAsphereGeometry(50, 0, [0.1])) with { GeometryDefinition = null };
        Assert.Throws<NotSupportedException>(() => ManufacturingGeometryService.Sag(asphere, 2));
        var freeform = Row(1, new PolynomialGeometry(new Dictionary<(int, int), double> { [(0, 3)] = 0.1 }));
        Assert.Throws<NotSupportedException>(() => OpticalManufacturingModel.BuildElements([freeform, Row(2, new PlaneGeometry())]));
    }

    [Fact]
    public void RadialManufacturingRejectsDecenteredGeometryInsteadOfReportingCoaxialThickness()
    {
        var front = Row(1, new StandardGeometry(50)) with
        {
            Inspection = new SurfaceInspectionDto("none", 1, 0, 0, 0, 0, 0)
        };
        Assert.Throws<NotSupportedException>(() => OpticalManufacturingModel.BuildElements([front, Row(2, new PlaneGeometry())]));
    }

    [Fact]
    public async Task Captured123456SagTableMatchesTheActualApplicationManufacturingPath()
    {
        using var application = WorkbenchApplication.Create();
        await application.Documents.OpenAsync(Fixture("zemax-123456.ZMX"));
        var surface = application.Prescription.GetSurfaces().Single(row => row.Number == 1);
        Assert.NotNull(surface.GeometryDefinition);
        var maximum = 0.0;
        var count = 0;
        foreach (var line in File.ReadLines(Fixture("zemax-123456-sag-table.txt")))
        {
            var values = Regex.Matches(line, @"[-+]?\d+\.\d+E[-+]\d+");
            if (values.Count != 8) continue;
            var y = double.Parse(values[0].Value, CultureInfo.InvariantCulture);
            if (y > 7.8) continue; // Rounded terminal coordinate is outside the captured clear radius.
            var native = double.Parse(values[1].Value, CultureInfo.InvariantCulture);
            var error = Math.Abs(OpticalManufacturingModel.Sag(surface, y)!.Value - native);
            maximum = Math.Max(maximum, error);
            Assert.InRange(error, 0, 5e-8); // Existing captured text precision gate, unchanged.
            count++;
        }
        Assert.Equal(40, count);
        output.WriteLine($"Captured 123456 surface 1: {count} sag rows, maximum absolute error={maximum:R} mm. Spherical capture only; no asphere native certification.");
    }

    [Theory]
    [InlineData("Through Focus MTF")]
    [InlineData("离焦 MTF")]
    [InlineData("Fourier Through Focus MTF")]
    public void ThroughFocusNamesResolveToOneProductImplementation(string name)
    {
        Assert.Equal("Fourier Through Focus MTF", WorkbenchAnalysisCatalog.CanonicalKey(name));
        var optic = Optic.CreateBlank();
        Assert.DoesNotContain("Through Focus MTF", optic.Analyses.Names);
        var core = Assert.IsType<MtfThroughFocusAnalysis>(optic.Analyses.Create("Through Focus MTF"));
        Assert.Equal("Fourier Through Focus MTF", core.Name);
        var runtime = new WorkbenchRuntime(optic);
        var settings = new Dictionary<string, string> { ["Sampling"] = "8", ["NumberOfSteps"] = "2", ["FieldNumber"] = "1" };
        var canonical = runtime.BuildAnalysisData("Fourier Through Focus MTF", settings);
        var alias = runtime.BuildAnalysisData(name, settings);
        Assert.NotEmpty(canonical.PlotSeries);
        Assert.Equal(canonical.Name, alias.Name);
        Assert.Equal(canonical.PlotSeries.SelectMany(series => series.Points), alias.PlotSeries.SelectMany(series => series.Points));
    }

    [Fact]
    public void Captured123456FftThroughFocusIsComparedWithoutChangingItsReference()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Fixture("zemax-123456.ZMX")), ".zmx");
        using var reference = JsonDocument.Parse(File.ReadAllText(Fixture("zemax-123456-fft-through-focus-mtf.json")));
        var current = new MtfThroughFocusAnalysis(optic, MtfComputationMethod.Fourier,
            spatialFrequency: 50, deltaFocus: 0.1, focusPlaneCount: 5,
            settings: new MtfComputationSettings(PupilSampling: 64, ZemaxCompatible: true),
            wavelengthNumber: 0, fieldNumber: 1).GenerateData();
        var native = reference.RootElement.GetProperty("dataSeries")[0];
        var errors = new List<double>();
        for (var component = 0; component < 2; component++)
        {
            var points = current.PlotSeries[component].Points;
            Assert.Equal(native.GetProperty("x").GetArrayLength(), points.Count);
            for (var index = 0; index < points.Count; index++)
            {
                Assert.Equal(native.GetProperty("x")[index].GetDouble(), points[index].X, 12);
                errors.Add(points[index].Y - native.GetProperty("y")[index][component].GetDouble());
            }
        }
        var rms = Math.Sqrt(errors.Average(value => value * value));
        var maximum = errors.Max(Math.Abs);
        output.WriteLine($"123456 captured FFT through-focus, axis field, 600 values: RMS={rms:R}; maximum={maximum:R}. Engineering acceptance: RMS <= 0.02 and maximum <= 0.04, not a Zemax specification.");
        Assert.True(rms <= 0.02 && maximum <= 0.04, $"Captured FFT through-focus mismatch: RMS={rms:R}, max={maximum:R}");
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static SurfaceRowDto Row(int number, IGeometry geometry)
    {
        var snapshot = ComponentSnapshotFactory.FromGeometry(geometry);
        return new SurfaceRowDto(number, "Surface", snapshot.Numbers.GetValueOrDefault("radius"), 5,
            "N-BK7", "None", 4, snapshot.Numbers.GetValueOrDefault("conic"), false,
            geometry.Kind, "None", "Refraction", "None", 1, 1, 0, 50, false, false,
            GeometryDefinition: new SurfaceGeometryDefinitionDto(snapshot.Kind, snapshot.Numbers));
    }
}
