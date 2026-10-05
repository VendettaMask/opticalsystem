# 照度的偏振功率链与像面入射边界

2026-10-03 CODA：共享照度现在读取系统 Unpolarized；开启时沿用双基矢强度平均，关闭时对系统 Jones 输入做相干组合后取功率。共同路径相位抵消，界面复相位仍保留。ApertureRayDiagnostic 分别发布 UnpolarizedIntensity、PolarizedIntensity 和选定的 PolarizationWeightedIntensity。 详见[实现与验证](COATING_DATA_OPERAND_2026-10-03.md)。

前一阶段（2026-10-03 RRET）：后续 RRET 和有相位 Jones 输入已共用此处双基矢运输及界面系数；既有非偏振照度行为继续回归。本页是原阶段记录，不代表全局偏振设置或 CODA 已完成。 详见[实现与验证](POLARIZATION_RETARDANCE_2026-10-03.md)。

后续第二十七批已将 RELI/EFNO 接入本地评价函数、编辑保存和优化；当前计数为 300 项受限执行、83 项兼容保留，原生导入及数值缺口见[操作数接入记录](ILLUMINATION_OPERANDS_2026-10-02.md)。下文仅记录本阶段完成时的状态与验证，保留历史计数。

采样后续：已新增可选均匀像方方向余弦网格和轴上参考接口，见[像方网格与轴上参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)。其单元边界与绝对 F 数尚未证明与原生等价；下文关于网格未实现的清单为当时阶段状态。

阶段说明：本文的 126 项验证与未完成清单记录透明介质阶段。后续已接入有限吸收膜层、透明外侧介质的复振幅传输及吸收基底反射，并增加原生快照保存，见[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)；吸收入射介质、吸收基底完整偏振透射及原生采样仍未完成。历史机器摘要保留当时的源码和二进制哈希。

2026-10-02，继[标量全光瞳基础](ILLUMINATION_CORE_2026-10-02.md)之后。本轮为共享照度服务和相对照度分析增加透明、均匀、各向同性介质的非偏振光功率传输。RELI / EFNO 操作数尚未接通，目录仍为 298/383 项受限可执行、85 项兼容保留；本轮没有增加操作数计数。

## 已实现

- [FresnelPower](../src/OptilandWorkbench.Core/Coatings/FresnelPower.cs) 发布按功率通量归一化的复数 s/p 反射、透射振幅。透射振幅包含 `sqrt(n₂ cosθ₂ / n₁ cosθ₁)`，不能把电场透射率直接平方当成功率。全反射保留 s/p 相位；相同折射率、正入射和布儒斯特角均有独立验证。物理定义参考 [MIT Fresnel 与能流讲义](https://ocw.mit.edu/courses/6-007-electromagnetic-energy-from-motors-to-lasers-spring-2011/resources/mit6_007s11_lec33/)。
- [UnpolarizedPowerTransport](../src/OptilandWorkbench.Core/Rays/UnpolarizedPowerTransport.cs) 传播两个相互不相干的正交 Jones 输入，保留每条光路中旋转的入射面和复相位，最后才求平均功率。不能将每个界面的非偏振平均透过率直接连乘；斜入射双界面平板和旋转的衰减轴会暴露这个错误。
- [孔径诊断](../src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.ApertureDiagnostics.cs) 继续调用正式相交和传播引擎，严格使用 `RayInteractionKind` 区分透射、指定反射和全反射。`NoneCoatingModel` 的透明介质界面计算 Fresnel；指定镜面按现有理想反射器语义处理。`SimpleCoatingModel` 表示不区分偏振、无额外相对相位的标量响应，替换界面损耗，其标量系数已在普通追迹中应用，不再重复乘一次。
- 像面照度改为入射功率。正式表面引擎新增显式 `stopBeforeInteraction` 分支：仍完成入射介质传播、吸收和像面物理孔径检查，在像面自身折射、反射、镀膜之前停止。照度调用通过 `stopAtIncidentImage: true` 选择此分支。普通追迹与默认诊断仍返回表面作用后的结果；没有改变其既有语义。
- [IlluminationMetrics](../src/OptilandWorkbench.Core/Services/IlluminationMetrics.cs) 可选择 `usePolarization`，仍共用正式光线生成、瞄准、孔径掩码、像面方向余弦积分和有界并行视场计算。非偏振光 Jones 结果与光线本身的标量源强、镀膜权重相乘；物理遮挡独立于零强度，不把诊断延续的被挡光线计入通光面积。
- 相对照度分析与应用参数接通 `UsePolarization`，默认 `false`，参数显示为“偏振损耗（透明介质）”，结果同时发布选择值和计算模型。该开关考虑非偏振入射光在系统中的偏振损耗，不等于任意用户输入 Jones 状态。官方说明见 [OpticStudio 相对照度](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html)。

图像模拟仍采用此前标量照度路径。没有改动其已有的实验性偏振选项，也没有将旧的 `JonesPupilEngine` 电场振幅语义改为功率振幅；这两者需要独立审计后才能接入相同传输服务。

## 验证

[PolarizedIlluminationTests](../tests/OptilandWorkbench.Tests/PolarizedIlluminationTests.cs) 包括分别对 s/p 的能量守恒、正入射功率通量、布儒斯特角、全反射复相位、旋转基底、相位延迟后的衰减、正式双界面平板、显式标量镀膜、全反射与理想镜面的类型化分支、像面作用前停止、完整光瞳积分、并行一致性、不支持模型和应用参数联动。独立公式与高精度积分只在测试内使用，不提供产品结果。

首轮 25/25 通过。应用联动测试最初误以为底层 `WorkbenchRuntime.BuildAnalysisView` 会把不支持的模型转换为空视图；实际契约是抛出明确异常，由上层呈现。测试按该现有契约修正，没有吞掉异常或更换计算模型。最终默认 Debug/Release Core、Application、App 与主测试项目构建成功，相关同一过滤器各 **126/126**，零失败、零跳过，编译无警告/错误。126 中包含本轮新增 **26** 项偏振照度用例、此前照度基础的 83 项，以及 17 项既有正式追迹/批量后端用例；没有与此前 2308 项合并回归相加。格式和 `git diff --check` 通过。机器记录见 [verification.json](../artifacts/validation/illumination-polarization-20261002/verification.json)，命令见[构建记录](BUILD_AND_RELEASE.md#照度偏振与入射边界复验2026-10-02)。本机测试运行窗口 Debug 约 185 秒，Release 约 50 秒，包含不同配置和调度开销，不能作为严格性能比较。

采样密度研究使用既有相对照度比较用例增加观测输出，在当前 Workbench 分别以密度 5、20、40 计算轴上、半视场、全视场；原密度 10 的 21 点比较和原 RMSE / 最大误差门槛不变。加密后的计算不是新的 Zemax 捕获，也没有为绝对有效 F 数新设宽松门槛。

| Workbench 密度 | 轴上有效 F 数 | 边缘相对照度 | 每个视场积分节点 |
| --- | --- | --- | --- |
| 5 | 2.8673791377382485 | 0.9691617775496111 | 1129 |
| 10 | 2.8668857109658967 | 0.9691639380051803 | 2473 |
| 20 | 2.866761750970437 | 0.9691648988669981 | 6673 |
| 40 | 2.86673076154367 | 0.9691651414523755 | 20473 |

已提交 JSON 的轴上有效 F 数为 **2.87577716994446**、边缘相对照度为 **0.9722914006965155**。当前密度 20→40 的轴上 F 数变化约 0.000031，距原生捕获仍约 0.009046；在这组观测中，加密当前光瞳网格未消除差异。不能据此断言差异完全来自某个原因，也不能把现有 Ray Density 与原生 Samp 当成同一网格。密度 10 的既有 RI 门槛仍通过且数值与第一阶段一致：RMSE 0.001439548656、最大误差 0.003127462691；完整精度 F 数最大相对误差仍约 0.309%。[采样对照](../artifacts/validation/illumination-polarization-20261002/density-comparison.csv)。

## 尚未完成

- 复折射率吸收界面、物理多层偏振镀膜、GRIN、非标准相互作用和随机散射的完整功率传输。本轮对不支持项明确报错；经验透过率起伏模型不能充当物理偏振镀膜。
- 原生均匀像空间方向余弦采样、Samp 规则和绝对 F 数差异。当前自适应光瞳网格与官方描述的原生网格不同，不能将同名密度参数视为原生采样等价。
- RELI 的轴上归一化、EFNO 调用和评价函数编辑、保存、导入、优化联动，以及对应原生 MFE 数值证据。分析曲线仍按峰值归一化；官方 RELI 使用轴上参考且可超过 1，见[官方操作数定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。
- 亚采样遮挡、光瞳岛和方向余弦焦散的进一步收敛验证，以及一般非均匀透过率路径性能。

没有新增 Zemax 捕获、重算历史 Optiland 资产、实机验收或 UI 截图。定向回归不替代此前 2308 项操作数合并回归或全产品发布验证。
