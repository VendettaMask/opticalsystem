# Petzval 相对照度与 RMS 随视场修复

日期：2026-10-20。按用户要求定位并修复 `Petzval.zmx` 的两项残差，沿用原处方、分析请求、参考和容差；Cooke 40° 与 Double Gauss 28° 参加同设置退化检查。

**两项差异现均为 Pass。** 相对照度最大绝对差从 **0.0378923834** 降至 **3.81762e-11**；RMS 随视场最大绝对差从 **0.2525150606 µm** 降至 **2.59349e-7 µm**。最新数据见[修复指标](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/fixed-metrics.json)。

## 根因与独立证据

两项差异来自同一处共享曲面求交缺陷。Petzval 第 8 面半径约 **1111723.05165 mm**，接近平面。原二次方程的直接 `(-b ± sqrt(D))/(2a)` 在求较小根时相减两个接近的大数，丢失有效位。现有曲面残差检查正确拒绝了这个不准确的候选，但求交器随即把实际存在的交点当成追迹失败。

独立原生 GQ6 审计覆盖原 RMS 曲线的 **16 个视场、1152 条光线**。Zemax 每条都可到达像面、没有错误或遮拦；修复前软件的标量高斯积分路径有 **28 条**在第 8 面错误终止，而普通批量路径仍可通过。RMS 因少了这些光线而改变统计权重；照度的诊断追迹同样会丢掉有效节点，扰动有限瞳映射和积分，继而影响既有峰值归一化。

归一化视场 0.2 的一个实际失败例子：

| 求根方式 | 距离 / mm | 曲面残差 / mm | 原残差限值 / mm | 结果 |
|---|---:|---:|---:|---|
| 原直接公式 | 2.0015766569413245 | -1.79489e-10 | 1.30014e-10 | 错误丢弃有效光线 |
| 稳定乘积公式 | 2.001576657120898 | -3.57151e-16 | 1.30014e-10 | 正确通过 |

完整输入、系数和限值保存在[相消实例](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/root-cancellation-example.json)。修复后的 1152 条光线全部恢复；与本轮新采原生参考的 **10368 个逐面记录**比较，最大坐标差 **2.10258e-10 mm**、最大方向差 **1.55431e-15**。见[逐场保留数](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/ray-retention-v3.json)和[原生光线误差](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/gaussian-native-v3.json)。

## 最终实现与被否决的候选

最终共享求交器先按原公式计算两个根，并执行原显式矢高和曲面残差校验；已有合法交点时逐位保留原结果。只有两个普通根都未通过原校验，才尝试稳定形式：`q = -0.5 * (b + CopySign(sqrt(D), b))`，两根为 `q/a`、`c/q`，再执行同一校验。线性分支、零根、法向和残差限值保持。该实现不再以估计舍入误差为由替换已合法的交点。

修复统一用于 [StandardGeometry](../src/OpticalSystemDesign.Core/Geometries/GeometryModels.cs)、[标量批量后备](../src/OpticalSystemDesign.Core/Backend/IBatchedNumericBackend.cs)和 [SIMD 批量路径](../src/OpticalSystemDesign.Core/Backend/ManagedCpuBackend.BatchedIntersections.cs)，判据与稳定根由[共享求交支持](../src/OpticalSystemDesign.Core/Backend/StandardIntersectionValidation.cs)定义。没有按镜头名、文件哈希、视场或原生结果调整算法；产品也不读取验证捕获。

首版候选对所有二次方程无条件替换求根表达式，虽然 Petzval 两项及三镜头矩阵通过，却使部分旧高采样衍射能量守卫退化：普通光线末位舍入发生变化，既有离散 Fourier 瞳边界对这些末位敏感。该候选被否决，保留源文件、结果和已观察到的失败；剩余首版全量测试由本任务主动中止，不能把它当成完整通过或产品崩溃。[首版记录](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/candidate-v1-rejected.json)保留阶段边界。

第二版按舍入误差选择稳定根（Core `71562147…`），通过 Petzval 和相关能量守卫后，协同聊天的内置初始结构常规检查仍发现 2/308 失败：`trial-0078` 的最大 RMS 为 **0.4851952254 mm**，超过原 **0.45 mm**；`TinyInitialRoundoff(-1e-12)` 为 **0.0400613269 mm**，未达到原 **0.03 mm** 目标。因此第二版也被否决，不能把此前局部通过当作整体验收。问题是该判据仍可能改变已通过原校验的根，扰动优化轨迹。[第二版失败记录](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/candidate-v2-rejected.json)及本地完整 TRX 保留。

最终第三版只在求交失败后使用稳定根，Core 为 `cf3647652213542dae90d0ed1c16b9df5290e740a0b204f1b4a6e0a9f40a6b2c`。没有修改能量算法、优化器、原有目标、旧参考或旧断言来获得通过；两种初始结构失败方法及相邻数据现 **4/4** 通过，使用默认必装模块 Engine 的固定程序集副本。

## 原设置重新对比

| Petzval 分析 | 修复前 | 修复后最大绝对差 | 修复后 NRMSE | 原 Pass NRMSE 限值 | 结果 |
|---|---|---:|---:|---:|---|
| 相对照度 | Difference | 3.81762e-11 | 1.41772e-11 | 0.003 | Pass |
| RMS 随视场 | Close | 2.59349e-7 µm | 5.95824e-9 | 0.003 | Pass |

相对照度仍是波长 2、密度 20、21 个视场点、去渐晕因子、无偏振、原有限照度积分；RMS 仍是波长 2、GQ6/12 方位点、16 个扫描位置、原参考定义。原比较设置 SHA-256 保持为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`，逐点、NRMSE、Close 和覆盖率阈值都未放宽。

| 原 90 项请求 | Pass | Close | Difference | 不可比 | Error | 原通过项退化 |
|---|---:|---:|---:|---:|---:|---:|
| 修复前 | 86 | 1 | 1 | 2 | 0 | — |
| 最终重算 | 88 | 0 | 0 | 2 | 0 | 0 |

Cooke 30/30 通过；Double Gauss 29 项通过、1 项原生低采样不可比；Petzval 29 项通过、1 项原生低采样不可比。原 **86/86 Pass** 保持，9 组原定义视场/波长逐面光线仍全部通过。两条低采样能量记录保持不可比，既有独立 128 档结果见[修复前报告](THREE_OBJECTIVES_COMPARISON_2026-10-20.md#两项衍射包围能量补充控制)，不将其重计为本轮原设置通过。最终[分析矩阵](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/analysis-matrix.csv)、[逐面结果](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/ray-results.json)及[通过保持核验](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/pass-preservation.json)均保留。

最终 90 份软件输出提取自授权协同聊天已完成的同一 Core 目录重算，逐项核对 **90/90** 请求、设置和容差一致。由于目录报告采用另一批原生捕获，本专项随后只执行比较步骤：原三镜头的 90 份原生数据逐字复制，与上述固定软件输出重新比较，未再调用光学计算。复制的 1530 份原生/软件文件逐一核对哈希，90 份原分析数据保持原字节；9 组原生光线请求和结果也分别一致。身份和逐项指纹见[请求对齐](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/request-alignment.json)、[光线对齐](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/ray-request-alignment.json)及[最终汇总](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/summary.json)。本表使用原三镜头捕获作为参考。

## 采集、构建与回归边界

本任务初期用独立 OpticStudio **2026 R1 build 260127 Enterprise** 会话新采 102 项原生任务，全部成功。第二版复用这些任务内捕获时逐请求检查源文件 SHA-256 和全部非输入路径参数，并逐文件核对复制哈希；102 项完全匹配、待补采为零，原[复用记录](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v2/native-reuse.json)不改写。最终第三版专项直接使用任务开始时保留的两项原始分析捕获，原件、请求和 12 份资产的[指纹清单](../validation/repairs/2026-10-20/petzval-illumination-rms/manifest.json)保持。1152 条光线审计使用本任务独立采集的原生数据；所有捕获均未因修复重写。最终默认构建、固定副本及内置 Engine 的身份见[记录](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/identity.json)，三份 Documents 原件完成后再次核对。

新增 14 项主测试覆盖正负大半径、非球面常数、非单位方向、标量/SIMD/尾部、重复零根，以及 Petzval 全扫描瞳的有效光线。第三版把这批新守卫明确为两类契约：原校验已接受的根逐位保持；原校验拒绝的根恢复后，仍以 **2e-12** 与已知解析交点核对。保留首版 12 项更严格距离断言的原失败记录，不将其冒充为第三版新断言的修改前结果；既有能量与初始结构断言未改。

最终 Release 的求交及五类已观察能量退化集合 **146/146**，初始结构指定方法 **4/4**，Petzval 原生守卫 **2/2**，禁用硬件向量指令的后备集合 **17/17** 通过；最终 Debug 的求交 **14/14**、Petzval **2/2** 通过。集合交叠，不相加。旧第二版的完整比较工具 244 项通过只保留其阶段身份，第三版不例行重跑整组。默认 Debug/Release 根构建各 19 项目、零警告/错误，默认主程序输出已更新。

用户要求后续不要每次跑全测试，已停止当时运行的第二版主 Release 全量检查。停止前日志观察到 4546 条通过、零失败记录，但没有完整结束，明确记为**未完成**，不声明全主集合通过。第三版只跑改动路径与已观察退化的定向回归，并与授权协同聊天共用相同 Core 的目录复验，避免重复计算。格式、严格文档检查、各集合及中止边界统一保存在[最终验证账目](validation/PETZVAL_ILLUMINATION_RMS_REPAIR_2026-10-20.json)；完成时指纹核对见[完整性记录](../validation/repairs/2026-10-20/petzval-illumination-rms/final-v3/final-integrity.json)。

大体积原生光线、两个候选的计算输出及测试日志保存在本地 `artifacts/petzval-repair/2026-10-20`，不假称随 Git 分发。仓库保留关键原始分析捕获、请求、数据指纹及最终汇总。未重新打包安装器、运行跨平台或初始结构长调查；认证范围限于本报告的原设置和现有回归。
