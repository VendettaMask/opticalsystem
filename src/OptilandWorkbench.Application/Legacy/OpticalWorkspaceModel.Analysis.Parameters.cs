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
public IReadOnlyList<AnalysisParameterDescriptor> GetAnalysisParameters(string analysisName)
    {
        var distributionChoices = new[] { "hexapolar", "uniform", "sobol", "random", "line_x", "line_y", "ring" };
        var primaryWavelengthNumber = Math.Max(
            1,
            CurrentOptic.Wavelengths.ToList().FindIndex(wavelength => wavelength.IsPrimary) + 1);
        var defaultFieldWidth = FieldCoordinates.MaximumRadius(CurrentOptic.Fields)
            .ToString("0.######", CultureInfo.InvariantCulture);
        var fftSamplingChoices = new[]
        {
            "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384"
        };
        return CanonicalAnalysisName(analysisName) switch
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
                ChoiceParameter("ColorRaysBy", "光线着色依据", "field", new[] { "field", "wavelength" })
            },
            "Distortion" => DistortionParameters(UsesAngularDistortionModel()),
            "Grid Distortion" => GridDistortionParameters(),
            "Field Curvature" => new[]
            {
                DoubleParameter("ParabasalDelta", "近轴光线间隔", "0.00001", 1e-8, 0.1, 0.00001)
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
                ChoiceParameter("Decomposition", "分解", "Zernike项", new[] { "Zernike项" }),
                IntParameter("MaximumTerm", "最大项", "37", 4, 256),
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
                ChoiceParameter("Distribution", "瞳孔采样分布", "sobol", distributionChoices)
            },
            "Pupil Aberration" => new[] { IntParameter("NumPoints", "采样点数", "256", 3, 1024) },
            "RMS vs Field" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "6", 1, 32),
                ChoiceParameter("Distribution", "瞳孔采样分布", "hexapolar", distributionChoices)
            },
            "RMS Wavefront vs Field" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "12", 1, 32)
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
                ChoiceParameter("ImageSampling", "图像采样", "64", new[] { "32", "64", "128", "256", "512" }),
                DoubleParameter("ImageDeltaMicrometers", "图像间隔 (µm，0 为自动)", "0", 0, 1000, 0.1),
                DoubleParameter("DeltaFocus", "离焦范围 (±mm)", "0.1", 0, 10, 0.01),
                DoubleParameter("SpatialFrequency", "空间频率 (cycles/mm)", "50", 0, 10000, 1),
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
            "Fourier MTF vs Field" or "Huygens MTF vs Field" or "Geometric MTF vs Field" => new[]
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
                IntParameter("WavelengthNumber", "波长序号（0=主波长）", "0", 0, 128),
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
                ChoiceParameter("DisplayAs", "显示为", "表面", new[] { "表面", "等高线" }),
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
                ChoiceParameter("DisplayAs", "显示为", "表面", new[] { "表面", "等高线" }),
                BoolParameter("Normalized", "归一化", "false")
            },
            "Huygens PSF Cross Section" => new[]
            {
                IntParameter("NumRays", "光线数", "9", 2, 128),
                IntParameter("ImageSize", "图像尺寸", "32", 1, 256),
                DoubleParameter("PixelPitchMillimeters", "像素间距 (mm)", "0.005", 1e-6, 10, 0.001)
            },
            "Huygens MTF" => new[]
            {
                ChoiceParameter("PupilSampling", "瞳面采样", "64", new[] { "32", "64", "128", "256", "512" }),
                ChoiceParameter("ImageSampling", "图像采样", "64", new[] { "32", "64", "128", "256", "512" }),
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
            "Sampled MTF" or "Contrast Loss Map" => new[]
            {
                IntParameter("PupilSampling", "瞳面采样数", "32", 8, 512),
                IntParameter("ZernikeTerms", "Zernike 拟合项数", "37", 1, 128),
                IntParameter("PlotPointCount", "曲线采样点数", "128", 2, 2048),
                DoubleParameter("MaximumFrequency", "最大频率（0=截止）", "0", 0, 10000, 10)
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
            "Wavefront Map" => new[]
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
            "Wavefront" or "Interferogram" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "15", 1, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257)
            },
            "Centroid Sphere Wavefront" or "Best Fit Sphere Wavefront" => new[]
            {
                IntParameter("NumRings", "六角采样环数", "8", 2, 32),
                IntParameter("MapSize", "波前图尺寸", "65", 17, 257),
                DoubleParameter("RobustTrimStandardDeviations", "鲁棒裁剪 sigma", "3", 0, 10, 0.5)
            },
            "Zernike Fringe" or "Zernike Standard" => new[]
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
            "Image Simulation" => new[]
            {
                ChoiceParameter(
                    "SourceImage",
                    "输入图像",
                    "彩色测试卡",
                    new[] { "彩色测试卡", "分辨率靶标", "畸变网格", "西门子星" }),
                IntParameter("PsfSize", "PSF 尺寸", "32", 8, 256),
                IntParameter("NumRays", "光线数", "16", 2, 256),
                IntParameter("EigenPsfComponents", "EigenPSF 分量数", "3", 1, 12),
                IntParameter("DistortionGridSize", "畸变采样网格", "9", 3, 33),
                IntParameter("DistortionPolynomialDegree", "畸变拟合阶数", "5", 1, 9)
            },
            "Jones Pupil" => new[] { IntParameter("GridSize", "网格尺寸", "65", 3, 257) },
            _ => Array.Empty<AnalysisParameterDescriptor>()
        };
    }
}
