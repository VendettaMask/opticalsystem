namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    private static IEnumerable<GuideChapter> ComputationChapters()
    {
        yield return Calculation("pipeline", "计算总览：从处方到结果", "From prescription to results", "処方から結果まで",
            "先理解一次分析使用什么输入、经过哪些计算，以及结果为何会随设置变化。",
            "Understand the input snapshot, calculation stages and settings behind a result.", "入力スナップショット、計算段階、結果が設定で変わる理由を説明します。",
            S("1 读取同一份工程快照", "1 Read one project snapshot", "1 同一スナップショットを読む",
                "分析先捕获指定结构的处方、孔径、视场、波长、材料及相关设置。编辑器、二维/三维视图、正式分析和内置实验室共用 C# 计算核心；显示的网格、颜色和窗口大小不会替代或改变光学处方。工程改变后旧结果应重新同步。",
                "An analysis captures the selected configuration, aperture, fields, wavelengths, materials and settings. Editors, layouts, formal analyses and laboratories use the shared C# computation core. Display meshes, colors and window size do not replace the prescription. Resynchronize results after editing the project.",
                "解析は指定構成、開口、視野、波長、材料、設定を取得します。エディター、表示、正式解析、ラボは共通の C# 計算コアを使用し、表示メッシュ・色・ウィンドウサイズは光学処方を変更しません。編集後は結果を再同期します。"),
            S("2 生成光线并求值", "2 Generate rays and evaluate", "2 光線を生成して評価",
                "按视场与波长选择生成光瞳样本，经过瞄准、相交、口径裁剪、折射/反射和光程累计，再进入点列、波前、衍射、能量或优化计算。不同分析有各自的参考方式与采样；不能只因为都叫 RMS 就混用数值。",
                "Pupil samples are generated for the selected fields and wavelengths, aimed, intersected, aperture-tested, refracted/reflected and accumulated in optical path. Spot, wavefront, diffraction, energy and optimization calculations then use their own reference and sampling conventions. Two values called RMS are not automatically comparable.",
                "視野・波長に応じて瞳サンプルを生成し、照準、交差、開口判定、屈折・反射、光路長計算を行います。その後のスポット、波面、回折、エネルギー、最適化は固有の基準とサンプリングを使用します。RMS という名称だけで値を混用できません。") with
            { Screenshot = "spot" });

        yield return Calculation("units-materials", "单位、坐标与材料色散", "Units, coordinates and dispersion", "単位・座標・分散",
            "明确毫米、角度、波长、局部坐标与玻璃模型之间的关系。", "Track length, angles, wavelengths, local frames and glass models.", "長さ、角度、波長、ローカル座標、材料モデルを確認します。",
            S("输入单位与坐标", "Input units and frames", "入力単位と座標系",
                "工程几何通常用 mm，材料查询波长用 nm；部分分析接口或显示使用 μm，微米与纳米需按下式换算。视场可以是角度或物/像高，不能把同一个数直接互换。表面在自身局部坐标中求交，再变换到全局；偏心、倾斜和折返必须使用真实坐标变换。物面正无穷厚度表示无穷远物，零厚度仍是有限共面物。",
                "Geometry normally uses mm and material queries use nm; some analysis interfaces/displays use μm, with the conversion shown below. Angular fields and object/image heights are different inputs. Intersections use local surface frames and transform back to global coordinates, preserving decenter, tilt and folded paths. Positive infinite Object thickness means an infinite conjugate; zero remains a finite coincident object.",
                "形状は通常 mm、材料照会は nm を使い、一部の解析・表示は μm を使います。換算は下式に示します。角度視野と物高・像高は別の入力です。交差は面のローカル座標で求め、偏心・傾斜・折返しを保持して全体座標へ変換します。物体面の正の無限厚さだけが無限遠を表し、ゼロは有限の同一平面です。"),
            S("折射率与消光", "Index and extinction", "屈折率と消衰係数",
                "材料按保存的实际模型计算 n(λ) 和 k(λ)，可为目录色散式、Sellmeier、Cauchy 或表格插值。下式是 Sellmeier 形式的示例，λ 通常以 μm 代入，系数单位以该模型约定为准；不能把其他色散式的系数直接套入。材料名称相同也可能有不同模型，计算前应核对目录、有效波段和来源。",
                "Materials evaluate n(λ) and k(λ) from the stored model: catalog dispersion, Sellmeier, Cauchy or tabulated interpolation. The example is a Sellmeier form, usually with λ in μm; coefficient units follow the model. Do not substitute coefficients from another dispersion formula. A shared name does not prove identical physics: check catalog, wavelength range and provenance.",
                "材料は保存モデルから n(λ)、k(λ) を求めます。カタログ式、Sellmeier、Cauchy、表の補間などを使います。例は通常 λ を μm とする Sellmeier 形式ですが、係数単位はモデルに従います。別形式の係数を代入しないでください。同じ材料名でも物理モデルが同じとは限りません。") with
            { Formula = "dispersion", Screenshot = "workbench" });

        yield return Calculation("ray-tracing", "实光线如何相交、折射与反射", "Real-ray intersections, refraction and reflection", "実光線の交差・屈折・反射",
            "沿实际面型求交并累计几何长度与光程，显示光线不是用示意线猜出来的。", "Trace the actual surfaces and accumulate geometric and optical path lengths.", "実際の面形状を追跡し、幾何学的距離と光路長を累積します。",
            S("求交与口径判定", "Intersections and apertures", "交差と開口判定",
                "按下式表示光线，求解真实面方程后检查传播方向、有效范围和净口径。球面、非球面和自由曲面按对应几何模型计算；需要迭代的求交有收敛与有效性检查。未相交、被遮挡和超出口径属于失败光线，不能伪造像点。网格仅用于相应网格对象或显示，不用旋转二维截面替代一般三维面型。",
                "Use the ray equation below, solve the surface equation, then test propagation direction, domain and clear aperture. Spherical, aspheric and freeform surfaces use their actual geometry models; iterative intersections check convergence and validity. Missed, obscured or clipped rays are failures, not invented image points. General 3D surfaces are not replaced by a rotated 2D sketch.",
                "下式の光線と実面方程式の交点を求め、進行方向、定義域、有効開口を検査します。球面・非球面・自由曲面は対応する実形状を使い、反復計算は収束を確認します。交差失敗や遮蔽光線から像点を作りません。一般の 3D 面を 2D 断面の回転で置き換えません。") with
            { Formula = "optical-path" },
            S("界面与全反射", "Interfaces and total internal reflection", "界面と全反射",
                "在交点处按局部法线应用矢量折射/反射。Snell 定律给出折射角；高折射率向低折射率传播且无实折射方向时按全反射处理。反射方向保留真实传播朝向，折返系统不能只按 Z 坐标增减判断。光程 OPL 用每段介质折射率乘几何长度累计；GRIN 支持范围内按空间变化介质积分，不能用一个固定 n 代替。",
                "Apply vector refraction/reflection using the local normal. Snell's law sets the refracted angle; if no real refracted direction exists from high to low index, use total internal reflection. Folded systems preserve actual propagation direction rather than inferring it from Z order. OPL accumulates n times segment length; supported GRIN paths integrate the spatial medium rather than using one fixed index.",
                "交点の局所法線でベクトル屈折・反射を計算します。高屈折率から低屈折率へ進み、実数の屈折方向がない場合は全反射です。折返し系では Z の順序でなく実進行方向を使います。光路長は各区間の n と距離から累積し、対応する GRIN では空間分布を積分します。") with
            { Formula = "snell-reflection", Screenshot = "solid-model" });

        yield return Calculation("paraxial", "一阶光学与焦距如何计算", "First-order optics and focal length", "一次光学と焦点距離",
            "近轴矩阵描述小角度的一阶行为，与完整实光线像差分析分工不同。", "Paraxial matrices describe small-angle behavior, separately from full aberration tracing.", "近軸行列は微小角の一次特性を表し、実光線収差解析とは役割が異なります。",
            S("高度—斜率矩阵", "Height–slope matrices", "高さ・傾き行列",
                "同轴、均匀介质中的公开固定平面传递矩阵使用高度和几何斜率，不使用折射率约化角。下式给出传播、折射及适用的空气到空气焦距关系，各段按传播顺序左乘。其他端点介质与基准平面需使用实际定义，不能直接套用。",
                "The centered homogeneous fixed-plane matrix uses height and geometric slope, not reduced angle. The equations give translation, refraction and the applicable air-to-air focal length. Successive stages left-multiply. Other endpoint media and reference planes require their actual definitions.",
                "同軸・均一媒質の固定平面行列は高さと幾何学的傾きを使い、換算角ではありません。式は伝搬、屈折、適用可能な空気間の焦点距離を示し、進行順に左から掛けます。他の端点媒質と基準面には実際の定義を使います。") with
            { Formula = "paraxial" },
            S("怎样解释摘要", "Reading the summary", "概要の読み方",
                "焦距、后焦、主面和 F 数是系统摘要，不能证明全视场成像质量。偏心、倾斜、强非近轴、反射或复杂介质的支持范围由具体入口决定；遇到明确不支持提示，不应以薄透镜估计冒充正式结果。用点列、波前和 MTF 继续评价像质。",
                "Focal length, back focus, principal planes and F/# summarize the system; they do not prove image quality across the field. Decenter, tilt, non-paraxial rays, reflection and complex media have entry-specific limits. Do not substitute a thin-lens estimate for an explicitly unsupported calculation. Continue with spot, wavefront and MTF analyses.",
                "焦点距離、バックフォーカス、主点、F ナンバーだけでは全視野の像質を保証できません。偏心、傾斜、非近軸、反射、複雑な媒質の対応範囲は入口ごとに異なります。未対応計算を薄レンズ近似で代用せず、スポット、波面、MTF を確認します。") with
            { Screenshot = "layout-2d" },
            S("Y-Ybar 与基面报告", "Y-Ybar and cardinal reports", "Y-Ybar と基点レポート",
                "Y-Ybar 横轴为近轴主光线高度，纵轴为近轴边缘光线高度，各节点对应表面。高度视场的兼容模式按下式把主光线像面高度归一到选定像高，并整体缩放所有面的主光线高度。基面报告使用同一一阶服务，参考平面和近轴焦点不等于实光线最佳焦点。",
                "Y-Ybar plots paraxial chief height horizontally and marginal height vertically at each surface. For image-height fields, compatible mode scales every chief height by the shown image-height factor. Cardinal reports use the first-order service; paraxial reference planes/focus are not real-ray best focus.",
                "Y-Ybar は主光線高を横軸、近軸周辺光線高を縦軸にして面ごとに描きます。像高視野では式の倍率で全主光線高を正規化します。基点レポートの近軸基準面・焦点は実光線の最良焦点と異なります。") with
            { Formula = "yybar" });

        yield return Calculation("spot-aberrations", "点列、RMS 与像差", "Spot, RMS and aberrations", "スポット・RMS・収差",
            "把真实像面交点转换为可比较的几何像质指标。", "Turn traced image points into interpretable geometric image-quality metrics.", "実際の像面交点から幾何学的な像質指標を求めます。",
            S("质心与 RMS 半径", "Centroid and RMS radius", "重心と RMS 半径",
                "点列来自所选视场、波长和光瞳样本在评价面的有效交点。以质心为参考时，先求加权质心，再求到质心的加权均方距离；以主光线等其他参考时不能套用同一个质心结果。失败样本、渐晕和样本权重按该分析的约定处理，比较前应核对通光情况、采样和单位。",
                "A spot uses valid evaluation-surface intersections for the chosen field, wavelengths and pupil samples. A centroid reference computes the weighted centroid and weighted mean squared distance. Chief-ray and other references are different definitions. Failed samples, vignetting and weights follow the analysis contract; compare transmission, sampling and units before comparing RMS.",
                "スポット図は選択した視野・波長・瞳サンプルの有効な評価面交点です。重心基準では重み付き重心を求め、重心からの二乗距離を平均します。主光線など別基準とは定義が異なります。失敗サンプル、ケラレ、重みは各解析の規約に従います。") with
            { Formula = "spot-rms", Screenshot = "spot" },
            S("像差与能量不是同一件事", "Aberration is not energy", "収差とエネルギーの違い",
                "光线扇形图表示光瞳位置对应的横向误差，场曲/畸变随视场变化；Seidel 是相应近轴三阶展开，不能替代强非球面或大视场的完整实光线结果。几何圈入/包围能量是按射线与权重累计，衍射能量则从 PSF 的像面强度积分。图例隐藏曲线只影响显示，不重新定义数据。",
                "Ray fans show transverse error against pupil position; field curvature and distortion vary across the field. Seidel terms are a third-order paraxial expansion, not a replacement for full tracing at strong aspheres or large fields. Geometric energy counts weighted rays; diffraction energy integrates image-plane PSF intensity. Hiding a legend series changes visibility, not the definition.",
                "光線ファンは瞳位置に対する横収差を示し、像面湾曲・歪曲は視野に依存します。Seidel は近軸三次展開であり、大視野や強い非球面の実光線解析の代用ではありません。幾何学エネルギーは光線重みを累積し、回折エネルギーは PSF 強度を積分します。凡例は表示のみを切り替えます。"));

        yield return Calculation("wavefront-psf", "波前、OPD 与 PSF", "Wavefront, OPD and PSF", "波面・OPD・PSF",
            "从光程差构造复振幅，再计算像面强度。", "Build complex amplitudes from optical path differences and form image intensity.", "光路差から複素振幅を作り、像面強度を計算します。",
            S("参考波面决定 OPD", "The reference determines OPD", "OPD と基準波面",
                "本项目 OPD 定义为参考光程减实际光程。下式给出当前 FFT 的负相位约定，与其傅里叶指数约定配套；不应单独改变符号。以 waves、mm 或 μm 表示时须使用一致波长单位。主光线参考去活塞，质心参考可去加权最佳拟合平面的倾斜；比较 PV/RMS 还需统一有效瞳和采样。",
                "The project's OPD is reference minus actual path. The equation shows the FFT's negative pupil-phase convention paired with its transform convention; do not change one sign alone. Keep wavelength units consistent for waves, mm or μm. Chief reference removes piston; centroid reference can remove weighted best-fit tilt. Match pupil validity and sampling when comparing PV/RMS.",
                "本製品の OPD は基準光路から実光路を引きます。式は FFT の負の瞳位相で、変換の符号と対になり、片方だけ変更できません。waves、mm、μm と波長単位を統一します。主光線基準はピストン、重心基準は重み付き最適傾斜も除去できます。有効瞳とサンプルも統一します。") with
            { Formula = "pupil-phase", Screenshot = "wavefront" },
            S("FFT 与 Huygens", "FFT and Huygens", "FFT と Huygens",
                "FFT PSF 在规定的光瞳网格与物理像面采样上对复光瞳进行傅里叶传播；Huygens 在支持条件下按真实采样点的传播距离、相位和权重求和。下式表达核心关系，实际积分还包含对应映射、权重和归一化。不同波长通常在同一物理像面网格上非相干叠加强度，不能直接相加电场。偏振、相干多束及多结构叠加必须看该入口的明确支持范围。",
                "FFT PSF Fourier-propagates a complex pupil using the defined pupil and physical image sampling. Supported Huygens calculations sum sampled contributions with actual propagation distance, phase and weights. The equations show the core relation; mapping, weights and normalization remain part of the implementation. Different wavelengths normally add intensities incoherently on one physical image grid. Polarization, coherent beams and configurations require the entry's stated support.",
                "FFT PSF は複素瞳を所定の瞳・像面サンプリングでフーリエ伝搬します。Huygens は対応範囲で実距離、位相、重み付き寄与を加算します。式は基本関係で、座標写像、重み、正規化を含めて計算します。異波長は通常同じ物理像面上で強度を非干渉加算し、多束・偏光・多構成の対応範囲を確認します。") with
            { Formula = "psf" });

        yield return Calculation("mtf-energy", "MTF、分辨率与圈入能量", "MTF, resolution and encircled energy", "MTF・解像度・包囲エネルギー",
            "将 PSF 或几何像点分布转换为空间频率与能量指标。", "Convert PSFs or geometric image points to spatial-frequency and energy metrics.", "PSF や幾何学像点を空間周波数・エネルギー指標へ変換します。",
            S("OTF 与 MTF", "OTF and MTF", "OTF と MTF",
                "衍射 OTF 来自 PSF 的傅里叶变换（或等价的光瞳自相关），MTF 是零频归一化 OTF 的模。频率通常为 cycles/mm，须与像面长度单位一致。切向/弧矢曲线是指定方向的截面；几何 MTF 使用像点分布，不包含完整的衍射限制。采样 MTF 还体现其采样模型，不能将所有 MTF 入口当成同一种算法。",
                "Diffraction OTF comes from the Fourier transform of the PSF or equivalent pupil autocorrelation. MTF is its zero-frequency-normalized magnitude. Frequency is commonly cycles/mm and must match image-plane length units. Tangential/sagittal curves are directional sections. Geometric MTF uses image points without the complete diffraction limit; sampled MTF includes its sampling model. These entries are not interchangeable algorithms.",
                "回折 OTF は PSF のフーリエ変換、または等価な瞳自己相関から求め、MTF はゼロ周波数で正規化した OTF の絶対値です。周波数は通常 cycles/mm で像面単位と整合させます。タンジェンシャル・サジタルは方向別断面です。幾何 MTF とサンプリング MTF は定義が異なります。") with
            { Formula = "mtf", Screenshot = "mtf" },
            S("能量积分与采样检查", "Energy integration and sampling", "エネルギー積分とサンプリング",
                "衍射圈入能量按指定中心和半径积分 PSF，再除以该定义的总能量。中心、像面窗口、像元间距和积分范围都会影响结果；先扩大窗口并加密采样检查收敛，再解释高能量百分位半径。Airy 圆只对应理想圆瞳条件，不能代替遮拦、像差、矩形瞳或多色计算。",
                "Diffraction encircled energy integrates the PSF about the selected center and normalizes by the defined total. Center, image window, pitch and integration range matter. Increase window and sampling to check convergence before interpreting high-percentile radii. An Airy disk assumes an ideal circular pupil and does not replace obscured, aberrated, rectangular or polychromatic calculations.",
                "包囲エネルギーは指定中心・半径内の PSF を積分し、定義された総エネルギーで正規化します。中心、窓、ピッチ、積分範囲を確認し、高百分位半径は窓拡大と細分化で収束を確認します。Airy 円は理想円形瞳の条件であり、遮蔽、収差、矩形瞳、多色計算の代用ではありません。") with
            { Formula = "encircled-energy" },
            S("几何与采样 MTF 的区别", "Geometric versus sampled MTF", "幾何とサンプル MTF の違い",
                "下式分别为几何像点 OTF 与当前采样 MTF 的光瞳相位差积分。坐标与频率单位必须匹配。采样 MTF 先拟合 Fringe 波前，再按频率换算瞳剪切，在重叠域积分并以零频归一化，不是随意乘一个像素 sinc；W 为 waves，Δ 为归一化瞳剪切。",
                "The expressions show geometric image-point OTF and current Sampled MTF's pupil phase-difference sum. Match position/frequency units. Sampled MTF fits a Fringe wavefront, maps frequency to pupil shear, integrates over the overlap and normalizes at zero, not an arbitrary pixel-sinc multiplier. W is in waves and Δ is normalized pupil shear.",
                "式は幾何像点 OTF とサンプル MTF の瞳位相差積分です。位置と周波数単位を統一します。Fringe 波面をフィットして周波数を瞳シアへ換算し、重複域を積分してゼロ周波数で正規化します。任意の画素 sinc を掛ける方式ではありません。") with
            { Formula = "ray-otf" },
            S("对比度损失图", "Contrast loss map", "コントラスト損失マップ",
                "对指定视场、波长和频率，用 Moore–Elliott 瞳剪切光线对计算共同参考波前差，按下式绘制瞳面局部损失。超出光瞳或无有效波前的点保持不可用；可选归一化再除以本图最大损失。它不是整个视场上的成像 MTF，也不能把显示裁切后的峰值当成未归一化损失。",
                "For a selected field, wavelength and frequency, Moore–Elliott sheared pupil-ray pairs give common-reference wavefront differences and local pupil loss. Invalid pairs stay unavailable. Optional display normalization divides by the map's maximum loss. This is not a field-wide imaging MTF.",
                "指定視野・波長・周波数の Moore–Elliott 瞳シア光線対から共通基準波面差と局所損失を求めます。無効な対は欠測のままで、任意正規化は図の最大値で割ります。全視野の結像 MTF ではありません。") with
            { Formula = "contrast-loss" });

        yield return Calculation("polarization-coatings", "偏振、Fresnel 与镀膜", "Polarization, Fresnel and coatings", "偏光・Fresnel・薄膜",
            "区分电场振幅、强度和功率，避免把透过率当作固定常数。", "Distinguish field amplitudes, intensity and power at optical interfaces.", "界面の電場振幅、強度、パワーを区別します。",
            S("界面与相干膜层", "Interfaces and coherent layers", "界面と干渉膜層",
                "裸界面的 S/P Fresnel 系数随入射角、波长和两侧材料变化。膜层用材料复折射率及物理厚度计算多界面干涉，输出 R/T/A；解释透过率时不能忽略吸收。正式偏振追迹保持 Jones 电场与参考基矢，非偏振模型通常以两种正交输入的功率平均，不能随意平均电场。",
                "Bare-interface S/P Fresnel coefficients depend on angle, wavelength and both media. Films use complex indices and physical thicknesses to calculate interference and R/T/A; absorption must be included when interpreting transmittance. Polarized tracing retains Jones fields and reference bases. Unpolarized models normally average powers from orthogonal inputs rather than averaging electric fields.",
                "裸界面の S/P Fresnel 係数は角度、波長、両媒質に依存します。薄膜は複素屈折率と物理厚さから干渉と R/T/A を求め、吸収を含めて透過率を評価します。偏光追跡は Jones 電場と基底を保持し、非偏光は通常直交入力のパワーを平均します。") with
            { Formula = "film-energy", Screenshot = "coating" },
            S("Jones 瞳的参考基底", "Jones-pupil reference bases", "Jones 瞳の基準基底",
                "Jones 瞳把两种独立单位输入沿同一真实路径传播，在输入与输出的正交横向基底间构造复矩阵。幅值与相位均有意义，不能把矩阵当作四个功率透过率。该入口对裸界面 Fresnel、体吸收及复杂膜层有独立开关和限制；当前遇到物理多层膜会明确拒绝，不能替换成裸界面后宣称已支持。",
                "Jones Pupil propagates two independent unit inputs along each real path to form a complex matrix between orthonormal transverse bases. Entries are amplitudes/phases, not four power transmittances. This entry has separate Fresnel/bulk-absorption settings and rejects physical multilayer coatings instead of substituting bare interfaces.",
                "Jones 瞳は独立な単位入力を同一実経路で伝搬し、直交横基底間の複素行列を作ります。要素は振幅・位相で、四つのパワー透過率ではありません。Fresnel・体吸収は個別設定で、物理多層膜は裸界面で代用せず拒否します。") with
            { Formula = "jones-matrix" },
            S("范围与边界", "Scope and boundaries", "範囲と境界",
                "膜层材料有效波段、吸收、外部介质、GRIN 与相干 XYZ 传播有各自边界。透明均匀介质下的支持不等于任意吸收介质矢量场均受支持。界面显示不支持时应调整模型或分析入口，不用一个经验透过率掩盖缺失计算。",
                "Wavelength ranges, absorption, exterior media, GRIN and coherent XYZ propagation have separate limits. Transparent homogeneous support does not imply arbitrary absorbing-medium vector fields. Respect explicit unsupported states instead of substituting an empirical transmission factor.",
                "有効波長、吸収、外部媒質、GRIN、XYZ 干渉伝搬は個別の制限があります。透明均一媒質の対応は任意の吸収媒質のベクトル場を意味しません。未対応の計算を経験的透過率で隠さないでください。"));

        yield return Calculation("optimization", "评价函数与优化算法如何工作", "Merit functions and optimizers", "メリット関数と最適化",
            "从变量与残差出发理解优化，而不是把“成功”当成最终设计认证。", "Understand variables, residuals and stopping conditions without treating success as certification.", "変数、残差、停止条件を理解し、成功を設計認証と混同しないようにします。",
            S("变量、目标和权重", "Variables, targets and weights", "変数・目標・重み",
                "变量有上下界与步长/缩放。一般曲率变量内部使用 1/mm，平面对应曲率零。操作数误差为实际值减目标值；正权重进入目标残差，负权重进入约束残差，零权重只监视。公共问题按全部权重绝对值之和归一化；目标 merit 与约束误差分别报告，不能把显示贡献的再次归一化误认为新的光学指标。",
                "Variables have bounds and step/scaling settings. General curvature variables use 1/mm internally, with zero for a plane. Positive weights create objective residuals, negative weights create constraint residuals and zero weights monitor only. The common problem normalizes by the sum of absolute weights; objective merit and constraint error are separate. Display contribution normalization is not another optical metric.",
                "変数には境界とステップ・スケールがあります。曲率変数は通常 1/mm、平面はゼロです。正重みは目的残差、負重みは制約残差、ゼロは監視だけです。共通問題は重み絶対値の和で正規化し、目的 merit と制約誤差を分けます。表示寄与の再正規化を別の光学指標と解釈しないでください。") with
            { Formula = "merit", Screenshot = "optimization" },
            S("选择算法与检查停止原因", "Algorithms and stopping reasons", "アルゴリズムと停止理由",
                "DLS 用阻尼残差雅可比求步进，并处理其支持的约束；Nelder–Mead 用单纯形反射、扩张、内/外收缩与缩小；坐标模式搜索和固定种子的随机扰动各有不同试探方式。达到迭代上限或 merit 有改善不证明已得全局最优。应同时核对约束误差、通光、制造范围及独立分析。计算副本只有在工程未被并发修改时才提交。",
                "DLS uses a damped residual Jacobian and its supported constraint handling. Nelder–Mead reflects, expands, contracts inside/outside and shrinks a simplex. Coordinate search and seeded random perturbation use different trials. Reaching a limit or improving merit does not prove a global optimum. Inspect constraints, transmission, manufacturability and independent analyses. Results commit only if the source project has not changed concurrently.",
                "DLS は減衰残差ヤコビアンと対応する制約処理を使います。Nelder–Mead は反射・拡張・内外収縮・縮小を行います。座標探索や固定シードのランダム摂動は別方式です。改善や上限到達は大域最適を保証しません。制約、通光、製造性、独立解析を確認し、元工程が並行変更された結果は確定しません。"));

        yield return Calculation("tolerance", "灵敏度、Monte Carlo 与良率", "Sensitivity, Monte Carlo and yield", "感度・Monte Carlo・歩留まり",
            "说明制造扰动怎样传播到成像指标，以及补偿和良率的含义。", "Follow manufacturing perturbations into image-quality metrics, compensation and yield.", "製造誤差から像質変化、補償、歩留まりまでを説明します。",
            S("灵敏度与补偿", "Sensitivity and compensation", "感度と補償",
                "灵敏度在名义处方上分别施加上下端点扰动，按选定准则重新追迹，必要时优化补偿量，再恢复名义状态。RSS 等组合属于局部统计估计，不能替代包含所有公差同时变化的 Monte Carlo。补偿器是允许调整的参数，不作为随机制造误差采样。",
                "Sensitivity applies each lower/upper endpoint to the nominal design, retraces the selected criterion, optionally optimizes compensators and restores nominal state. RSS is a local statistical estimate, not a replacement for simultaneous-error Monte Carlo. Compensators are allowed adjustments, not sampled manufacturing errors.",
                "感度解析は名義処方に上下端点の摂動を加え、基準を再追跡し、必要に応じて補償を最適化して元に戻します。RSS は局所推定で、全公差同時変動の Monte Carlo の代用ではありません。補償量は調整可能パラメーターで、ランダム製造誤差としてサンプリングしません。") with
            { Screenshot = "tolerance-settings" },
            S("确定性试验与良率", "Deterministic trials and yield", "再現可能な試行と歩留まり",
                "每次试验从名义快照开始，按定义的均匀、截断正态等分布同时施加公差，再计算未补偿与补偿后的准则。随机种子按试验编号派生，串行与并行保持有序一致。良率按配置的全部验收条件统计，失败试验不能被静默删掉提高良率；它是当前模型和分布下的估计，不是实测制造良率。",
                "Each trial starts from a nominal snapshot, simultaneously samples the defined distributions and evaluates uncompensated/compensated criteria. Seeds derive from trial index so serial and parallel results keep the same order. Yield checks all configured acceptance conditions; failed trials cannot be silently removed to improve yield. It estimates the chosen model/distributions, not measured production yield.",
                "各試行は名義スナップショットから始め、均一・打切り正規などの定義分布を同時に適用し、補償前後を評価します。シードは試行番号から派生し、直列・並列で同じ順序になります。全判定条件で歩留まりを計算し、失敗試行を削除しません。実製造歩留まりの測定値ではありません。") with
            { Formula = "yield", Screenshot = "tolerancing" });

        yield return Calculation("non-sequential", "非序列路径、功率与探测器", "Non-sequential paths, power and detectors", "ノンシーケンシャル経路・パワー・検出器",
            "不按表面序号传播，而是在空间里寻找下一次有效交互。", "Find the next spatial interaction rather than the next surface number.", "面番号順ではなく空間内の次の有効交差を探します。",
            S("对象求交与分支", "Objects and branches", "オブジェクトと分岐",
                "光源按定义的位置、方向、波长和功率生成射线；BVH 筛选候选对象后取最近正向命中。实体内部出发按几何识别介质，分裂模式按模型产生透射/反射分支；段数、活动分支和最低相对强度限制会截断路径。截断功率需要单独统计，不能算作探测器或吸收。",
                "Sources define position, direction, wavelength and power. A BVH narrows object candidates and selects the nearest forward hit. Geometry determines the initial medium inside solids. Splitting creates transmitted/reflected branches; segment, active-branch and relative-intensity limits truncate paths. Truncated power is reported separately, not counted as detected or absorbed.",
                "光源は位置、方向、波長、パワーを定義します。BVH で候補を絞り、最も近い前方交差を採用します。内部開始の媒質は形状から判定します。分岐と上限により打ち切られたパワーは、検出や吸収と分けて集計します。") with
            { Formula = "power-accounting" },
            S("像素量与数据库", "Pixel quantities and databases", "画素量とデータベース",
                "像素功率为进入该像素的有效射线功率和，位置空间辐照度再除以实际像素面积并换算单位；角度空间的辐射强度使用对应角度分箱定义。未知波长保持未知，双侧边缘均落入实际像素。探测器结果由光线数据库重建前先检查场景与材料身份；修改几何或材料后应重新追迹。清空并追迹从零计数，仅追迹才按兼容会话累加。",
                "Pixel power sums accepted ray powers. Position-space irradiance divides by physical pixel area with the stated unit conversion; angle-space intensity follows its angular-bin definition. Unknown wavelengths remain unknown and physical edges map to real pixels. Database reconstruction checks scene/material identity; retrace after geometry/material changes. Clear and Trace resets totals; Trace Only accumulates compatible sessions.",
                "画素パワーは受光光線パワーの和です。位置空間の放射照度は実画素面積で割り、角度空間の放射強度は角度ビンの定義に従います。未知波長と境界を正しく扱い、再構成前に形状・材料を検証します。変更後は再追跡し、クリア追跡はゼロから、追跡のみは互換セッションに累積します。") with
            { Formula = "detector-pixel", Screenshot = "detector" });

        yield return Calculation("laboratories", "内置实验室怎样复用计算", "How laboratories share calculations", "ラボの共通計算",
            "相机、初始结构、镀膜和装调有不同任务，但不应成为互不一致的光学引擎。", "The laboratories have different jobs while sharing the physical optical core.", "ラボは役割が異なりますが、光学物理コアを共有します。",
            S("相机：从像面光到电子数", "Camera: from irradiance to electrons", "カメラ：光から電子数へ",
                "当前镜头模式从镜头快照计算空间变化 PSF、畸变和照度；理想光学是独立对照。式中 L 为像面线性值，A 用 m²，t 用 s，波长用 m，Eref 为每波段满白标定辐照度（W/m²）。由此和绝对 QE 求期望光电子，再加入散粒/读出噪声、暗电流与不均匀性，经过满阱、增益、黑电平、ADC 和 ISP。普通图片不是实测光谱；连续模式按导入的光源、反射率和 QE 积分。",
                "Current Lens uses a snapshot for spatial PSF, distortion and illumination; Ideal Optics is a separate control. L is linear image value, A is in m², t in s, wavelength in m, and Eref is full-white per-band irradiance in W/m². Absolute QE gives expected electrons before noise, dark current, nonuniformity, saturation, gain, black level, ADC and ISP. RGB is not a measured spectrum; continuous mode integrates supplied spectral curves.",
                "現在レンズは PSF、歪曲、照度を計算し、理想光学は別の対照です。L は線形像値、A は m²、t は s、波長は m、Eref は波長帯ごとの白基準放射照度（W/m²）です。絶対 QE から電子数を求め、雑音、暗電流、不均一、飽和、ゲイン、黒レベル、ADC、ISP を適用します。RGB は実測スペクトルではなく、連続モードは入力曲線を積分します。") with
            { Formula = "camera-electrons", Screenshot = "camera" },
            S("初始结构、镀膜与装调", "Initial Structure, Coating and Assembly", "初期構成・薄膜・組立",
                "标准初始结构搜索用托管 AutoLens 组织候选、损失和优化，正式追迹、材料和验收复用共享核心；CUDA 只改变符合条件的计算后端，回退仍应注明。镀膜实验室以膜系和光谱目标组织共享薄膜求解，装调按其测量与流程模型评价。候选、实验或装调结果都有自己的状态，回到主工程后仍需核对处方和分析。",
                "Standard Initial Structure uses managed AutoLens to organize candidates, loss and optimization while sharing tracing, materials and formal acceptance. CUDA changes eligible backends and fallbacks remain disclosed. Coating organizes layer stacks and targets around the common film solver; Assembly uses its measurement/workflow model. Each result has its own state and must still be checked in the main project.",
                "標準の初期構成は managed AutoLens で候補・損失・最適化を構成し、正式追跡・材料・判定は共通コアを使います。CUDA は対応バックエンドを変えるだけで、フォールバックを明示します。薄膜と組立にも独自状態があり、主工程で処方と解析を再確認します。"));

        yield return Calculation("verification", "怎样判断结果是否可靠", "Checking numerical reliability", "数値結果の信頼性を確認する",
            "读懂设置、收敛、数据修订与验证范围，比只看一个漂亮图形更重要。", "Inspect settings, convergence, revisions and validation scope rather than appearance alone.", "見た目だけでなく設定、収束、リビジョン、検証範囲を確認します。",
            S("可重复的比较", "Repeatable comparisons", "再現可能な比較",
                "比较两次结果时固定同一处方、结构、材料、视场、波长、参考、偏振、采样与归一化。适当加密光瞳或像面采样、扩大 PSF 窗口，观察指标是否趋稳。失效、截断、几何回退和不支持提示都应纳入解释；不能隐藏失败样本后只报告好看的曲线。",
                "Hold prescription, configuration, materials, field, wavelengths, reference, polarization, sampling and normalization fixed. Increase pupil/image sampling or PSF window and check stability. Include failures, truncations, geometric fallbacks and unsupported states in interpretation rather than hiding failed samples.",
                "処方、構成、材料、視野、波長、基準、偏光、サンプリング、正規化を固定して比較します。瞳・像面サンプルや PSF 窓を増やして安定性を確認し、失敗・打切り・幾何フォールバック・未対応状態を隠さず解釈します。"),
            S("范围说明", "Scope of verification", "検証範囲",
                "内置模型、独立公式检查、历史回归、外部软件捕获和界面截图分别验证不同事情。项目以保存的 Zemax OpticStudio 2026 R1 指定用例作为主要外部数值参考；它只覆盖捕获的文件与设置，不表示全部镜头和分析都与其他软件等价。截图用于说明操作位置，不是数值精度证据。",
                "Model checks, independent equations, regressions, external captures and UI screenshots verify different things. The project uses captured Zemax OpticStudio 2026 R1 cases as its primary external numerical reference, limited to those files/settings. This does not establish universal equivalence. Screenshots explain operation, not numerical accuracy.",
                "モデル検査、独立式、回帰、外部キャプチャ、画面画像は別の目的を検証します。主要な外部数値基準は保存済みの Zemax OpticStudio 2026 R1 の指定ケースで、対象ファイルと設定だけをカバーします。画像は操作説明であり数値精度の証拠ではありません。"));
    }

    private static GuideChapter Calculation(string id, string zh, string en, string ja,
        string summaryZh, string summaryEn, string summaryJa, params GuideSection[] sections) =>
        new("calculation-" + id, T("计算原理", "How calculations work", "計算原理"), T(zh, en, ja),
            T(summaryZh, summaryEn, summaryJa), id switch
            {
                "units-materials" or "paraxial" or "ray-tracing" => UserGuideFigureKind.SequentialModel,
                "optimization" => UserGuideFigureKind.OptimizationLoop,
                "tolerance" => UserGuideFigureKind.ToleranceFlow,
                "non-sequential" => UserGuideFigureKind.NonSequentialModel,
                "laboratories" or "polarization-coatings" => UserGuideFigureKind.LibrariesAndModules,
                "verification" => UserGuideFigureKind.HelpSystem,
                _ => UserGuideFigureKind.AnalysisFlow
            }, sections);
}
