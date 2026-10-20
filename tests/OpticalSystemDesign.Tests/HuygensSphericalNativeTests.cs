using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class HuygensSphericalNativeTests
{
    [Theory]
    [InlineData("spherical-field1-linear", true)]
    [InlineData("spherical-field3-linear", true)]
    [InlineData("spherical8-field1-linear", true)]
    [InlineData("spherical8-field3-linear", true)]
    [InlineData("spherical-gaussian32-field1-linear", true)]
    [InlineData("spherical-gaussian32-field3-linear", true)]
    [InlineData("spherical64-field1-linear", false)]
    [InlineData("spherical64-field3-linear", false)]
    [InlineData("polychromatic-field1-linear", false)]
    [InlineData("polychromatic-field3-linear", false)]
    public void CapturedScalarPsfMatchesNativeAutoThroughRuntimeAndJson(string name, bool spherical)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensExtensions");
        var folder = Path.Combine(root, name);
        using var request = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "request.json")));
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Auto", "captured-settings.json")));
        var properties = settings.RootElement.GetProperty("properties");
        Assert.False(properties.GetProperty("UsePolarization").GetBoolean());
        Assert.False(properties.GetProperty("UseCentroid").GetBoolean());
        Assert.False(properties.GetProperty("Normalize").GetBoolean());
        var optic = Import(root);
        if (request.RootElement.TryGetProperty("diagnosticApodizationFactor", out var apodization))
            optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, apodization.GetDouble());
        var pupilCount = request.RootElement.GetProperty("pupilSampling").GetInt32();
        var waveNumber = request.RootElement.GetProperty("wavelength").GetInt32();
        var fieldNumber = request.RootElement.GetProperty("field").GetInt32();
        var pitch = properties.GetProperty("ImageDelta").GetDouble();
        Assert.Contains(pupilCount.ToString(CultureInfo.InvariantCulture), properties.GetProperty("PupilSampleSize").GetString());
        var distance = Math.Abs(optic.Paraxial.EstimateExitPupilLocation());
        var cornerSquared = 2 * Math.Pow(16 * pitch / 1000, 2);
        var curvature = cornerSquared / (Math.Sqrt(distance * distance + cornerSquared) + distance);
        Assert.Equal(spherical, curvature > optic.Wavelengths[0].Micrometers / 4000);
        var parameters = new Dictionary<string, string>
        {
            ["PupilSampling"] = pupilCount.ToString(CultureInfo.InvariantCulture),
            ["ImageSampling"] = "32",
            ["ImageDeltaMicrometers"] = pitch.ToString("R", CultureInfo.InvariantCulture),
            ["WavelengthNumber"] = waveNumber.ToString(CultureInfo.InvariantCulture),
            ["FieldNumber"] = fieldNumber.ToString(CultureInfo.InvariantCulture),
            ["UsePolarization"] = "false",
            ["UseCentroid"] = "false",
            ["Normalized"] = "false",
            ["Type"] = "线性"
        };
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens PSF", parameters);
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data))!;
        var actual = Assert.Single(data.PlotSeries).Points.Select(p => p.Value!.Value).ToArray();
        var expected = Grid(Path.Combine(folder, "Auto", "data.json"));
        var method = Grid(Path.Combine(folder, spherical ? "Spherical" : "Planar", "data.json"));
        Assert.Equal(expected, method);
        Assert.Equal(1024, actual.Length);
        var maximum = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        var relative = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Sum() / expected.Sum(v => v * v));
        Assert.True(maximum < 1e-5, $"{name}: max absolute difference {maximum:R}");
        Assert.True(relative < 5e-5, $"{name}: relative L2 {relative:R}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SphericalProjectionPreservesCombinedRigidTranslations(double hy)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensExtensions");
        var optic = Import(root);
        var baseline = DiffractionEngine.ComputeHuygensPsf(optic, (0, hy), optic.Wavelengths[0], 32, 32, .004, aimAtStop: true);
        var shifted = Optic.FromSnapshot(optic.ToSnapshot());
        var delta = new Vector3D(10, -12, 15);
        foreach (var surface in shifted.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + delta };
        var actual = DiffractionEngine.ComputeHuygensPsf(shifted, (0, hy), shifted.Wavelengths[0], 32, 32, .004, aimAtStop: true);
        Assert.InRange(actual.Values.Cast<double>().Zip(baseline.Values.Cast<double>(), (a, b) => Math.Abs(a - b) / 100).Max(), 0, 1e-7);
    }

    private static Optic Import(string root)
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        return optic;
    }

    private static double[] Grid(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var grid = document.RootElement.GetProperty("dataGrids")[0];
        Assert.Equal(32, grid.GetProperty("nx").GetInt32());
        Assert.Equal(32, grid.GetProperty("ny").GetInt32());
        return grid.GetProperty("values").EnumerateArray().SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
    }
}
