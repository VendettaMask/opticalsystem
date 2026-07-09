using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public abstract class BaseAnalysis
{
    protected BaseAnalysis(Optic optic)
    {
        Optic = optic;
    }

    protected Optic Optic { get; }

    public abstract string Name { get; }

    public abstract AnalysisData GenerateData();
}

public sealed record AnalysisData(string Name, IReadOnlyDictionary<string, object> Values)
{
    public string ExportText()
    {
        return string.Join(Environment.NewLine, Values.Select(item => $"{item.Key}: {item.Value}"));
    }
}

public sealed class SpotDiagramAnalysis : BaseAnalysis
{
    public SpotDiagramAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Spot Diagram";

    public override AnalysisData GenerateData()
    {
        var summary = new AnalysisRunner(Optic).EvaluateSpotDiagram();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["RayCount"] = summary.RayCount,
            ["VignettedRayCount"] = summary.VignettedRayCount,
            ["Centroid"] = summary.Centroid,
            ["RmsSpotRadius"] = summary.RmsSpotRadius,
            ["MaxSpotRadius"] = summary.MaxSpotRadius
        });
    }
}

public sealed class RayFanAnalysis : BaseAnalysis
{
    public RayFanAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Ray Fan";

    public override AnalysisData GenerateData()
    {
        var fan = new AnalysisRunner(Optic).BuildRayFan();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["Samples"] = fan.Count,
            ["Min"] = fan.Count == 0 ? 0 : fan.Min(),
            ["Max"] = fan.Count == 0 ? 0 : fan.Max()
        });
    }
}

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

public sealed class PlaceholderAnalysis : BaseAnalysis
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

public sealed class AnalysisCatalog
{
    private readonly Optic _optic;

    public AnalysisCatalog(Optic optic)
    {
        _optic = optic;
    }

    public IReadOnlyList<string> Names { get; } = new[]
    {
        "Spot Diagram",
        "Ray Fan",
        "Distortion",
        "Grid Distortion",
        "Field Curvature",
        "Encircled Energy",
        "Pupil Aberration",
        "RMS vs Field",
        "Through Focus",
        "Y-Ybar",
        "PSF",
        "MTF",
        "Wavefront",
        "Zernike",
        "Image Simulation",
        "Jones Pupil",
        "Prescription Report"
    };

    public BaseAnalysis Create(string name)
    {
        return name switch
        {
            "Spot Diagram" => new SpotDiagramAnalysis(_optic),
            "Ray Fan" => new RayFanAnalysis(_optic),
            "First Order" => new FirstOrderAnalysis(_optic),
            _ => new PlaceholderAnalysis(_optic, name)
        };
    }
}
