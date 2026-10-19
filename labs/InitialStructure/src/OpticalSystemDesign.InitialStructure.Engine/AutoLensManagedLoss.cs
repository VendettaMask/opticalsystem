using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.Core.Differentiation.DifferentialScalar;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Target/residual assembly only. Core supplies all geometry, dispersion and ray quantities.</summary>
internal static class AutoLensManagedLoss
{
    public static DifferentialScalar Evaluate(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        DifferentiableOpticalResult optical, InitialStructureSpecification specification, int elements)
    {
        var settings = specification.FlatStart!;
        var focal = specification.EffectiveFocalLengthMillimeters;
        var loss = Square((optical.OpticalPowerPerMillimeter * focal - 1) / settings.EffectiveFocalLengthRelativeTolerance);
        DifferentialScalar regularization = 0;
        foreach (var field in optical.Fields)
        {
            loss += Square(field.RmsRadiusMillimeters / specification.MaximumRmsSpotRadiusMillimeters) / optical.Fields.Count;
            loss += Square(PositivePart(field.MaximumRadiusMillimeters / specification.MaximumSpotRadiusMillimeters - 1)) / optical.Fields.Count;
            var ideal = DifferentiableOpticalMetrics.FirstOrderImageHeight(optic, focal, field.NormalizedFieldY);
            if (Math.Abs(ideal) > 1e-9)
                regularization += Square(PositivePart(Abs((field.CentroidY - ideal) / ideal) - .03)) / optical.Fields.Count;
            var chiefLimit = Math.Cos(Math.Min(specification.MaximumFieldAngleDegrees, 40) * Math.PI / 180);
            regularization += Square(PositivePart(chiefLimit - field.ChiefRayDirectionZ)) / optical.Fields.Count;
            var fieldWeight = field.Rays.Sum(sample => sample.Weight);
            foreach (var ray in field.Rays)
            {
                var weight = ray.Weight / fieldWeight / optical.Fields.Count;
                regularization += weight * Square(PositivePart(-ray.MinimumClearanceMillimeters) / specification.MaximumRmsSpotRadiusMillimeters);
                regularization += weight * Square(PositivePart(Math.Cos(30 * Math.PI / 180) - ray.MinimumBendCosine));
            }
        }
        var surfaces = 2 * elements;
        for (var index = 1; index <= surfaces; index++)
        {
            var surface = optic.SurfaceGroup.Items[index];
            var radius = Math.Max(surface.SemiDiameter, surface.MechanicalSemiDiameter);
            var centerMinimum = index == surfaces ? specification.MinimumBackFocusMillimeters
                : index % 2 == 1 ? specification.MinimumCenterThicknessMillimeters
                : AutoLensManagedTopology.IsCemented(specification, index / 2 - 1) ? 0 : specification.MinimumAirGapMillimeters;
            var center = DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, index, index + 1, 0);
            regularization += Square(PositivePart(centerMinimum - center) / .01);
            const int points = 64;
            for (var sample = 1; sample <= points; sample++)
            {
                var radial = radius * sample / points;
                var edge = DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, index, index + 1, radial);
                var edgeMinimum = index == surfaces ? specification.MinimumBackFocusMillimeters
                    : index % 2 == 1 ? settings.MinimumEdgeThicknessMillimeters : 0;
                regularization += Square(PositivePart(edgeMinimum - edge) / .01) / points;
                var sag = DifferentiableOpticalMetrics.SurfaceSag(optic, parameters, index, radial);
                var slope = DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, index, radial);
                regularization += Square(PositivePart(Abs(sag) / (2 * radius) - .5)) / points;
                regularization += Square(PositivePart(Abs(slope) - 1)) / points;
            }
        }
        var track = DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, 1, surfaces + 1, 0);
        regularization += Square(PositivePart(track - specification.MaximumTrackLengthMillimeters) / .01);
        return loss + .1 * regularization;
    }
}
