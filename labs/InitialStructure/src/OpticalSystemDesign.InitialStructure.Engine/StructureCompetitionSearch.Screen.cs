using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal static partial class StructureCompetitionSearch
{
    internal static StructureCompetitionScreen Screen(CompiledTopology source, TopologyTargets targets, long maximumRays, CancellationToken token)
    {
        var fields = TopologyEvaluationPlan.Fields(source, 3);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(1, 4).ToArray();
        var checks = TopologyEvaluationPlan.Checks(8);
        var completed = 0; var geometry = true; var boundary = true;
        double? minimumValid = null, rms = null, maximum = null;
        var metricsAvailable = true; long probes = 0; var outcome = "Screened";
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(maximumRays);
        try
        {
            var optic = source.CreateOptic();
            foreach (var row in TopologyEvaluationPlan.Geometry(source, targets, 64))
            {
                token.ThrowIfCancellationRequested(); probes++;
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                geometry &= double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit);
            }
            foreach (var field in fields)
            {
                token.ThrowIfCancellationRequested();
                var physical = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y, pupils, checks,
                    includeSurfaceTransmission: false, cancellationToken: token);
                var integration = physical.Integration;
                var valid = integration.RayCount > 0 && integration.Wavelengths.Count > 0
                    ? Math.Min((double)(integration.RayCount - integration.VignettedRayCount) / integration.RayCount,
                        integration.Wavelengths.Min(w => w.AttemptedRayCount > 0 ? (double)w.Rays.Count / w.AttemptedRayCount : 0)) : 0;
                minimumValid = Math.Min(minimumValid ?? 1, valid);
                boundary &= physical.Checks.RayCount > 0 && physical.Checks.VignettedRayCount == 0;
                // Missing spot statistics cannot erase measured zero-throughput fields.
                if (integration.Quantity == AnalysisAxisQuantity.ImageHeight && integration.Unit == AnalysisAxisUnit.Millimeter
                    && integration.Metrics is { } metrics && physical.MaximumRadius is { } radius
                    && double.IsFinite(metrics.RmsSpotRadius) && metrics.RmsSpotRadius >= 0 && double.IsFinite(radius) && radius >= 0)
                { rms = Math.Max(rms ?? 0, metrics.RmsSpotRadius); maximum = Math.Max(maximum ?? 0, radius); }
                else metricsAvailable = false;
                completed++;
            }
        }
        catch (OperationCanceledException) { outcome = "Cancelled"; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { outcome = "ScreenRayBudgetExhausted"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { outcome = "ScreenDomainRejected"; }
        var all = outcome == "Screened" && completed == fields.Length;
        var quality = all && metricsAvailable && rms is { } r && maximum is { } m
            ? Math.Max(r / targets.MaximumRmsRadiusMillimeters, m / targets.MaximumSpotRadiusMillimeters) : (double?)null;
        if (all) outcome = !geometry ? "GeometryRejected" : minimumValid < targets.MinimumValidRayFraction || !boundary
            ? "ThroughputRejected" : !metricsAvailable ? "UnavailablePhysicalMetrics" : "Screened";
        return new(all, geometry, completed, fields.Length, minimumValid, boundary, rms, maximum, quality, outcome, measured.RayCount, probes);
    }
}
