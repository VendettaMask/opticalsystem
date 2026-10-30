using System.Globalization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Services;

/// <summary>Paper-specific presentation of the same frozen calculation results.</summary>
internal sealed class DesignReportPdfModel(string name, string fileName, DateTimeOffset capturedAt)
{
    internal string Name { get; } = name;
    internal string FileName { get; } = fileName;
    internal DateTimeOffset CapturedAt { get; } = capturedAt;
    internal List<(int Number, OpticalDocumentSnapshot Document)> Configurations { get; } = [];
    internal List<DesignReportPdfSection> Sections { get; } = [];
    internal string Fields { get; set; } = "";
    internal string FieldLabel { get; set; } = "视场坐标";
    internal string Wavelengths { get; set; } = "";

    internal static string Number(double value) => double.IsFinite(value)
        ? value.ToString("0.####", CultureInfo.InvariantCulture) : "不可用";

    internal static string ReadableValue(string value) => Regex.Replace(value,
        @"(?<![\p{L}\d])[-+]?\d+\.\d{5,}(?:[eE][-+]?\d+)?", match =>
            double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number.ToString("G6", CultureInfo.InvariantCulture) : match.Value);

    internal static ReportTable? NumericalTable(AnalysisViewDto view)
    {
        if (view.Table is { } table)
            return new(table.Columns.ToArray(), table.Rows.Select(row => row.Select(ReadableValue).ToArray()).ToArray());
        if (view.PresentationKind is not (AnalysisPresentationKind.ZernikeFringe
            or AnalysisPresentationKind.ZernikeStandard or AnalysisPresentationKind.ZernikeAnnular)) return null;
        // These reports publish each fitted term as a named result row, alongside
        // auxiliary fitted-wavefront series. Keep every actual coefficient.
        var coefficients = view.Rows.Where(row => Regex.IsMatch(row.Metric, @"^Z\d+\s*\(n="))
            .Select(row => new[] { row.Metric, ReadableValue(row.Value) }).ToArray();
        if (coefficients.Length == 0) throw new InvalidDataException("系数分析没有提供完整数值表。");
        return new(["系数 (n, m)", "值（波）"], coefficients);
    }

    internal static IEnumerable<string[]> KeyMetrics(AnalysisViewDto view)
    {
        // Choose readable result summaries, never raw object/type names or debug keys.
        // This affects presentation only; full source rows remain in the optional appendix.
        var priorities = new[] { "RMS", "PV", "波峰到波谷", "最大几何点半径", "最大光线像差",
            "最大绝对畸变", "最大子午场曲", "最大弧矢场曲", "截止频率", "分析波长" };
        return view.Rows.Where(row => row.Value.Length <= 120 && row.Metric.Length <= 80
                && Regex.IsMatch(row.Metric, @"[\u4e00-\u9fff]")
                && !row.Value.Contains("System.", StringComparison.Ordinal)
                && !Regex.IsMatch(Regex.Replace(row.Metric, @"\([^)]*\)|（[^）]*）", ""), @"[A-Za-z]{4,}")
                && !row.Metric.Contains("序号", StringComparison.Ordinal))
            .OrderBy(row => priorities.Any(key => row.Metric.Contains(key, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .Take(5).Select(row => new[] { row.Metric, ReadableValue(row.Value) });
    }

    internal static string Condition(AnalysisParameterDescriptor parameter, IReadOnlyDictionary<string, string> settings)
    {
        var value = settings.GetValueOrDefault(parameter.Key, parameter.DefaultValue);
        var label = Regex.Replace(parameter.DisplayName, @"（0[^）]*）|\(0[^)]*\)", "");
        var readable = parameter.ChoiceLabels?.GetValueOrDefault(value) ?? value;
        var all = value.Equals("all", StringComparison.OrdinalIgnoreCase) || readable is "所有" or "全部";
        if (all)
        {
            readable = "全部";
            if (parameter.Key == "WavelengthNumber") label = "波长";
            if (parameter.Key == "FieldNumber") label = "视场";
        }
        if (value == "0") readable = parameter.Key switch
        {
            "FieldNumber" or "WavelengthNumber" => "全部",
            "SurfaceNumber" => "像面",
            _ => readable
        };
        else if (!all && readable == value) label = parameter.Key switch
        {
            "WavelengthNumber" => "波长序号",
            "FieldNumber" => "视场序号",
            _ => label
        };
        if (readable.Equals("true", StringComparison.OrdinalIgnoreCase)) readable = "是";
        if (readable.Equals("false", StringComparison.OrdinalIgnoreCase)) readable = "否";
        return $"{label}：{readable}";
    }
}

internal sealed class DesignReportPdfSection(int configuration, string title)
{
    internal int Configuration { get; } = configuration;
    internal string Title { get; } = title;
    internal bool Landscape { get; set; }
    internal List<string> Images { get; } = [];
    internal List<string[]> Metrics { get; } = [];
    internal string Conditions { get; set; } = "";
    internal ReportTable? Table { get; set; }
    internal string? Failure { get; set; }
}
