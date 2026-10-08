# 计算路径修复与结果对比 2026年10月8日

本轮修复了分析入口的瞄准遗漏、FFT 显示与计算网格混用、重复降低名义采样、负像面间距被忽略，以及 Huygens 的全局轴向原点依赖。Foucault 未实现的偏振设置现在明确拒绝并在桌面禁用。新增回归覆盖这些设置的真实计算效果。

这解决了已经定位的内部一致性问题，但不等于完成 Zemax 数值等价。Huygens 仍使用已明确标记的标量兼容近似，MS-L7 的 Huygens 截面和衍射包围能量原生残差仍未关闭。原六镜头 132 项完整矩阵本轮没有重跑，95 Pass、12 Close、16 Difference、8 Incomparable、1 Error 保留为历史结果，不能用本轮局部通过重新分类。

## 已落实的修改

| 路径 | 修复后行为 | 验证边界 |
| --- | --- | --- |
| 通用 Fringe Standard Annular Zernike 与扫场 | 六角波前明确传入系统瞄准；结果记录实际设置 | 三个冻结镜头、两种视场波长设置、瞄准开关共 12 组；三种同节点系数与正式引擎控制一致 |
| Centroid Sphere 与 Best Fit Sphere | 光线发射和入瞳相位修正采用同一系统瞄准；无焦回退也一致 | 两个有焦参考球与合成无焦内部控制；不称为新的原生参考球认证 |
| 桌面 Standard 与 Annular | 新设置使用明确的均匀瞳面网格；Core 一般默认仍为六角环 | 显式旧 NumRings 请求仍走六角兼容入口；均匀拟合按相同基函数和遮拦验证 |
| FFT 与 Jones 光瞳 | 单元中心标志只决定节点，不再强制瞄准；相位、偏振和工作 F 数采用一致瞄准 | 自动与同节点预计算输入一致；高 NA 必要回退保留且记录 |
| 预计算 FFT 输入 | 校验瞄准、视场、波长、参考波长、网格拉伸、实际节点和偏振镀膜约定 | 缺失来源信息或不匹配的输入直接拒绝，不静默重算调用者相位；不代表验证任意镜头快照的全部来源 |
| 零离焦 离焦 快速 FFT MTF 与 sampled MTF | 默认遵循系统瞄准；快速 MTF 的尺度和稀疏光瞳使用预计算波前的实际瞄准 | 有焦无焦、偏振开关、非零离焦和几何恢复控制；改变镜头前后显式脱离追迹缓存 |
| FFT PSF Sampling 与 Display | 计算阵列使用 2N；Display 只从完整结果中裁切，不参与采样间距、峰值和归一化计算 | 相同物理坐标在不同显示尺寸中逐点完全一致；超过计算阵列的显示请求被限于实际阵列并记录请求值 |
| FFT 负 Image Delta | Zemax 兼容模式下负数表示完整未拉伸光瞳，0 为自动拉伸，正数按傅里叶共轭间距重新追迹 | 不再钳制为 0；非有限值拒绝；一般 Core 模式不接受负数 |
| Huygens 坐标基准 | 兼容倾斜权重使用第一物理面的镜头基准，而非任意全局原点 | Cooke 轴上离轴与 MS-L7 轴上，整体 Z 平移 1、10、100 mm 共九组；未宣称任意偏心倾斜系统已认证 |
| Foucault 偏振 | Core 对 true 抛出未支持错误；桌面仅禁用该控件并给出帮助说明 | 当前响应明确标记为定性波前梯度近似，未实现原生物理刀口和偏振积分 |

主要代码位于 `WavefrontAnalyses.cs`、`FieldSweepAnalyses.cs`、`ReferenceSphereWavefrontEngine.cs`、`DiffractionEngine.cs`、`JonesPupilEngine.cs`、`MtfScanAnalysis.cs`、`SampledMtfEngine.cs`、`PsfAndMtfAnalyses.cs` 和 Application 分派与桌面设置。全部运行计算仍使用正式共享 C# Core；没有引入第二套光学引擎。

## 修复后数值

### 瞄准与原生光扇

12 组内部矩阵中，通用 Zernike 的 36 个同节点拟合、12 个扫场边缘系数控制、24 个合成无焦参考回退均与遵循请求的正式源结果一致。FFT 自动结果与相同瞄准的预计算结果逐点一致，12 个不匹配瞄准输入全部被拒绝。Jones 在相同偶数 Zemax 节点上切换单元中心标志，不再改变光线或 Jones 分量。

零离焦辅助波前在六组捕获的原始瞄准设置下，与冻结原生 OPD 光扇逐点比较 492 条记录，全部有有效对应值，零缺失。没有插值、拟合活塞、缩放或更改容差。

| 捕获设置 | 修复前零离焦最大误差 waves | 修复后最大误差 waves |
| --- | ---: | ---: |
| Cooke 轴上 550 nm | 约 0.01027 | 1.552293849e-10 |
| Cooke 20 度 480 nm | 364.929953861 | 2.624171760e-5 |
| Double Gauss 轴上 587.6 nm | 约 0.02196 | 2.422235745e-10 |
| Double Gauss 14 度 486.1 nm | 384.154817243 | 4.031837608e-5 |
| Relay 轴上 587.5618 nm | 与原设置一致 | 7.668519486e-9 |
| Relay 2 度 486.1327 nm | 与原设置一致 | 6.317280772e-7 |

每组 82 条光扇记录包括两个方向各自的中心记录。这是六组 OPD 光扇验证，不是六套全部二维 PSF MTF 分析的认证。主动切换瞄准的另外六组只有内部一致性意义。

### FFT 网格和显示

固定 Cooke、Sampling 64、轴上 550 nm，修复后结果如下。计算间距从修复前的 1.875570803 或 0.9377854014 µm，统一为正确计算阵列上的 0.9377854014 µm。

| Display | 瞳面名义采样 | 计算阵列 | 像面间距 µm | 显示点数 |
| ---: | ---: | ---: | ---: | ---: |
| 32 | 64 | 128 | 0.9377854014 | 1024 |
| 64 | 64 | 128 | 0.9377854014 | 4096 |
| 128 | 64 | 128 | 0.9377854014 | 16384 |

Sampling 32、64、128 的隐式与显式 2N 计算阵列使用相同名义采样，间距和全部图点一致。负 Image Delta 的未拉伸模式间距为 1.326228833 µm，与自动模式不同；负数的绝对值不被当作正像面间距。

Zemax 文档将 Display 定义为已有 FFT 阵列上的显示区域，并区分负数、零和正 Image Delta 的行为。本轮落实这些入口语义，没有把某个捕获文件的设置当成所有镜头默认。[FFT PSF 官方说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26101/zh-Hans/OpticStudio_User_Guide/OpticStudio_Help/topics/FFT_PSF.html)

### Huygens 坐标修复和撤回的候选

Cooke 离轴整体 Z 平移 100 mm，修复前峰值从 0.03784596230 降到 0.03349369791，约降低 11.50%。最终修复后峰值为 0.03784596229533561，全部强度点的最大差为 2.844392499e-12。九组平移控制的最大强度差分别不超过 Cooke 轴上 9.361045273e-12、Cooke 离轴 2.844392499e-12、MS-L7 轴上 3.540563398e-11。

最终修复只将既有标量权重转换到随镜头移动的自身基准，没有把该权重冒称为正确参考球法线。已有典型基准的第一物理面基准为零，因此保留其原计算值，并消除任意全局 Z 原点带来的变化。该近似仍对物理模型选择有依赖，完整偏心倾斜和坐标旋转适用性未完成。

本轮实际试验了两个传播升级候选，均没有保留到产品：

| 候选 | MS-L7 Huygens 截面 NRMSE | Huygens MTF vs Field 最差 NRMSE | 处理 |
| --- | ---: | ---: | --- |
| 参考球朝内法线权重 | 0.007438540572 | 0.008786110718 | 增大原生误差，撤回 |
| 光线方向的平面波相位 | 0.007340050275 | 0.008799855194 | 未满足非回退要求，撤回 |
| 最终镜头局部基准修复 | 0.003725667059 | 通过原有非回退检查 | 保留坐标修复，不声称关闭原生残差 |

官方说明包含平面与球面相位参考的自动选择，并不公开完整的数值权重、离散实现或全部归一化细节。因此只依据文档概念更换一个因子，不能证明完整复刻；候选失败记录保留，不提高容差。此处可访问的是 2025 R2 官方帮助，不冒称 2026 R1 新认证。[Huygens PSF 官方说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)

## 验证结果

默认正式解决方案 Debug、Release 构建均零警告、零错误；最后一次 Release 完整构建在全量测试结束后完成，默认桌面输出已更新。四处正式 Core Release 二进制哈希一致：产品、正式测试、比较测试和诊断工具。Debug/Release 最终数值矩阵完全一致。

| 验证范围 | 最终结果 | 边界 |
| --- | --- | --- |
| 正式完整 Release | 4492 通过、2 失败、共 4494 项；零跳过，耗时约 22 分 27 秒 | 整体发布验收不通过 |
| 比较工具完整 Release | 178 通过、2 失败、共 180 项；零跳过 | 原两个数值残差及原上界保持不变 |
| 新增计算与桌面回归 | 60/60 在完整 Release 中通过 | 59 项计算和 1 项桌面控件；未删除原 4434 个正式身份 |
| 最终 Debug 专项 | 103/103 通过；零失败、零跳过 | 含新增 60 项与相邻 43 项，不另加到完整测试总数 |
| 修复后受控数值矩阵 | 两配置逐项一致 | 三个冻结镜头的 12 组设置，不替代完整六镜头矩阵 |

正式全量的两个失败身份在修复前全量中均已存在：

- `FieldDefinitionParityTests.FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays`：冻结 Optiland 辅助历史 `finite_angle` 为 0.058589983242791625，实际仍为 0.060191701479492175。没有用该历史数据校准产品或重新生成参考。
- `AccessibilityAndResponsiveLayoutTests.SettingsGridReflowsWithoutFixedItemWidths`：`SafeHeadlessUnitTestSession.Dispose()` 第 137 行空引用。未扩大异常过滤、跳过测试或以隔离通过抵消完整失败。此前另外两个会话异常本轮未重现，不等于已修复其竞态。

正式原 4434 个测试身份全部保留，新增恰为 60 个；比较工具原 180 个身份全部保留，两个失败集合和数值保持既有状态。

最终冻结衍射相关 45 项为 43 通过、2 个既有失败、零跳过。通过集合包含误差非回退控制，不等同于 43 项原生精度 Pass。失败数据保持不变：

- MS-L7 Huygens PSF Cross Section 为 Close，NRMSE 0.0037256670590166724，最大强度差 0.005391581588203756。
- MS-L7 衍射包围能量为 Difference。衍射极限曲线 NRMSE 0.013875784968407625，最大差 0.0505309569642369；镜头曲线 NRMSE 0.011467280942848471，最大差 0.04068881984537476。

这两个捕获均为开启瞄准的轴上 MS-L7，不能归因于本轮关闭瞄准的 FFT 混用或 Cooke 离轴原点错误。原生节点、传播模型、归一化、有限图像窗口和像素面积积分仍需逐层认证。

## 尚未关闭的工作

当前执行清单见[待解决问题分类与完成判据](OPEN_ISSUES_2026-10-08.md)：将确认缺陷、数值残差、待认证范围、未实现功能和发布验收缺口分开记录。以下四项保留本报告的整改范围；本次文档总结未新增计算或全量运行。

1. 独立认证 Huygens 的平面球面相位选择、方向振幅权重、坐标变换和归一化。不得恢复全局原点依赖来抵消其他误差。
2. 分别核对包围能量的理想衍射极限、实际 PSF、窗口总能量与像素面积积分，不调整记录上界掩盖差异。
3. 补齐可用 ZOS-API 环境中的新原生二维 Wavefront PSF MTF 和 Standard Annular Zernike 捕获，再重跑六镜头 132 项矩阵。本轮没有新原生捕获，先前缺少 API 的记录也未当作许可检测。
4. 继续处理带符号 Y 渐晕、RA 波前节点和高密度收敛，以及有限物距历史辅助差异和界面会话退出问题。完整 Debug、实验室、安装包和人工桌面烟测未覆盖。

## 证据和复现

修复前证据保留在[诊断报告](CALCULATION_PATH_DIAGNOSIS_2026-10-08.md)及 `artifacts/validation/calculation-paths-20261008`，不会被修复后数据覆盖。

本轮记录位于 [calculation-path-repair-20261008](../artifacts/validation/calculation-path-repair-20261008)：最终矩阵 `matrix-release-final/summary.json` 和 `matrix-debug-final/summary.json`，最终平移 `translation-release-final/translation-summary.json`，专项 `target-debug-01/targeted.trx`，完整正式 `full-release-01/full-formal.trx`，完整工具 `full-tool-release-02/full-comparison.trx`，冻结比较 `native-release-02/native-diffraction.trx`，候选失败 `native-release-01` 和 `native-plane-candidate`。汇总校验与失败原文见 `verification-summary.json`。原生输入与 10 个列出的产品源文件在矩阵运行期间均校验未变；这不是全仓库指纹覆盖。中间失败或取消的试跑保留，但不冒充最终验收。

新回归位于 `CalculationPathRepairTests.cs`、`CalculationPathPanelTests.cs`；已有高 NA 回退用例增加显式关闭瞄准的控制。GUI 契约改为 PupilSampling，但显式旧 NumRings 请求仍验证可用。没有删除既有测试、跳过失败、重生成历史 Optiland 数据或改动冻结容差。

诊断工具复现及边界见[工具说明](../tools/diagnostics/CalculationPaths20261008/README.md)。主 `123456.ZMX` 冻结基准完整性本轮复核通过：165 个清单条目、148 捕获、17 未捕获，1300 个文件、216794423 字节；106 张原生截图和 42 张已标记替代重绘分别统计。其源哈希、实际清单引用和图像可读取性验证与当前数值重算分开，148 个已捕获条目不是 148 个本轮数值 Pass。

## 可阅读报告

[四页 PDF 修复与结果对比报告](../output/pdf/Zemax_Workbench_Comparison_Repair_2026-10-08.pdf)已生成并逐页检查，包含六镜头历史矩阵、修复前后误差、完整验收和剩余任务。生成器 `tools/create_calculation_path_report.py` 只排版现有正式 Core 证据，并校验测试身份、冻结输入、源码和二进制，不实现光学计算。

原 `reports/Zemax_Workbench_Six_Lens_Comparison_Report_2026-10-08.docx` 保持不变。本机的 Word 逐页渲染器因缺少可用 LibreOffice 无法执行，不能把未渲染的 Word 改稿当作已验收成品；本轮更新以仓库正文和 PDF 交付。
