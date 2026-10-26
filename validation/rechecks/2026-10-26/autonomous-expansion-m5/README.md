# AutonomousTopologyV1 M5：扩展结构操作与渐进删除

2026-10-26。按用户“继续”完成 M5 的显式 CPU 库层，纯 C#/.NET、共享 Core。未恢复旧 AutoLens 长训练，未进行优化效果对照。本轮只改变 InitialStructure 的模型/约束和编排；物理光学值仍由 Core 提供，AutoLens 专用损失统计留在实验室。

## 实现约定

- 新计划设置 `Options.Expansion = new()` 启用，空值保持 M4 行为。新增可空字段省略序列化，已有 schema 1 校验身份保持；三份实际 M4 检查点读取/验证通过且零追迹。加载不自动执行。
- 拆片：在原前/后顶点包络内建立两个同材料实体单片，保留原外侧面 ID/面型，新增两张内侧平面初猜及两个镜片 ID，真实空气隙隔开。默认初猜玻璃分配 0.55/0.45，空气隙至少满足原约束。原有其他元件不移动。
- 并片：仅将相邻、非保护且没有独立光阑夹入的两片替换为实际目录材料单片，保持外侧面及包络，移除内部面；不把这一步当成光学等效或胶合模型。
- 材料分支：真实目录/允许材料验证与全部波长色散由 Core 编译器处理。面型分支：显式引入偶次阶数或逐阶升级，新系数从零开始，圆锥/偶次参数有记录的边界与尺度。材料/面型/结构改变时重新绑定并建立新 Adam；旧绑定/状态在追迹前拒绝。
- 渐进删除仅在直接删除恢复失败后启动。持久化原父拓扑、原片、指纹、日程和索引，每次厚度/曲率/每个非球面系数为原值乘 s，中心固定，圆锥常数/阶数不缩放；不会把上一步乘积作为新原值。
- 目标片全部优化变量冻结，只对此片按 s 缩放中心/边厚下限。其他片、空气隙、斜率、口径、物理追迹和该次计划的原目标保持。过渡独立验收只允许 `transition-element` 违例时才推进；过渡结构不加入候选/达标归档。最后实体删除，清除例外，按原约束重新恢复、补偿和独立终验。
- 阶段内取消续跑沿用 M4 Adam/计费协议；恢复阶段取消则明确放弃该次恢复并保留原始基准/安全终点/消耗。未知在途整份预约只计一次并放弃，不发布过渡结构。最终验收的函数/光线额度仍保留，每父结构边界已修正为恰好配置的结构分支数。

源码入口为 `TopologyOperators.Expansion.cs`、`AutonomousSearchService.Expansion.cs` 和 `TopologyExpansionModels.cs`；M3 的单次插删 `TopologyRestorationWorkflow` 继续保留其原范围，M5 扩展由自动搜索服务编排。

## 实际结果和范围

[冻结协议](../../../../labs/InitialStructure/benchmarks/autonomous-topology-v1/expansion-m5.json)使用原三片形状，在内存显式改为轴上任务，保持参考/光阑和固定像面 55 mm、三波长；源文件未改。训练 1×8 瞳、8 径向几何点，恢复最多 300 函数；独立验收保留每方向 21 点、10×32 积分瞳、33 边界/中心和 64 径向点，轴上去重为一个物理视场。

| 记录 | 实际路径和消耗 |
| --- | --- |
| [拆片](automatic-split.json) | 3→4，恢复成功并进入局部/全目标与终验；26 函数、3 梯度、6510 次追迹，严格 RMS 夹具未达标 |
| [并片](automatic-merge.json) | 3→2 实体编译/追迹成立，59 次恢复评价后 `NoParaxialRecovery`；合计 60 函数、2120 次追迹，原归档保留；没有假称补偿成功 |
| [材料](automatic-material.json) | 真实 N-BK7→N-F2，恢复成功并进入终验；47 函数、3 梯度、6510 次追迹，严格 RMS 夹具未达标 |
| [面型](automatic-profile.json) | 显式偶次阶数/零新系数，恢复后进入终验；8 函数、3 梯度、6510 次追迹，严格 RMS 夹具未达标 |
| [渐进闭环](gradual-complete.json) | 原始 s=.7/.4/.2/.1/.02 后实体删除，全部六个阶段恢复成功，局部/全目标各有一次接受更新；234 函数、24 梯度、7 验收、203 显式恢复矩阵、168 差分、16040 次追迹。原约束下的两片终点在明确宽松测试目标下通过 |
| [原始缩放/例外隔离](original-relative-scales.json) | 包含非零 r4/r10/r18 的目标片各档均从原始系数缩放；只给该片例外，其他片变薄或标记过渡被拒绝；即使可追迹，过渡结构不能验收为合格 |
| [保存续跑与中断](gradual-resume.json) | s=.2 前取消、实际写文件再加载；终点和函数/追迹数与连续运行一致；原基准篡改拒绝，未知在途计费一次且原归档保留 |
| [父分支边界](parent-branch-limit.json) | 配置每父三分支，恰好三次零工作拒绝，没有从零起始游标多发第四次；只消耗种子独立终验一次 |

另保留[实际拆片](split-operator.json)、[实际并片](merge-operator.json)、[材料/面型身份](material-profile-identities.json)、[取消检查点](transition-checkpoint.json)和[在途检查点](transition-inflight-checkpoint.json)。检查点为实际测试文件，加载只读，显式 Run 才会继续；在途副本恢复会保守计费。

渐进测试故意把**直接删除恢复预算设为两次**以覆盖回退，之后每档恢复保持 300 次上限。光学验收目标显式放宽（例如 RMS 5 mm、焦距/F 数相对容差 1）；同一次流程各阶段原目标不变。此记录不能证明直接删除本来不可恢复、原始 50 µm 任务成功或渐进删除更优。拆片/材料/面型调度夹具另外采用 RMS 1e-9 mm，训练阶段为零更新评价，只验证流程与新变量能被实际评价；渐进闭环每个训练阶段有一次真实接受更新。并片失败不表示规格无解。

## 核验记录

- [首次编译](compile-initial.log)：集合表达式调用 `ImmutableArray.InsertRange` 重载歧义，改为明确 `ImmutableArray.Create`；没有光学工作或测试在该失败编译中执行。
- [首轮定向测试](m5-first.trx)：13/13 通过，包含 12 个 M5 用例与一项 M4 自动循环回归，实际测试约 6 秒；[完整构建/测试日志](m5-first.log)。
- [父分支上限](m5-branch-limit-final.trx)：收尾发现达标父结构从零起始游标会多发一个分支，修复后只补跑这一项，1/1 通过；[日志](m5-branch-limit-final.log)。
- 总计 **14 个不同用例通过：M5 13 项、相关 M4 1 项**，不累加重复执行。默认 Debug App、Core、模块和测试输出已更新，构建无新增警告/错误。只格式化本轮 10 个 C# 文件；格式、文档和空白核验及源码/输入/二进制/证据指纹见 [summary.json](summary.json)。

复现十三项 M5 用例：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M5_EVIDENCE = 'D:\Projects\opticalsystem\validation\rechecks\2026-10-26\autonomous-expansion-m5'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~TopologyExpansionTests --logger 'trx;LogFileName=m5-rerun.trx' --results-directory validation/rechecks/2026-10-26/autonomous-expansion-m5
```

当前是显式开启的 CPU 库接口；M6 界面待实施，原桌面 AutoLens 自动优先 GPU 默认保持。没有 M5 GPU 硬配额或外部数值认证，没有运行全仓测试、旧长训练或效果对照。未导入未提供的 ImplementationPack ZIP/外部渐进删除源码，未提交推送、发布安装包或改动已安装软件。工作区另有并行任务的 Core/分析修改，本轮不将其归因于 M5。
