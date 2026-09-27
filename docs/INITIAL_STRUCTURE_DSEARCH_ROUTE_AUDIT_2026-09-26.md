# DSEARCH 实现路径复核（2026-09-26）

后续运行时已完成 [v10 R2 修正](INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md)及 [v11 自动镜片直径](INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md)。本文保留 v8 阶段的冻结事实、测试数量与诊断结果，不代表当前版本的完整搜索验收。

结论：二进制形式搜索和共享正式 Core 的方向可以保留，但当前开发顺序与内层优化机制需要调整。v8 实现了输入、形式初始化、候选管理与预算调度，尚未建立与 DSEARCH 资料相应的快速像差筛选、材料/光阑连续变量和局部收敛能力。继续主要增加界面选项或重排外层试验，不能解决已经发现的内层问题。

本次查阅 OSD 官方自动设计说明和教程，以及原作者和独立研究论文，并对照现有代码完成独立诊断。正式 Core、实验室运行时代码、冻结验收规格和 v8 程序集均未修改；新增的是隔离的诊断程序、结果和整改依据。v8 的 **178/178** 工程基线、原协议 **30/60、6/12** 与留出集 **15/15** 仍是原记录，不是本次复测结果。诊断结果不加入工程测试总数或搜索验收分母。

本次没有新的 OpticStudio 实机捕获或外部数值比较；网络资料是方法依据，诊断是共享 Core 的内部计算实验，两者都不能冒充外部精度认证。

## 公开资料与实现差异

| 环节 | 资料支持的行为 | 本项目 v8 与判断 |
| --- | --- | --- |
| 初始形式 | 平板出发，给各片弱正/负光焦度，探索不同下降方向 | `DesignFormSearch` 的正负初始化与这一概念一致；10 倍焦距的起始半径是自己的选择，未证明为最佳值。 |
| QUICK | 三阶、五阶像差与少量真实光线筛选，然后以真实光线深入优化前列候选 | 当前只缩减真实光线优化预算，没有像差级初筛。这是明确的语义与能力差异。 |
| 局部优化 | PSD 使用相邻迭代的导数变化获取高阶信息 | 当前是矩形信赖域、QR 与 dogleg，不是 PSD；缺少历史曲率信息，但不能仅凭算法名称断言所有失败都由此引起。 |
| 材料与光阑 | 可优化模型玻璃的折射率/阿贝数、隐含光阑位置，随后匹配真实材料、落实物理光阑并复优化 | 当前主要是目录玻璃替换和离散表面光阑，可搜索空间更窄；这不表示目录玻璃路线本身不合法。 |
| 焦距与像面 | 根据问题选择一阶 solve 或带权目标；难例可固定后焦并放开最后曲率 | 当前让有限个相互关联的焦距/光阑指标与光斑超限量一起恢复，尚未验证等效的变量消元路径。 |
| 计算预算 | 示例中的 QUICK 参数表示优化 passes，并有不同阶段的优化安排 | 当前每根预算约等于 4 次完整中心差分与试探的成本；函数评价次数与 passes 不是同一单位。 |

二进制方向和 PSD 的关系见 Dilworth 的原始论文 [The Ascendency of Numerical Methods in Lens Design，§4–5](https://www.mdpi.com/2313-433X/4/12/137)。其中的速度与质量比较是论文示例，不能直接作为本项目性能目标或已验证事实。

QUICK 的两层流程由 [OSD Automatic Design Tools，第 7–8 页](https://osdoptics.com/wp-content/uploads/2021/12/Automatic-Design-Tools-in-SYNOPSYSTM.pdf)明确说明；该说明中的候选数属于示例流程。历史[搜索策略文档](INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md)已经提到这一差异，但 v8 没有补齐它。上一轮把工作流程改造作为主要推进，覆盖不足。

材料连续变量及真实材料替换见 [Lesson 22](https://osdoptics.com/wp-content/uploads/2020/05/22_using_the_synopsys_glass_model.pdf)；隐含光阑转物理光阑、后焦和曲率 solve 的取舍见 [Lesson 41，第 2–4 页](https://osdoptics.com/wp-content/uploads/2020/05/41_designing_a_very_wide-angle_lens.pdf)。同一教程也展示困难设计关闭 QUICK，逐候选执行完整真实光线优化。因此可以先做可靠的真实光线路径，不必等完整五阶像差实现后才推进。

官方 [Lesson 16](https://osdoptics.com/wp-content/uploads/2020/05/16_a_practical_camera_lens.pdf)分阶段使用不同成像目标，并在后期替换真实玻璃、安排制造条件。它说明设计搜索与最终工程验收需要分层，不能推导出本项目应该降低冻结门槛。官方[厚度控制说明](https://osdoptics.com/wp-content/uploads/2023/04/Airspace-and-lens-thickness-control-in-DSEARCH.pdf)还区分变量上下界与优化监控目标；本项目同样需要明确两者的作用。

## 代码中优先处理的问题

### 1. 局部优化、保留候选和外层排序使用三种目标

[`FlatStartLocalSolver`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartLocalSolver.cs) 在 Targets 阶段使用 `ConstraintResiduals`：焦距/光焦度/F 数超限，以及各视场的 RMS/最大半径超限。只有全部门槛满足后，才进入使用逐光线 `ImageResiduals` 的阶段；默认不继续改善已达标候选。

[`FlatStartDesignService`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartDesignService.cs) 保留密采样最佳点时使用 `Merit`；[`FlatStartCandidateArchive.Score`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartCandidateArchive.cs) 又按最大 RMS、最大半径、总通光和焦距误差重新组合分数。三者并不相同。状态分层与多样性选择有必要，但需要统一每个采样阶段的目标定义、尺度和排序依据，避免局部下降后在另一套分数下无意义地换分支。

### 2. 残差压缩与当前求解器不匹配

对四片、非固定后焦、非零视场的情况，变量为 16 个；粗采样 Targets 残差只有 9 项，密采样只有 15 项。门槛内的 hinge 残差还会变成零。由矩阵维度可知，雅可比不可能有满列秩；这不等于问题无解，但限制了 Gauss–Newton 模型携带的方向信息。多个焦距相关指标也不是独立的物理自由度。

现有 [`BoundedTrustRegionLeastSquares`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/Optimization/BoundedTrustRegionLeastSquares.cs) 使用矩形信赖域 dogleg。作为数值方法参考，SciPy 官方对同类 dogbox 方法明确提示秩不足时可能收敛缓慢。[least_squares 文档](https://docs.scipy.org/doc/scipy/reference/generated/scipy.optimize.least_squares.html)不是本项目的运行依赖，也不证明两个实现完全相同。

优先试验应保留 Core 返回的逐光线残差结构，明确尺度及约束处理，再比较局部求解器。把残差直接取一个总 RMS 或一个总范数交给最小二乘，虽然可能保持标量目标值，却改变了近似曲率信息。

### 3. 导数试探域被最终通光门槛截断

Targets 阶段要求每个数值差分点满足 `IsFeasible`，其中包含物理通光门槛。即便 Core 的非截断孔径诊断仍能返回完整、连续的光线数据，这些试探点也会无效。四轮缩步仍找不到单变量的有效差分后，求解器以 `DerivativeUnavailable` 退出。

这项发现来自源码与下述独立实验，不是 SYNOPSYS 内部实现描述。整改必须区分几何/传播无法计算、可计算但违反孔径约束、最终达标三个状态。只能使用正式 Core 的连续诊断与实际追迹；不得用近似光线填补 TIR 或追迹失败，也不能用非截断结果冒充最终物理通光。

### 4. 外层形式数增加之前，需要让每个方向有有效下降

当前 QUICK 配额 `4 * (2 * Dimension + 1)` 仅提供约四轮中心差分的数量级，实际还要扣除初始化、验证和拒绝步。资料中的优化 passes 与这个预算不同。前一轮六片原协议的种子 101 已显示：24/64 个形式，4704 次起始评价、5296 次细化评价，未进入玻璃或光阑邻域。[v8 分支证据](../labs/InitialStructure/benchmarks/dsearch-v8-20260926/branch-audit.json)

因此应先测每个方向的收敛与失败原因，再设置阶段预算和晋升标准。全局预算仍必须严格遵守，未完成覆盖继续明示，不改变原验收分母。

## 独立诊断结果

使用两个未进入原协议或留出集的目标：62 mm / F6 / 半视场 7°、113 mm / F7 / 半视场 4°。每个目标指定四种四片起始方向，固定同一玻璃配置；每个起点先以现有流程执行 256 次评价，再从完全相同的处方比较四种残差/试探域组合，每种最多 600 次评价。全部采用正式 Core 的六视场、97 瞳点及三波长密采样。总计 32 个诊断运行，完整保存所有结果。

| 组合 | 完整门槛成功 | 无法求导退出 | 求解评价总数 |
| --- | ---: | ---: | ---: |
| 当前 Targets 超限残差 | 0/8 | 4/8 | 4317 |
| 现有联合逐光线残差 | 0/8 | 2/8 | 4464 |
| 上述残差压成单一范数 | 0/8 | 2/8 | 4460 |
| 联合逐光线残差，允许 Core 连续诊断试探域 | 0/8 | 0/8 | 4772 |

第一组共 319 次评价仅因通光门槛被拒绝，仍有连续 Core 数据。四次导数失败均伴随这种拒绝。第四组与第二组目标相同，保留原有孔径违规残差和最终门槛，仅扩大可计算的试探域；两组对照中，导数失败从 2/8 降为 0/8。它没有改变严格的最终接受规则。

具体例子：62 mm 的 `++++` 起点，第一组到焦距 65.6566 mm、最差 RMS 0.06375 mm；第二组到 62.0760 mm、0.050106 mm，仍未通过。113 mm 的 `----` 起点，第四组到 113.1327 mm、0.047843 mm，但最大光斑半径 0.194372 mm，超过 0.15 mm，仍判失败。

这说明目标表达与可行性域确实影响内层行为；不能由此声称只换残差就能解决设计问题。第二组与第三组在同一点的平方目标完全相同，仅最小二乘残差表达不同；第一组与第二组则改变了目标，两类对照不能混为一谈。实验只隔离局部阶段，不代表完整搜索或新版本成功率。

证据和可复现 C# 程序见[诊断目录](../labs/InitialStructure/benchmarks/dsearch-route-audit-20260926/README.md)。增加记录和第四组后，原先 24 个案例逐字段完全一致；运行时源码和计算程序集仍与最终 v8 匹配。诊断中的光线计数是分析请求样本数，不是含全部瞄准开销的实际追迹次数。

## Core 能力边界

正式 Core 已有 [`SeidelCoefficientsAnalysis`](../src/OptilandWorkbench.Core/Analysis/Reports/SeidelCoefficientsAnalysis.cs)，但目前是报告接口；未找到可直接用于 DSEARCH 式筛选的完整三/五阶类型化评估接口。后续若走该路线，应先在共享 Core 建立并验证服务，实验室只组装残差。

Core 的 [`AbbeMaterial`](../src/OptilandWorkbench.Core/Materials/IMaterial.cs) 目前采用简单波长线性关系，不能因它接受 Nd/Vd 就认定等效于 SYNOPSYS 的玻璃图拟合模型。连续模型玻璃需要合法材料域、部分色散与目录替换后的复验。最终只能接受规格允许的真实材料，不得扩大冻结允许集合或让模型玻璃直接通过最终验收。

PSD 文献支持研究导数历史与曲率更新：[Dilworth 1978](https://doi.org/10.1364/AO.17.003372)、[Lesson 23，第 3 页](https://osdoptics.com/wp-content/uploads/2020/05/23_parametric_optimization_study_and_ray_failure_correction.pdf)。独立论文 [Open-source optimization algorithms for optical design](https://doi.org/10.1016/j.ijleo.2018.10.073)也说明通用算法可以用于镜头问题，并指出商业 PSD 实现源码不公开。不能声称必须复制 PSD 才能成功，也不应把一个通用曲率更新重新命名为 PSD。

公开可核查的局部优化候选包括带尺度的信赖域/阻尼最小二乘，以及受模型质量约束的 Jacobian 更新或测地加速；[Transtrum 与 Sethna](https://arxiv.org/abs/1201.5885)讨论了这些改进的速度与初值鲁棒性权衡。这里仅列研究方向，尚未接入产品，后续实现仍为纯 C#/.NET。

## 调整后的实施顺序

| 顺序 | 交付及通过条件 | 本次状态 |
| --- | --- | --- |
| R1 | 统一真实光线目标与排序；建立连续试探域、实际可行性和最终验收的独立契约；用机制用例验证残差形状、有限差分和边界行为 | 完成问题定位和隔离诊断；产品修复未接入 |
| R2 | 在同一目标和初值上比较秩不足处理、变量尺度和局部模型；记录 Jacobian/拒绝步/真实追迹成本，证明收敛后再采用 | 尚未实现 |
| R3 | 先建立可用的完整真实光线形式搜索；阶段预算覆盖有效优化工作，候选按同一采样/目标排序，材料与光阑操作单独计量 | v8 外层框架可复用，策略需复验 |
| R4 | 经共享 Core 验证后再接入三/五阶快速筛选、模型玻璃及连续光阑；每项都有独立精度与边界证据 | 尚未实现，不能写成现有能力 |
| R5 | 冻结新版本，以全部原协议和留出集验收；保留 v4/v6/v7/v8 结果，并明确外部精度和原生平台证据 | 尚未执行；v8 失败结论不变 |

本次不对验收集反复改权重，也不根据官方展示视频推定自有算法已经具备同等能力。下一轮先做 R1/R2，验证内层机制后再回到形式搜索成功率。
