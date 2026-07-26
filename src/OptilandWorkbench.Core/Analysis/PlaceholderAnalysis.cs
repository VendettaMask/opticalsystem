namespace OptilandWorkbench.Core.Analysis;

public class PlaceholderAnalysis : BaseAnalysis
{
    public PlaceholderAnalysis(Optic optic, string name) : base(optic)
    {
        Name = name;
    }

    public override string Name { get; }

    public override AnalysisData GenerateData()
    {
        var spot = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["WeightedMetric"] = spot.RmsSpotRadius,
            ["Status"] = "framework-ready"
        });
    }
}
