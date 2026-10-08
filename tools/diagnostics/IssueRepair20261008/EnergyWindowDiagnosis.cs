using System.Text.Json;
using System.Globalization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Metrics;
using OptilandWorkbench.ZemaxComparison.Normalization;

internal static class EnergyWindowDiagnosis
{
    public static void Run(string root, string output)
    {
        var fixture = Path.Combine(root, "validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixture, "source.ZMX")), ".zmx");
        var path = Path.Combine(fixture, "diffraction-encircled-energy-c1/data.json");
        var hash = JsonFiles.Hash(File.ReadAllBytes(path));
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "diffraction-encircled-energy-c1/captured-settings.json")));
        var request = captured.RootElement.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var expected = ExtendedResultNormalizer.Zemax(path, request);
        var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
        optic.RayAimingEnabled = request.UseRayAiming;
        var rows = new List<object>();
        foreach (var density in new[] { 32, 64, 128 })
        {
            var settings = AnalysisComparisonRegistry.MapWorkbench(entry, request);
            settings["PupilSampling"] = density.ToString(CultureInfo.InvariantCulture);
            settings["ImageSampling"] = (2 * density).ToString(CultureInfo.InvariantCulture);
            var current = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, settings);
            current = JsonSerializer.Deserialize<AnalysisData>(JsonSerializer.Serialize(current, JsonFiles.Options), JsonFiles.Options)!;
            var actual = ExtendedResultNormalizer.Workbench(current, request);
            foreach (var ideal in new[] { true, false })
            {
                var reference = expected.Series[ideal ? 0 : 1];
                var existing = actual.Series.Single(s => s.Id == reference.Id);
                var heatmap = new PsfAnalysis(optic, density, 2 * density, wavelengthNumber: 1,
                    fieldNumber: 1, type: "linear", displayAs: "heatmap", zemaxCompatible: true,
                    ignoreOpd: ideal).GenerateData().PlotSeries.Single(p => p.Kind == AnalysisSeriesKind.Heatmap);
                var points = heatmap.Points.Select(p => new WeightedEnergyPoint(p.X, p.Y, p.Value!.Value)).ToArray();
                var center = new GeometricEnergyDistribution(points, GeometricEnergyReference.Centroid, EnergyRegion.Circle).Center;
                var full = new DiffractionEnergyDistribution(points, center, EnergyRegion.Circle);
                var radii = reference.X;
                var fullValues = radii.Select(r => full.FractionAtDistance(r)).ToArray();
                var tolerance = entry.DefaultTolerances[reference.YAxis.Quantity];
                var existingMetric = ComparisonMetrics.Calculate(reference.Id, reference.YAxis.Unit,
                    ComparisonMetrics.Align(existing, reference, []), tolerance);
                var fullMetric = ComparisonMetrics.Calculate(reference.Id, reference.YAxis.Unit,
                    ComparisonMetrics.Align(reference with { Y = fullValues }, reference, []), tolerance);
                rows.Add(new { density, ideal, settings, existingMetric, fullMetric, full.MaximumCoveredDistanceMicrometers, center,
                    existing, fullValues, reference });
                Console.WriteLine($"DEE density={density}, ideal={ideal}: existing NRMSE={existingMetric.Nrmse:R}, full-window NRMSE={fullMetric.Nrmse:R}");
            }
        }
        if (JsonFiles.Hash(File.ReadAllBytes(path)) != hash) throw new InvalidDataException("Frozen native evidence changed.");
        JsonFiles.Write(Path.Combine(output, "summary.json"), new { rows, nativeSha256 = hash,
            request, semantics = "Formal Core and shared comparison metrics. Density 32 reproduces captured settings; 64/128 are Workbench-only controls against the same 32-sample native evidence, not native convergence certification. No runtime model or tolerance changed." });
    }
}
