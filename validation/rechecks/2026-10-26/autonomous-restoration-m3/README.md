# AutonomousTopologyV1：M3 真实增删恢复闭环

日期：2026-10-26。按[实施计划](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)交付 M3。当前是内置模块库接口，不包含 M4 搜索调度或 M6 界面；此前停止的 UpstreamE58 完整训练、精修及自动跟进均未恢复。

## 接口和计算定义

- Core `Paraxial.GetTransferMatrix(inputSurfaceIndex, outputSurfaceIndex, wavelengthNanometers)` 直接复用既有 `TraceMatrix`。只读返回 A/B/C/D、端点折射率、Core 参考坐标、面索引及波长。坐标为 `[height_mm, slope_dy_dz]`，B 的单位为 mm，C 为 1/mm；后续操作左乘。包含输入/输出面的折射和全部中间传播，包含末片至像面的传播，不包含像面之后的占位厚度。接口限同轴、向前、均匀介质的普通透射球面/平面/偶次非球面，其他路径明确拒绝；旧近轴接口保持原规则。
- `ParaxialRestorer` 从父、子各自映射的**固定机械参考平面到固定像面**读取矩阵，以父结构的 `[A, f_target*C]` 为恢复目标。任务残差为误差除以各自尺度，附加 `sqrt(eta)*(q-q0)`；q0 来自结构编辑后的子结构，维数不要求与父结构相同。
- 第一阶恢复只释放显式允许的镜片曲率或轴向位置，固定参考、光阑、像面、真实材料及保护片；使用现有有界信赖域，数值秩检查与差分也计费。每次几何检查调用共享 Core，不复制追迹、折射、色散或近轴公式。恢复成功只表示一阶容差满足。
- `TopologyRestorationWorkflow` 验证编辑指纹、增删 ID 与保留元件不变，依次执行真实结构编辑、近轴恢复、局部补偿、全目标优化和独立验收。每阶段重新绑定变量并新建 Adam 矩；不借用 UpstreamE58 状态，不把矩阵恢复当作完整达标。
- 函数请求包含父矩阵与全部子结构/差分尝试；几何探测、接受及拒绝试探、矩阵请求、中途取消的已用工作保留。全分支有函数、梯度和墙钟上限，并预留独立终验槽。矩阵计数仅覆盖恢复的显式矩阵请求，固定优化器内部近轴调用未单独计数；实际光线计数保留，硬光线配额仍属于 M4。

## 小预算协议

输入为未改写的 [foundation-3.json](../../../../labs/InitialStructure/benchmarks/autonomous-topology-v1/foundation-3.json)，像面固定 55 mm、参考与前光阑固定 0 mm。无限远、0–5°、F/d/C 三色，独立前光阑半径 2 mm。目标 EFL 50 mm、F/12.5，各容差 2%；最差 RMS ≤50 µm、最大半径 ≤150 µm、主光线畸变 ≤3%，并保持结构、制造及通光门槛。

增片分支在 lens-1 后与 lens-2 前的空气区间中点，加入厚 1 mm、半口径 7.5 mm、N-BK7、前后曲率 ±0.003/mm 的真实单片；首次恢复冻结新片。删片分支删除非保护 lens-2，其他元件坐标不变。恢复曲率界限 ±0.08/mm、变量尺度 0.01/mm，A 与 fC 残差尺度均为 0.01、接受容差均为 1e-4、最小改变量权重 1e-6。此组参数仅是本轮小样例校准，不是通用最优参数。

恢复最多 30 接受步、400 请求，测试显式使用 16 个轮廓点，产品默认 64 点。局部补偿释放首个保留镜片的两面曲率，做 2 次更新；全目标阶段释放全部非保护片曲率，做 5 次更新。训练为 3 视场、Gaussian 1×8、8 轮廓点，CPU 学习率 0.02。全分支上限为 1000 函数请求、400 梯度请求、10 分钟，实际远低于上限。

两阶段各自的独立验收均为 Core CPU、21 个视场、Gaussian 10×32、32 个边界点加中心、64 个轮廓点。原始参数、各阶段快照、残差、试探、工作量和协议指纹完整写入 JSON；两个终点均保存为实际 `.staropt` 并重开，处方指纹一致。没有恢复旧完整课程或进行原版最终效果对比。

## 实际结果

| 分支 | 恢复接受步 | A 误差初始→恢复后 | fC 误差初始→恢复后 | 全目标后 EFL / mm | F 数 | 最差 RMS / µm | 完整达标 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| [3→4 增片](insert-3-to-4.json) | 2 | -0.111845 → 2.744e-8 | -0.124806 → 4.304e-8 | 48.561735 | 12.140434 | 110.719 | 否 |
| [3→2 删片](delete-3-to-2.json) | 3 | -0.616624 → 8.950e-7 | -0.677304 → 1.283e-6 | 48.471862 | 12.117965 | 111.914 | 否 |

两个分支均完成恢复和后续真实补偿/验收；一阶恢复满足 1e-4 容差，短程终点仍有焦距、F 数和 RMS 越限，不能称为成功设计。共完成的函数请求分别为 52、49，其中恢复矩阵请求为 41、38；两者梯度请求均为 9、独立验收均为 2、实际计量光线均为 90390。这些请求的成本不同，不能用总次数等同完整光学计算成本。

## 定向验证与复现

共 **8 个不同用例最终通过**：Core 3 个矩阵契约；模块 5 个用例，包括两个真实分支、失败分类/中途取消、q0/首片删除参考，以及终验预留/篡改拒绝。模块只重跑受影响范围，最终按用例名称和时间去重；不累加重复执行数。

首轮模块为 3 通过、2 失败：ImmutableArray 断言错误地比较了容器身份。改为逐元素精确比较后，一项恢复失败夹具的初始曲率仍超出它声明的变量界限；夹具改为明确允许该初始值，随后由正式 Core 几何检查返回 GeometryBlocked。原始失败和修正 TRX 全部保留，不放宽运行时几何或设计门槛。终点 STAROPT 往返另行复核两个分支。

```powershell
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M3_EVIDENCE = 'validation/rechecks/2026-10-26/autonomous-restoration-m3'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --filter FullyQualifiedName~ParaxialTransferMatrixTests
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --filter FullyQualifiedName~TopologyRestorationTests
```

[机器汇总](summary.json)保存每次 TRX、去重结果、源代码与二进制指纹、两个分支原始结果和失败分类。只整理本轮 7 个 C# 文件的格式；默认 Release App/测试输出已更新，Debug 构建结果见 [日志](app-debug-build.log)。未跑全仓测试、未作 M3 整体 CUDA/外部数值认证、未修改冻结基准、未提交推送或发布安装包。

下一阶段为 M4：有限候选搜索、全流程工作预约/硬光线配额、检查点及恢复、已达标档案保护；当前单分支返回值不能冒称已经具备这些能力。
