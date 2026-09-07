using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
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
        _maximumScaledCurvature = 0.9 * specification.EffectiveFocalLengthMillimeters / optic.SurfaceGroup.Items[1].SemiDiameter;
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
        cancellationToken.ThrowIfCancellationRequested();
        var violations = GeometryViolations(optic);
        var residuals = new List<double>();
        var power = optic.Paraxial.EstimateOpticalPower();
        residuals.Add(5 * (power * _specification.EffectiveFocalLengthMillimeters - 1));
        var pupils = Pupils(dense).ToArray();
        var residualScale = 1 / (PupilRadius * Math.Sqrt(pupils.Length));
        SampledSpotResult? spot = null;
        if (violations.Count == 0)
        {
            var primary = optic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1;
            spot = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0,
                pupils.Select(pupil => new PupilSample(pupil.X, pupil.Y, 1)).ToArray(), primary,
                reference: "absolute", includeSurfaceTransmission: false, cancellationToken: cancellationToken);
            TracedRayCount += spot.RayCount;
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
        return new FlatStartEvaluation
        {
            OpticalPowerPerMillimeter = power,
            Merit = residuals.Sum(value => value * value),
            RmsInterceptMillimeters = spot?.Metrics?.RmsSpotRadius,
            ValidRayFraction = (double)valid / pupils.Length,
            Residuals = residuals.ToArray(),
            Violations = violations
        };
    }

    public bool IsFocused(FlatStartEvaluation evaluation) => evaluation.Violations.Count == 0
        && evaluation.ValidRayFraction == 1
        && Math.Abs(evaluation.OpticalPowerPerMillimeter * _specification.EffectiveFocalLengthMillimeters - 1) <= 0.02
        && evaluation.RmsInterceptMillimeters / PupilRadius <= 0.02;

    internal IReadOnlyList<ConstraintViolation> GeometryViolations(Optic optic)
    {
        var violations = new List<ConstraintViolation>();
        for (var element = 0; element < _elementCount; element++)
        {
            var front = optic.SurfaceGroup.Items[2 * element + 1];
            var back = optic.SurfaceGroup.Items[2 * element + 2];
            var radius = Math.Max(front.SemiDiameter, back.SemiDiameter);
            var edge = front.Thickness + back.Geometry.Sag(0, radius) - front.Geometry.Sag(0, radius);
            Minimum(front.Thickness, _specification.MinimumCenterThicknessMillimeters, "geometry.center-thickness");
            Minimum(Math.Min(front.Thickness, edge), _specification.FlatStart!.MinimumEdgeThicknessMillimeters, "geometry.edge-thickness");
            if (element < _elementCount - 1)
            {
                var next = optic.SurfaceGroup.Items[2 * element + 3];
                Minimum(back.Thickness, _specification.MinimumAirGapMillimeters, "geometry.air-gap");
                Minimum(back.Thickness + next.Geometry.Sag(0, radius) - back.Geometry.Sag(0, radius), 0, "geometry.edge-clearance");
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

        void Minimum(double actual, double minimum, string code)
        {
            if (!double.IsFinite(actual) || actual < minimum - 1e-9)
                violations.Add(new(code, ConstraintSeverity.Hard, "Geometry is outside the configured bound.", double.IsFinite(actual) ? actual : null, minimum));
        }
    }

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
