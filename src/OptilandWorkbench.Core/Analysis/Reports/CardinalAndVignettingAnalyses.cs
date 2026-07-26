namespace OptilandWorkbench.Core.Analysis;

public sealed class CardinalPointsDataAnalysis : BaseAnalysis
{
    private readonly int _referenceSurfaceNumber;

    public CardinalPointsDataAnalysis(Optic optic, int referenceSurfaceNumber = -1) : base(optic)
    {
        _referenceSurfaceNumber = referenceSurfaceNumber;
    }

    public override string Name => "Cardinal Points Data";

    public override AnalysisData GenerateData()
    {
        var cardinal = Optic.Paraxial.EstimateCardinalPoints();
        var focalLength = Math.Abs(cardinal.EffectiveFocalLength);
        var objectFocalPlane = cardinal.FrontFocalPosition - cardinal.FirstReferencePosition;
        var imageFocalPlane = cardinal.BackFocalPosition - cardinal.LastReferencePosition;
        var objectPrincipalPlane = cardinal.FrontPrincipalPlanePosition - cardinal.FirstReferencePosition;
        var imagePrincipalPlane = cardinal.BackPrincipalPlanePosition - cardinal.LastReferencePosition;
        var objectAntiPrincipalPlane = objectPrincipalPlane - (2 * focalLength);
        var imageAntiPrincipalPlane = imagePrincipalPlane + (2 * focalLength);
        var objectNodalPlane = cardinal.FrontNodalPlanePosition - cardinal.FirstReferencePosition;
        var imageNodalPlane = cardinal.BackNodalPlanePosition - cardinal.LastReferencePosition;
        var primaryWavelength = Optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        var referenceSurface = Optic.SurfaceGroup.Items.FirstOrDefault(surface =>
                surface.Number == _referenceSurfaceNumber)
            ?? Optic.SurfaceGroup.Items.LastOrDefault();
        var values = new Dictionary<string, object>
        {
            ["ReferenceSurfaceNumber"] = referenceSurface?.Number ?? 0,
            ["ReferenceSurfaceLabel"] = referenceSurface?.Label ?? string.Empty,
            ["ReferenceSurfacePosition"] = referenceSurface?.CoordinateSystem.Origin.Z ?? 0,
            ["StartSurface"] = Math.Min(1, Math.Max(0, Optic.SurfaceGroup.Items.Count - 1)),
            ["EndSurface"] = Math.Max(0, Optic.SurfaceGroup.Items.Count - 1),
            ["WavelengthMicrometers"] = primaryWavelength?.Micrometers ?? 0,
            ["Direction"] = "Y-Z",
            ["LensUnit"] = "毫米",
            ["EffectiveFocalLength"] = cardinal.EffectiveFocalLength,
            ["FNumber"] = Optic.Paraxial.EstimateFNumber(),
            ["EntrancePupilDiameter"] = Optic.Paraxial.EstimateEntrancePupilDiameter(),
            ["EntrancePupilLocation"] = Optic.Paraxial.EstimateEntrancePupilLocation(),
            ["ExitPupilDiameter"] = Optic.Paraxial.EstimateExitPupilDiameter(),
            ["ExitPupilLocation"] = Optic.Paraxial.EstimateExitPupilLocation(),
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack
        };
        var tableRows = new[]
        {
            Row("焦长", -focalLength, focalLength),
            Row("焦平面", objectFocalPlane, imageFocalPlane),
            Row("主平面", objectPrincipalPlane, imagePrincipalPlane),
            Row("反主平面", objectAntiPrincipalPlane, imageAntiPrincipalPlane),
            Row("节平面", objectNodalPlane, imageNodalPlane),
            Row("反节平面", objectAntiPrincipalPlane, imageAntiPrincipalPlane)
        };
        return new AnalysisData(
            Name,
            values,
            Table: new AnalysisTable(
                new[] { "基面量", "物空间", "像空间" },
                tableRows),
            ReportText: string.Join(Environment.NewLine, new[]
            {
                "基面数据概要",
                string.Empty,
                $"起始面：{values["StartSurface"]}",
                $"终止面：{values["EndSurface"]}",
                $"波长：{Convert.ToDouble(values["WavelengthMicrometers"]):0.000000}",
                "方向：Y-Z",
                "透镜单位：毫米",
                string.Empty,
                "物空间位置相对于起始面测量。",
                "像空间位置相对于终止面测量。",
                "物空间和像空间折射率均已考虑。",
                string.Empty,
                "基面量\t物空间\t像空间"
            }.Concat(tableRows.Select(row => string.Join('\t', row)))));
    }

    private static IReadOnlyList<string> Row(string label, double objectSpace, double imageSpace)
    {
        return new[]
        {
            label,
            objectSpace.ToString("0.######"),
            imageSpace.ToString("0.######")
        };
    }
}

public sealed class VignettingDiagramAnalysis : BaseAnalysis
{
    public VignettingDiagramAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Vignetting Diagram";

    public override AnalysisData GenerateData()
    {
        var fields = AnalysisTrace.DefinedFieldSamples(Optic);
        var series = new[]
        {
            new AnalysisSeries(
                AnalysisTrace.FieldAxisLabel(Optic),
                "渐晕（%）",
                fields.Select(field => new AnalysisPoint(
                    field.Coordinate,
                    100 * Math.Clamp(Optic.Fields[field.Index].VignetteFactorX, 0, 1),
                    field.Label)).ToArray(),
                Name: "X 渐晕",
                ColorIndex: 0,
                ShowMarkers: true),
            new AnalysisSeries(
                AnalysisTrace.FieldAxisLabel(Optic),
                "渐晕（%）",
                fields.Select(field => new AnalysisPoint(
                    field.Coordinate,
                    100 * Math.Clamp(Optic.Fields[field.Index].VignetteFactorY, 0, 1),
                    field.Label)).ToArray(),
                Name: "Y 渐晕",
                ColorIndex: 3,
                ShowMarkers: true)
        };
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FieldCount"] = fields.Count,
                ["MaximumXVignettingPercent"] = series[0].Points.Select(point => point.Y).DefaultIfEmpty(0).Max(),
                ["MaximumYVignettingPercent"] = series[1].Points.Select(point => point.Y).DefaultIfEmpty(0).Max()
            },
            series[0],
            series,
            new AnalysisPlotOptions(
                Title: "渐晕图",
                XMinimum: fields.Select(field => field.Coordinate).DefaultIfEmpty(0).Min(),
                XMaximum: fields.Select(field => field.Coordinate).DefaultIfEmpty(1).Max(),
                YMinimum: 0,
                YMaximum: 100,
                ShowLegend: true,
                GridOpacity: 0.25,
                LegendBelow: true));
    }
}
