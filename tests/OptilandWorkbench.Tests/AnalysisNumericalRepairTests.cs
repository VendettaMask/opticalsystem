using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisNumericalRepairTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [Theory]
    [InlineData("zemax-ms-l7-high-na.ZMX", 0.7471048941977483, 1.4722738652562612)]
    [InlineData("zemax-123456.ZMX", 0.0940942801067534, 0.1665067944948616)]
    public void CapturedMonochromaticNonpolarSpotSurvivesJsonSnapshot(string file, double rms, double geo)
    {
        // OpticStudio 2026 R1, field 1 / wavelength 1, chief reference,
        // hexapolar density 20 (1261 rays), polarization off; not universal defaults.
        var optic = Import(file);
        var restored = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(
            JsonSerializer.Serialize(optic.ToSnapshot(), JsonOptions), JsonOptions)!);
        foreach (var lens in new[] { optic, restored })
        {
            var data = new WorkbenchRuntime(lens).BuildAnalysisData("Spot Diagram", new Dictionary<string, string>
            {
                ["FieldNumber"] = "1",
                ["WavelengthNumber"] = "1",
                ["RayDensity"] = "20",
                ["Reference"] = "chief",
                ["Pattern"] = "hexapolar",
                ["UsePolarization"] = "false"
            });
            var metrics = Assert.Single(data.PlotPanes!).Metrics!;
            Assert.InRange(metrics[0].Value, rms - 2e-8, rms + 2e-8);
            Assert.InRange(metrics[1].Value, geo - 2e-8, geo + 2e-8);
        }
    }

    [Fact]
    public void FailedEdgeRaysRemainInSpotSamplingAccounting()
    {
        var optic = Import("zemax-ms-l7-high-na.ZMX");
        var field = SpotAnalysisEngine.DefinedFields(optic)[0];
        var unaimed = SpotAnalysisEngine.Generate(optic, [field], [optic.Wavelengths[0]], 20, "hexapolar");
        var aimed = SpotAnalysisEngine.Generate(optic, [field], [optic.Wavelengths[0]], 20, "hexapolar", aimAtStop: true);
        Assert.Equal(1261, unaimed.RayCount);
        Assert.Equal(120, unaimed.VignettedRayCount);
        Assert.Equal(1261, aimed.RayCount);
        Assert.Equal(0, aimed.VignettedRayCount);
    }

    [Theory]
    [InlineData("zemax-123456.ZMX")]
    [InlineData("zemax-ms-l7-high-na.ZMX")]
    public void SnapshotFreezesResolvedCatalogDispersionAndMetadata(string file)
    {
        var optic = Import(file);
        var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(JsonSerializer.Serialize(optic.ToSnapshot(), JsonOptions), JsonOptions)!;
        var restored = Optic.FromSnapshot(snapshot);
        foreach (var pair in optic.SurfaceGroup.Items.Zip(restored.SurfaceGroup.Items))
        {
            foreach (var materials in new[] { (pair.First.MaterialBefore, pair.Second.MaterialBefore), (pair.First.MaterialAfter, pair.Second.MaterialAfter) })
            {
                foreach (var wavelength in new[] { 400.0, 486.1, 550, 587.6, 656.3, 700 })
                {
                    Assert.Equal(materials.Item1.RefractiveIndex(wavelength), materials.Item2.RefractiveIndex(wavelength));
                    Assert.Equal(materials.Item1.ExtinctionCoefficient(wavelength), materials.Item2.ExtinctionCoefficient(wavelength));
                }
                if (materials.Item1 is CatalogGlassMaterial catalog)
                {
                    var other = Assert.IsType<CatalogGlassMaterial>(materials.Item2);
                    Assert.Equal(catalog.Manufacturer, other.Manufacturer);
                    Assert.Equal(catalog.Formula, other.Formula);
                    Assert.Equal(JsonSerializer.Serialize(catalog.ZemaxData, JsonOptions), JsonSerializer.Serialize(other.ZemaxData, JsonOptions));
                }
            }
        }
    }

    [Fact]
    public void TabulatedCatalogDataAndMissingThermalValuesRoundTrip()
    {
        var glass = new CatalogGlassMaterial("N-BK7", "Private capture", "tabulated nk", 400, 700,
            refractiveIndexWavelengthsNanometers: [400, 700], refractiveIndices: [1.71, 1.69],
            extinctionWavelengthsNanometers: [400, 700], extinctionCoefficients: [1e-8, 2e-8],
            zemaxData: new OpticalGlassDefinition { Name = "N-BK7", ThermalCoefficients = [double.NaN, 1e-6] });
        var encoded = JsonSerializer.Serialize(ComponentSnapshotFactory.FromMaterial(glass), JsonOptions);
        var restored = Assert.IsType<CatalogGlassMaterial>(ComponentSnapshotFactory.ToMaterial(
            JsonSerializer.Deserialize<ComponentSnapshot>(encoded, JsonOptions), "N-BK7", new MaterialRegistry()));
        Assert.Equal(1.7, restored.RefractiveIndex(550), 14);
        Assert.InRange(restored.ExtinctionCoefficient(550), 1.5e-8 - 1e-22, 1.5e-8 + 1e-22);
        Assert.True(double.IsNaN(restored.ZemaxData!.ThermalCoefficients[0]));
        Assert.Equal(1e-6, restored.ZemaxData.ThermalCoefficients[1]);
        var legacy = ComponentSnapshot.Empty("catalog") with { Text = new() { ["name"] = "N-BK7" } };
        Assert.NotEqual(restored.RefractiveIndex(550), ComponentSnapshotFactory.ToMaterial(legacy, "N-BK7", new MaterialRegistry()).RefractiveIndex(550));
    }

    [Theory]
    [InlineData("Encircled Energy")]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("MTF")]
    [InlineData("Contrast Loss Map")]
    public void HighNaApplicationAnalysisUsesConfiguredStopAiming(string key)
    {
        var data = new WorkbenchRuntime(Import("zemax-ms-l7-high-na.ZMX")).BuildAnalysisData(key, new Dictionary<string, string>
        {
            ["FieldNumber"] = "1",
            ["WavelengthNumber"] = "1",
            ["Sampling"] = "32",
            ["PupilSampling"] = "32",
            ["ImageSampling"] = "32",
            ["ImageSize"] = "32",
            ["NumRays"] = "32"
        });
        Assert.NotEmpty(data.PlotSeries);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void CapturedFftPsfGridHasChiefRayAtPhysicalZero(double pitch)
    {
        var data = new WorkbenchRuntime(Import("zemax-ms-l7-high-na.ZMX")).BuildAnalysisData("PSF", new Dictionary<string, string>
        {
            ["FieldNumber"] = "1",
            ["WavelengthNumber"] = "1",
            ["Sampling"] = "64",
            ["Display"] = "128",
            ["ImageDeltaMicrometers"] = pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        var points = Assert.Single(data.PlotSeries).Points;
        Assert.Equal(-63 * pitch, points[0].X);
        Assert.Equal(-63 * pitch, points[0].Y);
        Assert.Equal(64 * pitch, points[^1].X);
        var chief = points[63 * 128 + 63];
        Assert.Equal(0, chief.X);
        Assert.Equal(0, chief.Y);
        Assert.Equal(chief.Value, Convert.ToDouble(data.Values["StrehlRatio"]));
        Assert.Equal(pitch, Convert.ToDouble(data.Values["ImageDeltaMicrometers"]));
    }

    private static Optic Import(string name) => OpticalFormatCatalog.Import(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)), ".zmx");

    [Fact]
    public void FftPsfMatchesCapturedHighNaGridWithoutCoordinateFitOrRenormalization()
    {
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Validation", "Zemax", "NumericalRepair", "ms-l7-fft-psf-data.json")));
        var grid = capture.RootElement.GetProperty("dataGrids")[0];
        var data = new WorkbenchRuntime(Import("zemax-ms-l7-high-na.ZMX")).BuildAnalysisData("PSF", new Dictionary<string, string>
        {
            ["FieldNumber"] = "1",
            ["WavelengthNumber"] = "1",
            ["Sampling"] = "64",
            ["Display"] = "128",
            ["ImageDeltaMicrometers"] = "0.25",
            ["Normalized"] = "false",
            ["UsePolarization"] = "false"
        });
        var points = Assert.Single(data.PlotSeries).Points;
        var sumSquaredError = 0.0;
        var sumSquaredReference = 0.0;
        for (var row = 0; row < 128; row++)
        {
            for (var column = 0; column < 128; column++)
            {
                var point = points[row * 128 + column];
                Assert.Equal(grid.GetProperty("minX").GetDouble() + column * grid.GetProperty("dx").GetDouble(), point.X);
                Assert.Equal(grid.GetProperty("minY").GetDouble() + row * grid.GetProperty("dy").GetDouble(), point.Y);
                var reference = grid.GetProperty("values")[row][column].GetDouble();
                sumSquaredError += Math.Pow(point.Value!.Value - reference, 2);
                sumSquaredReference += reference * reference;
            }
        }
        Assert.InRange(Math.Sqrt(sumSquaredError / sumSquaredReference), 0, 0.01);
    }

    [Fact]
    public void FftInterpolationPreservesEdgeAndPeriodicNyquistSample()
    {
        var psf = new PsfResult(new double[,] { { 1, 2 }, { 3, 4 } }, 2, 2, 1, 0.5);
        Assert.Equal(4, PsfAnalysis.BilinearSample(psf, 0, 0));
        Assert.Equal(3, PsfAnalysis.BilinearSample(psf, 0.5, 0));
        Assert.Equal(1, PsfAnalysis.BilinearSample(psf, 0.5, 0.5));
        Assert.Equal(3.5, PsfAnalysis.BilinearSample(psf, 0.25, 0));
        Assert.Equal(0, PsfAnalysis.BilinearSample(psf, 1, 0));
    }
}
