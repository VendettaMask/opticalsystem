using System.Globalization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Services;

/// <summary>Shared report metadata, original images and native results for all four writers.</summary>
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
            return new(table.Columns.ToArray(), table.Rows.Select(row => row.ToArray()).ToArray(),
                view.PresentationKind is AnalysisPresentationKind.ZernikeFringe or AnalysisPresentationKind.ZernikeStandard or AnalysisPresentationKind.ZernikeAnnular ? 3 : -1);
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
    internal string FormulaNote { get; set; } = "";
    internal bool CoverImage { get; set; }

    internal void ApplyAnalysis(AnalysisViewDto view, IReadOnlyList<AnalysisParameterDescriptor> parameters, IReadOnlyDictionary<string, string> settings, bool hasPlot)
    {
        Landscape = !hasPlot && view.Table?.Columns.Count > 5;
        Conditions = string.Join(" · ", parameters.Where(parameter =>
            new[] { "Field", "Wave", "Sample", "Sampling", "Surface", "Reference" }.Any(part => parameter.Key.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Take(5).Select(parameter => DesignReportPdfModel.Condition(parameter, settings)));
        Metrics.AddRange(DesignReportPdfModel.KeyMetrics(view));
        var detectorWindow = view.Rows.Where(row => row.Metric is "探测器窗口来源" or "探测器范围").ToArray();
        if (detectorWindow.Length > 0)
            Conditions += " · " + string.Join(" · ", detectorWindow.Select(row => row.Metric + "：" + DesignReportPdfModel.ReadableValue(row.Value)));
        Table = DesignReportPdfModel.NumericalTable(view);
        if (!hasPlot && Table is null)
            Table = new(["项目", "数值"], view.Rows.Select(row => new[] { row.Metric, row.Value }).ToArray());
        if (view.PresentationKind is AnalysisPresentationKind.ZernikeFringe or AnalysisPresentationKind.ZernikeStandard or AnalysisPresentationKind.ZernikeAnnular)
            FormulaNote = "W(ρ, θ) = Σ c_j Z_j(ρ, θ)。ρ 为归一化瞳孔半径，θ 为方位角，系数单位为波。"
                + (view.PresentationKind == AnalysisPresentationKind.ZernikeAnnular
                    ? $"ε = {settings.GetValueOrDefault("ObscurationRatio", "0.5")}；u = (2ρ² − 1 − ε²) / (1 − ε²)，ε ≤ ρ ≤ 1。公式采用本次环形瞳孔的实际归一化。"
                    : view.PresentationKind == AnalysisPresentationKind.ZernikeStandard ? "0 ≤ ρ ≤ 1；Standard 基底采用单位 RMS 归一化。" : "0 ≤ ρ ≤ 1；Fringe 基底不加 Standard 归一化因子。");
    }
}
