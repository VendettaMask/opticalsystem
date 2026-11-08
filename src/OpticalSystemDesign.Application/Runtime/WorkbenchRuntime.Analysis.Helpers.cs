using System.Collections.ObjectModel;
using System.Globalization;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;
using ContractMeritFunctionPreset = OpticalSystemDesign.Application.Contracts.MeritFunctionPreset;

namespace OpticalSystemDesign.Application.Runtime;

public partial class WorkbenchRuntime
{
    private static AnalysisView FormatYYbarView(AnalysisView view, AnalysisData data)
    {
        var culture = CultureInfo.InvariantCulture;
        var compatible = data.Values.TryGetValue("FirstSurface", out var firstSurface);
        var first = compatible ? (int)firstSurface! : 0;
        var last = data.Values.TryGetValue("LastSurface", out var lastSurface)
            ? (int)lastSurface : (int)data.Values["SurfaceCount"] - 1;
        var wavelength = ((double)data.Values["WavelengthMicrometers"]).ToString("F6", culture);
        var rows = new List<IReadOnlyList<string>>();
        var maximumHeight = 0d;
        for (var surface = first; surface <= last; surface++)
        {
            var chief = (double)data.Values[$"Surface {surface} Chief"];
            var marginal = (double)data.Values[$"Surface {surface} Marginal"];
            maximumHeight = Math.Max(maximumHeight, Math.Max(Math.Abs(chief), Math.Abs(marginal)));
            rows.Add([surface.ToString(culture), chief.ToString("0.000000000E+00", culture),
                marginal.ToString("0.000000000E+00", culture)]);
        }

        var metadata = new AnalysisRow[] { new("第一面", first.ToString(culture)),
            new("最后面", last.ToString(culture)), new("波长 (µm)", wavelength) };
        var report = new List<string> { "Y–Ybar 数据列表" };
        report.AddRange(metadata.Select(row => $"{row.Metric}：{row.Value}"));
        report.AddRange(["", "Y-Bar 为主光线高度，Y 为边缘光线高度，单位为 mm。",
            $"{"Surf",4}  {"Y-Bar",17}  {"Y",17}"]);
        report.AddRange(rows.Select(row => $"{row[0],4}  {row[1],17}  {row[2],17}"));
        var halfSpan = Math.Max(5, 5 * Math.Ceiling(maximumHeight / 5));
        return view with
        {
            Rows = metadata,
            ReportText = string.Join(Environment.NewLine, report),
            Table = new AnalysisTable(["面号", "Y-Bar (mm)", "Y (mm)"], rows),
            PlotOptions = compatible && double.IsFinite(halfSpan)
                ? view.PlotOptions with { XMinimum = -halfSpan, XMaximum = halfSpan, YMinimum = -halfSpan, YMaximum = halfSpan }
                : view.PlotOptions
        };
    }

    private static AnalysisView FormatIncidentAngleView(AnalysisView view, AnalysisData data)
    {
        const int columnWidth = 20;
        var series = view.SeriesList;
        var rayNames = series.Select(ray => ray.Name).ToArray();
        string[] columns = ["像高 (mm)", .. rayNames.Select(name => $"{name} (°)")];
        var rows = new List<IReadOnlyList<string>>();
        // Every ray series keeps one slot per field, including failed traces.
        for (var index = 0; index < series[0].Points.Count; index++)
        {
            var points = series.Select(ray => ray.Points[index]).ToArray();
            var height = points.FirstOrDefault(point => double.IsFinite(point.X))?.X ?? double.NaN;
            rows.Add([Number(height, "0.000000E+00"), .. points.Select(point => Number(point.Y, "F6"))]);
        }

        var wavelength = Number((double)data.Values["WavelengthMicrometers"], "F4");
        var report = new List<string>
        {
            "入射角度与像高列表",
            "",
            "像高单位为 mm，入射角单位为 °。",
            $"波长：{wavelength} µm（序号 {data.Values["WavelengthNumber"]}）",
            $"测量表面：{data.Values["SurfaceIndex"]}",
            $"视场密度：{data.Values["FieldDensity"]}（{rows.Count} 个采样点）",
            "",
            // Figure spaces and the CJK glyph allowance follow the bundled report fonts.
            string.Join("\u2007\u2007", columns.Select(column =>
                column.PadLeft(columnWidth - column.Count(character => character > '\u3000') / 2, '\u2007')))
        };
        report.AddRange(rows.Select(row => string.Join("  ", row.Select(cell => cell.PadLeft(columnWidth)))));
        return view with
        {
            ReportText = string.Join(Environment.NewLine, report),
            Table = new AnalysisTable(columns, rows)
        };

        static string Number(double value, string format) => double.IsFinite(value)
            ? (value == 0 ? 0d : value).ToString(format, CultureInfo.InvariantCulture)
            : "—";
    }

    private static AnalysisRow FormatAnalysisRow(string analysisKey, KeyValuePair<string, object> item)
    {
        var (key, value) = item;
        if (analysisKey == "Fourier MTF vs Field" && value is double[] numbers)
        {
            if (key == "SpatialFrequencies")
                return new AnalysisRow("视场扫描频率", string.Join("、",
                    numbers.Select(number => number.ToString("0.########", CultureInfo.InvariantCulture))));
            if (key == "WavelengthsMicrometers" && numbers.Length > 0)
            {
                var endpoints = new[] { numbers.Min(), numbers.Max() }.Distinct();
                return new AnalysisRow("波长范围", string.Join("–",
                    endpoints.Select(number => number.ToString("F4", CultureInfo.InvariantCulture))));
            }
        }
        var label = analysisKey switch
        {
            "Contrast Loss Map" => key switch
            {
                "Frequency" => "空间频率",
                "RequestedFrequency" => "请求空间频率",
                "CutoffFrequency" => "衍射截止频率",
                _ => DisplayAnalysisKey(key)
            },
            "Huygens PSF Cross Section" when key == "ProfileType" => "截面方向",
            _ => DisplayAnalysisKey(key)
        };
        var format = analysisKey switch
        {
            "FFT Surface MTF" or "Huygens Surface MTF" or "FFT MTF Map" or "Geometric MTF Map"
                when key is "MtfGridMinimum" or "MtfGridMaximum" => "0.######",
            "Seidel Coefficients" when key is "WavelengthMicrometers" or "ChiefRaySlopeObjectSpace" or "ChiefRaySlopeImageSpace"
                or "MarginalRaySlopeObjectSpace" or "MarginalRaySlopeImageSpace" or "PetzvalRadius" or "OpticalInvariant" => "F4",
            "Grid Distortion" => key switch
            {
                "GridFieldWidth" or "GridFieldHeight" or "GridImageWidth" or "GridImageHeight" => "F2",
                "Scale" => "F3",
                "MaximumDistortionPercent" or "SmiaTvDistortionPercent" or "WavelengthMicrometers" => "F4",
                _ => null
            },
            "Contrast Loss Map" => key switch
            {
                "ContrastFieldX" or "ContrastFieldY" => "F2",
                "Frequency" or "WavelengthMicrometers" or "ContrastTangentialMtf" or "ContrastSagittalMtf"
                    or "ContrastDiffractionLimit" or "MaximumContrastLoss" => "F4",
                _ => null
            },
            "Huygens PSF Cross Section" => key switch
            {
                "PsfFieldX" or "PsfFieldY" or "ImageExtentMicrometers" or "ImageExtentMilliradians" => "F2",
                "PeakStrehlRatio" => "F3",
                "PsfImageCenterX" or "PsfImageCenterY" => "F6",
                _ => null
            },
            _ => null
        };
        if (format is null || value is not double number) return new AnalysisRow(label, FormatAnalysisValue(value));
        return new AnalysisRow(label, key == "PetzvalRadius" && double.IsInfinity(number)
            ? (number > 0 ? "∞" : "−∞")
            : double.IsFinite(number) ? number.ToString(format, CultureInfo.InvariantCulture) : "—");
    }

    private static string FormatAnalysisData(string canonicalName, AnalysisData data)
    {
        var lines = new List<string> { $"分析：{DisplayAnalysisName(data.Name)}" };
        lines.AddRange(data.Values.Where(item => item.Value is not IReadOnlyList<AnalysisSeries>)
            .Select(item => FormatAnalysisRow(canonicalName, item))
            .Select(row => $"{row.Metric}：{row.Value}"));
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

    private static AnalysisParameterDescriptor FileParameter(
        string key,
        string displayName,
        string defaultValue = "")
    {
        return new AnalysisParameterDescriptor(
            key,
            displayName,
            AnalysisParameterKind.File,
            defaultValue);
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

    private AnalysisParameterDescriptor[] FieldCurvatureAndDistortionParameters(bool angularField)
    {
        var parameters = new List<AnalysisParameterDescriptor>
        {
            DoubleParameter("MaximumCurvature", "最大场曲（0=自动）", "0", 0, 1_000_000, 0.1),
            DoubleParameter("MaximumDistortion", "最大畸变（0=自动）", "0", 0, 1_000_000, 0.1),
            ChoiceParameter(
                "WavelengthNumber",
                "波长（0=所有）",
                "0",
                new[] { "0" }.Concat(Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                    .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
            ChoiceParameter("DisplayMode", "畸变显示为", "百分比", new[] { "百分比", "绝对值" }),
            ChoiceParameter("ScanDirection", "扫描方向", "+y", new[] { "+y", "+x", "-y", "-x" }),
            ChoiceParameter(
                "ReferenceFieldNumber",
                "参考视场",
                "1",
                Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                    .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
            DoubleParameter("ParabasalDelta", "傍轴光瞳间隔", CurrentOptic.ImageSpaceAfocal ? "0.0001" : "0.001", 1e-8, 0.1, 0.00001),
            BoolParameter("ChiefRayParabasal", "以主光线为傍轴参考", CurrentOptic.ImageSpaceAfocal ? "false" : "true"),
            BoolParameter("IgnoreVignettingFactors", "忽略渐晕因数", "true")
        };
        if (angularField)
        {
            parameters.Insert(3, ChoiceParameter(
                "DistortionType",
                "畸变模型",
                "F-Tan(Theta)",
                DistortionModelChoices()));
        }

        return parameters.ToArray();
    }

    private static string[] DistortionModelChoices()
    {
        return new[]
        {
            "F-Tan(Theta)",
            "F-Theta",
            "Calibrated F-Theta",
            "Calibrated F-Tan(Theta)",
            "SMIA-TV"
        };
    }

    private AnalysisParameterDescriptor[] GridDistortionParameters()
    {
        var primary = Math.Max(1, CurrentOptic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1);
        return new[]
        {
            ChoiceParameter("DisplayMode", "显示", "截面", new[] { "截面", "向量" }),
            IntParameter("NumPoints", "网格尺寸", "13", 2, 128),
            DoubleParameter("Scale", "缩放", "1", 0, 1_000_000, 0.1),
            BoolParameter("SymmetricMagnification", "对称放大", "false"),
            ChoiceParameter(
                "WavelengthNumber",
                "波长",
                primary.ToString(CultureInfo.InvariantCulture),
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

        return ObjectConjugate.IsInfinite(CurrentOptic.SurfaceGroup.Items.FirstOrDefault());
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
            FourierValidationReport report => $"{(report.Mode == FourierSamplingMode.ValidateRequested ? "验证原采样" : "自适应精度")}："
                + $"请求 {report.RequestedPupilSampling}，返回 {report.ReturnedPupilSampling}，"
                + $"实际最高 {report.Steps.LastOrDefault()?.PupilSampling ?? 0}，"
                + $"相对RMS目标 {report.RelativeRmsAccuracy:P2}；{(report.Validated ? "验证通过" : "未通过验证")}",
            double number => NumericDisplayFormatter.Format(number),
            float number => NumericDisplayFormatter.Format(number),
            string text when text == "linear-height" => "线性高度",
            string text when text == "Physical aperture" => "物理孔径",
            string text when text == "Surface semi-diameter sampling window (no physical aperture added)"
                => "表面半口径采样窗口（未添加物理孔径）",
            _ => value.ToString() ?? string.Empty
        };
    }

}
