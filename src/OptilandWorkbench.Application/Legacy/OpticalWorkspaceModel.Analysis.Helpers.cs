using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
    private static string FormatAnalysisData(AnalysisData data)
    {
        var lines = new List<string> { $"分析：{DisplayAnalysisName(data.Name)}" };
        lines.AddRange(data.Values.Select(item => $"{DisplayAnalysisKey(item.Key)}：{FormatAnalysisValue(item.Value)}"));
        if (data.Table is not null)
        {
            lines.Add(string.Empty);
            lines.Add(string.Join('\t', data.Table.Columns));
            lines.AddRange(data.Table.Rows.Select(row => string.Join('\t', row)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static AnalysisParameterDescriptor IntParameter(
        string key,
        string displayName,
        string defaultValue,
        double minimum,
        double maximum)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Integer,
            defaultValue,
            minimum,
            maximum,
            1);
    }

    private static AnalysisParameterDescriptor DoubleParameter(
        string key,
        string displayName,
        string defaultValue,
        double minimum,
        double maximum,
        double increment)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Double,
            defaultValue,
            minimum,
            maximum,
            increment);
    }

    private static AnalysisParameterDescriptor ChoiceParameter(
        string key,
        string displayName,
        string defaultValue,
        IReadOnlyList<string> choices)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Choice,
            defaultValue,
            Choices: choices);
    }

    private static AnalysisParameterDescriptor BoolParameter(
        string key,
        string displayName,
        string defaultValue)
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.Boolean,
            defaultValue);
    }

    private AnalysisParameterDescriptor[] DistortionParameters(bool angularField)
    {
        var parameters = new List<AnalysisParameterDescriptor>
        {
            DoubleParameter("MaximumDistortion", "最大畸变（0=自动）", "0", 0, 1_000_000, 0.1),
            ChoiceParameter(
                "WavelengthNumber",
                "波长（0=所有）",
                "0",
                new[] { "0" }.Concat(Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count)).Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
            ChoiceParameter("DisplayMode", "显示为", "百分比", new[] { "百分比", "绝对值" }),
            ChoiceParameter(
                "ReferenceFieldNumber",
                "参考视场",
                "1",
                Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count)).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
            BoolParameter("IgnoreVignettingFactors", "忽略渐晕因数", "true")
        };
        if (angularField)
        {
            parameters.Insert(2, ChoiceParameter(
                "DistortionType",
                "畸变模型",
                "F-Tan(Theta)",
                new[] { "F-Tan(Theta)", "F-Theta" }));
        }

        return parameters.ToArray();
    }

    private AnalysisParameterDescriptor[] GridDistortionParameters()
    {
        return new[]
        {
            ChoiceParameter("DisplayMode", "显示", "截面", new[] { "截面", "向量" }),
            IntParameter("NumPoints", "网格尺寸", "12", 2, 128),
            DoubleParameter("Scale", "缩放", "1", 0, 1_000_000, 0.1),
            BoolParameter("SymmetricMagnification", "对称放大", "false"),
            ChoiceParameter(
                "WavelengthNumber",
                "波长",
                "1",
                Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count)).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
            ChoiceParameter(
                "ReferenceFieldNumber",
                "参考视场",
                "1",
                Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count)).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
            DoubleParameter("HeightWidthAspect", "H/W 纵横比", "1", 0.000001, 1_000_000, 0.1),
            DoubleParameter("FieldWidth", "视场宽度（0=自动）", "0", 0, 1_000_000, 0.1)
        };
    }

    private bool UsesAngularDistortionModel()
    {
        if (CurrentOptic.FieldDefinition == FieldDefinitionKind.Angle)
        {
            return true;
        }

        if (CurrentOptic.FieldDefinition != FieldDefinitionKind.RealImageHeight)
        {
            return false;
        }

        var objectSurface = CurrentOptic.SurfaceGroup.Items.FirstOrDefault();
        return objectSurface is null
            || double.IsInfinity(objectSurface.CoordinateSystem.Origin.Z)
            || Math.Abs(objectSurface.Thickness) <= 1e-12;
    }

    private static int TryReadInt(IReadOnlyDictionary<string, string> settings, string key, int fallback)
    {
        return settings.TryGetValue(key, out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static double TryReadDouble(IReadOnlyDictionary<string, string> settings, string key, double fallback)
    {
        return settings.TryGetValue(key, out var value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;
    }

    private static bool TryReadBool(IReadOnlyDictionary<string, string> settings, string key, bool fallback)
    {
        return settings.TryGetValue(key, out var value) && bool.TryParse(value, out var flag)
            ? flag
            : fallback;
    }

    private static string FormatAnalysisValue(object value)
    {
        return value switch
        {
            double number => NumericDisplayFormatter.Format(number),
            float number => NumericDisplayFormatter.Format(number),
            string text when text == "linear-height" => "线性高度",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static AnalysisSeries? BuildFallbackSeries(IReadOnlyDictionary<string, object> values)
    {
        var points = values
            .Select(item => (item.Key, Value: TryConvertFiniteNumber(item.Value)))
            .Where(item => item.Value.HasValue)
            .Select((item, index) => new AnalysisPoint(index, item.Value!.Value, DisplayAnalysisKey(item.Key)))
            .ToArray();
        return points.Length == 0
            ? null
            : new AnalysisSeries("Metric", "Value", points, AnalysisSeriesKind.Bar);
    }

    private static double? TryConvertFiniteNumber(object value)
    {
        if (value is not IConvertible || value is string or bool or char)
        {
            return null;
        }

        try
        {
            var number = Convert.ToDouble(value);
            return double.IsFinite(number) ? number : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
