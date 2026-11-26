using System.Globalization;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Real chief-ray deviation from the local object and image optical axes.</summary>
public sealed class TelecentricityAnalysis : BaseAnalysis
{
    private readonly int _wavelengthNumber;

    public TelecentricityAnalysis(Optic optic, int wavelengthNumber = 0) : base(optic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wavelengthNumber);
        _wavelengthNumber = wavelengthNumber;
    }

    public override string Name => "Telecentricity";

    public override AnalysisData GenerateData()
    {
        if (Optic.Fields.Count == 0 || Optic.Wavelengths.Count == 0 || Optic.SurfaceGroup.Items.Count < 2)
            return AnalysisData.Unavailable(Name, "No optical data");
        if (_wavelengthNumber > Optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(_wavelengthNumber));

        // A chief ray targets the stop centre, independent of pupil vignetting
        // offsets. Work on a detached copy; never alter the user's prescription.
        var optic = AnalysisTrace.PrepareVignettingFactors(Optic, true);
        var surfaces = optic.SurfaceGroup.Items;
        var last = surfaces.Count - 1;
        var waves = optic.Wavelengths.Select((wave, index) => (Wave: wave, Number: index + 1))
            .Where(item => _wavelengthNumber == 0 || item.Number == _wavelengthNumber).ToArray();
        var rows = new List<IReadOnlyList<string>>();
        var series = new List<AnalysisSeries>();
        var objectAngles = new List<double>();
        var imageAngles = new List<double>();
        var invalid = 0;
        foreach (var (wave, number) in waves)
        {
            var objectPoints = new List<AnalysisPoint>();
            var imagePoints = new List<AnalysisPoint>();
            for (var index = 0; index < optic.Fields.Count; index++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var field = optic.Fields[index];
                var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
                var objectAngle = Angles.Unavailable;
                var imageAngle = Angles.Unavailable;
                var status = "主光线未到达像面";
                try
                {
                    var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                        normalized.X, normalized.Y, 0, 0, wave.Micrometers, aimAtStop: true);
                    using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.IncidentSurface(last));
                    if (bundle.Rays[0].IsAlive && trace.TryGetSample(0, last, out var hit)
                        && !hit.Vignetted && hit.Intensity > 0)
                    {
                        objectAngle = Measure(surfaces[0].CoordinateSystem.ToLocalDirection(bundle.Rays[0].Direction));
                        // Use arrival direction, not refraction at the image or
                        // a curved detector's normal. The reference is local Z.
                        imageAngle = Measure(surfaces[last].CoordinateSystem.ToLocalDirection(hit.Direction));
                        if (double.IsFinite(objectAngle.Total) && double.IsFinite(imageAngle.Total))
                        {
                            objectAngles.Add(objectAngle.Total);
                            imageAngles.Add(imageAngle.Total);
                            status = "有效";
                        }
                    }
                }
                catch (RayAimingException)
                {
                    status = "主光线瞄准失败";
                }
                if (status != "有效")
                {
                    invalid++;
                    objectAngle = imageAngle = Angles.Unavailable;
                }
                var label = $"Field {index + 1}";
                objectPoints.Add(new AnalysisPoint(index + 1, objectAngle.Total, label));
                imagePoints.Add(new AnalysisPoint(index + 1, imageAngle.Total, label));
                rows.Add(new[] { (index + 1).ToString(CultureInfo.InvariantCulture), F(field.X), F(field.Y),
                    number.ToString(CultureInfo.InvariantCulture), F(wave.Micrometers),
                    F(objectAngle.X), F(objectAngle.Y), F(objectAngle.Total),
                    F(imageAngle.X), F(imageAngle.Y), F(imageAngle.Total), status });
            }
            var wavelengthLabel = $"λ{number} ({F(wave.Micrometers)} µm)";
            series.Add(Curve($"物方: {wavelengthLabel}", $"object:{number}", objectPoints, number - 1, AnalysisLineStyle.Dashed));
            series.Add(Curve($"像方: {wavelengthLabel}", $"image:{number}", imagePoints, number - 1, AnalysisLineStyle.Solid));
        }
        var valid = imageAngles.Count;
        var objectMaximum = objectAngles.DefaultIfEmpty(double.NaN).Max();
        var imageMaximum = imageAngles.DefaultIfEmpty(double.NaN).Max();
        return new AnalysisData(Name, new Dictionary<string, object>
        {
            ["MaximumObjectTelecentricityDegrees"] = objectMaximum,
            ["MaximumImageTelecentricityDegrees"] = imageMaximum,
            ["ValidChiefRayCount"] = valid,
            ["InvalidChiefRayCount"] = invalid,
            ["FieldCount"] = optic.Fields.Count,
            ["WavelengthCount"] = waves.Length,
            ["WavelengthNumber"] = _wavelengthNumber
        }, SeriesList: series,
            PlotOptions: new AnalysisPlotOptions(ShowLegend: true, XMinimum: .5,
                XMaximum: optic.Fields.Count + .5, YMinimum: 0, DottedGrid: true),
            Table: new AnalysisTable(new[] { "视场编号",
                optic.FieldDefinition == FieldDefinitionKind.Angle ? "视场 X (°)" : "视场 X (mm)",
                optic.FieldDefinition == FieldDefinitionKind.Angle ? "视场 Y (°)" : "视场 Y (mm)", "波长编号", "波长 (µm)",
                "物方 X 角 (°)", "物方 Y 角 (°)", "物方远心角 (°)",
                "像方 X 角 (°)", "像方 Y 角 (°)", "像方远心角 (°)", "状态" }, rows),
            ReportText: $"远心度\n物方最大远心角 (°): {F(objectMaximum)}\n像方最大远心角 (°): {F(imageMaximum)}\n"
                + $"有效主光线: {valid}\n无效主光线: {invalid}\n"
                + "按当前定义的视场编号和所选波长逐点测量，不作波长加权平均。最大值仅覆盖这些采样点。\n"
                + "远心角为主光线与对应空间局部 Z 轴的锐夹角，0° 为理想远心；X/Y 为带符号的投影角。",
            Outcome: valid > 0 ? AnalysisOutcome.Success : AnalysisOutcome.Unavailable,
            OutcomeReason: valid > 0 ? null : "主光线未到达像面");
    }

    private static AnalysisSeries Curve(string name, string key, IReadOnlyList<AnalysisPoint> points, int color, AnalysisLineStyle style)
        => new("视场编号", "远心角 (°)", points, Name: name, ColorIndex: color, LineStyle: style, ShowMarkers: true,
            LegendKey: key, LegendLabel: name,
            XQuantity: AnalysisAxisQuantity.FieldNumber, XUnit: AnalysisAxisUnit.Dimensionless,
            YQuantity: AnalysisAxisQuantity.Angle, YUnit: AnalysisAxisUnit.Degree);

    private static Angles Measure(Vector3D direction)
    {
        if (!double.IsFinite(direction.Length) || direction.Length <= 1e-15) return Angles.Unavailable;
        var axial = Math.Abs(direction.Z);
        const double degrees = 180 / Math.PI;
        return new(Math.Atan2(direction.X, axial) * degrees, Math.Atan2(direction.Y, axial) * degrees,
            Math.Atan2(Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y), axial) * degrees);
    }

    private static string F(double value) => double.IsFinite(value) ? value.ToString("G10", CultureInfo.InvariantCulture) : "—";
    private readonly record struct Angles(double X, double Y, double Total)
    {
        public static Angles Unavailable => new(double.NaN, double.NaN, double.NaN);
    }
}
