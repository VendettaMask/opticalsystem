# 优化与自动设计路线：文献和实现交叉复核（2026-09-27）

后续实施状态：用户批准修改后，第 1 节所述正式入口、默认残差和失败处理已进入 [优化入口修复](OPTIMIZATION_ENTRY_FIX_2026-09-27.md)。本文保留修复前的研究证据和当时的验证范围；下文“尚未实施”描述研究完成时状态，当前进度以实施记录为准。

本轮范围从初始结构实验室扩大到正式程序的参数化、评价函数、局部优化、光线瞄准、口径、材料和结构搜索，以及最终验收。依据用户“先不要改代码，找对方向”的要求，仅阅读实现和资料，新增本研究记录；没有修改产品或测试代码，没有运行新搜索、光学重算、构建或测试。检查时 HEAD 为 `20e5e639`；工作区原有的界面、主题和其他文档改动不属于本轮。

本文接续 [初始结构方向复核](INITIAL_STRUCTURE_DIRECTION_REVIEW_2026-09-27.md)，不改变算法版本、测试数量或验收结论。以下明确区分静态代码事实、文献结论、待验证的推断和建议。

**当前判断：首先审查优化问题如何被表达，再比较求解器。** 共享光学 Core 的路线应保留；但正式优化入口可能改变起始处方、限制可达参数域，其默认残差表达也与实验室不同。局部数值可靠性、外层搜索覆盖、最终产品指标需要分别验证，不能由一次总分下降共同证明。

## 1. 正式优化中新增的具体发现

### 1.1 平面变量会先被改成正半径，变量范围又限制了后续形状

**已确认的静态行为。** `OptimizeMarkedVariables` 中，平面若标记为半径变量，会先执行 `SetSurfaceRadius(surface, 40)`，然后按当前半径建立局部上下界。这个平面的实际初始半径变成 +40 mm，允许范围为 +20 至 +60 mm；一般正、负半径变量也分别被限制在各自符号一侧。

这意味着“把原处方的某面设为变量”同时隐含了改变处方和选择局部结构范围。若产品有意提供窄范围细化，应明确这一含义；该入口不能直接视为允许通过平面转换曲率符号的通用优化。撤销/异常恢复存在，但不能消除优化开始前处方已改变的事实。

单面 `OptimizeSurfaceRadius` 也会把平面改为 +40 mm，但其范围生成方式不同，不能把多变量入口的 +20 至 +60 mm 范围套用到单面动作。

代码：[`WorkbenchRuntime.Optimization.cs`](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs)，`OptimizeSurfaceRadius`、`OptimizeMarkedVariables`。

**建议，未实施：** 首先定义“设置变量不改变处方”的契约，并检查处方→参数向量→处方的往返一致性。研究用曲率表达平面附近变化，并让物理范围、局部搜索范围和结构符号限制分别可见。不能简单取消所有边界；几何可行性仍需由正式 Core 判断。上轮发现的实验室统一曲率限幅也应纳入同一类入口检查。

### 1.2 空评价函数的后备路径只提供一个总 RMS 残差

**已确认的静态行为。** 多变量入口在没有启用的有效评价行时，只添加一个 `RMS spot radius` 操作数；单面入口同样使用一个 RMS 操作数。因此，在多变量后备路径中，目标残差的雅可比只有一行，正则化前的 Gauss–Newton 目标近似最多秩一。

这是残差表达的问题，并不证明算法无法下降，也不证明最终像质必然差。相同的平方和目标，采用逐光线残差或先合并成一个 RMS，可能产生不同的 Gauss–Newton 局部模型；实际差别需在相同采样、参考点、权重和变量域下测量。

**已存在的能力：** `MeritFunctionCatalog.CreateDefaultRmsSpot` / `CreateFromWizard` 已能生成逐光线像差操作数，实验室真实光线阶段也已有坐标残差。因此不应把这个问题描述成整个产品缺少逐光线优化。需要核对的是不同入口采用什么默认问题，以及用户是否知情。

代码：[`WorkbenchRuntime.Optimization.cs`](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs) 的 `enabledMeritRows.Length == 0` 分支；[`MeritFunction.cs`](../src/OptilandWorkbench.Core/Optimization/MeritFunction.cs) 的 `CreateFromWizard`、`AddRayAberrationOperands`。

### 1.3 非有限梯度与满足梯度容差共用一个终止标签

**已确认的静态分支，尚未复现触发样例。** 正式 DLS 在约束误差满足阈值时，将 `!double.IsFinite(gradientNorm)` 与梯度足够小放入同一条件，并返回 `GradientTolerance`。动量梯度下降也有类似分类。

非有限数值与正常驻点是不同状态。这个分支需要后续构造数值异常样例，检查最终结果、界面和调用方如何解释终止原因。当前证据不足以声称它造成了某次宽角失败，也不能根据该标签断言已经达到局部极小值。

正式 DLS 当前计算的是目标的未投影梯度范数；有活动边界或等式约束时，它也不能独自构成约束问题的一阶最优性证据。

代码：[`NumericalOptimizers.cs`](../src/OptilandWorkbench.Core/Optimization/NumericalOptimizers.cs)，`DampedLeastSquaresSearch.Run`、`MomentumGradientDescentOptimizer`。

### 1.4 评价失败被替换为固定大数，可能形成假的平坦区域

**已确认的静态行为。** 多变量入口的 `EvaluateSpotMerit` 将非有限光斑值或非取消异常替换为 `1_000_000`；独立评价器的 RMS 分支采取相同策略，启用评价行时也会把错误或非有限值替换为该常数。`OptimizationProblem.BuildEvaluation` 随后把这个有限数作为普通操作数值装配残差，原始失败状态没有随值传递给求解器。

**条件性推断，未运行复现：** 若 RMS 后备路径的起点及差分探测点均失败，所有返回值相同，数值导数可能全为零；无其他约束时，DLS 就可能以 `GradientTolerance` 结束。较大的罚值能让已有效的候选避开失败区域，但不能为全失败邻域提供可靠恢复方向，也不能代替有效性标志。

应将有效性、失败原因、可用残差与终止原因一起纳入契约验证。具体采用拒绝无效步、缩短步长还是正式 Core 支持的可行性恢复，要由实际失败样例决定；不能在实验室添加近似追迹填补这些值。

代码：[`WorkbenchRuntime.Optimization.cs`](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs) 的 `EvaluateSpotMerit`、`EvaluateIndependentValues`；[`OptimizationFramework.cs`](../src/OptilandWorkbench.Core/Optimization/OptimizationFramework.cs) 的 `BuildEvaluation`。

## 2. 局部求解器与光学评价之间的边界

### 2.1 共用光学引擎，不代表共用优化行为

| 层面 | 正式优化 | 初始结构实验室 |
|---|---|---|
| 光学计算 | 正式 Core | 同一正式 Core |
| 局部数值方法 | DLS，有限差分，增广最小二乘/SVD；具有等式约束求解路径 | 有界信赖域；真实光线阶段用正则化步，几何阶段用 dogleg |
| 差分和缩放 | 自适应差分及变量缩放 | 有效性保护的差分、雅可比列缩放、受保护的割线更新 |
| 步长控制 | 接受后降低阻尼、拒绝后提高阻尼 | 比较实际下降与模型预测，调整信赖域 |
| 问题表达 | 由入口和评价函数编辑器决定 | 实验室装配规格、几何和逐光线残差 |

表格仅描述查到的实现，不评定哪套更优。实验室的独立数值求解器不等于第二套光学引擎，也不违反共享 Core 要求。应优先统一可比较的输入和诊断记录；没有证据支持现在就合并或替换两个求解器。

代码：[`NumericalOptimizers.cs`](../src/OptilandWorkbench.Core/Optimization/NumericalOptimizers.cs)、[`FlatStartLocalSolver.cs`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartLocalSolver.cs)、[`BoundedTrustRegionLeastSquares.cs`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/Optimization/BoundedTrustRegionLeastSquares.cs)。

### 2.2 “存在连续搜索残差”的标志不保证目标处处光滑

**已确认：** 实验室 `HasContinuousSearchResiduals` 的设置依赖几何/口径更新和所需光线坐标是否有效；残差同时包含 `max(0, excess)`、最坏光线量以及随光线包络更新的口径。这些条件不能证明数学意义上的连续可微。

**待验证的推断：** 活动约束、决定最大口径的光线或最坏光线发生切换时，局部导数可能变化；瞄准的内层迭代精度也可能影响外层有限差分。当前没有新的导数实验，不能宣称现有雅可比错误。

应选择光学样例进行差分步长扫描，比较不同方向的直接残差变化与雅可比预测，检查边界两侧、瞄准收敛精度和重新计算雅可比后的结果。已有整次评价的有限差分会包含部分口径变化效应，不能直接套用“自动微分漏掉边界项”来判定本项目有同样缺陷。

代码：[`FlatStartDesignProblem.cs`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartDesignProblem.cs)，`Evaluate`。

### 2.3 归档的起始家族不完全等于最终结构家族

**已确认：** `FlatStartCandidateArchive.FamilyKey` 包含面数、实际光阑位置、实际玻璃和 `candidate.Lineage.InitialForm`；数值结构距离只在相同家族内比较。处方指纹会去掉完全相同的候选，但不同起始标签仍可能让相近终态占据不同家族名额。

这说明家族定义混合了来源和终态，尚不证明已有结果中重复占位的数量。后续应对既有档案量化：按起始标签和最终处方描述分别统计多样性，检查重复保留和误合并。来源标签继续用于追溯；光焦度等光学结构描述若被采用，必须调用正式 Core。

代码：[`FlatStartCandidateArchive.cs`](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatStartCandidateArchive.cs)，`Select`、`FamilyKey`、`Distance`。

## 3. 本轮补充资料及其适用边界

下表记录核查的版本和阅读深度。论文方法是对照依据，不能当成本项目已实现或已经通过验证的能力。

| 文献/官方资料 | 阅读范围与核心信息 | 对本项目的启发及边界 |
|---|---|---|
| [Antonov 等，LDG-EA，2026-01-29 预印本 v1](https://arxiv.org/html/2601.22075v1) | 全文。按曲率符号、玻璃编号描述结构，学习搜索预算分配；验证固定六片双高斯。 | 借鉴真实结构覆盖和离散材料搜索。其 28° 全视场、最多 100 线程的实验不能直接预测本项目 80° 全视场的效果或耗时；最好结果也未超过精调参考镜头。 |
| [Teh 等，Automated design of compound lenses with discrete-continuous optimization，SIGGRAPH Asia 2025](https://imaging.cs.cmu.edu/automated_lens_design/index_files/paper.pdf) | 作者公开全文。连续优化结合插入、删除、拆分、合并元件；用近轴投影帮助结构变更。 | 可研究后续拓扑探索；仍需要较好的初始镜头，示例单色且没有显式光阑，不能直接替代本项目带物理光阑的从平板搜索。 |
| [Teh 等，Aperture-aware lens design，SIGGRAPH 2024](https://imaging.cs.cmu.edu/aperture_aware_lens_design/index_files/paper.pdf) | 作者公开全文。分析口径、遮挡等造成有效光线积分域变化时的梯度问题。 | 支持检查口径、通光量与导数之间的关系。本项目固定瞳采样、口径扩展及严格通过率策略与论文不同；尚无证据要求直接移植其重参数化方法。 |
| [Gao 等，Exploring Quasi-Global Solutions to Compound Lens Based Computational Imaging Systems，2025-02-21 修订版 v2](https://arxiv.org/html/2404.19201v2) | 全文。OptiFusion 搜索多样初始系统，后续联合优化光学与图像重建。 | 借鉴多种光阑形式与多次搜索。其近似色散不能进入本项目运行评价；重建后的图像质量也不能作为纯光学像质的证明。 |
| [Kononova 等，Addressing the Multiplicity of Solutions in Optical Lens Design as a Niching Evolutionary Algorithms Computational Challenge，2021](https://arxiv.org/html/2105.10541v1) | 全文。固定玻璃和间隔的六曲率三片系统，研究多极值搜索和结果复核。 | 支持多样性保留及驻点诊断。有限共轭、固定变量集合的结论不能直接外推到当前自由玻璃、片数与宽角问题。 |
| [van Turnhout、Bociort，Instabilities and fractal basins of attraction in optical system optimization，Optics Express 2009](https://home.imphys.tudelft.nl/~fbociort/OpEx_turnhout_1st.pdf) | 作者公开全文。展示 DLS 阻尼和微小初值变化对吸引域的影响。 | 应用相同起点的微扰和阻尼对照检查稳定性；其低维示例不证明当前阻尼参数错误。 |
| [Carneiro de Albuquerque 等，Multi-objective approach for the automatic design of optical systems，Optics Express 2016](https://pubmed.ncbi.nlm.nih.gov/27136851/) | 核查作者摘要和出版信息，未据此声称读过全文。把像质、公差和复杂度分开，结合探索与 DLS 细化。 | 支持检查单一总分隐藏的工程取舍；本文不据摘要复现其具体算法。 |
| [Ansys：真实玻璃替换优化](https://optics.ansys.com/hc/en-us/articles/42661830811795-How-to-use-glass-substitution-to-optimize-the-glasses-in-your-optical-design) | 官方全文。目录玻璃替换后重新优化，材料选择属于离散搜索。 | 修正“必须先有连续模型玻璃”的优先级判断。可以先验证代表性真实玻璃集合和替换后的充分细化，再决定模型玻璃是否值得开发。 |
| [Ansys：Enhanced Ray Aiming 与向导](https://optics.ansys.com/hc/en-us/articles/42661958377491-Introduction-to-Enhanced-Ray-Aiming-and-Ray-Aiming-Wizard) | 官方全文。说明大视场瞄准失败、错误路径和稳健性处理；文章包含历史版本示例。 | 应区分物理不可达与数值瞄准失败。正式 Core 已有瞄准能力；不能把问题简化为再添加一个开关，也不能把历史示例当成 2026 R1 通用默认设置。 |
| [Ceres：非线性最小二乘](https://ceres-solver.readthedocs.io/latest/nnls_solving.html)及[数值导数](https://ceres-solver.readthedocs.io/latest/numerical_derivatives.html) | 官方方法与接口文档。讨论 LM/dogleg、边界、预测下降、终止条件和差分精度。 | 用于建立诊断和实验标准，不能只凭小步长就确认极小值。不是引入 Ceres 或更换纯 C# 技术路线的建议。 |
| [Ansys：Contrast Optimization](https://optics.ansys.com/hc/en-us/articles/42661802617491-Optimizing-for-MTF-performance-using-Contrast-Optimization) | 官方全文。讨论残差信息量、从差结构直接优化 MTF 的困难，以及最终 MTF 的另行评价。 | 支持分阶段像质目标。正式 Core 已有相关评价能力，下一步应检查调用与验收安排，不能声称需要从零实现所有分析。 |

## 4. 其他部分需要一起复核的路线

| 环节 | 当前判断 | 下一轮需要回答的问题 |
|---|---|---|
| 共享 Core 与精度验证 | 保持现有计算权威；论文中的近似追迹/色散不能在实验室充当后备引擎。 | 已有基线覆盖哪些条件？宽角、强折转、色散或特殊几何样例还缺哪些独立证据？ |
| 物理光阑、瞄准与自动直径 | 相关能力已经存在；要检查它们与优化变量、残差的共同变化。 | 内层瞄准是否足够稳定？口径更新是否改变约束活动集合？失败是否分类明确？ |
| 连续变量与几何约束 | 已发现入口处方变化和隐式参数域；最优先验证。 | 已知有效处方能否原样进入求解？边界是否排除有用解？ |
| 目标与采样 | 正式后备路径和向导残差不同；实验室不同密度阶段的权重问题见上轮记录。 | 采样加密是在提高积分精度，还是同时改变不同目标的相对权重？ |
| 玻璃与光阑 | 已有邻域操作，但上轮证实有限预算中的根结构覆盖不足。 | 分层覆盖和换玻璃后的局部预算，分别贡献多少成功率？ |
| 片数、胶合、非球面 | 需要区分搜索不到与所选设计族表达能力不足；本轮没有证明目标不可实现。 | 在相同规格下，有无可核查的全球面参考处方？增加自由度前后效果如何？ |
| MTF、色差、畸变、照度与公差 | 名义光斑和通光率不足以概括用途；本次既有宽角结果仅为单色。 | 为选定用途明确波长、像面/畸变定义、MTF 频率、照度和制造条件，哪些作为搜索目标、哪些用于最终验收？ |
| 归档、排序与预算统计 | 需要分别报告可行性、像质、结构差异和运行成本。 | 最优总分是否符合用户所说的“最好”？同预算是否意味着相近的实际追迹成本？ |

此表不表示所有指标都要立即加入同一个评价函数。应先固定用途和一个可复现的小规模问题，再逐层增加难度；否则一次改变光学目标、采样、变量和算法，结果仍无法归因。

## 5. 建议的实验顺序：尚未实施

1. **入口与参数域。** 固定包含平面、正负曲率和接近几何边界的代表处方，验证进入优化前后不变；记录所有实际边界和投影。先解决问题定义是否一致，再测收敛速度。
2. **残差与导数。** 对相同 Core 快照、采样、参考点、目标和权重，比较 RMS 后备表达与逐光线表达；扫描差分步长，检查方向导数、瞄准精度、口径及活动约束切换。无效评价必须保留原因，不能用近似光学结果填补。
3. **局部收敛。** 为正式和实验室求解器提供等价问题、相同范围与评价预算，从可核查处方及受控扰动出发。记录目标贡献、约束误差、活动边界、投影/约束切空间梯度、步长、预测/实际下降及终止原因；先证明能保留和改善好结构。
4. **外层探索。** 冻结相同起点集合后比较预算策略；另做全局同成本、多种子比较。独立改变曲率形式、光阑、真实玻璃覆盖，按最终处方统计多样性。玻璃替换后必须预留重新优化预算。
5. **结构扩展。** 当前述结果能区分局部求解失败与结构覆盖不足时，再分别研究 SPC、元件增删/拆合、胶合或非球面。任何新的光学投影与评价能力先进入正式 Core，不能复制论文近似公式到实验室。
6. **独立验收。** 对最终冻结处方做更密的场/瞳/波长评价及用途相关 MTF、畸变、照度与公差检查。优化总分、可追迹和程序正常结束都不替代验收。

每步应先确定预期证据和停止条件。若第 1–3 步尚不稳定，增加全局种群、GPU、神经网络或迭代次数会使原因更难判断；若局部优化已稳定且覆盖仍不足，再投入外层搜索算法才有可解释的依据。

## 6. 当前证据的边界

- 本轮新确认的是代码路径及其数学结构，没有测量上述行为造成的像质损失，也没有证明替代方案会更快。
- 新论文提供方法和限制；没有一篇构成当前 50 mm、F/2.8、80° 全视场、限定长度和球面条件的直接成功证明。
- 连续模型玻璃、自动微分、全局进化和拓扑搜索均属可选研究方向，不能据文献热度排列实现优先级。
- `123456.ZMX` 的 Zemax OpticStudio 2026 R1 捕获仍只验证该文件、该设置和该版本。本轮未执行基线完整性审计、Workbench 重算、外部数值比较或截图复核；不更新通过数和构建日期。
- 下一阶段的路线是：保持共享 Core，先核查问题表达和局部数值可靠性，再验证离散结构搜索，最后按用途独立验收。所有实验及修复均尚未实施。
