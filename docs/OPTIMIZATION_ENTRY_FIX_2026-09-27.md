# 正式优化入口与无效评价修复（2026-09-27）

本次落实 [路线复核](OPTIMIZATION_ROUTE_LITERATURE_REVIEW_2026-09-27.md) 中首先需要处理的正式优化问题。修改正式 Core、Application 及相关测试；实验室搜索编排、光学公式、采样精度和像质门槛未改。工作区原有的界面、主题和其他改动继续保留。

## 已实现

1. **原处方进入优化。** Core 新增曲率参数适配，平面保持零曲率，正负曲率可以通过平面转换。默认局部范围为 `±max(0.025, 2 × |起始曲率|)`（1/mm），不是几何可行域；未添加可编辑范围界面。曲率写入复用 `OpticalSurface` 的几何同步，不产生另一个追迹模型。创建变量和写回相同坐标不改变原半径表示。既有零/负厚度也不会在入口被夹到 0.001 mm。
2. **逐光线默认目标。** 单面动作和空评价函数的多变量动作使用 Core 现有的默认 RMS 向导操作数。保留各视场、波长和光线的有符号 X/Y 残差；临时定义不填充或覆盖编辑器。用户已配置的有效评价函数保持原语义。
3. **无效评价有明确原因。** Core 操作数适配及应用独立评价删除 `1_000_000` 后备值，以 `OptimizationEvaluationException` 保留操作数和原因。原始评价表仍可显示错误；进入优化后不能把错误当成普通数值。
4. **DLS 拒绝无效步。** 无效起点报错；有效起点的无效试探步提高阻尼后重试。差分失败可探测相反方向或缩短扰动；如果所有探测均失败，报错而不是写入零导数。固定变量的零列与不可用导数分别处理。其它算法目前遇到无效评价中止，由应用事务恢复文档。
5. **异常不冒充收敛。** 非有限梯度与残差平方和溢出明确失败。DLS 版本为 `damped-least-squares/2`，动量梯度下降为 `momentum-gradient-descent/2`；算法目录与兼容名称不变。

正式入口的独立评价按同一份快照、操作数和变量绑定执行，结果仍只在正式文档成功写回及应用事务完成后发布。没有引入新的包、外部计算或实验室光学后备公式。

## 已执行验证

- 默认 Debug 输出优化及相邻数值子集 **112/112** 通过，零失败、零跳过，其中新增优化路线回归 **23 项**。范围包括真实光线失败、五种公开算法的初始失败回滚、原处方保留、曲率跨符号、默认光学目标下降与提交后 Core 重算一致、有界最小二乘、精确约束、独立并行雅可比、拾取和相邻光学分析。
- 因严格操作数适配也被公差调用，最终扩大到公差和 Monte Carlo 后共 **146 项：144 通过、2 失败、0 跳过**；上述 112 项包含于其中并再次全部通过。两个失败为 `TolerancingWorkflowTests.ImageSpacingCompensatorChangesRealOpticalCriterion` 和 `TolerancingWorkflowTests.MonteCarloRowsAndStatisticsUseActualCriterion`，与既有 [商业审计](COMMERCIAL_CODE_AUDIT_2026-09-27.md) 的公差显示精度失败一致；后者误差同为 `0.0008666000000000009`，原阈值 `2e-6` 不变。本轮未修复这些显示/DTO 问题，不宣称扩展检查全通过。
- 初次运行被 VSTest 本机套接字权限阻止；获准运行后，一组错误表面引用的测试先在快照校验层被拒绝，未触及目标路径。已将样例改为有效处方格式中的真实失追迹，最终 112 项全部通过。未通过修改数值容差消除失败。
- 最终正式解决方案默认 Release 输出构建 **0 警告、0 错误**；Debug 定向测试同时成功构建 Core、Application、App 与主测试项目。未用替代输出目录规避运行文件，桌面 Debug/Release 二进制均已更新。
- 本次八个源文件的 `dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过。格式工具首次也受到本机管道权限限制，获准后格式化与最终只读检查完成。

定向测试命令：

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPriorityReliabilityTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~RadiusSolveTests|FullyQualifiedName~SurfaceSolveTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~OptilandParityTests.Optimization|FullyQualifiedName~OptilandParityTests.Optimizer|FullyQualifiedName~OptilandParityTests.LeastSquares|FullyQualifiedName~OptilandParityTests.MomentumGradient|FullyQualifiedName~Toleranc|FullyQualifiedName~MonteCarlo' \
  --logger 'trx;LogFileName=optimization-regression.trx' \
  --results-directory artifacts/validation/optimization-route-20260927 /m:1 /nr:false
```

本地最终扩展检查原始结果：`artifacts/validation/optimization-route-20260927/optimization-regression.trx`（146 项）。去掉命令末尾两个公差/Monte Carlo 过滤项即为 112 项子集。只运行既有的普通数值/优化回归，未新增或生成 Optiland 对照。

最终构建命令：`dotnet build OptilandWorkbench.slnx --no-restore -c Release /m:1 /nr:false`。

## 验证范围与后续问题

本轮没有执行正式全量、实验室全量、宽角完整搜索或新性能基准；112 项子集与 146 项扩展检查均为定向结果，不替代文档中注明时间和范围的历史全量基线。没有执行 Zemax 基线完整性审计、外部数值比较或截图复核；OpticStudio 2026 R1 原始捕获不变。

这些测试证明新入口和错误处理的行为，不能证明宽角结构已达标。仍待验证：实际光学导数的步长/瞄准敏感性、约束投影梯度、正式与实验室等价问题的收敛对照，以及玻璃/光阑/拓扑覆盖、终态归档多样性和独立用途验收。默认逐光线目标没有自动加入焦距、F 数、畸变、MTF 或公差条件；需要这些条件的任务仍应配置相应评价函数。
