using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal sealed class FlatStartProblem
{
    private readonly InitialStructureSpecification _specification;
    private readonly int _elementCount;
    private readonly double _maximumBackFocus;
    private readonly double _maximumScaledCurvature;

    public FlatStartProblem(InitialStructureSpecification specification, int elementCount, double pupilFraction,
        FlatStartFamily? family = null)
    {
        SpecificationValidator.Validate(specification);
        if (specification.FlatStart is null) throw new ArgumentException("Flat-start settings are required.");
        if (pupilFraction is <= 0 or > 1 || !double.IsFinite(pupilFraction))
            throw new ArgumentOutOfRangeException(nameof(pupilFraction));
        _specification = specification;
        _elementCount = elementCount;
        var optic = Optic.FromSnapshot(new FlatRootFactory().Create(specification, elementCount, stopVariant: 0, family: family));
        // The target, not an undefined flat-system focal length, establishes a nonzero pupil.
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = specification.EffectiveFocalLengthMillimeters / specification.FNumber * pupilFraction;
        PupilRadius = optic.Aperture.Value / 2;
        foreach (var name in (family?.GlassNames ?? [specification.InitialGlass]).Distinct(StringComparer.OrdinalIgnoreCase))
            FlatStartFamilySupport.ValidateGlass(optic, name, specification);
        var last = optic.SurfaceGroup.Items[2 * elementCount];
        _maximumBackFocus = specification.MaximumTrackLengthMillimeters - last.CoordinateSystem.Origin.Z;
        last.Thickness = specification.FlatStart.FixedBackFocusMillimeters
            ?? Math.Clamp(specification.EffectiveFocalLengthMillimeters, specification.MinimumBackFocusMillimeters, _maximumBackFocus);
        foreach (var surface in optic.SurfaceGroup.Items.Skip(1).Take(2 * elementCount))
        {
            surface.SemiDiameterFixed = true;
            surface.SemiDiameterDefinesPhysicalAperture = true;
            surface.PhysicalAperture = new CircularAperture(surface.SemiDiameter);
        }
        optic.SurfaceGroup.Renumber();
        Root = optic.ToSnapshot();
        var first = optic.SurfaceGroup.Items[1];
        _maximumScaledCurvature = 0.9 * specification.EffectiveFocalLengthMillimeters /
            (specification.FlatStart.UsePhysicalStop ? Math.Max(first.SemiDiameter, first.MechanicalSemiDiameter) : first.SemiDiameter);
    }

    public OpticSnapshot Root { get; }
    public double PupilRadius { get; }
    public int Dimension => 2 * _elementCount + (_specification.FlatStart!.FixedBackFocusMillimeters is null ? 1 : 0);
    public long TracedRayCount { get; private set; }
    public double[] InitialVector()
    {
        var vector = new double[Dimension];
        if (Dimension > 2 * _elementCount)
            vector[^1] = Root.Surfaces[2 * _elementCount].Thickness / _specification.EffectiveFocalLengthMillimeters;
        return vector;
    }

    public double Bound(int index, double value) => index < 2 * _elementCount
        ? Math.Clamp(value, -_maximumScaledCurvature, _maximumScaledCurvature)
        : Math.Clamp(value, _specification.MinimumBackFocusMillimeters / _specification.EffectiveFocalLengthMillimeters,
            _maximumBackFocus / _specification.EffectiveFocalLengthMillimeters);

    public Optic CreateOptic(IReadOnlyList<double> vector)
    {
        if (vector.Count != Dimension || vector.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("The scaled parameter vector is invalid.", nameof(vector));
        var optic = Optic.FromSnapshot(Root);
        for (var index = 0; index < 2 * _elementCount; index++)
        {
            var curvature = Bound(index, vector[index]) / _specification.EffectiveFocalLengthMillimeters;
            optic.SurfaceGroup.Items[index + 1].Radius = curvature == 0 ? 0 : 1 / curvature;
        }
        if (Dimension > 2 * _elementCount)
            optic.SurfaceGroup.Items[2 * _elementCount].Thickness = Bound(Dimension - 1, vector[^1]) * _specification.EffectiveFocalLengthMillimeters;
        optic.SurfaceGroup.Renumber();
        return optic;
    }

    public FlatStartEvaluation Evaluate(Optic optic, bool dense, CancellationToken cancellationToken)
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        try { return EvaluateCore(optic, dense, cancellationToken); }
        finally { TracedRayCount += measurement.RayCount; }
    }

    private FlatStartEvaluation EvaluateCore(Optic optic, bool dense, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pupils = Pupils(dense).ToArray();
        var primary = optic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1;
        var stopFailure = PreparePhysicalStop(optic, _specification.FlatStart!);
        var sizing = stopFailure is null && _specification.FlatStart!.AutomaticLensDiameters
            ? AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, LensGroups(_elementCount), [(0, 0)],
                pupils.Select(p => new PupilSample(p.X, p.Y, 1)).ToArray(),
                _specification.SemiDiameterMarginFactor, primary, cancellationToken,
                preserveStopAperture: _specification.FlatStart.UsePhysicalStop) : null;
        var violations = GeometryViolations(optic).ToList();
        if (stopFailure is not null) violations.Add(stopFailure);
        if (sizing is { Applied: false }) violations.Add(ApertureSizingFailure(sizing));
        var residuals = new List<double>();
        var power = optic.Paraxial.EstimateOpticalPower();
        residuals.Add(5 * (power * _specification.EffectiveFocalLengthMillimeters - 1));
        if (_specification.FlatStart!.UsePhysicalStop) residuals.Add(stopFailure is null ? 0 : 10);
        var residualScale = 1 / (PupilRadius * Math.Sqrt(pupils.Length));
        SampledSpotResult? spot = null;
        if (violations.Count == 0)
        {
            spot = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0,
                pupils.Select(pupil => new PupilSample(pupil.X, pupil.Y, 1)).ToArray(), primary,
                reference: "absolute", includeSurfaceTransmission: false, cancellationToken: cancellationToken);
        }
        var valid = spot is null ? 0 : spot.RayCount - spot.VignettedRayCount;
        var rays = spot?.Wavelengths.SelectMany(wave => wave.Rays).ToArray() ?? [];
        // Incomplete bundles are rejected; a fixed-size penalty is a search residual, not an optical metric.
        for (var index = 0; index < pupils.Length; index++)
        {
            residuals.Add(valid == pupils.Length ? rays[index].X * residualScale : 10);
            residuals.Add(valid == pupils.Length ? rays[index].Y * residualScale : 10);
        }
        if (!double.IsFinite(power) || residuals.Any(value => !double.IsFinite(value)))
            throw new ArithmeticException("The startup residual is not finite.");
        var evaluation = new FlatStartEvaluation
        {
            EvaluatedOptic = optic.ToSnapshot(),
            OpticalPowerPerMillimeter = power,
            Merit = residuals.Sum(value => value * value),
            RmsInterceptMillimeters = spot?.Metrics?.RmsSpotRadius,
            ValidRayFraction = (double)valid / pupils.Length,
            Residuals = residuals.ToArray(),
            Violations = violations
        };
        return evaluation;
    }

    public bool IsFocused(FlatStartEvaluation evaluation) => evaluation.Violations.Count == 0
        && evaluation.ValidRayFraction == 1
        && Math.Abs(evaluation.OpticalPowerPerMillimeter * _specification.EffectiveFocalLengthMillimeters - 1) <= 0.02
        && evaluation.RmsInterceptMillimeters / PupilRadius <= 0.02;

    internal IReadOnlyList<ConstraintViolation> GeometryViolations(Optic optic, List<double>? residuals = null,
        double searchInteriorMillimeters = 0)
    {
        var violations = new List<ConstraintViolation>();
        for (var element = 0; element < _elementCount; element++)
        {
            var front = optic.SurfaceGroup.Items[2 * element + 1];
            var back = optic.SurfaceGroup.Items[2 * element + 2];
            var radius = Math.Max(BodyRadius(front), BodyRadius(back));
            var edge = AxialSurfaceSeparation.Evaluate(optic, 2 * element + 1, 0, radius);
            Minimum(front.Thickness, _specification.MinimumCenterThicknessMillimeters, "geometry.center-thickness");
            Minimum(Math.Min(front.Thickness, edge), _specification.FlatStart!.MinimumEdgeThicknessMillimeters, "geometry.edge-thickness");
            if (element < _elementCount - 1)
            {
                Minimum(back.Thickness, _specification.MinimumAirGapMillimeters, "geometry.air-gap");
                var nextRadius = Math.Max(BodyRadius(optic.SurfaceGroup.Items[2 * element + 3]),
                    BodyRadius(optic.SurfaceGroup.Items[2 * element + 4]));
                Minimum(AxialSurfaceSeparation.Evaluate(optic, 2 * element + 2, 0, Math.Min(radius, nextRadius)), 0, "geometry.edge-clearance");
            }
            else
            {
                Minimum(back.Thickness, _specification.MinimumBackFocusMillimeters, "geometry.back-focus");
                if (_specification.FlatStart.FixedBackFocusMillimeters is { } fixedBack && Math.Abs(back.Thickness - fixedBack) > 1e-9)
                    violations.Add(new("geometry.fixed-back-focus", ConstraintSeverity.Hard, "Fixed back-focus distance changed.", back.Thickness, fixedBack));
            }
        }
        Minimum(_specification.MaximumTrackLengthMillimeters - optic.SurfaceGroup.TotalTrack, 0, "geometry.maximum-track");
        return violations;

        double BodyRadius(OptilandWorkbench.Core.Domain.OpticalSurface surface) => _specification.FlatStart!.UsePhysicalStop
            ? Math.Max(surface.SemiDiameter, surface.MechanicalSemiDiameter) : surface.SemiDiameter;

        void Minimum(double actual, double minimum, string code)
        {
            var interior = code is "geometry.edge-thickness" or "geometry.edge-clearance" ? searchInteriorMillimeters : 0;
            residuals?.Add(double.IsFinite(actual)
                ? Math.Max(0, minimum + interior - actual) / _specification.EffectiveFocalLengthMillimeters : 1);
            if (!double.IsFinite(actual) || actual < minimum - 1e-9)
                violations.Add(new(code, ConstraintSeverity.Hard, "Geometry is outside the configured bound.", double.IsFinite(actual) ? actual : null, minimum));
        }
    }

    internal static ConstraintViolation? PreparePhysicalStop(Optic optic, FlatStartSettings settings)
    {
        if (!settings.UsePhysicalStop) return null;
        optic.InvalidateRayTraceCache();
        optic.RayAimingEnabled = true;
        var stop = optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.IsStop);
        var bodyRadius = stop?.MechanicalSemiDiameter;
        try
        {
            var result = PhysicalStopCalibration.Apply(optic);
            if (!settings.AutomaticLensDiameters && bodyRadius is { } body)
            {
                // A fixed lens body cannot silently expand to accommodate the new pupil.
                stop!.MechanicalSemiDiameter = body;
                if (result.ClearSemiDiameterMillimeters > body)
                    return new("geometry.stop-outside-body", ConstraintSeverity.Hard,
                        "The calibrated physical stop exceeds the fixed lens body.", null, body);
            }
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or ArithmeticException or NotSupportedException)
        {
            // Unavailable stop geometry is a hard optical-domain failure, not a fallback radius.
            return new("geometry.stop-calibration", ConstraintSeverity.Hard, error.Message, null, null);
        }
    }

    internal static IReadOnlyList<IReadOnlyList<int>> LensGroups(int elements) =>
        Enumerable.Range(0, elements).Select(element => (IReadOnlyList<int>)new[] { 2 * element + 1, 2 * element + 2 }).ToArray();

    internal static ConstraintViolation ApertureSizingFailure(RayEnvelopeSizingResult sizing) =>
        new("geometry.aperture-envelope-incomplete", ConstraintSeverity.Hard,
            "Automatic lens diameter needs complete real-ray propagation; no estimated size was applied.",
            sizing.CompletedRays, sizing.AttemptedRays);

    private static IEnumerable<(double X, double Y)> Pupils(bool dense)
    {
        yield return (0, 0);
        var rings = dense ? 3 : 2;
        var angles = dense ? 16 : 8;
        for (var ring = 1; ring <= rings; ring++)
            for (var angle = 0; angle < angles; angle++)
            {
                var theta = 2 * Math.PI * angle / angles;
                yield return ((double)ring / rings * Math.Cos(theta), (double)ring / rings * Math.Sin(theta));
            }
    }
}
