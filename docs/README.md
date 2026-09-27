# 项目文档索引

同步日期：2026-09-27。索引覆盖 126 份自有 Markdown（包括本索引与状态页）；第三方原文、许可证和 `validation/history` 冻结数据不做重写。不同阶段报告保留各自范围，日期较近不代表已经完成全量验收。

先读 [当前实现与验证状态](CURRENT_STATUS.md)，再按任务进入具体指南。桌面使用看 [GUI 工作流](GUI_QUICKSTART_REFACTOR.md)，维护界面看 [UI 规范](UI_DESIGN_SPEC.md) 与 [主题包规范](THEME_PACKAGES.md)，构建看 [构建与发布](BUILD_AND_RELEASE.md)，定位代码看 [代码地图](CODE_READING_MAP.md)。

## 阅读规则

- 当前指南描述已实现行为；计划、兼容入口和未完成能力必须以文内状态为准。
- 阶段记录中的构建日期、测试数、截图和光学比较只证明该阶段列明的范围；不能相加，不能把局部通过当成全量发布门禁通过。
- UI 当前状态包括默认双页启动、240–280 DIP 紧凑侧栏且仅纵向滚动、明确的蓝色主动作、统一禁用说明、材料非空蓝行、三位数字显示、2 DIP 二维镜片边缘、MDI 操作和局部容器测量修复。
- 冻结数值报告保留原运行时相对明细路径，部分明细未随此检出保存；仅能读取现有摘要，不据此补造数据。自有指南中的 8 处缺失本地报告链接已改为明确的历史路径说明。
- 文档整理阶段仅核对源码常量、TRX 和本地链接；随后 Git 同步前补做默认 Release 构建及既有 70 项 UI / 29 项镀膜回归，结果与未覆盖范围见 [当前状态](CURRENT_STATUS.md)。数值基线未更改。

## 产品与工程指南

- [Project collaboration instructions](../AGENTS.md) — `AGENTS.md`
- [Optical System Design](../README.md) — `README.md`
- [系统架构](ARCHITECTURE.md) — `docs/ARCHITECTURE.md`
- [应用品牌资源](BRANDING.md) — `docs/BRANDING.md`
- [构建与发布](BUILD_AND_RELEASE.md) — `docs/BUILD_AND_RELEASE.md`
- [光学镀膜设计实验室](COATING_DESIGN_LAB.md) — `docs/COATING_DESIGN_LAB.md`
- [全仓库代码阅读地图](CODE_READING_MAP.md) — `docs/CODE_READING_MAP.md`
- [当前实现与验证状态](CURRENT_STATUS.md) — `docs/CURRENT_STATUS.md`
- [FFT PSF：高 NA 镜头工作 F 数追迹失败修正](FFT_PSF_STOP_AIMING.md) — `docs/FFT_PSF_STOP_AIMING.md`
- [文件格式与插件](FILE_FORMATS_AND_PLUGINS.md) — `docs/FILE_FORMATS_AND_PLUGINS.md`
- [GUI 工作流与重构对照](GUI_QUICKSTART_REFACTOR.md) — `docs/GUI_QUICKSTART_REFACTOR.md`
- [打包镜头库](LENS_LIBRARY.md) — `docs/LENS_LIBRARY.md`
- [本地矢量图标库](LOCAL_ICONS.md) — `docs/LOCAL_ICONS.md`
- [可制造性审查与光学制图](MANUFACTURING_DRAWINGS.md) — `docs/MANUFACTURING_DRAWINGS.md`
- [非序列第二阶段：杂散光基础链路](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md) — `docs/NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md`
- [非序列光源](NONSEQUENTIAL_SOURCES.md) — `docs/NONSEQUENTIAL_SOURCES.md`
- [非序列工作模式与光线追迹](NON_SEQUENTIAL_TRACING.md) — `docs/NON_SEQUENTIAL_TRACING.md`
- [数值验证与冻结历史回归](NUMERICAL_PARITY.md) — `docs/NUMERICAL_PARITY.md`
- [优化算法真实性与兼容策略](OPTIMIZATION_ALGORITHMS.md) — `docs/OPTIMIZATION_ALGORITHMS.md`
- [产品能力与格式兼容矩阵](PARITY_MATRIX.md) — `docs/PARITY_MATRIX.md`
- [镜头数据求解入口](RADIUS_SOLVE_EDITOR.md) — `docs/RADIUS_SOLVE_EDITOR.md`
- [大规模光线追迹性能](RAY_TRACING_PERFORMANCE.md) — `docs/RAY_TRACING_PERFORMANCE.md`
- [赛德尔系数报告（2026-09-04）](SEIDEL_COEFFICIENT_REPORT.md) — `docs/SEIDEL_COEFFICIENT_REPORT.md`
- [STAROPT 工程格式](STAROPT_FILE_FORMAT.md) — `docs/STAROPT_FILE_FORMAT.md`
- [表面类型与属性分类面板](SURFACE_PROPERTIES_EDITOR.md) — `docs/SURFACE_PROPERTIES_EDITOR.md`
- [主题包开发规范](THEME_PACKAGES.md) — `docs/THEME_PACKAGES.md`
- [公差分析](TOLERANCING.md) — `docs/TOLERANCING.md`
- [UI 设计规范](UI_DESIGN_SPEC.md) — `docs/UI_DESIGN_SPEC.md`
- [38668 导入后的公共 Working F/# 修复](ZEMAX_38668_WORKING_F_NUMBER.md) — `docs/ZEMAX_38668_WORKING_F_NUMBER.md`
- [Zemax 分析设置与实现方式参考](ZEMAX_ANALYSIS_REFERENCE.md) — `docs/ZEMAX_ANALYSIS_REFERENCE.md`
- [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md) — `docs/ZEMAX_APODIZATION_LAYOUT_FIX.md`
- [Zemax 基准配置边界](ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md) — `docs/ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md`
- [独立 C# Zemax 比较工具审计](ZEMAX_COMPARISON_TOOL.md) — `docs/ZEMAX_COMPARISON_TOOL.md`
- [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md) — `docs/ZEMAX_OPERAND_SUPPORT.md`

## 规划、兼容边界与设计审阅

- [旧、新架构收敛与单一结果链路修正计划](ARCHITECTURE_CONVERGENCE_PLAN.md) — `docs/ARCHITECTURE_CONVERGENCE_PLAN.md`
- [从平板生成定焦初始结构：产品方案与开发计划](INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md) — `docs/INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md`
- [智能初始结构实验室功能独立开发计划](INITIAL_STRUCTURE_LAB_PLAN.md) — `docs/INITIAL_STRUCTURE_LAB_PLAN.md`
- [大文件拆分实施记录](LARGE_FILE_SPLIT_PLAN.md) — `docs/LARGE_FILE_SPLIT_PLAN.md`
- [非序列模式 Zemax 工作流对齐路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md) — `docs/NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md`
- [系统未完成能力收口计划](SYSTEM_COMPLETION_PLAN_2026-09-02.md) — `docs/SYSTEM_COMPLETION_PLAN_2026-09-02.md`
- [UI 符合性审计](UI_CONFORMANCE_AUDIT_2026-08-04.md) — `docs/UI_CONFORMANCE_AUDIT_2026-08-04.md`
- [UI 设计走查记录](UI_DESIGN_REVIEW.md) — `docs/UI_DESIGN_REVIEW.md`

## 界面实施与验证记录

- [正蓝 × 雾蓝主题（2026-09-27）](BLUE_THEME_2026-09-27.md) — `docs/BLUE_THEME_2026-09-27.md`
- [紧凑顶部菜单与工具栏（2026-09-27）](COMPACT_TOOLBAR_2026-09-27.md) — `docs/COMPACT_TOOLBAR_2026-09-27.md`
- [平铺子窗口交互与标题按钮修复](MDI_INTERACTION_FIX_2026-09-27.md) — `docs/MDI_INTERACTION_FIX_2026-09-27.md`
- [机械半直径显示精度修复](MECHANICAL_PRECISION_2026-09-27.md) — `docs/MECHANICAL_PRECISION_2026-09-27.md`
- [库存匹配与制图容器修复](PANEL_CONTENT_LAYOUT_2026-09-27.md) — `docs/PANEL_CONTENT_LAYOUT_2026-09-27.md`
- [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md) — `docs/PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md`
- [默认只打开镜头数据与二维视图](TWO_PAGE_STARTUP_2026-09-27.md) — `docs/TWO_PAGE_STARTUP_2026-09-27.md`

## 光学与全仓阶段记录

- [精度与页面对比验证（2026-09-05 更新）](ACCURACY_VALIDATION_2026-07-31.md) — `docs/ACCURACY_VALIDATION_2026-07-31.md`
- [商业软件质量与性能审计（2026-09-27）](COMMERCIAL_CODE_AUDIT_2026-09-27.md) — `docs/COMMERCIAL_CODE_AUDIT_2026-09-27.md`
- [Zemax 数值问题修复（2026-09-06）](NUMERICAL_REPAIR_2026-09-06.md) — `docs/NUMERICAL_REPAIR_2026-09-06.md`
- [正式优化入口与无效评价修复（2026-09-27）](OPTIMIZATION_ENTRY_FIX_2026-09-27.md) — `docs/OPTIMIZATION_ENTRY_FIX_2026-09-27.md`
- [优化与自动设计路线：文献和实现交叉复核（2026-09-27）](OPTIMIZATION_ROUTE_LITERATURE_REVIEW_2026-09-27.md) — `docs/OPTIMIZATION_ROUTE_LITERATURE_REVIEW_2026-09-27.md`
- [项目审查与 Zemax 数值对齐检查（2026-09-05）](PROJECT_AUDIT_2026-09-05.md) — `docs/PROJECT_AUDIT_2026-09-05.md`
- [项目审计修复与验证（2026-09-05）](PROJECT_REPAIR_2026-09-05.md) — `docs/PROJECT_REPAIR_2026-09-05.md`
- [项目理解与下一步建议（2026-09-23）](PROJECT_REVIEW_2026-09-23.md) — `docs/PROJECT_REVIEW_2026-09-23.md`
- [Python Optiland 移除审计与实施记录](PYTHON_OPTILAND_REMOVAL.md) — `docs/PYTHON_OPTILAND_REMOVAL.md`
- [可靠性与资源边界加固（2026-08-29）](RELIABILITY_HARDENING_2026-08-29.md) — `docs/RELIABILITY_HARDENING_2026-08-29.md`
- [保留引用逐文件索引](RETAINED_PYTHON_OPTILAND_REFERENCES.md) — `docs/RETAINED_PYTHON_OPTILAND_REFERENCES.md`
- [MS-L7 全分析数值对比扩展（2026-09-06）](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md) — `docs/ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md`
- [MS-L7 能量计算与相位对照修复（2026-09-06）](ZEMAX_ENERGY_REPAIR_2026-09-06.md) — `docs/ZEMAX_ENERGY_REPAIR_2026-09-06.md`
- [MS-L7 Huygens MTF 采样修复（2026-09-06）](ZEMAX_HUYGENS_REPAIR_2026-09-06.md) — `docs/ZEMAX_HUYGENS_REPAIR_2026-09-06.md`

## 初始结构阶段记录

- [初始结构实验室 v11：随真实光束调整镜片直径](INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md) — `docs/INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md`
- [初始结构实验室：实现方向复核（2026-09-27）](INITIAL_STRUCTURE_DIRECTION_REVIEW_2026-09-27.md) — `docs/INITIAL_STRUCTURE_DIRECTION_REVIEW_2026-09-27.md`
- [DSEARCH 形式搜索改造（2026-09-26）](INITIAL_STRUCTURE_DSEARCH_2026-09-26.md) — `docs/INITIAL_STRUCTURE_DSEARCH_2026-09-26.md`
- [DSEARCH 路径修正 R1：统一真实光线目标与连续求导域](INITIAL_STRUCTURE_DSEARCH_R1_2026-09-26.md) — `docs/INITIAL_STRUCTURE_DSEARCH_R1_2026-09-26.md`
- [DSEARCH 路径修正 R2：缩放与正则化局部步长](INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md) — `docs/INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md`
- [DSEARCH 实现路径复核（2026-09-26）](INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md) — `docs/INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md`
- [初始结构实验室：全视场细化入口与渐进路径复核](INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md) — `docs/INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md`
- [初始结构实验室：文献复核、瞄准设置修复与剩余质量缺口](INITIAL_STRUCTURE_LITERATURE_AUDIT_2026-09-27.md) — `docs/INITIAL_STRUCTURE_LITERATURE_AUDIT_2026-09-27.md`
- [初始结构实验室 v13：让材料和光阑邻域获得实际预算](INITIAL_STRUCTURE_NEIGHBORHOODS_2026-09-26.md) — `docs/INITIAL_STRUCTURE_NEIGHBORHOODS_2026-09-26.md`
- [精确平板启动：P0 / P1 实施与验证记录](INITIAL_STRUCTURE_P1_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P1_2026-09-07.md`
- [精确平板生成：共享计算内核与 P2 完整目标流程](INITIAL_STRUCTURE_P2_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P2_2026-09-07.md`
- [P3 多家族搜索：实施与验证记录](INITIAL_STRUCTURE_P3_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P3_2026-09-07.md`
- [P4 独立实验室界面接入](INITIAL_STRUCTURE_P4_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P4_2026-09-07.md`
- [P5：困难规格修复与首轮完整验收（未达发布门槛）](INITIAL_STRUCTURE_P5_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P5_2026-09-07.md`
- [P5 续项：孔径恢复与多色继续优化](INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md) — `docs/INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md`
- [实体光阑与共享 Core 瞄准修复（2026-09-27）](INITIAL_STRUCTURE_PHYSICAL_STOP_2026-09-27.md) — `docs/INITIAL_STRUCTURE_PHYSICAL_STOP_2026-09-27.md`
- [实体光阑接入初始结构搜索（2026-09-27）](INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md) — `docs/INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md`
- [全视场失追迹后的阶段恢复（2026-09-27）](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md) — `docs/INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md`
- [初始结构实验室：光学质量与结构合理性复核](INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md) — `docs/INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md`
- [初始结构实验室 v12：面积积分、耦合几何约束和实际渐进阶段](INITIAL_STRUCTURE_QUALITY_FIX_2026-09-26.md) — `docs/INITIAL_STRUCTURE_QUALITY_FIX_2026-09-26.md`
- [S1 通用局部求解器实施记录](INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md) — `docs/INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md`
- [正式数值回归与初始结构 S2–S4 实施记录](INITIAL_STRUCTURE_S2_S4_2026-09-23.md) — `docs/INITIAL_STRUCTURE_S2_S4_2026-09-23.md`
- [初始结构自动搜索：策略整改](INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md) — `docs/INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md`
- [初始结构实验室 v14：减少重复求导，让同一预算完成更多优化步](INITIAL_STRUCTURE_SECANT_SOLVER_2026-09-27.md) — `docs/INITIAL_STRUCTURE_SECANT_SOLVER_2026-09-27.md`
- [初始结构实验室：宽视场失败路径与搜索预算复核](INITIAL_STRUCTURE_STAGE_AUDIT_2026-09-27.md) — `docs/INITIAL_STRUCTURE_STAGE_AUDIT_2026-09-27.md`

## 实验室、工具与样例入口

- [光学镀膜设计实验室](../labs/CoatingDesign/README.md) — `labs/CoatingDesign/README.md`
- [Initial Structure Lab](../labs/InitialStructure/README.md) — `labs/InitialStructure/README.md`
- [Fixed-protocol search evidence](../labs/InitialStructure/tools/OptilandWorkbench.InitialStructure.Benchmarks/README.md) — `labs/InitialStructure/tools/OptilandWorkbench.InitialStructure.Benchmarks/README.md`
- [Inno Setup resources](../packaging/windows/inno/SOURCE.md) — `packaging/windows/inno/SOURCE.md`
- [测试镜头样例](../samples/lenses/README.md) — `samples/lenses/README.md`
- [非序列功能测试与教学样例](../samples/non-sequential/README.md) — `samples/non-sequential/README.md`
- [内置玻璃目录](../src/OptilandWorkbench.Core/Materials/Data/README.md) — `src/OptilandWorkbench.Core/Materials/Data/README.md`
- [Workbench accuracy recapture](../tools/OptilandWorkbench.AccuracyCapture/README.md) — `tools/OptilandWorkbench.AccuracyCapture/README.md`
- [C# Zemax / Workbench comparison](../tools/OptilandWorkbench.ZemaxComparison/README.md) — `tools/OptilandWorkbench.ZemaxComparison/README.md`
- [Zemax 一致性采集工具](../tools/zemax_parity/README.md) — `tools/zemax_parity/README.md`

## 基准与验证资料

- [Initial Structure Lab frozen specifications](../labs/InitialStructure/benchmarks/README.md) — `labs/InitialStructure/benchmarks/README.md`
- [v11 automatic lens diameter evidence](../labs/InitialStructure/benchmarks/automatic-diameter-20260926/README.md) — `labs/InitialStructure/benchmarks/automatic-diameter-20260926/README.md`
- [R1 runtime mechanism verification](../labs/InitialStructure/benchmarks/dsearch-r1-20260926/README.md) — `labs/InitialStructure/benchmarks/dsearch-r1-20260926/README.md`
- [R2 scaled regularized local solver](../labs/InitialStructure/benchmarks/dsearch-r2-20260926/README.md) — `labs/InitialStructure/benchmarks/dsearch-r2-20260926/README.md`
- [DSEARCH route audit — diagnostic evidence](../labs/InitialStructure/benchmarks/dsearch-route-audit-20260926/README.md) — `labs/InitialStructure/benchmarks/dsearch-route-audit-20260926/README.md`
- [v8 DSEARCH form-search evidence](../labs/InitialStructure/benchmarks/dsearch-v8-20260926/README.md) — `labs/InitialStructure/benchmarks/dsearch-v8-20260926/README.md`
- [Full-field restart and early-field continuation evidence — 2026-09-27](../labs/InitialStructure/benchmarks/field-continuation-20260927/README.md) — `labs/InitialStructure/benchmarks/field-continuation-20260927/README.md`
- [Independent holdout v1](../labs/InitialStructure/benchmarks/holdout-v1/README.md) — `labs/InitialStructure/benchmarks/holdout-v1/README.md`
- [v16 literature and candidate-quality audit](../labs/InitialStructure/benchmarks/literature-audit-20260927/README.md) — `labs/InitialStructure/benchmarks/literature-audit-20260927/README.md`
- [v13 neighborhood allocation evidence — 2026-09-26](../labs/InitialStructure/benchmarks/neighborhood-budget-20260926/README.md) — `labs/InitialStructure/benchmarks/neighborhood-budget-20260926/README.md`
- [Physical-stop diagnostic, 2026-09-27](../labs/InitialStructure/benchmarks/physical-stop-20260927/README.md) — `labs/InitialStructure/benchmarks/physical-stop-20260927/README.md`
- [Physical-stop search integration, v18](../labs/InitialStructure/benchmarks/physical-stop-search-20260927/README.md) — `labs/InitialStructure/benchmarks/physical-stop-search-20260927/README.md`
- [Pupil strategy and progressive recovery, 2026-09-27](../labs/InitialStructure/benchmarks/pupil-continuation-20260927/README.md) — `labs/InitialStructure/benchmarks/pupil-continuation-20260927/README.md`
- [v11 optical quality audit](../labs/InitialStructure/benchmarks/quality-audit-20260926/README.md) — `labs/InitialStructure/benchmarks/quality-audit-20260926/README.md`
- [v12 optical-quality repair evidence](../labs/InitialStructure/benchmarks/quality-fix-20260926/README.md) — `labs/InitialStructure/benchmarks/quality-fix-20260926/README.md`
- [S2–S4 evidence, 2026-09-23–26](../labs/InitialStructure/benchmarks/s2-s4-20260926/README.md) — `labs/InitialStructure/benchmarks/s2-s4-20260926/README.md`
- [v14 guarded secant solver evidence — 2026-09-27](../labs/InitialStructure/benchmarks/secant-solver-20260927/README.md) — `labs/InitialStructure/benchmarks/secant-solver-20260927/README.md`
- [Wide-field stage audit — 2026-09-27](../labs/InitialStructure/benchmarks/stage-recovery-20260927/README.md) — `labs/InitialStructure/benchmarks/stage-recovery-20260927/README.md`
- [镀膜实验室验收记录 · 2026-09-27](../validation/coating/README.md) — `validation/coating/README.md`
- [Zemax / Workbench 数值比较](../validation/zemax-2026-r1/initial-structure-20260907/1-element/comparison/COMPARISON_REPORT.md) — `validation/zemax-2026-r1/initial-structure-20260907/1-element/comparison/COMPARISON_REPORT.md`
- [Zemax / Workbench 数值比较](../validation/zemax-2026-r1/initial-structure-20260907/2-element/comparison/COMPARISON_REPORT.md) — `validation/zemax-2026-r1/initial-structure-20260907/2-element/comparison/COMPARISON_REPORT.md`
- [Zemax / Workbench 数值比较](../validation/zemax-2026-r1/initial-structure-20260907/3-element/comparison/COMPARISON_REPORT.md) — `validation/zemax-2026-r1/initial-structure-20260907/3-element/comparison/COMPARISON_REPORT.md`
- [Generated initial structures: Zemax 2026 R1 reference](../validation/zemax-2026-r1/initial-structure-20260907/README.md) — `validation/zemax-2026-r1/initial-structure-20260907/README.md`
- [2026-09-06 能量分析独立复验](../validation/zemax/2026-r1/energy-repair-2026-09-06/README.md) — `validation/zemax/2026-r1/energy-repair-2026-09-06/README.md`
- [Huygens MTF sampling evidence — 2026-09-06](../validation/zemax/2026-r1/huygens-mtf-sampling-2026-09-06/README.md) — `validation/zemax/2026-r1/huygens-mtf-sampling-2026-09-06/README.md`
- [Frozen MS-L7 native captures, OpticStudio 2026 R1](../validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/README.md) — `validation/zemax/2026-r1/ms-l7-analysis-expansion-2026-09-06/README.md`
- [2026-09-06 数值修复补充捕获](../validation/zemax/2026-r1/numerical-repair-2026-09-06/README.md) — `validation/zemax/2026-r1/numerical-repair-2026-09-06/README.md`

## 外部与冻结参考入口

- [Optiland 0.5.8 冻结辅助历史](../validation/history/optiland-0.5.8/README.md)：只供既有测试参考，不参与产品运行，不新增比较。
- 正式外部精度权威为提交的 `artifacts/zemax/123456-zemax-2026-r1-baseline`，仅适用于捕获文件、设置及 OpticStudio 2026 R1。
- `third_party/` 保留上游固定源码和许可证；本轮不改写上游文档。各产品接入边界由相应实验室指南说明。

## 镀膜工作流快捷入口

[启动与三个可运行示例](../labs/CoatingDesign/README.md) → [本地复用与共享计算契约](COATING_DESIGN_LAB.md) → [代码入口](CODE_READING_MAP.md#镀膜实验室代码入口2026-09-27) → [独立实验格式](FILE_FORMATS_AND_PLUGINS.md#镀膜实验文件) → [实际测试与未达标项](../validation/coating/README.md) → [上游归档、版本和许可证](../third_party/coating-reference/README.md)。

镀膜完整 Debug/Release 各 29/29 与初始结构实验室 v19 的 260/260 属于不同测试集；前者当前只使用共享 Light 资源，不能套用正式 App 四主题或原生窗口的验收结论。
