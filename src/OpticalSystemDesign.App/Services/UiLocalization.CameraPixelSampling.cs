namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddCameraPixelSamplingTexts(Action<string, string, string> add)
    {
        add("局部采样检查", "Local Sampling Check", "局所サンプリング確認");
        add("采样检查", "Sampling Check", "サンプリング確認");
        add("检查项目", "Check", "確認項目");
        add("瞳面加密", "Pupil Grid", "瞳グリッド");
        add("场景加密", "Scene Grid", "シーングリッド");
        add("像元加密", "Pixel Area", "画素面積");
        add("窗口扩大", "PSF Window", "PSF窓");
        add("保护带扩大", "Source Guard", "光源余白");
        add("检查本项", "Check Selected", "選択項目を確認");
        add("导出采样报告", "Export Sampling", "確認結果を出力");
        add("起点 X (从 0 开始)", "Origin X (0-based)", "開始X (0から)");
        add("起点 Y (从 0 开始)", "Origin Y (0-based)", "開始Y (0から)");
        add("区域宽度 (像元)", "Width (px)", "領域幅 (画素)");
        add("区域高度 (像元)", "Height (px)", "領域高さ (画素)");
        add("像元面积与 QE", "Pixel Area and QE", "画素面積とQE");
        add("像元采样", "Pixel Sampling", "画素サンプリング");
        add("像素中心", "Pixel Center", "画素中心");
        add("面积积分 2 × 2", "Area 2 × 2", "面積積分 2 × 2");
        add("面积积分 4 × 4", "Area 4 × 4", "面積積分 4 × 4");
        add("填充因子 (0–1)", "Fill Factor (0–1)", "開口率 (0–1)");
        add("QE 面积口径", "QE Area Reference", "QEの面積基準");
        add("整像元总 QE", "Whole pixel", "画素全体のQE");
        add("感光区 QE", "Active area", "感光領域のQE");
        add("三维场景与照明", "3D Scene and Lighting", "3Dシーンと照明");
        add("场景绝对波段", "Scene Band Powers", "シーンの帯域パワー");
        add("载入示例", "Load Example", "例を読み込む");
        add("打开三维场景", "Open 3D Scene", "3Dシーンを開く");
        add("保存三维场景", "Save 3D Scene", "3Dシーンを保存");
        add("物体深度偏移 (mm)", "Object Depth (mm)", "物体深度の変位 (mm)");
        add("点光源功率倍率", "Light Power Scale", "点光源パワー倍率");
        add("瞳面径向环数", "Pupil Rings", "瞳の径方向点数");
        add("每环方位点数", "Points per Ring", "各環の方位点数");
        add("计算裸界面 Fresnel 损失", "Bare Interface Fresnel Loss", "裸界面のFresnel損失");
        add("衍射瞳面每边采样", "Pupil Grid", "回折瞳グリッド");
        add("每像元场景细分", "Scene Subdivision", "シーン細分");
        add("衍射窗口半径 (像元)", "PSF Radius (px)", "PSF半径 (画素)");
        add("源保护带 (像元)", "Source Guard (px)", "光源余白 (画素)");
        add("可见表面反射率 · 几何预览", "Visible Surface Reflectance", "可視表面の反射率");
        add("像面辐照度 · 满白每波段 0.02 W/m²", "Image Irradiance · 0.02 W/m² per band", "像面照度・各帯域0.02 W/m²");
        add("保留原物理 PSF 范围和计算预算时，当前镜头使用 2×2；4×4 用于理想光学对照。", "Use 2×2 with the current lens to preserve PSF support and work limits; 4×4 is available for ideal optics.", "PSFの物理範囲と計算上限を保持するため現在のレンズは2×2を使用し、4×4は理想光学に使用します。");
        add("导入曲线定义为整像元总 QE，填充损失不会重复计算。", "Imported curves specify whole-pixel QE; fill loss is not counted again.", "読み込んだ曲線は画素全体のQEであり、開口損失を重複適用しません。");
        add("填充因子用于面积采样；中心采样保持原模型。", "Fill factor applies to area sampling; center sampling keeps the original model.", "開口率は面積サンプリングに適用します。中心サンプリングは従来のモデルです。");
        add("QE 面积口径用于面积采样；中心采样保持原模型。", "QE area reference applies to area sampling; center sampling keeps the original model.", "QE面積基準は面積サンプリングに適用します。中心サンプリングは従来のモデルです。");
        add("面积积分先计算细网格光学像，再平均中心方形感光区，最后生成 Bayer RAW。整像元总 QE 已含填充损失；感光区 QE 才额外乘填充因子。有限采样需检查收敛；可降低分辨率控制计算量。",
            "Area integration computes a fine optical grid, averages a centered square active area, then generates Bayer RAW. Whole-pixel QE already includes fill loss; only active-area QE adds that loss. Check sampling convergence; lower resolution to limit work.",
            "細かい光学格子を計算し、中心の正方形感光領域を平均してからBayer RAWを生成します。画素全体のQEは開口損失を含み、感光領域QEのみ開口率を追加適用します。収束を確認し、解像度で計算量を調整してください。");
    }
}
