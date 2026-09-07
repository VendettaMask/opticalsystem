# S1 通用局部求解器实施记录

日期：2026-09-08。本阶段交付 [策略整改](INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md) 的 S1：独立、可重复运行的局部最小二乘算法及成本记录。镜头搜索仍调用 v4，S2～S4 尚未完成；没有运行新一轮 60 次镜头验收，也没有对已知失败种子调整权重或门槛。

## 已实现的自动决策

代码位于 `labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/Optimization`：

- `BoundedTrustRegionLeastSquares` 接收变量、独立上下界和残差评价回调。用矩形信赖域内的 Cauchy/dogleg 步，在每次试探后比较真实下降与模型预测下降，自动接受、拒绝、缩小或扩大区域。
- 同一位置的拒绝步复用雅可比、活动变量集合和 QR 求得的 Gauss–Newton 方向。接受后重建差分模型；本阶段未加入跨位置割线或拟牛顿更新。
- `PivotedLeastSquares` 用列主元 Householder QR 求小型稠密最小二乘，避免显式构造 `JᵀJ`。秩不足时返回基本最小二乘解，不声称是最小范数解。模型步的预测下降不得低于 Cauchy 步。
- 通过梯度方向判断活动上下界，并在自由变量子空间求解；允许变量从边界向可行域内移动。固定变量不发起差分评价。
- 边界处使用实际差分距离；两侧距离不等时组合加权单侧斜率。只有一侧有效则使用那一侧；两侧都无效时缩小探测距离，仍不可用则返回 `DerivativeUnavailable`，不补造零梯度。
- 所有初始评价、差分、接受和拒绝试探均收费。每次调用前检查剩余评价预算、取消和外部停止；预算可在差分列中间耗尽，返回点仍是上次接受的位置。评价回调复用数组不会污染已保存的残差。

矩形区域、活动集合和 dogleg 的公开依据为 Voglis 与 Lagaris 的 [A Rectangular Trust Region Dogleg Approach](https://www.cse.uoi.gr/~lagaris/papers/PREPRINTS/dogbox.pdf)，特别是第 2～4 页的算法。本文实现以 Gauss–Newton 最小二乘模型和 QR 求解为基础，论文使用 BFGS；这里是自主 C# 实现，没有移植外部运行时。接受比例 0.1、收缩比例 0.25、扩张判断 0.75 来自公开算法规则，不来自某个失败镜头的调参。本实现不称为 SYNOPSYS PSD，不作商业软件速度比较。

## 接口与依赖边界

求解器只包含通用线性代数，不调用 Core 光学类型、实验室镜头问题、文件系统或外部进程，也没有新增包依赖。依赖方向仍是实验室 Engine → Contracts/Core，正式 `src` 不引用实验室。本阶段没有修改正式 Core 或 v4 的光线、材料、分析、搜索路径、算法版本和检查点格式。

变量和残差必须由调用方按明确的无量纲尺度提供。S1 **只支持各变量的独立上下界**，不支持任意耦合投影或非线性约束。旧 `FlatStartDesignProblem.Project` 会在总长越界时同时压缩多个厚度；直接把它当成独立坐标投影会使差分含义错误。因此本阶段没有直接替换 v4 的调用，S2 必须先用明确的约束或正确参数化表达总长，并将物理约束恢复与像质改善分开。

每个 `Solve` 的残差定义必须固定。返回向量维度发生变化会报错；如果采样、材料、变量集合或同维残差的含义改变，适配层必须开启新调用，不得复用旧模型。本阶段没有可跨设置保存的模型缓存。

`LeastSquaresResult` 记录总评价数、差分评价数、完整雅可比构建数、QR 分解数、实际工作量及逐步模型编号/半径/预测与实际下降。`MaximumEvaluations` 是硬预算；`WorkUnits` 是回调报告的实际累计成本，尚不是未知成本评价的预留式硬上限。取消抛出 `OperationCanceledException`；回调自身的异常不吞掉。`Stationary`、`StepTooSmall` 等仅是局部数值停止原因，不代表物理可行、达到光学指标或得到全局最优解。

## 验证

新增两个测试文件、**35 项机制测试**：解析线性和非线性 Rosenbrock、近相关矩阵和病态残差、秩不足/欠定/超定问题、活动/可离开/固定/窄边界、不可满足残差、无效评价与单侧差分、不可探测域、非有限成本、逐评价预算边界、取消、外部停止、可变数组隔离和确定性。

拒绝步复用测试使用 `r(x)=x²−1`、起点 0.01。三次连续拒绝共 **6 次评价 = 1 次初值 + 2 次差分 + 3 次试探**，只有 **1 次雅可比构建、1 次 QR 分解**；每次回调报告 7 个工作单位，总计 **42**。若每次拒绝后重新做这两次差分，同样三次试探需 10 次评价。这是机制成本证明，不是光学镜头加速实测。

首次定向运行 32/33 通过：窄边界测试把末位舍入后的 `StepTooSmall` 错当成失败。该例的剩余步小于机器可分辨位移，而 `Jᵀr` 仍可能略大于梯度阈值；修正测试以允许这两种明确停止原因，并保留最终变量精度断言，没有放宽求解器容差。随后增加完整求解器病态问题与非有限差分坐标防护测试，定向 **35/35** 通过。

完整实验室 **Release 150/150** 通过，零失败、零跳过，约 8 分钟，包含既有 115 项和新增 35 项；新增测试不能再与完整数相加。结果为 `s1-release-full-lab.trx`。锁定依赖还原、默认 Debug/Release 构建（均零警告、零错误）、完整实验室格式检查、`git diff --check` 及新增源文件/文档的空白检查通过。证据目录：`artifacts/validation/initial-structure-s1-20260908`，汇总为 `engineering-s1.json`。还原使用本机包缓存，关闭本次 NuGet 在线审计；没有新增包或修改锁定版本。

首次完整命令漏写 `-c Release`，启动了 Debug 全量测试；此前 115/115 的基线使用 Release。Debug 全量运行较长后主动中断，测试进程已退出，日志 `full-lab-tests.log` 只作为未完成记录，不能记为通过。随后按相同 Release 配置重新启动完整测试，详细日志为 `release-full-lab-tests.log`。Debug 的 35 项定向测试已完成，但不能替代 Debug 全量结果。

机制测试可独立复跑（不是完整实验室回归命令）：

```powershell
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj --no-restore --filter 'FullyQualifiedName~BoundedTrustRegionLeastSquaresTests|FullyQualifiedName~PivotedLeastSquaresTests' /m:1 /nr:false
```

完整实验室验证命令：

```powershell
dotnet restore labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx --locked-mode --force --source C:/Users/19851/.nuget/packages -p:NuGetAudit=false /m:1 /nr:false
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Debug --no-restore /m:1 /nr:false
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Release --no-restore /m:1 /nr:false
dotnet test labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Release --no-restore --no-build --logger 'console;verbosity=normal' /m:1 /nr:false
dotnet format labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx --no-restore --verify-no-changes
git diff --check
```

缓存路径属于本机；其他环境可使用正常 NuGet 源执行同一锁定还原。本阶段还核对了保存的八个 v4 源码文件，SHA-256 全部不变；正式 Core Release 哈希仍为 `34bd189f3b446a9d1004f62f05fbf54f329b6111616d0758a932dbbc1bf6542b`。证据 `v4-source-integrity.json` 区分“旧光学调用代码未改”和“加入 S1 后 Engine 二进制发生变化”。

本阶段未重新运行正式解决方案的完整测试；其此前结果仍是主测试 1289/1294（5 项既有失败）、工具 104/104，不能描述成全绿。冻结 v4 镜头门槛仍为 52/60、9/12，新 S1 模块尚无镜头精度或速度结论。新增代码会改变 Engine 程序集哈希，不能再用当前 Engine DLL 代替冻结 v4 二进制；原 `v4-preserved-bin` 和结果中的哈希保留。

## 后续顺序

S2 处理正式 Core 残差与显式约束、可行性恢复和自动阶段推进，并验证同快照同设置一致性；S3 才接入筛选、晋升、停滞和预算调度及其检查点状态。完成这些机制后冻结搜索版本，再执行 S4 原协议及独立留出规格。此处不更改原 12×5 协议或把 S1 测试计入镜头成功数。

S1 实施时先保留为本地可审查修改。2026-09-08，用户随后授权更新并同步到远端，本次提交纳入此前的 P5 修复、冻结验证证据与 S1 求解器。同步前重新核对：S1 源码及 Core/Engine 二进制与上述验证记录一致，Zemax 清单的 326 个文件哈希全部匹配。同步不代表 S2/S3 已实现、P5 发布门槛通过或正式项目的 5 项既有失败已解决；本次没有新增数值计算改动，也没有重跑完整测试。
