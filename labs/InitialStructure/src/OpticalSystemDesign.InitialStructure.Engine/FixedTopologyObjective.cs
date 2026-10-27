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
    private readonly PupilSample[] _boundaryPupils;
    private readonly bool _includeBoundaryMaximum;
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
        _boundaryPupils = sampling.IncludeBoundaryClearance == true ? TopologyEvaluationPlan.Checks() : [];
        _includeBoundaryMaximum = sampling.IncludeBoundaryMaximum == true;
        var rays = _pupils.Length * source.Topology.Wavelengths.Count(wave => wave.Weight > 0);
        var ids = new List<string> { "target:power", "target:inverse-f-number" };
        ids.AddRange(_geometry.Select(row => "geometry:" + row.Id));
        for (var field = 0; field < Fields.Length; field++)
        {
            ids.AddRange(new[] { $"field:{field}:rms", $"field:{field}:maximum", $"field:{field}:distortion" });
            for (var ray = 0; ray < rays; ray++) ids.AddRange(new[] { $"field:{field}:ray:{ray}:clearance", $"field:{field}:ray:{ray}:x", $"field:{field}:ray:{ray}:y" });
        }
        for (var field = 0; field < Fields.Length; field++)
            for (var ray = 0; ray < _boundaryPupils.Length * source.Topology.Wavelengths.Count(wave => wave.Weight > 0); ray++)
                ids.Add($"field:{field}:boundary:{ray}:clearance");
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
        bool includeJacobian = false, CancellationToken cancellationToken = default, bool includeConstraintTangents = false)
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        long probes = 0;
        SequentialGpuSession? session = null;
        CompiledTopology? compiled = null;
        DifferentiableOpticalResult? optical = null;
        DifferentiableOpticalResult? boundary = null;
        TopologyObjectiveEvaluation result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            compiled = TopologyParameterBinder.Apply(_source, _binding, coordinates);
            var optic = compiled.CreateOptic();
            session = new SequentialGpuSession(computeMode, cancellationToken);
            optical = DifferentiableOpticalMetrics.EvaluateAtFields(optic, _binding.CoreParameters,
                Fields.Select(field => (field.X, field.Y)).ToArray(), _pupils, cancellationToken, computeMode, usePhysicalStop: true);
            if (_boundaryPupils.Length > 0)
                boundary = DifferentiableOpticalMetrics.EvaluateAtFields(optic, _binding.CoreParameters,
                    Fields.Select(field => (field.X, field.Y)).ToArray(), _boundaryPupils, cancellationToken, computeMode, usePhysicalStop: true);
            var power = optical.OpticalPowerPerMillimeter;
            var residuals = new List<DifferentialScalar>
            {
                (power * _targets.EffectiveFocalLengthMillimeters - 1) / _targets.FocalLengthRelativeTolerance,
                (optical.InverseFNumber * _targets.FNumber - 1) / _targets.FNumberRelativeTolerance
            };
            var constraints = new List<(int Row, DifferentialScalar Value)>();
            if (includeConstraintTangents) { constraints.Add((0, residuals[0])); constraints.Add((1, residuals[1])); }
            foreach (var row in _geometry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                probes++;
                var value = TopologyEvaluationPlan.Measure(optic, _binding.CoreParameters, row);
                if (includeConstraintTangents) constraints.Add((residuals.Count, (row.Upper ? value - row.Limit : row.Limit - value) / (row.Slope ? 1 : .01)));
                residuals.Add(PositivePart(row.Upper ? value - row.Limit : row.Limit - value) / (row.Slope ? 1 : .01));
            }
            for (var fieldIndex = 0; fieldIndex < optical.Fields.Count; fieldIndex++)
            {
                var field = optical.Fields[fieldIndex];
                var maximum = _includeBoundaryMaximum
                    ? DifferentiableOpticalMetrics.MaximumRadiusWithChecks(field, boundary!.Fields[fieldIndex]) : field.MaximumRadiusMillimeters;
                if (includeConstraintTangents)
                {
                    constraints.Add((residuals.Count, field.RmsRadiusMillimeters / _targets.MaximumRmsRadiusMillimeters - 1));
                    constraints.Add((residuals.Count + 1, maximum / _targets.MaximumSpotRadiusMillimeters - 1));
                    constraints.Add((residuals.Count + 2, field.DistortionSquaredFraction / (_targets.MaximumDistortionFraction * _targets.MaximumDistortionFraction) - 1));
                }
                residuals.Add(PositivePart(field.RmsRadiusMillimeters / _targets.MaximumRmsRadiusMillimeters - 1));
                residuals.Add(PositivePart(maximum / _targets.MaximumSpotRadiusMillimeters - 1));
                residuals.Add(PositivePart(field.DistortionSquaredFraction /
                    (_targets.MaximumDistortionFraction * _targets.MaximumDistortionFraction) - 1));
                var weight = field.Rays.Sum(ray => ray.Weight);
                foreach (var ray in field.Rays)
                {
                    var scale = Math.Sqrt(ray.Weight / weight / optical.Fields.Count) / _targets.MaximumRmsRadiusMillimeters;
                    if (includeConstraintTangents) constraints.Add((residuals.Count, -ray.MinimumClearanceMillimeters * scale));
                    residuals.Add(PositivePart(-ray.MinimumClearanceMillimeters) * scale);
                    residuals.Add((ray.X - field.CentroidX) * scale);
                    residuals.Add((ray.Y - field.CentroidY) * scale);
                }
            }
            if (boundary is not null)
                foreach (var field in boundary.Fields)
                {
                    var weight = field.Rays.Sum(ray => ray.Weight);
                    foreach (var ray in field.Rays)
                    {
                        // Same 0.01 mm constraint scale as the geometry rows; no aperture enlargement or acceptance allowance.
                        var scale = Math.Sqrt(ray.Weight / weight / boundary.Fields.Count) / .01;
                        if (includeConstraintTangents) constraints.Add((residuals.Count, -ray.MinimumClearanceMillimeters * scale));
                        residuals.Add(PositivePart(-ray.MinimumClearanceMillimeters) * scale);
                    }
                }
            if (residuals.Count != ResidualIds.Length || residuals.Any(value => !value.IsFinite) || constraints.Any(item => !item.Value.IsFinite))
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
                Jacobian = includeJacobian ? residuals.Select(value => Enumerable.Range(0, _binding.Slots.Length).Select(value.Derivative).ToImmutableArray()).ToImmutableArray() : [],
                ConstraintTangents = includeConstraintTangents ? constraints.Select(item => new TopologyConstraintTangent(item.Row, item.Value.Value,
                    Enumerable.Range(0, _binding.Slots.Length).Select(item.Value.Derivative).ToImmutableArray())).ToImmutableArray() : null
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
            Execution = _boundaryPupils.Length == 0 ? optical?.Execution : boundary is null ? null : Merge(optical!.Execution, boundary.Execution),
            PropagationExecution = session?.Execution
        };
    }

    private static DesignComputeExecution Merge(DesignComputeExecution first, DesignComputeExecution second) => new(
        first.Backend == second.Backend ? first.Backend : "mixed-design-derivatives",
        first.DeviceName == second.DeviceName ? first.DeviceName : first.DeviceName + "; " + second.DeviceName,
        first.GpuBatches + second.GpuBatches, first.GpuRays + second.GpuRays, first.GpuMilliseconds + second.GpuMilliseconds)
    { FallbackReason = string.Join("; ", new[] { first.FallbackReason, second.FallbackReason }.Where(value => value is not null).Distinct()) is { Length: > 0 } reason ? reason : null };
}
