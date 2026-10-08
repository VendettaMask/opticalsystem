# 波前图修复验收报告 2026年10月8日

本页保留波前图修复阶段的历史验收，不是最新测试基线。当前负向渐晕、会话竞态、有限物距契约及完整重算结果见[确认缺陷修复与验收](CONFIRMED_ISSUE_REPAIR_2026-10-08.md)；此前 4492/2/4494 属于[计算路径阶段](CALCULATION_PATH_REPAIR_2026-10-08.md)。整体发布门禁仍未关闭，以下历史失败和计数不覆写。

**本次波前图物理瞄准修复通过专项验收，整体发布验收不通过。** 正式产品完整 Release 为 4430 通过、4 失败、共 4434 项；比较工具完整 Release 为 178 通过、2 失败、共 180 项，均零跳过。正式测试比此前新增观察到两个界面失败身份，不能宣称全产品没有新增失败。

本次只验收，不修改产品代码、测试、冻结参考或容差。固定检查的 17 个源码、测试、默认二进制和容差文件前后指纹均未变化。结论适用于当前工作区及所列测试和捕获设置，不是全部镜头、分析、发布包或实验室的商业精度认证。

## 验收结果

| 检查范围 | 本次结果 | 判定 |
| --- | --- | --- |
| 默认正式解决方案 Debug 与 Release 构建 | 两配置均零警告、零错误，默认桌面输出已更新 | 通过 |
| 正式完整 Release | 4430 通过，4 失败，共 4434 项，零跳过 | 不通过 |
| 比较工具完整 Release | 178 通过，2 失败，共 180 项，零跳过 | 不通过 |
| 新增波前瞄准回归 | 25 项在完整 Release 和本轮 Debug 定向中全部通过 | 通过 |
| 上轮 743 项正式相关回归 | 本次完整 Release 中全部保留并通过 | 通过，不另加到全量总数 |
| 本轮 Debug 修复与相邻回归 | 49 通过，零失败、零跳过，包含新增 25 项 | 通过 |
| 六组原生光扇与实际波前图精确共节点 | 36 条共有节点记录与上轮修复后结果一致，参考哈希未变 | 通过此局部数值范围 |
| 主 Zemax 基准完整性 | 源哈希、清单、数据引用和图像校验通过 | 通过完整性，不计数值通过 |
| 新原生 Wavefront Map 对照 | 缺少 ZOS-API，原生执行 0，数值比较 0 | 未完成 |

正式测试与此前 4409 项逐个核对：旧用例没有删除，新增恰为 25 项波前瞄准测试。比较工具 180 项测试身份全部保持不变。没有靠减少用例、跳过失败或提高容差获得通过。

## 完整运行中的失败

| 失败身份 | 本次观察 | 与上一完整 Release 的关系 |
| --- | --- | --- |
| `FieldDefinitionParityTests.FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays` | finite_angle 期望 0.058589983242791625，实际 0.060191701479492175 | 既有历史辅助参考差异，数值相同 |
| `PanelContentLayoutTests.DrawingReadsMaterialValuesAndCombinesThemWithEditedTolerances`，Iso10110，cemented=true | `SafeHeadlessUnitTestSession.Dispose` 空引用异常 | 既有界面关闭失败再次复现 |
| `FieldCurvatureFooterTests.CombinedFooterShowsTwoCompactSummariesFromTheActualLensResult` | 同一会话关闭位置的空引用异常 | 本次新增观察到的失败身份 |
| `AccessibilityAndResponsiveLayoutTests.ReadOnlyAnalysisChartsExposeNamedValuePeersWithoutFakeCommands` | 同一会话关闭位置的空引用异常 | 本次新增观察到的失败身份 |
| 工具 Huygens PSF Cross Section | NRMSE 0.0037256670590166724，高于记录上界 0.0037256669899999998 | 既有失败，数值相同 |
| 工具 Diffraction Encircled Energy，curve:1 | NRMSE 0.011467280942848471，高于记录上界 0.01146706362185577 | 既有失败，数值相同 |

三项界面异常的现有栈均定位到 [测试会话包装器](../tests/OptilandWorkbench.Tests/WavefrontSurfaceRenderTests.cs) 的第 137 行 `_inner.Dispose()`，未显示光学计算或界面业务断言失败。退出异常可能遮蔽此前异常，因此不能只靠这一栈证明全量中的测试体一定成功。现有异常过滤仅允许确定来自 Avalonia 内部关闭的异常；本次没有扩大过滤范围。

随后在两个新的 Release 测试进程分别隔离运行图纸 6 项、页脚 1 项、图表无障碍 1 项与 RMS 设置 5 项，两次均 **13/13** 通过。这是同一组 13 个身份的重复，不是新增 26 项。结果提供了全量会话状态或退出时序相关的线索，但根因仍待确认；隔离通过没有覆盖或消除完整运行中的 3 项界面失败。

## 专项数值验收

系统瞄准与出瞳显示开关已分离。均匀、六角采样的实际波前均遵循系统设置；系统开/关与显示开/关组合通过。正式应用入口在 Cooke 源镜头系统瞄准关闭、出瞳形状开启时也报告 `UseRayAiming=false`，不是只在直接 Core 测试中生效。

六组冻结原生 OPD 光扇对应三份镜头，每组实际产品计算 3001 个有效二维样本，但这里只比较 6 条精确共节点记录，即 5 个独立坐标，中心在两方向各一次。没有插值，也没有按原生值拟合活塞、比例或倾斜。

| 捕获设置 | 实际波前图共节点最大误差 waves |
| --- | ---: |
| Cooke 轴上 550 nm | 2.260835963e-11 |
| Cooke 20° 480 nm | 1.983404922e-5 |
| Double Gauss 轴上 587.6 nm | 1.632464719e-10 |
| Double Gauss 14° 486.1 nm | 4.031837608e-5 |
| Relay 轴上 587.5618 nm | 3.144181582e-10 |
| Relay 2° 486.1327 nm | 6.317280780e-7 |

这六组数值与上轮修复后记录完全一致；11 个清单、快照、原生光扇与 Tessar 源文件哈希无变化。实际波前图与遵循系统设置的正式波前引擎最大差约 1.72e-15 waves。旧 `123456.ZMX` 完整冻结 Wavefront Map 测试在完整 Release 和本次 Debug 定向中也通过，沿用原容差。六组共节点检查不是六幅完整原生二维图的重新认证。

主 `123456.ZMX` 基准检查程序确认清单含 165 个条目，148 个已捕获、17 个不适用或失败、零超时；148 张图像中 106 张为原生窗口截图，42 张为标记的替代重绘。1300 个文件共 216794423 字节，源 SHA-256 为 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。这是已有参考的完整性校验，不是本轮 148 个数值比较或新捕获。

正式工具新尝试记录 `ZOS-API not found`：Workbench 执行 1，Zemax 执行 0，数值比较 0。它是环境限制，不是新增数值 Pass；本机许可状态未检测。原六文件 132 项矩阵未重跑，其旧分类保持历史范围。

## 未覆盖路径和整改优先级

验收后的只读诊断已经进一步复现通用 Zernike、扫场、有焦和无焦参考球的瞄准遗漏、FFT 显示尺寸改变计算间距，以及 Huygens 离轴 PSF 对整体坐标原点的非物理依赖。产品计算没有在该诊断轮修复，完整验收计数保持本报告范围；具体数值和后续修复顺序见[其他计算路径诊断](CALCULATION_PATH_DIAGNOSIS_2026-10-08.md)。

1. 先定位全量界面会话退出问题，保留完整异常来源和全量复现，修复后重新跑完整 Release；不能扩大空引用过滤或用隔离通过代替全量通过。
2. 核实有限物距视场的既有历史辅助参考差异，以正式物理定义和 Zemax 为主验证，不重新生成 Optiland 历史参考或任意放宽容差。
3. 拆开 FFT PSF 单元中心节点与瞄准设置，检查零离焦辅助波前固定瞄准，再处理 Huygens 截面、衍射包围能量残差；保留每项原生设置和容差。
4. 调用审查发现通用六角 Zernike 分支仍在 `WavefrontAnalyses.cs` 第 470 行调用默认不瞄准的入口，均匀分支则传系统设置；`ReferenceSphereWavefrontAnalysis.cs` 第 130 行的无焦平面回退也保留该默认调用。它们是未改动的旁路，需要分别验证系统开启瞄准时的一致性，不能把本次 Wavefront/Interferogram 专项通过扩展为所有波前分析通过。
5. 出瞳 X/Y F 数形状投影仍未实现；负 Y 与混合符号 Y 渐晕、RA 波前精确节点和高密度收敛仍需原生认证。当前显示形状参数只是兼容设置，不能算完整功能验收。

完整 Debug、实验室全量、安装包与交互式桌面烟测不在本次完成范围；自动化界面测试不能替代人工交互验收。需要可用的 OpticStudio API 环境才能补齐上述新原生对照。当前只能接受已声明范围的波前图瞄准修复，不能关闭整体发布门禁。

## 验收记录

完整证据保存在 [本轮验收目录](../artifacts/validation/wavefront-acceptance-20261008/run-01)。正式与比较工具完整记录分别为 [formal-full-release.trx](../artifacts/validation/wavefront-acceptance-20261008/run-01/formal-full-release.trx) 和 [comparison-full-release.trx](../artifacts/validation/wavefront-acceptance-20261008/run-01/comparison-full-release.trx)；统计及失败身份保存在同目录 `formal-full-summary.json`、`tool-full-summary.json`。

Debug 定向、独立数值复查、基准完整性和前后指纹分别保存在 `repair-debug.trx`、`pupil-recheck/summary.json`、`baseline-integrity.json`、`start-fingerprints.json` 与 `end-fingerprints.json`。两次界面隔离记录为 `ui-isolation-release-01.trx`、`ui-isolation-release-02.trx`，原生环境失败保存在 `native-attempt/run-summary.json`。实现阶段及原始前后误差见[修复文档](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)。
