# Delano 厚系统实体化后台验证

完整说明见[本批报告](../../../../docs/DELANO_THICK_MATERIALIZATION_2026-10-29.md)。此前失败证据保持在相邻 `bounded-initial-structure`，没有覆盖或改写。

- `protocol.json`、`runs/frozen-protocol.json`：原任务源SHA、共同600函数/100梯度/200万追迹/60秒总额，原流程参考、上一版有界、本次厚系统三臂各一次。两准备臂都是4×最多30、总120/15秒，上限1个新起点，全部准备/新增初验在总额内计费。
- `runs/`：每臂准备、原任务、执行计划、持久检查点、事件/工作、汇总及可打开STAROPT。本次4/4通过一阶/制造，所保留新起点RMS180.745961µm仍不合格；最佳仍为原合格三片49.292572µm。
- `regressions/legacy-materialization-diagnosis.json`：最终四提议重放，初始处方SHA逐个匹配。28次Core一阶请求、3364几何查询、0追迹；单片光焦度匹配，实际主平面间距及固定光阑边缘高度却失配。
- `regressions/thick-before-restoration.json`：新提议在恢复前已满足原f/F/像面，独立列出其构造请求、正式矩阵和几何查询。
- `regressions/development-thick-preparation.json`：开发定向准备结果，未用于观察最终像质后调参。
- `core-scoped.trx`：8个Core用例通过。模块最终35个不同用例通过：主批 `module-scoped.trx` 的失败由后续 `materialization-final.trx`、`principal-plane-diagnosis-final.trx` 定向复验解决，不累计重复用例。最初缺少测试命名空间、错误测试预约预期及诊断跨度失败日志保留。
- `audits/`：冻结三臂结束后的24次一阶只读请求、0追迹，不属于预算内优化。
- `verification.json`：源文件、原验收/采样/变量界、全流程总额与事件账目、有限停止、旧新准备仅实体化版本差异、43个不同最终通过用例及文件哈希核验。

所有工作后台CPU、2处理器、BelowNormal或离屏，无桌面操作、全套重跑、旧完整训练、原生GUI/GPU验收或发布。新起点初验额外花费425,852追迹，相应减少后续补偿额度；未追加预算或以某个片数为目标继续评估。
