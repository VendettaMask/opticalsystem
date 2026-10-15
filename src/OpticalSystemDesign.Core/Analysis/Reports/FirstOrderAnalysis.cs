using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Apertures;
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
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = Optic.Paraxial.EstimateEffectiveFocalLength(),
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        });
    }
}
