using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Core.Analysis;

public sealed class SeidelCoefficientsAnalysis : BaseAnalysis
{
    private sealed record SurfaceCoefficients(string Label, double[] Values);

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
        var contributions = computed.Surfaces.Select((row, index) => new SurfaceCoefficients(
            SurfaceLabel(surfaces[index + 1], index + 2 == surfaces.Length), row.Coefficients.ToArray())).ToList();
        contributions.Add(new SurfaceCoefficients("累计", computed.Total.ToArray()));
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
            ["SeidelCoefficientsMillimeters"] = contributions.Select(c => c.Values.ToArray()).ToArray()
        };
        var marginalSlope = computed.MarginalSlopeImage;
        var transverseFactor = Math.Abs(marginalSlope) > 1e-15 ? -1 / (2 * imageIndex * marginalSlope) : double.NaN;
        var longitudinalFactor = Math.Abs(marginalSlope) > 1e-15 ? 1 / (2 * imageIndex * marginalSlope * marginalSlope) : double.NaN;
        values["WaveAberrationCoefficients"] = contributions.Select(c => WaveCoefficients(c.Values, wavelengthMicrometers / 1000)).ToArray();
        values["TransverseAberrationCoefficientsMillimeters"] = contributions.Select(c => RayAberrationCoefficients(c.Values, transverseFactor, -2 * transverseFactor)).ToArray();
        values["LongitudinalAberrationCoefficientsMillimeters"] = contributions.Select(c => RayAberrationCoefficients(c.Values, longitudinalFactor, 2 * longitudinalFactor)).ToArray();

        var table = CoefficientTable(SeidelColumns, contributions, coefficients => coefficients);
        return new AnalysisData(
            Name,
            values,
            Table: table,
            ReportText: BuildReport(values, contributions, table));
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
        IReadOnlyList<SurfaceCoefficients> contributions,
        AnalysisTable seidelTable)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"波长                    : {Number(values["WavelengthMicrometers"]),12:0.0000} µm");
        builder.AppendLine($"主光线斜率，物空间      : {Number(values["ChiefRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"主光线斜率，像空间      : {Number(values["ChiefRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，物空间    : {Number(values["MarginalRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，像空间    : {Number(values["MarginalRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"佩兹伐半径              : {FormatNumber(Number(values["PetzvalRadius"]), "0.0000"),12}");
        builder.AppendLine($"光学不变量              : {Number(values["OpticalInvariant"]),12:0.0000}");
        AppendTable(builder, "赛德尔像差系数：", seidelTable);

        // Core lens lengths are millimeters. Conversion uses unrounded surface
        // contributions and the final image-space n' and paraxial marginal u'.
        // Ansys OpticStudio User Guide, Seidel Coefficients, conversion table.
        var wavelengthMillimeters = Number(values["WavelengthMicrometers"]) / 1000;
        var n = Number(values["ImageSpaceRefractiveIndex"]);
        var u = Number(values["MarginalRaySlopeImageSpace"]);
        var canConvert = double.IsFinite(u) && Math.Abs(u) > 1e-15;
        var transverseFactor = canConvert ? -1 / (2 * n * u) : double.NaN;
        var longitudinalFactor = canConvert ? 1 / (2 * n * u * u) : double.NaN;
        var waves = CoefficientTable(WaveColumns, contributions, coefficients => WaveCoefficients(coefficients, wavelengthMillimeters));
        AppendTable(builder, "赛德尔像差系数（波长）：", waves);
        AppendTable(builder, "横向像差系数：", CoefficientTable(TransverseColumns, contributions,
            coefficients => RayAberrationCoefficients(coefficients, transverseFactor, -2 * transverseFactor)));
        AppendTable(builder, "轴向像差系数：", CoefficientTable(LongitudinalColumns, contributions,
            coefficients => RayAberrationCoefficients(coefficients, longitudinalFactor, 2 * longitudinalFactor)));

        AppendTable(builder, "波前像差系数汇总（波长）：", new AnalysisTable(WaveColumns, new[] { waves.Rows[^1] }));
        AppendTable(builder, "场曲波前系数汇总（波长）：", CoefficientTable(
            new[] { "表面", "W220S", "W220M", "W220T" }, new[] { contributions[^1] },
            coefficients => new[]
            {
                (coefficients[2] + coefficients[3]) / (4 * wavelengthMillimeters),
                ((2 * coefficients[2]) + coefficients[3]) / (4 * wavelengthMillimeters),
                ((3 * coefficients[2]) + coefficients[3]) / (4 * wavelengthMillimeters)
            }));
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
        IReadOnlyList<SurfaceCoefficients> contributions,
        Func<double[], double[]> convert) => new(columns, contributions.Select(row =>
            (IReadOnlyList<string>)new[] { row.Label }.Concat(convert(row.Values)
                .Select(value => double.IsFinite(value) ? FormatNumber(value, "0.000000") : "—")).ToArray()).ToArray());

    private static void AppendTable(StringBuilder builder, string title, AnalysisTable table)
    {
        builder.AppendLine();
        builder.AppendLine(title);
        builder.AppendLine();
        foreach (var row in new[] { table.Columns }.Concat(table.Rows))
        {
            builder.AppendLine(string.Concat(row.Select((value, index) => Pad(value, index == 0 ? 8 : 14))));
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

    private static string Pad(string value, int width)
    {
        return value.PadRight(Math.Max(width, value.Length + 1));
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
        _maximumAberration = maximumAberration > 0 ? maximumAberration : 0.1;
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
        var values = coefficients.Values.ToDictionary(item => item.Key, item => item.Value);
        values["MaximumAberration"] = _maximumAberration;
        values["GridInterval"] = _gridInterval;

        return new AnalysisData(
            Name,
            values,
            series.FirstOrDefault(),
            series,
            new AnalysisPlotOptions(
                YMinimum: -_maximumAberration,
                YMaximum: _maximumAberration,
                ShowLegend: true,
                HideTickLabels: true,
                LegendBelow: true),
            Table: new AnalysisTable(coefficients.Table.Columns, rows),
            ReportText: coefficients.ReportText);
    }
}
