# GRIN 材料、顺序追迹与原生快照

> 历史阶段报告：保留当时的实现、命令、测试与失败结果，不代表当前产品验收。最新状态见 [当前状态](CURRENT_STATUS.md)，可执行构建入口见 [构建与发布](BUILD_AND_RELEASE.md)；旧独立实验室架构和命令已被统一内置模块取代。

本文保留材料/追迹接入阶段的实现和验证范围；后续已补充限定共轴系统的一阶传递、瞳孔/瞄准及应用操作数工作流，见[当前一阶接入](GRIN_PARAXIAL_AIMING_2026-10-03.md)。下文尚未接通项按本阶段当时状态阅读。

2026-10-03。在[连续传播基础](GRIN_CONTINUOUS_PROPAGATION_2026-10-03.md)上，Gradient 1～4 现已作为不可变空间材料接入正式顺序追迹和 STAROPT 保存。当前入口是共享 Core 的显式光线 API；桌面材料编辑、近轴瞄准及 GRIN 评价函数尚未接通。**操作数数量仍为 302/383 项受限执行、81 项兼容保留**，本批不增加“已实现”数量。

## 已实现

- [GradientIndexMaterial](../src/OptilandWorkbench.Core/Materials/GradientIndexMaterial.cs) 保存名称、不可变折射率分布、有限正数路径预算和完整积分设置。四种分布当前均无色散、无吸收；系数及积分精度使用镜头长度单位。未知自定义分布明确拒绝，避免无法保存的材料进入处方。
- [正式表面追迹](../src/OptilandWorkbench.Core/Domain/OpticalSurface.StateTracing.cs) 在进入材料时绑定入口面的体坐标系，逐光线保留介质状态。体内段调用共享连续积分器，使用实际弧长和光程；到达目标面的曲线交点后，以两侧局部折射率、实际入射方向和目标面的局部法线执行既有反射/折射、孔径及标量膜层作用。
- 反射及全反射保留原体坐标；只有透射到下一 GRIN 材料才绑定新的入口坐标。空间分布坐标不随出口面移动。单面 `TraceRay` 接受显式入射体坐标，也可直接接续上一次返回的介质状态；缺少体坐标时明确报错。
- 曲线命中点用于物理孔径、诊断延续和圆孔净空；像面入射终点可在折射前返回。弯曲后的方向、相互作用类型、实际路程和累计光程继续通过既有类型化样本输出。求解器额外返回精确局部积分位置，法线求值采用相对坐标转换。
- 批处理遇到 GRIN 段或 GRIN 入射界面时转入同一正式标量表面实现；出射后的均匀段仍可批处理。取消使用当前计算令牌。达到路径预算或切向接触而未得到有效出口时终止光线，不产生有效像点。
- 材料配置和分布不可变；替换材料会触发既有光学修订及缓存脱离。相同快照、修订和请求可使用既有有界追迹缓存，没有另建按分析名称或材料名称共用的缓存。

## 保存契约

[严格材料快照](../src/OptilandWorkbench.Core/Serialization/ComponentSnapshot.GradientIndex.cs) 使用 `kind=gradient_index`。容器 2、工程负载 4、顺序 schema 6 不变；旧读取器不认识该材料 kind 时拒绝加载。

| 字段 | 内容 |
| --- | --- |
| Text | 必须且仅有 `name`、`profile`；profile 为 `gradient1`～`gradient4` |
| Gradient 1 系数 | `n0, nr2, nr1` |
| Gradient 2 系数 | `n0Squared, nr2, nr4, nr6, nr8, nr10, nr12`；基准是 n² |
| Gradient 3 系数 | `n0, nr2, nr4, nr6, nz1, nz2, nz3` |
| Gradient 4 系数 | `n0, nx1, nx2, ny1, ny2, nz1, nz2` |
| 每种模型必需的积分设置 | `maximumPathLength, maximumStep, minimumStep, positionTolerance, directionTolerance, opticalPathTolerance, relativeTolerance, surfaceTolerance, maximumAttempts` |

数字必须有限，参数需符合模型和积分器约束，尝试次数必须为 1～1000000 的整数。缺项、未知字段、非法名称/模型、子组件及非法精度/路径预算均拒绝；不填默认值救活损坏负载。运行时绑定的体坐标材料不能作为独立材料快照保存。Clone、OpticSnapshot 和实际 STAROPT 文件往返保留四种分布及全部非默认积分设置。

## 明确未完成

1. 桌面创建/编辑、优化变量、近轴传播与光线瞄准、一般分析工作流尚未接通。无空间位置的折射率查询抛出明确错误，近轴入口也显式拒绝；不会把基准 n 当均匀玻璃继续计算。保存可用不代表完整 GUI 打开及分析流程已完成。
2. 曲线节点还未进入场景 DTO 和缓存，故材料不允许 `RetainPath=true`，可视化入口明确阻断；不能用端点之间的直线展示体内路径。底层独立积分器仍可按原接口返回采样节点。
3. 连续偏振基矢运输未实现，携带偏振矩阵或要求偏振诊断的追迹明确拒绝。当前标量膜层接口接收真实局部 n 和入射方向，但这不等于完整 GRIN Jones/偏振强度链已完成。
4. 非顺序最近交点搜索仍是均匀介质实现，GRIN 场景和初始介质明确拒绝。无限物方 GRIN 尚无有界介质入口约定，明确拒绝。
5. ZMX 原生 GRIN TYPE、参数与步长映射未验证；不推测格式。ZMX/CODE V/OSLO 等经 `SequentialLensDocument` 的处方导出拒绝 GRIN，避免丢失空间系数。STAROPT 是本批已支持的文件往返路径。
6. `I1…6 GT/LT/VA`、`GRMN/GRMX/DLTN/LPTD` 本批仍为兼容保留。尤其 LPTD 针对官方 GRIN 5，本批 Gradient 1～4 不能替代它。
7. 继承底层积分器的边界：非零 Gradient 1 径向一次项轴上梯度不可微；弧长步长不是 Zemax 局部 Z 的 Δt；有限步长括区不保证任意振荡场的全局最近根或所有内部切点。原生 GRIN 数值捕获仍缺失。

## 验证

[新增运输测试](../tests/OptilandWorkbench.Tests/GradientIndexTransportTests.cs)共 29 项。覆盖横向梯度悬链线精确解、局部界面 n、曲面出口法线、独立 Fresnel 标量膜层参考、反射和全反射回程、体坐标旋转平移、弯曲交点孔径、入射像面终点、批处理并行一致性、缓存共享/失效、四种模型文件往返及非法数据拒绝。解析公式只在测试中提供独立参考。

初步定向测试 61/61，通过补充曲面出口、膜层及非法负载/配置检查后为 65/65（原积分器 36 + 本批 29）。最终默认 Debug/Release 输出重新构建，合并回归各 **2728/2728**（第二十九批 2560 + GRIN 积分 36 + 本批运输 29 + 相邻非顺序 103），零失败、零跳过、零编译警告/错误。两个配置包含相同用例，先前 2560 项全部保留；本次完整合并运行包含基础阶段最后的大坐标修复。C# 格式检查和 `git diff --check` 通过，29 份指定 Zemax 资产与 HEAD 字节一致。命令过滤器、TRX、日志、源文件/文档和默认二进制哈希见[本批验证清单](../artifacts/validation/grin-material-transport-20261003/verification.json)。

本批区分提交的 Zemax 基线完整性、当前 Workbench 回归与独立解析验证。未新增原生 Zemax 捕获，不认证 GRIN MFE 数值等价，不以截图或旧 Optiland 历史数据替代光学验证；也不宣称全产品发布门禁通过。
