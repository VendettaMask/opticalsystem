# PSF、MTF 与衍射圈入能量性能

2026-11-26。本轮将三处优化接入共享 C# Core，使用实际 Workbench 分析入口计时和对照结果。未运行完整 95 镜头精度矩阵。

## 已实现

- **Huygens PSF**：把独立像面行的直接积分分配给最多 4 个工作线程。每个像素保留完整、顺序一致的发射点求和，标量／矢量场、球面传播、有限单元足迹和归一化均沿用原定义。小任务及已有 Fourier moments 加速路径保留原调度。取消令牌随工作线程传递。
- **FFT MTF**：通过投影定理，先对完整原始 PSF 求两个方向的投影，再做两次一维 FFT，直接取得轴向复数 OTF。省去用于提取两条轴线的两张完整复数矩阵；保留相位、方向、原点、频率轴及 DC 归一化。完整二维 OTF API 仍供需要二维场的调用方使用；原 FFT 网格约束保留。
- **FFT 衍射圈入能量**：用索引循环合成多波长像面强度，消除每像素的选择器、闭包和迭代器。波长求和顺序、光谱权重、像素坐标、积分距离分箱及曲线计算保持。

源码：[MTF 轴向计算](../../../../src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.MtfAxes.cs)、[Huygens 行调度](../../../../src/OpticalSystemDesign.Core/Analysis/DiffractionEngine.cs)、[能量图合成](../../../../src/OpticalSystemDesign.Core/Analysis/Radiometry/EncircledEnergyVariants.cs)。

## 本机计时

环境：.NET 10.0.8，24 个逻辑处理器，Release；内置 Cooke triplet 作为小规模性能输入。每组采用同一份真实界面默认参数，再覆盖下表采样；先预热一次，再测三次，列出中位数。入口为 `WorkbenchRuntime.BuildAnalysisData`，计时不包含窗口绘制，也未接入 `AnalysisService` 的共享光线缓存。多线程任务的累计 CPU 时间不等于墙钟时间。

| 分析与设置 | 改动前 | 改动后 | 累计托管分配：前 → 后 |
|---|---:|---:|---:|
| Huygens PSF，光瞳 64²／像面 128²，轴上、多波长 | 4.384 s | 0.590 s | 13.3 → 13.4 MB |
| FFT PSF，光瞳 256²／显示 512²，轴上、多波长 | 0.283 s | 0.278 s | 111.2 → 111.2 MB |
| FFT MTF，采样 256，全部视场／多波长 | 1.641 s | 1.433 s | 667.3 → 592.0 MB |
| FFT 衍射圈入能量，固定光瞳 256²，全部视场／多波长 | 1.006 s | 0.922 s | 617.8 → 399.3 MB |

MB 为十进制累计托管分配量，不是峰值内存。FFT PSF 的耗时基本持平；本轮没有调整其 FFT 算法。计时来自本机样例的三次观测，逐次墙钟、CPU 和分配值与全部参数保存在归档中。

首次改动后计时与测试编译重叠，记录为 `after`，不用于上表；上表使用单独运行的 `after-quiet`。随后另行重建前版本的补充计时在 55 秒预算内取消，没有形成完整计时组，不纳入前后比较；该重建版本用于下述失败复核。原始前版本计时采用当时正常 Release DLL，身份见 `source-before.json`，源码复原经过逐字节 SHA-256 核对。

## 结果与回归

- 对四组完整 `AnalysisData` 的结构、数值、采样／物理轴和诊断逐项比较：Huygens PSF、FFT PSF、圈入能量完全一致；MTF 最大绝对差为 **3.5388358909926865e-16**，仅数值舍入不同，结构／参数一致。
- 最终 **150 项定向测试：141 通过、9 失败、0 跳过**；新增 8 项 MTF 检查全部通过，覆盖独立二维 DFT、离轴点的复数相位、全零输入、网格拒绝和计算中取消。既有球面／平面 Huygens、偏振及部分原生能量曲线回归通过。
- 9 项失败均在复原后的改动前代码中以相同误差重现：`apo-poly64`、`0014-all-waves`、`0308-all-waves`、`isolate-w8` 的实际／理想能量曲线共 8 项；`Incoherent Irradiance` 状态断言 1 项。原误差和阈值保存在 [tests.json](tests.json)，没有通过修改阈值改变判定。
- 正常 Release Core、Tests、App 构建均成功，0 警告／错误；App 输出中的 Core DLL 与验证后的 Core 输出哈希一致。主程序未启动，未操作用户窗口。

定向测试覆盖：`FftMtfProjectionTests`、`FourierPupilCorrelationTests`、`HuygensSphericalNativeTests`、`HuygensPolarizationRepairTests`、`HuygensPlanarIntegrationTests`、`FftCumulativeEnergyTests`、活动的 `PolychromaticCumulativeEnergyTests`、`DiffractionEnergyRecoveryTests`、`OpticalNumericalConsistencyTests`，以及两个相关取消行为测试。退休参考用例通过 `AcceptanceScope!=RetiredReference` 排除。

## 继续优化的位置

1. **自动加密的光瞳／衍射结果复用。** 默认全部视场的圈入能量在原始测试中超过 15 秒仍未完成；单独轴上视场约 0.05 秒。固定 64² 和 128² 在该输入中未通过局部相位检查，固定 256² 虽通过相位检查，但自动加密至 256 的相邻结果差约 0.0102，尚未达到原 0.001 收敛标准。本轮没有降低标准或将固定 256 的计时表述为默认自动模式已完成。
2. **跨分析的有界中间结果缓存。** 当前共享缓存保留光线，不等于缓存完整 PSF／复光瞳／OTF。可按光学快照、视场、波长、采样、参考、瞄准、偏振和物理窗口复用相同定义的数据；不同能量积分约定与不同采样网格需要分别处理。
3. **更少的光瞳和矩阵临时分配。** 256 采样 MTF 仍累计分配约 592 MB，后续应优先检查光瞳构建和 PSF 重排，而不是只调 FFT 蝶形内核。

还测试了预计算 FFT 置换／旋转因子的独立原型，复数结果逐位一致，但 256／512 网格的中位耗时没有改善，1024 有改善。本轮没有将该原型接入产品。

## 回执

- [计时、分配与完整结果差异](comparison.json)
- [逐项失败复核](tests.json)
- [源码与 DLL 身份](sources.json)
- [归档及内容索引](archive.json) · [原始输入设置、结果、TRX、源码诊断与构建回执](evidence.zip)

归档只保存一份相同的完整结果：三个完全一致的 after 结果指向各自 before 文件；MTF 的两个结果分别保存。ZIP 内每个条目的 SHA-256 均已回读核对。
