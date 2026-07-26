using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class YYbarAnalysis : BaseAnalysis
{
    public YYbarAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Y-Ybar";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return new AnalysisData(Name, new Dictionary<string, object> { ["Status"] = "No wavelengths" });
        }

        var marginal = Optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var chief = Optic.Paraxial.ChiefRay(wavelength.Micrometers);
        var ya = marginal.Heights.Select(values => values[0]).ToArray();
        var yb = chief.Heights.Select(values => values[0]).ToArray();
        var stopIndex = Optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var series = Enumerable.Range(1, Math.Max(0, Optic.SurfaceGroup.Items.Count - 1))
            .Select(index =>
            {
                var surface = Optic.SurfaceGroup.Items[index];
                var name = index == Optic.SurfaceGroup.Items.Count - 1
                    ? "Image"
                    : index == 1 || index == stopIndex
                        ? surface.Label + (index == stopIndex ? " (Stop)" : "")
                        : "";
                return new AnalysisSeries(
                    "Chief Ray Height (mm)",
                    "Marginal Ray Height (mm)",
                    new[]
                    {
                        new AnalysisPoint(yb[index - 1], ya[index - 1]),
                        new AnalysisPoint(yb[index], ya[index])
                    },
                    Name: name,
                    ColorIndex: index - 1,
                    ShowMarkers: true,
                    MarkerSize: 4);
            }).ToArray();
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count
        };
        for (var index = 0; index < ya.Length; index++)
        {
            values[$"Surface {index} Marginal"] = ya[index];
            values[$"Surface {index} Chief"] = yb[index];
        }

        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Y Y-bar Diagram (\u03BB={wavelength.Micrometers:0.000} \u00B5m)",
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            VerticalZeroLineWidth: 0.5,
            ShowLegend: true));
    }
}
