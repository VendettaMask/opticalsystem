# Afocal Riflescope：能量、照明与成像 15 项复核

本组原始镜头为 `src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX`，SHA-256 为 `36db5e0bd00f9435dbd0495d0bff5f5ccfa121ae95114dc75b11efdccf66570b`。逐项机器可读结论见 `audit-energy-imaging.json`。原图、原生 CFG 与原生截图均保留；本次没有操纵或重新捕获原生软件窗口。

| 索引 | 输出 | 结论与原因 |
|---|---|---|
| 18 | Encircled Energy | 设置差异：产品使用 mrad，原生使用角分；原自动范围、取样与参照也不同。`encircled-energy-aligned.png` 使用 10 角分对应的 2.908882086657216 mrad 范围，并保留当前角主光线参照。没有原生逐点数据，不宣称数值认证。 |
| 19 | Diffraction Encircled Energy | 原默认 64 采样有相位混叠风险。1024 仍未满足相位与 0.001 收敛目标；另发现 >1024 的受限窗口只允许有限像方，错误拒绝无焦系统。本次补真实角 Fourier 窗口，完整复验结果在下文登记。 |
| 20 | Geometric Line Edge Spread | 实现错误已修复：无焦光线坐标原本已经是 mrad，却被再次乘以 1000 并标为 µm。现在按真实角坐标建立直方图，5 角分对应半宽 1.454441043328608 mrad。原生两面板与产品同图双曲线仍是表示差异。 |
| 21 | Extended Source Encircled Energy | 原图使用修复前的稀疏点源替代面积源，并误乘无焦坐标。最新共享 Core 按真实发光面积取样、保留 mrad。产品默认均匀方形源宽 0.25°；原生 CFG 实际选择 LETTERF.IMA、宽 3.5355339059327378°。专项图对齐字母源与 2000 角分角范围，另保留产品默认结果。 |
| 44 | Relative Illumination | 两侧均不适用。原生截图写“无法计算相对照度…”，产品说明需要有限像共轭。不能把原始瞳孔投影面积改名为无焦相对照度。 |
| 45 | Incoherent Irradiance | 缺少同输入原生映射/截图。本次导出当前结果用于追溯，不能据此声称一致或原生不支持。 |
| 46 | Radiant Intensity | 缺原生映射。当前为 ±15°角窗内 W/sr；中央截面取全局 Y=0，离轴主峰不一定穿过它，近零中央截面不等于无光线。 |
| 57 | Contrast Loss Map | 缺原生映射。当前为 97×97 瞳孔点对子午/弧矢对比度的贡献，单色 0.587 µm、实际频率约 0.343 cycles/mrad，不能拿另一种 MTF 面图代替。 |
| 65 | Image Simulation | 缺原生输入与图；当前显式要求相对照度权重，在无焦系统被同一物理前置条件拒绝。保留诊断和权重设置。 |
| 66 | Geometric Image Analysis | 同上；内置源、实际光线核和相对照度权重不能与未提供的原生图直接比较。 |
| 67 | Geometric Bitmap Image Analysis | 同上；没有同一位图源和原生结果。 |
| 68 | Light Source Analysis | 缺原生映射。当前委托归一化角辐射强度分析，65×65 角网格；没有物理依据将其改成有限像方能量图。 |
| 69 | Partially Coherent Image Analysis | 缺同源原生输入与图；无焦相对照度权重前置条件明确拒绝。没有关闭权重来伪造输出。 |
| 70 | Extended Diffraction Image Analysis | 缺同源原生输入与图；当前默认同时请求相对照度权重，保留明确不可用诊断。 |
| 71 | Jones Pupil | 表示/输入差异：产品绘出完整 Jones 矩阵四个复分量的实/虚部；原生绘出固定 Jx=0、Jy=1 输入下的矢量/椭圆。热图、特定偏振矢量及整瞳透过率是不同物理读数。 |

## 本次代码与回归边界

`GeometricLineEdgeSpreadAnalysis` 按像方类型选择 mrad 或 µm，连同 typed axis 与数值元数据一致修正；参数页半宽标签按系统类型显示。

无焦衍射累计能量高档窗口与既有全 FFT 共用同一出瞳直径、波长相关网格间距和非零场投影椭圆伸长。窗口内使用独立 Fourier 频点，窗口外总功率及完整图像质心通过同一离散瞳孔的 Parseval 总功率和解析 Fourier 一阶矩获得。各波长只组合强度；谱权重、理想峰归一化和标量振幅插值沿用原路径。没有把裁剪窗口重新归一化为 1，也没有放松相位阈值、收敛误差或资源预算。

新增测试覆盖无焦 mono/poly、非零场投影、chief/centroid、角坐标平移不变、相位保护与有限像方回归。低档全 FFT 和受限窗口同节点比较仅验证有限和实现等价，不能代替独立加密收敛。

`energy-imaging.trx` 是一次 VSTest 测试宿主连接超时记录，不能计为测试通过。`checks.json` / `checks-direct.log` 记录直接调用 xUnit 断言的实际通过项；最终新增断言及原文件高档复验结果将在同目录日志中记录。

## 专项截图

- `encircled-energy-aligned.png`：10 角分同范围的几何累计能量。
- `line-edge-spread-fixed.png`：修复后的 ±5 角分线/边缘扩散。
- `extended-source-default.png`：保留产品默认均匀面积源。
- `extended-source-aligned.png`：LETTERF.IMA 同源专项对比。字母源只复用现有源文件，没有运行该目录下的其它镜头。

对应 JSON 保留实际输入、范围、采样和曲线点；`*-latest.json` 是本次当前实现的原始输出。每一项证据路径和状态均在审计 JSON 中对应原索引，缺失参照与尚未收敛单独记录。
