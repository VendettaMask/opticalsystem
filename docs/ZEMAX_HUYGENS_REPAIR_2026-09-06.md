# MS-L7 Huygens MTF 采样修复（2026-09-06）

本轮确认并修复了 Huygens MTF 后处理的三个错误：普通 MTF/视场 MTF 缺少零填充，
频率坐标使用了错误的跨度，频率间取值使用线性插值而非本次捕获的自然三次插值。
这些错误是剩余差异的一部分原因。PSF 波场合成和衍射圈入能量积分仍有独立差异，不能宣称全分析已对齐。
2026-09-06 用户后续授权将这些本地修改提交并同步到 `origin/main`。
此前运行发生于未提交工作区；原始捕获中的 Git 状态、历史阶段记录保持原样，不倒填提交身份。
冻结 `validation` 文件禁用 Git 换行转换，提交时逐字节核对暂存内容，避免跨平台检出破坏哈希。

## 如何确认原因

先将 **Zemax 自己的 PSF** 输入同一 C# 后处理，排除 Workbench 光线追迹和 PSF 合成的影响：

| 分析 | PSF 像面网格 | MTF 变换网格 | 频率取值 |
|---|---|---|---|
| Huygens MTF / MTF vs Field | N × N | 零填充到 2N × 2N | 端点跨度上的自然三次插值 |
| Huygens Through Focus MTF | N × N | N × N | 端点跨度上的自然三次插值 |

设变换大小为 K、PSF 像素间距为 Δx。物理 DFT 周期仍为 `K*Δx`；
捕获输出采用的端点跨度为 `(K-1)*Δx`，因此在物理 DFT 轴上评价目标频率 `f*(K-1)/K`。
不会修改 PSF 像素间距、平移源图、拟合频率比例或改变容差。

这不是从一个误差曲线猜出的补偿：除 MS-L7 的 32 × 32 网格之外，
独立捕获其 64 × 64 网格，并在主基准 `123456.ZMX` 上重复验证。
三个普通 MTF 捕获的两轴、每轴全部 **300 个原生点**均重现，最大绝对差分别为
`4.44e-16`、`5.55e-16`、`6.66e-16`。
离焦分析另验证 MS-L7 在零离焦处的 50 / 125 / 250 / 500 cycles/mm，
以及主基准的 50 cycles/mm；视场分析独立验证轴上六个频率。
回归容差为 `1e-10` 绝对调制度，只验证这一后处理契约。

原有直接 DFT 任意频率求值不能替代这一捕获输出约定：它没有同样的离散网格插值。
因此保留直接 DFT 的原有意义，没有用原生结果拟合产品 PSF。
设置、单位、两份镜头、软件版本、原始数组及哈希见
[冻结验证资料](../validation/zemax/2026-r1/huygens-mtf-sampling-2026-09-06/README.md)。
这些是具体镜头和 OpticStudio 2026 R1 捕获的规则，不是通用 Zemax 默认值。

## 代码与架构变化

- `DiffractionEngine.ComputePsfMtf` 支持显式零填充，保留物理像素间距，并在分配前检查变换大小上限。
- `MtfMethodEvaluator` 将频率/视场绘图与离焦绘图的变换大小分开；兼容模式在端点跨度上做自然三次插值。
- Huygens 分析结果增加变换大小和频率取值约定的元数据，便于复算。
- Core 的一般默认保持不填充、物理 DFT 轴、线性取值；Application 现有显式 `UseZemaxHuygensSemantics` 预设使用修复后的约定。
- 新增 14 个回归用例：原生捕获重建、独立镜头/网格、两点光源解析 MTF、一般默认、资源限制和冻结文件完整性。

依赖仍为 tools → Application/Core，tests → validation；产品不访问 ZOS-API、Python、外部工具或测试资产。
本轮不改动文件格式、光线追迹、材料、优化或公差代码。STAROPT、ZMX、SEQ、LEN 入口保留。
Huygens 相关分析数值会改变；读取方应使用类型化轴和采样元数据。
Python Optiland 移除清单与历史命名边界见 [原移除审计](PYTHON_OPTILAND_REMOVAL.md)，
逐文件、逐处保留理由见 [引用索引](RETAINED_PYTHON_OPTILAND_REFERENCES.md)。

## 仍需处理的原因与证据边界

逐光线诊断在同一轴上视场、第一波长、主光线加 740 个圆内光瞳输入下，
像面坐标最大差约 `4.12e-11 mm`，方向余弦约 `3.86e-9`，OPD 约 `1.02e-6` 波。
这排除了这组光线上的大尺度追迹错误，不能推广为所有视场、波长和处方的追迹证明。
诊断原始数据保留于本地 `artifacts/numerical-followup-2026-09-06/phase-host/native-rays`，
不计入正式的 52 项比较覆盖。

本次 MS-L7 的原生 Huygens Auto 与强制 Planar 相同，强制 Spherical 则明显不同。
现有有限像空间波场积分仍需核对振幅权重、光瞳积分和相位传播约定。
试验性的平面波替换没有通过独立数值验证，因此未写入产品；后处理修复不等于传播模型已经完全修复。
具体 Planar/Auto 含义参考
[官方 Huygens PSF 说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)，
该文档用于物理定义核对，版本和实际设置以本机 2026 R1 捕获为准。

衍射圈入能量的独立范围捕获显示，输出半径会影响近原点插值；现有直接像素面积 CDF
不能重现完整的原生积分与显示链条。只增加插值、加密 PSF 或缩放半径都未通过验证，未把这些试验当作修复。
其它 Close、图像/光源契约和物理定义差异继续保留为未解决项。

旁路审查还发现 `MtfMethodEvaluator.EvaluatePolychromatic` 的 Huygens 分支没有向下传递
非 Modulation 数据类型及无焦离焦参数；这些路径不在本次有限像空间 Modulation 契约内，
本轮未修复，不能从上述测试推断其正确性。它们需要单独的复数 OTF 和无焦传播测试。

完整测试还揭示了旧误差抵消：视场 MTF 的 `curve:7` 由 Pass 变为 Close，
而其它多个分量改善。最大 NRMSE 从 `0.00788362` 降为 `0.00702940`，
全分量最大绝对误差从 `0.0246668` 增为 `0.0264807`。
旧的逐分量“不许退步”断言因而失败，失败日志保留。
该项改为明确的未解决误差监控：最大 NRMSE 不超过原值、绝对误差上限 `0.027`，
并继续按原数值容差判定 Pass / Close。这一测试预算允许上述已记录的局部退步，
不代表精度验收；旧冻结数据、旧误差记录和正式比较容差均未修改。

## 最终验证

MS-L7 完整重跑 72 项：**44 Pass、6 Close、2 Difference、17 Incomparable、3 Skipped，0 Error**。
实际数值比较 52 项；另枚举 105 项 Zemax-only，未执行，不计入这 72 项。
两个源镜头的文件哈希和修改时间均未改变。正式比较容差与前次运行配置哈希相同。
完整运行返回码为 1，原因是仍有 Difference，非执行异常。

下表各列分别取该分析全部比较分量的最大值；NRMSE 和绝对误差的最大值可能来自不同分量。

| 分析 | 修复前最大 NRMSE | 当前最大 NRMSE | 当前最大绝对差 | 当前结论 |
|---|---:|---:|---:|---|
| Huygens MTF | 0.002781380 | 0.001702120 | 0.003180558 | Pass |
| Huygens Through Focus MTF | 0.032689150 | 0.022655212 | 0.051783187 | Difference |
| Huygens MTF vs Field | 0.007883622 | 0.007029404 | 0.026480641 | Close |
| Diffraction Encircled Energy | 0.013875843 | 0.013875843 | 0.050537607 | Difference |

离焦 MTF 的最大绝对误差由 `0.094583405` 降为 `0.051783187`，降低约 45%，仍不满足原容差。
其余 Close 为 Fourier MTF vs Field、Relative Illumination、Huygens PSF Cross Section、Jones Pupil、
System Data Report 的出口瞳直径。全部逐项指标见下方持久化 JSON 和完整报告。

独立主基准 `123456.ZMX` 使用工具明确设置重算三项：

| 分析 | 最大 NRMSE | 最大绝对差 | 结论 |
|---|---:|---:|---|
| Huygens MTF | 0.000151364 | 0.000261322 | Pass |
| Huygens Through Focus MTF | 0.002345750 | 0.004802473 | Pass |
| Huygens MTF vs Field | 0.001357812 | 0.004108639 | Pass |

这三项是当前 Workbench 与 Zemax 的完整分析链条比较，与“原生 PSF 重建”测试分开计数；
没有重跑主基准的全 72 项，不将三项 Pass 推广到未测试设置或 MS-L7。

- 强制锁定还原通过：`--locked-mode --force --source C:/Users/19851/.nuget/packages -p:NuGetAudit=false`，未运行在线漏洞审计。
- 完整解决方案 Release 默认输出构建通过，0 警告、0 错误。
- 最终完整测试 **1233 主测试 + 104 工具测试 = 1337**，零失败、零跳过。
- 完整 `dotnet format --verify-no-changes` 与 `git diff --check` 通过；100 个冻结说明/捕获文件的 manifest 校验通过。
- MS-L7 运行期间 70 次 Workbench 执行的计算程序集指纹一致。随后只清除了 `DiffractionEngine.cs` 末尾空行并重新构建、完整测试；Core 的 10753 个方法体、栈/局部签名元数据和异常区域完全相同，IL 审计写入验证 JSON。
- 未执行 GUI 截图、安装包安装/卸载、独立实验室、旧外部报告工具或在线漏洞审计。

[MS-L7 全分析报告](../artifacts/zemax-comparisons/ms-l7-huygens-sampling-repair-2026-09-06-final/COMPARISON_REPORT.md)、
[主基准三项独立复验](../artifacts/zemax-comparisons/primary-huygens-sampling-repair-2026-09-06-final/COMPARISON_REPORT.md)、
[持久化验证与全部逐项数值](validation/ZEMAX_HUYGENS_REPAIR_2026-09-06.json)。
构建、最终测试、格式、失败历史和 IL 审计保留于 `artifacts/numerical-followup-2026-09-06`。
