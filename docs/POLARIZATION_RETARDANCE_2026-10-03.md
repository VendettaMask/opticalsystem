# 共享复电场追迹与 RRET

本页保留 RRET 阶段 325 项/2962 项验证记录。后续已接通系统参考轴、CODA 和照度全局偏振输入，最新实现与边界见[膜层约束记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03。本批接通 `RRET` 的均匀各向同性、未渐晕圆瞳模式：**325/383 项受限执行、58 项兼容保留**，其中 51 项已知功能、2 项定义待核实、5 项 Unused；另有 4 项本程序扩展。名称存在、受限可执行、与原生数值等价是不同状态。`CODA`、全局偏振设置和应力双折射尚未实现。

## 定义与实现边界

官方[操作数帮助](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)将 RRET 定义为给定视场在像面上的 RMS 延迟，单位为弧度；采用高斯圆瞳采样，Wave=0 时按谱权重合成。[2026 R1 API](https://developer.synopsys.com/docs/zemax-opticstudio-zos-api-2026-r1/reference/_interfaces_m_f_e_8cs.md)进一步说明这是 Ex 与 Ey 的相位差。它不是 Jones 矩阵本征延迟，也不等于光程差 RMS。

| 参数槽 | 本地实现 |
| --- | --- |
| Int1 / Ring | 1..32 个高斯环；当前每环使用共享求积器的 6 个方位 |
| Int2 / Wave | 0：所有正权重波长；正编号：指定波长，包括其谱权重为 0 的情况 |
| Data1 / Hx、Data2 / Hy | 归一化视场，各自须在 [-1,1] 内 |
| Data3 / Jx、Data4 / Jy | 非负相对振幅，不能全部为零，计算前归一化 |
| Data5 / X-Phase、Data6 / Y-Phase | 入射 Jones 分量相位，单位为度 |

当前算法对像面**局部 XY 分量**的主值相位差 `arg(Ex × conjugate(Ey))` 求加权均方根，主值范围为 [-π,π]。不去均值，不再以出射强度重加权。共同传播相位在 Ex/Ey 中抵消，RRET 因而省略共同光程相位而保留各界面及膜层相对相位。波长权重和高斯面积权重相乘后归一化。零 Ex 或 Ey 的相位未定义，明确报错。

**以上主值绕相、零分量行为、方位采样、权重和输入参考选择尚无原生 RRET 数值捕获。** 本地结果是明确约定下的受限实现，不能称为原生等价。缺少参数尾槽的旧兼容行不补零冒充有效输入。

## 共享 Core

- `ComplexElectricField` 保存三个复电场分量；`JonesInputState` 将相对幅相转换成正交横向基矢，支持 X/Y/Z 参考轴和极大有限振幅的稳定归一化。参考轴与光线平行时拒绝未定义的基矢。
- `SequentialRayTracer.TracePolarized` 复用正式 `TraceSequentialSurface`，保留真实相交、孔径检查、折射率、传播方向及 `RayInteractionKind`。它不采用另一套几何追迹、不做孔径外延续、不建立私有缓存。
- 原 `UnpolarizedPowerTransport` 的两条基矢链扩展为可输出相干线性组合；偏振与非偏振照度共用界面系数。每次变化的入射面都会旋转基矢，跨界面保持复振幅相关性。标量源、体吸收、标量膜层强度仍由正式几何路径负责，振幅取其平方根，避免重复衰减。
- 物理相干多层膜复用 `CoherentMultilayerCoating.EvaluateJones`。本程序既有薄膜求解器采用 n+ik 和负时间谐波；新接口正时间约定显式共轭其振幅，光程相位取负号。两个时间约定均有测试，既有非偏振功率行为不变。
- 入射参考基矢遵循官方[初始偏振定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Defining_the_Initial_Polarization.html)；RRET 当前固定物面局部 X 参考。目标局部 XY 投影和传播相位符号依据[偏振术语](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Definition_of_Polarization_Terms.html)。官方文字依据不替代本文件/设置下的原生数值验证。

目前支持裸介质、理想反射、显式标量膜层和已支持的物理相干膜层，保留全反射的复相位。散射、光栅、薄透镜或未提供确定性复振幅的交互、GRIN 连续偏振、环形/非圆孔径、非零视场渐晕变换明确拒绝。采样光线实际遮挡或失交会使操作数报错。应力双折射与各向异性体传播尚未接入。

直接追迹 API 的入射 Jones 相位定义在传入光线起点，返回目标面相互作用后的电场；可选关闭共同传播相位。相位参考不从旧 `RealRay.PolarizationMatrix` 或 `OpticalPathDifference` 推导，混入旧实数偏振矩阵会报错。原 `JonesPupilEngine` 分析入口尚未迁移，本批不声明所有偏振分析已统一。

## 编辑、文件与帮助

本地 RRET 可编辑完整八槽，Data5/6 以既有 STAROPT 字段保存。生产 DLS 可使用曲率变量优化实际 RRET 残差。原生 ZMX 的列语义尚未捕获，所以原生导入及旧兼容行继续禁用只读，原值和未核实尾列保留，不自动升级。工程 schema 没有变化。

帮助路径为“光束与专项 → 镀膜与偏振 → RRET”，详情说明当前公式、参数与缺口。8 大类、51 族、387 条目不变；`CODA` 保留待实现，因为其依赖的系统全局偏振状态尚未建模。共享复电场接口为后续 CODA 提供基础，不能据此把 CODA 计为完成。

## 验证与证据

定向 **120/120** 已通过，包括新增 `PolarizationOperandTests` **47 项**及已有偏振照度、相干膜层、帮助与名称真实性测试。默认输出 Debug/Release 合并回归各 **2962/2962**（前批 2915 + 新增 47），零失败、零跳过，构建零警告、零错误。

新增测试覆盖独立 Snell/Fresnel 功率、吸收单层 Airy 复振幅、正负时间约定、共同光程相位、全反射、刚体坐标变换、相干交叉项、多波长加权、主值相位且不去均值、严格失败路径、取消、八槽编辑保存、只读原生导入与真实 DLS 曲率优化。独立公式只存在于测试；DLS 目标由当前 Core 生成，仅证明优化链有效，不作为独立精度证据。

首次定向 119/120：DLS 夹具的 2 mm 孔径使初始残差小于测试的可分辨门槛；改为 10 mm 孔径后保留原断言通过，未改变生产公式或放宽门槛。初次与复验的日志、TRX 均保留。

证据目录：[本批验证清单](../artifacts/validation/polarization-retardance-20261003/verification.json)、[官方来源读取记录](../artifacts/validation/polarization-retardance-20261003/sources.json)。最终清单区分固定 Zemax 基线完整性、当前 Workbench 重算、独立解析比较、原生验证缺口和 UI 自动测试；本批没有新增原生数值捕获或桌面截图。
