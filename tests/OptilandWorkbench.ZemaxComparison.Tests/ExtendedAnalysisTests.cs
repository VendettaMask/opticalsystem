using System.Text.Json;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;
using Xunit.Abstractions;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class ExtendedAnalysisTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Seidel Coefficients", "seidel-coefficients")]
    [InlineData("Seidel Diagram", "seidel-diagram")]
    [InlineData("Field Curvature and Distortion", "field-curvature-and-distortion")]
    [InlineData("Field Curvature", "field-curvature")]
    [InlineData("Color Focus Shift", "color-focus-shift")]
    [InlineData("Lateral Color", "lateral-color")]
    [InlineData("Axial Aberration", "axial-aberration")]
    [InlineData("Single Ray Trace", "single-ray-trace")]
    [InlineData("Grid Distortion", "grid-distortion")]
    [InlineData("Full Field Aberration", "full-field-aberration")]
    [InlineData("Cardinal Points Data", "cardinal-points-data")]
    [InlineData("Y-Ybar", "y-ybar")]
    [InlineData("Angle vs Image Height", "angle-vs-image-height")]
    [InlineData("RMS vs Field", "rms-vs-field")]
    [InlineData("RMS vs Wavelength", "rms-vs-wavelength")]
    [InlineData("RMS vs Focus", "rms-vs-focus")]
    [InlineData("RMS Wavefront vs Field", "rms-wavefront-vs-field")]
    [InlineData("Prescription Report", "prescription-report")]
    [InlineData("Through Focus MTF", "through-focus-mtf")]
    [InlineData("Fourier Through Focus MTF", "fourier-through-focus-mtf")]
    [InlineData("Geometric Through Focus MTF", "geometric-through-focus-mtf")]
    [InlineData("Geometric MTF", "geometric-mtf")]
    [InlineData("FFT PSF Cross Section", "fft-psf-cross-section")]
    [InlineData("FFT Line Edge Spread", "fft-line-edge-spread")]
    public void CurrentWorkbenchMatchesCapturedMsL7Curves(string key, string slug)
        => Verify(key, slug);

    [Theory]
    [InlineData("Huygens Through Focus MTF", "huygens-through-focus-mtf", 0.032689149705)]
    [InlineData("Huygens PSF Cross Section", "huygens-psf-cross-section", 0.003725666990)]
    public void OpenNumericalDifferenceIsMeasuredAndMustNotRegress(string key, string slug, double recordedNrmse)
    {
        // This is an error non-regression check, NOT a Zemax precision pass. The
        // native tolerances remain unchanged and the report retains Close/Difference.
        // Improvement to Pass is welcome; worsening the measured error is a failure.
        Verify(key, slug, recordedNrmse);
    }

    private void Verify(string key, string slug, double? recordedNrmse = null)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "analysis-expansion");
        var native = Path.Combine(root, slug + "-c1");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root, "source.ZMX")), ".zmx");
        var entry = AnalysisComparisonRegistry.Get(key);
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(native, "captured-settings.json")));
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var runtime = new WorkbenchRuntime(optic);
        var data = runtime.BuildAnalysisData(key, AnalysisComparisonRegistry.MapWorkbench(entry, request));
        // Exercise the same JSON boundary as the isolated Workbench worker.
        data = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(data, JsonFiles.Options), JsonFiles.Options)!;
        var w = ExtendedResultNormalizer.Workbench(data, request);
        var z = ExtendedResultNormalizer.Zemax(Path.Combine(native, "data.json"), request);
        Assert.Equal(z.Series.Select(s => s.Id), w.Series.Select(s => s.Id));
        var failures = new List<string>();
        Assert.Equal(z.Scalars.Select(s => s.Id), w.Scalars.Select(s => s.Id));
        foreach (var scalar in z.Scalars)
        {
            var actual = w.Scalars.Single(s => s.Id == scalar.Id);
            var metric = ComparisonMetrics.Calculate(scalar.Id, scalar.Unit, new([0], null, [actual.Value], [scalar.Value], 1), entry.DefaultTolerances[scalar.Id]);
            if (metric.Conclusion != Conclusion.Pass) failures.Add($"{scalar.Id}: {metric.Conclusion}, max={metric.MaxAbsolute:R}");
        }
        foreach (var curve in z.Series)
        {
            var values = ComparisonMetrics.Align(w.Series.Single(s => s.Id == curve.Id), curve, []);
            var metric = ComparisonMetrics.Calculate(curve.Id, curve.YAxis.Unit, values, entry.DefaultTolerances[curve.YAxis.Quantity]);
            output.WriteLine($"{key}/{curve.Id}: max={metric.MaxAbsolute:R}, NRMSE={metric.Nrmse:R}, {metric.Conclusion}");
            if (recordedNrmse is { } limit)
            {
                Assert.NotEqual(Conclusion.Incomparable, metric.Conclusion);
                Assert.InRange(metric.Nrmse, 0, limit);
                output.WriteLine("Open numerical difference; a green test does not certify native agreement.");
            }
            else if (metric.Conclusion != Conclusion.Pass) failures.Add($"{curve.Id}: {metric.Conclusion}, NRMSE={metric.Nrmse:R}, max={metric.MaxAbsolute:R}");
        }
        foreach (var grid in z.Grids)
        {
            var values = ComparisonMetrics.Align(w.Grids.Single(g => g.Id == grid.Id), grid, []);
            var metric = ComparisonMetrics.Calculate(grid.Id, grid.ValueAxis.Unit, values, entry.DefaultTolerances[grid.ValueAxis.Quantity]);
            output.WriteLine($"{key}/{grid.Id}: max={metric.MaxAbsolute:R}, NRMSE={metric.Nrmse:R}, {metric.Conclusion}");
            if (metric.Conclusion != Conclusion.Pass) failures.Add($"{grid.Id}: {metric.Conclusion}, NRMSE={metric.Nrmse:R}, max={metric.MaxAbsolute:R}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
