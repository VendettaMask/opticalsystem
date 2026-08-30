using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed class SeidelCoefficientsAnalysis : BaseAnalysis
{
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
                ReportText: "未定义波长。");
        }

        var wavelength = SelectWavelength(wavelengths);
        var wavelengthMicrometers = wavelength.Micrometers;
        var wavelengthNanometers = wavelength.Nanometers;
        var marginal = Optic.Paraxial.MarginalRay(wavelengthMicrometers);
        var chief = Optic.Paraxial.ChiefRay(wavelengthMicrometers);
        var surfaces = Optic.SurfaceGroup.Items.ToArray();
        var shortWavelength = wavelengths.Min(item => item.Nanometers);
        var longWavelength = wavelengths.Max(item => item.Nanometers);
        var rows = new List<IReadOnlyList<string>>();
        var totals = new double[7];
        var petzvalSum = 0.0;
        var invariant = 0.0;

        for (var index = 1; index < surfaces.Length; index++)
        {
            var surface = surfaces[index];
            var previous = surfaces[index - 1];
            var nBefore = SafeIndex(previous.MaterialAfter.RefractiveIndex(wavelengthNanometers));
            var nAfter = SafeIndex(surface.MaterialAfter.RefractiveIndex(wavelengthNanometers));
            var curvature = surface.IsPlane ? 0.0 : 1.0 / surface.Radius;
            var marginalHeight = marginal.Heights[index][0];
            var chiefHeight = chief.Heights[index][0];
            var marginalSlopeBefore = marginal.Slopes[index - 1][0];
            var chiefSlopeBefore = chief.Slopes[index - 1][0];
            var marginalSlopeAfter = marginal.Slopes[index][0];

            var marginalIncidence = nBefore * (marginalSlopeBefore + (marginalHeight * curvature));
            var chiefIncidence = nBefore * (chiefSlopeBefore + (chiefHeight * curvature));
            var opticalInvariant = nBefore
                * ((chiefSlopeBefore * marginalHeight) - (marginalSlopeBefore * chiefHeight));
            if (Math.Abs(opticalInvariant) > Math.Abs(invariant))
            {
                invariant = opticalInvariant;
            }

            var deltaSlopeOverIndex = (marginalSlopeAfter / nAfter) - (marginalSlopeBefore / nBefore);
            var s1 = -marginalIncidence * marginalIncidence * marginalHeight * deltaSlopeOverIndex;
            var s2 = -marginalIncidence * chiefIncidence * marginalHeight * deltaSlopeOverIndex;
            var s3 = -chiefIncidence * chiefIncidence * marginalHeight * deltaSlopeOverIndex;
            var s4 = -opticalInvariant * opticalInvariant * curvature * ((1.0 / nAfter) - (1.0 / nBefore));
            var s5 = Math.Abs(marginalIncidence) <= 1e-15
                ? 0.0
                : -(chiefIncidence / marginalIncidence) * (s3 + s4);

            var nBeforeShort = SafeIndex(previous.MaterialAfter.RefractiveIndex(shortWavelength));
            var nAfterShort = SafeIndex(surface.MaterialAfter.RefractiveIndex(shortWavelength));
            var nBeforeLong = SafeIndex(previous.MaterialAfter.RefractiveIndex(longWavelength));
            var nAfterLong = SafeIndex(surface.MaterialAfter.RefractiveIndex(longWavelength));
            var chromaticPower = curvature
                * (((nAfterShort - nBeforeShort) / nAfterShort)
                    - ((nAfterLong - nBeforeLong) / nAfterLong));
            var cl = -marginalIncidence * marginalHeight * chromaticPower;
            var ct = -chiefIncidence * marginalHeight * chromaticPower;
            var coefficients = new[] { s1, s2, s3, s4, s5, cl, ct }
                .Select(FiniteOrZero)
                .ToArray();

            for (var coefficientIndex = 0; coefficientIndex < totals.Length; coefficientIndex++)
            {
                totals[coefficientIndex] += coefficients[coefficientIndex];
            }

            petzvalSum += curvature * (nAfter - nBefore) / (nBefore * nAfter);
            rows.Add(new[]
            {
                SurfaceLabel(surface, index == surfaces.Length - 1),
                coefficients[0].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[1].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[2].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[3].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[4].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[5].ToString("0.000000", CultureInfo.InvariantCulture),
                coefficients[6].ToString("0.000000", CultureInfo.InvariantCulture)
            });
        }

        rows.Add(new[]
        {
            "累计",
            totals[0].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[1].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[2].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[3].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[4].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[5].ToString("0.000000", CultureInfo.InvariantCulture),
            totals[6].ToString("0.000000", CultureInfo.InvariantCulture)
        });

        var petzvalRadius = Math.Abs(petzvalSum) <= 1e-15 ? double.PositiveInfinity : 1.0 / petzvalSum;
        var values = new Dictionary<string, object>
        {
            ["WavelengthMicrometers"] = wavelengthMicrometers,
            ["ChiefRaySlopeObjectSpace"] = FirstSlope(chief),
            ["ChiefRaySlopeImageSpace"] = LastSlope(chief),
            ["MarginalRaySlopeObjectSpace"] = FirstSlope(marginal),
            ["MarginalRaySlopeImageSpace"] = LastSlope(marginal),
            ["PetzvalRadius"] = petzvalRadius,
            ["OpticalInvariant"] = invariant,
            ["SurfaceCount"] = Math.Max(0, surfaces.Length - 1)
        };

        return new AnalysisData(
            Name,
            values,
            Table: new AnalysisTable(
                new[] { "表面", "SPHA S1", "COMA S2", "ASTI S3", "FCUR S4", "DIST S5", "CLA (CL)", "CTR (CT)" },
                rows),
            ReportText: BuildReport(values, rows));
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

    private static double FirstSlope(Services.ParaxialTrace trace)
    {
        return trace.Slopes.Count == 0 ? 0 : trace.Slopes[0][0];
    }

    private static double LastSlope(Services.ParaxialTrace trace)
    {
        return trace.Slopes.Count == 0 ? 0 : trace.Slopes[^1][0];
    }

    private static double SafeIndex(double value)
    {
        return !double.IsFinite(value) || Math.Abs(value) <= 1e-15 ? 1.0 : value;
    }

    private static double FiniteOrZero(double value)
    {
        return double.IsFinite(value) ? value : 0.0;
    }

    private static string BuildReport(
        IReadOnlyDictionary<string, object> values,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"波长                    : {Number(values["WavelengthMicrometers"]),12:0.0000} µm");
        builder.AppendLine($"主光线斜率，物空间      : {Number(values["ChiefRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"主光线斜率，像空间      : {Number(values["ChiefRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，物空间    : {Number(values["MarginalRaySlopeObjectSpace"]),12:0.0000}");
        builder.AppendLine($"边缘光线斜率，像空间    : {Number(values["MarginalRaySlopeImageSpace"]),12:0.0000}");
        builder.AppendLine($"佩兹伐半径              : {FormatNumber(Number(values["PetzvalRadius"]), "0.0000"),12}");
        builder.AppendLine($"光学不变量              : {Number(values["OpticalInvariant"]),12:0.0000}");
        builder.AppendLine();
        builder.AppendLine("赛德尔像差系数：");
        builder.AppendLine();
        builder.AppendLine(
            $"{Pad("表面", 8)}{Pad("SPHA S1", 14)}{Pad("COMA S2", 14)}{Pad("ASTI S3", 14)}"
            + $"{Pad("FCUR S4", 14)}{Pad("DIST S5", 14)}{Pad("CLA (CL)", 14)}{Pad("CTR (CT)", 14)}");
        foreach (var row in rows)
        {
            builder.AppendLine(
                $"{Pad(row[0], 8)}{Pad(row[1], 14)}{Pad(row[2], 14)}{Pad(row[3], 14)}"
                + $"{Pad(row[4], 14)}{Pad(row[5], 14)}{Pad(row[6], 14)}{Pad(row[7], 14)}");
        }

        return builder.ToString().TrimEnd();
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
                : value.ToString(format, CultureInfo.InvariantCulture);
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
            return new AnalysisData(Name, coefficients.Values, ReportText: coefficients.ReportText);
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
                    double.TryParse(
                        row[coefficientIndex + 1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var coefficient)
                            ? coefficient
                            : 0))
                    .ToArray(),
                AnalysisSeriesKind.Bar,
                AberrationNames[coefficientIndex],
                ColorIndex: coefficientIndex,
                Opacity: 1))
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
