# 标准镜头 OPD 修复与复验 · 2026-10-06

本文保留 OPD 阶段 82 Pass 的历史修复与验证。当前原矩阵 95 Pass、独立 RMS 17+6 项全部 Pass，旧快照和重新导入两条路径见[最新复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文剩余问题、测试计数与后续建议仅描述当时状态。

共享 Core 的两处 OPD 问题已修复。六份官方标准镜头的原设置共 132 项重算，结果由 **76 Pass / 15 Close / 30 Difference / 10 Incomparable / 1 Error** 变为 **82 Pass / 12 Close / 27 Difference / 10 Incomparable / 1 Error**。原有 76 项 Pass 全部保持 Pass，6 项 OPD 转为 Pass；12 组 OPD 中 11 组 Pass，Tessar 边场仍不可比。Close、不可比及运行错误均不计为通过，商业软件精度与发布验收仍未完成。

## 已实现的修复

- `OpticalPathDifferenceAnalysis` 的各波长参考球、主波长参考球及光线采样统一采用源光学系统的 `RayAimingEnabled`；`VignettedPupil` 不再决定是否瞄准停面。结果发布实际 `UseRayAiming`，原设置值和构造器通用默认值保留。
- `WavefrontEngine.LaunchTiltDirection` 只为明确的正无限 Object 厚度添加平面波的入瞳相位项。有限物距已从同一物点累积光程，角度视场不会再次添加该项。零物距和极小正物距同样属于有限共轭。
- 共享 `ReferenceSphereWavefrontEngine` 使用同一相位条件；没有加入实验室或比较工具专用的追迹、相位、衍射公式。
- 渐晕坐标变换及 OPD 的 `VignettedPupil` 光瞳范围契约尚未补齐。本次只解除它与瞄准的误耦合；不能据此宣称渐晕光瞳选项已全面验证。

## 相同捕获设置的实测改进

下表是单项两方向曲线中最大的绝对误差，单位为 waves；NRMSE 取两方向中最差值。直接使用原始物理轴与原捕获容差，没有拟合、平移、峰值归一化、补零或减少数据列。

| 镜头 / 设置 | 修复前分类 | 修复前最大误差 | 修复后最大误差 | 修复后最差 NRMSE | 当前分类 |
| --- | --- | ---: | ---: | ---: | --- |
| Cooke / 轴上、550 nm | Close | 0.01027139068 | 1.552293849e-10 | 1.010619105e-10 | Pass |
| Double Gauss / 轴上、587.6 nm | Close | 0.02196377653 | 2.422235745e-10 | 9.249240770e-11 | Pass |
| Tessar / 轴上、589 nm | Close | 0.01761951305 | 1.730365018e-10 | 6.294489782e-11 | Pass |
| Cooke / 20°、480 nm | Difference | 364.9299539 | 2.624171760e-5 | 7.332213069e-6 | Pass |
| Double Gauss / 14°、486.1 nm | Difference | 384.1548172 | 4.031837608e-5 | 1.380833684e-5 | Pass |
| Relay / 2°、486.1327 nm、物距 250 mm | Difference | 897.3757764 | 6.317280772e-7 | 2.191602117e-7 | Pass |

原 OPD 门槛保持 `absolute=1e-5`、`relative=0.001`、`NRMSE=0.003`、`Close NRMSE=0.01`、`minimumCoverage=0.95`。原比较规则在覆盖率合格后接受逐点误差条件或曲线 NRMSE 条件；不是要求绝对误差、相对误差和 NRMSE 同时通过。

| 镜头（每份 22 项） | Pass | Close | Difference | Incomparable | Error |
| --- | ---: | ---: | ---: | ---: | ---: |
| Cooke | 14 | 1 | 7 | 0 | 0 |
| Double Gauss | 12 | 3 | 6 | 1 | 0 |
| Doublet | 14 | 2 | 3 | 2 | 1 |
| Even Asphere | 12 | 2 | 6 | 2 | 0 |
| Relay | 18 | 1 | 2 | 1 | 0 |
| Tessar | 12 | 3 | 3 | 4 | 0 |
| 合计 | **82** | **12** | **27** | **10** | **1** |

132 项包括每份轴上/主波长 18 项，以及最后定义视场/第一波长的单光线、点列、几何光扇、OPD 4 项。涉及全波长或扫描的契约仍采用原捕获请求。6 份一阶量及 12 组点列标量继续通过；只有上述 6 项分类改变，没有新跳过项。其余 119 份已成功计算的非 OPD 原始 Workbench JSON 与旧输出逐字节相同；另一项非 OPD 计算仍是原有 Doublet 场曲/畸变错误。另两次 128 采样控制沿用首批记录，未重算，也不计入 132 项。

## 原生证据与当前重算的边界

本次不是再次调用 OpticStudio，而是用当前默认 Release 的正式 `Application.WorkbenchRuntime.BuildAnalysisData` 和共享 Core，重算捕获时保存的同一 `configuration-1.json`。对侧采用 2026-10-06 原生 OpticStudio **26.1.0 build 260127 / EnterpriseEdition / 有效许可** 捕获的数据。全部 132 份原生环境记录的版本、许可与初始化状态复核通过。

每项验证原文件及捕获副本 SHA-256、原配置 SHA-256、原生请求指纹，以及已有 Workbench 证据中的快照和请求指纹。重算后再次验证 **1769 个证据文件** 未变化；新旧原始 Workbench 数值、原生数值、规范化、误差、请求、容差、程序集哈希及分类均可追溯。冻结 `123456` 主参考与 Optiland 辅助历史文件没有修改。

原生诊断接纳规则继续生效。带采样不足或无效结果提示的原生输出维持不可比；不会因本次 Core 修复改为可信参考。原单光线十一列全部保留，无限远首段发射平面约定的几何段长差异继续计为 Difference。

辅助程序第一轮缺少 SkiaSharp 原生依赖，其部分绘图失败被错误归入不可比，整轮结果已在 `opd-core-repair/INVALID_RUN.json` 标记无效并排除。正式复验来自独立新目录 **`opd-core-repair-v2`**；使用项目引用加载正式比较工具的完整依赖，并复用 `ComparisonRunner.Compare`，没有另一套数值比较规则。

## 回归覆盖

新增 **12 项**正式测试：三份真实镜头各两组捕获设置，验证两方向全部 41 点与参考轴；有限角度场与同一物点的物高场在物距 0、1e-9、250 mm、瞄准开/关时相位一致，同时检查普通主光线、质心参考球和最佳拟合参考球。新参考只从已捕获的三份快照与六份原生 OPD 输出复制，包含原文件、快照和原生数据哈希；没有更新已有主参考或历史辅助参考。

默认 Debug/Release 正式解决方案构建均零警告、零错误，结果来自默认产品输出目录。定向回归 **16/16**，包含上述 12 项以及原 OPD、冻结 `123456` OPD 与 Wavefront Map。

| 实际验证范围 | 结果 | TRX |
| --- | --- | --- |
| 正式完整 Release | 4306 通过 / 1 个既有失败 / 4307 项，零跳过 | main-release.trx |
| 正式完整 Debug | 4304 通过 / 3 个失败 / 4307 项，零跳过 | main-debug.trx |
| 新增 OPD 回归 | 两配置全量中均 12/12；不另加到总数 | 两份正式全量 TRX |
| OPD 与主参考定向 | 16/16 | opd-targeted-final.trx |
| Debug 等待超时用例与 OPD 隔离复验 | 16/16 | debug-timeouts-isolated.trx |
| 比较工具完整 Release | 118 通过 / 2 个既有失败 / 120 项 | comparison-release.trx |
| 初始结构完整 Release | 258 通过 / 2 个既有失败 / 260 项 | initial-structure-release.trx |
| 镀膜完整 Debug/Release | 各 47/47 | coating-debug.trx / coating-release.trx |

正式两配置共有的既有失败仍是历史有限角度光线参考，预期 `0.058589983242791625`，实际 `0.060191701479492175`。Debug 在并行运行实验室的同一轮中另有 `OpenWaitingForCommitCannotOverwriteANewDocument` 和 `OptimizationPreparationDoesNotBlockReadsAndRejectsConcurrentEdits(quick)` 的 5 秒等待超时；它们与全部新增 OPD 在独立测试进程复验 16/16 通过。该现象支持调度波动的判断，但根因尚未最终定责。原断言、5 秒门槛与首次全量 TRX 保留；隔离通过不冲销全量失败，当前发布门禁仍未通过。

比较工具的既有 Huygens PSF 截面和衍射包围能量非退化失败值仍为 `0.0037256670590166724`、`0.011467280942848471`，与修复前完全相同。初始结构仍是 `trial-0078` 重启可行性与 Secant `index: 9` 目标失败。全部计数与错误详情保存在最终验证清单，不把测试通过、原生参考完整性或当前重算混为一种数值认证。

## 下一批工作

1. Tessar 边场：统一渐晕后的物理光瞳横轴、采样点和有效掩码，并定义 `VignettedPupil` 开/关的准确行为。目前 OPD 子午/弧矢覆盖率仍为 **0.694091797 / 0.9145002365**，低于原 0.95 门槛；子午最大误差仍为 **2035.7126027403647 waves**。几何光扇还有无效点，不能删点或扩宽门槛。
2. 处理剩余 FFT/Huygens PSF、包围能量、离焦 MTF、相对照度及 RMS 视场误差；用多个有效原生采样验证收敛。Double Gauss 的 128 采样控制仍有真实差异，Even Asphere 的原生警告仍未消除。
3. 补齐 Doublet 唯一轴上视场的退化结果契约：场曲/畸变错误，RMS 视场及照度扫描不可比。
4. 在以上问题建立回归后扩展剩余 **21 份**标准镜头与完整视场/波长矩阵。本次没有执行这些文件，不能将 6 文件结果推广为全软件精度认证。

证据：

- [持久化 132 项修复结果](validation/ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.json)、[当前误差矩阵](../artifacts/zemax-standard-samples/20261006/opd-core-repair-v2/matrix.csv)、[原始首批结果](validation/ZEMAX_STANDARD_SAMPLE_RESULTS_2026-10-06.json)。
- [重算汇总与新旧证据](../artifacts/zemax-standard-samples/20261006/opd-core-repair-v2/summary.json)、[1769 文件完整性清单](../artifacts/zemax-standard-samples/20261006/opd-core-repair-v2/immutable-evidence.json)、[辅助程序源码](../artifacts/zemax-standard-samples/20261006/core-replay/Program.cs)。
- [新增正式回归与原生参考说明](../validation/zemax/2026-r1/standard-sample-opd-2026-10-06/README.md)、[测试源码](../tests/OptilandWorkbench.Tests/StandardSampleOpdParityTests.cs)、[最终构建与测试清单](../artifacts/validation/zemax-standard-sample-opd-20261006/verification-final.json)。
