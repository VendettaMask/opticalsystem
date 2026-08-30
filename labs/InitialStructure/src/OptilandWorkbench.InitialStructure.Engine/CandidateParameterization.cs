using OptilandWorkbench.Core;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal sealed class CandidateParameterization
{
    private readonly InitialStructureSpecification _specification;
    private readonly CandidateSnapshot _parent;
    private readonly int _elementCount;
    private readonly double _maximumCurvature;
    private readonly double _centerThicknessRange;
    private readonly double _airGapRange;

    public CandidateParameterization(
        InitialStructureSpecification specification,
        CandidateSnapshot parent)
    {
        _specification = specification;
        _parent = parent;
        _elementCount = parent.Lineage.ElementCount;
        _maximumCurvature = Math.Clamp(
            8 / specification.EffectiveFocalLengthMillimeters,
            0.02,
            0.5);

        var variableThicknessCount = (_elementCount * 2) - 1;
        var minimumStructuralTrack = (_elementCount * specification.MinimumCenterThicknessMillimeters)
            + ((_elementCount - 1) * specification.MinimumAirGapMillimeters);
        var availableSlack = Math.Max(
            0,
            specification.MaximumTrackLengthMillimeters
            - specification.MinimumBackFocusMillimeters
            - minimumStructuralTrack);
        var sharedSlack = availableSlack / variableThicknessCount;
        _centerThicknessRange = sharedSlack;
        _airGapRange = sharedSlack;
    }

    public int Dimension => (_elementCount * 4) - 1;

    public double[] ReadParentVector()
    {
        var vector = new double[Dimension];
        var offset = 0;
        for (var surfaceIndex = 1; surfaceIndex <= _elementCount * 2; surfaceIndex++)
        {
            var radius = _parent.Optic.Surfaces[surfaceIndex].Radius;
            var curvature = Math.Abs(radius) < 1e-12 ? 0 : 1 / radius;
            vector[offset++] = Math.Clamp(curvature / _maximumCurvature, -1, 1);
        }

        for (var elementIndex = 0; elementIndex < _elementCount; elementIndex++)
        {
            var thickness = _parent.Optic.Surfaces[1 + (elementIndex * 2)].Thickness;
            vector[offset++] = NormalizeThickness(
                thickness,
                _specification.MinimumCenterThicknessMillimeters,
                _centerThicknessRange);
        }

        for (var elementIndex = 0; elementIndex < _elementCount - 1; elementIndex++)
        {
            var gap = _parent.Optic.Surfaces[2 + (elementIndex * 2)].Thickness;
            vector[offset++] = NormalizeThickness(
                gap,
                _specification.MinimumAirGapMillimeters,
                _airGapRange);
        }

        return vector;
    }

    public Optic CreateOptic(IReadOnlyList<double> normalizedVector)
    {
        if (normalizedVector.Count != Dimension)
        {
            throw new ArgumentException(
                $"Expected {Dimension} parameters, received {normalizedVector.Count}.",
                nameof(normalizedVector));
        }

        var optic = Optic.FromSnapshot(_parent.Optic);
        var offset = 0;
        for (var surfaceIndex = 1; surfaceIndex <= _elementCount * 2; surfaceIndex++)
        {
            var normalized = RequireFiniteAndClamp(normalizedVector[offset++]);
            optic.SurfaceGroup.Items[surfaceIndex].Radius =
                FirstOrderSeedGenerator.RadiusFromCurvature(normalized * _maximumCurvature);
        }

        for (var elementIndex = 0; elementIndex < _elementCount; elementIndex++)
        {
            optic.SurfaceGroup.Items[1 + (elementIndex * 2)].Thickness = DenormalizeThickness(
                normalizedVector[offset++],
                _specification.MinimumCenterThicknessMillimeters,
                _centerThicknessRange);
        }

        for (var elementIndex = 0; elementIndex < _elementCount - 1; elementIndex++)
        {
            optic.SurfaceGroup.Items[2 + (elementIndex * 2)].Thickness = DenormalizeThickness(
                normalizedVector[offset++],
                _specification.MinimumAirGapMillimeters,
                _airGapRange);
        }

        optic.SurfaceGroup.Renumber();
        FirstOrderSeedGenerator.RecoverEffectiveFocalLength(
            optic,
            _specification.EffectiveFocalLengthMillimeters,
            _elementCount);
        FirstOrderSeedGenerator.RecoverImagePlane(optic, _specification, _elementCount);
        return optic;
    }

    public static double Clamp(double value) => Math.Clamp(value, -1, 1);

    private static double NormalizeThickness(double value, double minimum, double range) =>
        range <= 1e-12
            ? -1
            : Math.Clamp((((value - minimum) / range) * 2) - 1, -1, 1);

    private static double DenormalizeThickness(double normalized, double minimum, double range) =>
        minimum + (((RequireFiniteAndClamp(normalized) + 1) / 2) * range);

    private static double RequireFiniteAndClamp(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Candidate parameters must be finite.");
        }

        return Clamp(value);
    }
}
