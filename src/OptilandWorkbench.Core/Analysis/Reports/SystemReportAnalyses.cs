using System.Globalization;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class PrescriptionReportAnalysis : BaseAnalysis
{
    public PrescriptionReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Prescription Report";

    public override AnalysisData GenerateData()
    {
        var surfaces = Optic.SurfaceGroup.Items;
        var rows = surfaces.Select(surface => (IReadOnlyList<string>)new[]
        {
            surface.Number.ToString(CultureInfo.InvariantCulture),
            surface.Label,
            SurfaceType(surface),
            FormatRadius(surface.Radius),
            Format(surface.Thickness),
            surface.Material,
            Format(surface.SemiDiameter),
            Format(surface.Conic),
            surface.IsStop ? "是" : string.Empty,
            surface.Coating
        }).ToArray();
        var table = new AnalysisTable(
            new[] { "面", "标签", "类型", "曲率半径", "厚度", "材料", "半口径", "圆锥系数", "光阑", "镀膜" },
            rows);
        var values = SystemValues(Optic).ToDictionary(p => p.Key, p => p.Value);
        values["SurfacePrescription"] = surfaces.Select(surface => new[]
        {
            (double)surface.Number, double.IsInfinity(surface.Radius) || Math.Abs(surface.Radius) < 1e-30 ? 0 : 1 / surface.Radius,
            surface.Thickness, surface.SemiDiameter, surface.Conic
        }).ToArray();
        return new AnalysisData(
            Name,
            values,
            Table: table,
            ReportText: BuildReport("表面数据报告", values, table));
    }

    internal static IReadOnlyDictionary<string, object> SystemValues(Optic optic)
    {
        var stop = optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.IsStop);
        var primary = optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? optic.Wavelengths.FirstOrDefault();
        return new Dictionary<string, object>
        {
            ["Name"] = optic.Name,
            ["SurfaceCount"] = optic.SurfaceGroup.Items.Count,
            ["StopSurface"] = stop?.Number ?? -1,
            ["PrimaryWavelengthMicrometers"] = primary?.Micrometers ?? 0,
            ["TotalTrack"] = optic.SurfaceGroup.TotalTrack
        };
    }

    internal static string SurfaceType(OpticalSurface surface)
    {
        var type = surface.Geometry.GetType().Name;
        return type.EndsWith("Geometry", StringComparison.Ordinal)
            ? type[..^"Geometry".Length]
            : type;
    }

    internal static string BuildReport(
        string title,
        IReadOnlyDictionary<string, object> values,
        AnalysisTable table)
    {
        var lines = new List<string> { title, string.Empty };
        lines.AddRange(values.Select(item => $"{item.Key}: {item.Value}"));
        lines.Add(string.Empty);
        lines.Add(string.Join('\t', table.Columns));
        lines.AddRange(table.Rows.Select(row => string.Join('\t', row)));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatRadius(double radius) =>
        Math.Abs(radius) <= 1e-12 ? "无限" : Format(radius);

    private static string Format(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);
}

public sealed class SystemDataReportAnalysis : BaseAnalysis
{
    public SystemDataReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "System Data Report";

    public override AnalysisData GenerateData()
    {
        var cardinal = Optic.Paraxial.EstimateCardinalPoints();
        var stop = Optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.IsStop);
        var primary = Optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
            ?? Optic.Wavelengths.FirstOrDefault();
        var rows = new List<IReadOnlyList<string>>
        {
            Row("系统", "名称", Optic.Name),
            Row("系统", "表面数", Optic.SurfaceGroup.Items.Count),
            Row("系统", "视场数", Optic.Fields.Count),
            Row("系统", "波长数", Optic.Wavelengths.Count),
            Row("系统", "总长 (mm)", Optic.SurfaceGroup.TotalTrack),
            Row("孔径", "孔径类型", Optic.Aperture.Kind),
            Row("孔径", "孔径值", Optic.Aperture.Value),
            Row("孔径", "光阑面", stop?.Number ?? -1),
            Row("孔径", "物方远心", Optic.ObjectSpaceTelecentric ? "是" : "否"),
            Row("视场", "视场定义", Optic.FieldDefinition),
            Row("视场", "像方无焦", Optic.ImageSpaceAfocal ? "是" : "否"),
            Row("视场", "光线瞄准", Optic.RayAimingEnabled ? "启用" : "关闭"),
            Row("波长", "主波长 (μm)", primary?.Micrometers ?? 0),
            Row("近轴", "有效焦距 (mm)", cardinal.EffectiveFocalLength),
            Row("近轴", "F 数", Optic.Paraxial.EstimateFNumber()),
            Row("近轴", "入瞳直径 (mm)", Optic.Paraxial.EstimateEntrancePupilDiameter()),
            Row("近轴", "出瞳直径 (mm)", Optic.Paraxial.EstimateExitPupilDiameter())
        };
        var table = new AnalysisTable(
            new[] { "分类", "项目", "值" },
            rows,
            rows.Select(row => row[0]).ToArray());
        var values = PrescriptionReportAnalysis.SystemValues(Optic).ToDictionary(p => p.Key, p => p.Value);
        values["EffectiveFocalLength"] = cardinal.EffectiveFocalLength;
        values["FNumber"] = Optic.Paraxial.EstimateFNumber();
        values["EntrancePupilDiameter"] = Optic.Paraxial.EstimateEntrancePupilDiameter();
        values["ExitPupilDiameter"] = Optic.Paraxial.EstimateExitPupilDiameter();
        return new AnalysisData(
            Name,
            values,
            Table: table,
            ReportText: PrescriptionReportAnalysis.BuildReport("系统数据报告", values, table));
    }

    private static IReadOnlyList<string> Row(string category, string label, object value) =>
        new[] { category, label, FormatValue(value) };

    private static string FormatValue(object value) => value switch
    {
        double number => number.ToString("0.######", CultureInfo.InvariantCulture),
        float number => number.ToString("0.######", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}

public sealed class ClassifiedDataReportAnalysis : BaseAnalysis
{
    public ClassifiedDataReportAnalysis(Optic optic) : base(optic)
    {
    }

    public override string Name => "Classified Data Report";

    public override AnalysisData GenerateData()
    {
        var surfaces = Optic.SurfaceGroup.Items;
        var roleRows = surfaces
            .GroupBy(surface => ClassifySurface(surface, surfaces.Count))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<string>)new[]
            {
                "表面角色",
                group.Key,
                group.Count().ToString(CultureInfo.InvariantCulture),
                string.Join(", ", group.Select(surface => surface.Number))
            });
        var materialRows = surfaces
            .GroupBy(surface => surface.Material, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (IReadOnlyList<string>)new[]
            {
                "材料",
                group.Key,
                group.Count().ToString(CultureInfo.InvariantCulture),
                string.Join(", ", group.Select(surface => surface.Number))
            });
        var typeRows = surfaces
            .GroupBy(PrescriptionReportAnalysis.SurfaceType, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<string>)new[]
            {
                "表面类型",
                group.Key,
                group.Count().ToString(CultureInfo.InvariantCulture),
                string.Join(", ", group.Select(surface => surface.Number))
            });
        var rows = roleRows.Concat(materialRows).Concat(typeRows).ToArray();
        var table = new AnalysisTable(
            new[] { "分类", "项目", "数量", "表面序号" },
            rows,
            rows.Select(row => row[0]).ToArray());
        var values = PrescriptionReportAnalysis.SystemValues(Optic);
        return new AnalysisData(
            Name,
            values,
            Table: table,
            ReportText: PrescriptionReportAnalysis.BuildReport("分类数据报告", values, table));
    }

    private static string ClassifySurface(OpticalSurface surface, int surfaceCount)
    {
        if (surface.Number == 0)
        {
            return "物面";
        }

        if (surface.Number == surfaceCount - 1)
        {
            return "像面";
        }

        if (surface.IsStop)
        {
            return "光阑面";
        }

        if (surface.IsReflective)
        {
            return "反射面";
        }

        return surface.IsPlane ? "平面折射面" : "曲面折射面";
    }
}
