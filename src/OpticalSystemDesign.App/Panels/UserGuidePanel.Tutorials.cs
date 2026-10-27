namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    private static IEnumerable<GuideChapter> TutorialChapters()
    {
        yield return Tutorial("first-system", "建立并保存第一个系统", "Build and save your first system", "最初の光学系を作成・保存",
            "从示例开始，确认系统条件，再编辑处方并保存工程。", "Start with an example, check system conditions, edit the prescription, and save a project.", "例から開始し、システム条件と処方を確認してプロジェクトを保存します。",
            S("1 · 打开示例并核对系统", "1 · Open an example and check the system", "1 · 例を開いて条件を確認",
                "在文件功能区打开示例或已有工程。左侧系统选项中先核对孔径、视场、波长和单位；不要只根据二维外形判断系统是否正确。初次打开顺序系统时，镜头数据处于活动状态，二维视图作为另一文档页打开。",
                "Open an example or existing project from File. First check aperture, fields, wavelengths, and units in System Options. A plausible 2D outline alone does not establish a correct system. Sequential startup activates Lens Data and also opens 2D Layout as a document tab.",
                "ファイルから例または既存プロジェクトを開きます。システムオプションで開口、視野、波長、単位を確認します。二次元形状だけでは正しさを判断できません。順次モードではレンズデータがアクティブになり、2D レイアウトも開きます。") with
            { Screenshot = "workbench" },
            S("2 · 编辑、检查、保存", "2 · Edit, check, and save", "2 · 編集・確認・保存",
                "在镜头数据中编辑曲率半径、厚度、材料和半口径，确认光阑面及像面位置。材料非空的行以蓝底提示。修改后查看二维光线是否到达像面，再运行点列检查。文件 → 另存为，使用 .staropt 保存完整工程；导入文件应保留原件，导出交换格式后还需核对其支持的面型和参数。",
                "Edit radius, thickness, material, and semi-diameter in Lens Data, then check the stop and image surface. Material-filled rows have a blue background. Inspect propagation in 2D and run a spot diagram. Use File → Save As and .staropt for the full project. Preserve imported originals and check which surfaces and parameters an interchange export supports.",
                "レンズデータで曲率半径、厚さ、材料、半径を編集し、絞りと像面を確認します。材料のある行は青色です。2D の伝搬とスポットを確認し、ファイル → 名前を付けて保存から .staropt で保存します。インポート原本を保持し、交換形式の対応面型とパラメーターを確認してください。") with
            { Screenshot = "projects" });

        yield return Tutorial("views", "查看二维、三维与实体模型", "Inspect layouts and solid models", "2D・3D・ソリッドモデルの確認",
            "用视图检查结构；用实际分析结果判断光学性能。", "Use layouts to inspect geometry and analyses to assess optical performance.", "レイアウトで構造を確認し、解析結果で光学性能を評価します。",
            S("1 · 二维视图", "1 · 2D Layout", "1 · 2D レイアウト",
                "在视图功能区打开二维视图。设置中选择视场、波长和光线采样；处方修改后等待刷新，核对光线是否被口径截断。光线数量是显示采样条件，不能替代 PSF 或能量分析的采样设置。",
                "Open 2D Layout from View. Select fields, wavelengths, and ray sampling in its settings. After an edit, wait for refresh and inspect aperture clipping. Displayed ray counts do not replace the sampling settings of PSF or energy analyses.",
                "表示から 2D レイアウトを開き、設定で視野、波長、光線サンプリングを選びます。処方変更後は更新を待ち、開口による遮光を確認します。表示光線数は PSF やエネルギー解析のサンプリング条件とは別です。") with
            { Screenshot = "layout-2d" },
            S("2 · 旋转、切换方向与导出", "2 · Rotate, choose a view, and export", "2 · 回転・方向切替・出力",
                "打开三维视图或实体模型，使用图内底部的带文字方向按钮切换前、后、左、右、俯、仰和等轴测方向。“适配”调整取景；自由旋转会取消预设高亮。左下角 XYZ 随相机方向同步。实体模型可选择透明、线框、切面等显示，材料图例位于视口下方。需要图片时使用设置旁的相机按钮导出当前视图。",
                "Open 3D Layout or Solid Model and use the labeled in-view buttons for front, back, left, right, top, bottom, or isometric views. Fit adjusts framing; free rotation clears the preset highlight. The lower-left XYZ triad follows camera orientation. Solid Model offers transparent, wireframe, and cutaway displays, with its material legend below the viewport. Export the current view with the camera button beside Settings.",
                "3D レイアウトまたはソリッドモデルを開き、画面内下部の文字付きボタンで方向を選びます。フィットは表示範囲を調整し、自由回転でプリセット選択を解除します。左下 XYZ はカメラ方向に連動します。ソリッドモデルでは透明・ワイヤー・断面表示を選択でき、材料凡例は下部にあります。設定横のカメラボタンで現在の画像を出力します。") with
            { Screenshot = "solid-model" });

        yield return Tutorial("analysis", "运行并阅读成像分析", "Run and interpret imaging analyses", "結像解析の実行と読み方",
            "用同一处方、视场和波长比较点列、MTF 与波前。", "Compare spot, MTF, and wavefront results with consistent prescription, field, and wavelength settings.", "同じ処方・視野・波長条件でスポット、MTF、波面を比較します。",
            S("1 · 点列：检查采样与参考点", "1 · Spot: sampling and reference", "1 · スポット：サンプルと基準",
                "在分析功能区打开点列图，先设置视场、波长、光瞳采样和参考方式，再运行。读取每个视场的 RMS、几何半径及坐标单位。质心参考与主光线参考定义不同；跨软件比较前应统一。右上图例可开关显示序列，不会重新计算或改变原结果范围。",
                "Open Spot Diagram from Analysis. Set fields, wavelengths, pupil sampling, and reference before running. Read RMS, geometric radius, and axis units for each field. Centroid and chief-ray references differ; align them before comparing tools. The upper-right legend filters visible series without recalculating or changing the source result range.",
                "解析からスポット図を開き、視野、波長、瞳サンプル、基準を設定して実行します。各視野の RMS、幾何半径、軸単位を読みます。重心と主光線基準は異なるため比較時に統一します。右上凡例は再計算せずに表示系列を切り替えます。") with
            { Screenshot = "spot" },
            S("2 · MTF：先辨认算法与频率单位", "2 · MTF: method and frequency units", "2 · MTF：手法と周波数単位",
                "在 MTF 曲线菜单中选择傅里叶、Huygens 或几何类型。设置最大频率与采样，读取子午、弧矢及衍射极限曲线，横轴以结果标明的空间频率单位为准。几何 MTF 与衍射 MTF 的物理模型不同，不能只对比相似曲线标题。",
                "Choose Fourier, Huygens, or Geometric from MTF Curves. Set maximum frequency and sampling, and inspect tangential, sagittal, and diffraction-limit curves. Use the spatial-frequency units published on the result. Geometric and diffraction MTF use different physical models.",
                "MTF 曲線から Fourier、Huygens、幾何を選択します。最大周波数とサンプル数を設定し、タンジェンシャル・サジタル・回折限界を読みます。結果の空間周波数単位を確認してください。幾何と回折 MTF は物理モデルが異なります。") with
            { Screenshot = "mtf" },
            S("3 · 波前与结果导出", "3 · Wavefront and result export", "3 · 波面と結果出力",
                "打开波前图，确认波长、视场和参考，按需要选择曲面、等高线或二维热图。结合 PV、RMS 与有效光瞳查看，单位为 waves 时必须同时记录波长。使用分析页导出图片或数据，保留参数和单位；编辑处方后旧结果会过期，应重新运行。计算原理分类说明 OPD、PSF 和 MTF 的关系。",
                "Open Wavefront Map and check wavelength, field, and reference. Choose surface, contour, or 2D heatmap. Read PV/RMS with the valid pupil; values in waves require a recorded wavelength. Export images or data with settings and units. Prescription edits make results stale and require rerunning. How calculations work explains OPD, PSF, and MTF.",
                "波面図で波長、視野、基準を確認し、曲面・等高線・2D ヒートマップを選びます。有効瞳と PV/RMS を併せて読み、waves には波長を記録します。条件と単位を付けて画像・データを出力します。処方変更後は再実行が必要です。OPD・PSF・MTF の関係は計算原理を参照してください。") with
            { Screenshot = "wavefront" });

        yield return Tutorial("optimization", "设置变量并优化", "Set variables and optimize", "変数設定と最適化",
            "先定义目标和边界，再检查优化后的实际处方。", "Define goals and bounds, then inspect the resulting prescription.", "目標と境界を定義し、最適化後の実処方を確認します。",
            S("1 · 准备变量与评价函数", "1 · Variables and merit function", "1 · 変数とメリット関数",
                "在镜头数据中把需要调整的半径、厚度等设为变量，设置合理边界；固定不能变化的条件。在优化功能区打开评价函数，可先用向导建立 RMS 点列目标，再补充焦距、总长等约束。检查每行操作数、目标、权重和视场/波长索引。正权重为目标，负权重为约束，零权重仅监视。",
                "Mark adjustable radii, thicknesses, or other parameters as variables in Lens Data and give them sensible bounds. Keep required conditions fixed. Open the merit function from Optimization; start with the RMS spot wizard and add focal-length or track-length constraints. Check operands, targets, weights, and field/wavelength indices. Positive weights are objectives, negative weights constraints, and zero weights monitors.",
                "レンズデータで半径や厚さを変数にし、適切な境界を設定します。固定条件は保持します。最適化からメリット関数を開き、RMS スポットのウィザードと焦点距離・全長の制約を設定します。オペランド、目標、重み、視野・波長番号を確認します。正の重みは目的、負は制約、ゼロは監視です。") with
            { Screenshot = "optimization" },
            S("2 · 运行并复核", "2 · Run and verify", "2 · 実行と再確認",
                "保存名义工程后选择优化器、预算与停止条件，开始运行。不要只看评价值降低：还要检查约束误差、变量是否碰到边界、所有视场的光线是否有效，并用点列和 MTF 复算。取消、预算耗尽和收敛是不同状态；保存经过复核的结果为新的工程版本。",
                "Save the nominal design, choose an optimizer, budget, and stopping conditions, then run. Beyond a lower merit value, check constraint errors, variables at bounds, and valid rays across every field. Recalculate spot and MTF results. Cancellation, exhausted budget, and convergence are distinct outcomes. Save the verified design as a new project version.",
                "公称設計を保存し、最適化器、予算、停止条件を選んで実行します。評価値だけでなく、制約誤差、境界に達した変数、全視野の有効光線を確認し、スポットと MTF を再計算します。取消・予算終了・収束を区別し、確認済み設計を別バージョンで保存します。"));

        yield return Tutorial("tolerance", "公差分析与工程图", "Tolerancing and engineering drawings", "公差解析と光学図面",
            "把名义性能、制造扰动与图纸要求对应起来。", "Connect nominal performance, manufacturing variation, and drawing requirements.", "公称性能、製造変動、図面条件を対応させます。",
            S("1 · 公差表和运行设置", "1 · Tolerance table and run settings", "1 · 公差表と実行設定",
                "打开公差表，选择被扰动的面或参数，输入上下限及补偿器。打开运行设置，确认评价指标、视场、波长、Monte Carlo 次数、分布和随机种子。先运行灵敏度检查最敏感项，再运行 Monte Carlo；比较良率前必须使用相同指标和合格阈值。失败试验也应在报告中核对。",
                "Open the tolerance table and set affected surfaces or parameters, limits, and compensators. In run settings, check the criterion, fields, wavelengths, Monte Carlo trials, distribution, and seed. Inspect sensitivity first, then run Monte Carlo. Yield comparisons require the same criterion and pass threshold; review failed trials in the report too.",
                "公差表で対象面・パラメーター、上下限、補償器を設定します。実行設定で評価指標、視野、波長、Monte Carlo 回数、分布、シードを確認します。感度解析の後に Monte Carlo を実行します。歩留まり比較では指標と合格基準を統一し、失敗試行も確認します。") with
            { Screenshot = "tolerance-settings" },
            S("2 · 制图与输出", "2 · Drawing and output", "2 · 図面と出力",
                "打开工程图，选择图纸标准与被绘制元件，再核对尺寸、材料、表面和技术要求。标题栏中的标准品牌标识使用矢量资源；自定义图标是单独的用户内容。导出前检查纸张与比例。工程图是制造沟通输出，不能代替名义分析和公差合格判定。",
                "Open Optical Drawing, select the standard and element, and check dimensions, materials, surfaces, and notes. The standard title-block brand mark uses vector artwork; a user-supplied mark is separate content. Check paper and scale before export. A drawing communicates manufacturing requirements and does not replace nominal or tolerance acceptance.",
                "光学図面で規格と対象部品を選び、寸法、材料、面、技術条件を確認します。標準タイトル欄のブランドはベクターです。ユーザー画像は別扱いです。出力前に用紙と尺度を確認します。図面は公称性能や公差の合否判定に代わるものではありません。") with
            { Screenshot = "drawing" });

        yield return Tutorial("non-sequential", "追迹非序列场景与读取探测器", "Trace a non-sequential scene and read detectors", "ノンシーケンシャル追跡と検出器",
            "明确光源功率、对象位置和探测器采样，再运行追迹。", "Set source power, object poses, and detector sampling before tracing.", "光源パワー、物体配置、検出器サンプルを設定して追跡します。",
            S("1 · 建立场景", "1 · Build the scene", "1 · シーンの作成",
                "在工作模式中切换到非序列，保存当前顺序工程后添加光源、实体和矩形探测器。检查对象的启用状态、位置、姿态、材料、光源波长及总功率。探测器宽高与像素数决定接收范围和像素面积，不能只根据屏幕上的框选大小判断。",
                "Save the sequential project and switch to Non-Sequential. Add sources, solids, and a rectangular detector. Check enabled states, positions, orientations, materials, source wavelengths, and total power. Detector width, height, and pixel counts define its acceptance region and pixel area; the drawn outline alone is insufficient.",
                "順次プロジェクトを保存してノンシーケンシャルに切り替え、光源、実体、矩形検出器を追加します。有効状態、位置、姿勢、材料、波長、総パワーを確認します。検出器の幅・高さ・画素数が受光範囲と画素面積を定義します。") with
            { Screenshot = "non-sequential" },
            S("2 · 追迹、清空与探测器", "2 · Trace, clear, and inspect", "2 · 追跡・クリア・検出",
                "在追迹设置中确认光线预算、分裂、散射及数据库输出条件。需要独立结果时先清空再追迹；累加用于明确需要合并的试验。打开探测器查看器，选择探测器、显示量和波长，区分功率与辐照度的单位。场景或材料改变后旧数据库可能过期，应重新追迹，并使用新的数据库文件路径保护已有结果。",
                "Check ray budget, splitting, scattering, and database output in trace settings. Clear before tracing for an independent result; accumulate only for deliberately combined runs. In Detector Viewer choose detector, quantity, and wavelength, distinguishing power from irradiance units. Scene or material changes can make a database stale. Retrace and use a new database file path to preserve existing results.",
                "追跡設定で光線予算、分岐、散乱、データベース出力を確認します。独立結果にはクリアして再追跡し、意図的な合算のみ累積を使います。検出器ビューアーで検出器、表示量、波長を選び、パワーと放射照度の単位を区別します。シーン・材料変更後は再追跡し、新しい出力先で既存結果を保護します。") with
            { Screenshot = "detector" });

        yield return Tutorial("libraries-initial", "使用镜头库与初始结构", "Use lens libraries and Initial Structure", "レンズライブラリと初期構造",
            "检查导入处方和候选来源，再在主工作台验证。", "Inspect imported prescriptions and candidate provenance, then validate in the workbench.", "導入処方と候補の出所を確認し、ワークベンチで検証します。",
            S("1 · 从库中选择", "1 · Select from a library", "1 · ライブラリから選択",
                "打开镜头库，按目录、名称或规格筛选，先查看处方及单位再载入。材料库提供色散模型，实际材料名称和目录也要核对。商业目录信息与当前工程是不同数据来源；载入后仍需检查孔径、波长和导入限制。",
                "Open Lens Library and filter by catalog, name, or specification. Inspect the prescription and units before loading. Material Library supplies dispersion models; also verify material names and catalogs. Commercial catalog data and the current project have different provenance. Recheck aperture, wavelengths, and import limitations after loading.",
                "レンズライブラリでカタログ、名称、仕様を絞り込み、処方と単位を確認して読み込みます。材料名とカタログも確認します。商用カタログと現在のプロジェクトは出所が異なります。導入後に開口、波長、対応範囲を再確認します。") with
            { Screenshot = "lens-library" },
            S("2 · 初始结构设置与候选检查", "2 · Initial Structure and candidate review", "2 · 初期構造と候補確認",
                "打开内置初始结构，在系统页设目标，在搜索页确认预算和计算设备，然后运行标准托管 AutoLens。自动设备优先尝试 CUDA；应读取实际设备与回退说明，不能把 CPU 回退当作 GPU 运行。查看候选的目标误差、有效光线和处方，再导入主工作台复算分析。实验记录与主工程分别保存。",
                "Open the built-in Initial Structure module, set goals on System, and confirm budget and compute device on Search before running managed AutoLens. Automatic prefers CUDA; read actual-device and fallback evidence. CPU fallback is not GPU execution. Review target errors, valid rays, and prescriptions, then import a candidate and recalculate in the workbench. Save experiment records separately from the main project.",
                "内蔵初期構造のシステムで目標を、検索で予算と計算デバイスを設定し、マネージド AutoLens を実行します。自動は CUDA を優先しますが実際のデバイスとフォールバックを確認します。候補の目標誤差、有効光線、処方を確認し、ワークベンチに導入して再計算します。実験記録は主プロジェクトと別に保存します。") with
            { Screenshot = "initial-structure" });

        yield return Tutorial("camera", "相机成像实验", "Camera imaging experiment", "カメラ結像実験",
            "记录光学模糊、传感器和曝光条件，理解显示图像的来源。", "Record optical blur, sensor, and exposure conditions to interpret the image.", "光学ぼけ、センサー、露光条件を記録して画像を解釈します。",
            S("1 · 输入与参数", "1 · Input and parameters", "1 · 入力とパラメーター",
                "打开相机成像，选择场景输入，设置输出画幅、像素、曝光和传感器参数，再选择光学模糊模型。先用较小图像检查视场和裁剪，确认横纵画幅比例。输入 RGB 是颜色数据，不自动变成实测光谱；定量实验应提供并记录光谱及辐射条件。",
                "Open Camera Imaging, select scene input, set frame dimensions, pixels, exposure, and sensor parameters, then choose the optical blur model. Use a small image first to inspect field coverage, cropping, and aspect ratio. RGB input is color data, not a measured spectrum; quantitative experiments require recorded spectral and radiometric conditions.",
                "カメラ結像で入力、画面寸法、画素、露光、センサー、光学ぼけを設定します。小画像で視野、切り取り、縦横比を確認します。RGB は実測スペクトルではありません。定量実験ではスペクトルと放射条件を記録します。") with
            { Screenshot = "camera" },
            S("2 · 运行与解释", "2 · Run and interpret", "2 · 実行と解釈",
                "运行后分别查看光学输出、传感器与 ISP 阶段，区分真实光学细节损失、噪声和显示处理。改变曝光、量子效率或噪声条件后重新运行；导出时保留输入和设置。计算原理中的相机章节给出光子数、电荷与输出图像的计算链。",
                "Inspect optical, sensor, and ISP stages separately to distinguish optical detail loss, noise, and display processing. Rerun after changing exposure, quantum efficiency, or noise. Preserve input and settings with exports. The camera calculation chapter explains the chain from photons to charge and the displayed image.",
                "光学、センサー、ISP の各段階を見て、光学的な細部損失、雑音、表示処理を区別します。露光、量子効率、雑音条件を変えたら再実行します。出力には入力と条件を保持します。計算原理に光子・電荷・出力画像の流れを示します。"));

        yield return Tutorial("coating-assembly", "镀膜设计与装调", "Coating design and optical assembly", "コーティング設計と調整",
            "两个内置工作区分别维护膜系实验和装调流程。", "The built-in workspaces maintain coating experiments and assembly workflows separately.", "内蔵ワークスペースで膜系実験と調整工程を管理します。",
            S("1 · 镀膜实验", "1 · Coating experiment", "1 · コーティング実験",
                "打开光学镀膜设计，选择基底、入射介质、膜层材料与厚度，设置波段、角度和偏振。生成或计算后阅读 R/T/A 与目标残差；吸收材料的透射率必须包含能量损失。保存膜系记录并明确应用对象，不能把实验页结果当作已自动写回主工程。",
                "Open Coating Design and set substrate, incident medium, layer materials and thicknesses, band, angle, and polarization. Generate or calculate, then inspect R/T/A and target residuals. For absorbing materials, transmittance must account for absorption. Save the experiment and identify its intended surfaces; results are not automatically committed to the main project.",
                "コーティング設計で基板、入射媒質、膜材料・厚さ、波長域、角度、偏光を設定します。生成・計算後に R/T/A と残差を確認します。吸収材料は損失を含めて透過率を求めます。実験と適用面を記録します。主プロジェクトへの自動反映ではありません。") with
            { Screenshot = "coating" },
            S("2 · 装调流程", "2 · Assembly workflow", "2 · 調整工程",
                "打开光学装调，确认当前系统和测量/补偿输入，按流程查看调整建议与残差。区分测量数据、模型计算和执行后的复测值；未输入测量不能当作真实仪器结果。保存记录后，在主工作台更新经确认的参数并复算。",
                "Open Optical Assembly, check the current system and measurement/compensation inputs, and review adjustment suggestions and residuals. Distinguish measured data, model predictions, and post-adjustment measurements. Missing measurements are not instrument evidence. Save records, update confirmed parameters in the workbench, and recalculate.",
                "光学調整でシステムと測定・補償入力を確認し、提案と残差を読みます。実測、モデル予測、調整後の再測定を区別します。未入力値は計測結果ではありません。記録を保存し、確認したパラメーターを主画面で更新して再計算します。") with
            { Screenshot = "assembly" });
    }

    private static GuideChapter Tutorial(string id, string zh, string en, string ja, string summaryZh, string summaryEn, string summaryJa, params GuideSection[] sections)
        => new("tutorial-" + id, T("逐步操作教程", "Step-by-step tutorials", "操作チュートリアル"), T(zh, en, ja), T(summaryZh, summaryEn, summaryJa), UserGuideFigureKind.Workflow, sections);
}
