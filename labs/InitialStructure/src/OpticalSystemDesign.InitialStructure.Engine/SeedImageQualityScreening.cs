using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Consumes unchanged Core physical spot/throughput metrics, not a module-local optical approximation.</summary>
internal static class SeedImageQualityScreening
{
    internal static void Validate(SeedImageScreeningOptions options)
    {
        if (options.Version != 1 || options.MaximumCandidates is < 1 or > 64
            || options.FieldsPerDirection is < 2 or > 5 || options.PupilRings is < 1 or > 4
            || options.PupilArms is < 4 or > 16 || options.BoundaryCheckArms is < 4 or > 32
            || options.MaximumTracedRaysPerCandidate is < 1 or > 1_000_000
            || !double.IsFinite(options.RelativeQualityWindow) || options.RelativeQualityWindow is < 0 or > 1)
            throw new ArgumentException("Invalid real-ray seed screening protocol.");
    }

    internal static SeedImageScreeningResult Evaluate(CompiledTopology source, TopologyTargets targets,
        SeedImageScreeningOptions options, CancellationToken cancellationToken)
    {
        Validate(options);
        var fingerprint = TopologyDiversitySelection.PrescriptionIdentity(source.Topology);
        var fields = TopologyEvaluationPlan.Fields(source, options.FieldsPerDirection);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(options.PupilRings, options.PupilArms).ToArray();
        var checks = TopologyEvaluationPlan.Checks(options.BoundaryCheckArms ?? 32);
        var completed = 0;
        double? worstRms = null, worstMaximum = null, minimumValid = null;
        var boundaryPassed = true;
        var outcome = "Screened";
        using var measured = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRaysPerCandidate);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, cancellationToken);
            var optic = source.CreateOptic();
            foreach (var field in fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assessment = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y,
                    pupils, checks, includeSurfaceTransmission: false, cancellationToken: cancellationToken);
                var integration = assessment.Integration;
                var boundary = assessment.Checks;
                if (integration.Quantity != AnalysisAxisQuantity.ImageHeight || integration.Unit != AnalysisAxisUnit.Millimeter
                    || integration.Metrics is not { } metrics || assessment.MaximumRadius is not { } maximum
                    || !double.IsFinite(metrics.RmsSpotRadius) || !double.IsFinite(maximum)
                    || metrics.RmsSpotRadius < 0 || maximum < 0 || integration.RayCount <= 0
                    || integration.Wavelengths.Count == 0 || integration.Wavelengths.Any(wave => wave.AttemptedRayCount <= 0))
                { outcome = "UnavailablePhysicalMetrics"; break; }
                var valid = Math.Min((double)(integration.RayCount - integration.VignettedRayCount) / integration.RayCount,
                    integration.Wavelengths.Min(wave => (double)wave.Rays.Count / wave.AttemptedRayCount));
                minimumValid = Math.Min(minimumValid ?? 1, valid);
                boundaryPassed &= boundary.RayCount > 0 && boundary.VignettedRayCount == 0;
                worstRms = Math.Max(worstRms ?? 0, metrics.RmsSpotRadius);
                worstMaximum = Math.Max(worstMaximum ?? 0, maximum);
                completed++;
            }
        }
        catch (OperationCanceledException) { outcome = "Cancelled"; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { outcome = "RayBudgetExhausted"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { outcome = "PhysicalDomainRejected"; }
        if (outcome == "Screened" && (minimumValid is null || minimumValid < targets.MinimumValidRayFraction || !boundaryPassed))
            outcome = "ThroughputRejected";
        var score = outcome == "Screened" && completed == fields.Length
            ? Math.Max(worstRms!.Value / targets.MaximumRmsRadiusMillimeters,
                worstMaximum!.Value / targets.MaximumSpotRadiusMillimeters) : (double?)null;
        return new(fingerprint, outcome, completed, fields.Length, worstRms, worstMaximum,
            minimumValid, boundaryPassed, score, measured.RayCount);
    }

    internal static List<DesignTopology> Select(
        IReadOnlyList<(DesignTopology Topology, double Score)> pool,
        IReadOnlyList<SeedImageScreeningResult> screens, int count,
        SeedImageScreeningOptions options, TopologyTargets targets)
    {
        var byIdentity = screens.Where(screen => screen.Outcome == "Screened"
                && screen.CompletedFields == screen.ExpectedFields && screen.QualityScore is { } quality && double.IsFinite(quality))
            .ToDictionary(screen => screen.TopologyFingerprint);
        var eligible = pool.Where(item => byIdentity.ContainsKey(TopologyDiversitySelection.PrescriptionIdentity(item.Topology)))
            .OrderBy(item => byIdentity[TopologyDiversitySelection.PrescriptionIdentity(item.Topology)].QualityScore)
            .ThenBy(item => TopologyDiversitySelection.PrescriptionIdentity(item.Topology), StringComparer.Ordinal).ToList();
        var selected = new List<DesignTopology>();
        while (eligible.Count > 0 && selected.Count < count)
        {
            var bestScore = byIdentity[TopologyDiversitySelection.PrescriptionIdentity(eligible[0].Topology)].QualityScore!.Value;
            var window = bestScore + Math.Max(1e-12, bestScore * options.RelativeQualityWindow);
            var nearBest = eligible.Where(item => byIdentity[TopologyDiversitySelection.PrescriptionIdentity(item.Topology)].QualityScore <= window);
            var chosen = selected.Count == 0 ? eligible[0] : nearBest
                .OrderByDescending(item => selected.Min(seed => TopologyDiversitySelection.Distance(item.Topology, seed, targets)))
                .ThenBy(item => byIdentity[TopologyDiversitySelection.PrescriptionIdentity(item.Topology)].QualityScore)
                .ThenBy(item => TopologyDiversitySelection.PrescriptionIdentity(item.Topology), StringComparer.Ordinal).First();
            selected.Add(chosen.Topology);
            eligible.Remove(chosen);
        }
        return selected;
    }
}
