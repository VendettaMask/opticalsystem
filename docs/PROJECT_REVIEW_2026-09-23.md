# 项目理解与下一步建议（2026-09-23）

> 2026-09-27 后续：当前 v16 修正后续细化入口，在完整光线域内保持完整目标的几何/像质联合求解；同预算对照、文献及 247/247 回归见 [细化入口复核](INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md)。本文版本、计数、计划和结论保留为历史阶段信息。

> 本文保留该阶段历史记录。2026-09-26 的正式回归修复、v7 S2/S3 实现与 S4 验证见 [最新实施记录](INITIAL_STRUCTURE_S2_S4_2026-09-23.md)；下文旧测试计数及“尚未完成”状态限定于当时。

审阅对象：`280bb7b00e6bffbd2042ce907c9e74c56385a1c3`。开始时工作区干净。本轮梳理解决方案、项目引用、关键运行链路、测试、CI、近期提交和冻结验收记录，并实际执行构建与定向验证。没有逐行审计全部算法，没有修改产品或实验室代码，也没有重新进行完整光学验收。

建议先处理正式产品的数值回归红灯，再以初始结构实验室 S2 为下一个功能交付。S2 完成之后进入 S3 搜索调度和 S4 固定协议验收。当前已有明确的实现路线，最有价值的工作是把共享计算、约束恢复、搜索策略和验收接成闭环。

## 项目是什么

这是一个自行实现光学计算的纯 C#/.NET 10 桌面设计系统。正式产品通过 Avalonia/Dock 提供顺序与非序列设计、分析、优化、公差、制造与文件工作流；独立实验室探索从严格平板起步生成镜头。`OptilandWorkbench` 是现有程序集命名，不能据此推断产品依赖 Python Optiland。

| 部分 | 当前职责与关键入口 | 需要保持的边界 |
| --- | --- | --- |
| Core | `Optic`、表面/材料/坐标模型、顺序与非序列追迹、分析、优化、公差、序列化 | 唯一共享光学计算层；未知能力明确阻断，不能返回伪成功 |
| Application | `WorkbenchApplication` 组装服务，`WorkspaceCoordinator` 管事务与修订，`WorkbenchRuntime` 组织规范分析与编辑 | 编辑、撤销、保存、取消、缓存失效和结果来源必须保持一致 |
| App | Avalonia 桌面、镜头表、分析控件、二维/三维布局、Dock 工作区、主题 | 通过 Application 服务与 DTO 访问产品；展示选择和单位使用类型化元数据 |
| Initial Structure Lab | Contracts / Engine / Persistence / App / Benchmarks；严格平板根、家族搜索、精修、检查点和独立桌面 | Engine 依赖正式 Core；进程、数据和搜索策略独立，光学计算不独立复制 |
| 验证与维护工具 | ZemaxComparison、镜头库构建、玻璃转换、性能测试等 | 工具可依赖产品，产品不能依赖 ZOS-API、捕获工具或测试资料 |

实际依赖是 `App → Application → Core`。实验室桌面由正式 App 的启动器另开进程，不随正式产品自动加载，也尚未包含在标准安装包中。“AI 初始结构”的当前实现是确定性数值搜索；现有入口没有引入训练模型或代理系统。

正式分析链是：捕获文档快照和修订 → 规范化请求 → 调用共享 Core → 映射带来源、展示类型和轴单位的 DTO → 桌面显示。实验室则是：冻结规格 → 严格平板根 → 局部求解与结构提案 → Core 评价 → 独立密集复算 → 检查点/STAROPT 导出。后续改动应围绕这些入口展开。

## 已有能力与尚未完成的范围

- 正式产品已有广泛工作流，Core 目录登记 72 个规范分析，桌面按顺序 70 项、非序列 2 项隔离。登记、能执行和通过外部数值比较是不同状态。
- 非序列已有独立对象文档、10 类原生光源、实体/STL、BVH、Fresnel 分支、探测器、STARRDB 和独立布局/分析会话。精确 CAD 导入、完整 BSDF、偏振/相干探测和非序列优化等仍在计划中。
- 正式优化目录只公开五个已有实现。高级优化器与 Glass Expert 明确不支持；实验室 S1 的完成不会自动增加正式产品的优化算法。
- Zemax 操作数存在可执行与兼容只读两种状态；文档登记的 383 个代码中只有 124 个接入计算路径，不能将注册数量当作兼容数量。
- 膜层、散射和径向介质的部分实现仍明确为 Experimental/入口方向近似。完整薄膜 S-matrix、BSDF 和 GRIN 连续传播尚未完成。
- 最近的主要研发活动集中在实验室：P0～P4 已形成规格、共享评价、搜索和桌面闭环；P5 验收未通过；S1 已实现，S2/S3/S4 待实施。

上述能力来自源码与现有文档交叉核对，不构成所有模型、平台和参数组合的正确性声明。[系统架构](ARCHITECTURE.md)、[系统收口计划](SYSTEM_COMPLETION_PLAN_2026-09-02.md)和[实验室策略](INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md)分别描述其边界。较早的[代码阅读地图](CODE_READING_MAP.md)仍提到已移除的 Compatibility 项目，不能直接作为当前项目引用清单；当前以解决方案和 `.csproj` 为准。

## 本轮实际验证

环境：macOS ARM64，.NET SDK 10.0.300 / Runtime 10.0.8。两个解决方案均使用锁文件从本地 NuGet 缓存还原，本次没有在线漏洞审计。最初正式构建因两个比较工具项目缺少 `project.assets.json` 失败；还原后正常通过，这是本机依赖准备问题。

| 验证 | 2026-09-23 实测结果 | 能证明的范围 |
| --- | --- | --- |
| 正式解决方案 Release 默认输出构建 | 0 警告、0 错误 | 当前源码可构建 |
| 实验室解决方案 Release 默认输出构建 | 0 警告、0 错误 | 独立实验室可构建 |
| 正式主测试定向集合 | 19 项：14 通过、5 失败、0 跳过 | 复现既有失败，并验证相关 PSF 工作 F/# 回归；不是主测试全量结果 |
| ZemaxComparison 工具完整测试 | 104/104 通过、0 跳过 | 比较工具及其离线契约回归；不代表新执行了 Zemax 实机比较 |
| S1 求解器/QR 机制测试 | 35/35 通过、0 跳过 | 通用数值机制；不是光学搜索成功率验证 |
| 主基准源 ZMX SHA-256 | 与捕获 manifest 一致 | 仅该源镜头文件完整性；未全量校验该目录所有捕获资产 |
| v4 协议与规格输入 SHA-256 | 13/13 匹配 | 冻结协议与 12 个规格输入未变化 |
| v4 结果重新汇总 | 60 次中 52 次成功，9/12 个规格达到各 4/5 | 读取并核对既有结果；本轮未重新搜索 |

TRX 保存在本地 `artifacts/validation/project-review-20260923/`，不作为新全量基线。既有全量记录仍分别是正式主测试 1289/1294（5 失败）、比较工具 104/104，以及 S1 后实验室 Release 150/150。没有将本轮定向通过数与历史全量数相加。

本轮未进行 GUI 截图或原生交互验收、安装包验证、完整主测试/实验室测试、60 次搜索或新的 Zemax 实机捕获。基准完整性、当前 Workbench 重算、数值误差和界面验证分别报告。

## 下一步工作顺序

### 1. 先处理正式数值回归的两个问题

**历史 FFT MTF 契约：四项失败。** Cooke/Tessar 的截止频率实际为 331.7570273262463 / 341.3530915956969，冻结历史期望为 365.2289984316186 / 380.11010381917816；对应显示频率网格也失败。位置是 `FrozenAnalysisRegressionTests.FftMtfRetainsFrozenReferenceFrequencyGridWithPowerAmplitude` 和 `MtfAnalysisUsesFftSeriesContractWithPowerAmplitude`。

当前 `DiffractionEngine.ComputeFftMtf` 保留 PSF 所属视场的工作 F/#，`PsfWorkingFNumberRegressionTests` 的 14 项均通过。这提示需要优先核对旧频率契约与当前物理尺度定义是否冲突，但本轮没有完成根因证明。先用独立物理检查及适用的 Zemax 证据确定正确行为，再决定是修计算还是修测试契约。冻结历史资产不改写、不重新生成；不能为了旧参考回退已验证的产品语义，也不能单纯放宽断言。

**主 Zemax 相对照度：一项失败。** `ZemaxRelativeIlluminationParityTests.ChiefRayTangentPupilProjectionMatchesZemax123456` 实测 RMSE 0.0033418652、最大误差 0.015195217，原门槛为 0.002 / 0.004。需要逐视场核对原始捕获设置、瞳边界追迹、物理裁剪和积分链路。当前失败是确定事实；具体原因仍待定位。

交付门槛：两类问题各有独立原因、针对性回归和权威依据；完整主测试与工具测试通过，默认构建保持零警告/错误，所有声明当前全量基线的文档同步。任何经证实的测试语义调整都应保留原失败证据和修改理由。

### 2. 下一个功能交付：S2 光学约束适配

这是当前研发主线中最明确的下一步。`BoundedTrustRegionLeastSquares` 目前只在其定义与测试中出现，光学设计仍调用旧 `ProjectedDampedStep`。仅替换函数调用不成立：`FlatStartDesignProblem.Project` 在总长超限时会同时压缩多个厚度，而 S1 的差分模型只支持独立变量上下界。

建议把 S2 分为三个可独立验收的交付：

1. **变量与约束契约。** 明确曲率、中心厚度、气隙、后焦和总长的尺度及约束；通过正确参数化或显式耦合约束处理总长。证明边界附近的差分不被隐式多变量投影改变含义。
2. **可行性恢复与像质优化。** 区分无法传播、可传播但违反硬约束、满足约束三种状态；正式 Core 提供光斑、光焦度和孔径诊断，实验室只组织残差和接受规则。未裁剪诊断只服务恢复，最终验收仍用物理裁剪后的逐视场/逐波长结果。
3. **自动阶段控制。** 根据可行性与进展推进或回退孔径、视场、波长和采样阶段。设置、材料、变量或残差语义变化后开启新模型；把差分、拒绝试探、诊断和最终复算都纳入预算。

验收重点是同快照同设置与 Core 一致、总长活动边界、裁剪恢复后仍必须独立验收、阶段变化后模型失效、取消和预算不超支，以及固定种子的确定性。S1 的 `WorkUnits` 当前只是已发生成本统计，不能当作已有的光线数硬预算；如引入该上限，需要在调用前预留费用。

S2 首个实现任务可明确命名为：**“为平板设计建立显式变量/硬约束契约，并接入 S1 的单阶段求解”**。先证明这一层正确，再接自动阶段控制。

### 3. S3 自动调度，随后 S4 固定协议验收

`FlatStartSearchService.Propose` 目前按 `ordinal % 5` 循环选择精修、换玻璃、换光阑和扰动；父候选也按固定次序轮换。S3 应根据改善、停滞、多样性和剩余预算分配探索与精修，并将调度及随机状态纳入检查点。不同采样等级的分数不能直接混排。

机制稳定后冻结算法版本，执行原 12 个规格 × 5 个种子的 S4，并另报留出规格。原门槛保持至少 10/12 个规格各 4/5 次成功，不改变预算、分母或物理阈值。当前 v4 的规格 10～12 均为 3/5，只用于说明现状，不能据此硬编码种子策略或继续人工调权重。

搜索门槛之外，P5 还需要旧混合原型在相同目标和预算下的比较，以及要求的原生界面验收；不能用求解器单测或旧启动记录代替。[P5 记录](INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md)保存了这些独立条件。

### 4. 后续按实际设计任务扩大精度与功能覆盖

正式数值主线仍有 Huygens PSF 振幅/传播与衍射圈入能量链路待验证。历史 MS-L7 全分析结果为 44 Pass、6 Close、2 Difference、17 Incomparable、3 Skipped；本轮工具测试通过不改变这些状态。图像/光源共同输入契约和未执行操作数也应围绕实际镜头用例逐项接入。

完整 GRIN、BSDF、精确 CAD、高级非序列优化、Glass Expert、GPU 和整体命名重构需要独立投入，目前宜排在上述闭环之后。已有架构和界面可支撑下一阶段，先把数值依据与自动搜索能力做实。

## 验证复跑入口

两套解决方案均使用 `-c Release --no-restore /m:1 /nr:false` 构建，未使用替代输出目录。测试均使用 `-c Release --no-build --no-restore`：

- 正式定向过滤：`FullyQualifiedName~FftMtfRetainsFrozenReferenceFrequencyGridWithPowerAmplitude|FullyQualifiedName~MtfAnalysisUsesFftSeriesContractWithPowerAmplitude|FullyQualifiedName~ZemaxRelativeIlluminationParityTests|FullyQualifiedName~PsfWorkingFNumberRegressionTests`。
- 比较工具：完整执行 `tests/OptilandWorkbench.ZemaxComparison.Tests/OptilandWorkbench.ZemaxComparison.Tests.csproj`。
- 实验室过滤：`FullyQualifiedName~BoundedTrustRegionLeastSquaresTests|FullyQualifiedName~PivotedLeastSquaresTests`。

本轮只新增审阅记录及 README 索引；实施、提交和远端同步均未进行。
