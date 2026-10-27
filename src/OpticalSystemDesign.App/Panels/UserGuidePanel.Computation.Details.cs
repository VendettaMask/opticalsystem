namespace OpticalSystemDesign.App.Panels;

public sealed partial class UserGuidePanel
{
    private static IEnumerable<GuideChapter> DetailedComputationChapters()
    {
        yield return Calculation("surface-grin", "面型、坐标变换与 GRIN", "Surfaces, coordinate transforms and GRIN", "面形状・座標変換・GRIN",
            "求交使用真实面方程，折射率分布需要沿路径积分。", "Intersections use actual surface equations; graded index requires path integration.", "実面方程式で交差を求め、分布屈折率では経路を積分します。",
            S("圆锥与偶次非球面", "Conics and even aspheres", "円錐面と偶数次非球面",
                "下式给出旋转对称圆锥加偶次非球面的矢高，半径与长度使用同一几何单位；曲率项仅在非零有限半径时取倒数，平面曲率为零。根号定义域、半口径及表面姿态均需检查。XY、双圆锥、环面、Zernike 或网格面使用各自几何实现，不能套用该旋转对称公式。局部交点和法线经完整刚体变换回到全局。",
                "The sag expression describes a rotational conic with even asphere terms. Lengths share geometry units; curvature is reciprocal radius only for a finite nonzero radius, and zero for a plane. Check the radical domain, aperture and pose. XY, biconic, toroidal, Zernike and grid surfaces use their own models. Transform local points/normals with the complete rigid pose.",
                "式は回転対称円錐面と偶数次非球面のサグです。長さ単位を統一し、有限非ゼロ半径のみ逆数を曲率とします。平面曲率はゼロです。定義域・開口・姿勢を確認します。XY、双円錐、トロイダル、Zernike、グリッド面には固有モデルを使い、局所点と法線を剛体変換します。") with
            { Formula = "conic-sag" },
            S("空间变化介质", "Spatially varying media", "空間変化する媒質",
                "GRIN 中方向由折射率梯度连续改变。下式以弧长为参数，表达几何光学路径方程；实际实现还包含积分步进、边界事件和失败检查。积分步长误差应与光瞳采样误差分别做收敛检查。某分析不支持相应介质或坐标组合时会明确拒绝，不以常数折射率代替。",
                "GRIN bends rays continuously with the index gradient. The arc-length equation describes geometric propagation; implementation also handles integration steps, boundary events and failures. Check step convergence separately from pupil quadrature. Unsupported media/coordinate combinations must be rejected rather than replaced with a constant index.",
                "GRIN は屈折率勾配で方向を連続的に変えます。弧長を用いた経路式に加えて、積分刻み、境界イベント、失敗を処理します。積分刻みと瞳サンプルの収束を別々に確認します。未対応の組合せを一定屈折率で代用しません。") with
            { Formula = "grin" });

        yield return Calculation("pupil-sampling", "光瞳采样、瞄准与渐晕", "Pupil sampling, aiming and vignetting", "瞳サンプリング・照準・ケラレ",
            "样本位置、权重和有效性共同决定积分，光线数量本身不能代表精度。", "Positions, weights and validity define quadrature; ray count alone is not accuracy.", "位置、重み、有効性が積分を定義し、光線数だけでは精度を示せません。",
            S("采样权重", "Quadrature weights", "積分重み",
                "矩形、六角环和其他光瞳采样具有不同节点和面积权重。下式说明加权平均，实际分析还选择发射权重、透过权重或归一化分母。渐晕损失不能通过只对幸存光线重新归一化而隐藏。边界、遮挡、波长权重以及采样节点是否重复都影响积分。",
                "Rectangular, hexapolar and other samplings have different nodes and area weights. The equation illustrates a weighted mean; each analysis chooses launch/transmission weights and normalization. Renormalizing surviving rays can hide vignetting. Boundaries, obscuration, spectral weights and duplicate nodes affect integration.",
                "矩形や六角リング等は節点と面積重みが異なります。式は重み付き平均であり、発射・透過重みと分母は解析の定義に従います。生存光線だけの再正規化でケラレを隠さないでください。境界、遮蔽、波長重み、重複節点を確認します。") with
            { Formula = "pupil-quadrature" },
            S("瞄准与失败样本", "Aiming and failed samples", "照準と失敗サンプル",
                "光线瞄准求解使光线到达指定的实际光阑坐标，不能把入口瞳坐标直接当作所有系统的光阑交点。主光线、边界及积分光线有不同用途；失败、无交点、全反射和裁剪应保留原因。CPU/GPU 比较需保持相同节点、双精度、瞄准策略和失效判定。",
                "Aiming solves for the actual requested stop coordinate; entrance-pupil coordinates are not universal stop intersections. Chief, boundary and integration rays serve different roles. Preserve miss, TIR, clipping and aiming-failure reasons. CPU/GPU comparisons require identical nodes, precision, aiming policy and validity checks.",
                "照準は実絞りの指定座標へ到達する光線を解きます。入射瞳座標を常に絞り交点とみなすことはできません。主光線・境界・積分光線の役割を区別し、交差失敗、全反射、遮光、照準失敗の理由を保持します。CPU/GPU 比較は節点と判定条件を統一します。"));

        yield return Calculation("seidel-field", "Seidel、场曲、畸变与色差", "Seidel, field curvature, distortion and color", "Seidel・像面湾曲・歪曲・色収差",
            "区分三阶展开系数、实光线焦点与像高误差。", "Distinguish third-order coefficients, real-ray focus and image-height errors.", "三次展開係数、実光線焦点、像高誤差を区別します。",
            S("Seidel 的系数约定", "Seidel coefficient conventions", "Seidel 係数の定義",
                "下式展示典型三阶波像差基底，说明球差、彗差、像散、场曲与畸变项的依赖关系，不是把表中的七列直接相加。当前报告分别提供毫米制 Seidel、以波长换算的波像差，以及横向/纵向像差；转换依赖像方折射率、近轴边缘斜率和波长。每面贡献与累计行必须用同一基底和单位。",
                "The expression illustrates the third-order wave-aberration basis, not a sum of the report's seven columns. Reports distinguish Seidel values in mm, wavelength-scaled wave coefficients, and transverse/longitudinal aberrations. Conversion depends on image index, paraxial marginal slope and wavelength. Sum surface contributions only with consistent bases and units.",
                "式は三次波面収差の基底を示し、表の七列を直接加算する式ではありません。レポートは mm の Seidel、波長換算の波面係数、横・縦収差を区別します。変換は像側屈折率、近軸周辺傾き、波長に依存します。面寄与と合計の基底・単位を統一します。") with
            { Formula = "seidel-wavefront" },
            S("实光线与参考像高", "Real rays and reference height", "実光線と基準像高",
                "场曲根据子午/弧矢方向焦点相对参考像面的偏移评价；畸变比较实际主光线像高与所选参考像高，参考为零时使用专门的轴上处理，不直接除零。色焦移扫描波长，垂轴色差比较波长间像高，轴向像差随光瞳位置比较轴向交会。网格畸变把同一映射扩展到二维；这些量不能互称 RMS 或 MTF。",
                "Field curvature compares tangential/sagittal focus with the reference image plane. Distortion compares actual chief-ray height with the selected reference, using a defined on-axis treatment instead of division by zero. Color focus scans wavelength, lateral color compares image heights, and axial aberration scans pupil intersections. Grid distortion extends the mapping to 2D.",
                "像面湾曲は子午・サジタル焦点の基準面からの偏差です。歪曲は主光線像高と選択基準を比較し、基準ゼロには軸上処理を使います。色焦移は波長走査、倍率色収差は像高差、軸収差は瞳位置別の軸交点です。格子歪曲は同じ写像を二次元に拡張します。") with
            { Formula = "distortion" });

        yield return Calculation("zernike", "Zernike 系数怎样拟合", "How Zernike coefficients are fitted", "Zernike 係数のフィッティング",
            "拟合基底和采样域决定系数含义，编号不能跨定义混用。", "Basis and sampling domain define coefficients; numbering is not interchangeable.", "基底と領域で係数の意味が決まり、番号を異なる定義で混用できません。",
            S("设计矩阵与 QR 最小二乘", "Design matrix and QR least squares", "設計行列と QR 最小二乗",
                "从有效光瞳 OPD（waves）构建设计矩阵，各列为相应 Zernike 基底。当前拟合使用有效节点上的未加权 QR 最小二乘；不要把强度加权的波前统计公式写成系数求解公式。病态、秩不足和项数/内存预算分别检查，拟合残差应与系数一起阅读。",
                "Build the design matrix from Zernike basis values at valid pupil nodes and OPD in waves. Current coefficient fitting uses unweighted QR least squares on those nodes. Intensity-weighted wavefront statistics are a separate definition. Check conditioning, rank, term/resource limits and fit residuals.",
                "有効瞳節点の基底値と waves の OPD から行列を作り、現在の係数解法は未重み付き QR 最小二乗です。強度重み付き波面統計とは別です。条件数、階数、項数・メモリ上限、残差を確認します。") with
            { Formula = "zernike-fit" },
            S("Fringe、Standard 与 Annular", "Fringe, Standard and Annular", "Fringe・Standard・Annular",
                "Fringe 与 Standard 的编号、归一化不同；Annular 还依赖中心遮挡比及环形正交基底。遮挡比、有效域、参考球、活塞/倾斜移除和采样改变都会影响可比性。系数随视场分析在各采样视场重新拟合。界面只保留这三种明确入口，不恢复已退役的通用 Zernike 页面。",
                "Fringe and Standard use different numbering/normalization. Annular also depends on obscuration and an annular orthogonal basis. Compare only with matching obscuration, domain, reference sphere, piston/tilt removal and sampling. Coefficients-vs-field refits at each sampled field. The retired generic Zernike page is not restored.",
                "Fringe と Standard は番号と正規化が異なり、Annular は中心遮蔽比と環状直交基底にも依存します。遮蔽比、領域、基準球、ピストン・傾き除去、サンプルを統一します。視野走査では各視野で再フィットします。廃止済み汎用ページは復活しません。"));

        yield return Calculation("interferogram", "干涉图的相位与强度", "Interferogram phase and intensity", "干渉図の位相と強度",
            "两束真实波前相减后，应用倍率、主光程与瞳倾斜。", "Difference two wavefronts, then apply scale, chief path and pupil tilt.", "二波面の差に倍率、主光路、瞳傾きを適用します。",
            S("实际条纹公式", "The fringe equation", "縞の計算式",
                "波前以所选波长的 waves 表示，参考光束波前为零。式中 s 为倍率，tx/ty 为跨完整瞳直径的条纹数，倾斜在倍率之后加入。主光程项仅在勾选考虑光程差时加入，波长需换算到光程长度单位。当前 OPD 定义为参考光程减实际光程，因而两束主光程差的顺序如式所示。",
                "Wavefronts are in waves at the selected wavelength; the reference beam has zero wavefront error. Scale is s; tx/ty count fringes across a full pupil diameter and are added after scaling. Include the chief-path term only when enabled, with consistent length units. The project's OPD is reference minus actual path, giving the shown chief-path sign.",
                "波面は選択波長の waves、参照光束はゼロです。s は倍率、tx/ty は瞳全径の縞数で倍率の後に加えます。主光路差は設定時だけ加え、波長を光路長単位へ換算します。本製品の OPD は基準から実光路を引く定義で、式の符号に対応します。") with
            { Formula = "interferogram" },
            S("有效瞳与显示", "Valid pupil and display", "有効瞳と表示",
                "模型假定两束等强、归一化瞳完全重合，只在共同有效的追迹样本上求值。灰度色表可能将强度一显示为黑色，这不改变物理强度数据。子孔径、参考顶点小角度和中间分析面有各自支持范围；不能把 OPD 热图或任意错位双束当成该干涉图。",
                "The model assumes equal-strength beams with fully overlapping normalized pupils and uses common valid traced samples. A grayscale palette can map intensity one to black without reversing physical data. Subapertures, small-angle vertex reference and intermediate surfaces have explicit limits. An OPD heatmap is not this interferogram.",
                "等強・完全に重なる正規化瞳を仮定し、共通の有効サンプルだけを使います。グレースケールで強度一が黒でも物理値は反転しません。部分開口、微小角頂点基準、中間面には個別制限があります。OPD ヒートマップとは異なります。"));

        yield return Calculation("foucault", "傅科刀口与角谱传播", "Foucault knife edge and angular spectrum", "Foucault ナイフエッジと角スペクトル",
            "刀口遮挡复场后传播，显示强度与总透过功率要分别读取。", "Propagate a masked complex field and distinguish displayed intensity from total throughput.", "複素場を遮って伝搬し、表示強度と全透過パワーを区別します。",
            S("焦面刀口与理想瞳再成像", "Focal knife and ideal pupil reimaging", "焦面刀口と理想瞳再結像",
                "默认模式把共享波前生成的复瞳作 FFT，在焦面施加刀口掩模，再逆变换至理想再成像瞳。偏振模式分别传播支持的电场通道后加功率；非偏振输入的正交态不相干。填充网格的总功率、显示中央区域功率和刀口透过率分别报告，不能再次裁回原圆瞳隐藏衍射外溢。",
                "The default FFTs the shared complex pupil, applies a focal knife mask, then inverse-transforms to an ideal reimaged pupil. Supported polarization channels propagate separately before power summation; unpolarized input states are incoherent. Padded-grid power, central displayed power and knife throughput are distinct. Do not remask diffracted spill outside the original circle.",
                "既定は共通複素瞳の FFT に焦面マスクを適用し、逆変換で理想瞳を再結像します。偏光チャネルを別々に伝搬してパワーを加算します。非偏光の直交入力は非干渉です。全グリッド、中央表示、刀口透過率を区別し、円外回折を再マスクで消しません。") with
            { Formula = "foucault" },
            S("空间与矢量角谱", "Spatial and vector angular spectrum", "空間・ベクトル角スペクトル",
                "选择角谱模式后，刀口距离、观察距离和实际观察范围参与传播。下式为标量角谱基本关系；矢量模式还需横向投影与支持的介质约束，不等同于任意介质的通用矢量 POP。采样与窗口必须同时检查，焦面微米刀口不适用于未定义聚焦光学的无焦系统。",
                "Angular-spectrum modes use physical knife/observation distances and window extent. The scalar relation is shown; vector mode also applies transverse projection and medium restrictions, not arbitrary-media vector POP. Check sampling and extent together. A focal knife in micrometers requires defined focusing optics.",
                "角スペクトルは刀口・観察距離と物理範囲を使います。式はスカラー基本関係で、ベクトル版は横方向射影と媒質制限を含み、任意媒質 POP ではありません。刻みと窓を同時に確認します。有焦光学を定義しない無焦系に焦面刀口を適用しません。") with
            { Formula = "angular-spectrum" });

        yield return Calculation("illumination", "相对照度与辐射度量", "Relative illumination and radiometry", "相対照度と放射測定量",
            "相对量的归一化分母与绝对功率的面积/立体角单位必须明确。", "Identify relative normalization and the area/solid-angle units of absolute power.", "相対正規化と絶対パワーの面積・立体角単位を確認します。",
            S("相对照度不是固定 cos⁴ 曲线", "Relative illumination is not a fixed cosine-fourth curve", "相対照度は固定 cos⁴ 曲線ではない",
                "共享积分计算真实通光瞳的像方投影余弦面积 Q，按所选采样模式、渐晕及偏振/透过设置求值。当前曲线除以本次扫描的最大 Q，不保证总是以轴上值归一化。横轴来自实际角度或高度扫描。应同时记录采样模式、实际节点和是否包含透过率。",
                "The shared integral evaluates image-side projected cosine pupil area Q using actual transmission geometry and selected sampling/vignetting/polarization settings. The current curve divides by the maximum Q of the sampled scan, not necessarily its on-axis value. Record mode, actual nodes and whether scalar transmission is included.",
                "実通過瞳の像側投影余弦面積 Q を共通積分で計算します。現在の曲線は走査した Q の最大値で正規化し、必ずしも軸上値ではありません。角度・高さの走査、サンプル、ケラレ、偏光・透過設定を記録します。") with
            { Formula = "relative-illumination" },
            S("照度与辐射强度", "Irradiance and radiant intensity", "放射照度と放射強度",
                "非相干辐照度对像面功率分箱后除实际像素面积；辐射强度按方向分箱并除立体角。未归一化量分别使用 W/mm² 与 W/sr，选择归一化时不能再当作绝对值。几何统计不包含任意相干干涉；输入功率、采样权重、渐晕和透过损失需保持一致。",
                "Incoherent irradiance bins image-plane power per pixel area; radiant intensity bins direction per solid angle. Absolute units are W/mm² and W/sr respectively; normalized output is not absolute power. Geometric statistics do not add arbitrary coherent interference. Preserve source power, quadrature, vignetting and transmission conventions.",
                "非干渉放射照度は像面パワーを画素面積で、放射強度は方向別パワーを立体角で割ります。絶対単位は W/mm² と W/sr で、正規化後を絶対量として扱いません。幾何統計は任意の干渉を含みません。発射パワー・重み・ケラレ・透過を統一します。") with
            { Formula = "radiometry" });

        yield return Calculation("line-edge", "线扩散、边缘扩散与扩展源能量", "Line spread, edge spread and extended sources", "線像・エッジ応答・拡張光源",
            "截面、投影积分和累计积分是不同计算。", "A cross section, projected integral and cumulative integral are different operations.", "断面、投影積分、累積積分を区別します。",
            S("LSF 与 ESF", "LSF and ESF", "LSF と ESF",
                "衍射 LSF 沿垂直方向积分二维 PSF，ESF 再沿选定方向累计并按总量归一化。PSF 中心截线只取一行或一列，通常不等于 LSF。几何线/边缘扩散从对应方向的加权射线分布求投影与累计。需记录方向、中心、单位、窗口和归一化。",
                "Diffraction LSF integrates the 2D PSF in the perpendicular direction; ESF cumulatively integrates and normalizes it. A central PSF slice is generally not an LSF. Geometric line/edge spread instead projects and accumulates weighted ray distributions. Record direction, center, units, window and normalization.",
                "回折 LSF は二次元 PSF の直交方向積分、ESF はその累積と正規化です。PSF の中央断面は通常 LSF と異なります。幾何版は重み付き光線分布を投影・累積します。方向、中心、単位、窓、正規化を記録します。") with
            { Formula = "line-edge" },
            S("扩展光源能量", "Extended-source energy", "拡張光源エネルギー",
                "扩展光源把输入源分布与选定几何或衍射响应组合后，再计算圈入或包围量。源半径、形状、像面尺度与分析参考会改变结果；不能直接把点源 Airy 半径与扩展源的百分位半径比较。采样截断和图面裁剪也会影响高百分位量。",
                "Extended-source energy combines the source distribution with its chosen geometric/diffraction response before integration. Source size/shape, image scale and reference affect the result. A point-source Airy radius is not directly comparable to an extended-source percentile. Check truncation and image extent.",
                "拡張光源は入力分布と選択した幾何・回折応答を合成してからエネルギーを積分します。光源の寸法・形状、像面尺度、基準が結果を変えます。点光源 Airy 半径と拡張源の百分位を直接比較せず、窓と打切りを確認します。"));

        yield return Calculation("extended-imaging", "几何、相干与部分相干图像", "Geometric, coherent and partially coherent images", "幾何・コヒーレント・部分コヒーレント像",
            "先区分叠加强度还是电场，再检查空间变化与采样。", "Distinguish intensity and field addition before checking spatial variation and sampling.", "強度と電場の加算を区別し、空間変化とサンプルを確認します。",
            S("点响应到扩展图像", "From point responses to images", "点応答から画像へ",
                "几何图像分析把源样本经真实光线映射到像面并累计功率；衍射非相干图像叠加强度点响应，相干图像先叠加复场再取模平方。下式保留响应随源位置变化的形式，只有空间不变时才可简化为普通卷积。输入位图、IMA/BIM 的文件查看器只显示数据，不产生新的光学计算。",
                "Geometric imaging maps source samples through real rays and bins power. Incoherent diffraction adds intensity responses; coherent imaging adds complex amplitudes before squaring. The equations retain source-dependent response; convolution assumes shift invariance. Bitmap and IMA/BIM viewers display files without new optical calculations.",
                "幾何画像は光源サンプルを実光線で像面へ写してパワーを累積します。非干渉回折は強度応答、干渉像は複素振幅を加算後に二乗します。式は光源位置依存を保持し、通常の畳込みは空間不変の場合だけです。ファイルビューアーは新たな光学計算を行いません。") with
            { Formula = "extended-imaging" },
            S("互强度与范围", "Mutual intensity and scope", "相互強度と範囲",
                "部分相干通过源互强度与两点传播核计算强度，不能用清晰/模糊图片的随意混合代表全部部分相干系统。当前源模型及相干参数有明确构造、非负性与资源上限；复相位、偏振和空间传播的组合必须遵守支持范围。光源分析评价输入分布，图像模拟结果还依赖图像尺度、方向、边界和归一化。",
                "Partial coherence propagates source mutual intensity with a two-point kernel, not an arbitrary blend of sharp/blurred images. The implemented source/coherence model has defined construction, positivity and resource bounds. Phase, polarization and spatial propagation combinations remain scoped. Image results also depend on scale, orientation, boundaries and normalization.",
                "部分干渉は相互強度と二点核で伝搬し、鮮明・ぼけ画像の任意混合ではありません。源モデルと相干パラメーターは構成、非負性、資源上限を持ちます。位相、偏光、空間伝搬の組合せは対応範囲に従います。尺度、方向、境界、正規化も結果を変えます。") with
            { Formula = "mutual-intensity" });

        yield return Calculation("scans-configurations", "扫描、参考球与多结构比较", "Scans, reference spheres and configurations", "走査・基準球・複数構成",
            "横轴节点与参考定义同样属于计算条件。", "Scan nodes and reference definitions are calculation settings too.", "走査節点と基準定義も計算条件です。",
            S("视场、波长与离焦扫描", "Field, wavelength and focus scans", "視野・波長・デフォーカス走査",
                "RMS 点列随视场在零到最大已定义视场之间取 Field Density 加一个节点，方向由设置决定，不是直接重复视场表。其他扫描按各自节点设置运行。离焦分析在计算副本中改变评价位置；角度-像高扫描瞳孔或视场，不能混淆两种横轴。点列矩阵是同一指标的组织形式，不产生额外物理单位。",
                "RMS spot vs field uses Field Density plus one positions from zero to the maximum defined field in the chosen direction, not just Field Editor rows. Other scans use their own nodes. Focus scans change evaluation position in a copy. Angle/height scans through pupil and field have different horizontal variables. Matrices organize results without changing units.",
                "視野別 RMS スポットは指定方向でゼロから最大視野まで Field Density に一を加えた節点を使い、視野表そのものではありません。他の走査は固有の節点設定に従います。離焦は副本の評価位置を変えます。瞳走査と視野走査の横軸を区別します。") with
            { Formula = "scan-grid" },
            S("参考球与结构身份", "Reference spheres and configuration identity", "基準球と構成の識別",
                "主光线球、质心球与最佳拟合球使用不同的参考中心/半径求 OPD。最佳拟合球的几何拟合不能等同于只减去一个平面。多结构曲线叠加保留每个结构的处方和结果身份；相干双束、偏振和 Huygens 多结构组合有专门支持范围，不能把普通多结构显示叠加解释成电场合成。",
                "Chief-ray, centroid and best-fit spheres use different centers/radii for OPD. Sphere fitting is not merely plane removal. Configuration overlays retain each prescription/result identity. Coherent beams, polarization and Huygens combinations have specific support; ordinary overlays do not sum electric fields.",
                "主光線球、重心球、最適球は OPD の中心・半径が異なり、球フィットは平面除去だけではありません。構成の重ね表示は各処方の識別を保持します。干渉光束、偏光、Huygens の組合せには固有制限があり、通常の重ね表示は電場合成ではありません。"));

        yield return Calculation("derivatives", "优化步进、导数与边界", "Optimizer steps, derivatives and bounds", "最適化ステップ・導関数・境界",
            "残差、导数和实际应用后的变量必须一致。", "Residuals, derivatives and the applied variables must agree.", "残差、導関数、適用後の変数を一致させます。",
            S("阻尼最小二乘", "Damped least squares", "減衰最小二乗",
                "DLS 在线性化残差上求阻尼步进；式中的阻尼尺度矩阵由实现的变量缩放/对角策略决定，并结合约束处理及实际步长接受规则。Nelder–Mead 不用该雅可比，而采用单纯形的反射、扩张、内外收缩和缩小。变量被边界裁剪后应使用实际坐标评价，不能保留一个与处方不同的顶点。",
                "DLS solves a damped linearized residual problem; its scale matrix follows the implemented variable/diagonal strategy, with constraints and step acceptance. Nelder–Mead instead uses simplex reflection, expansion, inside/outside contraction and shrinkage. Evaluate the actual bounded coordinates, not a vertex inconsistent with the prescription.",
                "DLS は残差線形化に減衰を加え、実装の変数・対角スケール、制約、ステップ受理を適用します。Nelder–Mead は単体の反射、拡張、内外収縮、縮小です。境界適用後の実座標を評価し、処方と異なる頂点を保持しません。") with
            { Formula = "dls-step" },
            S("差分与可微追迹", "Differences and differentiable tracing", "差分と微分可能追跡",
                "中心差分需要两侧样本都有效；边界、失光和拓扑改变可能使它不适用，应使用实际支持的单侧或失败处理。可微追迹通过共享几何与折射规则传播导数；仅在固定有效分支内局部光滑，不代表遮挡、全反射切换也可任意求导。需以同定义的数值差分检查梯度，CPU/GPU 使用同一物理规则。",
                "Central differences require valid samples on both sides. Bounds, clipping and topology changes need supported one-sided/failure handling. Differentiable tracing propagates derivatives through shared geometry/refraction within valid smooth branches; clipping/TIR switches are not arbitrary smooth operations. Validate gradients against same-definition differences and preserve CPU/GPU physics.",
                "中心差分は両側が有効な場合に使い、境界・遮光・トポロジー変化には対応する片側処理や失敗判定が必要です。微分可能追跡も有効で滑らかな分岐内の局所微分です。遮光や全反射切替を常に滑らかと扱わず、同定義の差分で検証します。") with
            { Formula = "finite-difference" });

        yield return Calculation("coating-solver", "薄膜求解的相位、反射与透射", "Thin-film phase, reflection and transmission", "薄膜の位相・反射・透過",
            "复折射率与稳定递推处理吸收层，不能用经验曲线冒充。", "Complex indices and stable recursion handle absorption, not an empirical curve.", "複素屈折率と安定再帰で吸収層を扱います。",
            S("相位厚度与稳定递推", "Phase thickness and stable recursion", "位相厚と安定再帰",
                "实际共享薄膜求解器按 S/P 光学导纳与复相位厚度从基底向入射侧递推振幅，吸收通过衰减及对数透射幅值稳定处理。下式给出单层相位与递推关系；所有长度单位一致。相干层内电场先叠加，再按边界能流归一化得到功率。",
                "The shared solver recursively combines S/P admittances and complex phase from substrate to incident side. Attenuation and logarithmic transmission amplitudes stabilize absorption. The expressions show layer phase and reflection recursion with consistent lengths. Coherent amplitudes combine before boundary flux normalization.",
                "共通解法は基板側から S/P アドミタンスと複素位相を再帰結合し、減衰と対数透過振幅で吸収を安定化します。式は層位相と反射再帰で、長さ単位を統一します。振幅合成後に境界エネルギーフラックスで正規化します。") with
            { Formula = "thin-film-matrix" },
            S("材料与实验指标", "Materials and experiment criteria", "材料と実験指標",
                "复折射率的虚部约定、前向平方根、入射介质和基底都影响能流方向。实验目标可按波长、角度、偏振和公差评价，需分别读取 R、T、A 及不合格项。某些简化镀膜模型是单独的近似，不能与正式多层相干求解或实测膜系混称。",
                "Index sign convention, forward root, incident medium and substrate affect flux direction. Experiment criteria span wavelength, angle, polarization and tolerance; inspect R, T, A and failed criteria separately. Simplified coating models must not be presented as the full coherent multilayer solver or measured stacks.",
                "複素指数の符号、前進平方根、入射媒質、基板が能流を決めます。波長・角度・偏光・公差ごとに R/T/A と不合格条件を読みます。簡易モデルを正式多層干渉解法や実測膜系と混同しません。"));

        yield return Calculation("initial-search", "初始结构搜索与独立验收", "Initial-structure search and independent acceptance", "初期構造探索と独立判定",
            "训练损失、实际设备与正式验收是不同层面的结果。", "Training loss, actual compute device and formal acceptance are different results.", "学習損失、実デバイス、正式判定を区別します。",
            S("标准 AutoLens 的更新", "Standard AutoLens updates", "標準 AutoLens の更新",
                "标准入口采用托管 AutoLens，按原有阶段课程、固定样本和 Adam 更新候选，正式追迹及导数仍来自共享 Core。式中一阶/二阶动量经过偏差修正，步长和稳定项服从算法记录。实验室专用 RGB 损失、绿光质心或停止梯度定义不能替换通用点列 RMS。",
                "The standard entry uses managed AutoLens with its recorded curriculum, fixed samples and Adam updates. Formal tracing/derivatives remain shared Core. First/second moments are bias-corrected, with step and epsilon defined by the recorded algorithm. Module-specific RGB loss, green centroid and detached gradients are not generic spot RMS.",
                "標準は managed AutoLens の段階課程、固定サンプル、Adam 更新を使い、正式追跡・導関数は共通 Core です。一次・二次モーメントの偏り補正、歩幅、安定項は記録に従います。専用 RGB 損失、緑重心、停止勾配を一般スポット RMS と置き換えません。") with
            { Formula = "adam" },
            S("候选、拓扑与通过条件", "Candidates, topology and pass criteria", "候補・トポロジー・合格条件",
                "候选必须用独立的物理采样与原门槛检查通光、焦距、口径、像质及制造条件。自主拓扑工作区与标准 AutoLens 是不同实验协议，减片、补偿和预算需保留自身记录；一个已通过的指定案例不代表任意任务都能成功。回退、预算耗尽、未完成与实际 GPU 执行必须如实区分。",
                "Independently validate transmission, focal length, aperture, image quality and manufacturability with original gates. Autonomous topology and standard AutoLens are different experiment protocols; retain reduction, compensation and budget records. A passing case does not establish universal success. Distinguish fallback, budget exhaustion, incomplete runs and actual GPU execution.",
                "独立物理サンプルと元の基準で通光、焦点距離、開口、像質、製造性を判定します。自主トポロジーと標準 AutoLens は別の実験手順で、減枚・補償・予算記録を保持します。特定ケース成功を一般化せず、代替実行、予算終了、未完了、GPU 実行を区別します。"));

        yield return Calculation("assembly", "装调共轭与叉丝像怎样计算", "Assembly conjugates and reticle images", "調整共役とレチクル像",
            "共轭位置来自共享实光线的近轴极限，预览是有限孔径几何回程像。", "Conjugates use the paraxial limit of shared real rays; previews trace finite-aperture return images.", "共役位置は共通実光線の近軸極限、プレビューは有限開口の往復幾何像です。",
            S("近轴找像位置", "Paraxial conjugate position", "近軸共役位置",
                "装调对目标顶点或曲率中心构造共享实光线残差，以高度和斜率小扰动求导，并用两级外推检查步长收敛。位置相对第一实体面顶点报告。导数退化时可能为无穷远共轭；路径不支持、无反射和数值失败分别标识，不能都显示为零位置。",
                "Assembly builds shared real-ray residuals for a target vertex/curvature center, differentiates small height/slope probes and extrapolates to check convergence. Positions reference the first physical vertex. Degenerate derivatives can indicate infinity. Unsupported paths, no reflection and numerical failure are distinct, not zero positions.",
                "対象頂点・曲率中心に対する共通実光線残差を高さ・傾きで微小摂動し、外挿して収束を確認します。位置基準は第一実面頂点です。退化導数による無限遠、未対応経路、無反射、数値失敗をゼロ位置と区別します。") with
            { Formula = "assembly-conjugate" },
            S("有限孔径预览", "Finite-aperture preview", "有限開口プレビュー",
                "叉丝像使用探头/准直器参数与所选反射面，追迹返回射线并按传感器范围分箱，报告发射、返回和记录数量。聚焦与平行光模式的共轭含义不同；预览亮度经过归一化，且不包含正式镀膜或杂散光功率模型。它不是通用灵敏度矩阵装调求逆，也不是实际仪器测量。",
                "Reticle previews use probe/collimator parameters and the selected reflecting surface, bin returned rays and report launched/returned/recorded counts. Focused and infinity modes differ. Brightness is normalized without a formal coating/stray-light power model. This is not a general sensitivity-matrix alignment inverse or an instrument measurement.",
                "探頭・コリメーター条件と対象反射面から往復光線を追跡し、センサーへ分配して発射・帰還・記録数を示します。焦点・平行光モードは異なり、輝度は正規化され、正式膜・迷光パワーではありません。一般的な感度行列逆問題や実測器結果ではありません。") with
            { Screenshot = "assembly" });
    }
}
