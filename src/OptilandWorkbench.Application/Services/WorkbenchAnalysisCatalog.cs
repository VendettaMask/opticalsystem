using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.Application.Services;

public sealed record WorkbenchAnalysisDescriptor(
    string CanonicalKey,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    AnalysisPresentationKind PresentationKind,
    AnalysisRibbonCommand? RibbonCommand);

public static partial class WorkbenchAnalysisCatalog
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNamesByKey = new Dictionary<string, string>
    {
        ["Single Ray Trace"] = "单光线追迹",
        ["Non-Sequential Ray Trace"] = "非序列单光线追迹",
        ["Non-Sequential Detector Viewer"] = "探测器查看器",
        ["First Order"] = "系统数据摘要",
        ["Seidel Coefficients"] = "赛德尔系数",
        ["Seidel Diagram"] = "赛德尔图",
        ["Spot Diagram"] = "标准点列图",
        ["Full Field Spot Diagram"] = "全视场点列图",
        ["Matrix Spot Diagram"] = "矩阵点列图",
        ["Configuration Matrix Spot Diagram"] = "结构矩阵点列图",
        ["Ray Fan"] = "光线像差图",
        ["Footprint Diagram"] = "光迹图",
        ["Grid Distortion"] = "网格畸变",
        ["Field Curvature and Distortion"] = "场曲/畸变",
        ["Field Curvature"] = "场曲",
        ["Color Focus Shift"] = "色焦移",
        ["Lateral Color"] = "垂轴色差",
        ["Axial Aberration"] = "轴向像差",
        ["Full Field Aberration"] = "全视场像差",
        ["Encircled Energy"] = "\u51e0\u4f55",
        ["Diffraction Encircled Energy"] = "\u884d\u5c04",
        ["Geometric Line Edge Spread"] = "\u51e0\u4f55\u7ebf/\u8fb9\u7f18\u6269\u6563",
        ["Extended Source Encircled Energy"] = "\u6269\u5c55\u5149\u6e90",
        ["Pupil Aberration"] = "瞳孔像差",
        ["RMS vs Field"] = "RMS vs. 视场",
        ["RMS vs Wavelength"] = "RMS vs. 波长",
        ["RMS vs Focus"] = "RMS vs. 离焦",
        ["RMS Field Map"] = "二维视场RMS图",
        ["RMS Wavefront vs Field"] = "RMS 波前-视场",
        ["Zernike vs Field"] = "Zernike系数 vs. 视场",
        ["Through Focus"] = "离焦点列图",
        ["Through Focus MTF"] = "离焦 MTF",
        ["Fourier Through Focus MTF"] = "傅里叶离焦 MTF",
        ["Huygens Through Focus MTF"] = "惠更斯离焦 MTF",
        ["Geometric Through Focus MTF"] = "几何离焦 MTF",
        ["Fourier MTF vs Field"] = "傅里叶 MTF VS 视场",
        ["Huygens MTF vs Field"] = "惠更斯 MTF VS 视场",
        ["Geometric MTF vs Field"] = "几何 MTF VS 视场",
        ["Angle vs Image Height"] = "入射角 vs. 像高",
        ["Angle vs Image Height - Through Pupil"] = "入射角-像高（扫描瞳孔）",
        ["Angle vs Image Height - Through Field"] = "入射角-像高（扫描视场）",
        ["Cardinal Points Data"] = "基面数据",
        ["Vignetting Diagram"] = "渐晕图",
        ["Relative Illumination"] = "相对照度",
        ["Incoherent Irradiance"] = "非相干照度",
        ["Radiant Intensity"] = "辐射强度",
        ["Y-Ybar"] = "Y-Ybar",
        ["PSF"] = "FFT PSF",
        ["FFT PSF Cross Section"] = "FFT PSF截面图",
        ["FFT Line Edge Spread"] = "FFT 线/边缘扩散",
        ["Huygens PSF"] = "惠更斯PSF",
        ["Huygens PSF Cross Section"] = "惠更斯PSF截面图",
        ["MTF"] = "傅里叶 MTF",
        ["Huygens MTF"] = "惠更斯 MTF",
        ["Geometric MTF"] = "几何 MTF",
        ["Sampled MTF"] = "采样 MTF",
        ["Contrast Loss Map"] = "对比度损失图",
        ["Optical Path Difference"] = "光程差图",
        ["Foucault Analysis"] = "傅科分析",
        ["Wavefront Map"] = "波前图",
        ["Interferogram"] = "干涉图",
        ["Wavefront"] = "波前",
        ["Centroid Sphere Wavefront"] = "质心参考球波前",
        ["Best Fit Sphere Wavefront"] = "最佳拟合球波前",
        ["Zernike"] = "Zernike 系数",
        ["Zernike Fringe"] = "Zernike Fringe系数",
        ["Zernike Standard"] = "Zernike Standard系数",
        ["Zernike Annular"] = "Zernike Annular系数",
        ["Image Simulation"] = "图像模拟",
        ["Geometric Image Analysis"] = "几何图像分析",
        ["Geometric Bitmap Image Analysis"] = "几何位图图像分析",
        ["Light Source Analysis"] = "光源分析",
        ["Partially Coherent Image Analysis"] = "部分相干图像分析",
        ["Extended Diffraction Image Analysis"] = "扩展图像分析",
        ["Jones Pupil"] = "Jones 瞳",
        ["Prescription Report"] = "表面数据报告",
        ["System Data Report"] = "系统数据报告",
        ["Classified Data Report"] = "分类数据报告"
    };

    private static readonly IReadOnlyDictionary<string, string> AliasesByName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["一级像差/一阶量"] = "First Order",
            ["一阶量"] = "First Order",
            ["处方报告"] = "Prescription Report",
            ["光程差图"] = "Optical Path Difference",
            ["波前图"] = "Wavefront Map",
            ["干涉图"] = "Interferogram",
            ["傅科分析"] = "Foucault Analysis",
            ["对比度损失图"] = "Contrast Loss Map",
            ["Zernike Fringe系数"] = "Zernike Fringe",
            ["Zernike Standard系数"] = "Zernike Standard",
            ["Zernike Annular系数"] = "Zernike Annular",
            ["Zernike系数 vs. 视场"] = "Zernike vs Field",
            ["光瞳像差"] = "Pupil Aberration",
            ["全视场像差"] = "Full Field Aberration",
            ["场曲/畸变"] = "Field Curvature and Distortion",
            ["Distortion"] = "Field Curvature and Distortion",
            ["畸变"] = "Field Curvature and Distortion",
            ["轴向像差"] = "Axial Aberration",
            ["轴向色差"] = "Axial Aberration",
            ["垂轴色差"] = "Lateral Color",
            ["色焦移"] = "Color Focus Shift",
            ["赛德尔系数"] = "Seidel Coefficients",
            ["赛德尔图"] = "Seidel Diagram",
            ["点列图"] = "Spot Diagram",
            ["光线扇形图"] = "Ray Fan",
            ["非顺序光线追迹"] = "Non-Sequential Ray Trace",
            ["非顺序单光线追迹"] = "Non-Sequential Ray Trace",
            ["非序列光线追迹"] = "Non-Sequential Ray Trace",
            ["非序列探测器查看"] = "Non-Sequential Detector Viewer",
            ["离焦扫描"] = "Through Focus",
            ["点扩散函数 PSF"] = "PSF",
            ["惠更斯 PSF"] = "Huygens PSF"
        };

    private static readonly IReadOnlyDictionary<string, AnalysisPresentationKind> PresentationKindsByKey =
        new Dictionary<string, AnalysisPresentationKind>(StringComparer.Ordinal)
        {
            ["Cardinal Points Data"] = AnalysisPresentationKind.CardinalPoints,
            ["Seidel Coefficients"] = AnalysisPresentationKind.SeidelCoefficients,
            ["Zernike Fringe"] = AnalysisPresentationKind.ZernikeFringe,
            ["Zernike Standard"] = AnalysisPresentationKind.ZernikeStandard,
            ["Zernike Annular"] = AnalysisPresentationKind.ZernikeAnnular,
            ["Seidel Diagram"] = AnalysisPresentationKind.SeidelDiagram,
            ["Full Field Aberration"] = AnalysisPresentationKind.FullFieldAberration,
            ["Wavefront Map"] = AnalysisPresentationKind.WavefrontMap,
            ["Interferogram"] = AnalysisPresentationKind.Interferogram,
            ["PSF"] = AnalysisPresentationKind.FftPsf,
            ["Huygens PSF"] = AnalysisPresentationKind.HuygensPsf,
            ["Foucault Analysis"] = AnalysisPresentationKind.Foucault,
            ["Spot Diagram"] = AnalysisPresentationKind.SpotDiagram,
            ["Through Focus"] = AnalysisPresentationKind.ThroughFocusSpot,
            ["Matrix Spot Diagram"] = AnalysisPresentationKind.MatrixSpot,
            ["Configuration Matrix Spot Diagram"] = AnalysisPresentationKind.ConfigurationMatrixSpot,
            ["Full Field Spot Diagram"] = AnalysisPresentationKind.FullFieldSpot,
            ["Ray Fan"] = AnalysisPresentationKind.RayFan,
            ["Pupil Aberration"] = AnalysisPresentationKind.PupilAberration,
            ["Optical Path Difference"] = AnalysisPresentationKind.OpticalPathDifference,
            ["Footprint Diagram"] = AnalysisPresentationKind.FootprintDiagram,
            ["Axial Aberration"] = AnalysisPresentationKind.AxialAberration,
            ["Lateral Color"] = AnalysisPresentationKind.LateralColor,
            ["Color Focus Shift"] = AnalysisPresentationKind.ColorFocusShift,
            ["Field Curvature and Distortion"] = AnalysisPresentationKind.FieldCurvatureAndDistortion,
            ["Field Curvature"] = AnalysisPresentationKind.FieldCurvature,
            ["Angle vs Image Height"] = AnalysisPresentationKind.AngleVsImageHeight,
            ["Angle vs Image Height - Through Pupil"] = AnalysisPresentationKind.AngleVsImageHeight,
            ["Angle vs Image Height - Through Field"] = AnalysisPresentationKind.AngleVsImageHeight
        };

    private static readonly IReadOnlyDictionary<string, WorkbenchAnalysisDescriptor> DescriptorsByKey;

    public static IReadOnlyList<WorkbenchAnalysisDescriptor> Descriptors { get; }

    static WorkbenchAnalysisCatalog()
    {
        DescriptorsByKey = BuildDescriptors();
        Descriptors = DescriptorsByKey.Values.ToArray();
    }

    public static string CanonicalKey(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (DisplayNamesByKey.ContainsKey(name))
        {
            return name;
        }

        if (AliasesByName.TryGetValue(name, out var aliasKey))
        {
            return aliasKey;
        }

        foreach (var item in DisplayNamesByKey)
        {
            if (string.Equals(item.Value, name, StringComparison.Ordinal))
            {
                return item.Key;
            }
        }

        return name;
    }

    public static string DisplayName(string canonicalKey) =>
        DisplayNamesByKey.TryGetValue(canonicalKey, out var displayName)
            ? displayName
            : canonicalKey;

    public static AnalysisPresentationKind PresentationKind(string canonicalKey) =>
        PresentationKindsByKey.TryGetValue(canonicalKey, out var presentationKind)
            ? presentationKind
            : AnalysisPresentationKind.Standard;

    public static bool IsAvailableInMode(string name, OpticalWorkbenchMode mode)
    {
        var canonical = CanonicalKey(name);
        var isNonSequential = canonical is "Non-Sequential Ray Trace" or "Non-Sequential Detector Viewer";
        return isNonSequential
            ? mode == OpticalWorkbenchMode.NonSequential
            : mode == OpticalWorkbenchMode.Sequential;
    }

    public static IReadOnlyList<WorkbenchAnalysisDescriptor> DescriptorsForMode(
        OpticalWorkbenchMode mode) => Descriptors
        .Where(descriptor => IsAvailableInMode(descriptor.CanonicalKey, mode))
        .ToArray();

    public static bool TryGetDescriptor(string name, out WorkbenchAnalysisDescriptor descriptor) =>
        DescriptorsByKey.TryGetValue(CanonicalKey(name), out descriptor!);

    private static IReadOnlyDictionary<string, WorkbenchAnalysisDescriptor> BuildDescriptors()
    {
        var descriptors = new Dictionary<string, WorkbenchAnalysisDescriptor>(StringComparer.Ordinal);
        foreach (var item in DisplayNamesByKey)
        {
            var aliases = AliasesByName
                .Where(alias => string.Equals(alias.Value, item.Key, StringComparison.Ordinal))
                .Select(alias => alias.Key)
                .Append(item.Value)
                .Where(alias => !string.Equals(alias, item.Key, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var ribbonCommand = AllRibbonCommands.FirstOrDefault(command =>
                command.Kind == AnalysisRibbonCommandKind.Analysis
                && string.Equals(CanonicalKey(command.Name), item.Key, StringComparison.Ordinal));
            descriptors.Add(item.Key, new WorkbenchAnalysisDescriptor(
                item.Key,
                item.Value,
                aliases,
                PresentationKind(item.Key),
                ribbonCommand));
        }

        return descriptors;
    }
}
