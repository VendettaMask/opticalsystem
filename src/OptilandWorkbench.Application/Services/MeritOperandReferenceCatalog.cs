namespace OptilandWorkbench.Application.Services;

internal sealed record MeritOperandReference(string Category, string Calculation);

internal static class MeritOperandReferenceCatalog
{
    private const string CompatibilityCalculation =
        "当前仅支持从 ZMX 导入、在工程中保留原始参数并再次保存；计算引擎不会执行该操作数，也不会用零值冒充计算结果。";

    internal static MeritOperandReference Describe(string code, bool compatibilityOnly)
    {
        var canonical = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (compatibilityOnly)
        {
            return new MeritOperandReference("Zemax 兼容保留", CompatibilityCalculation);
        }

        return new MeritOperandReference(CategoryFor(canonical), CalculationFor(canonical));
    }

    private static string CategoryFor(string code) => code switch
    {
        "BLNK" or "DMFS" or "GOTO" or "ENDX" or "OOFF" or "SKIN" or "SKIS" or "USYM" => "说明与控制",
        "CONS" or "SINE" or "COSI" or "TANG" or "ASIN" or "ACOS" or "ATAN"
            or "ABSO" or "SQRT" or "RECI" or "LOGE" or "LOGT" or "SUMM" or "PROD"
            or "PROB" or "DIVB" or "DIVI" or "DIFF" or "EQUA" or "MAXX" or "MINN"
            or "OSUM" or "QSUM" or "OPVA" or "OPGT" or "OPLT"
            or "ABGT" or "ABLT" => "行数学与约束",
        "RSCE" or "RSCH" or "RSRE" or "RSRH" or "RWFE" or "OPDX" or "OPDM" or "OPDC"
            or "TRAC" or "TRAR" or "TRCX" or "TRCY" or "TRAX" or "TRAY"
            or "ANAC" or "ANAR" or "ANCX" or "ANCY" or "ANAX" or "ANAY"
            or "MECS" or "MECT" => "像质与波前",
        "REAX" or "REAY" or "REAR" or "RANG" => "实际光线",
        "MNIN" or "MXIN" or "MNAB" or "MXAB" or "INDX" => "玻璃数据约束",
        "EFFL" or "EFLX" or "EFLY" or "ENPP" or "EPDI" or "EXPP" or "EXPD"
            or "ISFN" or "SFNO" or "WFNO" or "FNUM" or "ISNA" or "PMAG" or "PETZ"
            or "WLEN" or "POWR" or "TOTR" => "一阶量与系统数据",
        "RADI" or "THIC" or "CVGT" or "CVLT" or "CVVA" or "COGT" or "COLT" or "COVA"
            or "MNCV" or "MXCV" or "MNSD" or "MXSD" => "表面数据与边界",
        _ => "厚度与结构边界"
    };

    private static string CalculationFor(string code) => code switch
    {
        "BLNK" => "空白行不读取参数、不计算数值，当前值和评价函数贡献均为 0。",
        "DMFS" => "评价函数向导生成的说明行，只保存设置说明，不参与数值计算。",
        "GOTO" => "跳转到 Int1 指定的后续操作数行；被跨过的行不执行。目标必须位于当前行之后且在评价函数范围内。",
        "ENDX" => "立即结束有序评价函数求值；其后的行不执行。",
        "OOFF" => "作为控制标记保留，当前实现返回 0 且不产生贡献。",
        "SKIN" => "系统不是旋转对称时跳转到 Int1 指定的后续行，否则继续下一行。",
        "SKIS" => "系统是旋转对称时跳转到 Int1 指定的后续行，否则继续下一行。",
        "USYM" => "把当前评价函数声明为旋转对称，供 SKIN/SKIS 控制流判断；本行不产生贡献。",
        "CONS" => "当前值直接取该行目标值 Target，因此该常数行自身的平方误差为 0。",
        "SINE" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 sin(x)。",
        "COSI" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 cos(x)。",
        "TANG" => "读取 Int1 指定的已完成前序行；Int2 非零时先把输入从度转换为弧度，再计算 tan(x)。",
        "ASIN" => "读取 Int1 指定前序行并计算 asin(x)，输入必须在 [-1, 1]；Int2 非零时把弧度结果转换为度。",
        "ACOS" => "读取 Int1 指定前序行并计算 acos(x)，输入必须在 [-1, 1]；Int2 非零时把弧度结果转换为度。",
        "ATAN" => "读取 Int1 指定前序行并计算 atan(x)；Int2 非零时把弧度结果转换为度。",
        "ABSO" => "当前值 = |Int1 指定前序行的当前值|。",
        "SQRT" => "当前值 = sqrt(Int1 指定前序行的当前值)；负输入会报告计算错误。",
        "RECI" => "当前值 = 1 / x，其中 x 来自 Int1 指定前序行；零或极小分母会报告错误。",
        "LOGE" => "对 Int1 指定前序行计算自然对数 ln(x)；x <= 0 时当前实现返回 0。",
        "LOGT" => "对 Int1 指定前序行计算常用对数 log10(x)；x <= 0 时当前实现返回 0。",
        "SUMM" => "当前值 = Int1 指定前序行值 + Int2 指定前序行值。",
        "PROD" => "当前值 = Int1 指定前序行值 × Int2 指定前序行值。",
        "PROB" => "当前值 = Int1 指定前序行值 × Data1(Factor)。",
        "DIVB" => "当前值 = Int1 指定前序行值 / Data1(Factor)；Factor 为零或极小时会报告错误。",
        "DIVI" => "当前值 = Int1 指定前序行值 / Int2 指定前序行值；零或极小分母会报告错误。",
        "DIFF" => "当前值 = Int1 指定前序行值 − Int2 指定前序行值。",
        "EQUA" => "读取 Int1 到 Int2 的闭区间前序行，以 Target 作为相等容差；先求平均值，再把超过容差的绝对偏差求和作为当前值。本行贡献 = |Weight| × Value²。",
        "MAXX" => "读取 Int1 到 Int2 的闭区间前序行，当前值取其中最大值。",
        "MINN" => "读取 Int1 到 Int2 的闭区间前序行，当前值取其中最小值。",
        "OSUM" => "读取 Int1 到 Int2 的闭区间前序行，当前值为所有输入当前值之和。",
        "QSUM" => "读取 Int1 到 Int2 的闭区间前序行，当前值 = sqrt(Σ value²)。",
        "OPVA" => "当前值等于 Int1 指定的已完成前序行当前值。",
        "OPGT" => "读取 Int1 指定前序行。值达到或超过 Target 时钳到 Target，使本行贡献为 0；不足部分形成平方误差。",
        "OPLT" => "读取 Int1 指定前序行。值不超过 Target 时钳到 Target，使本行贡献为 0；超出部分形成平方误差。",
        "ABGT" => "先取 Int1 指定前序行值的绝对值；达到或超过 Target 时贡献为 0，不足部分形成平方误差。",
        "ABLT" => "先取 Int1 指定前序行值的绝对值；不超过 Target 时贡献为 0，超出部分形成平方误差。",
        "RSCE" => RmsSpotCalculation("高斯求积瞳孔采样", "强度加权质心"),
        "RSCH" => RmsSpotCalculation("高斯求积瞳孔采样", "主波长主光线"),
        "RSRE" => RmsSpotCalculation("矩形阵列瞳孔采样", "强度加权质心"),
        "RSRH" => RmsSpotCalculation("矩形阵列瞳孔采样", "主波长主光线"),
        "RWFE" => "追迹有效瞳孔光线到目标面，减去光程的算术平均值，计算光程差的 RMS，再除以所选波长，结果单位为波。",
        "OPDX" => "追迹指定视场、波长和瞳孔坐标的光线；从累计光程中减去同一瞳孔采样的强度加权最佳拟合平面（活塞与 X/Y 倾斜），再除以波长。",
        "OPDM" => "追迹指定光线；从累计光程中仅减去同一瞳孔采样的强度加权平均光程，保留波前倾斜，再除以波长。",
        "OPDC" => "追迹指定光线和同视场、同波长的主光线，以两者累计光程之差除以波长；不移除拟合倾斜。",
        "TRAC" => TransverseAberrationCalculation("径向距离", "强度加权像面质心"),
        "TRAR" => TransverseAberrationCalculation("径向距离", "主波长主光线"),
        "TRCX" => TransverseAberrationCalculation("有符号 X 差", "强度加权像面质心"),
        "TRCY" => TransverseAberrationCalculation("有符号 Y 差", "强度加权像面质心"),
        "TRAX" => TransverseAberrationCalculation("有符号 X 差", "主波长主光线"),
        "TRAY" => TransverseAberrationCalculation("有符号 Y 差", "主波长主光线"),
        "ANAC" => AngularAberrationCalculation("方向余弦差的径向模", "强度加权方向余弦质心"),
        "ANAR" => AngularAberrationCalculation("方向余弦差的径向模", "主波长主光线方向"),
        "ANCX" => AngularAberrationCalculation("有符号 X 方向余弦差", "强度加权方向余弦质心"),
        "ANCY" => AngularAberrationCalculation("有符号 Y 方向余弦差", "强度加权方向余弦质心"),
        "ANAX" => AngularAberrationCalculation("有符号 X 方向余弦差", "主波长主光线方向"),
        "ANAY" => AngularAberrationCalculation("有符号 Y 方向余弦差", "主波长主光线方向"),
        "MECS" => MooreElliottCalculation("弧矢方向（Px）"),
        "MECT" => MooreElliottCalculation("切向方向（Py）"),
        "REAX" => "追迹 Hx/Hy、Px/Py 和波长指定的实际光线到 Surface，返回交点 X 坐标。Surface=0 时按该操作数语义解析为像面。",
        "REAY" => "追迹 Hx/Hy、Px/Py 和波长指定的实际光线到 Surface，返回交点 Y 坐标。Surface=0 时按该操作数语义解析为像面。",
        "REAR" => "追迹指定实际光线到目标面，当前值 = sqrt(X² + Y²)。",
        "RANG" => "追迹指定实际光线到目标面，根据方向余弦计算 atan2(sqrt(L² + M²), |N|)，结果单位为弧度。",
        "EFFL" => "由当前系统的近轴矩阵估算有效焦距。",
        "EFLX" or "EFLY" => "对 Int1 起始面到 Int2 终止面的子系统建立近轴矩阵，并计算该范围的有效焦距。当前旋转对称实现中 X/Y 使用同一路径。",
        "ENPP" => "由当前系统近轴追迹估算入瞳相对位置。",
        "EPDI" => "由当前系统近轴追迹和孔径定义估算入瞳直径。",
        "EXPP" => "由当前系统近轴追迹估算出瞳相对位置。",
        "EXPD" => "由当前系统近轴追迹估算出瞳直径。",
        "ISFN" or "SFNO" or "WFNO" or "FNUM" => "由当前系统近轴边缘光线估算像方 F 数；这些代码当前连接同一个 F 数计算入口。",
        "ISNA" => "追迹所选波长的近轴边缘光线，当前值 = |n_image × sin(atan(u_image))|。",
        "WLEN" => "返回 Int2 指定波长编号的波长值，单位为微米。编号 0 使用主波长。",
        "INDX" => "读取 Int1 指定表面之后的材料，在 Int2 指定波长处计算折射率。",
        "MNIN" => LowerBoundary("Int1 到 Int2 表面范围内玻璃材料的 d 线 Nd 最小值"),
        "MXIN" => UpperBoundary("Int1 到 Int2 表面范围内玻璃材料的 d 线 Nd 最大值"),
        "MNAB" => LowerBoundary("Int1 到 Int2 表面范围内玻璃材料的 Vd 阿贝数最小值"),
        "MXAB" => UpperBoundary("Int1 到 Int2 表面范围内玻璃材料的 Vd 阿贝数最大值"),
        "POWR" => "读取 Int1 指定标准折射表面和 Int2 指定波长；当前值 = (n_after − n_before) / Radius，平面返回 0，非标准面或反射面报告错误。",
        "PMAG" => "仅用于有限物距。以单位物高建立近轴主光线，并在近轴像面求像高；当前值 = 近轴像高 / 单位物高。",
        "PETZ" => "逐面累加 Petzval sum：Σ[c × (n_after − n_before)/(n_before × n_after)]，当前值取带像方曲率符号的 −1/sum。",
        "TOTR" => "返回光学系统表面组的总轴向长度 TotalTrack；无穷远物面厚度不作为有限传播距离累加。",
        "RADI" => "返回 Surface 指定表面的曲率半径。",
        "THIC" or "CTVA" => "返回指定表面之后的轴向中心厚度。",
        "CTGT" => LowerBoundary("指定表面之后的轴向中心厚度"),
        "CTLT" => UpperBoundary("指定表面之后的轴向中心厚度"),
        "CVVA" => "返回指定表面的曲率；平面为 0，其他表面为 1 / Radius。",
        "CVGT" => LowerBoundary("指定表面的曲率 1 / Radius（平面为 0）"),
        "CVLT" => UpperBoundary("指定表面的曲率 1 / Radius（平面为 0）"),
        "COVA" => "返回指定表面的圆锥常数。",
        "COGT" => LowerBoundary("指定表面的圆锥常数"),
        "COLT" => UpperBoundary("指定表面的圆锥常数"),
        "ETVA" or "TTVA" => "在指定表面与下一表面各自半口径处，按 Int2 方向代码 0(+Y)、1(+X)、2(−Y)、3(−X) 计算 Thickness + Sag_next − Sag_current。",
        "ETGT" or "TTGT" => LowerBoundary("按指定方向计算的边缘总厚度"),
        "ETLT" or "TTLT" => UpperBoundary("按指定方向计算的边缘总厚度"),
        "FTGT" => LowerBoundary("沿 +Y 从轴上到全口径进行 201 点采样所得的最小厚度"),
        "FTLT" => UpperBoundary("沿 +Y 从轴上到全口径进行 201 点采样所得的最大厚度"),
        "STHI" => "在 Data1=X、Data2=Y 处计算指定表面到下一表面的局部厚度：Thickness + Sag_next(X,Y) − Sag_current(X,Y)。",
        "TTHI" => "从 Int1 起始面到 Int2 终止面（不含终止面之后）累加轴向厚度；正无穷物面厚度不计入。",
        "TGTH" => "在 Int1 到 Int2 的范围内筛选玻璃介质空间并累加中心厚度，不计反射空间与空气空间。",
        "MNCA" => RangeThicknessBoundary("空气中心厚度", "最小值", lower: true),
        "MXCA" => RangeThicknessBoundary("空气中心厚度", "最大值", lower: false),
        "MNCG" => RangeThicknessBoundary("玻璃中心厚度", "最小值", lower: true),
        "MXCG" => RangeThicknessBoundary("玻璃中心厚度", "最大值", lower: false),
        "MNCT" => RangeThicknessBoundary("全部非反射空间中心厚度", "最小值", lower: true),
        "MXCT" => RangeThicknessBoundary("全部非反射空间中心厚度", "最大值", lower: false),
        "MNEA" => RangeThicknessBoundary("空气 +Y 边厚", "最小值", lower: true),
        "MXEA" => RangeThicknessBoundary("空气 +Y 边厚", "最大值", lower: false),
        "MNEG" => RangeThicknessBoundary("玻璃 +Y 边厚", "最小值", lower: true),
        "MXEG" => RangeThicknessBoundary("玻璃 +Y 边厚", "最大值", lower: false),
        "MNET" => RangeThicknessBoundary("全部非反射空间 +Y 边厚", "最小值", lower: true),
        "MXET" => RangeThicknessBoundary("全部非反射空间 +Y 边厚", "最大值", lower: false),
        "XNEA" => PerimeterThicknessBoundary("空气", "最小值", lower: true),
        "XXEA" => PerimeterThicknessBoundary("空气", "最大值", lower: false),
        "XNEG" => PerimeterThicknessBoundary("玻璃", "最小值", lower: true),
        "XXEG" => PerimeterThicknessBoundary("玻璃", "最大值", lower: false),
        "XNET" => PerimeterThicknessBoundary("全部非反射空间", "最小值", lower: true),
        "XXET" => PerimeterThicknessBoundary("全部非反射空间", "最大值", lower: false),
        "MNCV" => RangeScalarBoundary("曲率", "最小值", lower: true),
        "MXCV" => RangeScalarBoundary("曲率", "最大值", lower: false),
        "MNSD" => RangeScalarBoundary("半口径", "最小值", lower: true),
        "MXSD" => RangeScalarBoundary("半口径", "最大值", lower: false),
        _ => "由当前 Workbench 已连接的操作数计算入口求值；具体输入含义以参数表和定义为准。"
    };

    private static string RmsSpotCalculation(string sampling, string reference) =>
        $"使用{sampling}追迹有效光线，以{reference}为参考，按光线强度计算 sqrt(Σ[w × ((X−Xref)² + (Y−Yref)²)] / Σw)。波长编号为 0 时合并全部波长。";

    private static string TransverseAberrationCalculation(string output, string reference) =>
        $"追迹指定实际光线到目标面，相对{reference}计算{output}。质心参考可按设置使用单波长或多波长强度加权。";

    private static string AngularAberrationCalculation(string output, string reference) =>
        $"追迹指定实际光线到目标面，相对{reference}计算{output}。";

    private static string MooreElliottCalculation(string direction) =>
        $"根据空间频率与衍射截止频率确定成对瞳孔光线的间隔，在{direction}对称移动两条光线，返回累计光程差除以波长，单位为波。";

    private static string LowerBoundary(string quantity) =>
        $"计算{quantity}。结果达到或超过 Target 时钳到 Target，使贡献为 0；不足部分形成平方误差。";

    private static string UpperBoundary(string quantity) =>
        $"计算{quantity}。结果不超过 Target 时钳到 Target，使贡献为 0；超出部分形成平方误差。";

    private static string RangeThicknessBoundary(
        string quantity,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 表面范围内计算{quantity}的{extreme}；Zone 为 0 时按 1 处理。{BoundarySuffix(lower)}";

    private static string PerimeterThicknessBoundary(
        string material,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 范围内，对{material}的指定 Zone 使用 64 个方位角采样全周边厚并取{extreme}。{BoundarySuffix(lower)}";

    private static string RangeScalarBoundary(
        string quantity,
        string extreme,
        bool lower) =>
        $"在 Int1 到 Int2 的闭区间表面上计算{quantity}{extreme}。{BoundarySuffix(lower)}";

    private static string BoundarySuffix(bool lower) => lower
        ? "结果达到或超过 Target 时贡献为 0。"
        : "结果不超过 Target 时贡献为 0。";
}
