using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

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
