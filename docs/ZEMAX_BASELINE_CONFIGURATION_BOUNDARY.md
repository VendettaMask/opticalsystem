# Zemax 基准配置边界

更新时间：2026-08-04。

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

- 当前 Core 规范目录为 72 项；历史 69 页 Zemax 捕获清单不因独立“畸变”入口退场、几何 MTF 强度加权或新增非序列追迹/探测器分析而追溯改写。正式测试数量和结果以 README 当前验证记录为准，独立智能初始结构实验室不参与 Zemax 基线结论。
- Zemax 数值基准定向测试：`10/10` 通过。
- Core/Application/辅助兼容边界定向测试：`16/16` 通过。
- Python 报告与映射测试：`14/14` 通过。
- `[MS-L7]` 评价函数导入与编辑往返定向测试继续通过；当前 124 个 Zemax 顺序操作数已有定义级可执行路径，覆盖 `TRAR`、`TTHI/TGTH`、`REAR/RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`。注册表按本机 2026 R1 实测扩展到 383 个顺序兼容代码；`DIMX` 等尚未完整实现语义的兼容操作数仍为禁用只读记录。新增执行路径仍需 Zemax/ZOS-API golden 对照后才能标为完整兼容。

以上结论不表示所有 Zemax 版本、所有分析窗口或所有镜头共享 `123456.ZMX` 的设置；它只保证仓库不会再把该基准文件的捕获参数冒充成通用 Zemax 规格。
