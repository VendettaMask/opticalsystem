# 软件如何计算

入口为 **帮助 → 用户指南 → 计算原理**。当前按使用流程组织为 27 章、39 组排版公式，覆盖产品分析目录中的 76 个条目。界面与 PDF 共用同一份离线矢量公式资源；对应关系完整不代表所有算法、面型和参数均已通过外部数值认证。

逐项对应见[分析入口核对表](COMPUTATION_COVERAGE.md)。新增入口必须在定向检查中找到实际计算章节，不能只凭一段通用“打开后运行”说明计为覆盖。

## 数学排版与 PDF

右上角 **导出 PDF** 提供当前章节、全部计算原理、完整指南三种范围。导出采用点击时的语言和章节，正文使用嵌入字体，公式保持矢量，保留相关实际截图。生成中可取消；失败和取消均保留原目标文件。不依赖在线服务、浏览器或外部 TeX 安装。

全部公式以数学形式集中在[公式附录](COMPUTATION_FORMULAS.md)，不再用代码字符串代替分式、积分、矩阵或上下标。源定义在 `Assets/UserGuideMath/formulas.json`，维护工具只在开发时生成轮廓，不参与光学求解；产品绘制与导出均为 C#。

## 计算链与定义

| 主题 | 定义及适用边界 | 实现或详细说明 |
| --- | --- | --- |
| 工程快照 | 处方、结构、孔径、视场、波长、材料和设置一起进入分析。编辑后重算；显示网格和颜色不改变光学模型。 | [分析服务](../src/OpticalSystemDesign.Application/Services/AnalysisService.cs) |
| 单位、坐标、材料 | 几何通常用 mm，材料查询用 nm；换算见公式附录。角度与高度视场、局部与全局坐标不同，材料按真实模型求折射率和消光。 | [材料](../src/OpticalSystemDesign.Core/Materials) |
| 实光线、面型、GRIN | 按实际面型相交、检验口径、折反射、累计光程。圆锥/偶次非球面与其他自由曲面分别处理；GRIN 沿梯度积分并检查步长和边界。 | [几何](../src/OpticalSystemDesign.Core/Geometries)、[追迹](../src/OpticalSystemDesign.Core/Raytrace) |
| 光瞳采样与瞄准 | 节点、面积权重、真实光阑瞄准、渐晕和无效样本共同定义积分。不能通过只重归一化幸存光线隐藏失光。 | [采样支持](../src/OpticalSystemDesign.Core/Analysis/Shared/AnalysisTrace.cs) |
| 一阶、基面、Y-Ybar | 使用高度与几何斜率矩阵；空气到空气焦距关系有前提。Y-Ybar 对主光线像高有明确缩放。近轴焦点不等于实光线最佳焦点。 | [近轴矩阵](../src/OpticalSystemDesign.Core/Services/Paraxial.TransferMatrix.cs)、[Y-Ybar](../src/OpticalSystemDesign.Core/Analysis/Fields/YYbarAnalysis.cs) |
| 点列、Seidel、场曲与色差 | 质心/主光线参考、权重、失败样本和单位需一致。Seidel 三阶基底、实光线畸变、色焦移、横向和轴向像差不能混用。 | [点列](../src/OpticalSystemDesign.Core/Analysis/Rays)、[Seidel](../src/OpticalSystemDesign.Core/Analysis/Reports/SeidelCoefficientsAnalysis.cs) |
| OPD、参考球、PSF | OPD 为参考减实际光程，当前 FFT 瞳相位与变换符号配套。主光线、质心和最佳拟合球不同。FFT/Huygens 保留各自映射、权重、物理采样和归一化。多色通常叠加强度。 | [波前](../src/OpticalSystemDesign.Core/Analysis/WavefrontEngine.cs)、[衍射](../src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs) |
| MTF、采样与局部损失 | 几何像点 OTF、衍射 OTF 和 Fringe 波前剪切的采样 MTF 是不同算法。Moore–Elliott 损失图在瞳面评价光线对，不是全视场 MTF。 | [采样 MTF](../src/OpticalSystemDesign.Core/Analysis/SampledMtfEngine.cs)、[局部损失](../src/OpticalSystemDesign.Core/Services/ContrastMetrics.cs) |
| 能量、LSF、ESF | 几何能量累加加权射线；衍射能量积分 PSF。LSF 是投影积分，不能用中心截线代替；ESF 再累计。扩展源还需源分布。 | [能量与辐射](../src/OpticalSystemDesign.Core/Analysis/Radiometry) |
| Zernike | 系数使用有效节点上的未加权 QR 最小二乘；强度加权波前统计是另一计算。Fringe、Standard、Annular 的编号、归一化及遮挡比不互换。 | [拟合](../src/OpticalSystemDesign.Core/Analysis/ZernikeFitEngine.cs) |
| 干涉图 | 等强、归一化瞳完全重合的两束波前差，加入可选主光程项；倍率后加入瞳倾斜，再求条纹强度。 | [干涉定义](INTERFEROGRAM_2026-10-26.md) |
| 傅科与角谱 | 默认焦面刀口后理想瞳再成像；空间/矢量角谱使用实际距离。总功率、中央显示功率和刀口透过率分开，不宣称任意介质矢量 POP。 | [傅科](../src/OpticalSystemDesign.Core/Analysis/Wavefront/FoucaultEngine.cs) |
| 照度与辐射量 | 相对照度除以本次扫描的最大投影余弦面积。绝对辐照度和辐射强度分别按面积与立体角定义；归一化后不再是绝对功率。 | [相对照度](../src/OpticalSystemDesign.Core/Analysis/RelativeIlluminationAnalysis.cs) |
| 扩展和部分相干图像 | 强度叠加、电场叠加、互强度传播不同。当前均匀相干模型由对角项及秩一相干项构成，保留空间采样、相位和资源约束。文件查看器不新做光学计算。 | [图像](../src/OpticalSystemDesign.Core/Analysis/ImageSimulationEngine.cs)、[互强度](../src/OpticalSystemDesign.Core/Analysis/MutualIntensityEngine.cs) |
| Jones 与薄膜 | 矩阵元素为复电场，不是四个功率透过率。Jones 瞳有独立的 Fresnel/吸收开关，明确拒绝未接入的物理多层膜。正式薄膜求解用复相位稳定递推和边界能流归一化。 | [Jones](../src/OpticalSystemDesign.Core/Analysis/JonesPupilEngine.cs)、[薄膜](../src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs) |
| 扫描和结构 | RMS 视场扫描取密度加一个节点，其他扫描有各自设置。离焦使用计算副本。结构曲线叠加不自动成为电场相干合成。 | [扫描](../src/OpticalSystemDesign.Core/Analysis/RmsScanAnalyses.cs)、[结构](../src/OpticalSystemDesign.Application/Services/AnalysisConfigurationSelection.cs) |
| 优化和导数 | 正权重目标、负权重约束、零权重监视分开；总绝对权重参与归一化。DLS 使用阻尼和约束步进，Nelder–Mead 使用单纯形。差分和可微追迹检查实际边界及有效分支。 | [框架](../src/OpticalSystemDesign.Core/Optimization/OptimizationFramework.cs)、[优化器](../src/OpticalSystemDesign.Core/Optimization/NumericalOptimizers.cs) |
| 公差 | 灵敏度扰动单项，Monte Carlo 同时抽样，补偿器不作为制造误差。种子按试验编号派生，失败不能删去提高良率；估计值不是生产实测。 | [公差](TOLERANCING.md) |
| 非序列 | 最近前向交互、初始介质、分支与截断形成能量账目。像素功率与面积换算为辐照度。清空与累加不同，数据库须匹配场景、材料及探测器几何。 | [非序列](NON_SEQUENTIAL_TRACING.md) |
| 相机 | 光学像按像元面积、曝光、波长和绝对 QE 转为光电子，再经噪声、满阱、ADC 与 ISP。RGB 不是实测光谱，连续模式积分输入光谱。 | [相机](CAMERA_IMAGING_LAB.md) |
| 初始结构 | 标准托管 AutoLens 采用共享 Core 和 Adam，专用训练损失不同于通用 RMS。自主拓扑是另一实验协议。候选需独立验收，实际设备、回退和预算保留证据。 | [标准策略](AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.md)、[设备](AUTOLENS_GPU.md) |
| 装调 | 共享实光线残差的微扰导数与外推给出近轴共轭；叉丝像是有限孔径的几何回程预览，亮度归一化，不含正式镀膜/杂散光功率，不是通用装调矩阵求逆。 | [共轭与预览](../src/OpticalSystemDesign.Core/Analysis/Assembly/AssemblyConjugates.cs) |

## 判断结果是否可靠

统一工程版本、结构、材料、孔径、波长、视场、参考、采样、物理窗口与归一化，再比较数值。增加采样或扩大窗口检查收敛，保留失光、失败、截断和回退。显示光线数不能替代衍射采样，训练损失不能替代正式验收。

主要外部数值依据是保存的 Zemax OpticStudio 2026 R1 指定捕获，只覆盖相应文件和设置。截图和 PDF 用于说明与阅读，不扩大数值验证范围。最新证据见[当前状态](CURRENT_STATUS.md)。
