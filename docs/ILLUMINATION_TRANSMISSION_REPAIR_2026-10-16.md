# 照度传输修复与剩余原生差异

日期：2026-10-16。结论：**已修正 Pol=0 的传输策略，尚未解决全部有效 F 数和照度原生差异。** 本报告不将回归通过或主基准改善记作四文件数值等价。

## 已修复的原因与边界

EFNO/RELI 的 Pol=0 原先直接使用含玻璃吸收和镀膜损耗的标量光线强度，与所比较的原生几何积分约定不同。现在从共享表面追迹显式跳过体吸收与镀膜；折射、实际孔径交点、初始变迹权重仍保留。没有替换材料、清空孔径、修改原处方或往正常缓存写入诊断光线。

主程序“相对照度”的偏振开关选择相同策略，并发布“计入标量透射损耗”元数据。通用 Core `IlluminationMetrics` 和 `RelativeIlluminationAnalysis` 构造器仍默认计算物理功率；显式几何调用使用 `includeScalarTransmission: false`。图像模拟保持物理功率，不随产品照度预设改成无损传播。偏振模式保留标量损耗与 Jones 功率，不允许用几何参数悄悄关闭损耗；尚未支持的吸收偏振接口仍拒绝。

这符合[官方相对照度说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html)与[厂商工程师说明第 7–10 页](https://community.zemax.com/people-pointers-9/faq-about-relative-illumination-1006)给出的方向：像方方向余弦积分还需要处理像差，系统传输由偏振选项控制。公开说明没有给出全部有限网格细节，不能据此宣称已经复刻原生算法。

## 同文件复算

原始输入及参考为[既有四文件清单](../validation/zemax/2026-r1/fcgt-efno-2026-10-14/manifest.json)。没有新建原生捕获；已有记录绑定 OpticStudio 2026 R1、构型 1、Samp 5/10/20/40、全部活动波长及定义视场、Pol=0。以下“修复前”包括本轮之前保留的近轴/瞄准修复，“修复后”仅分离照度传输。最大值遍历每个文件的全部档位，不挑选点。

| 文件 | EFNO 最大相对差：前 → 后 | RELI 最大绝对差：前 → 后 |
| --- | --- | --- |
| 123456 | 9.289113% → 11.989687% | 0.004913498 → 0.000666349 |
| Double Gauss 28 degree field | 12.061829% → 12.917789% | 0.023244218 → 0.024859663 |
| Relay Lens | 12.307935% → 12.411538% | 0.005621481 → 0.005621076 |
| Even Asphere | 12.537049% → 12.601119% | 0.013255151 → 0.013255300 |

原生 **256 个操作数控制仍为 Difference**，没有为 EFNO 新设通过门槛。移除错误损耗后，部分旧的数值抵消消失，因此某些误差增大；不得只展示主基准照度改善而隐藏这些变化。

主程序按原始 123456 分析捕获设置（密度 10、21 场、主波长 0.44 µm、+Y、清除渐晕、关闭偏振）重算，RI 的 RMSE 为 **0.00013580598817169025**、最大绝对差 **0.0003334504898485635**，通过未变的原门槛 RMSE≤0.002、最大差≤0.004。此曲线按峰值归一化，不能与操作数的轴上归一化混称一个矩阵。轴上有效 F 数为 **2.7436156635407603**，原生为 **2.87577716994446**；这一绝对差仍在。

## 验证

新增 16 项回归覆盖两种积分器、独立玻璃/镀膜损耗、真实孔径、非均匀变迹、零透射、Pol=0/1 分离、并行与轴上参考、产品与通用 Core 默认值，以及上述原曲线门槛。四文件已有 60 个非偏振光线强度与几何模式一致；几何坐标、方向和首截光保持原追迹结果。这些是强度通道的验证，不代替积分等价。

原“零镀膜导致无光”的物理操作数测试明确放在 Pol=1，并新增 Pol=0 真实孔径完全遮挡的失败断言；没有删除无光保护，也未放宽任何数值容差。原物理积分、相干膜层及吸收测试继续执行。第一轮 121 项中的 2 项因旧 Pol=0 假设失败，原始 TRX 保留；修正测试所属物理模式后复验，不计作产品错误已忽略。

相关 Debug/Release 集合各 **203/203**、最终 Release 新增集合 **16/16**，零失败、零跳过；集合有重叠，不相加。默认两配置根解决方案各 19 项目构建零警告、零错误。改动 C# 的格式检查、896 份文档严格检查及差异检查通过。修复前后矩阵、六份 TRX、构建日志和文档审计共 11 个原件已[归档](../validation/repairs/2026-10-16/illumination-transmission-evidence.zip)，逐项读回 SHA-256 一致。

完整哈希与未执行范围见[机器账本](validation/ILLUMINATION_TRANSMISSION_REPAIR_2026-10-16.json)。前一轮完整 Release 结果只适用于[近轴与瞄准修复](PARAXIAL_FIELD_AND_STOP_AIMING_REPAIR_2026-10-16.md)，不能当作本次改动后的全量验证。没有提交、推送或重新打安装包。

## 尚不能关闭的部分

有限像方网格放置、边界权重和像差消除仍未与原生算法对齐。不能使用随采样密度变化的经验增益、改写冻结值或扩大容差代替修复。

本传输修复阶段记录了未找到原生安装及按用户授权跳过补采的范围，因此该阶段没有新增原生捕获，残余差异不转为通过。随后用户澄清本机已有 Zemax；现已实际核验 `D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio` 的 **2026 R1 build 260127 Enterprise API** 授权，并重新采集原四文件全部 256 个控制。最新环境与结果见[后续本机复核](ZEMAX_NATIVE_DIFFRACTION_ADAPTER_REPAIR_2026-10-16.md)，不再把此前环境跳过作为当前工作限制。独立 Cooke/Tessar 调查仍不在范围内。

源码：[共享积分](../src/OpticalSystemDesign.Core/Services/IlluminationMetrics.cs)、[操作数策略](../src/OpticalSystemDesign.Core/Optimization/MeritFunction.Illumination.cs)、[回归](../tests/OpticalSystemDesign.Tests/GeometricIlluminationTests.cs)。复算命令与仅照度选项见[诊断工具说明](../tools/diagnostics/Accuracy20261014/README.md)。
