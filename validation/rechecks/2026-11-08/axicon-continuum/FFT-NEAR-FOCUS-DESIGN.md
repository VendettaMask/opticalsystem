# 轴锥 FFT 的近焦诊断

## 官方条件与冻结原生返回

OpticStudio 2026 R1.03 将 FFT PSF 的计算平面置于主光线交点并垂直于主光线。文档要求像面处于 “fairly close to the geometric focus for all rays” 的范围，也明确指出 “There is no hard and fast limit”。横向像差超过 “a few hundred wavelengths” 属于可能失准的量级，文档以圆柱面只聚焦一个方向为例。主光线入射角、出瞳像差和快速系统的标量近似另有各自的适用性说明。 [Ansys FFT PSF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_PSF.html)

FFT MTF 另要求 “reasonably uniform distribution of rays on the exit pupil in cosine space”，并引用 FFT 和 Huygens PSF 的讨论。FFT 截线明确说 “The cross sections are taken directly from the PSF data.” 衍射包围能量的讨论也引用两种 PSF，原 #952 的 `UseHuygensPSF=false` 选择了 FFT 路径。 [Ansys FFT MTF](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_MTF.html) [Ansys FFT 截线](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/FFT_Cross_Section.html) [Ansys 衍射包围能量](https://ansyshelp.ansys.com/public/views/secured/zemax/v26103/en/opticstudio_user_guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html)

| 冻结项 | 原设置摘要 | 原生返回 |
| --- | --- | --- |
| #945 FFT PSF | 64 瞳孔、128 输出、0.25 µm | 一个空网格描述，`nx=ny=0`、`values=[]`。API 与纯文本均提示「取样太低, 数据不准确!」 |
| #947 FFT MTF | 64 瞳孔、0–50 cycles/mm | 一条数据集含 300 个频率和两列数值，API 与文本均带同一欠采样提示 |
| #949 FFT X 截线 | 32 瞳孔、中央、自动尺度 | `CaptureFailure/NativeInvalidResult`，空 data-series 缓冲，无冻结原生文本 |
| #952 FFT 衍射包围能量 | 32 瞳孔、质心、5 µm、关闭 Huygens | API 无网格或数列，API 与纯文本均提示欠采样 |

冻结载荷给出采样诊断或缓冲失败。下面的近焦判定来自实际光线几何和独立算子比较。 [原生设置、返回形状与载荷哈希](fft-near-focus-guard-evidence.json)

## 可计算的物理证据

对选定视场、波长和像面，构造经过真实主光线交点的主光线法向平面。每条实际透射光线沿像方方向交该平面，取与主光线交点的距离除以波长。对实际出瞳按面积和透射功率取低分位、中位与高分位。并记录真实像面横向偏差，在倾斜像面中分开保留两种量。多档探针降低单个边缘或渐晕点对判定的影响。

把实际光线方向投影到主光线法平面，比较其方向余弦与原 FFT 采用的线性瞳坐标。另拟合允许平移、旋转、伸缩和剪切的任意仿射方向映射。仿射拟合后的残差仍说明出瞳非线性，且可乘物理像高再除以波长，表示 Fourier 核的相位误差。近轴透射点以成对方式探测，检查缩小入瞳间距后方向余弦间距是否同步缩小。对参考球与原像面相位传递可另记录去活塞和倾斜后的 RMS。

| 独立光路 | 横向偏差第 5 百分位 / 中位 / 最大，波长 | 仿射余弦残差 RMS | 瞳半径 2⁻¹² 对向光线的余弦差 | 去活塞和倾斜后传递相位 RMS，波 |
| --- | ---: | ---: | ---: | ---: |
| Axicon | 77,210 / 81,217 / 87,431 | 0.029760 | 0.181972 | 234.95 |
| Twyman Green #2157 | 22 / 105 / 305 | 0.005409 | 0.00020330 | 2.527 |
| Scanning mirror #2875 | 30 / 249 / 424 | 0.000729 | 0.00003228 | 0.05555 |
| Scanning mirror #2897/#2919 | 95 / 275 / 833 | 0.000876 | 0.00003134 | 0.13810 |
| Sample Spectrometer #443 | 20 / 83 / 163 | 0.000186 | 0.00004514 | 31.016 |
| Toroid #1365，条件模型 | 1,659 / 16,137 / 39,891 | 0.001313 | 0.00003847 | 602.06 |

轴锥用 131,072 条原生实光线计算面积分位。对照采用两档等面积瞳孔探针，分别为 48×128 和 96×256。Twyman #2157 和 Scanning mirror #2875 的原窗口有收敛原生 FFT 对照，扫描镜 #2897/#2919 与光谱仪 #443 则有各自独立几何模型和正式结果对照。Toroid 条件模型用于区分大横向像差与轴锥特有的角度压缩，两者可以独立发生。 [Twyman](../twyman-streamed-fourier/README.md) [Scanning mirror](../scanning-mirror-folded-fft/README.md) [Spectrometer](../spectrometer-fft-psf-reference/README.md) [Toroid](../toroid-fft-phase-domain/README.md)

轴锥的光线方向映射在物理可采样区域呈近轴平台。对向入瞳半径从 2⁻³ 缩小到 2⁻¹²，间距缩小 512 倍，余弦差仍保留 82.7%。在最小的原生采样半径 1.126×10⁻⁶ 处，单侧余弦仍为 0.090949。处方第 3 面是有圆钝尖端的双曲面，曲率为 10⁸ mm⁻¹，圆锥常数 −33.163437。圆锥面斜率过渡的特征半径为 `1/(c√(−1−K)) = 1.763×10⁻⁹ mm`，按前两面近轴增益换算到入瞳约为 2.49×10⁻¹¹。该量级说明为什么实际受光区域的方向压缩会持续。精确模型在数学轴心仍光滑。

在原 128² 像面上，FFT 与 Huygens 各自加密的相对 L2 变化均低于 2.3×10⁻⁷，两算子在相同坐标与相同理想强度标度下相差 0.43017。绝对差约 2.08×10⁻¹⁰，属于很低的原像面信号。43% 描述算子差异，不用于判定原比较门槛。四项共享原视场、波长和像面，因此共享这项近焦几何证据。各输出量的条件 FFT 数值另行归档。 [同坐标算子比较](fft-huygens-applicability.json) [原截线与能量曲线](fft-949-952-continuum.json) [原 MTF](fft-947-mtf-continuum.json)

## 产品诊断设计

1. 在显式近焦诊断入口追迹同一请求的透射光线。沿用现有的视场、波长、实际瞳孔、光阑、瞄准、透射权重和像面。对多视场或波长逐项记录。用可重复的多档、多个方位探针核对覆盖，跟踪有效透射权重，独立保留采样收敛、坐标尺度和光学适用性。
2. 以文档的数百波长量级提供软提示。强判定需要全透射束的低分位仍大幅偏离，并有独立物理佐证。对轴锥，强佐证可以是原处方可核验的有限角度平台加压缩尺度，也可以是同一原域内两种独立收敛的传播算子差。可把低分位超过 10,000 波长作为首轮保守工程筛选值，只有射线覆盖和第二证据都完成后才进入「近焦前提未满足」。该值用于筛选本类极端域，需用扩展控制组校准。Ansys 文档没有规定产品硬阈值。
3. 在有多个有效透射象限的实际瞳孔上要求角度平台重复出现。中心被遮挡、光线失败、可用透射权重过低、角度探针未稳定时，保留「证据不足」或软提示。旋转角、正有限 F 数、局部相位采样、绝对低峰值和单个边缘光线均各自保留诊断。圆锥过渡可由曲面及追迹公式证明或保守估界，避免把离散角度平台误报为真实不连续。
4. 保留原 FFT 与 Huygens 各自的请求和结果类型。仅在物理证据完备的 FFT 分支返回专门的 `NotApplicable` 原因及物理度量。采样预算耗尽保留现有 `Unavailable` 和可重试信息。Huygens、真实无焦分析和缺乏域证据的固定请求沿用各自路径。冻结原生欠采样或缓冲失败仍作为单独证据记录。

硬判定使用的是证据交集。单独使用最大偏差数百波长，会误伤有收敛证据的扫描镜。单独使用参考球传递相位 RMS，会误伤有限共轭的光谱仪。Twyman 的快速 F 数和密瞳相位要求也需要保留独立判断。建议回归同时覆盖上述有限系统、圆瞳与环形遮挡、显式瞄准、轴锥正常焦面与重新选面，以及 FFT 和 Huygens 各自的入口。

使用 `python Probe-FftDomain.py <仓库目录> <native-ray-phase.bin.gz> <输出目录>` 重放。程序复用同目录及相邻证据目录中的独立顺序追迹，逐项核对冻结原生返回。 [复算程序](Probe-FftDomain.py)
