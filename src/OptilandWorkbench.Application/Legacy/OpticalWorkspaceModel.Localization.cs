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
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Tolerancing;
using ContractMeritFunctionPreset = OptilandWorkbench.Application.Contracts.MeritFunctionPreset;

namespace OptilandWorkbench.Application.Legacy;

public partial class OpticalWorkspaceModel
{
    public static string DisplayOptimizerMessage(string message)
    {
        const string optimizedWith = "Optimized with ";
        if (message.StartsWith(optimizedWith, StringComparison.Ordinal))
        {
            var optimizer = message[optimizedWith.Length..];
            const string fallback = " using orthogonal descent fallback";
            if (optimizer.EndsWith(fallback, StringComparison.Ordinal))
            {
                optimizer = optimizer[..^fallback.Length];
                return $"已使用 {optimizer} 的正交下降回退完成优化";
            }

            return $"已使用 {optimizer} 完成优化";
        }

        return message;
    }

    private static string DisplayAnalysisName(string name) =>
        WorkbenchAnalysisCatalog.DisplayName(name);

    private static string CanonicalAnalysisName(string name) =>
        WorkbenchAnalysisCatalog.CanonicalKey(name);

    private static string DisplayAnalysisKey(string key)
    {
        if (key.StartsWith("FieldCurvature.", StringComparison.OrdinalIgnoreCase))
        {
            return "场曲." + DisplayAnalysisKey(key["FieldCurvature.".Length..]);
        }

        if (key.StartsWith("Distortion.", StringComparison.OrdinalIgnoreCase))
        {
            return "畸变." + DisplayAnalysisKey(key["Distortion.".Length..]);
        }

        if (key.StartsWith("Surface ", StringComparison.OrdinalIgnoreCase))
        {
            return "表面 " + key["Surface ".Length..];
        }

        if (key.StartsWith("Field ", StringComparison.OrdinalIgnoreCase))
        {
            return "视场 " + key["Field ".Length..];
        }

        return AnalysisKeyDisplayNames.TryGetValue(key, out var display) ? display : key;
    }

    private static bool TryNormalizeApertureKind(string name, out ApertureKind kind)
    {
        if (ApertureKind.TryParse(name, out kind))
        {
            return true;
        }

        kind = name switch
        {
            "入瞳直径" => ApertureKind.EntrancePupilDiameter,
            "像方 F 数" or "F 数" => ApertureKind.FNumber,
            "物方数值孔径" or "数值孔径" => ApertureKind.NumericalAperture,
            "按光阑面尺寸浮动" => ApertureKind.FloatByStopSize,
            _ => ApertureKind.EntrancePupilDiameter
        };

        return name is "入瞳直径"
            or "像方 F 数"
            or "F 数"
            or "物方数值孔径"
            or "数值孔径"
            or "按光阑面尺寸浮动";
    }

    private static string CanonicalGeometryKind(string value)
    {
        return value switch
        {
            "平面" => "Plane",
            "标准球面/圆锥" => "Standard",
            "平面光栅" => "Plane Grating",
            "标准曲面光栅" => "Standard Grating",
            "偶次非球面" => "Even Asphere",
            "奇次非球面" => "Odd Asphere",
            "双圆锥" => "Biconic",
            "环形面" => "Toroidal",
            "XY 多项式" => "Polynomial",
            "Chebyshev 曲面" => "Chebyshev",
            "Zernike 曲面" => "Zernike",
            "Forbes Q 曲面" => "Forbes Q",
            _ => value
        };
    }

    private static string CanonicalCoatingKind(string value)
    {
        return value switch
        {
            "无镀膜" => "None",
            "MgF2 单层" => "MgF2",
            "四分之一波堆栈" => "Quarter-wave Stack",
            _ => value
        };
    }

    private static string CanonicalInteractionKind(string value)
    {
        return value switch
        {
            "折射" => "Refractive",
            "反射" => "Reflective",
            "薄透镜" => "Thin Lens",
            "反射薄透镜" => "Reflective Thin Lens",
            "衍射" => "Diffractive",
            "反射衍射" => "Reflective Diffractive",
            "相位" => "Phase",
            _ => value
        };
    }

    private static string CanonicalPhysicalApertureKind(string value)
    {
        return value switch
        {
            "圆形" => "Circular",
            "环形" => "Annular",
            "偏心圆" => "Offset Radial",
            "矩形" => "Rectangular",
            "椭圆" => "Elliptical",
            "多边形" => "Polygon",
            "组合孔径" => "Boolean",
            "无" => "None",
            _ => value
        };
    }

    private static string CanonicalApodizationKind(string value)
    {
        return value switch
        {
            "无" => "None",
            "均匀" => "Uniform",
            "高斯" => "Gaussian",
            "余弦平方" => "CosineSquared",
            "Hann" => "Hann",
            "多项式" => "Polynomial",
            "超高斯" => "SuperGaussian",
            "Tukey" => "Tukey",
            _ => value
        };
    }

    private static readonly IReadOnlyDictionary<string, string> AnalysisKeyDisplayNames = new Dictionary<string, string>
    {
        ["RayCount"] = "光线数",
        ["VignettedRayCount"] = "渐晕光线数",
        ["Centroid"] = "质心",
        ["CentroidX"] = "质心 X",
        ["CentroidY"] = "质心 Y",
        ["RmsSpotRadius"] = "RMS 点半径",
        ["MaxSpotRadius"] = "最大点半径",
        ["Samples"] = "采样数",
        ["Min"] = "最小值",
        ["Max"] = "最大值",
        ["EffectiveFocalLength"] = "有效焦距",
        ["FNumber"] = "F 数",
        ["TotalTrack"] = "系统总长",
        ["MaxFieldDegrees"] = "最大视场角",
        ["IdealImageHeight"] = "理想像高",
        ["MeanActualHeight"] = "平均实际像高",
        ["DistortionPercent"] = "畸变百分比",
        ["PetzvalProxy"] = "Petzval 近似",
        ["SagAtFullField"] = "全视场场曲矢高",
        ["Radius50"] = "50% 半径",
        ["Radius80"] = "80% 半径",
        ["Radius95"] = "95% 半径",
        ["TotalWeight"] = "总权重",
        ["ApertureRadius"] = "孔径半径",
        ["EntrancePupilEstimate"] = "入瞳估计",
        ["ChiefRayPupilShiftProxy"] = "主光线瞳移近似",
        ["WeightedMean"] = "加权平均",
        ["IncludedFieldWeight"] = "参与聚合视场权重",
        ["FocusStep"] = "离焦步长",
        ["DefocusStepMicrometers"] = "离焦范围 (µm)",
        ["DeltaFocus"] = "离焦范围 (±mm)",
        ["Steps"] = "步长数",
        ["Minus2StepRms"] = "-2 步 RMS",
        ["Minus1StepRms"] = "-1 步 RMS",
        ["NominalRms"] = "名义焦点 RMS",
        ["Plus1StepRms"] = "+1 步 RMS",
        ["Plus2StepRms"] = "+2 步 RMS",
        ["BestFocusShift"] = "最佳焦移",
        ["BestRmsSpotRadius"] = "最佳 RMS 点半径",
        ["Radius80AtBest"] = "最佳 80% 半径",
        ["ReferenceOpticalPathLength"] = "参考光程",
        ["MeanOpticalPathDifference"] = "平均光程差",
        ["RmsOpticalPathDifference"] = "RMS 光程差",
        ["PeakToValleyOpticalPathDifference"] = "PV 光程差",
        ["RmsWaves"] = "RMS 波数",
        ["RmsWavefrontProxy"] = "RMS 波前近似",
        ["PeakToValleyProxy"] = "PV 波前近似",
        ["Reference"] = "参考",
        ["Method"] = "方法",
        ["Data"] = "数据",
        ["ShowDiffractionLimit"] = "显示衍射极限",
        ["DiffractionLimitMillimeters"] = "衍射极限 (mm)",
        ["RemoveVignetting"] = "移除渐晕",
        ["Sigma"] = "Sigma",
        ["PeakNormalized"] = "归一化峰值",
        ["Pipeline"] = "仿真流程",
        ["ZemaxImageSimulationSettings"] = "Zemax 图像仿真设置",
        ["SourceMode"] = "源类型",
        ["OutputShape"] = "输出形状",
        ["FieldHeight"] = "视场高度",
        ["Oversampling"] = "过采样",
        ["GuardBand"] = "保护带",
        ["SourceFlip"] = "翻转位图",
        ["SourceRotationDegrees"] = "旋转位图 (°)",
        ["ImageFlip"] = "翻转图像",
        ["RelativeIllumination"] = "相对照度",
        ["AberrationMode"] = "像差模式",
        ["Reference"] = "参考",
        ["FieldCenter"] = "视场中心",
        ["PixelSizeMillimeters"] = "像素大小 (mm)",
        ["DetectorXPixels"] = "X 像素",
        ["DetectorYPixels"] = "Y 像素",
        ["ApplyFixedApertures"] = "应用固定孔径",
        ["CompressFrame"] = "压缩框架",
        ["OutputFile"] = "输出文件",
        ["ImageWidth"] = "图像宽度",
        ["ImageHeight"] = "图像高度",
        ["WavelengthsMicrometers"] = "仿真波长 (µm)",
        ["PsfGridShape"] = "PSF 视场网格",
        ["PsfGridRows"] = "PSF 网格行",
        ["PsfGridColumns"] = "PSF 网格列",
        ["PsfSize"] = "PSF 尺寸",
        ["EigenPsfComponents"] = "EigenPSF 分量数",
        ["DistortionGridSize"] = "畸变采样网格",
        ["DistortionPolynomialDegree"] = "畸变拟合阶数",
        ["MeanAbsoluteChange"] = "平均绝对变化",
        ["MaximumOutputValue"] = "最大输出值",
        ["Field"] = "视场",
        ["ValidRayCount"] = "有效光线数",
        ["CoatingMode"] = "镀膜模式",
        ["Layout"] = "图形布局",
        ["Name"] = "名称",
        ["SurfaceCount"] = "表面数",
        ["FieldCount"] = "视场数",
        ["WavelengthCount"] = "波长数",
        ["WavelengthMicrometers"] = "分析波长 (µm)",
        ["WavelengthNumber"] = "波长序号",
        ["Frequency"] = "频率 (cycles/mm)",
        ["RequestedFrequency"] = "请求频率 (cycles/mm)",
        ["PupilSeparation"] = "瞳孔分离量",
        ["Normalize"] = "归一化",
        ["ShowOPD"] = "显示 OPD",
        ["MaximumContrastLoss"] = "最大对比度损失",
        ["ValidSampleCount"] = "有效采样数",
        ["MaximumAberration"] = "最大像差范围",
        ["GridInterval"] = "网格线间隔",
        ["MaximumFocalShiftChangeMicrometers"] = "最大焦移变化",
        ["DiffractionLimitChangeMicrometers"] = "衍射极限变化",
        ["PupilZone"] = "光瞳区域",
        ["MaximumShiftMicrometers"] = "最大漂移",
        ["ShortestWavelengthMicrometers"] = "短波长",
        ["LongestWavelengthMicrometers"] = "长波长",
        ["UseRealRays"] = "使用实际光线",
        ["AllWavelengths"] = "所有波长",
        ["ShowAiryDisk"] = "显示艾里斑",
        ["AiryRadiusMicrometers"] = "艾里斑半径",
        ["GraphScaleMicrometers"] = "图形缩放",
        ["Row"] = "行",
        ["ProfileType"] = "类型",
        ["Spread"] = "扩散",
        ["UseCoherentPsf"] = "使用相干 PSF",
        ["UseCentroid"] = "使用质心",
        ["CentroidXMicrometers"] = "质心 X (µm)",
        ["CentroidYMicrometers"] = "质心 Y (µm)",
        ["PupilRadiusMillimeters"] = "光瞳半径",
        ["GraphScaleMillimeters"] = "图形缩放",
        ["UseDashes"] = "使用虚线",
        ["FieldShape"] = "视场形状",
        ["XFieldWidth"] = "X 视场宽度",
        ["YFieldWidth"] = "Y 视场宽度",
        ["Decomposition"] = "分解",
        ["MaximumTerm"] = "最大项",
        ["Aberration"] = "像差",
        ["XFieldSamples"] = "X 视场采样",
        ["YFieldSamples"] = "Y 视场采样",
        ["PupilSampling"] = "光瞳采样",
        ["DisplayAs"] = "显示为",
        ["DisplayMode"] = "显示",
        ["MeanAberrationWaves"] = "平均",
        ["PlotMinimumWaves"] = "绘图范围最小值",
        ["PlotMaximumWaves"] = "绘图范围最大值",
        ["ValidFieldSamples"] = "有效视场采样数",
        ["DistortionType"] = "畸变模型",
        ["DisplayMode"] = "显示方式",
        ["ScanDirection"] = "扫描方向",
        ["ReferenceFieldNumber"] = "参考视场",
        ["IgnoreVignettingFactors"] = "忽略渐晕因数",
        ["SmiaTvDistortionPercent"] = "SMIA-TV 畸变 (%)",
        ["MaximumAbsoluteDistortionPercent"] = "最大绝对畸变 (%)",
        ["MaximumAbsoluteDistortionMillimeters"] = "最大绝对畸变 (mm)",
        ["MaximumDistortionPercent"] = "最大网格畸变 (%)",
        ["Scale"] = "畸变显示缩放",
        ["HeightWidthAspect"] = "H/W 纵横比",
        ["SymmetricMagnification"] = "对称放大",
        ["FieldWidth"] = "视场宽度",
        ["MappingA"] = "参考映射 A",
        ["MappingB"] = "参考映射 B",
        ["MappingC"] = "参考映射 C",
        ["MappingD"] = "参考映射 D",
        ["MaximumTangentialFieldCurvatureMillimeters"] = "最大子午场曲 (mm)",
        ["MaximumSagittalFieldCurvatureMillimeters"] = "最大弧矢场曲 (mm)",
        ["MaximumAbsoluteImagePlaneDelta"] = "最大像面偏移 (mm)",
        ["MaximumCurvatureScale"] = "最大场曲缩放",
        ["GridSize"] = "网格尺寸",
        ["ImageDeltaMicrometers"] = "像面采样间距 (µm)",
        ["ImageSize"] = "图像尺寸",
        ["ImageExtentMicrometers"] = "像的尺寸 (µm)",
        ["PixelPitchMicrometers"] = "像素间距 (µm)",
        ["PixelPitchMillimeters"] = "像素间距 (mm)",
        ["ImagePlane"] = "像面平面",
        ["Samples"] = "采样点数",
        ["NumberOfRaysEachSide"] = "原点每侧光线数",
        ["PlotScaleMicrometers"] = "图形缩放 (µm)",
        ["ScaleBarMicrometers"] = "缩放标尺 (µm)",
        ["Magnification"] = "放大",
        ["IgnoreLateralColor"] = "忽略垂轴色差",
        ["RmsRadiusMicrometers"] = "RMS 半径 (µm)",
        ["GeometricRadiusMicrometers"] = "GEO 半径 (µm)",
        ["TangentialAberration"] = "子午分量",
        ["SagittalAberration"] = "弧矢分量",
        ["UseDashes"] = "使用虚线",
        ["VignettedPupil"] = "渐晕光瞳",
        ["CheckApertures"] = "检查孔径",
        ["NumRings"] = "六角采样环数",
        ["NumRays"] = "光线数",
        ["Distribution"] = "瞳孔采样分布",
        ["PlotPointCount"] = "曲线采样点数",
        ["MaximumGeometricSpotRadius"] = "最大几何点半径 (mm)",
        ["MaximumRmsSpotSize"] = "最大 RMS 点尺寸 (mm)",
        ["MaximumRmsWavefrontError"] = "最大 RMS 波前误差 (波)",
        ["ScanMode"] = "扫描模式",
        ["SurfaceIndex"] = "测量表面序号",
        ["Axis"] = "测量轴",
        ["PointCount"] = "采样点数",
        ["FixedCoordinates"] = "固定坐标",
        ["FocusPlaneCount"] = "焦面数量",
        ["SpatialFrequency"] = "空间频率 (cycles/mm)",
        ["RawTangential"] = "切向 MTF 原始数据",
        ["RawSagittal"] = "弧矢 MTF 原始数据",
        ["ParaxialStopRadius"] = "近轴停光面半径 (mm)",
        ["MinimumPupilAberration"] = "最小光瞳像差 (%)",
        ["MaximumPupilAberration"] = "最大光瞳像差 (%)",
        ["MinimumRayAberration"] = "最小光线像差 (mm)",
        ["MaximumRayAberration"] = "最大光线像差 (mm)",
        ["PeakToValleyWaves"] = "波峰到波谷",
        ["PupilDiameterMillimeters"] = "出瞳直径 (mm)",
        ["Sampling"] = "采样",
        ["RotationDegrees"] = "旋转",
        ["Rotation"] = "旋转",
        ["DisplayScale"] = "显示缩放",
        ["Apodization"] = "偏振",
        ["ReferenceChiefRay"] = "参考主光线",
        ["UseExitPupilShape"] = "使用出瞳形状",
        ["DisplayAs"] = "显示为",
        ["RemoveTilt"] = "除去倾斜",
        ["PupilSx"] = "Sx",
        ["PupilSy"] = "Sy",
        ["PupilSr"] = "Sr",
        ["KnifeEdge"] = "刀口",
        ["DataSource"] = "数据",
        ["YPositionMicrometers"] = "Y位置 (µm)",
        ["KnifePositionMicrometers"] = "刀口 Y 位置 (µm)",
        ["MinimumResponse"] = "最小响应",
        ["MaximumResponse"] = "最大响应",
        ["PupilSampling"] = "瞳面采样数",
        ["RayDensity"] = "光线密度",
        ["Pattern"] = "样式",
        ["SurfaceNumber"] = "表面序号",
        ["SurfaceLabel"] = "表面标注",
        ["FieldNumber"] = "视场序号",
        ["DeleteVignetted"] = "删除渐晕光线",
        ["UseSymbols"] = "使用标注",
        ["ColorRaysBy"] = "颜色显示",
        ["UsePolarization"] = "使用偏振",
        ["DirectionCosines"] = "方向余弦",
        ["ShowAiryDisk"] = "显示艾里斑",
        ["AiryRadius"] = "艾里斑半径 (mm)",
        ["DisplayScale"] = "显示缩放",
        ["ScatterRays"] = "散射光线",
        ["LaunchedRayCount"] = "发射光线数",
        ["PlottedRayCount"] = "绘制光线数",
        ["TransmissionPercent"] = "绘制比例 (%)",
        ["XScaleMillimeters"] = "X 缩放 (mm)",
        ["YScaleMillimeters"] = "Y 缩放 (mm)",
        ["ApertureDiameterMillimeters"] = "孔径 (mm)",
        ["RayXMinimumMillimeters"] = "光线 X 最小 (mm)",
        ["RayXMaximumMillimeters"] = "光线 X 最大 (mm)",
        ["RayYMinimumMillimeters"] = "光线 Y 最小 (mm)",
        ["RayYMaximumMillimeters"] = "光线 Y 最大 (mm)",
        ["MaximumRayRadiusMillimeters"] = "最大半径 (mm)",
        ["WavelengthsMicrometers"] = "波长 (µm)",
        ["FieldDensity"] = "视场密度",
        ["RemoveVignettingFactors"] = "移除渐晕因子",
        ["MaximumProjectedCosineArea"] = "最大投影方向余弦面积",
        ["EffectiveFNumbers"] = "有效 F/#",
        ["ValidRayCounts"] = "有效光线数",
        ["FoldedCellCounts"] = "方向余弦折叠单元数",
        ["DetectorSurfaceIndex"] = "探测器表面序号",
        ["DetectorExtent"] = "探测器范围",
        ["Resolution"] = "探测器分辨率",
        ["Normalized"] = "归一化显示",
        ["PeakIrradiance"] = "峰值照度 (W/mm²)",
        ["PythonRequirement"] = "Python Optiland 要求",
        ["ReferenceSurfaceIndex"] = "参考表面序号",
        ["ReferenceSurfaceNumber"] = "参考面",
        ["ReferenceSurfaceLabel"] = "参考面名称",
        ["ReferenceSurfacePosition"] = "参考面位置 (mm)",
        ["AngularBins"] = "角度分箱",
        ["AngleXRange"] = "X 角度范围",
        ["AngleYRange"] = "Y 角度范围",
        ["UseAbsoluteUnits"] = "使用绝对单位",
        ["PeakRadiantIntensity"] = "峰值辐射强度 (W/sr)",
        ["ScaleByDiffractionLimit"] = "乘以衍射极限包络",
        ["FitRings"] = "拟合球六角采样环数",
        ["ReferenceCenters"] = "最佳拟合球中心",
        ["ZernikeTerms"] = "Zernike 拟合项数",
        ["WorkingFNumber"] = "工作 F/#",
        ["StrehlRatio"] = "斯特列尔比",
        ["PeakStrehlRatio"] = "峰值斯特列尔比",
        ["WavelengthRange"] = "波长范围",
        ["CutoffFrequency"] = "截止频率 (cycles/mm)",
        ["ReferenceSphereCenter"] = "参考球中心",
        ["ReferenceSphereRadius"] = "参考球半径 (mm)",
        ["ZernikeType"] = "Zernike 类型",
        ["FieldHx"] = "归一化视场 Hx",
        ["FieldHy"] = "归一化视场 Hy",
        ["FieldSelection"] = "视场",
        ["PupilPx"] = "归一化瞳孔 Px",
        ["PupilPy"] = "归一化瞳孔 Py",
        ["CoordinateSystem"] = "坐标系",
        ["TraceType"] = "追迹类型",
        ["RayAiming"] = "光线瞄准",
        ["LastSurface"] = "最后到达表面",
        ["VignettedSurface"] = "渐晕表面",
        ["ShowRaySegments"] = "显示光线段",
        ["ParabasalDelta"] = "近轴光线间隔",
        ["MaxFieldDegrees"] = "最大视场角 (deg)",
        ["MaxObjectHeightMillimeters"] = "最大物高 (mm)",
        ["MaxParaxialImageHeightMillimeters"] = "最大近轴像高 (mm)",
        ["MaxRealImageHeightMillimeters"] = "最大实际像高 (mm)",
        ["EFL"] = "有效焦距",
        ["WeightedMetric"] = "加权指标",
        ["Status"] = "状态"
    };
}
