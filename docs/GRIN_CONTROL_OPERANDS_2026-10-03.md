# GRIN 材料控制操作数

> 历史阶段报告：保留当时的实现、命令、测试与失败结果，不代表当前产品验收。最新状态见 [当前状态](CURRENT_STATUS.md)，可执行构建入口见 [构建与发布](BUILD_AND_RELEASE.md)；旧独立实验室架构和命令已被统一内置模块取代。

本页保留 GRIN 材料控制阶段的 323 项/2869 项验证记录；当前实现与验证见[膜层约束后续记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03。在共享空间材料、连续追迹及一阶接入之后，新增 **21 个本地受限执行代码**：`I1GT/I1LT/I1VA` 至 `I6GT/I6LT/I6VA`、`GRMN`、`GRMX`、`DLTN`。注册表仍为官方 2026 R1 名称中的 383 项，其中 **323 项受限执行、60 项兼容保留**（53 项已知功能待接通、2 项定义待核实、5 项官方 Unused）；另有 4 项本程序扩展。不将六点的测试实例或四种材料分布重复计为操作数。

## 计算与定义边界

[共享材料约束服务](../src/OptilandWorkbench.Core/Services/GradientIndexControlMetrics.cs)直接读取正式 `GradientIndexMaterial` 的入口局部空间场。没有单点均匀折射率替代、追迹近似或实验室专用公式。四种不可变分布目前无色散，波长依然必须有效。

| 项目 | 本地计算 |
| --- | --- |
| `I1…6 VA` | 前顶点、前端 +Y、前端 +X、后顶点、后端 +Y、后端 +X 的折射率 |
| `I1…6 GT/LT` | 同一位置的下限/上限约束；满足约束时 Value 等于 Target，贡献为零 |
| `GRMN/GRMX` | 上述六点的最小/最大值下限/上限；不代表整个体积内的极值 |
| `DLTN` | 毛坯轴向起止位置折射率的绝对差；不搜索非单调分布内部的驻点 |

六点横向距离取前后表面较大的**净半口径**。本地曲面点 Z 使用该横向位置处的正式矢高，后端再加中心厚度。DLTN 的毛坯前端为 `min(0,前表面边缘矢高)`，后端为 `中心厚度+max(0,后表面边缘矢高)`，两面各用自身净半口径；不把机械半口径或延伸区混入这个定义。

支持相邻、共轴、局部横轴对齐的标准球面/圆锥和平面边界；整个元件刚体平移或旋转不改变材料局部值。非球面、两边界相对偏心/倾斜、不一致顶点间距、无有限出口、无效表面/波长、非有限坐标和非正折射率明确失败，失败贡献为无穷。材料约束不依赖瞄准：Gradient 1 轴上有径向一次项时 n 可读，但追迹所需梯度仍不可微；Gradient 4 非旋转梯度可以取值，不能因此解除标量近轴限制。单点操作数只检查请求点，六点边界逐点检查全部六点；取消沿原调用链传播。

定义依据为官方[梯度控制操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_Index_Control_Operands.html)与[使用说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Using_Gradient_Index_Operands.html)，并交叉读取 [2026 R1.03 字母目录](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。这几个网页不是本项目已提交的原生数值捕获。特别是**本地曲面取点的 Z 约定、毛坯非圆/机械边界、原生 MFE 列和数值尚未实机验证**；不能据本地解析测试宣称 Zemax 等价。

## 编辑、保存与帮助

[评价函数接入](../src/OptilandWorkbench.Core/Optimization/MeritFunction.GradientIndex.cs)采用 Surf/Wave/Unused/Unused/Unused/Unused 六槽；Surf 为有后端边界的实际材料入口面，Wave 为正编号。原始槽位优先于派生字段。Application/桌面操作数编辑器可编辑前两槽，未用槽锁定，STAROPT 保存全部原始值、目标、权重及状态；旧兼容行不会被自动升级。

21 种 ZMX 行仍禁用、只读保留，原始整数/数据槽和目标权重不丢失。正式 GRIN TYPE/参数导入未完成，不能把材料导成普通玻璃后自动启用这些行。本程序创建的执行行可经应用服务编辑保存；I4VA 和 DLTN 已用于正式 DLS 厚度优化，迭代快照仍走共享 Core。

帮助保留“功能大类 → 操作数族 → 具体操作数”。18 个编号约束仍归在同一族，各项显示具体位置、边界方向和当前可计算状态；搜索“前端 +X”仅匹配 I3 的三种约束。支持状态和参数说明已随计算能力更新，不改变面板布局。

## 验证与剩余工作

新增 [70 项测试](../tests/OptilandWorkbench.Tests/GradientIndexOperandTests.cs)：四种分布 × 六位置的独立多项式/球面参考、21 种边界与原生保留往返、凹凸毛坯端点、非单调分布反例、圆锥与材料/口径更新、刚体姿态、无效输入、取消、完整应用保存和两项生产 DLS。初次定向检查发现帮助状态断言及测试夹具问题，并发现共用说明引起的点位搜索误匹配；修正后定向 **88/88**（本批 70 + 既有帮助/名称 18）通过。未放宽数值阈值。

默认输出 Debug/Release 合并回归各 **2869/2869**（2799 + 70），零失败、零跳过，构建零警告、零错误。格式和差异检查通过，29 个已提交 Zemax 基线文件保持原样。筛选与结果、383 项状态、剩余分类、基线完整性及文件哈希见[验证清单](../artifacts/validation/grin-operands-20261003/verification.json)。前阶段 [2799 项范围](GRIN_PARAXIAL_AIMING_2026-10-03.md)全部纳入。合并捕获曾为空白，已作为无效图片另存；独立 Skia 进程的两项帮助交互通过，重新生成并逐张复核明暗主题共 8 张有效图片，见[视觉复核记录](../artifacts/validation/grin-operands-20261003/visual-review.md)。

本阶段未完成的 Gradient 5 空间模型及色散已由第四十批补充；LPTD 单调性约束仍未完成。桌面 GRIN 材料创建、系数编辑和受支持系数优化变量已由第四十一批接通。其余未完成项目：原生 ZMX 映射及数值捕获；更一般的几何边界、偏振、曲线场景与非顺序传播，以及其它操作数的剩余模式。此次操作数可执行数量增加，不等于这些工作完成。

[383 项逐项清单](../artifacts/validation/grin-operands-20261003/operand-audit.csv)与[剩余分类](../artifacts/validation/grin-operands-20261003/remaining-by-category.csv)区分执行模式缺口、53 项功能待接通、2 项定义待核实和 5 项未用名称。没有新增原生捕获，也没有重生成冻结的 Optiland 对照。
