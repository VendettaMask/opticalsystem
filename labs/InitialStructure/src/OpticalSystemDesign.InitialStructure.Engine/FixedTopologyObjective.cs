using System.Collections.Immutable;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.Core.Differentiation.DifferentialScalar;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Fixed ray-indexed residuals and target arithmetic; every optical/geometric value comes from Core.</summary>
public sealed class FixedTopologyObjective
{
    private readonly CompiledTopology _source;
    private readonly BoundTopologyParameters _binding;
    private readonly TopologyTargets _targets;
    private readonly ImmutableArray<TopologyGeometryRow> _geometry;
    private readonly PupilSample[] _pupils;
    public ImmutableArray<TopologyFieldCoordinate> Fields { get; }
    public ImmutableArray<string> ResidualIds { get; }
    public string Fingerprint { get; }

    public FixedTopologyObjective(CompiledTopology source, BoundTopologyParameters binding, TopologyTargets targets,
        TopologyTrainingSampling? sampling = null)
    {
        sampling ??= new();
        TopologyEvaluationPlan.Validate(targets, sampling);
        Require(source.BindingFingerprint == binding.SourceFingerprint, TopologyFailureCode.IdentityMismatch, "The binding belongs to another compiled prescription.");
        Require(source.Topology.RayAimingEnabled, TopologyFailureCode.UnsupportedFeature, "This optimizer requires explicit stop aiming; it cannot change a task's aiming setting.");
        _source = source; _binding = binding; _targets = targets;
        Fields = TopologyEvaluationPlan.Fields(source, sampling.FieldsPerDirection);
        _ = TopologyEvaluationPlan.Fields(source, 21); // Reject an unassessable final protocol before consuming training work.
        _geometry = TopologyEvaluationPlan.Geometry(source, targets, sampling.ProfileRadialSamples);
        _pupils = ApertureSampler.GenerateGaussianQuadrature(sampling.PupilRings, sampling.PupilArms).ToArray();
        var rays = _pupils.Length * source.Topology.Wavelengths.Count(wave => wave.Weight > 0);
        var ids = new List<string> { "target:power", "target:inverse-f-number" };
        ids.AddRange(_geometry.Select(row => "geometry:" + row.Id));
        for (var field = 0; field < Fields.Length; field++)
        {
            ids.AddRange(new[] { $"field:{field}:rms", $"field:{field}:maximum", $"field:{field}:distortion" });
            for (var ray = 0; ray < rays; ray++) ids.AddRange(new[] { $"field:{field}:ray:{ray}:clearance", $"field:{field}:ray:{ray}:x", $"field:{field}:ray:{ray}:y" });
        }
        Require((long)ids.Count * binding.Slots.Length <= 4_000_000, TopologyFailureCode.InvalidInput, "The fixed Jacobian exceeds the bounded working set.");
        ResidualIds = ids.ToImmutableArray();
        Fingerprint = ContentFingerprint.Compute(new
        {
            Identity = "autonomous-fixed-residual-v1",
            binding.Fingerprint,
            targets,
            sampling,
            Fields,
            ResidualIds,
            GeometryScaleMillimeters = .01,
            SlopeScale = 1,
            DistortionReference = "primary-chief/current-image-paraxial",
            ImageReference = "common-polychromatic-centroid",
            IncludeSurfaceTransmission = false,
            FieldWeights = "equal-evaluation-points"
        });
    }

    public TopologyObjectiveEvaluation Evaluate(IReadOnlyList<double> coordinates, DesignComputeMode computeMode = DesignComputeMode.Cpu,
        bool includeJacobian = false, CancellationToken cancellationToken = default)
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        long probes = 0;
        SequentialGpuSession? session = null;
        CompiledTopology? compiled = null;
        DifferentiableOpticalResult? optical = null;
        TopologyObjectiveEvaluation result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            compiled = TopologyParameterBinder.Apply(_source, _binding, coordinates);
            var optic = compiled.CreateOptic();
            session = new SequentialGpuSession(computeMode, cancellationToken);
            optical = DifferentiableOpticalMetrics.EvaluateAtFields(optic, _binding.CoreParameters,
                Fields.Select(field => (field.X, field.Y)).ToArray(), _pupils, cancellationToken, computeMode, usePhysicalStop: true);
            var power = optical.OpticalPowerPerMillimeter;
            var residuals = new List<DifferentialScalar>
            {
                (power * _targets.EffectiveFocalLengthMillimeters - 1) / _targets.FocalLengthRelativeTolerance,
                (optical.InverseFNumber * _targets.FNumber - 1) / _targets.FNumberRelativeTolerance
            };
            foreach (var row in _geometry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                probes++;
                var value = TopologyEvaluationPlan.Measure(optic, _binding.CoreParameters, row);
                residuals.Add(PositivePart(row.Upper ? value - row.Limit : row.Limit - value) / (row.Slope ? 1 : .01));
            }
            foreach (var field in optical.Fields)
            {
                residuals.Add(PositivePart(field.RmsRadiusMillimeters / _targets.MaximumRmsRadiusMillimeters - 1));
                residuals.Add(PositivePart(field.MaximumRadiusMillimeters / _targets.MaximumSpotRadiusMillimeters - 1));
                residuals.Add(PositivePart(field.DistortionSquaredFraction /
                    (_targets.MaximumDistortionFraction * _targets.MaximumDistortionFraction) - 1));
                var weight = field.Rays.Sum(ray => ray.Weight);
                foreach (var ray in field.Rays)
                {
                    var scale = Math.Sqrt(ray.Weight / weight / optical.Fields.Count) / _targets.MaximumRmsRadiusMillimeters;
                    residuals.Add(PositivePart(-ray.MinimumClearanceMillimeters) * scale);
                    residuals.Add((ray.X - field.CentroidX) * scale);
                    residuals.Add((ray.Y - field.CentroidY) * scale);
                }
            }
            if (residuals.Count != ResidualIds.Length || residuals.Any(value => !value.IsFinite))
                throw new ArithmeticException("A fixed residual slot or complete tangent is unavailable.");
            DifferentialScalar objective = 0;
            foreach (var value in residuals) objective += Square(value);
            if (!objective.IsFinite) throw new ArithmeticException("The full objective or its tangent is non-finite.");
            result = new()
            {
                LayoutFingerprint = Fingerprint,
                OpticFingerprint = compiled.OpticFingerprint,
                Available = true,
                Residuals = residuals.Select(value => value.Value).ToImmutableArray(),
                Objective = objective.Value,
                Gradient = Enumerable.Range(0, _binding.Slots.Length).Select(objective.Derivative).ToImmutableArray(),
                Jacobian = includeJacobian ? residuals.Select(value => Enumerable.Range(0, _binding.Slots.Length).Select(value.Derivative).ToImmutableArray()).ToImmutableArray() : []
            };
        }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error) || error is OperationCanceledException)
        {
            result = new()
            {
                LayoutFingerprint = Fingerprint,
                OpticFingerprint = compiled?.OpticFingerprint ?? "",
                Residuals = Enumerable.Repeat(1e6, ResidualIds.Length).ToImmutableArray(),
                Available = false,
                Failure = error.Message
            };
        }
        finally { session?.Dispose(); }
        return result with
        {
            TracedRayCount = measurement.RayCount,
            GeometryProbes = probes,
            Execution = optical?.Execution,
            PropagationExecution = session?.Execution
        };
    }

}
