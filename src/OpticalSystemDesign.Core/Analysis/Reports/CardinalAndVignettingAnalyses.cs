using System.Globalization;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

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
            ["TotalTrack"] = Optic.SurfaceGroup.TotalTrack,
            ["CardinalPositionsMillimeters"] = new[]
            {
                new[] { -focalLength, focalLength }, new[] { objectFocalPlane, imageFocalPlane },
                new[] { objectPrincipalPlane, imagePrincipalPlane }, new[] { objectAntiPrincipalPlane, imageAntiPrincipalPlane },
                new[] { objectNodalPlane, imageNodalPlane }, new[] { objectAntiPrincipalPlane, imageAntiPrincipalPlane }
            }
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
    private readonly int _rayDensity;
    private readonly int _fieldDensity;

    public VignettingDiagramAnalysis(Optic optic, int rayDensity = 10, int fieldDensity = 21) : base(optic)
    {
        _rayDensity = Math.Clamp(rayDensity, 5, 128);
        _fieldDensity = Math.Clamp(fieldDensity, 2, 201);
    }

    public override string Name => "Vignetting Diagram";

    public override AnalysisData GenerateData()
    {
        var wavelength = Optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? Optic.Wavelengths.FirstOrDefault();
        if (wavelength is null || Optic.SurfaceGroup.Items.Count < 2)
            return AnalysisData.Unavailable(Name, "需要分析波长及像面。");

        var fields = AnalysisTrace.ScanFieldSamples(Optic, "+y", _fieldDensity);
        if (fields[^1].Coordinate == 0) fields = [fields[0]];
        var side = 2 * _rayDensity + 1;
        if ((long)side * side * fields.Count * Optic.SurfaceGroup.Items.Count > AnalysisResourceLimits.MaximumAnalysisRayWork)
            return AnalysisData.Unavailable(Name, "渐晕图采样超过光线追迹预算。");

        // Use the normalized grid step for the pupil boundary test, as in the native sampling.
        var step = 1d / _rayDensity;
        var pupil = new List<PupilSample>();
        for (var row = -_rayDensity; row <= _rayDensity; row++)
        for (var column = -_rayDensity; column <= _rayDensity; column++)
        {
            var x = column * step;
            var y = row * step;
            if (x * x + y * y <= 1) pupil.Add(new PupilSample(x, y, 1));
        }

        // Geometric acceptance counts rays equally, independent of pupil
        // illumination, field-editor compression and material transmission.
        var workingOptic = Optic;
        if (Optic.Apodization is not (null or UniformApodization or ZemaxApodization { Type: ZemaxApodizationType.Uniform }))
        {
            workingOptic = Optic.CreateMeritEvaluationCopy();
            workingOptic.Apodization = null;
        }
        var tracer = workingOptic.SequentialRayTracer;
        var token = ComputationCancellation.Current;
        var points = new List<AnalysisPoint>(fields.Count);
        foreach (var field in fields)
        {
            token.ThrowIfCancellationRequested();
            var bundle = tracer.RayGenerator.GenerateNormalizedPupilSamples(field.Hx, field.Hy, wavelength.Micrometers,
                pupil, aimAtStop: workingOptic.RayAimingEnabled, applyVignettingFactors: false);
            var accepted = 0;
            foreach (var ray in bundle.Rays)
            {
                token.ThrowIfCancellationRequested();
                var trace = tracer.DiagnoseFirstFailure(ray, token, ignoreScalarTransmission: true);
                if (trace.Failure is SurfaceTraceFailure.RayBudgetExhausted or SurfaceTraceFailure.Unsupported or SurfaceTraceFailure.DomainError)
                    return AnalysisData.Unavailable(Name, trace.Detail ?? trace.Failure.ToString());
                if (trace.ReachedImage) accepted++;
            }
            token.ThrowIfCancellationRequested();
            points.Add(new AnalysisPoint(field.Coordinate, (double)accepted / pupil.Count));
        }

        var fieldUnit = AnalysisTrace.FieldAxisUnit(Optic);
        var fieldLabel = fieldUnit == AnalysisAxisUnit.Degree ? "视场 (°)" : "视场 (mm)";
        const string fractionLabel = "未渐晕光线分数";
        var curve = new AnalysisSeries(fieldLabel, fractionLabel, points, ShowMarkers: points.Count == 1,
            XQuantity: AnalysisTrace.FieldAxisQuantity(Optic), XUnit: fieldUnit,
            YQuantity: AnalysisAxisQuantity.Transmission, YUnit: AnalysisAxisUnit.Dimensionless);
        var rows = points.Select(point => (IReadOnlyList<string>)
            [point.X.ToString("F6", CultureInfo.InvariantCulture), point.Y.ToString("F6", CultureInfo.InvariantCulture)]).ToArray();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["FieldCount"] = fields.Count,
            ["RaysPerField"] = pupil.Count,
            ["WavelengthMicrometers"] = wavelength.Micrometers
        }, curve, [curve],
            new AnalysisPlotOptions(XMinimum: fields.Count > 1 ? 0 : null,
                XMaximum: fields.Count > 1 ? fields[^1].Coordinate : null,
                YMinimum: 0, YMaximum: 1, HideTopAndRightAxes: true),
            Table: new AnalysisTable([fieldLabel, fractionLabel], rows));
    }
}
