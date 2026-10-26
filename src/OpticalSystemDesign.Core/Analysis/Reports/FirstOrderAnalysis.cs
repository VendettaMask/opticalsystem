using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class FirstOrderAnalysis : BaseAnalysis
{
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
        var power = Optic.Paraxial.EstimateOpticalPower();
        var zeroPower = power == 0;
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = zeroPower ? double.PositiveInfinity : Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = zeroPower ? double.PositiveInfinity : Optic.Paraxial.EstimateFNumber(),
            ["OpticalPowerInverseMillimeters"] = power,
            ["EffectiveFocalLengthStatus"] = zeroPower ? "Infinite" : "Finite",
            ["CardinalParametersApplicable"] = true,
            ["ImageSpaceAfocal"] = Optic.ImageSpaceAfocal,
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}
