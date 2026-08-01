using System.Text.Json;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxEncircledEnergyParityTests
{
    [Fact]
    public void DiffractionEncircledEnergyUsesZemaxFftSamplingAndPixelAreaIntegration()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-diffraction-encircled-energy.json")));

        var current = new DiffractionEncircledEnergyAnalysis(
            optic,
            pupilSampling: 64,
            imageSampling: 128,
            numPoints: 401,
            wavelengthNumber: 0,
            fieldNumber: 0,
            type: "encircled",
            reference: "centroid",
            maximumDistanceMicrometers: 10).GenerateData();
        var referenceCurves = zemax.RootElement.GetProperty("dataSeries");

        Assert.Equal("FFT PSF pixel-area integration", current.Values["Method"]);
        Assert.Equal(10.0, Convert.ToDouble(current.Values["MaximumDistanceMicrometers"]), 10);
        Assert.Equal(referenceCurves.GetArrayLength(), current.PlotSeries.Count);
        Assert.All(current.PlotSeries, series => Assert.Equal(401, series.Points.Count));

        var errors = new List<double>();
        for (var curveIndex = 0; curveIndex < referenceCurves.GetArrayLength(); curveIndex++)
        {
            var reference = referenceCurves[curveIndex];
            var points = current.PlotSeries[curveIndex].Points;
            var squaredError = 0.0;
            var count = reference.GetProperty("x").GetArrayLength();
            for (var pointIndex = 0; pointIndex < count; pointIndex++)
            {
                var radius = reference.GetProperty("x")[pointIndex].GetDouble();
                var expected = reference.GetProperty("y")[pointIndex][0].GetDouble();
                var actual = Interpolate(points, radius);
                squaredError += Math.Pow(actual - expected, 2);
            }

            errors.Add(Math.Sqrt(squaredError / count));
        }

        var ordered = errors.Order().ToArray();
        var median = ordered[ordered.Length / 2];
        Assert.True(
            median <= 0.02 && errors.Max() <= 0.02,
            $"Diffraction EE RMS fraction errors: median={median:G17}, max={errors.Max():G17}; "
            + string.Join(", ", errors.Select(value => value.ToString("G8"))));
    }

    [Fact]
    public void ExtendedSourceUsesTheCapturedLetterFImageAndFieldScale()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        var source = ExtendedSourceImage.ParseZemaxTextIma(
            File.ReadAllText(Path.Combine(fixtureDirectory, "LETTERF.IMA")));
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-extended-source-encircled-energy.json")));

        var current = new ExtendedSourceEncircledEnergyAnalysis(
            optic,
            fieldSize: 6.3639610306789285,
            numRays: 2_000_000,
            numPoints: 397,
            wavelengthNumber: 0,
            fieldNumber: 1,
            type: "encircled",
            reference: "centroid",
            maximumDistanceMicrometers: 10_000,
            sourceImage: source,
            sourceName: "LETTERF.IMA").GenerateData();

        Assert.Equal("Zemax IMA weighted-pixel source", current.Values["Method"]);
        Assert.Equal(14, current.Values["ActiveSourcePixelCount"]);
        Assert.Equal(350, current.Values["ActiveSourcePointCount"]);
        Assert.InRange(Convert.ToDouble(current.Values["ReferenceXMicrometers"]), -800, -760);
        Assert.InRange(Convert.ToDouble(current.Values["ReferenceYMicrometers"]), 760, 800);

        var reference = zemax.RootElement.GetProperty("dataSeries")[0];
        var points = Assert.Single(current.PlotSeries).Points;
        var squaredError = 0.0;
        var count = reference.GetProperty("x").GetArrayLength();
        for (var index = 0; index < count; index++)
        {
            var distance = reference.GetProperty("x")[index].GetDouble();
            var expected = reference.GetProperty("y")[index][0].GetDouble();
            var actual = Interpolate(points, distance);
            squaredError += Math.Pow(actual - expected, 2);
        }

        var normalizedRootMeanSquareError = Math.Sqrt(squaredError / count);
        var diagnostic = string.Join(", ", new[] { 1000.0, 2000, 3000, 4000 }
            .Select(distance =>
            {
                var nearest = Enumerable.Range(0, count).MinBy(index => Math.Abs(
                    reference.GetProperty("x")[index].GetDouble() - distance));
                var referenceDistance = reference.GetProperty("x")[nearest].GetDouble();
                var expected = reference.GetProperty("y")[nearest][0].GetDouble();
                return $"{referenceDistance:0}: current={Interpolate(points, referenceDistance):0.000000}, zemax={expected:0.000000}";
            }));
        Assert.True(
            normalizedRootMeanSquareError <= 0.03,
            $"Extended-source fraction NRMSE against Zemax is {normalizedRootMeanSquareError:G17}; {diagnostic}.");
    }

    [Fact]
    public void GeometricEncircledEnergyUsesNormalizedPolychromaticCentroidCurves()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        using var zemax = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(fixtureDirectory, "zemax-123456-geometric-encircled-energy.json")));

        var current = new EncircledEnergyAnalysis(
            optic,
            numRays: 10_000,
            distribution: "sobol",
            numPoints: 256).GenerateData();
        var referenceCurves = zemax.RootElement.GetProperty("dataSeries");

        Assert.Equal(optic.Wavelengths.Count, current.Values["WavelengthCount"]);
        Assert.Equal(true, current.Values["MultiplyByDiffractionLimit"]);
        Assert.Equal(referenceCurves.GetArrayLength(), current.PlotSeries.Count);
        Assert.All(current.PlotSeries, series =>
        {
            Assert.Equal("Radius (µm)", series.XAxisLabel);
            Assert.Equal("Fraction of Energy", series.YAxisLabel);
            Assert.All(series.Points, point => Assert.InRange(point.Y, 0, 1));
        });

        var errors = new List<double>();
        for (var fieldIndex = 0; fieldIndex < referenceCurves.GetArrayLength(); fieldIndex++)
        {
            var reference = referenceCurves[fieldIndex];
            var currentPoints = current.PlotSeries[fieldIndex].Points;
            var squaredError = 0.0;
            var count = reference.GetProperty("x").GetArrayLength();
            for (var index = 0; index < count; index++)
            {
                var radius = reference.GetProperty("x")[index].GetDouble();
                var expected = reference.GetProperty("y")[index][0].GetDouble();
                var actual = Interpolate(currentPoints, radius);
                squaredError += Math.Pow(actual - expected, 2);
            }

            errors.Add(Math.Sqrt(squaredError / count));
        }

        var median = errors.Order().ElementAt(errors.Count / 2);
        Assert.True(
            median <= 0.01,
            $"Median absolute RMS fraction error against Zemax is {median:G17}; fields: {string.Join(", ", errors.Select(value => value.ToString("G8")))}.");
    }

    private static double Interpolate(IReadOnlyList<AnalysisPoint> points, double x)
    {
        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

        var upper = 1;
        while (points[upper].X < x)
        {
            upper++;
        }

        var lower = upper - 1;
        var fraction = (x - points[lower].X) / (points[upper].X - points[lower].X);
        return points[lower].Y + (fraction * (points[upper].Y - points[lower].Y));
    }
}
