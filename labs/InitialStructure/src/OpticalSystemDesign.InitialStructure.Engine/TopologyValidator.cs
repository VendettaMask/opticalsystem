using System.Collections.Immutable;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Fresh physical Core CPU acceptance; training coordinates and loss do not supply this result.</summary>
public static class TopologyValidator
{
    public static string GetProtocolFingerprint(CompiledTopology source, TopologyTargets targets)
    {
        var fields = TopologyEvaluationPlan.Fields(source, 21);
        // Protocol identity describes the algorithm/settings, not libm's last bits in generated samples.
        // Actual acceptance continues to generate and trace the original shared Core sample set.
        return ContentFingerprint.Compute(new { Identity = "autonomous-physical-validation-v2", targets,
            StructuralConstraints = source.Constraints, fields,
            Pupils = new { Algorithm = "Core.GaussianQuadrature.v1", RadialSamples = 10, AzimuthalSamples = 32, Obscuration = 0d },
            Checks = new { Algorithm = "center-and-unit-rim-v1", AzimuthalSamples = 32 },
            ProfileRadialSamples = 64, Backend = "CPU", Reference = "common-polychromatic-centroid",
            DistortionReference = "primary-chief/current-image-paraxial", IncludeSurfaceTransmission = false });
    }

    internal static string GetNativeLegacyProtocolFingerprint(CompiledTopology source, TopologyTargets targets)
    {
        var fields = TopologyEvaluationPlan.Fields(source, 21);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
        var checks = TopologyEvaluationPlan.Checks();
        return ContentFingerprint.Compute(new { Identity = "autonomous-physical-validation-v1", targets, StructuralConstraints = source.Constraints, fields, pupils, checks, ProfileRadialSamples = 64, Backend = "CPU", Reference = "common-polychromatic-centroid", DistortionReference = "primary-chief/current-image-paraxial", IncludeSurfaceTransmission = false });
    }

    internal static bool MatchesProtocol(CompiledTopology source, TopologyTargets targets, string fingerprint)
    {
        var portable = GetProtocolFingerprint(source, targets);
        return fingerprint == portable || fingerprint == GetNativeLegacyProtocolFingerprint(source, targets)
            || HistoricalTopologyProtocols.Matches(portable, fingerprint);
    }

    public static TopologyAssessment Validate(CompiledTopology source, TopologyTargets targets, CancellationToken cancellationToken = default)
    {
        TopologyEvaluationPlan.Validate(targets, new());
        var fields = TopologyEvaluationPlan.Fields(source, 21);
        var rows = TopologyEvaluationPlan.Geometry(source, targets, 64);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
        var checks = TopologyEvaluationPlan.Checks();
        var protocol = GetProtocolFingerprint(source, targets);
        var violations = new List<TopologyMetricViolation>();
        var assessments = new List<TopologyFieldAssessment>();
        using var measurement = SequentialTraceMeasurement.Begin();
        long probes = 0;
        double? focal = null, fNumber = null;
        string? failure = null;
        var completed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, cancellationToken);
            var optic = source.CreateOptic();
            focal = Finite(optic.Paraxial.EstimateEffectiveFocalLength());
            fNumber = Finite(optic.Paraxial.EstimateFNumber());
            Upper("focal-length", focal is > 0 ? Math.Abs(focal.Value / targets.EffectiveFocalLengthMillimeters - 1) : null, targets.FocalLengthRelativeTolerance);
            Upper("f-number", fNumber is > 0 ? Math.Abs(fNumber.Value / targets.FNumber - 1) : null, targets.FNumberRelativeTolerance);
            if (source.Topology.HasTransitionElements) violations.Add(new("transition-element", 1, 0));
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                probes++;
                var value = Finite(TopologyEvaluationPlan.FormalMeasure(optic, row));
                Upper("geometry:" + row.Id, value is null ? null : row.Upper ? value - row.Limit : row.Limit - value, 0);
            }
            foreach (var field in fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var physical = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, field.X, field.Y, pupils, checks,
                    includeSurfaceTransmission: false, cancellationToken: cancellationToken);
                var integration = physical.Integration; var boundary = physical.Checks;
                var valid = integration.RayCount - integration.VignettedRayCount;
                var checkValid = boundary.RayCount - boundary.VignettedRayCount;
                double? distortion = null;
                try { distortion = Finite(Core.Services.DistortionMetrics.ParaxialChiefFractionAtField(optic, field.X, field.Y)); }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { /* An unavailable physical chief is a hard failed metric below. */ }
                var waveFractions = integration.Wavelengths.Select(wave => (double)wave.Rays.Count / wave.AttemptedRayCount).ToImmutableArray();
                assessments.Add(new(field, integration.RayCount, valid, boundary.RayCount, checkValid,
                    integration.Metrics?.RmsSpotRadius, physical.MaximumRadius, distortion, waveFractions));
                var prefix = "field:" + (assessments.Count - 1) + ":";
                Upper(prefix + "lost-fraction", 1 - (double)valid / integration.RayCount, 1 - targets.MinimumValidRayFraction);
                for (var index = 0; index < waveFractions.Length; index++) Upper(prefix + "wave:" + index + ":lost-fraction", 1 - waveFractions[index], 1 - targets.MinimumValidRayFraction);
                Upper(prefix + "check-lost", boundary.RayCount - checkValid, 0);
                Upper(prefix + "rms", integration.Metrics?.RmsSpotRadius, targets.MaximumRmsRadiusMillimeters);
                Upper(prefix + "maximum", physical.MaximumRadius, targets.MaximumSpotRadiusMillimeters);
                Upper(prefix + "distortion", distortion, targets.MaximumDistortionFraction);
            }
            completed = true;
        }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) || error is OperationCanceledException) { failure = error.Message; }
        return new()
        {
            OpticFingerprint = source.OpticFingerprint,
            ProtocolFingerprint = protocol,
            Targets = targets,
            StructuralConstraints = source.Constraints,
            EffectiveFocalLengthMillimeters = focal,
            FNumber = fNumber,
            Fields = assessments.ToImmutableArray(),
            Violations = violations.ToImmutableArray(),
            Completed = completed,
            MeetsTargets = completed && violations.Count == 0,
            Failure = failure,
            TracedRayCount = measurement.RayCount,
            GeometryProbes = probes
        };

        void Upper(string code, double? actual, double limit)
        {
            if (actual is null || !double.IsFinite(actual.Value) || actual.Value > limit)
                violations.Add(new(code, actual, limit));
        }
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
