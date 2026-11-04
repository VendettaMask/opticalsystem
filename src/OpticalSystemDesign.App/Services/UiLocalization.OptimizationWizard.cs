namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    internal const string OptimizationWizardSummary = "当前优化变量：{0}。\n预计生成 {1} 行评价函数。\n"
        + "当前组合：{2} · RMS · {3}。\n操作数：{4}。\n每个视场/波长使用{5}{6}–{7} 个光瞳采样点。";

    private static void AddOptimizationWizardTexts(Action<string, string, string> add)
    {
        add("优化向导与操作数", "Optimization Wizard and Operands", "最適化ウィザードとオペランド");
        add("根据当前视场、波长和光瞳采样生成可直接执行的评价函数。", "Generate a ready-to-use merit function from the current fields, wavelengths and pupil sampling.", "現在の視野・波長・瞳サンプリングから実行可能な評価関数を生成します。");
        add("优化函数", "Merit Function", "評価関数");
        add("成像质量", "Image Quality", "結像性能");
        add("空间频率", "Spatial Frequency", "空間周波数");
        add("X 权重", "X Weight", "X 重み");
        add("Y 权重", "Y Weight", "Y 重み");
        add("权重缩放", "Weight Scale", "重みの倍率");
        add("使用所有波长", "Use All Wavelengths", "すべての波長を使用");
        add("忽略垂轴色差", "Ignore Lateral Color", "倍率色収差を無視");
        add("加入常用约束", "Include Common Constraints", "共通制約を追加");
        add("替换当前评价函数", "Replace Current Merit Function", "現在の評価関数を置換");
        add("生成评价函数", "Generate Merit Function", "評価関数を生成");
        add("当前目标为最佳名义性能。", "The current target is best nominal performance.", "現在の目標は最良の公称性能です。");
        add("光瞳采样", "Pupil Sampling", "瞳サンプリング");
        add("采样方式", "Sampling Method", "サンプリング方法");
        add("环", "Rings", "リング数");
        add("臂", "Arms", "アーム数");
        add("中心遮拦", "Central Obscuration", "中心遮蔽");
        add("生成位置", "Generation Location", "生成位置");
        add("起始行", "Start Row", "開始行");
        add("向导摘要", "Wizard Summary", "ウィザードの概要");
        add("波前", "Wavefront", "波面");
        add("对比度", "Contrast", "コントラスト");
        add("点列图", "Spot Diagram", "スポットダイアグラム");
        add("角向", "Angular", "角度");
        add("质心", "Centroid", "重心");
        add("主光线", "Chief Ray", "主光線");
        add("无参考", "Unreferenced", "基準なし");
        add("移位光线对", "Shifted Ray Pairs", "シフト光線対");
        add("高斯求积", "Gaussian Quadrature", "ガウス求積");
        add("矩形阵列", "Rectangular Array", "矩形配列");
        add("最多 ", "up to ", "最大 ");
        add("OPDM（主光线参考波前）", "OPDM (chief-ray-referenced wavefront)", "OPDM（主光線基準波面）");
        add("OPDC（无参考波前）", "OPDC (unreferenced wavefront)", "OPDC（基準なし波面）");
        add("OPDX（质心参考波前）", "OPDX (centroid-referenced wavefront)", "OPDX（重心基準波面）");
        add("MECS / MECT（Moore-Elliott 对比度）", "MECS / MECT (Moore-Elliott contrast)", "MECS / MECT（Moore-Elliott コントラスト）");
        add("TRAR（主光线参考径向像差）", "TRAR (chief-ray-referenced radial aberration)", "TRAR（主光線基準の動径収差）");
        add("TRAC（质心参考径向像差）", "TRAC (centroid-referenced radial aberration)", "TRAC（重心基準の動径収差）");
        add("ANAR（主光线参考径向角差）", "ANAR (chief-ray-referenced radial angular aberration)", "ANAR（主光線基準の動径角収差）");
        add("ANAC（质心参考径向角差）", "ANAC (centroid-referenced radial angular aberration)", "ANAC（重心基準の動径角収差）");
        add("臂数仅用于高斯求积采样；矩形阵列不使用臂数。", "Arms apply only to Gaussian quadrature; rectangular arrays do not use arms.", "アーム数はガウス求積のみで使用し、矩形配列では使用しません。");
        add("空间频率仅用于对比度评价。", "Spatial frequency applies only to contrast evaluation.", "空間周波数はコントラスト評価のみで使用します。");
        add("波前评价不使用 X/Y 方向权重。", "Wavefront evaluation does not use X/Y weights.", "波面評価では X/Y の重みを使用しません。");
        add("对比度评价不使用此参考选项。", "Contrast evaluation does not use this reference option.", "コントラスト評価ではこの基準を使用しません。");
        add("忽略横向色差需选择全部波长，且评价类型不能为对比度。", "Ignoring lateral color requires all wavelengths and a non-contrast image quality mode.", "倍率色収差を無視するには、すべての波長とコントラスト以外の結像性能モードを選択してください。");
        add("生成条件：空间频率必须大于 0，且 X/Y 权重至少一项大于 0。", "Generation requires a positive spatial frequency and at least one positive X/Y weight.", "生成には、正の空間周波数と、少なくとも一方の正の X/Y 重みが必要です。");
        add(OptimizationWizardSummary,
            "Active optimization variables: {0}.\nEstimated merit function rows: {1}.\nCurrent selection: {2} · RMS · {3}.\nOperands: {4}.\nPupil samples per field/wavelength: {5}{6}–{7}.",
            "現在の最適化変数：{0}。\n生成予定の評価関数：{1} 行。\n現在の組み合わせ：{2} · RMS · {3}。\nオペランド：{4}。\n各視野・波長の瞳サンプル数：{5}{6}–{7}。");
    }
}
