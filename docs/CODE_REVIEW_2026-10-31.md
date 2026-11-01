# 2026-10-31 代码问题与未完成功能审查

**后续更新：本页三项缺陷已在本地实现修复，验证范围见[修复记录](CODE_REVIEW_FIXES_2026-10-31.md)。以下保留审查时的复现和代码位置；未完成功能清单不因这三项修复而关闭。**

本轮审查的是 `D:\Projects\opticalsystem` 当前工作区（HEAD `3b43bb7f87270e034218bf466b23f885330814fc`，含正在进行的未提交修改）。扫描了 812 个产品 C# 文件的入口/未实现提示，并定向追查应用服务、实验室界面、初始结构、镀膜、非序列及相关测试。**这不是逐行读完全部代码或全仓精度认证。** 以下缺陷均有受控复现，功能缺口则与源码和现有范围说明交叉核对。

主程序和内置模块默认 Release 构建成功，0 警告、0 错误；现有相关回归 **31/31 通过**。这些已有测试未覆盖本次发现的输入组合，不能据此判断没有缺陷。本轮增加后台/离屏诊断及审查记录，未修改产品实现。

## 需要修复的代码问题

### P1：快速聚焦会破坏合法的反射系统，并报告错误的最终像质

- 位置：[OptimizationService.Run.cs](../src/OpticalSystemDesign.Application/Services/OptimizationService.Run.cs)，第 22–26 行。
- 问题：最终像方间隔统一执行 `Math.Max(.001, initial + summary.BestFocusShift)`，将合法的反射系统负轴向距离强制变为正数。返回的 RMS 仍是裁剪前最佳焦移的结果，并非实际写入处方后的像质。
- 复现：独立合成凹面镜 `R=-100 mm`、入瞳直径 `2 mm`、像方间隔 `-50 mm`、550 nm 轴上视场。共享 Core 给出的最佳焦移为 **0**，快速聚焦却把间隔改成 **+0.001 mm**，实际移动 **50.001 mm**。
- 结果：接口报告 RMS `0.0000308895588371 mm`，对保存后的真实处方重新测量为 `0.760823424615734 mm`。另一个初始间隔 `-49 mm` 的控制也被改为 `+0.001 mm`。
- 影响：正常的反射/折返设计可在一次聚焦操作中被改坏，同时结果摘要让用户误以为保持了很小的光斑。
- 修复方向：依据真实传播方向和镜头结构验证可用像方位置，保留合法符号；以实际应用的位置重算返回指标。补充负间隔反射和正常透射的对照回归。

### P2：相机数值框的非法文本会被静默忽略，仍按旧参数运行

- 位置：[CameraImagingWindow.Actions.cs](../src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Actions.cs)，第 19、200 行；[CameraImagingWindow.SensorFiles.cs](../src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.SensorFiles.cs)，第 180 行。
- 问题：读取参数只检查 `NumericUpDown.Value` 是否为空，不检查当前 `Text` 是否有效及与 Value 一致。Avalonia 可以在保留上一次有效 Value 的同时显示非法文本。
- 离屏复现：曝光原值 `10 ms`，把文本改为 `not-a-number` 后运行理想光学对照，仍成功产生结果，实际曝光为 **10 ms**；运行后界面仍显示 `not-a-number`。
- 影响：画面显示的输入与实际仿真/导出参数不一致，用户不会收到参数错误提示。同类读取方式也出现在[装调实验室](../src/OpticalSystemDesign.App/Laboratories/OpticalAssemblyWindow.cs)第 244 行，但本批只对相机完整运行流程作了复现。
- 修复方向：按控件实际文化/格式解析当前文本，校验范围、整数语义和 Value 一致性；非法编辑应阻止计算/保存参数，并使旧结果的适用状态明确。已有初始结构数字输入校验可作为复用方向。

### P3：相机新增实验控件缺少三语翻译

- 位置：[CameraImagingWindow.Experiments.cs](../src/OpticalSystemDesign.App/Laboratories/CameraImagingWindow.Experiments.cs)，第 11 行，以及本次新增实验/标定/场景相关文案。
- 离屏复现：切换 `en-US` 后标题和“运行仿真”正常变为英文，“保存完整实验”仍是中文；切换 `ja-JP` 后该按钮也仍是中文。
- 影响：实验室的三语界面覆盖不完整。不能把已支持的部分标题/基础按钮翻译等同于新增功能已完整本地化。
- 修复方向：补齐新控件和状态文案的字典及动态模板，并在英文、日文下检查实际控件内容。

原始输入、聚焦前后处方和离屏结果见[最终复现 JSON](../validation/rechecks/2026-10-31/code-review/final-reproductions/reproductions.json)。复现程序在[CodeReview20261031](../tools/diagnostics/CodeReview20261031/)。

## 尚未完成或只完成部分接线的功能

这些条目不等同于隐藏空实现；多数在当前代码中明确拒绝不支持的情况。扩展能力需要真实计算、状态和验证闭环，不能仅取消禁用或去掉异常。

| 优先顺序 | 功能 | 当前实际边界与证据 |
|---|---|---|
| 1 | 正式散射/BSDF 与完整杂散光到传感器链路 | [ScatteringModels.cs](../src/OpticalSystemDesign.Core/Scattering/ScatteringModels.cs)第 29、61 行只扣减主光线强度，不改变方向；没有 BSDF Evaluate/Sample/Pdf。NSC 的复杂膜系分裂、相干/偏振探测与散射事件仍缺，不能因已有顺序 Jones/薄膜求解器就算接通。见[NSC 路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md)。 |
| 2 | 复用任意 NSC 追迹会话生成相机 RAW/ISP 的桌面入口 | [NonSequentialCameraSensor.Capture](../src/OpticalSystemDesign.Core/NonSequential/NonSequentialCameraSensor.cs)已有库接口，但 Application/App 没有调用它；会话快照身份、工程保存/模式协调和用户入口未接。现有三维相机直射路径是另一条已接通的有限场景流程。 |
| 3 | 三维相机支持常见非球面、折返和复杂镀膜镜头 | [DirectCameraRenderer.cs](../src/OpticalSystemDesign.Core/Analysis/Imaging/DirectCameraRenderer.cs)第 134–147 行明确限制共轴、非折返、球面/平面、圆孔径、透射、None/SimpleCoating，拒绝非零圆锥、GRIN、反射等。共享 Core 的其他追迹功能已支持其中部分类型，不代表三维成像已支持。 |
| 4 | 初始结构的胶合自主调度和真正 SPC 分支优化 | 编译、参数绑定和物理验收已有基础，[AutonomousSearchService.cs](../labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/AutonomousSearchService.cs)第 582–583 行仍拒绝带胶合界面的自主计划。库检索并未补完此调度。 |
| 5 | 初始结构自动口径分配、准备阶段精确续跑与完整产品交付 | [新计划](INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md)第 145–148 行的 M3.3–M3.6 仍未完成。已有最终准备报告/计划保存和后续搜索检查点，不能等同于准备中间状态可恢复；并行性能专项、完整淘汰原因/报告/兼容流程也仍有待办。 |
| 6 | 任意多材料膜系综合、针插入和通用拓扑变化 | 当前自动生成主要围绕 Low/High 两种材料的镜段/腔原型及匹配；[DesignService.GeneralSearch.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.GeneralSearch.cs)与[通用约束说明](COATING_GENERAL_FIXES_2026-10-30.md)没有提供任意多材料针插入、通用层增删/分裂或全局最优求解。连续膜厚优化和逐项目标保护已实现，不能列为未做。困难真实规格仍有未达标结果，但一次搜索失败也不证明不存在解。 |
| 7 | 一部分原生优化操作数 | 实际登记 383 项：341 Executable，42 CompatibilityOnly；其中 5 项官方标为 Unused，扣除后仍有 **37 项仅兼容保存、不可执行**，如 `POPD/POPI`、`LPTD`、`ZPLM`。完整名单见[实际注册表清单](../validation/rechecks/2026-10-31/code-review/operand-inventory.json)。此统计仅针对该原生登记表，不是全部 Workbench 自定义操作数总数。 |
| 8 | 逐表面高级设置 | [LensEditorPanel.SurfaceProperties.cs](../src/OpticalSystemDesign.App/Panels/LensEditorPanel.SurfaceProperties.cs)第 214–253 行明确说明独立表面颜色/透明度/隐藏、散射参数编辑、逐面 POP、面型 DLL、复合表面参数、逐表面偏振覆盖未实现。已有 PSF/波前分析和系统级偏振不替代这些逐面功能。 |
| 9 | 非序列专用设计、公差与探测器扩展 | 目前有矩形探测器及非偏振 Fresnel 分裂/路径库；Color/Polar/Surface/Volume 等探测器、专用优化操作数/公差、跨窗口路径联动和真正随机索引树浏览仍待完成。见[NSC 当前差距](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md#当前与-zemax-的主要差距)。 |
| 10 | 无焦 MTF 公差及部分工程统计适用范围 | [MtfToleranceMetric.cs](../src/OpticalSystemDesign.Core/Tolerancing/MtfToleranceMetric.cs)第 22 行明确拒绝无焦；目前不是 cycles/mrad 的公差闭环。制造单片图/径向评估对偏心倾斜系统、体积/质量对非圆偏心孔径，以及最佳拟合球面/离轴面形统计也仍有限制。见相应 Service 的显式支持边界。 |

优先建议：先修 P1 聚焦与 P2 输入校验，再推进与产品目标直接相关的正式散射、NSC→传感器接线及三维非球面支持；初始结构则优先胶合调度、自动口径和准备续跑。三语遗漏可随相应页面修正同步补齐。

## 验证范围和未覆盖项

- 默认 Release App 构建：主程序及两个内置模块通过，0 警告/错误。
- 后台合成复现：两个反射镜焦位、一个非法曝光文本完整运行、英文/日文实验按钮覆盖检查；没有控制用户桌面。
- 定向现有测试：`ReflectiveParaxialSignTests`、`QuickFocusUpdatesTheImageSpaceThickness`、`CameraImagingWindowTests`，**31/31 通过，0 跳过**，见[TRX](../validation/rechecks/2026-10-31/code-review/tests/review-focused.trx)。没有把这些绿灯算作新缺陷已修复。
- 未重跑全仓或全部数值对照；没有新增独立 Cooke/Tessar 精度调查、安装/发布或跨平台原生验收。旧无焦相对照度 `NotApplicable` 是物理适用性边界，不计为待实现功能；已经关闭的 335 点等历史残差也未重复列为当前缺陷。
