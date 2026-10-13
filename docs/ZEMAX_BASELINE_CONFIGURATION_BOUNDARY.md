# Zemax 基准配置边界

2026-10-03 行表更新：本地 MCE 四类数值行与 MCO 三项计算已接通，没有新增原生行表顺序、文件列或数值捕获。ZMX 配置属性解析不会被猜测拼成 MCE 行顺序；原生 MCO 行仍禁用只读。已提交 123456 基线仅保留原范围。 详见[本批记录](MCE_ROW_OPERANDS_2026-10-03.md)。

2026-10-03 多配置更新：CONF/ZTHI 的本地配置求值与候选链接回归已接通；没有新增这两个操作数的原生 MFE 捕获。已提交 123456 基线只保留原有文件、设置和版本范围，不能据其通过声称多配置状态组合或列映射等价。 详见[本批记录](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。

2026-10-03 膜层约束：本批复数 Airy 解析和实际 CODA 优化验证共享链路，不扩展固定 123456 原生捕获范围。原生九项 MFE 读数、全部层聚合及文本膜层记录仍无新增捕获。 详见[实现与验证](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03 CODA：CODA 的独立 Fresnel/Airy、传播相位和椭圆解析检查不扩展固定 123456 原生捕获的覆盖范围；本批没有新增原生 CODA 参数、全局偏振映射或数值文件。 详见[实现与验证](COATING_DATA_OPERAND_2026-10-03.md)。

2026-10-03 RRET：RRET 的独立 Fresnel/Airy/全反射解析测试只验证当前 Core 数学与执行链，不是 123456 捕获的新增比较项；固定 Zemax 文件不包含本批 RRET 参数、绕相或原生数值证据。 详见[实现与验证](POLARIZATION_RETARDANCE_2026-10-03.md)。

当前操作数统计与最新构建/测试范围统一见[当前状态](CURRENT_STATUS.md)。下方各批次计数保留对应历史阶段，不代表最新总数。

2026-10-01 操作数帮助已改为三级导航，帮助阶段默认桌面 Debug/Release 输出已更新；当时帮助、名称标识及字号检查各 **19/19** 通过，含明暗主题和窄窗口渲染。见[层级与验证](OPERAND_HELP_HIERARCHY_2026-10-01.md)。该 19 项记录保留原阶段范围，后续合并回归见下方第二十六批。

更新时间：2026-08-04。

2026-09-06 补充：[独立 C# 比较工具](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md) 对任意输入保存显式 CanonicalAnalysisRequest、CFG、实际软件版本和容差配置哈希。数值契约配置为 CapturedSettings；未对齐的接口检查单独标为 NativeCapabilityInspectionNotAligned。均不扩展 `123456.ZMX` 的历史适用范围，也不继承该镜头的 4.5 mm 视场或自动范围作为通用值。RMS 工具实验的 12 角向样本不是 native 默认值，Core 默认仍为 6；尚未验证的角度/像高场网格转换必须拒绝，不得仅改轴单位标签。

## 三类信息必须分开

1. **Zemax 规格/物理定义**：只能来自官方文档、公开 API 契约或可复现的物理定义。例如 Y-Ybar 表示各表面的边缘光线高度 `Y` 与主光线高度 `Ybar`。
2. **Workbench 产品预设**：GUI 打开分析时由 Application 层选择的初始值。这是本产品的选择，可以与某次 Zemax 窗口设置相同，但不得称为 Zemax 通用默认值。
3. **`123456.ZMX` 捕获设置**：随固定 Zemax OpticStudio 2026 R1 基准保存的采样数、视场/波长选择、焦移范围、参考方式等。它只用于复现这一基准，必须由对标测试显式传入。
4. **`[MS-L7]` 评价函数导入夹具**：用于核对 ZMX 评价函数的 103 行源顺序和参数槽位，不是 69 页分析截图或 30 项数值映射的精度来源，也不能证明只读兼容操作数已经实现计算。

精度权威与配置权威不是同一件事：`123456.ZMX` 是当前默认精度基准，但它的分析设置不会因此成为 Zemax 的通用规格。

## 本轮代码审计

- 生产代码没有读取 `123456.ZMX` 或其报告来动态决定分析算法。
- Core `AnalysisCatalog` 已恢复为通用构造器默认值，不再承担 Workbench 产品预设。
- `RMS vs. Field` 的 Core 默认数据为 spot、参考为 centroid；未显式给出 `fieldDensity` 时由 `numFields` 推导，不再暗含基准的 15 个间隔。
- `RMS vs. Field` 的 Spot 路径按 `Field Density + 1` 和 Orientation 从零连续扫描至最大定义视场，不把 Field Editor 离散行冒充扫描点；公共波前 RMS 中 chief reference 仅减加权 piston，centroid reference 减加权最佳拟合 piston 及 X/Y tilt。两者都是分析语义，不是 `123456.ZMX` 专用参数。
- RMS 密度按方法解释：Core GQ 支持 1–32 径向环，RA 支持 1–1024 每边点；OpticStudio RMS 文档的 GQ 上限为 20，不能把本程序扩展范围当作其规格。一般 GQ 角向默认仍为 6，捕获工具的 `2 × 径向阶数` 需显式传入。
- 波前 RMS 的偏振开关使用正式系统 Jones/非偏振透过强度加权，关闭时保持几何权重；未支持的吸收接口或 GRIN 偏振明确拒绝。RA 波前端点节点与更宽的渐晕插值范围仍待原生认证，见[本轮实现边界](RMS_SAMPLING_POLARIZATION_REPAIR_2026-10-08.md)。
- `RMS vs. Focus`、Diffraction Encircled Energy、Pupil Aberration、Huygens PSF Cross Section、Huygens MTF 和 Contrast Loss Map 的 Core 默认值已与 `123456` 捕获参数解耦。
- Workbench GUI 仍可在 Application 层显式选择用于当前产品的预设；注释明确说明这些不是 Zemax 文件格式或通用默认规则。
- Zemax 对标测试已显式传入捕获参数，测试名使用 `Captured...Settings` 或明确的 `123456` 表述。
- ZOS-API 捕获工具把加载文件后读到的初始值记录为 `capturedInitialSettings`，不再命名为 `zemaxDefaults`。

## 防回归规则

- [AGENTS.md](../AGENTS.md) 要求所有后续改动遵守三层边界。
- `AnalysisPresetBoundaryTests` 直接检查关键 Core 构造器的默认参数；若基准采样数、焦移范围或兼容开关再次进入 Core 默认值，测试会失败。
- `OptilandParityTests.RmsVsFieldRetainsZeroWeightFieldsButExcludesThemFromAggregate` 验证 Core catalog 仍按已定义视场运行通用 RMS spot 分析。
- `ZemaxRmsWavefrontVsFieldParityTests.WorkbenchProductPresetUsesTheCaptured123456RmsFieldSettings` 单独验证 Workbench 产品预设使用显式捕获设置，避免把产品行为和 Core 规格混在一起。

## 验证结果

- 当前 Core 规范目录以 `AnalysisCatalog.Names` 和比较工具 `--list-analyses` 为准；2026-10-13 将旧 `Through Focus MTF` 收敛为 `Fourier Through Focus MTF` 的兼容别名，不再重复列项。历史 69 页 Zemax 捕获清单不因目录变化而追溯改写。当前测试数量和结果见 [状态页](CURRENT_STATUS.md)；初始结构已是内置模块，但其搜索求解测试不等于 Zemax 数值基线认证。
- Zemax 数值基准定向测试：`10/10` 通过。
- Core/Application/辅助兼容边界定向测试：`16/16` 通过。
- Python 报告与映射测试：`14/14` 通过。
- `[MS-L7]` 评价函数导入与编辑往返定向测试继续通过；当前 340 个 Zemax 顺序操作数已有定义级可执行路径，覆盖 `TRAR`、`TTHI/TGTH`、`REAR/RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`。注册表保留 383 个顺序兼容代码，名称已与官方 2026 R1 API 目录逐项核对；`DISC` 等未实现项仍为兼容只读；`DIST/DISA` 本地受限执行，原生导入仍只读；`DIMX` 已接通本批限定模式。新增执行路径仍需 Zemax/ZOS-API golden 对照后才能标为完整兼容。

以上结论不表示所有 Zemax 版本、所有分析窗口或所有镜头共享 `123456.ZMX` 的设置；它只保证仓库不会再把该基准文件的捕获参数冒充成通用 Zemax 规格。
