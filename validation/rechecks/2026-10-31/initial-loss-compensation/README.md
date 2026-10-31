# 固定口径失光与减片补偿：后台对照

本轮效果验收未通过。候选补偿策略仍未取得新增原规格合格设计，不能启用为默认修复；原口径、焦距/F 数、像质、通光和制造验收门槛均保持。工程回归通过不代表三个光学任务已经解决。

## 固定的实验定义

原八片 F/8 和 F/4 全视场 25°、原三片 21°半视场，种子 42–46。`control` 为本次重跑的原 `compete` 策略，实际使用原通光 v4（局部最多两变量），不能称为纯 Adam。`allocation-control` 保留该策略，只提高每阶段额度；`candidate` 在同样阶段额度内启用 `GateCompensationVersion=1`、编辑去重、原视场/光瞳竞争和新的全变量 Core AD/QR 补偿。

阶段额度为最多 20 次更新、300,000 实际光线、10 秒。三组统一总上限 600 函数、200 梯度、2,000,000 实际光线、110 秒，原起点获取费用已经从搜索余额扣除。原参数界、结构/材料权限及独立终验协议保持。阶段分配和算法同时改变时，不把组间差异归因于算法本身；实际运行时间也不是隔离的性能基准。

详见 [experiment-protocol.json](experiment-protocol.json)。正式结果另由汇总和独立回读程序生成；开发试验不并入正式计数。

## 正式结果

45 次正式运行全部完成；三组各 15 次，新增原规格合格设计均为 0。每组的原 21°三片父处方均保留 5 次，其余两个任务没有原合格父方案，不能把继承数称作新增成功。

| 组别 | 原 21°最佳二片 RMS / 最大半径 | 每组总实际光线（含起点） | 单任务最大函数 / 梯度 / 光线 |
| --- | ---: | ---: | ---: |
| 原控制 | 67.867662 / 199.461913 µm | 8,406,817 | 182 / 55 / 1,375,036 |
| 只增阶段额度 | 68.897509 / 206.324808 µm | 9,750,049 | 299 / 172 / 1,633,416 |
| 新补偿候选 | 94.472560 / 264.855938 µm | 12,403,437 | 307 / 200 / 1,963,568 |

二片数值均来自完整原规格终验，通光和边界检查通过，仍超过原 50/150 µm 门槛。新补偿既未新增合格解，计算量也更大；不能宣称优于原策略。F/8 仍有失光，缺失视场的全局像质不报为有效数值。F/4 新策略的费用只剩继承的起点获取费用，预检查本身没有追迹，不能混称冷启动零成本。

逐任务和逐种子见 [comparison.json](comparison.json) 及 [comparison-table.md](comparison-table.md)。原始处方、失败和全量费用均保留。

最终独立回读核对 45 份运行记录，116 份原规格验收结果全部冷重放一致（包含原失败结果），不是 116 个光学合格设计。另完成 73 份失光诊断。冷重放实际追迹 20,910,916 条、诊断 144,082 条，全部作为搜索外验证费用单列。见 [formal-verification.json](formal-verification.json)、[independent-original-replays.json](independent-original-replays.json) 和 [posthoc-loss-diagnostics.json](posthoc-loss-diagnostics.json)。例如种子 44 的 F/8 原处方为 147/225 条诊断光线到像面，新候选为 150/225；这点局部变化不能替代原全视场验收或证明失光已解决。

## 已确认的问题

- **F/4 的规格冲突**：前置光阑半径 6.25 mm，所有镜片净半径上限 5 mm。无限远轴上边缘光线在第一折射面即超口径；在固定光阑、固定口径、无前置折射元件的 M2 权限内无法通过原独立边界检查。显式新策略零光线终止并记录支持域，不把终止称为成功。包含判断调用 Core，保留原数值边界容差。
- **F/8 仍失光**：部分候选仍有完全失光视场。训练亏量下降不能替代原通光验收；新策略还可能让像差明显变大。
- **21°减片仍超差**：原三片合格父处方保留，完整通光的二片候选仍超过 50 µm RMS/150 µm 最大半径等原门槛。继承父处方不计新增，也不计减片成功。

原始 `*.summary.json` 中 nullable `Max` 只表示有数值视场的最大值，若其他视场完全失光，不能据此报告“全视场 RMS 达标”。正式汇总要求所有视场 RMS/最大半径均存在才给出全局值；`formal-finalists.json` 的完整性标记、通光和违规名单是判断依据。

## 来源与复算

- [baseline-source.zip](baseline-source.zip)、[baseline-source-sha256.json](baseline-source-sha256.json)：冻结原始 Core/模块/控制脚本。
- [candidate-source.zip](candidate-source.zip)、[candidate-source-sha256.json](candidate-source-sha256.json)：冻结正式候选源代码及搜索运行库哈希。基线、额外控制和候选共用 Core `083b8e4cae25ad6ed84f42015478d43d166d8a7bd3b8eab579f72537e4e4ffcb`。
- `control/`、`allocation-control/`、`candidate/`：完整输入、原输入 SHA-256、处方、逐分支恢复/补偿/竞争、原验收和实际费用。
- `development-v1/` 至 `development-v7/` 及对应运行库：探索失败，不能跨版本套用模型/检查点。v1 的策略混用配置拒绝也保留，没有计作物理实验成功。部分开发版本只保留运行库，不能声称每版都有源码归档。
- `verification-final-runtime/`、[verification-final-source.cs](verification-final-source.cs)、[verification-final-sha256.json](verification-final-sha256.json)：最终独立回读/复算程序。相对候选源码归档中的验证器，仅增加后台低优先级和禁止嵌套并行；搜索产物未变化。

独立回读检查保存哈希、相同原任务/权限、固定平面和逐面口径、所有费用结算、总预算、合格归档及 Pareto。完整终验另外冷重放；失光诊断与重放费用单列，既不向搜索提供候选，也不混入预算内成果。光学计算始终为冻结的共享 C# Core。

复算可从保留的 `control/seed42/*.input.json` 开始，使用候选运行库执行 `legacy`、`legacy-budget`、`gates-budget`，输出须使用新目录。`legacy` 重现原阶段分配；带 `-budget` 使用相同的提高后阶段额度。不得覆盖已冻结结果。

## 工作区回归验证

[最终测试](tests/initial-loss-final.trx)：46 通过，0 失败，0 跳过；[构建记录](build-verification.txt)：默认 Release App 构建零警告、零错误。覆盖真实 Core 补偿/续算和独立终验、禁用操作/重复减片计费、边界数值容差、原策略兼容与原验收协议。第一次沙箱内测试连接超时中止，后续使用已授权的本地后台测试连接完成。

工作区有其他并行修改，当前默认构建 Core 为 `59cd45ac5642f17e6fa735d3dc6551d80ab51cacf4ed6dbf7613a2130015ebdd`；它与正式比较的冻结 Core 分开记录，见 [default-build-sha256.json](default-build-sha256.json)。46 项回归对应工作区产物，不冒充冻结实验产物的同构建重复测试。测试源码与结果哈希见 [test-source-sha256.json](test-source-sha256.json)。

没有操作用户桌面、调用 DeepSeek、提交/推送代码或发布程序。新策略保留为底层显式研究选项；本轮不能关闭失光/减片像差效果问题，也不能把 M3 自动扩径提前计为修复。
