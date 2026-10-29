namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddManufacturabilityTexts(Action<string, string, string> add)
    {
        add("基于当前处方的几何指标与规则筛查", "Geometry and rule screening for the current prescription", "現在の処方に基づく形状とルールの検査");
        add("玻璃元件", "Glass elements", "ガラス素子");
        add("通过项", "Passed checks", "合格項目");
        add("需评审项", "Checks to review", "要確認項目");
        add("不可加工项", "Blocking checks", "加工不可項目");
        add("评估规则", "Assessment rules", "評価ルール");
        add("重新评估", "Reassess", "再評価");
        add("最小中心厚度 (mm)", "Min. center thickness (mm)", "最小中心厚 (mm)");
        add("最小边厚 (mm)", "Min. edge thickness (mm)", "最小縁厚 (mm)");
        add("最大 D/CT", "Max. D/CT", "最大 D/CT");
        add("最小 |R|/D", "Min. |R|/D", "最小 |R|/D");
        add("最大边缘斜率 (°)", "Max. edge slope (°)", "最大縁傾斜角 (°)");
        add("几何加工指标", "Manufacturing geometry", "加工形状の指標");
        add("逐元件查看尺寸、面型与边缘几何", "Dimensions, surface shapes and edge geometry by element", "素子ごとの寸法、面形状、縁形状");
        add("规则评估结论", "Assessment findings", "ルール評価結果");
        add("优先处理不可加工与需评审项目", "Address blocking checks and review items first", "加工不可・要確認項目を優先して確認");
        add("当前规则未发现问题", "No issues found under the current rules", "現在のルールでは問題は見つかりませんでした");
        add("存在不可加工项目", "Blocking manufacturing issues found", "加工不可の項目があります");
        add("部分项目需要工艺评审", "Some checks need process review", "一部の項目に工程確認が必要です");
        add("没有识别到可评估的玻璃元件", "No glass elements available for assessment", "評価できるガラス素子がありません");
        add("没有可显示的评估结论", "No assessment findings to display", "表示できる評価結果がありません");
        add("无法评估", "Assessment unavailable", "評価できません");
        add("元件 {0}", "Element {0}", "素子 {0}");
        add("不可加工", "Blocked", "加工不可");
        add("需评审", "Review", "要確認");
        add("通过", "Passed", "合格");
        add("元件", "Element", "素子");
        add("项目", "Item", "項目");
        add("计算值", "Calculated value", "計算値");
        add("说明", "Notes", "説明");
        add("机械直径", "Mechanical diameter", "機械径");
        add("中心厚度", "Center thickness", "中心厚");
        add("有光焦度面半径绝对值", "Powered surface |R|", "屈折面の半径絶対値");
        add("全口径弧高", "Full-aperture sag", "全口径サグ");
        add("全口径边厚", "Full-aperture edge thickness", "全口径縁厚");
        add("表面边缘倾角", "Surface edge angle", "面の縁傾斜角");
        add("按前后表面较大半口径计算", "From the larger front/back semidiameter", "前後面の大きい方の半径から算出");
        add("按共同净半口径计算", "At the common clear semidiameter", "共通の有効半径で算出");
        add("共享 Core 实际面型在共同净半口径处的切线角", "Tangent angle of the actual surface at the common clear semidiameter", "実際の面形状の共通有効半径における接線角");
        add("最小边缘厚度", "Minimum edge thickness", "最小縁厚");
        add("曲面有效域", "Surface validity", "曲面の有効領域");
        add("口径/厚度比", "Diameter/thickness ratio", "口径／厚さ比");
        add("前表面曲率", "Front surface curvature", "前面の曲率");
        add("后表面曲率", "Back surface curvature", "後面の曲率");
        add("前表面有效域", "Front surface validity", "前面の有効領域");
        add("后表面有效域", "Back surface validity", "後面の有効領域");
        add("前表面边缘斜率", "Front surface edge slope", "前面の縁傾斜角");
        add("后表面边缘斜率", "Back surface edge slope", "後面の縁傾斜角");
        add("前表面面型", "Front surface shape", "前面の形状");
        add("后表面面型", "Back surface shape", "後面の形状");
        add("超出实数矢高范围", "Outside the real sag domain", "実数サグの定義域外");
        add("无实数矢高", "No real sag", "実数サグなし");
        add("无有光焦度面", "No powered surfaces", "屈折力のある面なし");
        add("建议不小于 {0} mm，或由工艺人员确认夹持方案。", "Use at least {0} mm, or have the process team approve the holding method.", "{0} mm 以上を推奨します。工程担当者による保持方法の確認も可能です。");
        add("建议不小于 {0} mm，以满足倒边和装夹余量。", "Use at least {0} mm to allow for chamfering and holding.", "面取りと保持の余裕を確保するため、{0} mm 以上を推奨します。");
        add("高于设定上限 {0}，薄片加工和检测易变形。", "Above the limit of {0}; thin elements may deform during processing and inspection.", "上限 {0} を超えています。薄い素子は加工・検査中に変形する恐れがあります。");
        add("超过 {0}°，需要专用工装或非传统加工路线。", "Above {0}°; special tooling or a non-conventional process is needed.", "{0}° を超えています。専用治具または特殊な加工工程が必要です。");
        add("中心厚度必须大于 0 mm。", "Center thickness must be greater than 0 mm.", "中心厚は 0 mm より大きい必要があります。");
        add("减小净口径、增大曲率半径或检查圆锥系数。", "Reduce the clear aperture, increase the radius, or check the conic constant.", "有効口径を減らすか曲率半径を増やし、円錐定数を確認してください。");
        add("两曲面发生相交，必须修改半径、口径或中心厚度。", "The surfaces intersect. Change the radius, aperture, or center thickness.", "両面が交差しています。半径、口径、中心厚を変更してください。");
        add("曲面较陡，建议确认磨削工具、抛光头可达性和检测补偿能力。", "The surface is steep. Confirm tool access, polishing access, and inspection compensation.", "急な曲面です。研削工具、研磨工具の到達性、検査補正能力を確認してください。");
        add("减小口径或重新检查完整面型参数。", "Reduce the aperture or check the complete surface parameters.", "口径を減らすか、面形状の全パラメータを確認してください。");
        add("属于特殊面型，建议采用 CNC/磁流变加工并明确面形检测数据接口。", "A special surface requires an appropriate CNC/MRF process and a defined inspection data interface.", "特殊面形状です。CNC／磁気粘性加工工程と面形状検査のデータ仕様を確認してください。");
        add("综合评估", "Overall assessment", "総合評価");
        add("满足当前规则", "Meets the current rules", "現在のルールを満たしています");
        add("可进入公差细化和工艺审核。", "Proceed to tolerance detailing and process review.", "公差の詳細設計と工程確認に進めます。");
        add("规则用于设计阶段筛查，最终结论应由加工和检验人员审核。", "These rules support design screening. Manufacturing and inspection teams must review the final conclusions.", "設計段階の検査用ルールです。最終判断は加工・検査担当者が確認してください。");
    }
}
