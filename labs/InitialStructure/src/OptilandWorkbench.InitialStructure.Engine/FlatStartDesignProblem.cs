using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Parameter/target adapter only. All optical quantities come from formal Core.</summary>
internal sealed class FlatStartDesignProblem
{
    private readonly InitialStructureSpecification _specification;
    private readonly int _elementCount;
    private readonly FlatStartProblem _geometry;
    private readonly OpticSnapshot _template;
    private readonly double _focalLength;
    private readonly double _maximumCurvature;
    private int SurfaceCount => 2 * _elementCount;
    public int Dimension => 2 * SurfaceCount - (_specification.FlatStart!.FixedBackFocusMillimeters.HasValue ? 1 : 0);
    public long TracedRayCount { get; private set; }
    public static DesignStage FullStage { get; } = new(1, 1, true);
    public FlatStartSolverCoordinates SolverCoordinates => new(_specification, _elementCount, _maximumCurvature);

    public FlatStartDesignProblem(InitialStructureSpecification specification, int elementCount, OpticSnapshot template,
        FlatStartFamily? family = null)
    {
        _specification = specification;
        _elementCount = elementCount;
        _geometry = new(specification, elementCount, .3, family);
        _template = template;
        _focalLength = specification.EffectiveFocalLengthMillimeters;
        _maximumCurvature = .9 * _focalLength / template.Surfaces.Skip(1).Take(SurfaceCount).Max(surface =>
            specification.FlatStart!.UsePhysicalStop ? Math.Max(surface.SemiDiameter, surface.MechanicalSemiDiameter ?? surface.SemiDiameter) : surface.SemiDiameter);
    }

    public double[] Vector(OpticSnapshot snapshot)
    {
        var vector = new double[Dimension];
        for (var index = 0; index < SurfaceCount; index++)
        {
            var surface = snapshot.Surfaces[index + 1];
            vector[index] = surface.Radius == 0 ? 0 : _focalLength / surface.Radius;
            if (SurfaceCount + index < Dimension) vector[SurfaceCount + index] = surface.Thickness / _focalLength;
        }
        return Project(vector);
    }

    public double[] Project(IReadOnlyList<double> vector)
    {
        if (vector.Count != Dimension || vector.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Invalid design parameter vector.", nameof(vector));
        var result = vector.ToArray();
        for (var index = 0; index < SurfaceCount; index++)
            result[index] = Math.Clamp(result[index], -_maximumCurvature, _maximumCurvature);
        var fixedBack = _specification.FlatStart!.FixedBackFocusMillimeters ?? 0;
        var minimumTotal = fixedBack;
        var slackTotal = 0.0;
        for (var index = SurfaceCount; index < Dimension; index++)
        {
            var minimum = MinimumThickness(index - SurfaceCount);
            result[index] = Math.Clamp(result[index], minimum / _focalLength, _specification.MaximumTrackLengthMillimeters / _focalLength);
            minimumTotal += minimum;
            slackTotal += result[index] * _focalLength - minimum;
        }
        var available = Math.Max(0, _specification.MaximumTrackLengthMillimeters - minimumTotal);
        if (slackTotal > available)
            for (var index = SurfaceCount; index < Dimension; index++)
            {
                var minimum = MinimumThickness(index - SurfaceCount) / _focalLength;
                result[index] = minimum + (result[index] - minimum) * available / slackTotal;
            }
        return result;
    }

    private double MinimumThickness(int surfaceIndex) => surfaceIndex == SurfaceCount - 1
        ? _specification.MinimumBackFocusMillimeters
        : surfaceIndex % 2 == 0 ? _specification.MinimumCenterThicknessMillimeters : _specification.MinimumAirGapMillimeters;

    public Optic CreateOptic(IReadOnlyList<double> values, DesignStage stage)
    {
        var vector = Project(values);
        var optic = Optic.FromSnapshot(_template);
        optic.Aperture.Value = _focalLength / _specification.FNumber * stage.PupilFraction;
        for (var index = 0; index < SurfaceCount; index++)
        {
            var surface = optic.SurfaceGroup.Items[index + 1];
            surface.Radius = vector[index] == 0 ? 0 : _focalLength / vector[index];
            surface.Thickness = SurfaceCount + index < Dimension
                ? vector[SurfaceCount + index] * _focalLength : _specification.FlatStart!.FixedBackFocusMillimeters!.Value;
            surface.ThicknessVariable = SurfaceCount + index < Dimension;
        }
        optic.SurfaceGroup.Renumber();
        return optic;
    }

    public DesignEvaluation Evaluate(Optic optic, DesignStage stage, bool dense, CancellationToken cancellationToken,
        bool independentValidation = false)
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        try { return EvaluateCore(optic, stage, dense, cancellationToken, independentValidation); }
        finally { TracedRayCount += measurement.RayCount; }
    }

    private DesignEvaluation EvaluateCore(Optic optic, DesignStage stage, bool dense, CancellationToken cancellationToken,
        bool independentValidation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = _specification.FlatStart!.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1;
        double[] fields = _specification.MaximumFieldAngleDegrees == 0 || stage.FieldFraction == 0 ? [0]
            : area && independentValidation ? Enumerable.Range(0, 21).Select(i => i / 20.0).ToArray()
            : dense ? [0, .25, .5, .7, .85, 1] : [0, .5, 1];
        var pupils = IntegrationPupils(_specification.FlatStart.SamplingPolicy, dense, independentValidation);
        var checks = area ? CheckPupils(dense, independentValidation) : [];
        var wavelengthNumber = stage.AllWavelengths ? 0 : optic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1;
        var stopFailure = FlatStartProblem.PreparePhysicalStop(optic, _specification.FlatStart!);
        var sizing = stopFailure is null && _specification.FlatStart!.AutomaticLensDiameters
            ? AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, FlatStartProblem.LensGroups(_elementCount),
                fields.Select(field => (0.0, field * stage.FieldFraction)).ToArray(), pupils.Concat(checks).ToArray(),
                _specification.SemiDiameterMarginFactor, wavelengthNumber, cancellationToken,
                preserveStopAperture: _specification.FlatStart.UsePhysicalStop) : null;
        var geometryResiduals = new List<double>();
        var violations = _geometry.GeometryViolations(optic, geometryResiduals, area ? .01 : 0).ToList();
        if (_specification.FlatStart!.UsePhysicalStop) geometryResiduals.Add(stopFailure is null ? 0 : 1);
        if (stopFailure is not null) violations.Add(stopFailure);
        var geometryValid = violations.Count == 0;
        // Manufacturing bounds remain hard gates. Finite Core geometry/coordinates
        // may still supply derivatives across a bound during constrained search.
        var traceGeometry = area ? violations.All(v => v.Actual is not null) : geometryValid;
        if (sizing is { Applied: false }) violations.Add(FlatStartProblem.ApertureSizingFailure(sizing));
        var power = optic.Paraxial.EstimateOpticalPower();
        var efl = optic.Paraxial.EstimateEffectiveFocalLength();
        var fNumber = optic.Paraxial.EstimateFNumber();
        var constraints = new List<double>();
        var transmissionResiduals = new List<double>();
        var imageResiduals = new List<double>();
        // Retain diagnostic constraint vectors for audit/compatibility. Runtime
        // optical optimization uses the joint ray vector below in every phase.
        const double interior = 1e-6;
        constraints.Add(double.IsFinite(power)
            ? Math.Max(0, Math.Abs(power * _focalLength - 1) / _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance - 1 + interior)
            : 1);
        constraints.Add(efl > 0 && double.IsFinite(efl)
            ? Math.Max(0, Math.Abs(efl / _focalLength - 1) / _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance - 1 + interior) : 1);
        constraints.Add(fNumber > 0 && double.IsFinite(fNumber)
            ? Math.Max(0, Math.Abs(fNumber * stage.PupilFraction / _specification.FNumber - 1) / _specification.FlatStart!.FNumberRelativeTolerance - 1 + interior) : 1);
        var residuals = new List<double> { double.IsFinite(power)
            ? (power * _focalLength - 1) / _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance : 1e3 };
        if (area) residuals.AddRange(geometryResiduals.Select(value => value * _focalLength / .01));
        Upper("target.focal-length", efl > 0 ? Math.Abs(efl / _focalLength - 1) : null,
            _specification.FlatStart!.EffectiveFocalLengthRelativeTolerance);
        Upper("target.f-number", fNumber > 0 ? Math.Abs(fNumber * stage.PupilFraction / _specification.FNumber - 1) : null,
            _specification.FlatStart.FNumberRelativeTolerance);

        var fieldResults = new List<DesignFieldEvaluation>();
        var waves = optic.Wavelengths.Where(wave => wave.Weight > 0 && (stage.AllWavelengths || wave.IsPrimary)).ToArray();
        var throughputValid = geometryValid && sizing is not { Applied: false };
        var continuousResiduals = traceGeometry && sizing is not { Applied: false };
        foreach (var field in fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = field * stage.FieldFraction;
            SampledSpotResult? result = null;
            SampledSpotResult? checkResult = null;
            double? maximumRadius = null;
            if (traceGeometry)
            {
                if (area)
                {
                    var assessment = SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, 0, normalized, pupils, checks,
                        wavelengthNumber, includeSurfaceTransmission: false, cancellationToken: cancellationToken);
                    result = assessment.Integration;
                    checkResult = assessment.Checks;
                    maximumRadius = assessment.MaximumRadius;
                }
                else
                {
                    result = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, normalized, pupils, wavelengthNumber,
                        reference: "centroid", includeSurfaceTransmission: false, cancellationToken: cancellationToken);
                    maximumRadius = result.Metrics?.MaximumSpotRadius;
                }
            }
            var attempted = pupils.Length * waves.Length;
            var valid = result is null ? 0 : result.RayCount - result.VignettedRayCount;
            // Physical metrics above remain the sole acceptance authority. If clipping occurs,
            // formal Core can supply continuous image coordinates and aperture margins for recovery.
            SampledApertureDiagnosticResult? diagnostic = null;
            if (traceGeometry && valid != attempted)
            {
                diagnostic = SampledApertureDiagnostics.Evaluate(optic, 0, normalized, pupils, wavelengthNumber, cancellationToken);
            }
            var searchSpot = diagnostic?.UnclippedSpot ?? result;
            var hasCoordinates = searchSpot?.Metrics is not null && searchSpot.VignettedRayCount == 0;
            if (area) hasCoordinates &= checkResult is { Metrics: not null, VignettedRayCount: 0 };
            constraints.Add(searchSpot?.Metrics is { } rmsMetric
                ? Math.Max(0, rmsMetric.RmsSpotRadius / _specification.MaximumRmsSpotRadiusMillimeters - 1 + interior) : 1);
            constraints.Add(searchSpot?.Metrics is { } maxMetric
                ? Math.Max(0, maxMetric.MaximumSpotRadius / _specification.MaximumSpotRadiusMillimeters - 1 + interior) : 1);
            for (var index = 0; index < attempted; index++)
                transmissionResiduals.Add(diagnostic?.Rays[index].MinimumClearanceMillimeters is { } clearance
                    ? Math.Max(0, -clearance) / (_focalLength * Math.Sqrt(attempted)) : 0);
            continuousResiduals &= hasCoordinates;
            var fieldIndex = fieldResults.Count;
            var waveResults = waves.Select((wave, index) => new DesignWavelengthEvaluation(wave.Nanometers, pupils.Length,
                result?.Wavelengths[index].Rays.Count ?? 0)).ToArray();
            fieldResults.Add(new(normalized, normalized * _specification.MaximumFieldAngleDegrees, attempted, valid,
                result?.Metrics?.RmsSpotRadius, maximumRadius, waveResults)
            {
                CheckAttemptedRays = checks.Length * waves.Length,
                CheckValidRays = checkResult is null ? 0 : checkResult.RayCount - checkResult.VignettedRayCount
            });
            if (area)
            {
                // Boundary checks are not area weights; require every check to propagate.
                Upper($"field.{fieldIndex}.check-lost-rays", checks.Length * waves.Length - fieldResults[^1].CheckValidRays, 0);
                throughputValid &= fieldResults[^1].CheckValidRays == checks.Length * waves.Length;
            }
            Upper($"field.{fieldIndex}.lost-fraction", 1 - (double)valid / attempted, 1 - _specification.FlatStart.MinimumValidRayFraction);
            Upper($"field.{fieldIndex}.rms", result?.Metrics?.RmsSpotRadius, _specification.MaximumRmsSpotRadiusMillimeters);
            Upper($"field.{fieldIndex}.maximum-radius", maximumRadius, _specification.MaximumSpotRadiusMillimeters);
            // Every field's RMS limit is an active target. Minimizing the mean ray
            // energy alone can improve central fields while making the worst field worse.
            if (area) residuals.Add(searchSpot?.Metrics is { } rms
                ? Math.Max(0, rms.RmsSpotRadius / _specification.MaximumRmsSpotRadiusMillimeters - 1) : 10);
            // The formal maximum-radius metric is also an active search target, not merely an exit gate.
            var searchMaximum = area ? maximumRadius : searchSpot?.Metrics?.MaximumSpotRadius;
            residuals.Add(searchMaximum is { } maximum
                ? Math.Max(0, maximum / _specification.MaximumSpotRadiusMillimeters - 1) : 10);
            // Fixed ray-indexed residual slots; clipping must not remove or reorder Jacobian rows.
            for (var index = 0; index < attempted; index++)
                residuals.Add(diagnostic?.Rays[index].MinimumClearanceMillimeters is { } clearance
                    ? Math.Max(0, -clearance) * 10 / (_specification.MaximumRmsSpotRadiusMillimeters * Math.Sqrt(attempted)) : 0);
            foreach (var (wave, index) in waveResults.Select((wave, index) => (wave, index)))
                Upper($"field.{fieldIndex}.wave.{index}.lost-fraction", 1 - (double)wave.ValidRays / wave.AttemptedRays,
                    1 - _specification.FlatStart.MinimumValidRayFraction);
            // Incumbent ranking must use the frozen per-field/per-wave throughput gate too.
            // A hidden 100% rule discarded improvements that already met the specified 98% gate.
            throughputValid &= (double)valid / attempted + 1e-12 >= _specification.FlatStart.MinimumValidRayFraction
                && waveResults.All(wave => (double)wave.ValidRays / wave.AttemptedRays + 1e-12
                    >= _specification.FlatStart.MinimumValidRayFraction);
            if (!hasCoordinates)
            {
                residuals.AddRange(Enumerable.Repeat(10.0, 2 * attempted));
                imageResiduals.AddRange(Enumerable.Repeat(0.0, 2 * attempted));
            }
            else
            {
                var totalWeight = searchSpot!.Wavelengths.Sum(wave => wave.SpectralWeight * wave.Rays.Sum(ray => ray.Weight));
                foreach (var wave in searchSpot.Wavelengths)
                    foreach (var ray in wave.Rays)
                    {
                        var scale = Math.Sqrt(wave.SpectralWeight * ray.Weight / (totalWeight * fields.Length))
                            / _specification.MaximumRmsSpotRadiusMillimeters;
                        residuals.Add(ray.X * scale);
                        residuals.Add(ray.Y * scale);
                        imageResiduals.Add(ray.X * scale);
                        imageResiduals.Add(ray.Y * scale);
                    }
            }
        }
        var merit = residuals.Sum(value => value * value);
        var evaluation = new DesignEvaluation
        {
            EvaluatedOptic = optic.ToSnapshot(),
            EffectiveFocalLengthMillimeters = efl > 0 && double.IsFinite(efl) ? efl : null,
            FNumber = fNumber > 0 && double.IsFinite(fNumber) ? fNumber : null,
            Merit = merit,
            Objective = new(area ? FlatStartObjectiveKind.ConstrainedRealRaySumSquaresV2 : FlatStartObjectiveKind.RealRaySumSquaresV1,
                stage, dense, continuousResiduals, merit)
            {
                SamplingPolicy = _specification.FlatStart.SamplingPolicy,
                UsePhysicalStop = _specification.FlatStart.UsePhysicalStop,
                IndependentValidation = area && independentValidation
            },
            Residuals = residuals,
            Fields = fieldResults,
            Violations = violations,
            IsFeasible = throughputValid,
            HasContinuousSearchResiduals = continuousResiduals,
            GeometryResiduals = geometryResiduals,
            ConstraintResiduals = constraints,
            TransmissionResiduals = transmissionResiduals,
            ImageResiduals = imageResiduals,
            GeometryFeasible = geometryValid
        };
        return evaluation;

        void Upper(string code, double? actual, double limit)
        {
            if (actual is null || !double.IsFinite(actual.Value) || actual > limit + 1e-12)
                violations.Add(new(code, ConstraintSeverity.Hard, "The requested design bound was not met.",
                    actual is { } value && double.IsFinite(value) ? value : null, limit));
        }
    }

    internal static PupilSample[] IntegrationPupils(FlatStartSamplingPolicy policy, bool dense, bool validation = false) =>
        policy == FlatStartSamplingPolicy.LegacyEqualRings ? Pupils(dense).ToArray()
        : ApertureSampler.GenerateGaussianQuadrature(validation ? 10 : dense ? 6 : 3, validation ? 32 : dense ? 24 : 12).ToArray();

    internal static PupilSample[] CheckPupils(bool dense, bool validation = false) =>
        new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(validation ? 64 : dense ? 24 : 12, PupilSampling.Ring)).ToArray();

    internal static IEnumerable<PupilSample> Pupils(bool dense)
    {
        yield return new(0, 0, 1);
        var rings = dense ? 6 : 3;
        var angles = dense ? 16 : 8;
        for (var ring = 1; ring <= rings; ring++)
            for (var angle = 0; angle < angles; angle++)
            {
                var theta = 2 * Math.PI * angle / angles;
                yield return new((double)ring / rings * Math.Cos(theta), (double)ring / rings * Math.Sin(theta), 1);
            }
    }
}
