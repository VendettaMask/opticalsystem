# 初始结构（产品内置模块）

更新：2026-10-26。通过 **实验室 → AI 初始结构** 打开，与镀膜设计同在主程序进程内运行，随标准安装包交付。产品保持纯 C#；相同的基础光学计算使用共享 Core，AutoLens 专用的绿色参考、detach 与 epsilon RMS 损失统计留在实验室。

后续镜片增删与自主搜索按[自主镜头拓扑设计实施计划](../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)推进。`AutonomousTopologyV1` 已完成 M0–M2：稳定 ID 模型、Core 编译/受限导入、参数绑定、结构插删，以及任意支持片数的固定拓扑 Adam、固定残差和独立 Core 验收。M0/M1 的 25 项见[基础层证据](../../validation/rechecks/2026-10-26/autonomous-topology-foundation/README.md)；本轮 Core 30 项、模块 41 项共 71 项定向通过，三片短程样例达标，两片/四片未达标，见[M2 完整记录](../../validation/rechecks/2026-10-26/autonomous-fixed-topology/README.md)。当前为库接口，近轴恢复、自动搜索、保存恢复和界面仍待 M3–M6；下述 `UpstreamE58` 行为保持独立。

新实验使用 `AutoLensManagedV1` 入口的显式 `UpstreamE58` 策略，算法后缀 `autolens-upstream-e58-v2`、优化状态 schema 2。默认预设来自已实际跑通的原版 MOBILE_CONFIG：5 mm、F/1.8、全视场 90°、六片、独立前置光阑、20000 mm 物距、r⁴–r¹⁸、种子 42。另保留 README 的五项本地目标适配。载入不自动运行，也不代表成功处方。[逐项对齐及保留差异](../../docs/AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.md)、[预设来源](../../docs/AUTOLENS_OFFICIAL_EXAMPLES.md)、[当前状态](../../docs/CURRENT_STATUS.md)分别说明范围。

系统页管理目标与 RGB 波长；目标页管理初始化总长/后焦、允许玻璃及独立验收门槛；搜索页管理种子数、资源、面型、材料优化和完整阶段计划。旧二元形式、QUICK 筛选、起始半径和移动光阑搜索控制已移除。平板祖先仅为旧存储格式的来源证明，实际随机起点另存 `TrainingStartOptic`。

阶段交接的原版 JSON 舍入约定在实验室执行，首面归零后由舍入面间距累计重建所有面和像面；见[交接修正](../../docs/AUTOLENS_HANDOFF_REPAIR_2026-10-25.md)。流程按原版随机初始化、半到全口径课程、固定输入光线求导、detach 绿色参考/权重、线性约束、直接 Adam、目录投影及固定玻璃精修推进。默认执行 2001 + 3001 次更新。预算不足保存坐标、矩、缓存与完整阶段进度，不压缩课程、不做下降线搜索、不接原生 target polish，也不用旧 incumbent 替换训练终点。默认使用原版 61 项 CDGM 表及对应真实色散，显式允许玻璃限制仍有效。

完成终点才进入产品原有的独立物理验收：21 视场、10×32 面积积分瞳、独立边界/中心检查，以及焦距、F 数、制造、光斑与通光硬门槛。训练掩码和连续材料不能代替验收，未完成训练不发布临时投影副本。

设备选择为自动优先 CUDA、GPU、CPU；均使用共享 Core 双精度。有限物距的方向瞄准使用 Core CPU，单独计为 `CpuAimingRays`；固定输入四输出方向导数采用 `cuda-fixed-input-forward-v1`，按光线和参数在 CUDA 成批计算，沿正式 Core 交点与折射方向传播。历史隐式瞄准/相对厚度保留六输出记录反传并复用内存。正式传播、完成的有效光线导数、批次和回退原因分别计数。[GPU 边界](../../docs/AUTOLENS_GPU.md)说明 2026-10-26 相同处方、完整默认单步规模的提速及数值检查；这不是完整训练时间或最终像质比较。训练随机流未逐位复现 CUDA Philox，Core double 与原版 float32 仍不同，另一套 CAMERA_CONFIG 拓扑尚未完整复现。

运行记录位于本地应用数据 `OpticalSystemDesign/Labs/InitialStructure/flat-start/runs`，采用带校验的 `.family.json` 文件。剩余预算和配额允许时恢复同一状态；追加细化使用独立预算。文件按批次原子保存，恢复工作先预约配额，进程丢失的未知工作保持计费；未派发取消为已知零工作。旧 v1、原生及混合记录保持原身份，可查看/导出，不能通过当前桌面恢复或细化。导出候选经 Core STAROPT 回读验证，不修改正式工作台。

```powershell
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -m:1
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj --filter FullyQualifiedName~AutoLensReferenceTests -m:1
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release
```

构建和测试不需要原版 Python 环境；冻结规则夹具只作测试输入。参考训练要求三个有效 RGB 波长、绿色为主波长，所有采样覆盖都保存为规格身份。资源上限为 100000 评价、24 小时、128 起点、4 并行任务及 64 MiB 搜索文件。[模块计算边界](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md)与[授权声明](../../third_party/autolens/AutoLens-NOTICE.txt)保留；“AI”不代表预训练网络或自主设计智能体。本轮未发布安装包。
