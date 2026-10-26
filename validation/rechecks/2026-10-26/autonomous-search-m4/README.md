# AutonomousTopologyV1 M4：有限搜索、预算与持久恢复

2026-10-26。本轮按用户“继续”推进 M4，仍为纯 C# 库接口。未恢复用户停止的 AutoLens 完整课程/精修，未开始优化效果对照。原始处方、旧 UpstreamE58 和冻结外部基线不变。

## 交付与边界

- `AutonomousSearchService`：最多八个明确种子，默认 32 次有限决策、每片数四个活动候选和每父六个结构分支；未达标先连续优化，再尝试真实插片；达标后直接删除。有限随机流/游标、失败和拒绝全部保存。材料使用实际目录；渐进删除、拆并、材料替换和面型分支留待 M5。
- `SequentialTraceMeasurement.BeginCpuBudget`：CPU 提交前在父子/并行作用域原子预约，失败/瞄准/诊断均计数，缓存命中不重复消耗；批次不能预约则整批不派发。并行聚合的配额异常按不可用评价返回，不能误归为成功。CUDA 入口在 CPU 配额作用域内拒绝执行。新搜索显式要求 CPU，桌面 AutoLens 的自动优先 GPU 默认不变。
- 训练保留一个最终评价槽及指定终验光线额度，恢复差分/拒步、梯度、显式恢复矩阵、几何查询和真实尝试追迹分别计费。时间记录本次活动墙钟并排除关机/停放时间，采用协作取消，当前 Core 调用及保存清理可能略晚于时限结束；不宣称操作系统级强制终止。恢复内部矩阵计数不覆盖优化器内部所有近轴调用。
- 正常取消连续阶段保存同模板 Adam 坐标、矩、学习率、有效梯度/残差、变量/阶段指纹和累计工作；恢复不重算已有初始评价。结构/阶段变化重新绑定并清空状态。近轴恢复取消明确放弃该次尝试，保留其 q0、安全终点与实际消耗；不恢复信赖域内部状态。
- 独立 schema 1 检查点保存原规格、谱系、全部稳定 ID、阶段变量/绑定/残差指纹、随机流、池和归档、累计/在途工作；映射由同一编译器重建并由阶段身份核验。调用方须传入持久化保存委托，空委托只作内存运行。加载只读，显式 Run 才继续。64 MiB 有界原子写入、内容校验、独占写锁、revision 比较及归档追加检查阻止损坏、旧写入和达标归档覆盖。未知在途阶段整份预约只保守计费一次并放弃；已知未派发取消退还预约，保留待执行阶段。
- 合格归档和活动池独立，失败后代不会改写先前处方/验收；候选和终验仍由共享 Core 计算。AutoLens 特有损失统计继续留在实验室，没有把不同定义搬到 Core。

## 实际小预算结果

协议见 [search-m4.json](../../../../labs/InitialStructure/benchmarks/autonomous-topology-v1/search-m4.json)。只在内存把冻结三片输入改为轴上任务，仍固定像面 55 mm、三波长与原参考/光阑。训练 1×8 瞳、几何径向 8 点；终验仍为每方向 21 点、10×32 瞳、33 边界/中心检查及 64 径向点，轴上去重为一个物理视场。

| 记录 | 实际结果 |
| --- | --- |
| [自动循环](finite-search.json) | 四次决策：优化、两个零长度间隙的零工作拒绝、真实 3→4；恢复成功后完成局部/全目标和独立终验。50 函数、6 梯度、3 终验、41 显式恢复矩阵、36 差分、6660 次追迹；0 个达标归档 |
| [Adam 续跑](adam-resume.json) | 单独连续优化取消后坐标、一阶/二阶矩、接受步和函数总数与连续运行一致；变更阶段在追迹前拒绝 |
| [真实持久续跑](search-mid-phase-resume.json) | 自动搜索连续阶段第一个接受更新后取消、真实保存/加载再继续；终点与连续运行一致，均为 6 函数、4 梯度、2 终验、4440 次追迹，没有重复初始评价 |
| [归档保护](archive-resume.json) | 明确宽松目标下的种子独立验收通过；取消/加载后删片恢复耗尽预算，原达标归档完整保留；旧 revision、损坏校验及归档移除被拒绝 |
| [中断预约](interrupted-work.json) | 模拟在途进程丢失，整份预约仅计一次且归档保留；保存后但未派发的已知取消实际光学消耗为零 |
| [极小配额](hard-budget.json) | 20 次总/终验光线额度内停止，部分终验不能发布合格候选，显式拒绝非 CPU 搜索与变更原目标的恢复 |

自动循环故意设置 RMS 1e-9 mm 来检验未达标流程，归档用例另外放宽 RMS 至 5 mm 等门槛。两者均为流程夹具，不能宣称原始 50 µm 任务成功。保留实际删片失败；不据此判断设计规格无解。没有做输出效果 A/B 或长程达标率比较。

另保留真实文件副本：[归档终点](checkpoint-archive.json)、[连续阶段取消](checkpoint-mid-phase.json)、[未知在途预约](checkpoint-interrupted.json)。这些均为测试运行文件；加载不会执行计算，在途副本若显式恢复会按预约保守计费。副本内容与相关原始测试 JSON/校验包由机器汇总记录。

## 验证和失败记录

按最新对应执行去重，**12 个不同用例最终通过**：M4 八项，以及原有终验预留、取消工作量、M3 结构预算和两步 CPU/CUDA 四项相关回归。设备回归实际报告 225 CUDA 导数光线、9 批次，不是无设备直接返回；不构成 M4 GPU 硬配额认证。默认 Debug App、Core、模块和测试输出随测试构建更新，无新增警告/错误。未跑全仓测试。

- [m4-first.trx](m4-first.trx)：4 通过、3 失败，保留并行配额聚合异常、非法测试瞳坐标、有限决策尚未覆盖实际插片的错误。
- [m4-corrected.trx](m4-corrected.trx)：6 通过、2 失败；配额修复、持久化/归档/中断已通过，剩余两个夹具修正继续保留。
- [m4-loop-resume-final.trx](m4-loop-resume-final.trx)：只跑受影响两项、新增真实持久续跑及两个相关旧回归，5/5 通过。
- [m4-device-path-final.trx](m4-device-path-final.trx)：Core CUDA 配额守卫补齐后的两步设备路径与并行配额，2/2 通过。构建输出见 [device-build-test.log](device-build-test.log)。
- [m4-persistence-final.trx](m4-persistence-final.trx)：追加历史账目/处方不可回退守卫后，仅重跑真实阶段保存续跑与归档/旧写入/账目回退/损坏两项持久化用例，2/2 通过。
- [source-format-sandbox-failure.log](source-format-sandbox-failure.log)：格式工具的命名管道权限失败，未改业务代码；允许本机管道后仅格式化本轮 16 个文件。最终格式与文档检查结果见 [summary.json](summary.json)。

复现定向入口（只执行八项 M4 小预算用例）：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M4_EVIDENCE = 'D:\Projects\opticalsystem\validation\rechecks\2026-10-26\autonomous-search-m4'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~AutonomousSearchTests --logger 'trx;LogFileName=m4-rerun.trx' --results-directory validation/rechecks/2026-10-26/autonomous-search-m4
```

当前只完成 M4 库层，下一阶段 M5。源码、输入、当前二进制和证据指纹见机器汇总。本轮未提交推送、未发布安装包或改动已安装应用。
