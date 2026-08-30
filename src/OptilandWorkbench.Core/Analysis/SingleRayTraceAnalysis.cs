using System.Globalization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Core.Analysis;

public sealed class SingleRayTraceAnalysis : BaseAnalysis
{
    private readonly int _fieldNumber;
    private readonly double _hx;
    private readonly double _hy;
    private readonly int _wavelengthNumber;
    private readonly double _px;
    private readonly double _py;
    private readonly bool _globalCoordinates;
    private readonly string _type;
    private readonly bool _useRayAiming;
    private readonly bool _showRaySegments;

    public SingleRayTraceAnalysis(
        Optic optic,
        int fieldNumber = 1,
        double hx = 0,
        double hy = 0,
        int wavelengthNumber = 1,
        double px = 0,
        double py = 0,
        bool globalCoordinates = false,
        string type = "方向余弦",
        bool useRayAiming = true,
        bool showRaySegments = false) : base(optic)
    {
        _fieldNumber = Math.Max(0, fieldNumber);
        _hx = Math.Clamp(hx, -1, 1);
        _hy = Math.Clamp(hy, -1, 1);
        _wavelengthNumber = Math.Max(1, wavelengthNumber);
        _px = Math.Clamp(px, -1, 1);
        _py = Math.Clamp(py, -1, 1);
        _globalCoordinates = globalCoordinates;
        _type = type;
        _useRayAiming = useRayAiming;
        _showRaySegments = showRaySegments;
    }

    public override string Name => "Single Ray Trace";

    public override AnalysisData GenerateData()
    {
        if (Optic.Wavelengths.Count == 0 || Optic.SurfaceGroup.Items.Count == 0)
        {
            return new AnalysisData(Name, new Dictionary<string, object>
            {
                ["Status"] = "No optical data"
            });
        }

        var definedFields = SpotAnalysisEngine.DefinedFields(Optic);
        var field = _fieldNumber > 0 && definedFields.Count > 0
            ? definedFields[Math.Clamp(_fieldNumber - 1, 0, definedFields.Count - 1)]
            : (Hx: _hx, Hy: _hy);
        var wavelengthIndex = Math.Clamp(_wavelengthNumber - 1, 0, Optic.Wavelengths.Count - 1);
        var wavelength = Optic.Wavelengths[wavelengthIndex];

        if (IsParaxialMarginalChiefType())
        {
            return GenerateMarginalChief(field, wavelength, wavelengthIndex);
        }

        var bundle = Optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            field.Hx,
            field.Hy,
            _px,
            _py,
            wavelength.Micrometers,
            aimAtStop: _useRayAiming,
            allowOutsideUnitPupil: true);
        var sourceRay = bundle.Rays.Single();
        var history = Optic.SequentialRayTracer.Trace(bundle).RayHistories.Single();
        var realRows = BuildRealRows(sourceRay, history);
        var paraxialRows = BuildParaxialRows(field, wavelength);
        var rows = realRows.Concat(paraxialRows).ToArray();
        var table = IsTangentAngleType()
            ? TangentAngleTable(rows)
            : DirectionCosineTable(rows);
        var pane = BuildLayoutPane(realRows, paraxialRows);
        var vignettedSurface = history.FirstOrDefault(sample => sample.Vignetted)?.SurfaceNumber ?? 0;
        var lastSurface = history.LastOrDefault()?.SurfaceNumber ?? -1;

        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FieldSelection"] = _fieldNumber <= 0 ? "Arbitrary" : _fieldNumber,
                ["FieldHx"] = field.Hx,
                ["FieldHy"] = field.Hy,
                ["PupilPx"] = _px,
                ["PupilPy"] = _py,
                ["WavelengthNumber"] = wavelengthIndex + 1,
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["CoordinateSystem"] = _globalCoordinates ? "Global" : "Local",
                ["TraceType"] = IsTangentAngleType() ? "Tangent Angles" : "Direction Cosines",
                ["RayAiming"] = _useRayAiming ? "Paraxial" : "Off",
                ["LastSurface"] = lastSurface,
                ["VignettedSurface"] = vignettedSurface,
                ["ShowRaySegments"] = _showRaySegments
            },
            pane.Series[0],
            pane.Series,
            pane.PlotOptions,
            new[] { pane },
            PlotPaneColumns: 1,
            Table: table,
            ReportText: BuildZemaxDirectionCosineReport(field, realRows, paraxialRows));
    }

    private AnalysisData GenerateMarginalChief(
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int wavelengthIndex)
    {
        var marginal = Optic.Paraxial.TraceNormalizedPupil(
            0,
            new[] { 1.0 },
            wavelength.Micrometers);
        var chief = Optic.Paraxial.TraceNormalizedPupil(
            1,
            new[] { 0.0 },
            wavelength.Micrometers);
        var count = Math.Min(
            Optic.SurfaceGroup.Items.Count,
            new[]
            {
                marginal.Heights.Count,
                marginal.Slopes.Count,
                chief.Heights.Count,
                chief.Slopes.Count
            }.Min());
        var rows = Enumerable.Range(0, count)
            .Select(index => (IReadOnlyList<string>)new[]
            {
                index.ToString(CultureInfo.InvariantCulture),
                Optic.SurfaceGroup.Items[index].Label,
                Format(marginal.Heights[index][0]),
                Format(marginal.Slopes[index][0]),
                Format(chief.Heights[index][0]),
                Format(chief.Slopes[index][0])
            })
            .ToArray();
        var marginalPoints = Enumerable.Range(0, count)
            .Select(index => new AnalysisPoint(
                Optic.SurfaceGroup.Items[index].CoordinateSystem.Origin.Z,
                marginal.Heights[index][0],
                index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        var chiefPoints = Enumerable.Range(0, count)
            .Select(index => new AnalysisPoint(
                Optic.SurfaceGroup.Items[index].CoordinateSystem.Origin.Z,
                chief.Heights[index][0],
                index.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        var series = new[]
        {
            new AnalysisSeries(
                "Z (mm)",
                "Y (mm)",
                marginalPoints,
                Name: "近轴边缘光线",
                ColorIndex: 0,
                ShowMarkers: true,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.RayHeight,
                YUnit: AnalysisAxisUnit.Millimeter),
            new AnalysisSeries(
                "Z (mm)",
                "Y (mm)",
                chiefPoints,
                Name: "近轴主光线",
                ColorIndex: 1,
                ShowMarkers: true,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.RayHeight,
                YUnit: AnalysisAxisUnit.Millimeter)
        };
        return new AnalysisData(
            Name,
            new Dictionary<string, object>
            {
                ["FieldSelection"] = "Ignored by Ym, Um, Yc, Uc",
                ["FieldHx"] = field.Hx,
                ["FieldHy"] = field.Hy,
                ["WavelengthNumber"] = wavelengthIndex + 1,
                ["WavelengthMicrometers"] = wavelength.Micrometers,
                ["TraceType"] = "Ym, Um, Yc, Uc"
            },
            series[0],
            series,
            new AnalysisPlotOptions(
                Title: "近轴边缘光线与主光线",
                ShowHorizontalZeroLine: true,
                ShowLegend: true,
                DottedGrid: true),
            Table: new AnalysisTable(
                new[] { "面", "名称", "Ym", "Um", "Yc", "Uc" },
                rows));
    }

    private IReadOnlyList<TraceDisplayRow> BuildRealRows(
        RealRay sourceRay,
        IReadOnlyList<RayTraceSample> history)
    {
        var rows = new List<TraceDisplayRow>(history.Count);
        var incidentDirection = sourceRay.Direction;
        foreach (var sample in history)
        {
            var surface = Optic.SurfaceGroup.Items.FirstOrDefault(item => item.Number == sample.SurfaceNumber);
            if (surface is null)
            {
                continue;
            }

            var localPosition = surface.CoordinateSystem.ToLocalPoint(sample.Position);
            var localDirection = surface.CoordinateSystem.ToLocalDirection(sample.Direction);
            var localIncident = surface.CoordinateSystem.ToLocalDirection(incidentDirection);
            var localNormal = surface.Geometry.SurfaceNormal(localPosition);
            var position = _globalCoordinates ? sample.Position : localPosition;
            var direction = _globalCoordinates ? sample.Direction : localDirection;
            var normal = _globalCoordinates
                ? surface.CoordinateSystem.ToGlobalDirection(localNormal)
                : localNormal;
            rows.Add(new TraceDisplayRow(
                "实光线",
                sample.SurfaceNumber,
                sample.SurfaceLabel,
                ReferenceEquals(surface, Optic.SurfaceGroup.Items[0]),
                position,
                direction,
                normal,
                sample.SegmentLength,
                IncidenceAngleDegrees(localIncident, localNormal),
                sample.Vignetted,
                sample.Intensity,
                sample.Position,
                false));
            incidentDirection = sample.Direction;
        }

        return rows;
    }

    private IReadOnlyList<TraceDisplayRow> BuildParaxialRows(
        (double Hx, double Hy) field,
        Wavelength wavelength)
    {
        var xTrace = Optic.Paraxial.TraceNormalizedPupil(
            field.Hx,
            new[] { _px },
            wavelength.Micrometers);
        var yTrace = Optic.Paraxial.TraceNormalizedPupil(
            field.Hy,
            new[] { _py },
            wavelength.Micrometers);
        var count = Math.Min(
            Optic.SurfaceGroup.Items.Count,
            new[]
            {
                xTrace.Heights.Count,
                xTrace.Slopes.Count,
                yTrace.Heights.Count,
                yTrace.Slopes.Count
            }.Min());
        var rows = new List<TraceDisplayRow>(count);
        Vector3D? previousGlobalPosition = null;
        var previousLocalDirection = Normalize(new Vector3D(
            xTrace.Slopes[0][0],
            yTrace.Slopes[0][0],
            1));
        for (var index = 0; index < count; index++)
        {
            var surface = Optic.SurfaceGroup.Items[index];
            var localPosition = new Vector3D(
                xTrace.Heights[index][0],
                yTrace.Heights[index][0],
                0);
            var localDirection = Normalize(new Vector3D(
                xTrace.Slopes[index][0],
                yTrace.Slopes[index][0],
                1));
            var localNormal = surface.Geometry.SurfaceNormal(localPosition);
            var globalPosition = surface.CoordinateSystem.ToGlobalPoint(localPosition);
            var position = _globalCoordinates ? globalPosition : localPosition;
            var direction = _globalCoordinates
                ? surface.CoordinateSystem.ToGlobalDirection(localDirection)
                : localDirection;
            var normal = _globalCoordinates
                ? surface.CoordinateSystem.ToGlobalDirection(localNormal)
                : localNormal;
            var segmentLength = previousGlobalPosition.HasValue
                ? (globalPosition - previousGlobalPosition.Value).Length
                : 0;
            rows.Add(new TraceDisplayRow(
                "近轴光线",
                surface.Number,
                surface.Label,
                index == 0,
                position,
                direction,
                normal,
                segmentLength,
                IncidenceAngleDegrees(previousLocalDirection, localNormal),
                false,
                1,
                globalPosition,
                true));
            previousGlobalPosition = globalPosition;
            previousLocalDirection = localDirection;
        }

        return rows;
    }

    private AnalysisPlotPane BuildLayoutPane(
        IReadOnlyList<TraceDisplayRow> realRows,
        IReadOnlyList<TraceDisplayRow> paraxialRows)
    {
        var firstSurface = Optic.SurfaceGroup.Items
            .Skip(1)
            .FirstOrDefault()?.Number ?? 0;
        var lastSurface = Optic.SurfaceGroup.Items.LastOrDefault()?.Number ?? firstSurface;
        var layout = new Layout2DBuilder(Optic).Build(
            options: new LayoutBuildOptions(
                FirstSurface: firstSurface,
                LastSurface: lastSurface,
                RayCount: 0));
        var displayedRealRows = realRows
            .Where(row => row.SurfaceNumber >= firstSurface && row.SurfaceNumber <= lastSurface)
            .ToArray();
        var displayedParaxialRows = paraxialRows
            .Where(row => row.SurfaceNumber >= firstSurface && row.SurfaceNumber <= lastSurface)
            .ToArray();
        var series = new List<AnalysisSeries>();

        foreach (var element in layout.LensElements)
        {
            series.Add(new AnalysisSeries(
                "Z (mm)",
                "Y (mm)",
                CloseBoundary(element.Boundary)
                    .Select(point => new AnalysisPoint(point.Z, point.Y))
                    .ToArray(),
                Name: "",
                ColorIndex: 7,
                LineWidth: 1.2,
                Opacity: 0.65,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.RayHeight,
                YUnit: AnalysisAxisUnit.Millimeter));
        }

        foreach (var surface in layout.Surfaces)
        {
            series.Add(new AnalysisSeries(
                "Z (mm)",
                "Y (mm)",
                surface.Points.Select(point => new AnalysisPoint(point.Z, point.Y)).ToArray(),
                Name: "",
                ColorIndex: surface.IsStop ? 3 : surface.IsReferencePlane ? 10 : 7,
                LineWidth: surface.IsStop ? 1.8 : 0.8,
                Opacity: surface.IsStop ? 0.9 : 0.5,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Millimeter,
                YQuantity: AnalysisAxisQuantity.RayHeight,
                YUnit: AnalysisAxisUnit.Millimeter));
        }

        var realPoints = displayedRealRows
            .Select(row => new AnalysisPoint(
                row.GlobalPosition.Z,
                row.GlobalPosition.Y,
                $"面 {row.SurfaceNumber}"))
            .ToArray();
        var paraxialPoints = displayedParaxialRows
            .Select(row => new AnalysisPoint(
                row.GlobalPosition.Z,
                row.GlobalPosition.Y,
                $"面 {row.SurfaceNumber}"))
            .ToArray();
        series.Add(new AnalysisSeries(
            "Z (mm)",
            "Y (mm)",
            realPoints,
            Name: "实光线",
            ColorIndex: 0,
            ShowMarkers: true,
            LineWidth: 2.4,
            MarkerSize: 3.8,
            XQuantity: AnalysisAxisQuantity.Coordinate,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.RayHeight,
            YUnit: AnalysisAxisUnit.Millimeter));
        series.Add(new AnalysisSeries(
            "Z (mm)",
            "Y (mm)",
            paraxialPoints,
            Name: "近轴光线",
            LineStyle: AnalysisLineStyle.Dashed,
            ColorIndex: 1,
            ShowMarkers: false,
            LineWidth: 1.6,
            XQuantity: AnalysisAxisQuantity.Coordinate,
            XUnit: AnalysisAxisUnit.Millimeter,
            YQuantity: AnalysisAxisQuantity.RayHeight,
            YUnit: AnalysisAxisUnit.Millimeter));

        var allGeometryPoints = layout.Surfaces.SelectMany(surface => surface.Points)
            .Concat(layout.LensElements.SelectMany(element => element.Boundary))
            .ToArray();
        var allZ = allGeometryPoints.Select(point => point.Z)
            .Concat(realPoints.Select(point => point.X))
            .Concat(paraxialPoints.Select(point => point.X))
            .Where(double.IsFinite)
            .ToArray();
        var allY = allGeometryPoints.Select(point => point.Y)
            .Concat(realPoints.Select(point => point.Y))
            .Concat(paraxialPoints.Select(point => point.Y))
            .Where(double.IsFinite)
            .ToArray();
        var xMinimum = allZ.DefaultIfEmpty(0).Min();
        var xMaximum = allZ.DefaultIfEmpty(1).Max();
        var xPadding = Math.Max(0.5, (xMaximum - xMinimum) * 0.03);
        var yExtent = allY.Select(Math.Abs).DefaultIfEmpty(1).Max();
        yExtent = Math.Max(0.1, yExtent * 1.08);

        return new AnalysisPlotPane(
            "单光线镜头剖面",
            series,
            new AnalysisPlotOptions(
                Title: "单光线镜头剖面（物面至第一面的长距离已省略）",
                EqualAspect: true,
                ShowHorizontalZeroLine: true,
                XMinimum: xMinimum - xPadding,
                XMaximum: xMaximum + xPadding,
                YMinimum: -yExtent,
                YMaximum: yExtent,
                ShowLegend: true,
                DottedGrid: false,
                GridOpacity: 0.2));
    }

    private static IReadOnlyList<Layout2DPoint> CloseBoundary(
        IReadOnlyList<Layout2DPoint> boundary)
    {
        if (boundary.Count == 0)
        {
            return boundary;
        }

        return boundary.Concat(new[] { boundary[0] }).ToArray();
    }

    private static AnalysisTable DirectionCosineTable(IReadOnlyList<TraceDisplayRow> rows)
    {
        return new AnalysisTable(
            new[]
            {
                "表面", "X-坐标", "Y-坐标", "Z-坐标",
                "X-余弦", "Y-余弦", "Z-余弦",
                "X-法线", "Y-法线", "Z-法线",
                "角", "路径长度", "注释"
            },
            rows.Select(row => (IReadOnlyList<string>)new[]
            {
                row.IsObjectSurface
                        ? "OBJ"
                        : row.SurfaceNumber.ToString(CultureInfo.InvariantCulture),
                Format(row.Position.X),
                Format(row.Position.Y),
                Format(row.Position.Z),
                Format(row.Direction.X),
                Format(row.Direction.Y),
                Format(row.Direction.Z),
                DirectionCosineDetail(row, row.Normal.X, Format),
                DirectionCosineDetail(row, row.Normal.Y, Format),
                DirectionCosineDetail(row, row.Normal.Z, Format),
                DirectionCosineDetail(row, row.IncidenceAngleDegrees, Format),
                DirectionCosineDetail(row, row.PathLength, Format),
                row.IsParaxial ? "" : row.Vignetted ? "渐晕" : ""
            }).ToArray(),
            rows.Select(row => row.Section).ToArray());
    }

    private static AnalysisTable TangentAngleTable(IReadOnlyList<TraceDisplayRow> rows)
    {
        return new AnalysisTable(
            new[] { "光线", "面", "名称", "X", "Y", "Z", "Tan X", "Tan Y", "渐晕" },
            rows.Select(row => (IReadOnlyList<string>)new[]
            {
                row.Section,
                row.SurfaceNumber.ToString(CultureInfo.InvariantCulture),
                row.SurfaceLabel,
                Format(row.Position.X),
                Format(row.Position.Y),
                Format(row.Position.Z),
                Format(row.Direction.X / Math.Max(1e-30, row.Direction.Z)),
                Format(row.Direction.Y / Math.Max(1e-30, row.Direction.Z)),
                row.Vignetted ? "是" : "否"
            }).ToArray());
    }

    private string BuildZemaxDirectionCosineReport(
        (double Hx, double Hy) field,
        IReadOnlyList<TraceDisplayRow> realRows,
        IReadOnlyList<TraceDisplayRow> paraxialRows)
    {
        var lines = new List<string>
        {
            $"归一化X视场坐标(Hx) ：    {field.Hx,14:0.0000000000}",
            $"归一化Y视场坐标(Hy) ：    {field.Hy,14:0.0000000000}",
            $"归一化X光瞳坐标(Px) ：    {_px,14:0.0000000000}",
            $"归一化Y光瞳坐标(Py) ：    {_py,14:0.0000000000}",
            string.Empty,
            "实际光线追迹数据：",
            string.Empty,
            ZemaxReportHeader()
        };
        lines.AddRange(realRows.Select(ZemaxReportRow));
        lines.Add(string.Empty);
        lines.Add("近轴光线追迹数据：");
        lines.Add(string.Empty);
        lines.Add(ZemaxReportHeader());
        lines.AddRange(paraxialRows.Select(ZemaxReportRow));
        return string.Join(Environment.NewLine, lines);
    }

    private static string ZemaxReportHeader()
    {
        return string.Join('\t', new[]
        {
            "表面", "X-坐标", "Y-坐标", "Z-坐标",
            "X-余弦", "Y-余弦", "Z-余弦",
            "X-法线", "Y-法线", "Z-法线",
            "角", "路径长度", "注释"
        });
    }

    private static string ZemaxReportRow(TraceDisplayRow row)
    {
        return string.Join('\t', new[]
        {
            row.IsObjectSurface ? "OBJ" : row.SurfaceNumber.ToString(CultureInfo.InvariantCulture),
            ZemaxCoordinate(row.Position.X),
            ZemaxCoordinate(row.Position.Y),
            ZemaxCoordinate(row.Position.Z),
            ZemaxDirection(row.Direction.X),
            ZemaxDirection(row.Direction.Y),
            ZemaxDirection(row.Direction.Z),
            DirectionCosineDetail(row, row.Normal.X, ZemaxDirection),
            DirectionCosineDetail(row, row.Normal.Y, ZemaxDirection),
            DirectionCosineDetail(row, row.Normal.Z, ZemaxDirection),
            DirectionCosineDetail(row, row.IncidenceAngleDegrees, ZemaxDirection),
            DirectionCosineDetail(row, row.PathLength, ZemaxDirection),
            row.IsParaxial ? "" : row.Vignetted ? "渐晕" : ""
        });
    }

    private static string DirectionCosineDetail(
        TraceDisplayRow row,
        double value,
        Func<double, string> formatter)
    {
        if (row.IsParaxial)
        {
            return string.Empty;
        }

        return row.SurfaceNumber == 0 ? "-" : formatter(value);
    }

    private static string ZemaxCoordinate(double value)
    {
        return Math.Abs(value) < 5e-15
            ? "0.0000000000E+00"
            : value.ToString("0.0000000000E+00", CultureInfo.InvariantCulture);
    }

    private static string ZemaxDirection(double value)
    {
        if (Math.Abs(value) < 5e-15)
        {
            value = 0;
        }

        return value.ToString("0.0000000000", CultureInfo.InvariantCulture);
    }

    private bool IsTangentAngleType()
    {
        return _type.Contains("正切", StringComparison.OrdinalIgnoreCase)
            || _type.Contains("Tangent", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsParaxialMarginalChiefType()
    {
        return _type.Contains("Ym", StringComparison.OrdinalIgnoreCase)
            || _type.Contains("Yc", StringComparison.OrdinalIgnoreCase);
    }

    private static double IncidenceAngleDegrees(Vector3D direction, Vector3D normal)
    {
        var unitDirection = Normalize(direction);
        var unitNormal = Normalize(normal);
        var cosine = Math.Clamp(Math.Abs(Dot(unitDirection, unitNormal)), 0, 1);
        return Math.Acos(cosine) * 180 / Math.PI;
    }

    private static Vector3D Normalize(Vector3D value)
    {
        return value.Length <= 1e-30 ? new Vector3D(0, 0, 1) : value / value.Length;
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        var magnitude = Math.Abs(value);
        if (magnitude < 5e-10)
        {
            return "0";
        }

        return magnitude < 1e-5 || magnitude >= 1e6
            ? value.ToString("0.#####E+0", CultureInfo.InvariantCulture)
            : value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private sealed record TraceDisplayRow(
        string Section,
        int SurfaceNumber,
        string SurfaceLabel,
        bool IsObjectSurface,
        Vector3D Position,
        Vector3D Direction,
        Vector3D Normal,
        double PathLength,
        double IncidenceAngleDegrees,
        bool Vignetted,
        double Intensity,
        Vector3D GlobalPosition,
        bool IsParaxial);
}
