# 自主拓扑短程基准

2026-10-26 冻结，独立身份 `AutonomousTopologyV1` schema 1。用于 M0/M1 编译、导入与参数一致性核对，后续按[实施计划](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)扩展，不能修改旧 UpstreamE58 协议。

- [两片球面输入](foundation-2.json)：N-BK7/N-F2，固定机械长 55 mm，独立前置光阑半径 2 mm。
- [三片非球面输入](foundation-3.json)：增加 N-BK7，首面含非零 r4、r10、r18，r2 为零。
- [明确评价设置](protocol.json)：无限远物方，0–5°，三波长，主波长 587.6 nm，Gaussian 4×16 面积积分瞳，多色共同质心，关闭表面透过率；对照位置为归一化视场 0、0.5、1。

两面有效半口径分别为 8 和 7.5 mm，前面机械半口径 9 mm，后面未指定机械覆盖值。JSON 的枚举数值由 Contracts 定义。这些是故意保留像差/离焦的普通小型处方，不是合格设计或优化成功案例，不包含独立 Cooke/Tessar 调查。

M2 在其上单独冻结 [20 步固定拓扑协议](fixed-topology-m2.json)，不覆盖原 M0 输入。两/三/四片样例显式解除像面保护，采用同一目标与独立密集验收；固定像面另有回归。三片达标、两片/四片未达标及所有实际结果见 [M2 证据](../../../../validation/rechecks/2026-10-26/autonomous-fixed-topology/README.md)。四片只是预先插片后的输入，尚不构成 M3 增删恢复闭环。

[定向测试](../../tests/OpticalSystemDesign.InitialStructure.Tests/AutonomousTopologyFoundationTests.cs)另外独立组装硬编码面表，比较共享 Core 焦距、瞳径、每条有效光线落点和多色光斑，并覆盖增删与真实裁光。输入及测试工厂本轮已逐项核对；实际 Core 快照、材料数据、二进制指纹、日志与限制见[本轮证据](../../../../validation/rechecks/2026-10-26/autonomous-topology-foundation/README.md)。

M3 使用独立的[增删恢复协议](restoration-m3.json)，保持原三片输入的固定像面，实际执行 3→4 与 3→2 恢复、局部/全目标补偿和密集终验；两个终点均未完整达标。见[M3 结果与工作量](../../../../validation/rechecks/2026-10-26/autonomous-restoration-m3/README.md)。原 M0/M2 输入及历史结果保持不变。

M4 的[有限自动搜索协议](search-m4.json)只验证 CPU 调度、配额、实际插片恢复、持久化与归档保护。自动循环显式改为轴上、极严 RMS，归档用例另设宽松门槛；不改原始输入文件，不报告原任务达标。实际循环、取消/加载续跑、旧写入/损坏拒绝和失败历史见[M4 证据](../../../../validation/rechecks/2026-10-26/autonomous-search-m4/README.md)。

M5 的[扩展协议](expansion-m5.json)验证实际拆片/并片、目录材料/面型分支、五档原始快照缩放和实体删除后的原约束终验，过渡检查点可保存/续跑但不能进入达标归档。直接删除恢复预算显式缩为两次以覆盖渐进回退，归档目标明确放宽；不能推断直接删除不可恢复、原任务已成功或渐进方法更优。并片样例真实恢复失败仍完整保留，见[M5 证据](../../../../validation/rechecks/2026-10-26/autonomous-expansion-m5/README.md)。
