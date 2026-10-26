# AutonomousTopologyV1：M0/M1 实施与短程证据

日期：2026-10-26。对应[计划 v1.1](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)。本批实现基线与拓扑基础层，保留现有 UpstreamE58；没有恢复用户已停止的完整训练。

最终默认 Release 构建及定向测试 **25/25 通过、0 失败、0 跳过**，其中新基础用例 22 项，旧 Upstream 规则回归 3 项。[TRX](foundation-complete.trx)、[控制台](tests-complete.log)、[机器汇总及 SHA256](summary.json)保存实际结果。测试依赖正常 App 项目，默认桌面输出已更新，未采用另一个输出目录替代。

## 实现与验证范围

新增 Contracts 的 `DesignTopology` 与 Engine 的 `TopologyValidation`、`TopologyCompiler`、`TopologyParameterBinder`、`TopologyOperators`。物理传播、目录色散、近轴量、Gaussian 采样及光斑均来自共享 Core；本批没有修改 Core 光学公式，也未增加 Python 产品依赖。

| 范围 | 实际核对 |
| --- | --- |
| 编译与独立 Core 对照 | 两片球面、三片非零高阶非球面；相同主波长/谱权重/瞳采样，焦距、瞳径、各光线 XY、RMS 和最大半径均通过 11 位小数精度断言 |
| 结构和身份 | 不可变父子、唯一稳定 ID、首/中/末插片、删首片与回删、最小片数/上限、受保护片、内部光阑分割、空间不足及最小间隙 |
| 参数绑定 | 显式物理边界/尺度；绝对轴向、曲率、圆锥、全部 r4–r18 的读写与扰动，r2 固定零；非零机械原点、平面轴向更新、显式解除像面保护 |
| 实际介质与裁光 | 空气/真实目录玻璃、前后面不同有效半口径、独立机械半口径；缩小一面后真实 Core 光线被遮挡，导入后仍保持 |
| 往返与保护 | 模型 JSON、Core 导入往返；可变 Core 副本不污染父；零步保留稀疏项/材料名/保护元数据；0.1 与 10000.123 mm 机械原点及非整数厚度往返；变更设置、身份、材料、坐标系的旧绑定明确拒绝 |
| 不支持输入 | 有限共轭、偏心/倾斜、胶合介质、活动后焦求解、缺失物理口径、非目录或同名异数据玻璃、r2 非零、非法顺序/口径、未授权过渡片等 |
| 旧协议回归 | MOBILE_CONFIG 身份/目录；冻结线性约束、面型、材料及梯度；冻结 Adam 矩与两段调度的规则测试。未执行完整训练 |

结构检查只是顶点/间隙/角色契约，尚不包含完整边缘碰撞、制造和最终像质验收。`Compile` 不等于优化或达标，`Insert`/`Delete` 不等于已完成近轴恢复。

## 冻结输入和实际输出

输入与设置在[短程基准](../../../../labs/InitialStructure/benchmarks/autonomous-topology-v1/README.md)。[两片输出](baseline-2.json)、[三片输出](baseline-3.json)包含模型、Core 完整快照、材料指纹和评价量；[插删谱系](insert-delete.json)保留父子与回删内容。

| 输入 | EFL / mm | 轴上多色 RMS / mm | 5° 多色 RMS / mm | 每个报告视场尝试/遮挡数 |
| --- | ---: | ---: | ---: | --- |
| 两片球面 | 214.479135358866 | 0.940747412577491 | 0.934355205450300 | 192 / 0 |
| 三片非球面 | 49.0692209888653 | 0.151288974391781 | 0.142592278121989 | 192 / 0 |

这里的 RMS 是共同质心的真实多色光斑半径，处方未优化，不据此宣称达标。192 是 4×16 瞳点×三波长的正式评价输入数，不等于包括瞄准在内的全部内部传播工作；完整预算计费留在 M2/M4。上述小采样仅验证一致性，不替代计划中 21 视场、10×32 加边界/中心的独立终验。

## 复现命令

在仓库根目录执行，使用默认构建输出：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_EVIDENCE = Join-Path (Get-Location) 'validation/rechecks/2026-10-26/autonomous-topology-foundation'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AutonomousTopologyFoundationTests|FullyQualifiedName~AutoLensReferenceTests.ReferenceDefaultIsPinnedMobileProtocolAndCatalog|FullyQualifiedName~AutoLensReferenceTests.UpstreamFrozenLinearBoundsProfilesMaterialsAndGradientsAgree|FullyQualifiedName~AutoLensReferenceTests.UpstreamFrozenAdamMomentsAndBothSchedulesAgreeIncludingNonFiniteSanitization' --logger 'trx;LogFileName=foundation-complete.trx' --results-directory validation/rechecks/2026-10-26/autonomous-topology-foundation --nologo
```

早期记录保留：`build-first.log` 为 TryResolve 参数签名编译失败；`tests-first.log` 为测试精度参数超出 xUnit 范围；`tests-second.log` 中两项失败来自独立参考误把 F/C 也设为主波长；`tests-third.log` 为当时 14 项通过；`tests-fourth.log` 中一项失败暴露首片最小间隙的浮点端点舍入。参考主波长已显式对齐；插入区间端点向可行内侧取可表示值，未放宽间隙门槛。`foundation-final.trx` 为当时 23 项通过；追加的 `fractional-origin.trx` 暴露非整数原点零步往返误报像面移动。现改为比较编译时的 Core 坐标，并保留未变化的原始绝对坐标，未放宽保护位置判断。`foundation-complete.trx` 覆盖全部 25 项的最终结果，不把早期通过数相加。

## 后续入口与限制

M2 要完成任意支持拓扑的真实导数、固定残差、无效/奇异路径、取消计费和结构无关终验；M3 再公开 Core 只读近轴矩阵并跑通实际 N→N+1/N−1 补偿。M4–M6 的自动搜索、检查点、扩展分支和界面仍未实现。没有运行全仓测试、长时间精修、外部 Python 比较、Zemax 新认证或原生界面验收。

此前实测的口径精确边界、极近临界折射与奇异导数差异仍保留，见[AutoLens 短程边界](../autolens-multiprescription/README.md)；本批没有声称这些差异已消除。原文提到的 ZIP/31 项自检源码未提供，未计作本批成果。
