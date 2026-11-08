using System.Globalization;
using System.Text;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public sealed class SeidelCoefficientsAnalysis : BaseAnalysis
{
    private static readonly string[] SeidelColumns =
        { "表面", "SPHA S1", "COMA S2", "ASTI S3", "FCUR S4", "DIST S5", "CLA (CL)", "CTR (CT)" };
    private static readonly string[] WaveColumns =
        { "表面", "W040", "W131", "W222", "W220P", "W311", "W020", "W111" };
    private static readonly string[] TransverseColumns =
        { "表面", "TSPH", "TSCO", "TTCO", "TAST", "TPFC", "TSFC", "TTFC", "TDIS", "TAXC", "TLAC" };
    private static readonly string[] LongitudinalColumns =
        { "表面", "LSPH", "LSCO", "LTCO", "LAST", "LPFC", "LSFC", "LTFC", "LDIS", "LAXC", "LLAC" };

    private readonly int _wavelengthNumber;

    public SeidelCoefficientsAnalysis(Optic optic, int wavelengthNumber = 0) : base(optic)
    {
        _wavelengthNumber = wavelengthNumber;
    }

    public override string Name => "Seidel Coefficients";

    public override AnalysisData GenerateData()
    {
        var wavelengths = Optic.Wavelengths.ToArray();
        if (wavelengths.Length == 0)
        {
            return new AnalysisData(
                Name,
                new Dictionary<string, object> { ["Status"] = "No wavelengths" },
                ReportText: "未定义波长。", Outcome: AnalysisOutcome.Unavailable, OutcomeReason: "No wavelengths");
        }

        if (_wavelengthNumber < 0 || _wavelengthNumber > wavelengths.Length)
            return AnalysisData.Unavailable(Name, "波长编号不在当前系统范围内。");
        var wavelength = SelectWavelength(wavelengths);
        var wavelengthMicrometers = wavelength.Micrometers;
        SeidelData computed;
        try
        {
            computed = SeidelMetrics.Calculate(Optic, wavelengthMicrometers);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return AnalysisData.Unavailable(Name, error.Message);
        }
        var surfaces = Optic.SurfaceGroup.Items.ToArray();
        var labels = computed.Surfaces.Select((_, index) =>
            SurfaceLabel(surfaces[index + 1], index + 2 == surfaces.Length)).Append("累计").ToArray();
        var total = computed.Total.ToArray();
        var coefficients = computed.Surfaces.Select(row => row.Coefficients.ToArray()).Append(total).ToArray();
        var imageIndex = computed.ImageRefractiveIndex;
        var petzvalRadius = computed.PetzvalCurvature == 0 ? double.PositiveInfinity : 1 / computed.PetzvalCurvature;
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelengthMicrometers,
            ["ChiefRaySlopeObjectSpace"] = computed.ChiefSlopeObject,
            ["ChiefRaySlopeImageSpace"] = computed.ChiefSlopeImage,
            ["MarginalRaySlopeObjectSpace"] = computed.MarginalSlopeObject,
            ["MarginalRaySlopeImageSpace"] = computed.MarginalSlopeImage,
            ["PetzvalRadius"] = petzvalRadius,
            ["OpticalInvariant"] = computed.OpticalInvariant,
            ["ImageSpaceRefractiveIndex"] = imageIndex,
            ["SurfaceCount"] = Math.Max(0, surfaces.Length - 1),
            ["SeidelCoefficientsMillimeters"] = coefficients
        };
        // Convert from the unrounded surface coefficients, using image-space n' and marginal u'.
        var marginalSlope = computed.MarginalSlopeImage;
        var canConvert = double.IsFinite(marginalSlope) && Math.Abs(marginalSlope) > 1e-15;
        var transverseFactor = canConvert ? -1 / (2 * imageIndex * marginalSlope) : double.NaN;
        var longitudinalFactor = canConvert ? 1 / (2 * imageIndex * marginalSlope * marginalSlope) : double.NaN;
        var wavelengthMillimeters = wavelengthMicrometers / 1000;
        var waves = coefficients.Select(row => WaveCoefficients(row, wavelengthMillimeters)).ToArray();
        var transverse = coefficients.Select(row => RayAberrationCoefficients(row, transverseFactor, -2 * transverseFactor)).ToArray();
        var longitudinal = coefficients.Select(row => RayAberrationCoefficients(row, longitudinalFactor, 2 * longitudinalFactor)).ToArray();
        values["WaveAberrationCoefficients"] = waves;
        values["TransverseAberrationCoefficientsMillimeters"] = transverse;
        values["LongitudinalAberrationCoefficientsMillimeters"] = longitudinal;

        var table = CoefficientTable(SeidelColumns, labels, coefficients);
        var waveTable = CoefficientTable(WaveColumns, labels, waves);
        AnalysisReportSection[] sections =
        [
            new("赛德尔像差系数", "赛德尔", "mm", table),
            new("赛德尔像差系数（波长）", "波长", "λ", waveTable),
            new("横向像差系数", "横向", "mm", CoefficientTable(TransverseColumns, labels, transverse)),
            new("轴向像差系数", "轴向", "mm", CoefficientTable(LongitudinalColumns, labels, longitudinal)),
            new("波前像差系数汇总（波长）", "波前汇总", "λ", new AnalysisTable(WaveColumns, [waveTable.Rows[^1]])),
            new("场曲波前系数汇总（波长）", "场曲汇总", "λ", CoefficientTable(["表面", "W220S", "W220M", "W220T"], ["累计"],
                [[(total[2] + total[3]) / (4 * wavelengthMillimeters),
                    (2 * total[2] + total[3]) / (4 * wavelengthMillimeters),
                    (3 * total[2] + total[3]) / (4 * wavelengthMillimeters)]]))
        ];
        return new AnalysisData(
            Name,
            values,
            Table: table,
            ReportText: BuildReport(values, sections, canConvert),
            ReportSections: sections);
    }

    private Wavelength SelectWavelength(IReadOnlyList<Wavelength> wavelengths)
    {
        if (_wavelengthNumber > 0 && _wavelengthNumber <= wavelengths.Count)
        {
            return wavelengths[_wavelengthNumber - 1];
        }

        return wavelengths.FirstOrDefault(item => item.IsPrimary) ?? wavelengths[0];
    }

    private static string SurfaceLabel(OpticalSurface surface, bool isLast)
    {
        if (isLast)
        {
            return "像面";
        }

        return surface.IsStop ? "光阑" : surface.Number.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildReport(
        IReadOnlyDictionary<string, object> values,
        IReadOnlyList<AnalysisReportSection> sections,
        bool canConvert)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"波长                    : {Number(values["WavelengthMicrometers"]),12:0.0000} µm");
        builder.AppendLine($"主光线斜率，物空间      : {Number(values["ChiefRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"主光线斜率，像空间      : {Number(values["ChiefRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，物空间    : {Number(values["MarginalRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，像空间    : {Number(values["MarginalRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"佩兹伐半径              : {FormatNumber(Number(values["PetzvalRadius"]), "0.0000"),12}");
        builder.AppendLine($"光学不变量              : {Number(values["OpticalInvariant"]),12:0.0000}");
        foreach (var section in sections) AppendTable(builder, section.Title + "：", section.Table);
        if (!canConvert)
        {
            builder.AppendLine();
            builder.AppendLine("注：像方边缘光线斜率为零或无效，横向/轴向换算未定义，以 — 表示。");
        }
        return builder.ToString().TrimEnd();
    }

    internal static double[] WaveCoefficients(double[] s, double wavelength) =>
        new SeidelCoefficients(s[0], s[1], s[2], s[3], s[4], s[5], s[6]).InWaves(wavelength);

    internal static double[] RayAberrationCoefficients(double[] s, double factor, double colorFactor) => new[]
    {
        s[0] * factor, s[1] * factor, 3 * s[1] * factor, 2 * s[2] * factor,
        s[3] * factor, (s[2] + s[3]) * factor, ((3 * s[2]) + s[3]) * factor, s[4] * factor,
        s[5] * colorFactor, s[6] * colorFactor
    };

    private static AnalysisTable CoefficientTable(
        IReadOnlyList<string> columns,
        IReadOnlyList<string> labels,
        IEnumerable<double[]> rows) => new(columns, labels.Zip(rows, (label, coefficients) =>
            (IReadOnlyList<string>)new[] { label }.Concat(coefficients
                .Select(value => double.IsFinite(value) ? FormatNumber(value, "0.000000") : "—")).ToArray()).ToArray());

    private static void AppendTable(StringBuilder builder, string title, AnalysisTable table)
    {
        builder.AppendLine();
        builder.AppendLine(title);
        builder.AppendLine();
        var rows = new[] { table.Columns }.Concat(table.Rows).ToArray();
        var widths = Enumerable.Range(0, table.Columns.Count)
            .Select(column => rows.Max(row => row[column].Length)).ToArray();
        foreach (var row in rows)
        {
            builder.AppendLine(row[0].PadRight(6 - row[0].Count(character => character >= '\u3000')) + "  " +
                string.Join("   ", row.Skip(1).Select((value, index) => value.PadLeft(widths[index + 1]))));
        }
    }

    private static double Number(object value)
    {
        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static string FormatNumber(double value, string format)
    {
        return double.IsPositiveInfinity(value)
            ? "∞"
            : double.IsNegativeInfinity(value)
                ? "-∞"
                : (value == 0 ? 0.0 : value).ToString(format, CultureInfo.InvariantCulture);
    }

}

public sealed class SeidelDiagramAnalysis : BaseAnalysis
{
    private static readonly string[] AberrationNames =
    {
        "球差", "彗差", "像散", "场曲", "畸变", "轴上色差", "垂轴色差"
    };

    private readonly int _wavelengthNumber;
    private readonly double _maximumAberration;
    private readonly double _gridInterval;

    public SeidelDiagramAnalysis(
        Optic optic,
        int wavelengthNumber = 0,
        double maximumAberration = 0.1,
        double gridInterval = 0.01) : base(optic)
    {
        _wavelengthNumber = wavelengthNumber;
        _maximumAberration = double.IsFinite(maximumAberration) && maximumAberration >= 0
            ? maximumAberration
            : 0.1;
        _gridInterval = gridInterval > 0 ? gridInterval : 0.01;
    }

    public override string Name => "Seidel Diagram";

    public override AnalysisData GenerateData()
    {
        var coefficients = new SeidelCoefficientsAnalysis(Optic, _wavelengthNumber).GenerateData();
        if (coefficients.Table is null)
        {
            return coefficients with { Name = Name };
        }

        var rows = coefficients.Table.Rows
            .Select((row, index) => (IReadOnlyList<string>)row
                .Select((value, column) =>
                    index == coefficients.Table.Rows.Count - 1 && column == 0 ? "总和" : value)
                .ToArray())
            .ToArray();
        var series = Enumerable.Range(0, AberrationNames.Length)
            .Select(coefficientIndex => new AnalysisSeries(
                "",
                "",
                rows.Select((row, surfaceIndex) => new AnalysisPoint(
                    surfaceIndex,
                    ((double[][])coefficients.Values["SeidelCoefficientsMillimeters"])[surfaceIndex][coefficientIndex]))
                    .ToArray(),
                AnalysisSeriesKind.Bar,
                AberrationNames[coefficientIndex],
                ColorIndex: coefficientIndex,
                Opacity: 1,
                XQuantity: AnalysisAxisQuantity.Coordinate,
                XUnit: AnalysisAxisUnit.Dimensionless,
                YQuantity: AnalysisAxisQuantity.Coefficient,
                YUnit: AnalysisAxisUnit.Millimeter))
            .ToArray();
        var maximum = _maximumAberration;
        if (maximum == 0)
        {
            // Include the displayed table values as well as unrounded series values:
            // the desktop bars currently use the table's printed coefficients.
            var peak = series.SelectMany(item => item.Points)
                .Select(point => point.Y)
                .Concat(rows.SelectMany(row => row.Skip(1)).Select(value =>
                    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : 0))
                .Where(double.IsFinite)
                .Select(Math.Abs)
                .DefaultIfEmpty(0)
                .Max();
            maximum = peak > 0
                ? peak > double.MaxValue / 1.1 ? double.MaxValue : peak * 1.1
                : 0.1;
        }

        var values = coefficients.Values.ToDictionary(item => item.Key, item => item.Value);
        values["MaximumAberration"] = maximum;
        values["GridInterval"] = _gridInterval;

        return new AnalysisData(
            Name,
            values,
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                YMinimum: -maximum,
                YMaximum: maximum,
                ShowLegend: true,
                HideTickLabels: true,
                LegendBelow: true),
            Table: new AnalysisTable(coefficients.Table.Columns, rows),
            ReportText: coefficients.ReportText);
    }
}
