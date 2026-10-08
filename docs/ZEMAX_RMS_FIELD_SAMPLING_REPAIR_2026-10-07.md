# RMS 光斑视场采样修复与多文件复验 · 2026-10-07

后续 2026-10-08 的[参数与偏振链路修正](RMS_SAMPLING_POLARIZATION_REPAIR_2026-10-08.md)恢复了 RA 高密度请求的实际执行，并增加桌面方法区分与偏振强度加权；没有把本页的 RA 光斑节点认证扩大为 RA 波前节点认证。

后续[波前图系统瞄准修复](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)已修复显示选项误控物理光线的问题；不改变本页历史 RA 光斑认证，也未重算这里的外部分类和全量计数。

本页保留矩形单元中心采样阶段的历史结果（原 84 Pass；独立控制 14 Pass/3 Difference）。后续当前原矩阵为 95 Pass，独立 17+6 项全部 Pass，见[最新复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。下文误差和验证仅描述此历史阶段。

已修正共享 Core 中 RMS 光斑矩形采样的节点位置：原生 RA 在正方形网格的单元中心发射光线，之前 Workbench 在边界节点发射。五份官方标准镜头补采的 **17 项独立控制**，修复前为 **10 Pass / 4 Close / 3 Difference**，修复后为 **14 Pass / 3 Difference**。四份无遮挡镜头的 RA 64 全部由 Close 变为 Pass。

原有六文件 **132 项**仍使用第一次捕获的原生数据、请求和容差，当前分类保持 **84 Pass / 12 Close / 27 Difference / 8 Incomparable / 1 Error**。上一阶段的 84 项 Pass 均无回退，131 份成功计算的原始 Workbench JSON 与上一阶段逐字节相同。新增控制另计，不能替换原 GQ 项或增加已比较的官方镜头数量；其余 21 份计划镜头尚未运行。商业软件精度与发布门禁仍未完成。

## 已完成的修复

`ApertureSampler.GenerateRectangularArray` 提供等权单元中心、圆形入瞳内的矩形采样。RMS 光斑视场、波长、离焦及场图通过共用的 `RmsScanSupport.SpotRadius` 调用该接口，仍使用正式光线生成、孔径、瞄准、材料、像面坐标与加权统计。通用端点网格及 GQ 采样不变；RMS 波前 RA 的节点约定尚未在本批认证，未据此扩大改动。

采样密度支持 1～1024，缓存只保留不超过 64 的小网格，纳入既有总容量限制；大网格逐次生成，支持取消。新增分析和实验均调用纯 C# 正式共享 Core，没有另建追迹或近似计算引擎。

正式比较工具增加有类型的 `rmsFieldMethod`、`rmsFieldRayDensity`、`rmsFieldRemoveVignettingFactors`，仅作用于 `RMS vs Field`。默认 GQ/6/true 保持既有请求指纹；控制中可选择 GQ 1～20 或 RA 32/64/128/256。双方方法、密度和渐晕选项进入同一请求，原生设置逐项及最终读回。GQ 采用本实验的两倍径向阶数作为角向样本数，已有 6、12 阶数值证据；其余阶数可设置，但不作为已认证范围。通用 Core 默认值未更换。

## 五文件原生控制

实际捕获环境为本机 **OpticStudio 26.1.0 build 260127 / EnterpriseEdition / 有效 API 许可**。每份镜头捕获移除渐晕因子的 RA 64、RA 128、GQ 12；Tessar 另捕获保留因子的 GQ 6 与 RA 128。双方质心参考、16 个从零到最大定义视场的扫描点、波长、瞄准设置、源文件 SHA-256 以及原生中文报告均已核对。接口枚举的别名不作为物理定义：例如 GQ 6 最终属性可能显示 `RayDens_1024x1024`，与请求名称共享同一枚举值。

| 镜头 / RA 64 | 修复前 NRMSE | 修复后 NRMSE | 修复后最大绝对误差 µm | 分类 |
| --- | ---: | ---: | ---: | --- |
| Cooke 40° | 0.007452954929 | 1.989935874e-14 | 8.082423619e-13 | Close → Pass |
| Double Gauss 28° | 0.008010405638 | 8.353285942e-14 | 2.344791029e-12 | Close → Pass |
| Relay | 0.006673692923 | 4.272043224e-10 | 1.602178656e-8 | Close → Pass |
| Even Asphere | 0.006861988588 | 4.632977458e-9 | 2.336284552e-7 | Close → Pass |

两档 RA 的八份无遮挡曲线都通过严格逐点回归，原始门槛没有放宽。四份文件的 GQ 12 也为 Pass，这些结果区分了采样规则问题与一般追迹/材料问题。

Doublet 只有零视场，不能为这种扫描建立非退化范围，故没有补采该控制。它仍属于原六文件矩阵，原有不可比和错误没有隐藏。

## Tessar 仍有差异

| 控制 / 移除因子 | 当前分类 | NRMSE | 最大绝对误差 µm |
| --- | --- | ---: | ---: |
| 原 GQ 6 | Difference | 0.163043151995 | 48.7919044141 |
| 新 GQ 12 | Difference | 0.157864228767 | 47.0705591495 |
| 新 RA 64 | Pass | 0.001367310501 | 0.2391060786 |
| 新 RA 128 | Pass | 0.000966670781 | 0.1318168531 |

RA 128 边缘视场原生为 **51.6599529585 µm**，Workbench 为 **51.5281361054 µm**；原 GQ 6 的原生值为 **99.4882012717 µm**，Workbench 为 **50.6962968576 µm**。新增 RA 达到原门槛，不意味着孔径边界逐光线一致或原 GQ 差异已修复。两档原生 RA 的边缘结果仍相差约 0.415 µm，下一批须增加密度并检查遮挡边界，不能仅凭同一网格下的 Pass 宣称积分已收敛。

关闭物理孔径的独立诊断中，Tessar GQ 6 的 72 条光线有 23 条越过孔径边界；9 条即使不检查圆孔径也未传播到像面，剩余光线的 RMS 为 **86.1943257662 µm**，仍未复现原生 99.4882 µm。这是诊断计算，不能用于物理通过率或验收。四份无遮挡镜头的对应诊断没有这些损失。官方本机手册第 843 页及第 1242 页说明，孔径截断、非椭圆有效光瞳会影响 GQ 积分准确性；该说明限制了 GQ 的适用范围，但不能单独证明当前差异的唯一原因。

保留因子的 Tessar GQ 6 与 RA 128 分别仍为 Difference，NRMSE **0.011709817146**、**0.011746094955**，最大误差 **1.2353587279**、**1.2317312158 µm**。RA 128 在定义视场 0°、10°、25° 的误差约 2e-11 µm，中间扫描点有差异，最差在 6.6667°。当前 Core 使用最近定义视场的因子，连续扫描的原生选择规则仍未建立完整参考。

简单按视场坐标线性插值的诊断也未复现原生全部点，最大剩余误差约 **0.441 µm**。该假设没有进入产品，也未改写全局渐晕规则。下一批应以原生逐光线坐标与因子控制确认规则，覆盖多波长、正负方向及非共线视场，避免把某一分析的规则推广到全部追迹路径。

## 验证

默认正式解决方案 Debug/Release 均构建成功，零警告、零错误。正式完整 Release **4349 通过 / 2 失败 / 4351 项**；比较工具完整 Release **154 通过 / 2 个既有失败 / 156 项**，均零跳过。本批新增正式 **14/14**、工具 **30/30** 在对应全量通过，正式 RMS 定向 Debug/Release 各 **22/22**，工具 RMS 定向 **30/30**。

正式失败包含既有冻结历史有限角度参考：预期 0.058589983242791625，实际 0.060191701479492175；另有本次 PanelContentLayoutTests 的 GbT13323_1991/cemented true 在 SafeHeadlessUnitTestSession.Dispose 出现 NullReferenceException。相关 PanelContentLayout 与 WavefrontSurfaceRender 隔离复验 **16/16** 通过，原全量失败保留；原因未完全定责，本次未修改测试清理辅助类或屏蔽异常。工具两个既有失败仍为 Huygens PSF 截面和衍射包围能量，NRMSE 分别 0.0037256670590166724、0.011467280942848471，与上一阶段相同。没有删除断言或放宽误差门槛。

本轮未重跑正式完整 Debug 和实验室。2026-10-06 正式完整 Debug 4336/1/4337、初始结构 Release 258/2/260、镀膜 Debug/Release 各 47/47 为历史验证，不作为本轮执行。当前基线同步到 36 份文档。

新增正式回归 **14 项**：四文件两档原生曲线共 8 项，3 项采样积分/对称性及通用网格边界，3 项非法密度。新增比较工具回归 **30 项**：12 项设置往返、指纹与非法契约，17 项真实原生控制的严格分类、1 项源文件和全部夹具哈希。Tessar 仍为 Difference 的用例验证严格门槛，不代表其数值精度通过。

全部原设置重算与新增控制使用正式 `WorkbenchExecutor`、共享 Core、同一 `ComparisonRunner.Compare` 和原逐量容差。完整性验证覆盖原捕获 **1769 个文件**、新增控制 **295 个证据文件**、上一阶段 **131 份**原始成功结果、新夹具 **153 个文件**以及 **27 份**官方源文件。冻结 `123456` 主参考及历史辅助参考未改动。

原生数组、完整逐点误差、最大误差、NRMSE、覆盖率与请求都保存在 [持久化矩阵](validation/ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.json)。[Tessar RA 128 叠图](../artifacts/zemax-standard-samples/20261007/rms-controls-recalculated/independent-rms-controls/tessar-lens-using-vignetting-factors-ra-128-remove/comparisons/rms-vs-field-c1/curve-0-overlay.png) 和对应差值图由正式比较工具生成，已查看；叠图的视觉一致不能替代上面的误差记录。

## 后续范围

优先抓取 Tessar 同一批入瞳点的原生逐光线状态、孔径截断面与像面坐标，核对 GQ 的光线接纳及未到达像面的处理；补充 RA 256/更密网格的收敛控制。随后为保留渐晕因子的连续 RMS 建立原生因子选择参考，并补 RA 波前、有限共轭和多波长控制。PSF、包围能量、照度及其余 21 份镜头仍按既定矩阵推进；本批不扩大精度认证范围。

证据：[新原生参考](../validation/zemax/2026-r1/rms-field-sampling-2026-10-07/manifest.json)、[17 项独立重算](../artifacts/zemax-standard-samples/20261007/rms-controls-recalculated/summary.json)、[132 项原设置重算](../artifacts/zemax-standard-samples/20261007/rms-original132-recalculated/summary.json)、[最终验证与哈希](../artifacts/validation/zemax-rms-field-20261007/verification-final.json)、[上阶段历史报告](ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md)、[27 文件计划](validation/ZEMAX_STANDARD_SAMPLE_PLAN_2026-10-06.json)。官方语义来源：`D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio/OpticStudio_UserManual_en.pdf`。
