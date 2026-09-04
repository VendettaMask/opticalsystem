using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.Optimization;

public sealed class MeritOperandDefinition
{
    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = "RSCE";

    public int Surface { get; set; }

    public int Field { get; set; }

    public int Wavelength { get; set; }

    public double Hx { get; set; }

    public double Hy { get; set; }

    public double Px { get; set; }

    public double Py { get; set; }

    public double Target { get; set; }

    public double Weight { get; set; } = 1;

    public string Comment { get; set; } = string.Empty;

    public int PupilRings { get; set; } = 3;

    public int PupilArms { get; set; } = 6;

    public double PupilObscuration { get; set; }

    public string PupilSampling { get; set; } = "hexapolar";

    public double SpatialFrequency { get; set; } = 30;

    public bool IgnoreLateralColor { get; set; }

    public bool PolychromaticReference { get; set; }

    public bool CompatibilityOnly { get; set; }

    public int[] ZemaxIntegerParameters { get; set; } = [];

    public double[] ZemaxDataParameters { get; set; } = [];

    public MeritOperandDefinition Clone() => new()
    {
        Enabled = Enabled,
        Type = Type,
        Surface = Surface,
        Field = Field,
        Wavelength = Wavelength,
        Hx = Hx,
        Hy = Hy,
        Px = Px,
        Py = Py,
        Target = Target,
        Weight = Weight,
        Comment = Comment,
        PupilRings = PupilRings,
        PupilArms = PupilArms,
        PupilObscuration = PupilObscuration,
        PupilSampling = PupilSampling,
        SpatialFrequency = SpatialFrequency,
        IgnoreLateralColor = IgnoreLateralColor,
        PolychromaticReference = PolychromaticReference,
        CompatibilityOnly = CompatibilityOnly,
        ZemaxIntegerParameters = ZemaxIntegerParameters?.ToArray() ?? [],
        ZemaxDataParameters = ZemaxDataParameters?.ToArray() ?? []
    };
}

public enum MeritImageQuality
{
    RmsSpot,
    RmsWavefront,
    Contrast,
    Angular
}

public enum MeritPupilSampling
{
    GaussianQuadrature,
    RectangularArray
}

public enum MeritSpotReference
{
    Centroid,
    ChiefRay,
    Unreferenced
}

public sealed record MeritFunctionWizardSettings(
    MeritImageQuality ImageQuality,
    MeritPupilSampling PupilSampling,
    int PupilRings,
    int PupilArms,
    double PupilObscuration,
    double WeightScale,
    bool UseAllWavelengths,
    bool IncludeCommonOperands,
    MeritSpotReference Reference = MeritSpotReference.Centroid,
    double SpatialFrequency = 30,
    double XWeight = 1,
    double YWeight = 1,
    bool IgnoreLateralColor = false);

public sealed record MeritOperandType(
    string Code,
    string DisplayName,
    string Description);

public sealed record MeritOperandEvaluation(
    double Value,
    double Contribution,
    string Error = "");

public static class MeritFunctionCatalog
{
    private const double FraunhoferCLineNanometers = 656.2725;
    private const double FraunhoferDLineNanometers = 587.5618;
    private const double FraunhoferFLineNanometers = 486.1327;

    private static readonly AsyncLocal<EvaluationBatch?> ActiveEvaluationBatch = new();

    public static IDisposable BeginEvaluationBatch()
    {
        var previous = ActiveEvaluationBatch.Value;
        ActiveEvaluationBatch.Value = new EvaluationBatch();
        return new EvaluationBatchScope(previous);
    }

    private static readonly IReadOnlyList<MeritOperandType> WorkbenchTypes = new[]
    {
        new MeritOperandType("DMFS", "默认评价函数设置", "默认评价函数向导生成的说明行"),
        new MeritOperandType("BLNK", "空白/注释", "不参与评价函数计算"),
        new MeritOperandType("CONF", "Zemax 配置切换", "从 Zemax 导入并作为只读记录保留"),
        new MeritOperandType("RANG", "实际光线角度", "指定光线在表面处相对光轴的角度（弧度）"),
        new MeritOperandType("CONS", "Zemax 常数", "按 Zemax 行顺序把目标值作为当前值"),
        new MeritOperandType("DIVB", "Zemax 系数除法", "按 Zemax 行顺序读取前序行并除以 Factor"),
        new MeritOperandType("PROD", "Zemax 乘积运算", "按 Zemax 行顺序计算两个前序行的乘积"),
        new MeritOperandType("PROB", "Zemax 系数乘法", "按 Zemax 行顺序读取前序行并乘以 Factor"),
        new MeritOperandType("OSUM", "Zemax 范围求和", "按 Zemax 行顺序对前序行闭区间求和"),
        new MeritOperandType("QSUM", "Zemax 平方和根", "按 Zemax 行顺序对前序行闭区间计算平方和根"),
        new MeritOperandType("EQUA", "Zemax 相等约束", "按 Zemax 行顺序约束前序行闭区间在 Target 公差内相等"),
        new MeritOperandType("OPLT", "Zemax 小于约束", "指定前序行值的上限约束"),
        new MeritOperandType("OPGT", "Zemax 大于约束", "指定前序行值的下限约束"),
        new MeritOperandType("ABLT", "绝对值小于约束", "指定前序行绝对值的上限约束"),
        new MeritOperandType("ABGT", "绝对值大于约束", "指定前序行绝对值的下限约束"),
        new MeritOperandType("OPVA", "操作数值", "读取指定前序行的当前值"),
        new MeritOperandType("MNCA", "最小空气中心厚度", "指定范围内空气空间中心厚度的最小值下限"),
        new MeritOperandType("MXCA", "最大空气中心厚度", "指定范围内空气空间中心厚度的最大值上限"),
        new MeritOperandType("MNEA", "最小空气边缘厚度", "指定范围内空气空间 +Y 边缘厚度的最小值下限"),
        new MeritOperandType("MXEA", "最大空气边缘厚度", "指定范围内空气空间 +Y 边缘厚度的最大值上限"),
        new MeritOperandType("MNCG", "最小玻璃中心厚度", "指定范围内玻璃空间中心厚度的最小值下限"),
        new MeritOperandType("MXCG", "最大玻璃中心厚度", "指定范围内玻璃空间中心厚度的最大值上限"),
        new MeritOperandType("MNCT", "最小中心厚度", "指定范围内全部空间中心厚度的最小值下限"),
        new MeritOperandType("MXCT", "最大中心厚度", "指定范围内全部空间中心厚度的最大值上限"),
        new MeritOperandType("MNEG", "最小玻璃边缘厚度", "指定范围内玻璃空间 +Y 边缘厚度的最小值下限"),
        new MeritOperandType("MXEG", "最大玻璃边缘厚度", "指定范围内玻璃空间 +Y 边缘厚度的最大值上限"),
        new MeritOperandType("MNET", "最小边缘厚度", "指定范围内全部空间 +Y 边缘厚度的最小值下限"),
        new MeritOperandType("MXET", "最大边缘厚度", "指定范围内全部空间 +Y 边缘厚度的最大值上限"),
        new MeritOperandType("XNEA", "最小空气边缘厚度（全周）", "指定范围内空气空间全周边厚的最小值下限"),
        new MeritOperandType("XXEA", "最大空气边缘厚度（全周）", "指定范围内空气空间全周边厚的最大值上限"),
        new MeritOperandType("XNEG", "最小玻璃边缘厚度（全周）", "指定范围内玻璃空间全周边厚的最小值下限"),
        new MeritOperandType("XXEG", "最大玻璃边缘厚度（全周）", "指定范围内玻璃空间全周边厚的最大值上限"),
        new MeritOperandType("XNET", "最小边缘厚度（全周）", "指定范围内全部空间全周边厚的最小值下限"),
        new MeritOperandType("XXET", "最大边缘厚度（全周）", "指定范围内全部空间全周边厚的最大值上限"),
        new MeritOperandType("TGTH", "玻璃总厚度", "指定起止表面之间玻璃空间中心厚度总和"),
        new MeritOperandType("TTHI", "Zemax 范围厚度", "指定起止表面之间的轴向总厚度"),
        new MeritOperandType("CTGT", "中心厚度下限", "指定表面后的中心厚度下限"),
        new MeritOperandType("CTLT", "中心厚度上限", "指定表面后的中心厚度上限"),
        new MeritOperandType("CTVA", "中心厚度值", "指定表面后的中心厚度"),
        new MeritOperandType("ETGT", "边缘厚度下限", "指定表面后边缘厚度的下限约束"),
        new MeritOperandType("ETLT", "边缘厚度上限", "指定表面后边缘厚度的上限约束"),
        new MeritOperandType("ETVA", "边缘厚度值", "指定表面后边缘厚度"),
        new MeritOperandType("FTGT", "全口径厚度下限", "指定表面后径向全厚度的最小值下限"),
        new MeritOperandType("FTLT", "全口径厚度上限", "指定表面后径向全厚度的最大值上限"),
        new MeritOperandType("STHI", "指定点厚度", "指定表面后给定 X/Y 坐标处的厚度"),
        new MeritOperandType("CVGT", "曲率下限", "指定表面曲率的下限约束"),
        new MeritOperandType("CVLT", "曲率上限", "指定表面曲率的上限约束"),
        new MeritOperandType("CVVA", "曲率值", "指定表面的曲率"),
        new MeritOperandType("MNCV", "最小曲率", "指定范围内表面曲率的最小值下限"),
        new MeritOperandType("MXCV", "最大曲率", "指定范围内表面曲率的最大值上限"),
        new MeritOperandType("COGT", "圆锥常数下限", "指定表面圆锥常数的下限约束"),
        new MeritOperandType("COLT", "圆锥常数上限", "指定表面圆锥常数的上限约束"),
        new MeritOperandType("COVA", "圆锥常数值", "指定表面的圆锥常数"),
        new MeritOperandType("MNSD", "最小半口径", "指定范围内表面半口径的最小值下限"),
        new MeritOperandType("MXSD", "最大半口径", "指定范围内表面半口径的最大值上限"),
        new MeritOperandType("PMAG", "近轴放大率", "有限物方共轭的近轴横向放大率"),
        new MeritOperandType("REAR", "实际光线径向坐标", "指定光线在表面上的径向坐标"),
        new MeritOperandType("DIMX", "最大畸变", "按现有畸变分析计算最大绝对畸变百分比上限"),
        new MeritOperandType("PETZ", "佩兹伐半径", "按 Seidel Petzval sum 计算佩兹伐半径"),
        new MeritOperandType("SINE", "正弦", "按 Zemax 行顺序对指定前序行取正弦"),
        new MeritOperandType("DIVI", "除法", "按 Zemax 行顺序计算两个前序行的商"),
        new MeritOperandType("RSCE", "RMS 点列半径", "指定视场和波长的 RMS 点列半径"),
        new MeritOperandType("RSCH", "RMS 点列半径（主光线参考）", "使用高斯求积采样的主光线参考 RMS 点列半径"),
        new MeritOperandType("RSRE", "RMS 点列半径（矩形采样）", "使用矩形阵列采样的质心参考 RMS 点列半径"),
        new MeritOperandType("RSRH", "RMS 点列半径（矩形/主光线）", "使用矩形阵列采样的主光线参考 RMS 点列半径"),
        new MeritOperandType("RWFE", "RMS 波前差", "指定视场和波长的 RMS 光程差"),
        new MeritOperandType("OPDX", "光程差", "指定视场、波长和瞳孔坐标的光程差（波数）"),
        new MeritOperandType("OPDM", "光程差（主光线）", "减去平均波前但保留倾斜的光程差"),
        new MeritOperandType("OPDC", "光程差（无参考）", "以主光线为零点且不移除平均值或倾斜的光程差"),
        new MeritOperandType("TRAC", "横向像差半径（质心）", "相对于质心的横向像差半径"),
        new MeritOperandType("TRAR", "横向像差半径（主光线）", "相对于主波长主光线的横向像差半径"),
        new MeritOperandType("TRCX", "横向像差 X（质心）", "相对于质心的有符号 X 横向像差"),
        new MeritOperandType("TRCY", "横向像差 Y（质心）", "相对于质心的有符号 Y 横向像差"),
        new MeritOperandType("TRAX", "横向像差 X（主光线）", "相对于主波长主光线的有符号 X 横向像差"),
        new MeritOperandType("TRAY", "横向像差 Y（主光线）", "相对于主波长主光线的有符号 Y 横向像差"),
        new MeritOperandType("ANAC", "角像差半径（质心）", "相对于方向余弦质心的角像差半径"),
        new MeritOperandType("ANAR", "角像差半径（主光线）", "相对于主波长主光线的角像差半径"),
        new MeritOperandType("ANCX", "角像差 X（质心）", "相对于方向余弦质心的有符号 X 角像差"),
        new MeritOperandType("ANCY", "角像差 Y（质心）", "相对于方向余弦质心的有符号 Y 角像差"),
        new MeritOperandType("ANAX", "角像差 X（主光线）", "相对于主波长主光线的有符号 X 角像差"),
        new MeritOperandType("ANAY", "角像差 Y（主光线）", "相对于主波长主光线的有符号 Y 角像差"),
        new MeritOperandType("MECS", "Moore-Elliott 弧矢对比度", "弧矢方向移位光线对的光程差"),
        new MeritOperandType("MECT", "Moore-Elliott 切向对比度", "切向方向移位光线对的光程差"),
        new MeritOperandType("REAX", "实际光线 X", "指定光线在表面上的 X 坐标"),
        new MeritOperandType("REAY", "实际光线 Y", "指定光线在表面上的 Y 坐标"),
        new MeritOperandType("EFFL", "有效焦距", "系统有效焦距"),
        new MeritOperandType("EFLX", "X 向有效焦距", "X 截面的系统有效焦距"),
        new MeritOperandType("EFLY", "Y 向有效焦距", "Y 截面的系统有效焦距"),
        new MeritOperandType("ENPP", "入瞳位置", "系统入瞳相对位置"),
        new MeritOperandType("EPDI", "入瞳直径", "系统入瞳直径"),
        new MeritOperandType("EXPP", "出瞳位置", "系统出瞳相对位置"),
        new MeritOperandType("EXPD", "出瞳直径", "系统出瞳直径"),
        new MeritOperandType("ISFN", "像方 F 数", "系统像方 F 数"),
        new MeritOperandType("SFNO", "系统 F 数", "系统 F 数"),
        new MeritOperandType("WFNO", "工作 F 数", "系统工作 F 数"),
        new MeritOperandType("ISNA", "像方数值孔径", "近轴边缘光线给出的像方数值孔径"),
        new MeritOperandType("WLEN", "波长", "指定波长编号的波长值（µm）"),
        new MeritOperandType("INDX", "折射率", "指定表面后材料在指定波长处的折射率"),
        new MeritOperandType("MNIN", "最小 d 线折射率", "指定表面范围内玻璃 Nd 的最小值下限"),
        new MeritOperandType("MXIN", "最大 d 线折射率", "指定表面范围内玻璃 Nd 的最大值上限"),
        new MeritOperandType("MNAB", "最小阿贝数", "指定表面范围内玻璃 Vd 的最小值下限"),
        new MeritOperandType("MXAB", "最大阿贝数", "指定表面范围内玻璃 Vd 的最大值上限"),
        new MeritOperandType("POWR", "表面光焦度", "标准折射表面在指定波长处的光焦度，单位为 1/镜头单位"),
        new MeritOperandType("FNUM", "像方 F 数", "系统像方 F 数"),
        new MeritOperandType("TOTR", "系统总长", "系统总光程长度"),
        new MeritOperandType("TTGT", "总厚度下限", "指定表面后给定边缘方向总厚度的下限约束"),
        new MeritOperandType("TTLT", "总厚度上限", "指定表面后给定边缘方向总厚度的上限约束"),
        new MeritOperandType("TTVA", "总厚度值", "指定表面后给定边缘方向总厚度"),
        new MeritOperandType("RADI", "表面曲率半径", "指定表面的曲率半径"),
        new MeritOperandType("THIC", "表面厚度", "指定表面后的轴向厚度")
    };

    public static IReadOnlyList<MeritOperandType> Types { get; } = WorkbenchTypes
        .Concat(ZemaxOperandRegistry.Descriptors
            .Where(descriptor => WorkbenchTypes.All(type => type.Code != descriptor.Code))
            .Select(descriptor => new MeritOperandType(
                descriptor.Code,
                $"Zemax {descriptor.Code}",
                descriptor.SupportLevel == ZemaxOperandSupportLevel.Executable
                    ? "已连接当前 Workbench 计算引擎"
                    : "可无损保留；尚未提供可执行语义")))
        .OrderBy(type => type.Code, StringComparer.Ordinal)
        .ToArray();

    private sealed class EvaluationBatch
    {
        public Dictionary<RaySampleCacheKey, RayTraceSample> RaySamples { get; } = new();

        public Dictionary<AberrationReferenceCacheKey, (double X, double Y)> AberrationReferences { get; } = new();

        public Dictionary<WavefrontReferenceCacheKey, (double Piston, double XTilt, double YTilt)> WavefrontReferences { get; } = new();
    }

    private sealed class EvaluationBatchScope(EvaluationBatch? previous) : IDisposable
    {
        public void Dispose()
        {
            ActiveEvaluationBatch.Value = previous;
        }
    }

    private readonly record struct RaySampleCacheKey(
        Optic Optic,
        int SurfaceNumber,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        double Px,
        double Py,
        bool AimAtStop);

    private readonly record struct AberrationReferenceCacheKey(
        Optic Optic,
        bool Angular,
        bool ChiefReference,
        int Surface,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        int PupilRings,
        int PupilArms,
        double PupilObscuration,
        string PupilSampling,
        bool PolychromaticReference);

    private readonly record struct WavefrontReferenceCacheKey(
        Optic Optic,
        string Type,
        int Surface,
        int Field,
        int Wavelength,
        double Hx,
        double Hy,
        int PupilRings,
        int PupilArms,
        double PupilObscuration,
        string PupilSampling,
        bool PolychromaticReference);

    private sealed class OrderedMeritEvaluationContext
    {
        private readonly IReadOnlyList<MeritOperandDefinition> _definitions;
        private readonly MeritOperandEvaluation[] _evaluations;
        private readonly double[] _values;
        private readonly bool[] _hasFiniteValue;

        public OrderedMeritEvaluationContext(
            IReadOnlyList<MeritOperandDefinition> definitions,
            MeritOperandEvaluation[] evaluations)
        {
            _definitions = definitions;
            _evaluations = evaluations;
            _values = new double[definitions.Count];
            _hasFiniteValue = new bool[definitions.Count];
        }

        public int CurrentRowIndex { get; set; }

        public void Record(int rowIndex, MeritOperandEvaluation evaluation)
        {
            var definition = _definitions[rowIndex];
            var canonicalType = (definition.Type ?? string.Empty).Trim().ToUpperInvariant();
            var usableValue = definition.Enabled
                && canonicalType is not ("BLNK" or "DMFS")
                && string.IsNullOrEmpty(evaluation.Error)
                && double.IsFinite(evaluation.Value);
            _values[rowIndex] = evaluation.Value;
            _hasFiniteValue[rowIndex] = usableValue;
        }

        public double RowValue(int oneBasedRow)
        {
            if (oneBasedRow <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(oneBasedRow),
                    oneBasedRow,
                    "Zemax 行引用必须是从 1 开始的评价函数行号。");
            }

            var rowIndex = oneBasedRow - 1;
            if (rowIndex >= _definitions.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(oneBasedRow),
                    oneBasedRow,
                    "Zemax 行引用超出评价函数范围。");
            }

            if (rowIndex >= CurrentRowIndex)
            {
                throw new InvalidOperationException("Zemax 数学操作数只能引用已经计算完成的前序行。");
            }

            if (!_hasFiniteValue[rowIndex])
            {
                var referenced = _evaluations[rowIndex];
                var reason = !string.IsNullOrEmpty(referenced.Error)
                    ? referenced.Error
                    : "被引用行不是可用的有限数值。";
                throw new InvalidOperationException($"Zemax 行 {oneBasedRow} 不能作为数学操作数输入：{reason}");
            }

            return _values[rowIndex];
        }

        public double[] RowRangeValues(int firstOneBasedRow, int lastOneBasedRow)
        {
            if (lastOneBasedRow < firstOneBasedRow)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastOneBasedRow),
                    lastOneBasedRow,
                    "Zemax 行范围终点不能小于起点。");
            }

            var values = new double[lastOneBasedRow - firstOneBasedRow + 1];
            for (var offset = 0; offset < values.Length; offset++)
            {
                values[offset] = RowValue(firstOneBasedRow + offset);
            }

            return values;
        }
    }

    public static MeritOperandEvaluation Evaluate(Optic optic, MeritOperandDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.Enabled)
        {
            return new MeritOperandEvaluation(0, 0);
        }

        return EvaluateCore(optic, definition, context: null);
    }

    public static IReadOnlyList<MeritOperandEvaluation> EvaluateAll(
        Optic optic,
        IReadOnlyList<MeritOperandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(definitions);

        using var evaluationBatch = BeginEvaluationBatch();
        var evaluations = new MeritOperandEvaluation[definitions.Count];
        var context = new OrderedMeritEvaluationContext(definitions, evaluations);
        var rotationallySymmetric = definitions.Any(definition =>
                definition is not null
                && definition.Enabled
                && CanonicalType(definition.Type) == "USYM")
            || IsRotationallySymmetric(optic);
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index]
                ?? throw new ArgumentException("评价函数操作数不能为 null。", nameof(definitions));
            context.CurrentRowIndex = index;
            evaluations[index] = EvaluateCore(optic, definition, context);
            context.Record(index, evaluations[index]);

            if (!definition.Enabled || !string.IsNullOrEmpty(evaluations[index].Error))
            {
                continue;
            }

            var canonicalType = CanonicalType(definition.Type);
            if (canonicalType == "ENDX")
            {
                for (var skipped = index + 1; skipped < definitions.Count; skipped++)
                {
                    evaluations[skipped] = new MeritOperandEvaluation(0, 0);
                }

                break;
            }

            var shouldJump = canonicalType == "GOTO"
                || (canonicalType == "SKIS" && rotationallySymmetric)
                || (canonicalType == "SKIN" && !rotationallySymmetric);
            if (!shouldJump)
            {
                continue;
            }

            var targetRow = ZemaxIntegerParameter(definition, 0, definition.Surface);
            var targetIndex = targetRow - 1;
            if (targetIndex <= index || targetIndex >= definitions.Count)
            {
                evaluations[index] = new MeritOperandEvaluation(
                    double.NaN,
                    double.PositiveInfinity,
                    $"{canonicalType} target row {targetRow} must be after row {index + 1} and within the merit function.");
                context.Record(index, evaluations[index]);
                continue;
            }

            for (var skipped = index + 1; skipped < targetIndex; skipped++)
            {
                evaluations[skipped] = new MeritOperandEvaluation(0, 0);
            }

            index = targetIndex - 1;
        }

        return evaluations;
    }

    public static Operand CreateOperand(Optic optic, MeritOperandDefinition definition)
    {
        return new Operand(
            CanonicalType(definition.Type),
            definition.Target,
            definition.Weight,
            () =>
            {
                var value = Evaluate(optic, definition);
                return string.IsNullOrEmpty(value.Error) && double.IsFinite(value.Value)
                    ? value.Value
                    : 1_000_000;
            });
    }

    public static IReadOnlyList<Operand> CreateOperands(
        Optic optic,
        IReadOnlyList<MeritOperandDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions
            .Select((definition, index) => (Definition: definition, Index: index))
            .Where(item => item.Definition.Enabled
                && CanonicalType(item.Definition.Type) is not ("BLNK" or "DMFS"))
            .Select(item => new Operand(
                $"{CanonicalType(item.Definition.Type)} row {item.Index + 1}",
                item.Definition.Target,
                item.Definition.Weight,
                () =>
                {
                    var evaluations = EvaluateAll(optic, definitions);
                    var evaluation = evaluations[item.Index];
                    return string.IsNullOrEmpty(evaluation.Error) && double.IsFinite(evaluation.Value)
                        ? evaluation.Value
                        : 1_000_000;
                }))
            .ToArray();
    }

    private static MeritOperandEvaluation EvaluateCore(
        Optic optic,
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context)
    {
        if (!definition.Enabled)
        {
            return new MeritOperandEvaluation(0, 0);
        }

        try
        {
            var canonicalType = CanonicalType(definition.Type);
            if (canonicalType is "BLNK" or "DMFS" or "GOTO" or "ENDX" or "OOFF" or "SKIN" or "SKIS" or "USYM")
            {
                return new MeritOperandEvaluation(0, 0);
            }

            if (definition.CompatibilityOnly || HasOpaqueZemaxParameters(canonicalType))
            {
                throw new NotSupportedException(
                    $"Merit operand '{canonicalType}' is preserved for compatibility but is not executable.");
            }

            var value = EvaluateValue(optic, definition, context);
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("计算结果不是有限数值。");
            }

            return new MeritOperandEvaluation(
                value,
                ContributionFor(canonicalType, definition, value));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or ArithmeticException
            or KeyNotFoundException
            or NotSupportedException)
        {
            return new MeritOperandEvaluation(double.NaN, double.PositiveInfinity, exception.Message);
        }
    }

    private static double ContributionFor(
        string canonicalType,
        MeritOperandDefinition definition,
        double value)
    {
        if (!double.IsFinite(definition.Weight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.Weight),
                definition.Weight,
                "操作数权重必须是有限数值。");
        }

        if (canonicalType == "EQUA")
        {
            var equalityContribution = Math.Abs(definition.Weight) * value * value;
            return double.IsFinite(equalityContribution)
                ? equalityContribution
                : throw new InvalidOperationException("评价函数贡献不是有限数值。");
        }

        if (!double.IsFinite(definition.Target))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.Target),
                definition.Target,
                "操作数目标值必须是有限数值。");
        }

        var error = value - definition.Target;
        var contribution = Math.Abs(definition.Weight) * error * error;
        return double.IsFinite(contribution)
            ? contribution
            : throw new InvalidOperationException("评价函数贡献不是有限数值。");
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateDefaultRmsSpot(Optic optic)
    {
        return CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsSpot,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false));
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateDefaultRmsWavefront(Optic optic)
    {
        return CreateFromWizard(optic, new MeritFunctionWizardSettings(
            MeritImageQuality.RmsWavefront,
            MeritPupilSampling.GaussianQuadrature,
            PupilRings: 3,
            PupilArms: 6,
            PupilObscuration: 0,
            WeightScale: 1,
            UseAllWavelengths: true,
            IncludeCommonOperands: false));
    }

    public static IReadOnlyList<MeritOperandDefinition> CreateFromWizard(
        Optic optic,
        MeritFunctionWizardSettings settings)
    {
        var rings = Math.Clamp(settings.PupilRings, 1, 20);
        var arms = Math.Clamp(settings.PupilArms, 3, 36);
        var obscuration = Math.Clamp(settings.PupilObscuration, 0, 0.95);
        var weightScale = double.IsFinite(settings.WeightScale)
            ? Math.Max(0, settings.WeightScale)
            : 1;
        var samplingName = settings.PupilSampling == MeritPupilSampling.RectangularArray
            ? "uniform"
            : "gaussian_quad";
        var operands = new List<MeritOperandDefinition>
        {
            new() { Enabled = false, Type = "DMFS" },
            new()
            {
                Enabled = false,
                Type = "BLNK",
                Comment = $"序列评价函数：RMS {QualityName(settings.ImageQuality)}；" +
                          $"{ReferenceName(settings.Reference)}参考；{rings} 环 {arms} 臂"
            },
            new()
            {
                Enabled = false,
                Type = "BLNK",
                Comment = "由优化向导生成；各视场和波长权重已归一化。"
            }
        };

        var fieldWeights = NormalizeWeights(optic.Fields.Select(field => field.Weight).ToArray());
        var wavelengthIndices = settings.UseAllWavelengths
            ? Enumerable.Range(0, optic.Wavelengths.Count).ToArray()
            : new[] { PrimaryWavelengthIndex(optic) };
        var wavelengthWeights = NormalizeWeights(
            wavelengthIndices.Select(index => optic.Wavelengths[index].Weight).ToArray());
        var pupilPrototype = new MeritOperandDefinition
        {
            PupilRings = rings,
            PupilArms = arms,
            PupilObscuration = obscuration,
            PupilSampling = samplingName
        };
        var xWeight = Math.Max(0, double.IsFinite(settings.XWeight) ? settings.XWeight : 1);
        var yWeight = Math.Max(0, double.IsFinite(settings.YWeight) ? settings.YWeight : 1);
        if (settings.ImageQuality == MeritImageQuality.Contrast
            && (settings.SpatialFrequency <= 0 || (xWeight <= 0 && yWeight <= 0)))
        {
            throw new ArgumentException("对比度优化需要正的空间频率，并至少启用一个方向权重。", nameof(settings));
        }

        for (var fieldIndex = 0; fieldIndex < optic.Fields.Count; fieldIndex++)
        {
            var field = optic.Fields[fieldIndex];
            var normalizedField = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
            var pupilSamples = CreateWizardOperandPupilSamples(pupilPrototype, normalizedField);
            operands.Add(new MeritOperandDefinition
            {
                Enabled = false,
                Type = "BLNK",
                Comment = $"视场操作数 {fieldIndex + 1}：{field.Label}"
            });

            for (var wavelengthOffset = 0; wavelengthOffset < wavelengthIndices.Length; wavelengthOffset++)
            {
                var wavelengthIndex = wavelengthIndices[wavelengthOffset];
                var baseWeight = fieldWeights[fieldIndex] * wavelengthWeights[wavelengthOffset];
                foreach (var pupilSample in pupilSamples)
                {
                    if (settings.ImageQuality == MeritImageQuality.Contrast)
                    {
                        AddContrastOperands(
                            optic,
                            operands,
                            settings,
                            fieldIndex,
                            wavelengthIndex,
                            pupilSample,
                            baseWeight,
                            weightScale,
                            xWeight,
                            yWeight,
                            pupilPrototype);
                        continue;
                    }

                    var axisWeight = baseWeight * pupilSample.Weight;
                    if (settings.ImageQuality == MeritImageQuality.RmsWavefront)
                    {
                        operands.Add(CreateSampleOperand(
                            settings.Reference switch
                            {
                                MeritSpotReference.ChiefRay => "OPDM",
                                MeritSpotReference.Unreferenced => "OPDC",
                                _ => "OPDX"
                            },
                            fieldIndex,
                            wavelengthIndex,
                            pupilSample,
                            weightScale * axisWeight,
                            settings,
                            pupilPrototype));
                        continue;
                    }

                    AddRayAberrationOperands(
                        operands,
                        settings,
                        fieldIndex,
                        wavelengthIndex,
                        pupilSample,
                        axisWeight,
                        weightScale,
                        xWeight,
                        yWeight,
                        pupilPrototype);
                }
            }
        }

        if (settings.ImageQuality == MeritImageQuality.Contrast
            && !operands.Any(operand => operand.Type is "MECS" or "MECT"))
        {
            throw new InvalidOperationException("当前空间频率没有可用的 Moore-Elliott 移位光线对。");
        }

        AddCommonOperands(optic, operands, weightScale, settings.IncludeCommonOperands);
        return operands;
    }

    private static void AddRayAberrationOperands(
        ICollection<MeritOperandDefinition> operands,
        MeritFunctionWizardSettings settings,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double baseWeight,
        double weightScale,
        double xWeight,
        double yWeight,
        MeritOperandDefinition prototype)
    {
        var angular = settings.ImageQuality == MeritImageQuality.Angular;
        if (xWeight <= 0 && yWeight <= 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAR" : "ANAC"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAR" : "TRAC",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight,
                settings,
                prototype));
            return;
        }

        if (xWeight > 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAX" : "ANCX"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAX" : "TRCX",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * xWeight,
                settings,
                prototype));
        }

        if (yWeight > 0)
        {
            operands.Add(CreateSampleOperand(
                angular
                    ? settings.Reference == MeritSpotReference.ChiefRay ? "ANAY" : "ANCY"
                    : settings.Reference == MeritSpotReference.ChiefRay ? "TRAY" : "TRCY",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * yWeight,
                settings,
                prototype));
        }
    }

    private static void AddContrastOperands(
        Optic optic,
        ICollection<MeritOperandDefinition> operands,
        MeritFunctionWizardSettings settings,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double baseWeight,
        double weightScale,
        double xWeight,
        double yWeight,
        MeritOperandDefinition prototype)
    {
        var frequency = Math.Max(0, settings.SpatialFrequency);
        var cutoff = DiffractionCutoff(optic, optic.Wavelengths[wavelengthIndex]);
        var pupilShift = cutoff <= 1e-12 ? double.PositiveInfinity : 2 * frequency / cutoff;
        if (!double.IsFinite(pupilShift) || pupilShift > 2 + 1e-12)
        {
            return;
        }

        if (xWeight > 0 && PairFitsPupil(pupilSample.X, pupilSample.Y, pupilShift, sagittal: true))
        {
            var operand = CreateSampleOperand(
                "MECS",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * pupilSample.Weight * xWeight,
                settings,
                prototype);
            operand.SpatialFrequency = frequency;
            operands.Add(operand);
        }

        if (yWeight > 0 && PairFitsPupil(pupilSample.X, pupilSample.Y, pupilShift, sagittal: false))
        {
            var operand = CreateSampleOperand(
                "MECT",
                fieldIndex,
                wavelengthIndex,
                pupilSample,
                weightScale * baseWeight * pupilSample.Weight * yWeight,
                settings,
                prototype);
            operand.SpatialFrequency = frequency;
            operands.Add(operand);
        }
    }

    private static MeritOperandDefinition CreateSampleOperand(
        string type,
        int fieldIndex,
        int wavelengthIndex,
        Raytrace.PupilSample pupilSample,
        double weight,
        MeritFunctionWizardSettings settings,
        MeritOperandDefinition prototype)
    {
        return new MeritOperandDefinition
        {
            Type = type,
            Field = fieldIndex + 1,
            Wavelength = wavelengthIndex + 1,
            Px = pupilSample.X,
            Py = pupilSample.Y,
            Target = 0,
            Weight = weight,
            PupilRings = prototype.PupilRings,
            PupilArms = prototype.PupilArms,
            PupilObscuration = prototype.PupilObscuration,
            PupilSampling = prototype.PupilSampling,
            SpatialFrequency = settings.SpatialFrequency,
            IgnoreLateralColor = settings.IgnoreLateralColor,
            PolychromaticReference = settings.UseAllWavelengths && !settings.IgnoreLateralColor
        };
    }

    private static IReadOnlyList<Raytrace.PupilSample> NormalizePupilSamples(
        IReadOnlyList<Raytrace.PupilSample> samples)
    {
        var total = samples.Sum(sample => Math.Max(0, sample.Weight));
        if (total <= 1e-12)
        {
            return samples.Select(sample => sample with { Weight = 1.0 / Math.Max(1, samples.Count) }).ToArray();
        }

        return samples.Select(sample => sample with { Weight = Math.Max(0, sample.Weight) / total }).ToArray();
    }

    private static IReadOnlyList<Raytrace.PupilSample> CreateWizardOperandPupilSamples(
        MeritOperandDefinition prototype,
        (double X, double Y) normalizedField)
    {
        if (!string.Equals(prototype.PupilSampling, "gaussian_quad", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizePupilSamples(CreateWizardPupilSamples(prototype, 37));
        }

        var rings = Math.Clamp(prototype.PupilRings, 1, 20);
        var arms = Math.Clamp(prototype.PupilArms, 3, 36);
        var obscuration = Math.Clamp(prototype.PupilObscuration, 0, 0.95);
        var onAxis = Math.Abs(normalizedField.X) <= 1e-12 && Math.Abs(normalizedField.Y) <= 1e-12;
        var directionCount = onAxis ? 1 : Math.Max(1, arms / 2);
        return GaussianRadialSamples(rings, obscuration)
            .SelectMany(radialSample => Enumerable.Range(0, directionCount).Select(direction =>
            {
                var angle = onAxis
                    ? 0
                    : (((directionCount - 1) / 2.0) - direction) * (2 * Math.PI / arms);
                return new Raytrace.PupilSample(
                    radialSample.Radius * Math.Cos(angle),
                    radialSample.Radius * Math.Sin(angle),
                    radialSample.Weight / directionCount);
            }))
            .ToArray();
    }

    private static bool PairFitsPupil(double px, double py, double shift, bool sagittal)
    {
        var half = shift / 2;
        var firstX = sagittal ? px - half : px;
        var firstY = sagittal ? py : py - half;
        var secondX = sagittal ? px + half : px;
        var secondY = sagittal ? py : py + half;
        return ((firstX * firstX) + (firstY * firstY) <= 1 + 1e-12)
            && ((secondX * secondX) + (secondY * secondY) <= 1 + 1e-12);
    }

    private static int PrimaryWavelengthIndex(Optic optic)
    {
        var index = optic.Wavelengths
            .Select((wavelength, offset) => (wavelength, offset))
            .FirstOrDefault(item => item.wavelength.IsPrimary).offset;
        return Math.Clamp(index, 0, Math.Max(0, optic.Wavelengths.Count - 1));
    }

    private static double DiffractionCutoff(Optic optic, Wavelength wavelength)
    {
        var fNumber = Math.Abs(optic.Paraxial.EstimateFNumber());
        return fNumber <= 1e-12 || wavelength.Micrometers <= 1e-12
            ? 0
            : 1 / (wavelength.Micrometers * 1e-3 * fNumber);
    }

    private static string QualityName(MeritImageQuality quality) => quality switch
    {
        MeritImageQuality.RmsWavefront => "波前",
        MeritImageQuality.Contrast => "对比度",
        MeritImageQuality.Angular => "角向",
        _ => "点列图"
    };

    private static string ReferenceName(MeritSpotReference reference) => reference switch
    {
        MeritSpotReference.ChiefRay => "主光线",
        MeritSpotReference.Unreferenced => "无参考",
        _ => "质心"
    };

    private static IReadOnlyList<(double Radius, double Weight)> GaussianRadialSamples(
        int sampleCount,
        double obscuration)
    {
        var lower = obscuration * obscuration;
        var span = 1 - lower;
        var samples = new (double Radius, double Weight)[sampleCount];
        var rootsToFind = (sampleCount + 1) / 2;
        for (var rootIndex = 0; rootIndex < rootsToFind; rootIndex++)
        {
            var root = Math.Cos(Math.PI * (rootIndex + 0.75) / (sampleCount + 0.5));
            double derivative;
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var previous = 1.0;
                var current = root;
                for (var order = 2; order <= sampleCount; order++)
                {
                    var next = (((2 * order) - 1) * root * current - ((order - 1) * previous)) / order;
                    previous = current;
                    current = next;
                }

                derivative = sampleCount * ((root * current) - previous) / ((root * root) - 1);
                var nextRoot = root - (current / derivative);
                if (Math.Abs(nextRoot - root) <= 1e-15)
                {
                    root = nextRoot;
                    break;
                }

                root = nextRoot;
            }

            var p0 = 1.0;
            var p1 = root;
            for (var order = 2; order <= sampleCount; order++)
            {
                var next = (((2 * order) - 1) * root * p1 - ((order - 1) * p0)) / order;
                p0 = p1;
                p1 = next;
            }

            derivative = sampleCount * ((root * p1) - p0) / ((root * root) - 1);
            var legendreWeight = 2 / ((1 - (root * root)) * derivative * derivative);
            SetRadialSample(rootIndex, -root, legendreWeight);
            SetRadialSample(sampleCount - rootIndex - 1, root, legendreWeight);
        }

        return samples;

        void SetRadialSample(int index, double node, double legendreWeight)
        {
            var normalizedRadiusSquared = (node + 1) / 2;
            samples[index] = (
                Math.Sqrt(lower + (span * normalizedRadiusSquared)),
                span * legendreWeight / 2);
        }
    }

    private static void AddCommonOperands(
        Optic optic,
        ICollection<MeritOperandDefinition> operands,
        double weightScale,
        bool includeCommonOperands)
    {
        if (!includeCommonOperands)
        {
            return;
        }

        operands.Add(new MeritOperandDefinition
        {
            Type = "EFFL",
            Target = optic.Paraxial.EstimateEffectiveFocalLength(),
            Weight = weightScale,
            Comment = "保持当前有效焦距"
        });
        operands.Add(new MeritOperandDefinition
        {
            Type = "FNUM",
            Target = optic.Paraxial.EstimateFNumber(),
            Weight = weightScale,
            Comment = "保持当前 F 数"
        });
    }

    public static string CanonicalType(string? type)
    {
        var canonical = (type ?? string.Empty).Trim().ToUpperInvariant();
        if (Types.Any(item => item.Code == canonical))
        {
            return canonical;
        }

        throw new ArgumentException(
            $"Unknown merit operand type '{type}'.",
            nameof(type));
    }

    public static bool HasOpaqueZemaxParameters(string? type) =>
        ZemaxOperandRegistry.TryGet(type, out var descriptor)
        && descriptor.SupportLevel == ZemaxOperandSupportLevel.CompatibilityOnly;

    private static double EvaluateValue(
        Optic optic,
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context)
    {
        return CanonicalType(definition.Type) switch
        {
            "CONS" => definition.Target,
            "SINE" => EvaluateForwardTrigonometricRowMath(definition, context, Math.Sin),
            "COSI" => EvaluateForwardTrigonometricRowMath(definition, context, Math.Cos),
            "TANG" => EvaluateForwardTrigonometricRowMath(definition, context, Math.Tan),
            "ASIN" => EvaluateInverseTrigonometricRowMath(definition, context, value =>
                value is < -1 or > 1
                    ? throw new ArithmeticException("ASIN 的输入必须在 [-1, 1] 内。")
                    : Math.Asin(value)),
            "ACOS" => EvaluateInverseTrigonometricRowMath(definition, context, value =>
                value is < -1 or > 1
                    ? throw new ArithmeticException("ACOS 的输入必须在 [-1, 1] 内。")
                    : Math.Acos(value)),
            "ATAN" => EvaluateInverseTrigonometricRowMath(definition, context, Math.Atan),
            "ABSO" => EvaluateUnaryRowMath(definition, context, Math.Abs),
            "SQRT" => EvaluateUnaryRowMath(definition, context, value =>
                value < 0
                    ? throw new ArithmeticException("SQRT 的输入不能为负数。")
                    : Math.Sqrt(value)),
            "RECI" => EvaluateUnaryRowMath(definition, context, value =>
                Math.Abs(value) <= 1e-30
                    ? throw new DivideByZeroException("RECI 的输入不能为 0。")
                    : 1 / value),
            "LOGE" => EvaluateUnaryRowMath(definition, context, value =>
                value <= 0 ? 0 : Math.Log(value)),
            "LOGT" => EvaluateUnaryRowMath(definition, context, value =>
                value <= 0 ? 0 : Math.Log10(value)),
            "SUMM" => EvaluateBinaryRowMath(definition, context, (first, second) => first + second),
            "PROD" => EvaluateBinaryRowMath(definition, context, (first, second) => first * second),
            "PROB" => EvaluateScaledRowMath(definition, context, (value, factor) => value * factor),
            "DIVB" => EvaluateScaledRowMath(definition, context, (value, factor) =>
                Math.Abs(factor) <= 1e-30
                    ? throw new DivideByZeroException("DIVB 的 Factor 不能为 0。")
                    : value / factor),
            "EQUA" => EvaluateEqualityRowMath(definition, context),
            "OSUM" => EvaluateRowRangeMath(definition, context, values => values.Sum()),
            "QSUM" => EvaluateRowRangeMath(definition, context, values =>
                Math.Sqrt(values.Sum(value => value * value))),
            "MAXX" => EvaluateRowRangeMath(definition, context, values => values.Max()),
            "MINN" => EvaluateRowRangeMath(definition, context, values => values.Min()),
            "DIFF" => EvaluateBinaryRowMath(definition, context, (first, second) => first - second),
            "DIVI" => EvaluateBinaryRowMath(definition, context, (first, second) =>
                Math.Abs(second) <= 1e-30
                    ? throw new DivideByZeroException("DIVI 的分母不能为 0。")
                    : first / second),
            "OPVA" => EvaluateOperandValue(definition, context),
            "OPGT" => BoundaryGreaterThanOrEqual(EvaluateOperandValue(definition, context), definition.Target),
            "OPLT" => BoundaryLessThanOrEqual(EvaluateOperandValue(definition, context), definition.Target),
            "ABGT" => BoundaryGreaterThanOrEqual(Math.Abs(EvaluateOperandValue(definition, context)), definition.Target),
            "ABLT" => BoundaryLessThanOrEqual(Math.Abs(EvaluateOperandValue(definition, context)), definition.Target),
            "RSCE" => EvaluateRmsSpot(optic, definition),
            "RSCH" => EvaluateRmsSpot(optic, definition),
            "RSRE" => EvaluateRmsSpot(optic, definition),
            "RSRH" => EvaluateRmsSpot(optic, definition),
            "RWFE" => EvaluateRmsWavefront(optic, definition),
            "OPDX" => EvaluateOpticalPathDifference(optic, definition),
            "OPDM" => EvaluateOpticalPathDifference(optic, definition),
            "OPDC" => EvaluateOpticalPathDifference(optic, definition),
            "TRAC" => EvaluateRayAberration(optic, definition),
            "TRAR" => EvaluateRayAberration(optic, definition),
            "TRCX" => EvaluateRayAberration(optic, definition),
            "TRCY" => EvaluateRayAberration(optic, definition),
            "TRAX" => EvaluateRayAberration(optic, definition),
            "TRAY" => EvaluateRayAberration(optic, definition),
            "ANAC" => EvaluateAngularAberration(optic, definition),
            "ANAR" => EvaluateAngularAberration(optic, definition),
            "ANCX" => EvaluateAngularAberration(optic, definition),
            "ANCY" => EvaluateAngularAberration(optic, definition),
            "ANAX" => EvaluateAngularAberration(optic, definition),
            "ANAY" => EvaluateAngularAberration(optic, definition),
            "MECS" => EvaluateMooreElliottDifference(optic, definition, sagittal: true),
            "MECT" => EvaluateMooreElliottDifference(optic, definition, sagittal: false),
            "REAX" => SampleAtSurface(optic, definition).Position.X,
            "REAY" => SampleAtSurface(optic, definition).Position.Y,
            "REAR" => EvaluateRealRayRadius(optic, definition),
            "RANG" => EvaluateRealRayAngle(optic, definition),
            "EFFL" => optic.Paraxial.EstimateEffectiveFocalLength(),
            "EFLX" => EvaluateEffectiveFocalLengthBetweenSurfaces(optic, definition),
            "EFLY" => EvaluateEffectiveFocalLengthBetweenSurfaces(optic, definition),
            "ENPP" => optic.Paraxial.EstimateEntrancePupilLocation(),
            "EPDI" => optic.Paraxial.EstimateEntrancePupilDiameter(),
            "EXPP" => optic.Paraxial.EstimateExitPupilLocation(),
            "EXPD" => optic.Paraxial.EstimateExitPupilDiameter(),
            "ISFN" => optic.Paraxial.EstimateFNumber(),
            "SFNO" => optic.Paraxial.EstimateFNumber(),
            "WFNO" => optic.Paraxial.EstimateFNumber(),
            "ISNA" => EvaluateImageSpaceNumericalAperture(optic, definition),
            "WLEN" => EvaluateWavelengthMicrometers(optic, definition),
            "INDX" => EvaluateRefractiveIndex(optic, definition),
            "MNIN" => BoundaryGreaterThanOrEqual(
                EvaluateGlassIndexExtreme(optic, definition, maximum: false), definition.Target),
            "MXIN" => BoundaryLessThanOrEqual(
                EvaluateGlassIndexExtreme(optic, definition, maximum: true), definition.Target),
            "MNAB" => BoundaryGreaterThanOrEqual(
                EvaluateGlassAbbeExtreme(optic, definition, maximum: false), definition.Target),
            "MXAB" => BoundaryLessThanOrEqual(
                EvaluateGlassAbbeExtreme(optic, definition, maximum: true), definition.Target),
            "POWR" => EvaluateSurfacePower(optic, definition),
            "FNUM" => optic.Paraxial.EstimateFNumber(),
            "TOTR" => optic.SurfaceGroup.TotalTrack,
            "TTGT" => BoundaryGreaterThanOrEqual(EvaluateDirectedEdgeThickness(optic, definition), definition.Target),
            "TTLT" => BoundaryLessThanOrEqual(EvaluateDirectedEdgeThickness(optic, definition), definition.Target),
            "TTVA" => EvaluateDirectedEdgeThickness(optic, definition),
            "TTHI" => EvaluateRangeThickness(optic, definition),
            "TGTH" => EvaluateGlassThicknessSum(optic, definition),
            "CTGT" => BoundaryGreaterThanOrEqual(EvaluateCenterThickness(optic, definition), definition.Target),
            "CTLT" => BoundaryLessThanOrEqual(EvaluateCenterThickness(optic, definition), definition.Target),
            "CTVA" => EvaluateCenterThickness(optic, definition),
            "ETGT" => BoundaryGreaterThanOrEqual(EvaluateDirectedEdgeThickness(optic, definition), definition.Target),
            "ETLT" => BoundaryLessThanOrEqual(EvaluateDirectedEdgeThickness(optic, definition), definition.Target),
            "ETVA" => EvaluateDirectedEdgeThickness(optic, definition),
            "FTGT" => BoundaryGreaterThanOrEqual(EvaluateFullThicknessExtreme(optic, definition, maximum: false), definition.Target),
            "FTLT" => BoundaryLessThanOrEqual(EvaluateFullThicknessExtreme(optic, definition, maximum: true), definition.Target),
            "STHI" => EvaluateThicknessAtCoordinate(optic, definition),
            "CVGT" => BoundaryGreaterThanOrEqual(EvaluateSurfaceCurvature(optic, definition), definition.Target),
            "CVLT" => BoundaryLessThanOrEqual(EvaluateSurfaceCurvature(optic, definition), definition.Target),
            "CVVA" => EvaluateSurfaceCurvature(optic, definition),
            "MNCV" => BoundaryGreaterThanOrEqual(EvaluateRangeScalarExtreme(
                optic, definition, EvaluateSurfaceCurvature, maximum: false), definition.Target),
            "MXCV" => BoundaryLessThanOrEqual(EvaluateRangeScalarExtreme(
                optic, definition, EvaluateSurfaceCurvature, maximum: true), definition.Target),
            "COGT" => BoundaryGreaterThanOrEqual(EvaluateSurfaceConic(optic, definition), definition.Target),
            "COLT" => BoundaryLessThanOrEqual(EvaluateSurfaceConic(optic, definition), definition.Target),
            "COVA" => EvaluateSurfaceConic(optic, definition),
            "MNSD" => BoundaryGreaterThanOrEqual(EvaluateRangeScalarExtreme(
                optic, definition, EvaluateSurfaceSemiDiameter, maximum: false), definition.Target),
            "MXSD" => BoundaryLessThanOrEqual(EvaluateRangeScalarExtreme(
                optic, definition, EvaluateSurfaceSemiDiameter, maximum: true), definition.Target),
            "MNCA" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: false, perimeter: false, maximum: false), definition.Target),
            "MXCA" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: false, perimeter: false, maximum: true), definition.Target),
            "MNEA" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: true, perimeter: false, maximum: false), definition.Target),
            "MXEA" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: true, perimeter: false, maximum: true), definition.Target),
            "MNCG" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: false, perimeter: false, maximum: false), definition.Target),
            "MXCG" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: false, perimeter: false, maximum: true), definition.Target),
            "MNEG" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: true, perimeter: false, maximum: false), definition.Target),
            "MXEG" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: true, perimeter: false, maximum: true), definition.Target),
            "MNCT" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: false, perimeter: false, maximum: false), definition.Target),
            "MXCT" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: false, perimeter: false, maximum: true), definition.Target),
            "MNET" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: true, perimeter: false, maximum: false), definition.Target),
            "MXET" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: true, perimeter: false, maximum: true), definition.Target),
            "XNEA" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: true, perimeter: true, maximum: false), definition.Target),
            "XXEA" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Air, edge: true, perimeter: true, maximum: true), definition.Target),
            "XNEG" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: true, perimeter: true, maximum: false), definition.Target),
            "XXEG" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Glass, edge: true, perimeter: true, maximum: true), definition.Target),
            "XNET" => BoundaryGreaterThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: true, perimeter: true, maximum: false), definition.Target),
            "XXET" => BoundaryLessThanOrEqual(EvaluateThicknessExtreme(
                optic, definition, ThicknessMaterialFilter.Any, edge: true, perimeter: true, maximum: true), definition.Target),
            "PMAG" => EvaluateParaxialMagnification(optic, definition),
            "PETZ" => EvaluatePetzvalRadius(optic, definition),
            "DIMX" => BoundaryLessThanOrEqual(EvaluateMaximumDistortion(optic, definition), definition.Target),
            "RADI" => ResolveSurface(optic, definition.Surface).Radius,
            "THIC" => ResolveSurface(optic, definition.Surface).Thickness,
            _ => throw new NotSupportedException(
                $"Merit operand '{CanonicalType(definition.Type)}' is not executable.")
        };
    }

    private static double EvaluateUnaryRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<double, double> operation)
    {
        return operation(RequiredOrderedContext(context).RowValue(
            ZemaxIntegerParameter(definition, 0, definition.Surface)));
    }

    private static double EvaluateForwardTrigonometricRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<double, double> operation)
    {
        var value = RequiredOrderedContext(context).RowValue(
            ZemaxIntegerParameter(definition, 0, definition.Surface));
        if (UsesDegreeFlag(definition))
        {
            value *= Math.PI / 180.0;
        }

        return operation(value);
    }

    private static double EvaluateInverseTrigonometricRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<double, double> operation)
    {
        var radians = operation(RequiredOrderedContext(context).RowValue(
            ZemaxIntegerParameter(definition, 0, definition.Surface)));
        return UsesDegreeFlag(definition)
            ? radians * 180.0 / Math.PI
            : radians;
    }

    private static bool UsesDegreeFlag(MeritOperandDefinition definition) =>
        ZemaxIntegerParameter(definition, 1, definition.Wavelength) != 0;

    private static double EvaluateBinaryRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<double, double, double> operation)
    {
        var ordered = RequiredOrderedContext(context);
        var first = ordered.RowValue(ZemaxIntegerParameter(definition, 0, definition.Surface));
        var second = ordered.RowValue(ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        return operation(first, second);
    }

    private static double EvaluateScaledRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<double, double, double> operation)
    {
        var value = RequiredOrderedContext(context).RowValue(
            ZemaxIntegerParameter(definition, 0, definition.Surface));
        var factor = ZemaxDataParameter(definition, 0, definition.Hx);
        if (!double.IsFinite(factor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.Hx),
                factor,
                "Zemax Factor 必须是有限数值。");
        }

        return operation(value, factor);
    }

    private static double EvaluateEqualityRowMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context)
    {
        if (!double.IsFinite(definition.Target) || definition.Target < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition.Target),
                definition.Target,
                "EQUA 的 Target 是相等公差，必须是非负有限数值。");
        }

        var values = RequiredRowRangeValues(definition, context);
        var mean = values.Average();
        var errorSum = 0.0;
        foreach (var value in values)
        {
            var absoluteError = Math.Abs(value - mean);
            if (absoluteError > definition.Target)
            {
                errorSum += absoluteError;
            }
        }

        return errorSum;
    }

    private static double EvaluateRowRangeMath(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context,
        Func<IReadOnlyList<double>, double> operation)
    {
        return operation(RequiredRowRangeValues(definition, context));
    }

    private static double[] RequiredRowRangeValues(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context)
    {
        var ordered = RequiredOrderedContext(context);
        var firstRow = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var lastRow = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (lastRow <= 0)
        {
            lastRow = firstRow;
        }

        var values = ordered.RowRangeValues(firstRow, lastRow);
        if (values.Length == 0)
        {
            throw new InvalidOperationException("Zemax 行范围没有可计算输入。");
        }

        return values;
    }

    private static OrderedMeritEvaluationContext RequiredOrderedContext(
        OrderedMeritEvaluationContext? context)
    {
        return context
            ?? throw new InvalidOperationException("该 Zemax 数学操作数必须通过有序评价函数入口计算。");
    }

    private static double EvaluateOperandValue(
        MeritOperandDefinition definition,
        OrderedMeritEvaluationContext? context)
    {
        return RequiredOrderedContext(context).RowValue(
            ZemaxIntegerParameter(definition, 0, definition.Surface));
    }

    private static double EvaluateRealRayRadius(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        return Math.Sqrt((sample.Position.X * sample.Position.X) + (sample.Position.Y * sample.Position.Y));
    }

    private static double EvaluateRealRayAngle(Optic optic, MeritOperandDefinition definition)
    {
        var direction = SampleAtSurface(optic, definition).Direction;
        var transverse = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
        var axial = Math.Abs(direction.Z);
        if (!double.IsFinite(transverse) || !double.IsFinite(axial))
        {
            throw new InvalidOperationException("实际光线方向余弦不是有限数值。");
        }

        return Math.Atan2(transverse, axial);
    }

    private static double BoundaryGreaterThanOrEqual(double value, double target)
    {
        if (!double.IsFinite(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "边界目标值必须是有限数值。");
        }

        return value >= target ? target : value;
    }

    private static double BoundaryLessThanOrEqual(double value, double target)
    {
        if (!double.IsFinite(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "边界目标值必须是有限数值。");
        }

        return value <= target ? target : value;
    }

    private enum ThicknessMaterialFilter
    {
        Any,
        Air,
        Glass
    }

    private static double EvaluateEffectiveFocalLengthBetweenSurfaces(
        Optic optic,
        MeritOperandDefinition definition)
    {
        var startSurface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var endSurface = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        return optic.Paraxial.EstimateEffectiveFocalLengthBetweenSurfaces(startSurface, endSurface);
    }

    private static double EvaluateWavelengthMicrometers(Optic optic, MeritOperandDefinition definition)
    {
        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        return wavelength.Micrometers;
    }

    private static double EvaluateRefractiveIndex(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var index = surface.MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        if (!double.IsFinite(index))
        {
            throw new InvalidOperationException($"表面 {surface.Number} 后材料折射率不是有限数值。");
        }

        return index;
    }

    private static double EvaluateGlassIndexExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        bool maximum)
    {
        return EvaluateGlassDataExtreme(
            optic,
            definition,
            GlassIndexD,
            quantityName: "Nd",
            maximum);
    }

    private static double EvaluateGlassAbbeExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        bool maximum)
    {
        return EvaluateGlassDataExtreme(
            optic,
            definition,
            GlassAbbeNumber,
            quantityName: "Vd",
            maximum);
    }

    private static double EvaluateGlassDataExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        Func<IMaterial, double?> selector,
        string quantityName,
        bool maximum)
    {
        var extreme = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        var found = false;
        foreach (var surface in SurfaceRange(optic, definition, includeEndSurface: true))
        {
            if (surface.Number == 0 && ObjectConjugate.IsInfinite(surface))
            {
                continue;
            }

            if (!MatchesThicknessMaterialFilter(
                surface,
                FraunhoferDLineNanometers,
                ThicknessMaterialFilter.Glass))
            {
                continue;
            }

            var value = selector(surface.MaterialAfter);
            if (value is not { } actual || !double.IsFinite(actual))
            {
                throw new InvalidOperationException(
                    $"表面 {surface.Number} 后玻璃材料没有可计算的 {quantityName} 数据。");
            }

            extreme = maximum ? Math.Max(extreme, actual) : Math.Min(extreme, actual);
            found = true;
        }

        if (!found || !double.IsFinite(extreme))
        {
            throw new InvalidOperationException($"指定范围内没有可计算的玻璃 {quantityName} 数据。");
        }

        return extreme;
    }

    private static double? GlassIndexD(IMaterial material)
    {
        var index = material switch
        {
            AbbeMaterial abbe => abbe.Nd,
            CatalogGlassMaterial { ZemaxData.ReferenceIndexD: > 0 } catalog => catalog.ZemaxData!.ReferenceIndexD,
            _ => material.RefractiveIndex(FraunhoferDLineNanometers)
        };
        return double.IsFinite(index) && index > 0 ? index : null;
    }

    private static double? GlassAbbeNumber(IMaterial material)
    {
        var abbe = material switch
        {
            AbbeMaterial model => model.Vd,
            CatalogGlassMaterial { ZemaxData.ReferenceAbbeNumber: > 0 } catalog =>
                catalog.ZemaxData!.ReferenceAbbeNumber,
            _ => CalculatedAbbeNumber(material)
        };
        return abbe is { } value && double.IsFinite(value) && value > 0 ? value : null;
    }

    private static double? CalculatedAbbeNumber(IMaterial material)
    {
        var nd = material.RefractiveIndex(FraunhoferDLineNanometers);
        var nF = material.RefractiveIndex(FraunhoferFLineNanometers);
        var nC = material.RefractiveIndex(FraunhoferCLineNanometers);
        var denominator = nF - nC;
        if (!double.IsFinite(nd)
            || !double.IsFinite(nF)
            || !double.IsFinite(nC)
            || denominator <= 1e-12)
        {
            return null;
        }

        return (nd - 1.0) / denominator;
    }

    private static double EvaluateSurfacePower(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        if (surface.Geometry is not (PlaneGeometry or StandardGeometry))
        {
            throw new InvalidOperationException("POWR 只适用于 Zemax 标准面。");
        }

        if (surface.IsReflective)
        {
            throw new InvalidOperationException("POWR 当前只支持折射标准面，反射面不会用 0 值代替。");
        }

        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var nBefore = surface.MaterialBefore.RefractiveIndex(wavelength.Nanometers);
        var nAfter = surface.MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        if (!double.IsFinite(nBefore) || !double.IsFinite(nAfter))
        {
            throw new InvalidOperationException("POWR 所需的表面前后折射率不是有限数值。");
        }

        if (surface.IsPlane || Math.Abs(surface.Radius) <= 1e-15)
        {
            return 0.0;
        }

        return (nAfter - nBefore) / surface.Radius;
    }

    private static double EvaluateCenterThickness(Optic optic, MeritOperandDefinition definition)
    {
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var surface = ResolveSurface(optic, surfaceNumber);
        if (!double.IsFinite(surface.Thickness))
        {
            throw new InvalidOperationException("中心厚度不是有限数值。");
        }

        return surface.Thickness;
    }

    private static double EvaluateDirectedEdgeThickness(Optic optic, MeritOperandDefinition definition)
    {
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var surface = ResolveSurface(optic, surfaceNumber);
        var nextSurface = ResolveNextSurface(optic, surfaceNumber);
        var edgeCode = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        return EdgeThicknessAtDirection(surface, nextSurface, edgeCode, zone: 1.0);
    }

    private static double EvaluateFullThicknessExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        bool maximum)
    {
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var surface = ResolveSurface(optic, surfaceNumber);
        var nextSurface = ResolveNextSurface(optic, surfaceNumber);
        if (!double.IsFinite(surface.SemiDiameter) || surface.SemiDiameter <= 0)
        {
            throw new InvalidOperationException("全口径厚度所在表面的半口径不是有效正数。");
        }

        const int sampleCount = 200;
        var extreme = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        for (var index = 0; index <= sampleCount; index++)
        {
            var y = surface.SemiDiameter * index / sampleCount;
            var value = ThicknessAtCoordinate(surface, nextSurface, 0, y);
            extreme = maximum ? Math.Max(extreme, value) : Math.Min(extreme, value);
        }

        return extreme;
    }

    private static double EvaluateThicknessAtCoordinate(Optic optic, MeritOperandDefinition definition)
    {
        var surfaceNumber = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var surface = ResolveSurface(optic, surfaceNumber);
        var nextSurface = ResolveNextSurface(optic, surfaceNumber);
        return ThicknessAtCoordinate(
            surface,
            nextSurface,
            ZemaxDataParameter(definition, 0, definition.Hx),
            ZemaxDataParameter(definition, 1, definition.Hy));
    }

    private static double EvaluateSurfaceCurvature(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        return surface.IsPlane ? 0.0 : 1.0 / surface.Radius;
    }

    private static double EvaluateSurfaceConic(Optic optic, MeritOperandDefinition definition)
    {
        return ResolveSurface(
            optic,
            ZemaxIntegerParameter(definition, 0, definition.Surface)).Conic;
    }

    private static double EvaluateSurfaceSemiDiameter(Optic optic, MeritOperandDefinition definition)
    {
        var surface = ResolveSurface(optic, ZemaxIntegerParameter(definition, 0, definition.Surface));
        if (!double.IsFinite(surface.SemiDiameter) || surface.SemiDiameter < 0)
        {
            throw new InvalidOperationException("表面半口径不是有限非负数值。");
        }

        return surface.SemiDiameter;
    }

    private static double EvaluateRangeScalarExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        Func<Optic, MeritOperandDefinition, double> evaluator,
        bool maximum)
    {
        var extreme = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        var found = false;
        foreach (var surface in SurfaceRange(optic, definition, includeEndSurface: true))
        {
            var localDefinition = definition.Clone();
            localDefinition.Surface = surface.Number;
            localDefinition.ZemaxIntegerParameters = [surface.Number, 0];
            var value = evaluator(
                optic,
                localDefinition);
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("范围标量计算得到非有限数值。");
            }

            extreme = maximum ? Math.Max(extreme, value) : Math.Min(extreme, value);
            found = true;
        }

        if (!found || !double.IsFinite(extreme))
        {
            throw new InvalidOperationException("指定范围内没有可计算的表面。");
        }

        return extreme;
    }

    private static double EvaluateThicknessExtreme(
        Optic optic,
        MeritOperandDefinition definition,
        ThicknessMaterialFilter materialFilter,
        bool edge,
        bool perimeter,
        bool maximum)
    {
        var wavelength = ResolveWavelength(optic, 0).Nanometers;
        var extreme = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        var found = false;
        foreach (var surface in SurfaceRange(optic, definition, includeEndSurface: true))
        {
            if (surface.Number == 0 && ObjectConjugate.IsInfinite(surface))
            {
                continue;
            }

            if (!MatchesThicknessMaterialFilter(surface, wavelength, materialFilter))
            {
                continue;
            }

            var values = edge
                ? ThicknessEdgeSamples(optic, surface, definition, perimeter)
                : [CenterThickness(surface)];
            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                {
                    throw new InvalidOperationException("厚度计算得到非有限数值。");
                }

                extreme = maximum
                    ? Math.Max(extreme, value)
                    : Math.Min(extreme, value);
                found = true;
            }
        }

        if (!found || !double.IsFinite(extreme))
        {
            var materialName = materialFilter switch
            {
                ThicknessMaterialFilter.Air => "空气",
                ThicknessMaterialFilter.Glass => "玻璃",
                _ => "任意介质"
            };
            var thicknessName = edge ? "边厚" : "中心厚度";
            throw new InvalidOperationException($"指定范围内没有可计算的{materialName}{thicknessName}。");
        }

        return extreme;
    }

    private static double EvaluateGlassThicknessSum(Optic optic, MeritOperandDefinition definition)
    {
        var wavelength = ResolveWavelength(optic, 0).Nanometers;
        var thickness = 0.0;
        var found = false;
        foreach (var surface in SurfaceRange(optic, definition, includeEndSurface: false))
        {
            if (surface.Number == 0 && ObjectConjugate.IsInfinite(surface))
            {
                continue;
            }

            if (!MatchesThicknessMaterialFilter(surface, wavelength, ThicknessMaterialFilter.Glass))
            {
                continue;
            }

            thickness += CenterThickness(surface);
            found = true;
        }

        if (!found)
        {
            throw new InvalidOperationException("指定范围内没有可计算的玻璃中心厚度。");
        }

        return thickness;
    }

    private static IReadOnlyList<double> ThicknessEdgeSamples(
        Optic optic,
        OpticalSurface surface,
        MeritOperandDefinition definition,
        bool perimeter)
    {
        var nextSurface = ResolveNextSurface(optic, surface.Number);
        var zone = ZemaxDataParameter(definition, 0, definition.Hx);
        if (Math.Abs(zone) <= 1e-15)
        {
            zone = 1.0;
        }

        if (!double.IsFinite(zone) || zone <= 0 || zone > 1)
        {
            throw new InvalidOperationException("边厚 Zone 必须在 (0, 1] 范围内。");
        }

        if (!perimeter)
        {
            return [EdgeThicknessAtDirection(surface, nextSurface, edgeCode: 0, zone)];
        }

        const int sampleCount = 64;
        return Enumerable.Range(0, sampleCount)
            .Select(index => EdgeThicknessAtAngle(
                surface,
                nextSurface,
                2.0 * Math.PI * index / sampleCount,
                zone))
            .ToArray();
    }

    private static bool MatchesThicknessMaterialFilter(
        OpticalSurface surface,
        double wavelengthNanometers,
        ThicknessMaterialFilter materialFilter)
    {
        return materialFilter switch
        {
            ThicknessMaterialFilter.Any => !surface.IsReflective,
            ThicknessMaterialFilter.Air => IsAirSpace(surface, wavelengthNanometers),
            ThicknessMaterialFilter.Glass => IsGlassSpace(surface, wavelengthNanometers),
            _ => false
        };
    }

    private static OpticalSurface ResolveNextSurface(Optic optic, int surfaceNumber)
    {
        var surfaces = optic.SurfaceGroup.Items;
        var index = surfaces.ToList().FindIndex(surface => surface.Number == surfaceNumber);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), surfaceNumber, "找不到指定表面。");
        }

        if (index >= surfaces.Count - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceNumber), surfaceNumber, "指定表面之后没有下一表面。");
        }

        return surfaces[index + 1];
    }

    private static IReadOnlyList<OpticalSurface> SurfaceRange(
        Optic optic,
        MeritOperandDefinition definition,
        bool includeEndSurface)
    {
        var startSurface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var endSurface = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (endSurface <= 0)
        {
            endSurface = Math.Max(0, optic.SurfaceGroup.Items.Count - 2);
        }

        if (startSurface < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), startSurface, "起始表面不能为负数。");
        }

        if (endSurface < startSurface)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), endSurface, "范围终点不能小于起点。");
        }

        var surfacesByNumber = optic.SurfaceGroup.Items.ToDictionary(surface => surface.Number);
        if (!surfacesByNumber.ContainsKey(startSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), startSurface, "找不到范围起始表面。");
        }

        if (!surfacesByNumber.ContainsKey(endSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), endSurface, "找不到范围终止表面。");
        }

        var lastSurface = includeEndSurface ? endSurface : endSurface - 1;
        if (lastSurface < startSurface)
        {
            return [];
        }

        var range = new List<OpticalSurface>(lastSurface - startSurface + 1);
        for (var surfaceNumber = startSurface; surfaceNumber <= lastSurface; surfaceNumber++)
        {
            if (!surfacesByNumber.TryGetValue(surfaceNumber, out var surface))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(definition),
                    surfaceNumber,
                    "范围中的表面编号不连续。");
            }

            range.Add(surface);
        }

        return range;
    }

    private static double EdgeThicknessAtDirection(
        OpticalSurface surface,
        OpticalSurface nextSurface,
        int edgeCode,
        double zone)
    {
        var angle = edgeCode switch
        {
            0 => Math.PI / 2.0,
            1 => 0.0,
            2 => -Math.PI / 2.0,
            3 => Math.PI,
            _ => throw new ArgumentOutOfRangeException(
                nameof(edgeCode),
                edgeCode,
                "边缘方向代码必须是 0(+Y)、1(+X)、2(-Y) 或 3(-X)。")
        };
        return EdgeThicknessAtAngle(surface, nextSurface, angle, zone);
    }

    private static double EdgeThicknessAtAngle(
        OpticalSurface surface,
        OpticalSurface nextSurface,
        double angle,
        double zone)
    {
        if (!double.IsFinite(surface.Thickness))
        {
            throw new InvalidOperationException("厚度所在空间的中心厚度不是有限数值。");
        }

        if (!double.IsFinite(surface.SemiDiameter)
            || surface.SemiDiameter <= 0
            || !double.IsFinite(nextSurface.SemiDiameter)
            || nextSurface.SemiDiameter <= 0)
        {
            throw new InvalidOperationException("边厚相邻表面的半口径不是有效正数。");
        }

        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var currentRadius = surface.SemiDiameter * zone;
        var nextRadius = nextSurface.SemiDiameter * zone;
        var currentSag = surface.Geometry.Sag(currentRadius * cosine, currentRadius * sine);
        var nextSag = nextSurface.Geometry.Sag(nextRadius * cosine, nextRadius * sine);
        if (!double.IsFinite(currentSag) || !double.IsFinite(nextSag))
        {
            throw new InvalidOperationException("边厚所在表面的 sag 不是有限数值。");
        }

        var thickness = surface.Thickness + nextSag - currentSag;
        return surface.IsReflective ? Math.Abs(thickness) : thickness;
    }

    private static double ThicknessAtCoordinate(
        OpticalSurface surface,
        OpticalSurface nextSurface,
        double x,
        double y)
    {
        if (!double.IsFinite(surface.Thickness))
        {
            throw new InvalidOperationException("厚度所在空间的中心厚度不是有限数值。");
        }

        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new InvalidOperationException("厚度采样坐标必须是有限数值。");
        }

        var currentSag = surface.Geometry.Sag(x, y);
        var nextSag = nextSurface.Geometry.Sag(x, y);
        if (!double.IsFinite(currentSag) || !double.IsFinite(nextSag))
        {
            throw new InvalidOperationException("厚度所在表面的 sag 不是有限数值。");
        }

        var thickness = surface.Thickness + nextSag - currentSag;
        return surface.IsReflective ? Math.Abs(thickness) : thickness;
    }

    private static bool IsGlassSpace(OpticalSurface surface, double wavelengthNanometers)
    {
        if (surface.IsReflective)
        {
            return false;
        }

        var material = surface.MaterialAfter;
        var name = material.Name.Trim();
        if (name.Equals("Air", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Vacuum", StringComparison.OrdinalIgnoreCase)
            || name.Equals("None", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = material.RefractiveIndex(wavelengthNanometers);
        if (!double.IsFinite(index))
        {
            throw new InvalidOperationException($"材料 {material.Name} 的折射率不是有限数值。");
        }

        return Math.Abs(index - 1.0) > 1e-9
            || material.GetType().Name.Contains("Glass", StringComparison.Ordinal);
    }

    private static bool IsAirSpace(OpticalSurface surface, double wavelengthNanometers)
    {
        if (surface.IsReflective)
        {
            return false;
        }

        var material = surface.MaterialAfter;
        var name = material.Name.Trim();
        if (name.Equals("Air", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Vacuum", StringComparison.OrdinalIgnoreCase)
            || name.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var index = material.RefractiveIndex(wavelengthNanometers);
        if (!double.IsFinite(index))
        {
            throw new InvalidOperationException($"材料 {material.Name} 的折射率不是有限数值。");
        }

        return Math.Abs(index - 1.0) <= 1e-9
            && !material.GetType().Name.Contains("Glass", StringComparison.Ordinal);
    }

    private static double CenterThickness(OpticalSurface surface)
    {
        if (!double.IsFinite(surface.Thickness))
        {
            throw new InvalidOperationException("中心厚度不是有限数值。");
        }

        return surface.Thickness;
    }

    private static double EdgeThickness(OpticalSurface surface, OpticalSurface nextSurface)
    {
        if (!double.IsFinite(surface.Thickness))
        {
            throw new InvalidOperationException("边厚所在空间的中心厚度不是有限数值。");
        }

        var y = surface.SemiDiameter;
        if (!double.IsFinite(y) || y <= 0)
        {
            throw new InvalidOperationException("边厚所在表面的半口径不是有效正数。");
        }

        var currentSag = surface.Geometry.Sag(0, y);
        var nextSag = nextSurface.Geometry.Sag(0, Math.Min(y, nextSurface.SemiDiameter));
        if (!double.IsFinite(currentSag) || !double.IsFinite(nextSag))
        {
            throw new InvalidOperationException("边厚所在表面的 sag 不是有限数值。");
        }

        return surface.Thickness + nextSag - currentSag;
    }

    private static double EvaluateParaxialMagnification(Optic optic, MeritOperandDefinition definition)
    {
        var objectSurface = optic.SurfaceGroup.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("系统没有物面。");
        if (ObjectConjugate.IsInfinite(objectSurface))
        {
            throw new InvalidOperationException("无穷远物方没有有限物高，无法计算近轴横向放大率 PMAG。");
        }

        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var objectPosition = objectSurface.CoordinateSystem.Origin.Z;
        var entrancePupilPosition = optic.Paraxial.EstimateEntrancePupilLocation(wavelength.Micrometers);
        var denominator = entrancePupilPosition - objectPosition;
        if (Math.Abs(denominator) <= 1e-15)
        {
            throw new InvalidOperationException("入瞳与物面重合，无法建立近轴主光线。");
        }

        const double objectHeight = 1.0;
        var initialSlope = -objectHeight / denominator;
        var trace = optic.Paraxial.TraceGeneric(
            new[] { objectHeight },
            new[] { initialSlope },
            objectPosition,
            wavelength.Micrometers);
        var marginal = optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var marginalHeight = marginal.Heights[^1][0];
        var marginalSlope = marginal.Slopes[^1][0];
        if (!double.IsFinite(marginalHeight)
            || !double.IsFinite(marginalSlope)
            || Math.Abs(marginalSlope) <= 1e-15)
        {
            throw new InvalidOperationException("PMAG 无法由近轴边缘光线确定近轴像面。");
        }

        var paraxialImageDistance = -marginalHeight / marginalSlope;
        var imageHeight = trace.Heights[^1][0] + (paraxialImageDistance * trace.Slopes[^1][0]);
        if (!double.IsFinite(imageHeight))
        {
            throw new InvalidOperationException("PMAG 近轴追迹得到非有限像高。");
        }

        return imageHeight / objectHeight;
    }

    private static double EvaluatePetzvalRadius(Optic optic, MeritOperandDefinition definition)
    {
        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var surfaces = optic.SurfaceGroup.Items.ToArray();
        var petzvalSum = 0.0;
        for (var index = 1; index < surfaces.Length; index++)
        {
            var surface = surfaces[index];
            var previous = surfaces[index - 1];
            var nBefore = SafeIndex(previous.MaterialAfter.RefractiveIndex(wavelength.Nanometers));
            var nAfter = SafeIndex(surface.MaterialAfter.RefractiveIndex(wavelength.Nanometers));
            var curvature = surface.IsPlane ? 0.0 : 1.0 / surface.Radius;
            petzvalSum += curvature * (nAfter - nBefore) / (nBefore * nAfter);
        }

        if (Math.Abs(petzvalSum) <= 1e-15)
        {
            throw new InvalidOperationException("Petzval sum is zero; Petzval radius is infinite.");
        }

        // Zemax reports the Petzval image-surface radius with the image-space
        // curvature sign, which is opposite to the accumulated surface sum.
        return -1.0 / petzvalSum;
    }

    private static double SafeIndex(double value) =>
        double.IsFinite(value) && Math.Abs(value) > 1e-12 ? value : 1.0;

    private static double EvaluateMaximumDistortion(Optic optic, MeritOperandDefinition definition)
    {
        var wavelengthNumber = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        var analysis = new DistortionAnalysis(
            optic,
            numPoints: 33,
            wavelengthNumber: wavelengthNumber,
            displayMode: "percent");
        var data = analysis.GenerateData();
        if (data.Values.TryGetValue("MaximumAbsoluteDistortionPercent", out var value)
            && value is double distortion
            && double.IsFinite(distortion))
        {
            return distortion;
        }

        throw new InvalidOperationException("DIMX 无法从畸变分析取得最大畸变值。");
    }

    private static double EvaluateImageSpaceNumericalAperture(Optic optic, MeritOperandDefinition definition)
    {
        var wavelength = ResolveWavelength(
            optic,
            ZemaxIntegerParameter(definition, 1, definition.Wavelength));
        var marginal = optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var finalSlope = marginal.Slopes[^1][0];
        if (!double.IsFinite(finalSlope))
        {
            throw new InvalidOperationException("像方边缘光线斜率不是有限数值。");
        }

        var imageMaterial = optic.SurfaceGroup.Items.LastOrDefault()?.MaterialAfter
            ?? optic.Materials.Resolve("Air");
        var imageIndex = imageMaterial.RefractiveIndex(wavelength.Nanometers);
        if (!double.IsFinite(imageIndex) || imageIndex <= 0)
        {
            throw new InvalidOperationException("像方介质折射率不是有效正数。");
        }

        return Math.Abs(imageIndex * Math.Sin(Math.Atan(finalSlope)));
    }


    private static double EvaluateRangeThickness(Optic optic, MeritOperandDefinition definition)
    {
        if (optic.SurfaceGroup.Items.Count < 2)
        {
            throw new InvalidOperationException("系统至少需要物面和像面才能计算范围厚度。");
        }

        var startSurface = ZemaxIntegerParameter(definition, 0, definition.Surface);
        var endSurface = ZemaxIntegerParameter(definition, 1, definition.Wavelength);
        if (endSurface <= 0)
        {
            endSurface = optic.SurfaceGroup.Items[^1].Number;
        }

        if (startSurface < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), startSurface, "起始表面不能为负数。");
        }

        if (endSurface <= startSurface)
        {
            throw new ArgumentOutOfRangeException(nameof(definition), endSurface, "终止表面必须大于起始表面。");
        }

        var surfacesByNumber = optic.SurfaceGroup.Items.ToDictionary(surface => surface.Number);
        if (!surfacesByNumber.ContainsKey(startSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), startSurface, "找不到范围厚度起始表面。");
        }

        if (!surfacesByNumber.ContainsKey(endSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(definition), endSurface, "找不到范围厚度终止表面。");
        }

        var thickness = 0.0;
        for (var surfaceNumber = startSurface; surfaceNumber <= endSurface; surfaceNumber++)
        {
            if (!surfacesByNumber.TryGetValue(surfaceNumber, out var surface))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(definition),
                    surfaceNumber,
                    "范围厚度中的表面编号不连续。");
            }

            if (surfaceNumber == 0 && ObjectConjugate.IsInfinite(surface))
            {
                continue;
            }

            if (!double.IsFinite(surface.Thickness))
            {
                throw new InvalidOperationException("范围厚度包含非有限表面厚度。");
            }

            thickness += surface.Thickness;
        }

        return thickness;
    }

    private static int ZemaxIntegerParameter(
        MeritOperandDefinition definition,
        int index,
        int fallback)
    {
        return definition.ZemaxIntegerParameters is { Length: > 0 } parameters
            && index >= 0
            && index < parameters.Length
                ? parameters[index]
                : fallback;
    }

    private static double ZemaxDataParameter(
        MeritOperandDefinition definition,
        int index,
        double fallback)
    {
        return definition.ZemaxDataParameters is { Length: > 0 } parameters
            && index >= 0
            && index < parameters.Length
                ? parameters[index]
                : fallback;
    }

    private static double EvaluateRmsSpot(Optic optic, MeritOperandDefinition definition)
    {
        var normalized = ResolveNormalizedField(optic, definition);
        var pupilSamples = CreateWizardPupilSamples(definition, 37);
        var wavelengths = definition.Wavelength <= 0
            ? optic.Wavelengths.ToArray()
            : new[] { ResolveWavelength(optic, definition.Wavelength) };
        if (wavelengths.Length == 0)
        {
            throw new InvalidOperationException("系统没有波长。");
        }

        var samplesByWavelength = wavelengths
            .Select(wavelength => TraceSpotSamples(
                optic,
                definition,
                normalized,
                wavelength,
                pupilSamples))
            .ToArray();
        var primaryIndex = definition.Wavelength <= 0
            ? Array.FindIndex(wavelengths, wavelength => wavelength.IsPrimary)
            : 0;
        if (primaryIndex < 0)
        {
            primaryIndex = 0;
        }

        var referenceSamples = samplesByWavelength[primaryIndex];
        if (referenceSamples.Length == 0)
        {
            throw new InvalidOperationException("主波长没有有效光线。");
        }

        var referenceType = CanonicalType(definition.Type);
        double referenceX;
        double referenceY;
        if (referenceType is "RSCH" or "RSRH")
        {
            var chief = TraceChiefRaySample(optic, definition, normalized, wavelengths[primaryIndex]);
            referenceX = chief.Position.X;
            referenceY = chief.Position.Y;
        }
        else
        {
            var referenceWeight = referenceSamples.Sum(sample => sample.Intensity);
            if (referenceWeight <= 1e-12)
            {
                throw new InvalidOperationException("主波长没有有效光线。");
            }

            referenceX = referenceSamples.Sum(sample => sample.Position.X * sample.Intensity) / referenceWeight;
            referenceY = referenceSamples.Sum(sample => sample.Position.Y * sample.Intensity) / referenceWeight;
        }

        var samples = samplesByWavelength.SelectMany(items => items).ToArray();
        var totalWeight = samples.Sum(sample => sample.Intensity);
        if (samples.Length == 0 || totalWeight <= 1e-12)
        {
            throw new InvalidOperationException("没有有效光线。");
        }

        return Math.Sqrt(samples.Sum(sample =>
            (((sample.Position.X - referenceX) * (sample.Position.X - referenceX))
             + ((sample.Position.Y - referenceY) * (sample.Position.Y - referenceY))) * sample.Intensity) / totalWeight);
    }

    private static RayTraceSample TraceChiefRaySample(
        Optic optic,
        MeritOperandDefinition definition,
        (double X, double Y) normalized,
        Wavelength wavelength)
    {
        var chiefDefinition = definition.Clone();
        chiefDefinition.Wavelength = FindWavelengthIndex(optic, wavelength) + 1;
        chiefDefinition.Hx = normalized.X;
        chiefDefinition.Hy = normalized.Y;
        chiefDefinition.Px = 0;
        chiefDefinition.Py = 0;
        var sample = SampleAtSurface(optic, chiefDefinition);
        if (sample.Vignetted || sample.Intensity <= 0)
        {
            throw new InvalidOperationException("主光线未到达指定表面。");
        }

        return sample;
    }

    private static RayTraceSample[] TraceSpotSamples(
        Optic optic,
        MeritOperandDefinition definition,
        (double X, double Y) normalized,
        Wavelength wavelength,
        IReadOnlyList<Raytrace.PupilSample> pupilSamples)
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalized.X,
            normalized.Y,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop: optic.RayAimingEnabled);
        var surfaceNumber = definition.Surface <= 0
            ? ResolveImageSurfaceNumber(optic)
            : definition.Surface;
        var surfaceIndex = optic.SurfaceGroup.Items
                .Select((surface, index) => (surface, index))
                .Where(item => item.surface.Number == surfaceNumber)
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        if (surfaceIndex < 0)
        {
            return Array.Empty<RayTraceSample>();
        }

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            Raytrace.TraceRequest.Selected(new[] { surfaceIndex }));
        var surfaceSamples = trace.GetSurfaceSamples(surfaceIndex);
        var wavelengthIndex = FindWavelengthIndex(optic, wavelength) + 1;
        var samples = new List<RayTraceSample>(trace.RayCount);
        for (var index = 0; index < trace.RayCount; index++)
        {
            var sampleValue = surfaceSamples[index];
            if (sampleValue is not { } value || value.Vignetted || value.Intensity <= 0)
            {
                continue;
            }

            var sample = value.ToRayTraceSample();
            samples.Add(sample);
            var batch = ActiveEvaluationBatch.Value;
            if (batch is not null && index < pupilSamples.Count)
            {
                var pupil = pupilSamples[index];
                batch.RaySamples[CreateRaySampleCacheKey(
                    optic,
                    definition,
                    wavelengthIndex,
                    pupil.X,
                    pupil.Y,
                    surfaceNumber,
                    optic.RayAimingEnabled)] = sample;
            }
        }

        return samples.ToArray();
    }

    private static double EvaluateRayAberration(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var reference = ResolveAberrationReference(optic, definition, angular: false);
        var x = sample.Position.X - reference.X;
        var y = sample.Position.Y - reference.Y;
        return CanonicalType(definition.Type) switch
        {
            "TRCX" or "TRAX" => x,
            "TRCY" or "TRAY" => y,
            _ => Math.Sqrt((x * x) + (y * y))
        };
    }

    private static double EvaluateAngularAberration(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var reference = ResolveAberrationReference(optic, definition, angular: true);
        var x = sample.Direction.X - reference.X;
        var y = sample.Direction.Y - reference.Y;
        return CanonicalType(definition.Type) switch
        {
            "ANCX" or "ANAX" => x,
            "ANCY" or "ANAY" => y,
            _ => Math.Sqrt((x * x) + (y * y))
        };
    }

    private static (double X, double Y) ResolveAberrationReference(
        Optic optic,
        MeritOperandDefinition definition,
        bool angular)
    {
        var type = CanonicalType(definition.Type);
        var chiefReference = type is "TRAR" or "TRAX" or "TRAY" or "ANAR" or "ANAX" or "ANAY";
        var cacheKey = new AberrationReferenceCacheKey(
            optic,
            angular,
            chiefReference,
            definition.Surface == 0 && SurfaceZeroMeansImage(definition.Type)
                ? ResolveImageSurfaceNumber(optic)
                : definition.Surface,
            definition.Field,
            chiefReference || definition.PolychromaticReference ? 0 : definition.Wavelength,
            definition.Hx,
            definition.Hy,
            definition.PupilRings,
            definition.PupilArms,
            definition.PupilObscuration,
            definition.PupilSampling,
            definition.PolychromaticReference);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is not null && batch.AberrationReferences.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var normalized = ResolveNormalizedField(optic, definition);
        (double X, double Y) result;
        if (chiefReference)
        {
            var primary = optic.Wavelengths[PrimaryWavelengthIndex(optic)];
            var chief = TraceChiefRaySample(optic, definition, normalized, primary);
            result = angular
                ? (chief.Direction.X, chief.Direction.Y)
                : (chief.Position.X, chief.Position.Y);
        }
        else
        {
            var wavelengthIndices = definition.PolychromaticReference
                ? Enumerable.Range(0, optic.Wavelengths.Count).ToArray()
                : new[] { Math.Clamp(definition.Wavelength - 1, 0, optic.Wavelengths.Count - 1) };
            var wavelengthWeights = NormalizeWeights(
                wavelengthIndices.Select(index => optic.Wavelengths[index].Weight).ToArray());
            var pupilSamples = NormalizePupilSamples(CreateWizardPupilSamples(definition, 37));
            var weightedX = 0.0;
            var weightedY = 0.0;
            var totalWeight = 0.0;
            for (var wavelengthOffset = 0; wavelengthOffset < wavelengthIndices.Length; wavelengthOffset++)
            {
                var wavelength = optic.Wavelengths[wavelengthIndices[wavelengthOffset]];
                var samples = TraceSpotSamples(optic, definition, normalized, wavelength, pupilSamples);
                foreach (var sample in samples)
                {
                    var weight = sample.Intensity * wavelengthWeights[wavelengthOffset];
                    weightedX += (angular ? sample.Direction.X : sample.Position.X) * weight;
                    weightedY += (angular ? sample.Direction.Y : sample.Position.Y) * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight <= 1e-12)
            {
                throw new InvalidOperationException("没有可用于计算参考质心的有效光线。");
            }

            result = (weightedX / totalWeight, weightedY / totalWeight);
        }

        if (batch is not null)
        {
            batch.AberrationReferences[cacheKey] = result;
        }

        return result;
    }

    private static double EvaluateMooreElliottDifference(
        Optic optic,
        MeritOperandDefinition definition,
        bool sagittal)
    {
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var cutoff = DiffractionCutoff(optic, wavelength);
        if (cutoff <= 1e-12)
        {
            throw new InvalidOperationException("系统没有有效的衍射截止频率。");
        }

        var shift = 2 * Math.Max(0, definition.SpatialFrequency) / cutoff;
        if (!PairFitsPupil(definition.Px, definition.Py, shift, sagittal))
        {
            throw new InvalidOperationException("Moore-Elliott 移位光线超出当前入瞳。");
        }

        var half = shift / 2;
        var first = definition.Clone();
        var second = definition.Clone();
        if (sagittal)
        {
            first.Px -= half;
            second.Px += half;
        }
        else
        {
            first.Py -= half;
            second.Py += half;
        }

        var firstSample = SampleAtSurface(optic, first);
        var secondSample = SampleAtSurface(optic, second);
        var wavelengthMillimeters = wavelength.Micrometers / 1000.0;
        return (secondSample.CumulativeOpticalPathLength - firstSample.CumulativeOpticalPathLength)
            / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static double EvaluateRmsWavefront(Optic optic, MeritOperandDefinition definition)
    {
        using var trace = TraceBundleAtSurface(
            optic,
            definition,
            37,
            surfaceNumber: 0,
            out var surfaceIndex);
        var paths = trace.GetSurfaceSamples(surfaceIndex)
            .Where(sample => sample is { Vignetted: false, Intensity: > 0 })
            .Select(sample => sample!.Value.CumulativeOpticalPathLength)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("没有有效光线。");
        }

        var mean = paths.Average();
        var rmsMillimeters = Math.Sqrt(paths.Select(path => (path - mean) * (path - mean)).Average());
        var wavelengthMillimeters = ResolveWavelength(optic, definition.Wavelength).Micrometers / 1000.0;
        return rmsMillimeters / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static double EvaluateOpticalPathDifference(Optic optic, MeritOperandDefinition definition)
    {
        var sample = SampleAtSurface(optic, definition);
        var wavelengthMillimeters = ResolveWavelength(optic, definition.Wavelength).Micrometers / 1000.0;
        var type = CanonicalType(definition.Type);
        if (type == "OPDC")
        {
            var chiefDefinition = definition.Clone();
            chiefDefinition.Px = 0;
            chiefDefinition.Py = 0;
            var chief = SampleAtSurface(optic, chiefDefinition);
            return (sample.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength)
                / Math.Max(1e-12, wavelengthMillimeters);
        }

        var wavefrontSurface = definition.Surface == 0 && SurfaceZeroMeansImage(definition.Type)
            ? ResolveImageSurfaceNumber(optic)
            : definition.Surface;
        var cacheKey = new WavefrontReferenceCacheKey(
            optic,
            type,
            wavefrontSurface,
            definition.Field,
            definition.Wavelength,
            definition.Hx,
            definition.Hy,
            definition.PupilRings,
            definition.PupilArms,
            definition.PupilObscuration,
            definition.PupilSampling,
            definition.PolychromaticReference);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is null || !batch.WavefrontReferences.TryGetValue(cacheKey, out var plane))
        {
            var pupilSamples = NormalizePupilSamples(CreateWizardPupilSamples(definition, 37));
            using var trace = TraceBundleAtSurface(
                optic,
                definition,
                37,
                wavefrontSurface,
                out var targetSurfaceIndex);
            var surfaceSamples = trace.GetSurfaceSamples(targetSurfaceIndex);
            var fitted = new List<(
                double X,
                double Y,
                double Path,
                double Weight)>(surfaceSamples.Count);
            for (var index = 0; index < surfaceSamples.Count; index++)
            {
                if (surfaceSamples[index] is not { } tracedSample
                    || tracedSample.Vignetted
                    || tracedSample.Intensity <= 0)
                {
                    continue;
                }

                var pupil = pupilSamples[index];
                fitted.Add((
                    pupil.X,
                    pupil.Y,
                    tracedSample.CumulativeOpticalPathLength,
                    tracedSample.Intensity));
            }

            var fittedSamples = fitted.ToArray();
            if (fittedSamples.Length == 0)
            {
                throw new InvalidOperationException("没有可用于计算波前参考的有效光线。");
            }

            if (type == "OPDM")
            {
                var totalWeight = fittedSamples.Sum(item => item.Weight);
                plane = (
                    fittedSamples.Sum(item => item.Path * item.Weight) / totalWeight,
                    0,
                    0);
            }
            else
            {
                plane = FitWeightedPlane(fittedSamples);
            }

            if (batch is not null)
            {
                batch.WavefrontReferences[cacheKey] = plane;
            }
        }

        var referencePath = plane.Piston + (plane.XTilt * definition.Px) + (plane.YTilt * definition.Py);
        return (sample.CumulativeOpticalPathLength - referencePath)
            / Math.Max(1e-12, wavelengthMillimeters);
    }

    private static (double Piston, double XTilt, double YTilt) FitWeightedPlane(
        IReadOnlyList<(double X, double Y, double Path, double Weight)> samples)
    {
        var matrix = new double[3, 4];
        foreach (var sample in samples)
        {
            var weight = Math.Max(0, sample.Weight);
            var basis = new[] { 1.0, sample.X, sample.Y };
            for (var row = 0; row < 3; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    matrix[row, column] += weight * basis[row] * basis[column];
                }

                matrix[row, 3] += weight * basis[row] * sample.Path;
            }
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < 3; row++)
            {
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot]))
                {
                    best = row;
                }
            }

            if (Math.Abs(matrix[best, pivot]) <= 1e-18)
            {
                var totalWeight = samples.Sum(sample => Math.Max(0, sample.Weight));
                var mean = samples.Sum(sample => sample.Path * Math.Max(0, sample.Weight))
                    / Math.Max(1e-12, totalWeight);
                return (mean, 0, 0);
            }

            if (best != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    (matrix[pivot, column], matrix[best, column]) =
                        (matrix[best, column], matrix[pivot, column]);
                }
            }

            var divisor = matrix[pivot, pivot];
            for (var column = pivot; column < 4; column++)
            {
                matrix[pivot, column] /= divisor;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = matrix[row, pivot];
                for (var column = pivot; column < 4; column++)
                {
                    matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
        }

        return (matrix[0, 3], matrix[1, 3], matrix[2, 3]);
    }

    private static RayTraceSample SampleAtSurface(Optic optic, MeritOperandDefinition definition)
    {
        var targetSurfaceNumber = definition.Surface == 0 && SurfaceZeroMeansImage(definition.Type)
            ? ResolveImageSurfaceNumber(optic)
            : definition.Surface;
        var cacheKey = CreateRaySampleCacheKey(
            optic,
            definition,
            definition.Wavelength,
            definition.Px,
            definition.Py,
            targetSurfaceNumber,
            optic.RayAimingEnabled);
        var batch = ActiveEvaluationBatch.Value;
        if (batch is not null && batch.RaySamples.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var normalized = ResolveNormalizedField(optic, definition);
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
            normalized.X,
            normalized.Y,
            Math.Clamp(definition.Px, -1, 1),
            Math.Clamp(definition.Py, -1, 1),
            wavelength.Micrometers,
            aimAtStop: optic.RayAimingEnabled);
        var surfaceIndex = optic.SurfaceGroup.Items
                .Select((surface, index) => (surface, index))
                .Where(item => item.surface.Number == targetSurfaceNumber)
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        if (surfaceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                definition.Surface,
                "The requested merit-function surface does not exist.");
        }

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            Raytrace.TraceRequest.Selected(new[] { surfaceIndex }));
        if (!trace.TryGetSample(0, surfaceIndex, out var sampleValue))
        {
            throw new InvalidOperationException("The ray did not reach the requested surface.");
        }

        var sample = sampleValue.ToRayTraceSample();
        if (batch is not null)
        {
            batch.RaySamples[cacheKey] = sample;
        }

        return sample;
    }

    private static bool SurfaceZeroMeansImage(string operandType)
    {
        var type = CanonicalType(operandType);
        return type is "RSCH" or "RSRH" or "MECS" or "MECT"
            || type.StartsWith("TR", StringComparison.Ordinal)
            || type.StartsWith("AN", StringComparison.Ordinal)
            || type.StartsWith("OPD", StringComparison.Ordinal);
    }

    private static RaySampleCacheKey CreateRaySampleCacheKey(
        Optic optic,
        MeritOperandDefinition definition,
        int wavelength,
        double px,
        double py,
        int surfaceNumber,
        bool aimAtStop)
    {
        return new RaySampleCacheKey(
            optic,
            surfaceNumber,
            definition.Field,
            wavelength,
            Quantize(definition.Hx),
            Quantize(definition.Hy),
            Quantize(px),
            Quantize(py),
            aimAtStop);
    }

    private static int FindWavelengthIndex(Optic optic, Wavelength wavelength)
    {
        for (var index = 0; index < optic.Wavelengths.Count; index++)
        {
            if (ReferenceEquals(optic.Wavelengths[index], wavelength)
                || optic.Wavelengths[index].Equals(wavelength))
            {
                return index;
            }
        }

        return 0;
    }

    private static int ResolveImageSurfaceNumber(Optic optic)
    {
        return optic.SurfaceGroup.Items.Count == 0
            ? 0
            : optic.SurfaceGroup.Items[^1].Number;
    }

    private static double Quantize(double value) => Math.Round(value, 12);

    private static Raytrace.RequestedTrace TraceBundleAtSurface(
        Optic optic,
        MeritOperandDefinition definition,
        int sampleCount,
        int surfaceNumber,
        out int surfaceIndex)
    {
        var normalized = ResolveNormalizedField(optic, definition);
        var wavelength = ResolveWavelength(optic, definition.Wavelength);
        var pupilSamples = CreateWizardPupilSamples(definition, sampleCount);
        var targetSurfaceNumber = surfaceNumber <= 0
            ? ResolveImageSurfaceNumber(optic)
            : surfaceNumber;
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            normalized.X,
            normalized.Y,
            wavelength.Micrometers,
            pupilSamples,
            aimAtStop: optic.RayAimingEnabled);
        surfaceIndex = ResolveSurfaceIndex(optic, targetSurfaceNumber);
        var request = surfaceIndex == optic.SurfaceGroup.Items.Count - 1
            ? Raytrace.TraceRequest.FinalOnly(false)
            : Raytrace.TraceRequest.Selected(new[] { surfaceIndex });
        return optic.SequentialRayTracer.Trace(bundle, request);
    }

    private static int ResolveSurfaceIndex(Optic optic, int surfaceNumber)
    {
        if (surfaceNumber <= 0)
        {
            return optic.SurfaceGroup.Items.Count - 1;
        }

        var surfaceIndex = optic.SurfaceGroup.Items
            .Select((surface, index) => (surface, index))
            .Where(item => item.surface.Number == surfaceNumber)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        return surfaceIndex >= 0
            ? surfaceIndex
            : throw new ArgumentOutOfRangeException(nameof(surfaceNumber));
    }

    private static IReadOnlyList<Raytrace.PupilSample> CreateWizardPupilSamples(
        MeritOperandDefinition definition,
        int fallbackSampleCount)
    {
        var rings = Math.Clamp(definition.PupilRings, 1, 20);
        var arms = Math.Clamp(definition.PupilArms, 3, 36);
        var obscuration = Math.Clamp(definition.PupilObscuration, 0, 0.95);
        IReadOnlyList<Raytrace.PupilSample> samples;
        if (string.Equals(definition.PupilSampling, "gaussian_quad", StringComparison.OrdinalIgnoreCase))
        {
            var radialSamples = GaussianRadialSamples(rings, obscuration);
            samples = radialSamples
                .SelectMany(radialSample => Enumerable.Range(0, arms).Select(index =>
                {
                    var angle = 2 * Math.PI * (index + 1) / arms;
                    return new Raytrace.PupilSample(
                        radialSample.Radius * Math.Cos(angle),
                        radialSample.Radius * Math.Sin(angle),
                        radialSample.Weight / arms);
                }))
                .ToArray();
        }
        else if (string.Equals(definition.PupilSampling, "uniform", StringComparison.OrdinalIgnoreCase))
        {
            var side = (rings * 2) + 1;
            samples = Enumerable.Range(0, side)
                .SelectMany(y => Enumerable.Range(0, side).Select(x => new Raytrace.PupilSample(
                    side == 1 ? 0 : -1 + (2.0 * x / (side - 1)),
                    side == 1 ? 0 : -1 + (2.0 * y / (side - 1)),
                    1)))
                .Where(sample =>
                {
                    var radiusSquared = (sample.X * sample.X) + (sample.Y * sample.Y);
                    return radiusSquared <= 1.0 + 1e-12
                        && radiusSquared >= (obscuration * obscuration) - 1e-12;
                })
                .ToArray();
        }
        else
        {
            var generated = new List<Raytrace.PupilSample>();
            if (obscuration <= 1e-12)
            {
                generated.Add(new Raytrace.PupilSample(0, 0, 1));
            }

            for (var ring = 1; ring <= rings; ring++)
            {
                var radius = ring / (double)rings;
                if (radius + 1e-12 < obscuration)
                {
                    continue;
                }

                var points = ring * arms;
                for (var index = 0; index < points; index++)
                {
                    var angle = 2 * Math.PI * index / points;
                    generated.Add(new Raytrace.PupilSample(
                        radius * Math.Cos(angle),
                        radius * Math.Sin(angle),
                        1));
                }
            }

            samples = generated;
        }

        return samples.Count > 0
            ? samples
            : Raytrace.ApertureSampler.Generate(fallbackSampleCount, Raytrace.PupilSampling.Hexapolar);
    }

    private static (double X, double Y) ResolveNormalizedField(
        Optic optic,
        MeritOperandDefinition definition)
    {
        if (Math.Abs(definition.Hx) > 1e-12 || Math.Abs(definition.Hy) > 1e-12)
        {
            return (Math.Clamp(definition.Hx, -1, 1), Math.Clamp(definition.Hy, -1, 1));
        }

        var field = ResolveField(optic, definition.Field);
        return FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
    }

    private static FieldPoint ResolveField(Optic optic, int oneBasedIndex)
    {
        if (optic.Fields.Count == 0)
        {
            throw new InvalidOperationException("系统没有视场。");
        }

        var index = oneBasedIndex <= 0 ? 0 : oneBasedIndex - 1;
        return optic.Fields[Math.Clamp(index, 0, optic.Fields.Count - 1)];
    }

    private static Wavelength ResolveWavelength(Optic optic, int oneBasedIndex)
    {
        if (optic.Wavelengths.Count == 0)
        {
            throw new InvalidOperationException("系统没有波长。");
        }

        if (oneBasedIndex <= 0)
        {
            return optic.Wavelengths.FirstOrDefault(wavelength => wavelength.IsPrimary)
                ?? optic.Wavelengths[0];
        }

        return optic.Wavelengths[Math.Clamp(oneBasedIndex - 1, 0, optic.Wavelengths.Count - 1)];
    }

    private static OpticalSurface ResolveSurface(Optic optic, int surfaceNumber)
    {
        return optic.SurfaceGroup.Items.FirstOrDefault(surface => surface.Number == surfaceNumber)
            ?? throw new ArgumentOutOfRangeException(nameof(surfaceNumber), "找不到指定表面。");
    }

    private static bool IsRotationallySymmetric(Optic optic)
    {
        return optic.SurfaceGroup.Items.All(surface =>
            IsNearlyZero(surface.CoordinateSystem.Origin.X)
            && IsNearlyZero(surface.CoordinateSystem.Origin.Y)
            && IsNearlyZero(surface.CoordinateSystem.RotationXDegrees)
            && IsNearlyZero(surface.CoordinateSystem.RotationYDegrees)
            && IsRotationallySymmetric(surface.Geometry)
            && IsRotationallySymmetric(surface.PhysicalAperture));
    }

    private static bool IsRotationallySymmetric(IGeometry geometry)
    {
        return geometry switch
        {
            PlaneGeometry or StandardGeometry or EvenAsphereGeometry or OddAsphereGeometry or ForbesQGeometry => true,
            BiconicGeometry biconic => IsNearlyEqual(biconic.RadiusX, biconic.RadiusY)
                && IsNearlyEqual(biconic.ConicX, biconic.ConicY),
            ZernikeGeometry zernike => zernike.Coefficients.All(term =>
                term.Key.AzimuthalFrequency == 0 || IsNearlyZero(term.Value)),
            _ => false
        };
    }

    private static bool IsRotationallySymmetric(IPhysicalAperture? aperture)
    {
        return aperture switch
        {
            null or CircularAperture or AnnularAperture => true,
            OffsetRadialAperture offset => IsNearlyZero(offset.OffsetX) && IsNearlyZero(offset.OffsetY),
            EllipticalAperture ellipse => IsNearlyEqual(ellipse.SemiAxisX, ellipse.SemiAxisY)
                && IsNearlyZero(ellipse.OffsetX)
                && IsNearlyZero(ellipse.OffsetY),
            BooleanAperture boolean => IsRotationallySymmetric(boolean.Left)
                && IsRotationallySymmetric(boolean.Right),
            _ => false
        };
    }

    private static bool IsNearlyZero(double value) => Math.Abs(value) <= 1e-12;

    private static bool IsNearlyEqual(double left, double right) =>
        left.Equals(right)
        || (double.IsFinite(left)
            && double.IsFinite(right)
            && Math.Abs(left - right) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right))));

    private static double[] NormalizeWeights(IReadOnlyList<double> weights)
    {
        var sum = weights.Sum(weight => Math.Max(0, weight));
        return sum <= 1e-12
            ? Enumerable.Repeat(1.0 / Math.Max(1, weights.Count), weights.Count).ToArray()
            : weights.Select(weight => Math.Max(0, weight) / sum).ToArray();
    }
}
