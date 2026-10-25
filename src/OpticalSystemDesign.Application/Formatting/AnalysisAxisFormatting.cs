using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Application.Formatting;

public static class AnalysisAxisFormatting
{
    private static readonly Regex ParenthesizedUnit = new(
        @"[（(]\s*(?:mm|µm|μm|nm|waves?|deg(?:rees)?|°|%|cycles/mm|cycles/mrad|µm⁻¹|μm⁻¹|radians?|rad|mrad|px|dB|W/sr|W/mm²|10⁻⁶/K|D|-)\s*[）)]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExplicitUnit = new(
        @"(?:单位\s*[:：]\s*)?(?:毫米|微米|µm|μm|mm|nm|waves?|degrees?|deg|°|%|cycles/mm|cycles/mrad|µm⁻¹|μm⁻¹|radians?|rad|mrad|px|dB|W/sr|W/mm²|10⁻⁶/K|D)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EnglishInUnit = new(
        @"\s+in\s+(?:millimeters?|micrometers?|degrees?|waves?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string UnitSymbol(AnalysisAxisUnit unit) => unit switch
    {
        AnalysisAxisUnit.Dimensionless => "1",
        AnalysisAxisUnit.Millimeter => "mm",
        AnalysisAxisUnit.Micrometer => "µm",
        AnalysisAxisUnit.Nanometer => "nm",
        AnalysisAxisUnit.Degree => "°",
        AnalysisAxisUnit.Wave => "waves",
        AnalysisAxisUnit.Percent => "%",
        AnalysisAxisUnit.CyclesPerMillimeter => "cycles/mm",
        AnalysisAxisUnit.InverseMicrometer => "µm⁻¹",
        AnalysisAxisUnit.Pixel => "px",
        AnalysisAxisUnit.Radian => "rad",
        AnalysisAxisUnit.Milliradian => "mrad",
        AnalysisAxisUnit.Decibel => "dB",
        AnalysisAxisUnit.WattsPerSteradian => "W/sr",
        AnalysisAxisUnit.WattsPerSquareMillimeter => "W/mm²",
        AnalysisAxisUnit.PartsPerMillionPerKelvin => "10⁻⁶/K",
        AnalysisAxisUnit.Watt => "W",
        AnalysisAxisUnit.CyclesPerMilliradian => "cycles/mrad",
        AnalysisAxisUnit.Diopter => "D",
        AnalysisAxisUnit.WattsPerSquareMeterPerNanometer => "W/m²/nm",
        _ => string.Empty
    };

    public static string FormatValue(
        double value,
        AnalysisAxisUnit unit,
        IFormatProvider? provider = null)
    {
        var formatted = NumericDisplayFormatter.Format(value, provider ?? CultureInfo.CurrentCulture);
        var symbol = UnitSymbol(unit);
        return string.IsNullOrEmpty(symbol) || unit == AnalysisAxisUnit.Dimensionless
            ? formatted
            : $"{formatted} {symbol}";
    }

    public static string FormatLabel(
        string? legacyLabel,
        AnalysisAxisQuantity quantity,
        AnalysisAxisUnit unit)
    {
        var label = ParenthesizedUnit.Replace(legacyLabel ?? string.Empty, string.Empty);
        label = EnglishInUnit.Replace(label, string.Empty);
        label = ExplicitUnit.Replace(label, string.Empty);
        label = label.Trim().TrimEnd(':', '：', ',', '，');
        if (string.IsNullOrWhiteSpace(label))
        {
            label = QuantityLabel(quantity);
        }

        var symbol = UnitSymbol(unit);
        return string.IsNullOrEmpty(symbol) || unit == AnalysisAxisUnit.Dimensionless
            ? label
            : $"{label} ({symbol})";
    }

    public static bool CanConvert(AnalysisAxisUnit source, AnalysisAxisUnit target)
    {
        if (source == target)
        {
            return source != AnalysisAxisUnit.Unspecified;
        }

        return IsLength(source) && IsLength(target)
            || IsAngle(source) && IsAngle(target);
    }

    public static double Convert(double value, AnalysisAxisUnit source, AnalysisAxisUnit target)
    {
        if (source == target)
        {
            return value;
        }

        if (IsLength(source) && IsLength(target))
        {
            return value * MillimetersPerUnit(source) / MillimetersPerUnit(target);
        }

        if (IsAngle(source) && IsAngle(target))
        {
            var radians = source switch
            {
                AnalysisAxisUnit.Degree => value * Math.PI / 180,
                AnalysisAxisUnit.Milliradian => value / 1000,
                _ => value
            };
            return target switch
            {
                AnalysisAxisUnit.Degree => radians * 180 / Math.PI,
                AnalysisAxisUnit.Milliradian => radians * 1000,
                _ => radians
            };
        }

        throw new ArgumentException($"Cannot convert axis unit {source} to {target}.");
    }

    private static bool IsLength(AnalysisAxisUnit unit) => unit is
        AnalysisAxisUnit.Millimeter or AnalysisAxisUnit.Micrometer or AnalysisAxisUnit.Nanometer;

    private static bool IsAngle(AnalysisAxisUnit unit) => unit is
        AnalysisAxisUnit.Degree or AnalysisAxisUnit.Radian or AnalysisAxisUnit.Milliradian;

    private static double MillimetersPerUnit(AnalysisAxisUnit unit) => unit switch
    {
        AnalysisAxisUnit.Millimeter => 1,
        AnalysisAxisUnit.Micrometer => 1e-3,
        AnalysisAxisUnit.Nanometer => 1e-6,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null)
    };

    private static string QuantityLabel(AnalysisAxisQuantity quantity) => quantity switch
    {
        AnalysisAxisQuantity.Coordinate => "坐标",
        AnalysisAxisQuantity.FieldAngle => "视场角",
        AnalysisAxisQuantity.FieldHeight => "视场高度",
        AnalysisAxisQuantity.NormalizedField => "归一化视场",
        AnalysisAxisQuantity.Probability => "概率",
        AnalysisAxisQuantity.ImageHeight => "像高",
        AnalysisAxisQuantity.ObjectHeight => "物高",
        AnalysisAxisQuantity.PupilCoordinate => "归一化光瞳",
        AnalysisAxisQuantity.Wavelength => "波长",
        AnalysisAxisQuantity.WavefrontError => "波前差",
        AnalysisAxisQuantity.Defocus => "离焦",
        AnalysisAxisQuantity.Radius => "半径",
        AnalysisAxisQuantity.SpatialFrequency => "空间频率",
        AnalysisAxisQuantity.Modulation => "调制度",
        AnalysisAxisQuantity.EnergyFraction => "能量分数",
        AnalysisAxisQuantity.Irradiance => "辐照度",
        AnalysisAxisQuantity.Distortion => "畸变",
        AnalysisAxisQuantity.RayHeight => "光线高度",
        AnalysisAxisQuantity.IncidentAngle => "入射角",
        AnalysisAxisQuantity.Angle => "角度",
        AnalysisAxisQuantity.ZernikeTerm => "Zernike 项",
        AnalysisAxisQuantity.Coefficient => "系数",
        AnalysisAxisQuantity.SurfaceNumber => "表面序号",
        AnalysisAxisQuantity.RefractiveIndex => "折射率",
        AnalysisAxisQuantity.AbbeNumber => "阿贝数",
        AnalysisAxisQuantity.Dispersion => "色散",
        AnalysisAxisQuantity.Intensity => "强度",
        AnalysisAxisQuantity.Pixel => "像素",
        AnalysisAxisQuantity.ChromaticPower => "色光焦",
        AnalysisAxisQuantity.ThermalOpticalPower => "热光焦",
        AnalysisAxisQuantity.Transmission => "透过率",
        _ => string.Empty
    };
}

public static class AnalysisCsvFormatter
{
    public static string Format(AnalysisViewDto view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var builder = new StringBuilder();
        var includeConfiguration = view.ConfigurationScope is not null;
        var singleConfiguration = view.ConfigurationScope?.ConfigurationNumbers.Count == 1
            ? view.ConfigurationScope.ConfigurationNumbers[0].ToString(CultureInfo.InvariantCulture) : string.Empty;
        IEnumerable<string> WithConfiguration(IEnumerable<string> row, string configuration) =>
            includeConfiguration ? row.Append(configuration) : row;
        var source = view.PlotPanes.SelectMany(pane => pane.Series.Select(series => (Pane: pane.Title, Series: series)));
        if (!source.Any(item => item.Series.Points.Count > 0))
            source = view.Series.Select(series => (Pane: string.Empty, Series: series));

        if (!source.Any(item => item.Series.Points.Count > 0))
        {
            if (view.Table is { } table)
            {
                if (table.Columns.Count == 0)
                    throw new InvalidDataException("分析表格缺少列定义，无法导出 CSV。");
                var includeGroups = table.RowGroups?.Any(group => !string.IsNullOrEmpty(group)) == true;
                AppendRow(builder, WithConfiguration(includeGroups ? table.Columns.Append("row_group") : table.Columns, "configuration"));
                for (var index = 0; index < table.Rows.Count; index++)
                {
                    var row = table.Rows[index];
                    if (row.Count != table.Columns.Count)
                        throw new InvalidDataException($"分析表格第 {index + 1} 行的列数与表头不一致，已拒绝导出以免丢失数据。");
                    var group = table.RowGroups is { } groups && index < groups.Count ? groups[index] : string.Empty;
                    AppendRow(builder, WithConfiguration(includeGroups ? row.Append(group) : row, singleConfiguration));
                }
            }
            else if (view.Rows.Count > 0)
            {
                AppendRow(builder, WithConfiguration(["metric", "value"], "configuration"));
                foreach (var row in view.Rows) AppendRow(builder, WithConfiguration([row.Metric, row.Value], singleConfiguration));
            }
            else
            {
                throw new InvalidOperationException("分析结果没有可导出的 CSV 数据。");
            }
            return builder.ToString();
        }

        // Preserve the existing typed plot schema for chart and mixed views.
        builder.AppendLine("pane,series,x_quantity,x_unit,x,y_quantity,y_unit,y,value_quantity,value_unit,value,label"
            + (includeConfiguration ? ",configuration" : ""));

        foreach (var (pane, series) in source)
        {
            foreach (var point in series.Points)
            {
                Append(builder, pane);
                Append(builder, series.Name);
                Append(builder, series.XQuantity.ToString());
                Append(builder, AnalysisAxisFormatting.UnitSymbol(series.XUnit));
                Append(builder, point.X.ToString("R", CultureInfo.InvariantCulture));
                Append(builder, series.YQuantity.ToString());
                Append(builder, AnalysisAxisFormatting.UnitSymbol(series.YUnit));
                Append(builder, point.Y.ToString("R", CultureInfo.InvariantCulture));
                Append(builder, series.ValueQuantity.ToString());
                Append(builder, AnalysisAxisFormatting.UnitSymbol(series.ValueUnit));
                Append(builder, point.Value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);
                Append(builder, point.Label, endOfRow: !includeConfiguration);
                if (includeConfiguration)
                    Append(builder, series.ConfigurationNumber?.ToString(CultureInfo.InvariantCulture) ?? singleConfiguration, endOfRow: true);
            }
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first) builder.Append(',');
            builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            first = false;
        }
        builder.AppendLine();
    }

    private static void Append(StringBuilder builder, string value, bool endOfRow = false)
    {
        builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        builder.Append(endOfRow ? Environment.NewLine : ',');
    }
}
