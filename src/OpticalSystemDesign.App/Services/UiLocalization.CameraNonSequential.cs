namespace OpticalSystemDesign.App.Services;

internal static partial class UiLocalization
{
    private static void AddCameraNonSequentialTexts(Action<string, string, string> add)
    {
        add("非序列探测器", "Non-sequential detector", "非順序検出器");
        add("Lambertian 方向散射", "Lambertian directional scattering", "Lambertian方向散乱");
        add("导入实测 BSDF CSV", "Import measured BSDF CSV", "測定BSDF CSVを読み込む");
        add("导入 BSDF 文件", "Import BSDF file", "BSDFファイルを読み込む");
        add("文件导入说明", "File import details", "ファイル読み込みの詳細");
        add("Zemax 测量波长 (nm)", "Zemax measurement wavelength (nm)", "Zemax測定波長 (nm)");
        add("Zemax 原数据为辐射度（不是光度 Y）", "Zemax source is radiometric (not photometric Y)", "Zemax入力は放射量（測光Yではない）");
        add("Zemax 正面入射折射率", "Zemax front incident index", "Zemax表側の入射屈折率");
        add("Zemax 正面出射折射率", "Zemax front outgoing index", "Zemax表側の出射屈折率");
        add("正面入射折射率（Zemax / 镜面增强）", "Front incident index (Zemax / peak)", "表側入射屈折率（Zemax / 鏡面）");
        add("正面出射折射率（Zemax / 镜面增强）", "Front outgoing index (Zemax / peak)", "表側出射屈折率（Zemax / 鏡面）");
        add("XYZ 辐射度校准 JSON 路径", "XYZ radiometric calibration JSON path", "XYZ放射量校正JSONパス");
        add("选择 XYZ 校准文件", "Select XYZ calibration", "XYZ校正ファイルを選択");
        add("XYZ 辐射度校准", "XYZ radiometric calibration", "XYZ放射量校正");
        add("Speos 显式镜面锥（覆盖文件设置）", "Speos explicit peak cone (override file)", "Speos鏡面コーン（ファイルを上書き）");
        add("镜面锥半角 (°)", "Peak cone half-angle (°)", "鏡面コーン半角 (°)");
        add("锥内背景高度 / 本切片峰值", "Cone background height / slice maximum", "コーン背景高さ / 最大値");
        add("White Specular（需镜面锥）", "White Specular (requires cone)", "White Specular（コーン必須）");
        add("White Specular 基准波长 (nm)", "White Specular base wavelength (nm)", "White Specular基準波長 (nm)");
        add("XYZ 须用实测辐射度校准；镜面峰采用守恒的斜率配准，背景固定。校准与散射数据随工程和相机实验冻结。", "XYZ requires measured radiometric calibration. Peaks use conservative slope registration; background stays fixed. Calibration and scattering data are frozen with projects and camera experiments.", "XYZには放射量校正が必要です。鏡面ピークには保存的な傾き配准を使用し、背景は固定します。校正と散乱データはプロジェクトとカメラ実験に保存されます。");
        add("商业文件入射侧（CSV 使用文件自身定义）", "Commercial file incident side (CSV uses its own sides)", "商用ファイルの入射側（CSVはファイル指定）");
        add("仅正面", "Front only", "表側のみ"); add("仅背面", "Back only", "裏側のみ");
        add("正反相同（显式声明）", "Both identical (explicit declaration)", "表裏同一（明示指定）");
        add("局部材料轴 X", "Local material axis X", "局所材料軸X"); add("局部材料轴 Y", "Local material axis Y", "局所材料軸Y"); add("局部材料轴 Z", "Local material axis Z", "局所材料軸Z");
        add("清除 BSDF", "Clear BSDF", "BSDFをクリア");
        add("实测模型直接使用文件中的 BRDF/BTDF；此反射率只用于 Lambertian。", "Measured models use the file's BRDF/BTDF directly; this reflectance applies only to Lambertian.", "測定モデルはファイルのBRDF/BTDFを直接使用します。この反射率はLambertian専用です。");
        add("BTDF 使用正反两侧数据与折射交互；测量分布包含全部反射/透射，不再叠加 Fresnel。", "BTDF requires front/back measurements and refractive interaction; measured reflection/transmission replaces Fresnel.", "BTDFには表裏の測定値と屈折相互作用が必要です。測定分布がFresnel反射・透過を置き換えます。");
        add("散射统计为筛选前全部交互，含重复反弹。", "Scattering statistics cover all interactions before filtering, including repeated bounces.", "散乱統計はフィルター前の全相互作用を含み、反復反射も数えます。");
        add("漫反射率 ρ (0–1)", "Diffuse reflectance ρ (0–1)", "拡散反射率 ρ (0–1)");
        add("不透明表面：ρ 反射，其余吸收；替代整个表面反射，不与 Fresnel 叠加。", "Opaque surface: reflect fraction ρ and absorb the remainder; replaces surface reflection without adding Fresnel.", "不透明面：ρを反射し残りを吸収。表面反射を置換しFresnelを重ねません。");
        add("使用实验探测器与输入", "Use experiment detector and inputs", "実験の検出器と入力を使用");
        add("使用实验内嵌 NSC 功率", "Use embedded experiment NSC power", "実験に埋め込まれたNSCパワーを使用");
        add("请切换到非序列模式后读取探测器。", "Switch to non-sequential mode to read a detector.", "非順序モードに切り替えて検出器を読み込んでください。");
        add("探测器功率输入", "Detector power input", "検出器パワー入力");
        add("探测器光谱波段", "Detector spectral bands", "検出器のスペクトル帯域");
        add("非序列分析到 RAW / ISP", "Non-sequential analysis to RAW / ISP", "非順序解析からRAW / ISPへ");
        add("矩形探测器", "Rectangular detector", "矩形検出器");
        add("路径筛选", "Path filter", "経路フィルター");
        add("读取 NSC 结果", "Read NSC result", "NSC結果を読み込む");
        add("留空读取全部已保存路径", "Leave empty for all saved paths", "空欄で保存済みの全経路を読み込む");
        add("请先完成当前场景的非序列分析追迹。", "Complete the non-sequential analysis trace for this scene first.", "先に現在のシーンの非順序解析追跡を完了してください。");
        add("请先读取非序列探测器结果。", "Read a non-sequential detector result first.", "先に非順序検出器の結果を読み込んでください。");
        add("请选择非序列探测器输入。", "Select non-sequential detector input.", "非順序検出器入力を選択してください。");
        add("非序列分析结果已过期或未完成，请重新追迹。", "The non-sequential analysis is stale or incomplete. Trace again.", "非順序解析は古いか未完了です。再追跡してください。");
        add("非序列输入需要覆盖探测波长的绝对 QE 曲线。", "Non-sequential input requires an absolute QE curve covering the detector wavelengths.", "非順序入力には検出波長をカバーする絶対QE曲線が必要です。");
        add("绝对 QE 曲线必须覆盖全部探测波长。", "The absolute QE curve must cover all detector wavelengths.", "絶対QE曲線は全検出波長をカバーする必要があります。");
        add("传感器须为与 NSC 探测器完全匹配的方形像元。", "Square sensor pixels must exactly match the NSC detector.", "センサーの正方形画素はNSC検出器と完全に一致する必要があります。");
        add("光学传播已经包含在 NSC 功率中。", "Optical propagation is already included in the NSC power.", "光学伝搬はNSCパワーに既に含まれています。");
        add("使用冻结探测器的实际波长表。", "Use the frozen detector wavelength table.", "固定した検出器の実際の波長表を使用します。");
        add("尺寸由冻结的 NSC 探测器决定。", "Dimensions follow the frozen NSC detector.", "寸法は固定したNSC検出器によって決まります。");
        add("NSC 功率已包含整个像元的积分。", "NSC power already includes integration over the full pixel.", "NSCパワーは全画素の積分を既に含んでいます。");
        add("NSC 使用整像元绝对 QE。", "NSC uses whole-pixel absolute QE.", "NSCは全画素の絶対QEを使用します。");
        add("NSC 功率已读取，请导入匹配的 QE 后运行 B；A 保留。", "NSC power loaded. Import matching QE and run B; A is retained.", "NSCパワーを読み込みました。対応するQEを読み込みBを実行してください。Aは保持されます。");
        add("NSC 分析会话已改变，请重新读取探测器。", "The NSC analysis session changed. Read the detector again.", "NSC解析セッションが変更されました。検出器を再度読み込んでください。");
        add("探测器选择已改变，请读取 NSC 结果。", "Detector selection changed. Read the NSC result.", "検出器の選択が変更されました。NSC結果を読み込んでください。");
        add("路径筛选已改变，请读取 NSC 结果。", "Path filter changed. Read the NSC result.", "経路フィルターが変更されました。NSC結果を読み込んでください。");
        add("读取已取消，原 A/B 保留。", "Read cancelled; the original A/B results are retained.", "読み込みをキャンセルしました。元のA/B結果は保持されます。");
        add("NSC 读取失败，原 A/B 保留：", "NSC read failed; original A/B retained: ", "NSC読み込み失敗。元のA/Bを保持：");
        add("取消使用实验输入后，可读取当前 NSC 结果。", "Disable experiment inputs to read the current NSC result.", "実験入力を無効にすると現在のNSC結果を読み込めます。");
        add("取消使用实验输入后，可选择当前探测器。", "Disable experiment inputs to select a current detector.", "実験入力を無効にすると現在の検出器を選択できます。");
        add("取消使用实验输入后，可选择当前路径。", "Disable experiment inputs to select current paths.", "実験入力を無効にすると現在の経路を選択できます。");
        add("光学传播已经包含在 NSC 功率中；只应用一次曝光、QE、噪声、ADC 和 ISP。", "NSC power already includes optics; exposure, QE, noise, ADC and ISP are applied once.", "光学伝搬はNSCパワーに含まれます。露光、QE、ノイズ、ADC、ISPは一度だけ適用します。");
        add("非序列输入使用冻结的分波长 W/像元；光源、材料与镜头已经在 NSC 中计算。", "Non-sequential input uses frozen spectral W/pixel; sources, materials and lenses were computed in NSC.", "非順序入力は固定した波長別W/画素を使用します。光源、材料、レンズはNSCで計算済みです。");
        add("NSC 总辐照度 · 固定标尺灰度预览", "NSC total irradiance · fixed-scale grayscale preview", "NSC総放射照度・固定スケールのグレー表示");
        add("已计算光学功率 · 不再次传播", "Computed optical power · no additional propagation", "計算済み光学パワー・再伝搬なし");
        add("先在非序列追迹控制中完成分析，再读取探测器。功率包含所选已保存路径，不另加顺序主成像。导入覆盖实际波长的绝对 QE，运行生成 RAW/ISP。像元和尺寸必须与探测器匹配；完整实验保留功率与来源，复算不重新追迹。",
            "Complete an NSC analysis trace, then read a detector. Power includes the selected saved paths with no extra sequential image. Import absolute QE covering the wavelengths and run RAW/ISP. Pixels and dimensions must match. Complete experiments retain power and provenance; recomputation does not retrace NSC.",
            "NSC解析追跡を完了して検出器を読み込みます。パワーは選択した保存済み経路を含み、順序主像を追加しません。実際の波長をカバーする絶対QEを読み込みRAW/ISPを実行します。画素と寸法は一致が必要です。完全な実験はパワーと出所を保持し、再計算では再追跡しません。");
    }
}
