# GRIN 空间折射率与连续传播基础

2026-10-07 RA256 与单光线深入复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **4383 通过 / 1 失败 / 共 4384 项**，比较工具完整 Release **178 通过 / 2 个既有失败 / 共 180 项**，均零跳过；新增正式 **20/20**、工具 **16/16** 通过。正式导入/单光线/RMS/GRIN 定向两配置各 **153/153**，工具定向 Debug **54/54**。六份官方镜头原设置 132 项的旧快照与重新导入两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，已有 85 Pass 无回退；独立五文件 RMS 控制为旧 17 项加新 RA256 6 项，**23/23 Pass**，分开计数。六个完整边缘光瞳共 308808 条输入、2521932 个逐面结果，修复自动 STOP 后接纳/首次截断差异均归零；更高密度收敛、Relay 瞄准、衍射、其余 21 份镜头及发布门禁未完成，见[修复与证据](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。完整 Debug 和实验室本轮未重跑，2026-10-06 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜两配置各 **47/47** 保留历史范围。此前阶段计数不相加。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

本文保留 2026-10-03 连续积分基础阶段的实现及测试范围。后续已接入正式顺序显式光线追迹与 STAROPT 材料快照，最新范围见[材料与追迹接入](GRIN_MATERIAL_TRANSPORT_2026-10-03.md)；下文“尚未”均指基础阶段当时状态。

2026-10-03。本批补充正式共享 C# Core 的空间折射率场、自适应弯曲光线积分和目标表面交点求解。**尚未接入顺序处方、材料编辑/快照、ZMX 或评价函数；可执行操作数仍为 302/383，81 项兼容保留。** 原先入口方向近似及其过时的 `GrinPropagationModel` 别名没有被重新包装为真实 GRIN。

## 已实现的共享接口

- [空间折射率](../src/OptilandWorkbench.Core/Materials/SpatialRefractiveIndex.cs)：`ISpatialRefractiveIndex` 分别提供局部位置的折射率查询和折射率/解析空间梯度联合查询。四个不可变模型覆盖 Gradient 1、2、3、4 的指数分布公式，当前均无色散；波长仍须为有限正数。Gradient 2 的基准参数是 **n²**，不是 n。坐标及梯度采用镜头长度单位和其倒数。
- Gradient 1 非零径向一次项在轴上的折射率仍可读取，但其梯度不可微；连续传播明确拒绝该点，不用零梯度掩盖。官方轴上追迹约定尚未核实。其他模型支持轴上解析梯度。
- [连续传播](../src/OptilandWorkbench.Core/Propagation/GradientIndexRayIntegrator.cs)：用弧长 s 积分 `dr/ds=u`、`du/ds=(∇n−u(u·∇n))/n` 和 `dOPL/ds=n`，使用 Dormand–Prince 5(4) 自适应步进。位置、方向和光程分别控制局部误差，接受步后归一化方向；不以终点折射率乘弦长代替光程积分。
- 场坐标系与目标表面坐标系独立，支持平移和三个轴旋转；交点直接使用两坐标系的相对原点，避免经过大绝对坐标后丢失局部位移；积分在场的局部坐标系内进行，返回全局位置、显式传播方向、累计实际路程、累计光程和局部折射率。可选保留曲线采样节点，供后续场景接入使用。
- `TraceDistance` 传播指定弧长；`TraceToSurface` 沿曲线求 `z−Sag(x,y)=0`，复用正式 `IGeometry` 矢高及法线。检查步长中点并对符号变化区间细化，区分表面到达、切向接触、距离完成和到达路径预算但未找到交点。正/负 Z 传播均由光线方向决定。
- 试探步超出有限、正折射率定义域时拒绝并缩步。初始点无效、取消、最小步长精度不足、尝试次数超限、非有限几何或交点不收敛均明确失败；不截断折射率、不退回直线、不返回伪成功。交点细化也计入有界工作预算。

## 尚未完成的接入与边界

1. `ISpatialRefractiveIndex` 不是 `IMaterial`，当前不能经材料注册表或桌面处方加载。这一接口隔离防止已有均匀介质追迹误用单点折射率。下一步必须在正式顺序追迹中传递体坐标系，使用曲线出口及两侧局部 n 做界面相互作用，并贯通近轴/瞄准、偏振、吸收、场景曲线和缓存身份。
2. 材料快照、严格校验、编辑参数/优化变量、导入保存及正式评价函数还没有接入；`I1…6 GT/LT/VA`、`GRMN/GRMX/DLTN/LPTD` 本批均没有升级。不可把这些 Core 模型的存在当成对应操作数已完成。
3. GRIN 1～4 之外的分布及色散文件仍待实现。LPTD 的官方定义针对 GRIN 5，不能将 Gradient 3 的任意梯度罚函数命名为 LPTD。
4. 本积分器的 `MaximumStep` 为**实际弧长**上限；公开 Zemax 顺序 GRIN 文档的 Δt 为局部 Z 间隔。两者尚未建立导入映射，不能直接声称相同采样或默认值。
5. 当前求交是有限步长的符号括区方案，中点检查不能证明任意振荡场/高阶面形的全局最近根；未跨符号的内部切点可能未被发现。初始点或命中点的切向接触单独标记。目标矢高超定义域会明确报错。复杂多根及切点事件还需补强，不能宣称通用完整 GRIN 追迹。
6. 本服务只计算几何路径和光程，不对强度、偏振、界面反射/折射或吸收作隐式修改。现有分析调用路径未改，也没有在实验室复制另一套求解器。

## 定义证据

- Ansys 官方 [Gradient 1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_1.html)、[Gradient 2](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_2.html)、[Gradient 3](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_3.html)及 [Gradient 4](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_4.html)提供模型公式；Gradient 4 本次读取的是该官方文档版本，未将其冒充原生 2026 R1 捕获。
- [梯度控制操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Gradient_Index_Control_Operands.html)：六个取样位置的 +X/+Y 距离采用前后表面的较大净半口径，不是机械半径或延伸区；[GRIN 操作数使用说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Using_Gradient_Index_Operands.html)限定 LPTD 对应 GRIN 5。这里只记录后续实现约束，未声称已接入。
- 连续光线方程见 [Optics Express 28, 6172 (2020)，式 (2)](https://opg.optica.org/abstract.cfm?URI=oe-28-5-6172)；自适应积分采用 Dormand–Prince 1980 年 5(4) 方法，出处亦见 [RK45 官方算法说明](https://docs.scipy.org/doc/scipy/reference/generated/scipy.integrate.RK45.html)。运行时代码完全为 C#；没有引入或执行 SciPy、Python、Optiland。

## 验证范围

[新测试](../tests/OptilandWorkbench.Tests/GradientIndexIntegrationTests.cs)检查四种分布的手算值和独立差分梯度、正负轴向梯度精确解、横向线性梯度的非近轴悬链线精确解、强梯度步长收敛、坐标变换、守恒量和可逆性、球面/倾斜出口、内部折返、传播预算端点、取消及非法数据。

解析解仅用于测试，不向运行时提供光线、接受条件或显示结果。初次定向测试为 31 通过/1 失败，暴露大试探步越过指数定义域的问题；已通过域错误识别与缩步修复，保持解析解门槛不变。后续补充路径预算端点的正反向用例。收尾复现了 ±10¹² 绝对坐标下约 6.9923×10⁻⁵ 的路程误差，已改为相对坐标转换，两个用例保持 3×10⁻⁹ 的路程及光程门槛。

Debug/Release 合并回归各 **2594/2594** 通过（既有 2560 + 当时新增 34）。最后的大坐标修复之后，默认 Debug/Release 输出重新构建，全部 GRIN 定向测试各 **36/36** 通过；没有宣称运行过单次 2596 项回归。27 份前批源码保持字节不变，29 份指定 Zemax 资产与已提交版本一致。11 个解析对照的最终最大误差见清单；它们不代表原生 Zemax 数值认证。

最终构建、测试计数、解析误差、源文件和默认二进制校验见[验证清单](../artifacts/validation/grin-propagation-core-20261003/verification.json)。该证据区分：既有 Zemax 资产完整性、当前回归、独立解析解验证，以及尚缺的原生 GRIN 数值捕获。没有新增 Zemax 捕获、GRIN MFE 数值等价认证或界面截图。第二十九批 2560 项仍是其历史阶段范围，不能将本批基础能力计入已实现操作数数目。
