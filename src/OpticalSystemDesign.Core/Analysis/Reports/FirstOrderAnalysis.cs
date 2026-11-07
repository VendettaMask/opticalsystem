using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class FirstOrderAnalysis : BaseAnalysis
{
    /// <summary>Vertex powers and ordinary scalar indices; excludes affine chief-ray walk-off.</summary>
    public const string ReferenceConvention = "CenteredVertexOrdinaryIndexV1";
    public FirstOrderAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "First Order";

    public override AnalysisData GenerateData()
    {
        if (Optic.SurfaceGroup.Items.Any(surface => surface.Geometry is PolygonGroupGeometry))
        {
            const string reason = "A mixed sequential/non-sequential polygon group has no general paraxial equivalent. EFL and F-number are not applicable; use the physical ray and angular image-space analyses.";
            var values = new Dictionary<string, object>
            {
                ["CardinalParametersApplicable"] = false,
                ["ApplicabilityReason"] = reason,
                ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
                ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
            };
            if (Optic.Aperture.Kind == ApertureKind.EntrancePupilDiameter)
                values["EntrancePupilDiameter"] = Optic.Aperture.Value;
            return new AnalysisData(Name, values, ReportText: reason);
        }
        var (effective, imageSpace, power) = Optic.Paraxial.EstimateFirstOrderFocalLengths();
        var zeroPower = power == 0;
        var diameter = zeroPower ? 0 : Optic.Paraxial.EstimateEntrancePupilDiameter();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = effective,
            ["ImageSpaceEffectiveFocalLength"] = imageSpace,
            ["FNumber"] = zeroPower ? double.PositiveInfinity : diameter > 0 ? Math.Abs(effective) / diameter : 0,
            ["OpticalPowerInverseMillimeters"] = power,
            ["EffectiveFocalLengthStatus"] = zeroPower ? "Infinite" : "Finite",
            ["CardinalParametersApplicable"] = true,
            ["FirstOrderReferenceConvention"] = ReferenceConvention,
            ["IncludesChiefRayWalkOff"] = false,
            ["DirectionalMediumUsesOrdinaryIndex"] = Optic.SurfaceGroup.Items.Any(s =>
                s.MaterialAfter is UniaxialMaterial { TraceExtraordinary: true, ParaxialIgnore: false }),
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}
