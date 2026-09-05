using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class YYbarAnalysis : BaseAnalysis
{
    private readonly bool _zemaxCompatible;

    public YYbarAnalysis(Optic optic, bool zemaxCompatible = true) : base(optic)
    {
        _zemaxCompatible = zemaxCompatible;
    }

    public override string Name => "Y-Ybar";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null)
        {
            return AnalysisData.Unavailable(Name, "No wavelengths");
        }

        var marginal = Optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var chief = Optic.Paraxial.ChiefRay(wavelength.Micrometers);
        var ya = marginal.Heights.Select(values => values[0]).ToArray();
        var yb = chief.Heights.Select(values => values[0]).ToArray();
        var maximumFieldY = Optic.Fields
            .OrderByDescending(field => Math.Abs(field.Y))
            .Select(field => field.Y)
            .FirstOrDefault();
        if (_zemaxCompatible
            && Optic.FieldDefinition is FieldDefinitionKind.ParaxialImageHeight or FieldDefinitionKind.RealImageHeight
            && Math.Abs(yb[^1]) > 1e-15)
        {
            // Zemax defines Y-bar using the paraxial chief ray normalized to the
            // selected image-height field. The raw unit-chief trace has an arbitrary
            // launch scale, so its image-surface height must not be plotted directly.
            var chiefScale = maximumFieldY / yb[^1];
            yb = yb.Select(height => height * chiefScale).ToArray();
        }

        if (!_zemaxCompatible)
        {
            return GenerateLegacyData(wavelength.Micrometers, ya, yb);
        }

        var stopIndex = Optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var lineSeries = Enumerable.Range(1, Math.Max(0, Optic.SurfaceGroup.Items.Count - 2))
            .Select(index =>
            {
                var surface = Optic.SurfaceGroup.Items[index];
                return new AnalysisSeries(
                    "Ybar (mm)",
                    "Y (mm)",
                    new[]
                    {
                        new AnalysisPoint(yb[index], ya[index]),
                        new AnalysisPoint(yb[index + 1], ya[index + 1])
                    },
                    Name: index == stopIndex ? "Stop" : "",
                    ColorIndex: index - 1,
                    ShowMarkers: false,
                    XQuantity: AnalysisAxisQuantity.RayHeight,
                    XUnit: AnalysisAxisUnit.Millimeter,
                    YQuantity: AnalysisAxisQuantity.RayHeight,
                    YUnit: AnalysisAxisUnit.Millimeter);
            }).ToArray();
        var labels = new AnalysisSeries(
            "Ybar (mm)",
            "Y (mm)",
            Enumerable.Range(1, Math.Max(0, Optic.SurfaceGroup.Items.Count - 1))
                .Select(index => new AnalysisPoint(yb[index], ya[index], index.ToString()))
                .ToArray(),
            Kind: AnalysisSeriesKind.Scatter,
            Name: "Surfaces",
            ColorIndex: 0,
            ShowMarkers: true,
            MarkerSize: 2,
            XQuantity: AnalysisAxisQuantity.RayHeight,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.RayHeight,
            YUnit: AnalysisAxisUnit.Millimeter);
        var series = lineSeries.Append(labels).ToArray();
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelength.Micrometers,
            ["FirstSurface"] = 1,
            ["LastSurface"] = Optic.SurfaceGroup.Items.Count - 1,
            ["SurfaceCount"] = Math.Max(0, Optic.SurfaceGroup.Items.Count - 1)
        };
        for (var index = 1; index < ya.Length; index++)
        {
            values[$"Surface {index} Marginal"] = ya[index];
            values[$"Surface {index} Chief"] = yb[index];
        }

        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: "Y",
            EqualAspect: true,
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            VerticalZeroLineWidth: 0.5,
            XMinimum: -5,
            XMaximum: 5,
            YMinimum: -5,
            YMaximum: 5,
            ShowLegend: false,
            DefaultSquareViewport: true));
    }

    private AnalysisData GenerateLegacyData(
        double wavelengthMicrometers,
        IReadOnlyList<double> marginalHeights,
        IReadOnlyList<double> chiefHeights)
    {
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
                        new AnalysisPoint(chiefHeights[index - 1], marginalHeights[index - 1]),
                        new AnalysisPoint(chiefHeights[index], marginalHeights[index])
                    },
                    Name: name,
                    ColorIndex: index - 1,
                    ShowMarkers: true,
                    MarkerSize: 4,
                    XQuantity: AnalysisAxisQuantity.RayHeight,
                    XUnit: AnalysisAxisUnit.Millimeter,
                    YQuantity: AnalysisAxisQuantity.RayHeight,
                    YUnit: AnalysisAxisUnit.Millimeter);
            }).ToArray();
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelengthMicrometers,
            ["SurfaceCount"] = Optic.SurfaceGroup.Items.Count
        };
        for (var index = 0; index < marginalHeights.Count; index++)
        {
            values[$"Surface {index} Marginal"] = marginalHeights[index];
            values[$"Surface {index} Chief"] = chiefHeights[index];
        }

        return new AnalysisData(Name, values, series.FirstOrDefault(), series, new AnalysisPlotOptions(
            Title: $"Y Y-bar Diagram (λ={wavelengthMicrometers:0.000} µm)",
            ShowVerticalZeroLine: true,
            ShowHorizontalZeroLine: true,
            VerticalZeroLineWidth: 0.5,
            ShowLegend: true));
    }
}
