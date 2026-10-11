using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class HuygensPlanarIntegrationTests
{
    [Theory]
    [InlineData("psf/field2-uniform-linear", -10.6 / 15, false, 0)]
    [InlineData("psf/field3-uniform-linear", -1, false, 0)]
    [InlineData("psf/field2-gaussian-linear", -10.6 / 15, false, 512)]
    [InlineData("mtf/positive-chief", 1, false, 0)]
    [InlineData("mtf/positive-centroid", 1, true, 0)]
    [InlineData("mtf/negative-centroid", -1, true, 0)]
    public void CapturedOffAxisScalarPsfMatchesNativeTangentGrid(string name, double hy, bool centroid, double factor)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensOffAxis");
        var folder = Path.Combine(root, name);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        optic.RayAimingEnabled = true;
        if (factor > 0) optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, factor);
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Planar", "captured-settings.json")));
        var properties = settings.RootElement.GetProperty("properties");
        var size = factor > 0 ? 64 : 32;
        var pitch = properties.GetProperty("ImageDelta").GetDouble() / 1000;
        Assert.Equal(centroid, properties.GetProperty("UseCentroid").GetBoolean());
        Assert.False(properties.GetProperty("UsePolarization").GetBoolean());
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "Planar", "data.json")));
        var grid = native.RootElement.GetProperty("dataGrids")[0];
        Assert.Equal(size, grid.GetProperty("nx").GetInt32());
        Assert.Equal(-size / 2 * pitch * 1000, grid.GetProperty("minX").GetDouble(), 12);
        var expected = grid.GetProperty("values").EnumerateArray()
            .SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
        var wave = optic.Wavelengths[0];
        var offset = centroid ? DiffractionEngine.HuygensGeometricCenterOffset(optic, (0, hy),
            new[] { wave }, wave, 32, true) : (0d, 0d);
        var actual = DiffractionEngine.ComputeHuygensPsf(optic, (0, hy), wave, 32, size, pitch,
            aimAtStop: true, imageCenterOffset: offset).Values.Cast<double>().Select(v => v / 100).ToArray();
        var maximum = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        var relative = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Sum() / expected.Sum(v => v * v));
        Assert.True(maximum < 1e-5, $"{name}: maximum absolute error {maximum:R}");
        Assert.True(relative < 5e-5, $"{name}: relative L2 {relative:R}");
    }

    [Theory]
    [InlineData("ms-l7-uniform", 0)]
    [InlineData("ms-l7-gaussian4", 4)]
    [InlineData("ms-l7-gaussian32", 32)]
    [InlineData("ms-l7-gaussian512", 512)]
    [InlineData("cooke-uniform", 0)]
    [InlineData("tessar-uniform", 0)]
    public void CapturedPlanarFieldsCloseAcrossLensesAndPupilIllumination(string name, double factor)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Validation", "Zemax", "HuygensIntegration", name);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(folder, "source.ZMX")), ".zmx");
        using var system = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "system-data-settings.json")));
        optic.RayAimingEnabled = system.RootElement.GetProperty("RayAiming")
            .GetProperty("RayAiming").GetString() != "Off";
        if (factor > 0) optic.Apodization = new ZemaxApodization(ZemaxApodizationType.Gaussian, factor);
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "data.json")));
        var grid = native.RootElement.GetProperty("dataGrids")[0];
        var expected = grid.GetProperty("values").EnumerateArray()
            .SelectMany(row => row.EnumerateArray().Select(v => v.GetDouble())).ToArray();
        var psf = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 32, 32, .00025,
            aimAtStop: optic.RayAimingEnabled);
        var actual = psf.Values.Cast<double>().Select(v => v / 100).ToArray();
        var maximum = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Max();
        var relative = Math.Sqrt(actual.Zip(expected, (a, b) => (a - b) * (a - b)).Sum()
            / expected.Sum(v => v * v));
        Assert.True(maximum < 1e-5, $"{name}: maximum absolute error {maximum:R}");
        Assert.True(relative < 5e-5, $"{name}: relative L2 {relative:R}");
    }
}
