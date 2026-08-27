# 优化算法真实性与兼容策略

## 当前公开算法

界面和 `OptimizerCatalog.Names` 只公开代码中真实执行的算法。运行结果保存规范名称和版本，不能使用请求别名冒充实际算法。

| 规范名称 | 版本 | 当前实现 | 诊断信息 |
|---|---|---|---|
| `Damped Least Squares` | `damped-least-squares/1` | 带阻尼的残差雅可比最小二乘步进和精确约束处理 | 停止原因、梯度范数、函数评价次数 |
| `Nelder-Mead` | `nelder-mead/1` | 单纯形反射、扩张、收缩和缩小 | 停止原因、函数评价次数 |
| `Coordinate Pattern Search` | `coordinate-pattern-search/1` | 有界坐标方向模式试探和步长收缩 | 停止原因、函数评价次数 |
| `Momentum Gradient Descent` | `momentum-gradient-descent/1` | 有限差分梯度、动量方向和回退步长 | 停止原因、梯度范数、函数评价次数 |
| `Greedy Random Perturbation` | `greedy-random-perturbation/1` | 固定种子的逐坐标随机扰动，只接受更优候选 | 停止原因、函数评价次数、随机种子 |

`Greedy Random Perturbation` 是启发式局部/区域搜索，不宣称具有全局最优保证。

## 旧名称兼容

旧工程或外部调用仍可传入以下名称，但每次运行都会显示并记录兼容警告；返回结果的 `Algorithm` 始终是右侧真实名称。

| 旧名称 | 实际执行 |
|---|---|
| `LM / DLS`、`Least Squares` | `Damped Least Squares` |
| `Powell`、`COBYLA`、`Orthogonal Descent` | `Coordinate Pattern Search` |
| `BFGS`、`L-BFGS-B` | `Momentum Gradient Descent` |
| `Differential Evolution`、`Dual Annealing`、`Basin Hopping` | `Greedy Random Perturbation` |

兼容别名不会出现在算法选择器、Ribbon、能力清单或宣传文案中。它们只用于旧调用迁移，后续可按主版本策略移除。

## 尚未实现

以下算法在真实、独立实现和数值测试完成前不得出现在产品 UI 或功能宣传中：

1. 带强 Wolfe 线搜索、曲率更新保护和收敛诊断的 BFGS。
2. 具有投影梯度、活动边界和有限内存校正对的 L-BFGS-B。
3. 使用线性近似约束和信赖区域的 COBYLA。
4. 具有种群、变异、交叉和选择过程的差分进化，以及独立的 CMA-ES。
5. 具有接受概率与温度调度的双重退火，以及局部求解器驱动的盆地跳跃。
6. 使用增益比调整信赖域的 Levenberg-Marquardt。

建议实现顺序为：Wolfe BFGS → 信赖域 LM → L-BFGS-B → COBYLA → DE/CMA-ES。每个算法必须先通过标准基准函数、有界问题、约束问题、固定种子复现、停止条件和函数评价计数测试，再加入公开目录。

## 运行记录约定

每次优化至少记录：

- 实际算法规范名称与实现版本；
- 停止原因和迭代次数；
- 函数评价次数；
- 最终梯度范数（算法可计算时）；
- 随机种子（随机算法适用时）；
- 兼容别名警告。

取消或异常仍由应用事务回滚，不生成伪造的“完成”结果。
