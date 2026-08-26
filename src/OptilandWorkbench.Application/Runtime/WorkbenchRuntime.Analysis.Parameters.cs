using System.Collections.ObjectModel;
using System.Globalization;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
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
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Runtime;

public partial class WorkbenchRuntime
{
    public IReadOnlyList<AnalysisParameterDescriptor> GetAnalysisParameters(string analysisName)
    {
        // Descriptor defaults are Workbench product presets. A value matching a
        // captured Zemax window describes that preset only, not a Zemax-wide
        // specification.
        var distributionChoices = new[] { "hexapolar", "uniform", "sobol", "random", "line_x", "line_y", "ring" };
        var primaryWavelengthNumber = Math.Max(
            1,
            CurrentOptic.Wavelengths.ToList().FindIndex(wavelength => wavelength.IsPrimary) + 1);
        var defaultFieldWidth = FieldCoordinates.MaximumRadius(CurrentOptic.Fields)
            .ToString("0.######", CultureInfo.InvariantCulture);
        var imageSimulationWavelengthChoices = new[] { "RGB" }
            .Concat(CurrentOptic.Wavelengths.Select((wavelength, index) =>
                $"{index + 1} - {wavelength.Micrometers:0.0000} µm"))
            .ToArray();
        var imageSimulationFieldChoices = CurrentOptic.Fields.Count == 0
            ? new[] { "1 - 轴上视场" }
            : CurrentOptic.Fields.Select((field, index) =>
                $"{index + 1} - {field.Label}").ToArray();
        var fftSamplingChoices = new[]
        {
            "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384"
        };
        if (!WorkbenchAnalysisCatalog.TryGetDescriptor(analysisName, out var descriptor))
        {
            return Array.Empty<AnalysisParameterDescriptor>();
        }

        return descriptor.CanonicalKey switch
        {
            "Seidel Coefficients" => new[]
            {
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray())
            },
            "Seidel Diagram" => new[]
            {
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray()),
                DoubleParameter("MaximumAberration", "最大像差范围（毫米）", "0.1", 0.000001, 1000, 0.01),
                DoubleParameter("GridInterval", "网格线间隔（毫米）", "0.01", 0.000001, 1000, 0.001)
            },
            "Single Ray Trace" => new[]
            {
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1",
                    new[] { "任意" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                DoubleParameter("Hx", "Hx", "0", -1, 1, 0.01),
                DoubleParameter("Hy", "Hy", "0", -1, 1, 0.01),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "1",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                DoubleParameter("Px", "Px", "0", -1, 1, 0.01),
                DoubleParameter("Py", "Py", "0", -1, 1, 0.01),
                BoolParameter("GlobalCoordinates", "全局坐标", "false"),
                ChoiceParameter(
                    "Type",
                    "类型",
                    "方向余弦",
                    new[] { "方向余弦", "正切角", "Ym, Um, Yc, Uc" }),
                BoolParameter("UseRayAiming", "使用系统光线瞄准", "true"),
                BoolParameter("ShowRaySegments", "显示光线段", "false")
            },
            "Non-Sequential Ray Trace" => new[]
            {
                ChoiceParameter(
                    "SourceNumber",
                    "光源对象（0=全部）",
                    "0",
                    new[] { "0" }.Concat(
                        Enumerable.Range(1, CurrentNonSequentialDocument.Objects.Count(item => item.Enabled && item.Kind is
                            NonSequentialObjectKind.SourceRay
                                or NonSequentialObjectKind.SourcePoint
                                or NonSequentialObjectKind.SourceRectangle
                                or NonSequentialObjectKind.SourceGaussian))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                BoolParameter("DirectRay", "使用直接 XYZ/LMN 射线", "false"),
                DoubleParameter("X", "起点 X (mm)", "0", -1_000_000, 1_000_000, 0.1),
                DoubleParameter("Y", "起点 Y (mm)", "0", -1_000_000, 1_000_000, 0.1),
                DoubleParameter("Z", "起点 Z (mm)", "0", -1_000_000, 1_000_000, 0.1),
                DoubleParameter("L", "方向 L", "0", -1, 1, 0.01),
                DoubleParameter("M", "方向 M", "0", -1, 1, 0.01),
                DoubleParameter("N", "方向 N", "1", -1, 1, 0.01),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "1",
                    Enumerable.Range(1, Math.Max(1, CurrentNonSequentialDocument.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                DoubleParameter("PowerWatts", "直接射线功率 (W)", "1", 0.000000001, 1_000_000, 0.1),
                BoolParameter("LayoutRays", "使用布局射线数", "false"),
                BoolParameter("SplitFresnelRays", "Fresnel 分支", "true")
            },
            "Non-Sequential Detector Viewer" => new[]
            {
                ChoiceParameter(
                    "DetectorNumber",
                    "探测器对象",
                    "1",
                    Enumerable.Range(1, Math.Max(1, CurrentNonSequentialDocument.Objects.Count(item =>
                            item.Enabled && item.Kind == NonSequentialObjectKind.DetectorRectangle)))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter(
                    "SourceNumber",
                    "光源对象（0=全部）",
                    "0",
                    new[] { "0" }.Concat(Enumerable.Range(1, CurrentNonSequentialDocument.Objects.Count(item =>
                            item.Enabled && item.Kind is NonSequentialObjectKind.SourceRay
                                or NonSequentialObjectKind.SourcePoint
                                or NonSequentialObjectKind.SourceRectangle
                                or NonSequentialObjectKind.SourceGaussian))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray())
            },
            "Full Field Spot Diagram" => new[]
            {
                IntParameter("RayDensity", "光线密度", "6", 1, 32),
                ChoiceParameter("Pattern", "样式", "六边", new[] { "六边", "矩形", "随机", "Sobol", "环形" }),
                ChoiceParameter("ColorRaysBy", "颜色显示", "波长", new[] { "波长", "视场" }),
                ChoiceParameter("Reference", "参照", "主光线", new[] { "主光线", "质心" }),
                DoubleParameter("Magnification", "放大", "1", 0, 1_000_000, 0.1),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("ShowAiryDisk", "显示艾里斑", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                ChoiceParameter("DisplayScale", "显示缩放", "比例尺", new[] { "比例尺", "坐标轴" }),
                DoubleParameter("PlotScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("ScatterRays", "散射光线", "false"),
                BoolParameter("UseSymbols", "使用标注", "true")
            },
            "Matrix Spot Diagram" => new[]
            {
                IntParameter("RayDensity", "光线密度", "6", 1, 32),
                ChoiceParameter("Pattern", "样式", "六边", new[] { "六边", "矩形", "随机", "Sobol", "环形" }),
                ChoiceParameter("ColorRaysBy", "颜色显示", "波长", new[] { "波长", "视场" }),
                ChoiceParameter("Reference", "参照", "主光线", new[] { "主光线", "质心" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("DirectionCosines", "方向余弦", "false"),
                BoolParameter("ShowAiryDisk", "显示艾里斑", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                ChoiceParameter("DisplayScale", "显示缩放", "比例尺", new[] { "比例尺", "坐标轴" }),
                DoubleParameter("PlotScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("ScatterRays", "散射光线", "false"),
                BoolParameter("UseSymbols", "使用标注", "true"),
                BoolParameter("IgnoreLateralColor", "忽略垂轴色差", "false")
            },
            "Spot Diagram"
                or "Configuration Matrix Spot Diagram" => new[]
            {
                IntParameter("RayDensity", "光线密度", "6", 1, 32),
                ChoiceParameter("Pattern", "样式", "六边", new[] { "六边", "矩形", "随机", "Sobol", "环形" }),
                ChoiceParameter("ColorRaysBy", "颜色显示", "波长", new[] { "波长", "视场" }),
                ChoiceParameter("Reference", "参照", "主光线", new[] { "主光线", "质心" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("DirectionCosines", "方向余弦", "false"),
                BoolParameter("ShowAiryDisk", "显示艾里斑", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                ChoiceParameter("DisplayScale", "显示缩放", "比例尺", new[] { "比例尺", "坐标轴" }),
                DoubleParameter("PlotScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("ScatterRays", "散射光线", "false"),
                BoolParameter("UseSymbols", "使用标注", "true")
            },
            "Ray Fan" => new[]
            {
                DoubleParameter("PlotScaleMicrometers", "图形缩放 (µm，0 为自动)", "0", 0, 1_000_000, 0.1),
                IntParameter("NumberOfRays", "光线数", "20", 1, 4096),
                BoolParameter("UseDashes", "使用虚线", "false"),
                BoolParameter("VignettedPupil", "渐晕光瞳", "true"),
                BoolParameter("CheckApertures", "检查孔径", "true"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "TangentialAberration",
                    "子午",
                    "Y Aberration",
                    new[] { "Y Aberration", "X Aberration" }),
                ChoiceParameter(
                    "SagittalAberration",
                    "弧矢",
                    "X Aberration",
                    new[] { "X Aberration", "Y Aberration" }),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray())
            },
            "Footprint Diagram" => new[]
            {
                IntParameter("RayDensity", "光线密度", "10", 1, 64),
                IntParameter("SurfaceNumber", "表面序号", "-1", -1, 1024),
                IntParameter("WavelengthNumber", "波长序号（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场序号（0 为全部）", "0", 0, 256),
                BoolParameter("DeleteVignetted", "删除渐晕光线", "false"),
                BoolParameter("UseSymbols", "使用符号区分", "true"),
                ChoiceParameter("ColorRaysBy", "光线着色依据", "视场", new[] { "视场", "波长" })
            },
            "Field Curvature and Distortion" => FieldCurvatureAndDistortionParameters(UsesAngularDistortionModel()),
            "Grid Distortion" => GridDistortionParameters(),
            "Field Curvature" => new[]
            {
                DoubleParameter("MaximumCurvature", "最大场曲（0=自动）", "0", 0, 1_000_000, 0.1),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长（0=所有）",
                    "0",
                    new[] { "0" }.Concat(Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter("ScanDirection", "扫描方向", "+y", new[] { "+y", "+x", "-y", "-x" }),
                DoubleParameter("ParabasalDelta", "近轴光线间隔", "0.00001", 1e-8, 0.1, 0.00001),
                BoolParameter("IgnoreVignettingFactors", "忽略渐晕因数", "true")
            },
            "Color Focus Shift" => new[]
            {
                DoubleParameter("MaximumShift", "最大漂移", "0", 0, 1_000_000, 0.1),
                DoubleParameter("PupilZone", "光瞳", "0", 0, 1, 0.01)
            },
            "Lateral Color" => new[]
            {
                DoubleParameter("GraphScale", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("AllWavelengths", "所有波长", "false"),
                BoolParameter("UseRealRays", "使用实际光线", "true"),
                BoolParameter("ShowAiryDisk", "显示艾里斑", "true")
            },
            "Axial Aberration" => new[]
            {
                DoubleParameter("GraphScale", "图形缩放", "0", 0, 1_000_000, 0.001),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture)))
                        .ToArray()),
                BoolParameter("UseDashes", "使用虚线", "false")
            },
            "Full Field Aberration" => new[]
            {
                ChoiceParameter("FieldShape", "视场形状", "椭圆", new[] { "椭圆", "矩形" }),
                DoubleParameter("XFieldWidth", "X 视场宽度", defaultFieldWidth, 0.000001, 1_000_000, 0.1),
                DoubleParameter("YFieldWidth", "Y 视场宽度", defaultFieldWidth, 0.000001, 1_000_000, 0.1),
                IntParameter("MaximumTerm", "最大项", "37", 4, ZernikeFitEngine.MaximumFringeTerm),
                ChoiceParameter(
                    "Aberration",
                    "像差",
                    "离焦",
                    new[] { "离焦", "像散", "彗差", "球差", "X 倾斜", "Y 倾斜", "RMS 波前" }),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    $"1 - {CurrentOptic.Fields.FirstOrDefault()?.Label ?? "轴上视场"}",
                    CurrentOptic.Fields.Select((field, index) =>
                        $"{index + 1} - {field.Label}").ToArray()),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("XFieldSamples", "X视场采样", "11", new[] { "5", "7", "9", "11", "15", "21", "31" }),
                ChoiceParameter("YFieldSamples", "Y视场采样", "11", new[] { "5", "7", "9", "11", "15", "21", "31" }),
                ChoiceParameter("PupilSampling", "光瞳采样", "32 x 32", new[] { "16 x 16", "32 x 32", "64 x 64" }),
                ChoiceParameter("DisplayAs", "显示为", "图标", new[] { "图标", "颜色图" }),
                ChoiceParameter("DisplayMode", "显示", "绝对值", new[] { "绝对值", "带符号" })
            },
            "Encircled Energy" => new[]
            {
                IntParameter("NumRays", "光线数", "10000", 1, 200000),
                IntParameter("NumPoints", "曲线采样点数", "256", 2, 2048),
                ChoiceParameter("Distribution", "瞳孔采样分布", "sobol", distributionChoices),
                ChoiceParameter("WavelengthNumber", "波长", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Reference", "参照", "centroid", new[] { "chief", "centroid", "vertex" }),
                DoubleParameter("MaximumDistanceMicrometers", "最大距离 (µm)", "0", 0, 1_000_000, 1),
                BoolParameter("MultiplyByDiffractionLimit", "乘以衍射极限", "true")
            },
            "Diffraction Encircled Energy" => new[]
            {
                ChoiceParameter("PupilSampling", "\u77b3\u9762\u91c7\u6837", "64 x 64",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter("ImageSampling", "\u50cf\u9762\u91c7\u6837", "128 x 128",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256", "512 x 512" }),
                IntParameter("NumPoints", "\u66f2\u7ebf\u91c7\u6837\u70b9\u6570", "401", 2, 2048),
                ChoiceParameter("WavelengthNumber", "\u6ce2\u957f", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("FieldNumber", "\u89c6\u573a", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Fields.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Type", "\u7c7b\u578b", "encircled",
                    new[] { "encircled", "X only", "Y only", "ensquared" }),
                ChoiceParameter("Reference", "\u53c2\u7167", "centroid",
                    new[] { "chief", "centroid", "vertex" }),
                DoubleParameter("MaximumDistanceMicrometers", "\u6700\u5927\u8ddd\u79bb (\u00b5m)",
                    "0", 0, 1_000_000, 1)
            },
            "Geometric Line Edge Spread" => new[]
            {
                ChoiceParameter("PupilSampling", "\u77b3\u9762\u91c7\u6837", "32 x 32",
                    new[] { "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                IntParameter("NumPoints", "\u66f2\u7ebf\u91c7\u6837\u70b9\u6570", "257", 33, 2049),
                ChoiceParameter("WavelengthNumber", "\u6ce2\u957f", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("FieldNumber", "\u89c6\u573a", "1",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Orientation", "\u65b9\u5411", "X", new[] { "X", "Y" }),
                ChoiceParameter("Display", "\u663e\u793a", "line and edge",
                    new[] { "line and edge", "line", "edge" }),
                DoubleParameter("MaximumRadiusMicrometers", "\u6700\u5927\u534a\u5f84 (\u00b5m)",
                    "0", 0, 1_000_000, 1)
            },
            "Extended Source Encircled Energy" => new[]
            {
                FileParameter("SourceFile", "源 IMA 文件"),
                DoubleParameter("FieldSize", "\u89c6\u573a\u5c3a\u5bf8", "0", 0, 1_000_000, 0.1),
                IntParameter("SourceSampling", "\u5149\u6e90\u91c7\u6837", "5", 1, 21),
                IntParameter("NumRays", "\u5149\u7ebf\u6570", "5000", 100, 2_000_000),
                IntParameter("NumPoints", "\u66f2\u7ebf\u91c7\u6837\u70b9\u6570", "256", 2, 2048),
                ChoiceParameter("WavelengthNumber", "\u6ce2\u957f", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("FieldNumber", "\u89c6\u573a", "1",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Type", "\u7c7b\u578b", "encircled",
                    new[] { "encircled", "X only", "Y only", "ensquared" }),
                ChoiceParameter("Reference", "\u53c2\u7167", "centroid",
                    new[] { "chief", "centroid", "vertex" }),
                DoubleParameter("MaximumDistanceMicrometers", "\u6700\u5927\u8ddd\u79bb (\u00b5m)",
                    "0", 0, 1_000_000, 1)
            },
            "Pupil Aberration" => new[]
            {
                IntParameter("NumberOfRays", "原点每侧光线数", "20", 1, 4096)
            },
            "RMS vs Field" => new[]
            {
                IntParameter("FieldDensity", "视场间隔数", "15", 1, 200),
                ChoiceParameter("ScanDirection", "扫描方向", "+y", new[] { "+y", "-y", "+x", "-x" }),
                IntParameter("NumRings", "六角采样环数", "6", 1, 32),
                ChoiceParameter("Method", "计算方法", "GQ", new[] { "GQ", "RA" }),
                ChoiceParameter("Data", "数据", "wavefront", new[] { "spot", "wavefront" }),
                ChoiceParameter("Distribution", "瞳孔采样分布", "hexapolar", distributionChoices),
                ChoiceParameter(
                    "WavelengthNumber",
                    "\u6ce2\u957f",
                    "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Reference", "\u53c2\u7167", "chief", new[] { "centroid", "chief" }),
                BoolParameter("ShowDiffractionLimit", "显示衍射极限", "false"),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("RemoveVignetting", "移除渐晕", "true")
            },
            "RMS vs Wavelength" => new[]
            {
                IntParameter("WaveDensity", "\u6ce2\u957f\u5bc6\u5ea6", "21", 2, 100),
                IntParameter("NumRings", "\u5149\u7ebf\u5bc6\u5ea6", "6", 1, 32),
                ChoiceParameter("Method", "计算方法", "GQ", new[] { "GQ", "RA" }),
                ChoiceParameter("Data", "数据", "spot", new[] { "spot", "wavefront" }),
                ChoiceParameter("Distribution", "\u91c7\u6837\u65b9\u6cd5", "hexapolar", distributionChoices),
                ChoiceParameter(
                    "FieldNumber",
                    "\u89c6\u573a",
                    "0",
                    new[] { "0" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter("Reference", "\u53c2\u7167", "centroid", new[] { "centroid", "chief" }),
                BoolParameter("ShowDiffractionLimit", "显示衍射极限", "false"),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("RemoveVignetting", "移除渐晕", "true")
            },
            "RMS vs Focus" => new[]
            {
                IntParameter("FocusDensity", "\u79bb\u7126\u5bc6\u5ea6", "16", 2, 100),
                DoubleParameter("MinimumFocus", "\u6700\u5c0f\u79bb\u7126", "-0.01", -1_000_000, 1_000_000, 0.001),
                DoubleParameter("MaximumFocus", "\u6700\u5927\u79bb\u7126", "0.01", -1_000_000, 1_000_000, 0.001),
                IntParameter("NumRings", "\u5149\u7ebf\u5bc6\u5ea6", "6", 1, 32),
                ChoiceParameter("Method", "计算方法", "GQ", new[] { "GQ", "RA" }),
                ChoiceParameter("Data", "数据", "wavefront", new[] { "spot", "wavefront" }),
                ChoiceParameter("Distribution", "\u91c7\u6837\u65b9\u6cd5", "hexapolar", distributionChoices),
                ChoiceParameter(
                    "WavelengthNumber",
                    "\u6ce2\u957f",
                    "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Reference", "\u53c2\u7167", "chief", new[] { "centroid", "chief" }),
                BoolParameter("ShowDiffractionLimit", "显示衍射极限", "false"),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("RemoveVignetting", "移除渐晕", "true")
            },
            "RMS Field Map" => new[]
            {
                IntParameter("XFieldSamples", "X\u89c6\u573a\u91c7\u6837", "11", 3, 101),
                IntParameter("YFieldSamples", "Y\u89c6\u573a\u91c7\u6837", "11", 3, 101),
                DoubleParameter("XFieldWidth", "X\u89c6\u573a\u5927\u5c0f", "0", 0, 1_000_000, 0.1),
                DoubleParameter("YFieldWidth", "Y\u89c6\u573a\u5927\u5c0f", "0", 0, 1_000_000, 0.1),
                IntParameter("NumRings", "\u5149\u7ebf\u5bc6\u5ea6", "6", 1, 32),
                ChoiceParameter("Method", "计算方法", "GQ", new[] { "GQ", "RA" }),
                ChoiceParameter("Data", "数据", "spot", new[] { "spot", "wavefront" }),
                ChoiceParameter("Distribution", "\u91c7\u6837\u65b9\u6cd5", "hexapolar", distributionChoices),
                ChoiceParameter(
                    "WavelengthNumber",
                    "\u6ce2\u957f",
                    "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("Reference", "\u53c2\u7167", "centroid", new[] { "centroid", "chief" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("RemoveVignetting", "移除渐晕", "true")
            },
            "RMS Wavefront vs Field" => new[]
            {
                IntParameter("RayDensity", "光线密度", "6", 1, 32),
                IntParameter("FieldDensity", "视场密度", "15", 1, 200),
                ChoiceParameter("Method", "计算方法", "GQ", new[] { "GQ", "RA" }),
                ChoiceParameter("Reference", "参照", "chief", new[] { "chief", "centroid" }),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长（0 为全部）",
                    "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("ScanType", "视场方向", "+y", new[] { "+y", "+x", "-y", "-x" }),
                BoolParameter("RemoveVignettingFactors", "移除渐晕因子", "true")
            },
            "Zernike vs Field" => new[]
            {
                IntParameter("FieldDensity", "视场密度", "20", 2, 200),
                IntParameter("NumRings", "六角采样环数", "12", 2, 32),
                IntParameter("ZernikeTerms", "Zernike 系数项数", "8", 1, 64),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray())
            },
            "Through Focus" => new[]
            {
                IntParameter("RayDensity", "光线密度", "6", 1, 32),
                ChoiceParameter("Pattern", "样式", "六边", new[] { "六边", "矩形", "随机", "Sobol", "环形" }),
                ChoiceParameter("ColorRaysBy", "颜色显示", "波长", new[] { "波长", "视场" }),
                ChoiceParameter("Reference", "参照", "主光线", new[] { "主光线", "质心" }),
                DoubleParameter("DefocusStepMicrometers", "离焦范围", "50", 0, 1_000_000, 1),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("ShowAiryDisk", "显示艾里斑", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                ChoiceParameter("DisplayScale", "显示缩放", "比例尺", new[] { "比例尺", "坐标轴" }),
                DoubleParameter("PlotScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("ScatterRays", "散射光线", "false"),
                BoolParameter("UseSymbols", "使用标注", "true")
            },
            "Through Focus MTF" => new[]
            {
                ChoiceParameter("Sampling", "采样", "64", new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" }),
                DoubleParameter("DeltaFocus", "离焦范围 (±mm)", "0.1", 0, 10, 0.01),
                DoubleParameter("Frequency", "频率 (cycles/mm)", "0", 0, 10000, 1),
                IntParameter("NumberOfSteps", "步长数", "5", 1, 101),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256),
                ChoiceParameter("Type", "类型", "调制", new[] { "调制", "实部", "虚部", "相位", "方波" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseDashes", "使用虚线", "false")
            },
            "Fourier Through Focus MTF" => new[]
            {
                ChoiceParameter("Sampling", "采样", "64", new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" }),
                DoubleParameter("DeltaFocus", "离焦范围 (±mm)", "0.1", 0, 10, 0.01),
                DoubleParameter("Frequency", "频率 (cycles/mm)", "0", 0, 10000, 1),
                IntParameter("NumberOfSteps", "步长数", "5", 1, 101),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256),
                ChoiceParameter("Type", "类型", "调制", new[] { "调制", "实部", "虚部", "相位", "方波" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseDashes", "使用虚线", "false")
            },
            "Huygens Through Focus MTF" => new[]
            {
                ChoiceParameter("PupilSampling", "瞳面采样", "64", new[] { "32", "64", "128", "256", "512" }),
                ChoiceParameter("ImageSampling", "图像采样", "32", new[] { "32", "64", "128", "256", "512" }),
                DoubleParameter("ImageDeltaMicrometers", "图像间隔 (µm，0 为自动)", "0", 0, 1000, 0.1),
                DoubleParameter("DeltaFocus", "离焦范围 (±mm)", "0.1", 0, 10, 0.01),
                DoubleParameter("SpatialFrequency", "空间频率 (cycles/mm)", "20", 0, 10000, 1),
                IntParameter("Steps", "步长数", "5", 1, 31),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256)
            },
            "Geometric Through Focus MTF" => new[]
            {
                ChoiceParameter("Sampling", "采样", "64", new[] { "32", "64", "128", "256", "512" }),
                DoubleParameter("DeltaFocus", "离焦范围 (±mm)", "0.1", 0, 10, 0.01),
                DoubleParameter("SpatialFrequency", "空间频率 (cycles/mm)", "50", 0, 10000, 1),
                IntParameter("Steps", "步长数", "5", 1, 31),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256),
                ChoiceParameter("Distribution", "几何光线采样分布", "uniform", distributionChoices),
                BoolParameter("ScaleByDiffractionLimit", "几何结果乘以衍射极限包络", "true")
            },
            "Huygens MTF vs Field" => new[]
            {
                ChoiceParameter("Sampling", "瞳面采样", "64", new[] { "32", "64", "128", "256", "512" }),
                DoubleParameter("Frequency1", "空间频率 1 (cycles/mm)", "10", 0, 10000, 1),
                DoubleParameter("Frequency2", "空间频率 2 (cycles/mm)", "20", 0, 10000, 1),
                DoubleParameter("Frequency3", "空间频率 3 (cycles/mm)", "30", 0, 10000, 1),
                DoubleParameter("Frequency4", "空间频率 4 (cycles/mm)", "40", 0, 10000, 1),
                DoubleParameter("Frequency5", "空间频率 5 (cycles/mm)", "50", 0, 10000, 1),
                DoubleParameter("Frequency6", "空间频率 6 (cycles/mm)", "60", 0, 10000, 1),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseDashes", "使用虚线", "false"),
                BoolParameter("RemoveVignettingFactors", "移除渐晕因子", "true"),
                IntParameter("FieldDensity", "视场密度", "10", 2, 100),
                ChoiceParameter("ScanType", "扫描方向", "+y", new[] { "+y", "+x", "-y", "-x" })
            },
            "Fourier MTF vs Field" or "Geometric MTF vs Field" => new[]
            {
                DoubleParameter("SpatialFrequency", "空间频率 (cycles/mm)", "20", 0, 10000, 1),
                IntParameter("PupilSampling", "瞳面/光线采样数", "32", 2, 512),
                IntParameter("ImageSize", "计算网格尺寸", "64", 4, 2048),
                DoubleParameter("PixelPitchMillimeters", "惠更斯像素间距 (mm)", "0.005", 1e-6, 10, 0.001),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                ChoiceParameter("Distribution", "几何光线采样分布", "uniform", distributionChoices),
                BoolParameter("ScaleByDiffractionLimit", "几何结果乘以衍射极限包络", "true")
            },
            "Angle vs Image Height" => new[]
            {
                IntParameter("FieldDensity", "视场密度", "20", 2, 200),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    Math.Max(
                            1,
                            CurrentOptic.Wavelengths
                                .Select((wavelength, index) => (wavelength, index))
                                .FirstOrDefault(item => item.wavelength.IsPrimary)
                                .index + 1)
                        .ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray())
            },
            "Angle vs Image Height - Through Pupil" => new[]
            {
                IntParameter("SurfaceIndex", "测量表面序号", "-1", -128, 128),
                ChoiceParameter("Axis", "测量轴", "Y", new[] { "Y", "X" }),
                IntParameter("NumPoints", "采样点数", "128", 2, 1024)
            },
            "Angle vs Image Height - Through Field" => new[]
            {
                IntParameter("SurfaceIndex", "测量表面序号", "-1", -128, 128),
                ChoiceParameter("Axis", "测量轴", "Y", new[] { "Y", "X" })
            },
            "Cardinal Points Data" => new[]
            {
                ChoiceParameter(
                    "ReferenceSurfaceNumber",
                    "参考面",
                    (CurrentOptic.SurfaceGroup.Items.LastOrDefault()?.Number ?? 0)
                        .ToString(CultureInfo.InvariantCulture),
                    CurrentOptic.SurfaceGroup.Items
                        .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray())
            },
            "Relative Illumination" => new[]
            {
                IntParameter("RayDensity", "光线密度", "10", 5, 128),
                IntParameter("FieldDensity", "视场密度", "21", 2, 201),
                IntParameter("WavelengthNumber", "波长序号（0=主波长）", "0", 0, 128),
                ChoiceParameter(
                    "ScanDirection",
                    "扫描方向",
                    "+y",
                    new[] { "+y", "+x", "-y", "-x" }),
                BoolParameter("RemoveVignettingFactors", "移除渐晕因子", "true")
            },
            "Incoherent Irradiance" => new[]
            {
                IntParameter("NumRays", "光线数", "5", 1, 100000),
                IntParameter("ResolutionX", "X 分辨率", "128", 1, 1024),
                IntParameter("ResolutionY", "Y 分辨率", "128", 1, 1024),
                IntParameter("DetectorSurfaceIndex", "探测器表面序号", "-1", -128, 128),
                ChoiceParameter("Distribution", "瞳孔采样分布", "random", distributionChoices),
                BoolParameter("Normalized", "归一化显示", "true")
            },
            "Radiant Intensity" => new[]
            {
                IntParameter("AngularBinsX", "X 角度分箱", "101", 1, 1024),
                IntParameter("AngularBinsY", "Y 角度分箱", "101", 1, 1024),
                IntParameter("NumRays", "光线数", "2048", 1, 200000),
                IntParameter("ReferenceSurfaceIndex", "参考表面序号", "-1", -128, 128),
                ChoiceParameter("Distribution", "瞳孔采样分布", "random", distributionChoices),
                BoolParameter("UseAbsoluteUnits", "使用绝对单位", "true")
            },
            "PSF" => new[]
            {
                ChoiceParameter(
                    "Sampling",
                    "采样",
                    "64 x 64",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter(
                    "Display",
                    "显示",
                    "128 x 128",
                    new[] { "64 x 64", "128 x 128", "256 x 256", "512 x 512" }),
                ChoiceParameter("Rotation", "旋转", "0", new[] { "0", "90", "180", "270" }),
                DoubleParameter(
                    "ImageDeltaMicrometers",
                    "像面采样间距",
                    "0",
                    0,
                    1_000_000,
                    0.1),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        CurrentOptic.Wavelengths.Select((wavelength, index) =>
                            $"{index + 1} - {wavelength.Micrometers:0.0000} µm"))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                ChoiceParameter("Type", "类型", "线性", new[] { "线性", "对数" }),
                ChoiceParameter(
                    "DisplayAs",
                    "显示为",
                    "伪彩色",
                    new[] { "伪彩色", "等高线", "表面" }),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                BoolParameter("Normalized", "归一化", "false")
            },
            "FFT PSF Cross Section" => new[]
            {
                ChoiceParameter(
                    "Sampling",
                    "采样",
                    "64 x 64",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter("Row", "行", "中心", new[] { "中心" }),
                DoubleParameter("GraphScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        CurrentOptic.Wavelengths.Select((wavelength, index) =>
                            $"{index + 1} - {wavelength.Micrometers:0.0000} µm"))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                ChoiceParameter(
                    "Type",
                    "类型",
                    "X-线性",
                    new[] { "X-线性", "Y-线性", "X-对数", "Y-对数" }),
                BoolParameter("Normalized", "归一化", "false")
            },
            "FFT Line Edge Spread" => new[]
            {
                ChoiceParameter(
                    "Sampling",
                    "采样",
                    "64 x 64",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter("Spread", "扩散", "线", new[] { "线", "边缘" }),
                DoubleParameter("GraphScaleMicrometers", "图形缩放", "0", 0, 1_000_000, 0.1),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        CurrentOptic.Wavelengths.Select((wavelength, index) =>
                            $"{index + 1} - {wavelength.Micrometers:0.0000} µm"))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                ChoiceParameter(
                    "Type",
                    "类型",
                    "X-线性",
                    new[] { "X-线性", "Y-线性", "X-对数", "Y-对数" }),
                BoolParameter("UseCoherentPsf", "使用相干 PSF", "false")
            },
            "MTF" => new[]
            {
                ChoiceParameter("Sampling", "采样", "64", fftSamplingChoices),
                DoubleParameter("MaximumFrequency", "最大频率 (cycles/mm，0 为默认)", "0", 0, 10000, 10),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256),
                IntParameter("SurfaceNumber", "表面（0 为像面）", "0", 0, 1024),
                ChoiceParameter("Type", "类型", "调制", new[] { "调制", "实部", "虚部", "相位", "方波" }),
                BoolParameter("ShowDiffractionLimit", "显示衍射极限", "false"),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseDashes", "使用虚线", "false")
            },
            "Huygens PSF" => new[]
            {
                ChoiceParameter(
                    "PupilSampling",
                    "光瞳采样",
                    "32 x 32",
                    new[] { "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                ChoiceParameter(
                    "ImageSampling",
                    "像面采样",
                    "32 x 32",
                    new[] { "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                DoubleParameter(
                    "ImageDeltaMicrometers",
                    "像面采样间距",
                    "0",
                    0,
                    1_000_000,
                    0.1),
                ChoiceParameter("Rotation", "旋转", "0", new[] { "0", "90", "180", "270" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseCentroid", "使用质心", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        CurrentOptic.Wavelengths.Select((wavelength, index) =>
                            $"{index + 1} - {wavelength.Micrometers:0.0000} µm"))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                ChoiceParameter("Type", "类型", "线性", new[] { "线性", "对数" }),
                ChoiceParameter(
                    "DisplayAs",
                    "显示为",
                    "伪彩色",
                    new[] { "伪彩色", "等高线", "表面" }),
                BoolParameter("Normalized", "归一化", "false")
            },
            "Huygens PSF Cross Section" => new[]
            {
                ChoiceParameter("PupilSampling", "光瞳采样", "32", new[] { "16", "32", "64", "128" }),
                ChoiceParameter("ImageSampling", "像面采样", "32", new[] { "16", "32", "64", "128" }),
                DoubleParameter("ImageDeltaMicrometers", "像面采样间距 (µm，0 为自动)", "0", 0, 1000, 0.1),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场", "1", 1, 256),
                ChoiceParameter("ProfileType", "截面", "X", new[] { "X", "Y", "Both" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                BoolParameter("UseCentroid", "使用质心", "false")
            },
            "Huygens MTF" => new[]
            {
                ChoiceParameter("PupilSampling", "瞳面采样", "32", new[] { "32", "64", "128", "256", "512" }),
                ChoiceParameter("ImageSampling", "图像采样", "32", new[] { "32", "64", "128", "256", "512" }),
                DoubleParameter("ImageDeltaMicrometers", "图像间隔 (µm，0 为自动)", "0", 0, 1000, 0.1),
                DoubleParameter("MaximumFrequency", "最大频率 (cycles/mm，0=自动)", "0", 0, 10000, 10),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256)
            },
            "Geometric MTF" => new[]
            {
                IntParameter("NumRays", "光线数", "32", 2, 10000),
                IntParameter("PlotPointCount", "曲线采样点数", "128", 2, 2048),
                ChoiceParameter("Distribution", "瞳孔采样分布", "uniform", distributionChoices),
                DoubleParameter("MaximumFrequency", "最大频率（0=截止）", "0", 0, 10000, 10),
                IntParameter("WavelengthNumber", "波长（0 为全部）", "0", 0, 256),
                IntParameter("FieldNumber", "视场（0 为全部）", "0", 0, 256),
                BoolParameter("ScaleByDiffractionLimit", "乘以衍射极限包络", "true")
            },
            "Sampled MTF" => new[]
            {
                IntParameter("PupilSampling", "瞳面采样数", "32", 8, 512),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                IntParameter("PlotPointCount", "曲线采样点数", "128", 2, 2048),
                DoubleParameter("MaximumFrequency", "最大频率（0=截止）", "0", 0, 10000, 10)
            },
            "Contrast Loss Map" => new[]
            {
                ChoiceParameter("Sampling", "采样", "13", new[] { "13", "17", "25", "33", "49", "65" }),
                DoubleParameter("Frequency", "频率 (cycles/mm，0=5%截止)", "100", 0, 10000, 1),
                BoolParameter("Normalize", "归一化", "false"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                BoolParameter("ShowOPD", "显示 OPD", "false")
            },
            "Optical Path Difference" => new[]
            {
                DoubleParameter("GraphScale", "图形缩放", "0", 0, 1_000_000, 0.1),
                IntParameter("NumberOfRays", "光线数", "20", 1, 4096),
                BoolParameter("UseDashes", "使用虚线", "false"),
                BoolParameter("VignettedPupil", "渐晕光瞳", "true"),
                BoolParameter("CheckApertures", "检查孔径", "true"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "所有",
                    new[] { "所有" }.Concat(
                        Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                            .Select(index => index.ToString(CultureInfo.InvariantCulture))).ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray())
            },
            "Wavefront Map" or "Wavefront" => new[]
            {
                ChoiceParameter(
                    "Sampling",
                    "采样",
                    "64 x 64",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter("Rotation", "旋转", "0", new[] { "0", "90", "180", "270" }),
                DoubleParameter("DisplayScale", "缩放", "1", 0.01, 1_000, 0.1),
                ChoiceParameter("Apodization", "偏振", "无", new[] { "无", "高斯" }),
                BoolParameter("ReferenceChiefRay", "参考主光线", "false"),
                BoolParameter("UseExitPupilShape", "使用出瞳形状", "true"),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                ChoiceParameter(
                    "SurfaceNumber",
                    "表面",
                    "像面",
                    new[] { "像面" }.Concat(
                        CurrentOptic.SurfaceGroup.Items
                            .Where(surface => surface.Number > 0)
                            .Select(surface => surface.Number.ToString(CultureInfo.InvariantCulture)))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()),
                ChoiceParameter("DisplayAs", "显示为", "表面", new[] { "表面", "等高线" }),
                BoolParameter("RemoveTilt", "除去倾斜", "false"),
                DoubleParameter("PupilSx", "Sx", "0", -10, 10, 0.01),
                DoubleParameter("PupilSy", "Sy", "0", -10, 10, 0.01),
                DoubleParameter("PupilSr", "Sr", "1", 0.000001, 10, 0.01)
            },
            "Foucault Analysis" => new[]
            {
                ChoiceParameter(
                    "Sampling",
                    "采样",
                    "32 x 32",
                    new[] { "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                ChoiceParameter("Type", "类型", "线性", new[] { "线性", "二次" }),
                ChoiceParameter("DisplayAs", "显示为", "灰度", new[] { "灰度", "伪彩色" }),
                ChoiceParameter(
                    "KnifeEdge",
                    "刀口",
                    "水平线上",
                    new[] { "水平线上", "水平线下", "垂直线左", "垂直线右" }),
                ChoiceParameter("DataSource", "数据", "计算的", new[] { "计算的" }),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray()),
                DoubleParameter("YPositionMicrometers", "Y位置：µm", "0", -1_000_000, 1_000_000, 0.1),
                BoolParameter("UsePolarization", "使用偏振", "false")
            },
            "Interferogram" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257)
            },
            "Centroid Sphere Wavefront" or "Best Fit Sphere Wavefront" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "8", 2, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257),
                DoubleParameter("RobustTrimStandardDeviations", "鲁棒裁剪 sigma", "3", 0, 10, 0.5),
                ChoiceParameter("WavelengthNumber", "波长", "0",
                    Enumerable.Range(0, Math.Max(1, CurrentOptic.Wavelengths.Count) + 1)
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()),
                ChoiceParameter("FieldNumber", "视场", "1",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray())
            },
            "Zernike Fringe" => new[]
            {
                ChoiceParameter(
                    "PupilSampling",
                    "\u77b3\u9762\u91c7\u6837",
                    "32 x 32",
                    new[] { "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                IntParameter("ZernikeTerms", "Zernike \u62df\u5408\u9879\u6570", "37", 1, 37),
                ChoiceParameter(
                    "WavelengthNumber",
                    "\u6ce2\u957f",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "\u89c6\u573a",
                    "1 - \u8f74\u4e0a\u89c6\u573a",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - \u8f74\u4e0a\u89c6\u573a"
                            : $"{index} - \u89c6\u573a {index}")
                        .ToArray())
            },
            "Zernike Standard" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray())
            },
            "Zernike Annular" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                DoubleParameter("ObscurationRatio", "遮光", "0.5", 0, 0.95, 0.01),
                ChoiceParameter(
                    "WavelengthNumber",
                    "波长",
                    primaryWavelengthNumber.ToString(CultureInfo.InvariantCulture),
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Wavelengths.Count))
                        .Select(index => index.ToString(CultureInfo.InvariantCulture))
                        .ToArray()),
                ChoiceParameter(
                    "FieldNumber",
                    "视场",
                    "1 - 轴上视场",
                    Enumerable.Range(1, Math.Max(1, CurrentOptic.Fields.Count))
                        .Select(index => index == 1
                            ? "1 - 轴上视场"
                            : $"{index} - 视场 {index}")
                        .ToArray())
            },
            "Zernike" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257)
            },
            "Geometric Image Analysis" => new[]
            {
                ChoiceParameter("SourceImage", "输入图像", "分辨率靶标",
                    new[] { "彩色测试卡", "分辨率靶标", "畸变网格", "西门子星" }),
                ChoiceParameter("ImageSize", "图像尺寸", "64",
                    new[] { "32", "64", "128", "256" }),
                IntParameter("NumRays", "每点光线数", "8", 2, 128),
                DoubleParameter("FieldHeight", "视场高度", "0", 0, 1_000_000, 0.1),
                IntParameter("Oversampling", "过采样", "1", 1, 16),
                IntParameter("GuardBand", "保护带", "4", 0, 512),
                BoolParameter("RelativeIllumination", "相对照度", "true"),
                ChoiceParameter("AberrationMode", "像差模式", "Geometric",
                    new[] { "Geometric", "None" })
            },
            "Geometric Bitmap Image Analysis" => new[]
            {
                ChoiceParameter("ImageSize", "图像尺寸", "64",
                    new[] { "32", "64", "128", "256" }),
                IntParameter("RaysPerPixel", "每像素光线数", "8", 2, 128),
                DoubleParameter("FieldHeight", "视场高度", "0", 0, 1_000_000, 0.1),
                IntParameter("Oversampling", "过采样", "1", 1, 16),
                IntParameter("GuardBand", "保护带", "4", 0, 512),
                BoolParameter("RelativeIllumination", "相对照度", "true"),
                ChoiceParameter("AberrationMode", "像差模式", "Geometric",
                    new[] { "Geometric", "None" })
            },
            "Light Source Analysis" => new[]
            {
                ChoiceParameter("Resolution", "采样分辨率", "65",
                    new[] { "33", "65", "129", "257" }),
                IntParameter("NumRays", "光线数", "2048", 32, 200000)
            },
            "Partially Coherent Image Analysis" => new[]
            {
                ChoiceParameter("ImageSize", "图像尺寸", "64",
                    new[] { "32", "64", "128", "256" }),
                ChoiceParameter("PupilSampling", "瞳面采样", "16 x 16",
                    new[] { "8 x 8", "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                DoubleParameter("Coherence", "相干度", "0.5", 0, 1, 0.05),
                DoubleParameter("FieldHeight", "视场高度", "0", 0, 1_000_000, 0.1),
                IntParameter("Oversampling", "过采样", "1", 1, 16),
                IntParameter("GuardBand", "保护带", "16", 0, 512),
                BoolParameter("RelativeIllumination", "相对照度", "true")
            },
            "Extended Diffraction Image Analysis" => new[]
            {
                ChoiceParameter("SourceImage", "输入图像", "分辨率靶标",
                    new[] { "彩色测试卡", "分辨率靶标", "畸变网格", "西门子星" }),
                ChoiceParameter("ImageSize", "图像尺寸", "64",
                    new[] { "32", "64", "128", "256" }),
                ChoiceParameter("PupilSampling", "瞳面采样", "16 x 16",
                    new[] { "8 x 8", "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                ChoiceParameter("FieldGrid", "视场 PSF 网格", "5",
                    new[] { "3", "5", "7", "9" }),
                DoubleParameter("FieldHeight", "视场高度", "0", 0, 1_000_000, 0.1),
                IntParameter("Oversampling", "过采样", "1", 1, 16),
                IntParameter("GuardBand", "保护带", "16", 0, 512),
                BoolParameter("RelativeIllumination", "相对照度", "true"),
                ChoiceParameter("AberrationMode", "像差模式", "Diffraction",
                    new[] { "Diffraction", "Geometric", "None" })
            },
            "Image Simulation" => new[]
            {
                ChoiceParameter(
                    "SourceImage",
                    "输入图像",
                    "彩色测试卡",
                    new[] { "彩色测试卡", "分辨率靶标", "畸变网格", "西门子星" }),
                ChoiceParameter("SourceMode", "源类型", "内置图像", new[] { "内置图像", "外部位图" }),
                FileParameter("SourceFile", "导入文件"),
                IntParameter("ImageWidth", "图像宽度", "64", 16, 2048),
                IntParameter("ImageHeight", "图像高度", "48", 16, 2048),
                DoubleParameter("FieldHeight", "视场高度", defaultFieldWidth, 0, 1_000_000, 0.1),
                ChoiceParameter("Oversampling", "过采样", "无", new[] { "无", "2 x", "4 x", "8 x", "16 x" }),
                ChoiceParameter("SourceFlip", "翻转位图", "无", new[] { "无", "水平", "垂直", "水平和垂直" }),
                ChoiceParameter("GuardBand", "安全宽度", "无", new[] { "无", "4", "8", "16", "32", "64" }),
                ChoiceParameter("SourceRotation", "旋转位图", "无", new[] { "无", "90°", "180°", "270°" }),
                ChoiceParameter("WavelengthNumber", "波长", "RGB", imageSimulationWavelengthChoices),
                ChoiceParameter("FieldNumber", "视场", imageSimulationFieldChoices[0], imageSimulationFieldChoices),
                ChoiceParameter("NumRays", "光瞳采样", "32 x 32",
                    new[] { "8 x 8", "16 x 16", "32 x 32", "64 x 64", "128 x 128" }),
                ChoiceParameter("PsfSize", "像面采样", "32 x 32",
                    new[] { "8 x 8", "16 x 16", "32 x 32", "64 x 64", "128 x 128", "256 x 256" }),
                ChoiceParameter("PsfGridColumns", "PSF-X点数", "3", new[] { "1", "3", "5", "7", "9", "11", "13", "15" }),
                ChoiceParameter("PsfGridRows", "PSF-Y点数", "3", new[] { "1", "3", "5", "7", "9", "11", "13", "15" }),
                BoolParameter("UsePolarization", "使用偏振", "false"),
                ChoiceParameter("AberrationMode", "像差", "几何的", new[] { "衍射", "几何的", "无" }),
                BoolParameter("ApplyFixedApertures", "应用固定孔径", "true"),
                BoolParameter("RelativeIllumination", "使用相对照度", "true"),
                ChoiceParameter("DisplayAs", "显示为", "仿真图", new[] { "仿真图", "源位图" }),
                ChoiceParameter("Reference", "参考", "主光线", new[] { "主光线", "质心" }),
                ChoiceParameter("ImageFlip", "翻转图像", "无", new[] { "无", "水平", "垂直", "水平和垂直" }),
                DoubleParameter("PixelSize", "像素大小", "0", 0, 1_000_000, 0.001),
                IntParameter("DetectorXPixels", "X 像素", "0", 0, 16_000),
                IntParameter("DetectorYPixels", "Y 像素", "0", 0, 16_000),
                BoolParameter("CompressFrame", "压缩框架", "false"),
                FileParameter("OutputFile", "输出文件"),
                IntParameter("EigenPsfComponents", "EigenPSF 分量数", "3", 1, 12),
                IntParameter("DistortionGridSize", "畸变采样网格", "9", 3, 33),
                IntParameter("DistortionPolynomialDegree", "畸变拟合阶数", "5", 1, 9)
            },
            "Jones Pupil" => new[] { IntParameter("GridSize", "网格尺寸", "65", 3, 257) },
            _ => Array.Empty<AnalysisParameterDescriptor>()
        };
    }
}
