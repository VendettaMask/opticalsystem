# 项目文档索引

整理日期：2026-10-14。先按任务阅读当前指南；带日期的记录只证明对应阶段，不能把旧通过数、旧独立模块架构或旧命令当作当前状态。

## 从这里开始

| 需要做什么 | 入口 |
| --- | --- |
| 了解产品、当前包和未解决问题 | [项目首页](../README.md)、[当前状态](CURRENT_STATUS.md)、[问题清单](OPEN_ISSUES_2026-10-08.md) |
| 构建、测试、安装与恢复 | [构建发布](BUILD_AND_RELEASE.md)、[Windows 安装](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)、[内置模块交付](BUILT_IN_MODULES_2026-10-13.md) |
| 使用桌面和两个模块 | [桌面工作流](GUI_QUICKSTART_REFACTOR.md)、[初始结构](../labs/InitialStructure/README.md)、[镀膜设计](../labs/CoatingDesign/README.md) |
| 维护代码与界面 | [架构](ARCHITECTURE.md)、[代码地图](CODE_READING_MAP.md)、[UI 规范](UI_DESIGN_SPEC.md)、[协作约束](../AGENTS.md) |
| 查询文件和数值边界 | [文件格式](FILE_FORMATS_AND_PLUGINS.md)、[数值验证](NUMERICAL_PARITY.md)、[操作数支持](ZEMAX_OPERAND_SUPPORT.md) |

## 当前指南与功能契约

- [系统架构](ARCHITECTURE.md)
- [应用品牌资源](BRANDING.md)
- [构建与发布](BUILD_AND_RELEASE.md)
- [初始结构与镀膜设计：统一产品交付（2026-10-13）](BUILT_IN_MODULES_2026-10-13.md)
- [镀膜设计计算与实现契约](COATING_DESIGN_LAB.md)
- [全仓库代码阅读地图](CODE_READING_MAP.md)
- [当前实现与验证状态](CURRENT_STATUS.md)
- [文件格式与插件](FILE_FORMATS_AND_PLUGINS.md)
- [GUI 工作流与重构对照](GUI_QUICKSTART_REFACTOR.md)
- [初始结构内置模块：范围与后续计划](INITIAL_STRUCTURE_LAB_PLAN.md)
- [打包镜头库](LENS_LIBRARY.md)
- [本地矢量图标库](LOCAL_ICONS.md)
- [可制造性审查与光学制图](MANUFACTURING_DRAWINGS.md)
- [非序列工作模式与光线追迹](NON_SEQUENTIAL_TRACING.md)
- [非序列第二阶段：杂散光基础链路](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)
- [非序列光源](NONSEQUENTIAL_SOURCES.md)
- [非序列模式 Zemax 工作流对齐路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md)
- [数值验证与冻结历史回归](NUMERICAL_PARITY.md)
- [计算精度与发布待解决问题分类](OPEN_ISSUES_2026-10-08.md)
- [光学装调实验室](OPTICAL_ASSEMBLY_LAB.md)
- [优化算法真实性与兼容策略](OPTIMIZATION_ALGORITHMS.md)
- [产品能力与格式兼容矩阵](PARITY_MATRIX.md)
- [镜头数据求解入口](RADIUS_SOLVE_EDITOR.md)
- [大规模光线追迹性能](RAY_TRACING_PERFORMANCE.md)
- [赛德尔系数报告（2026-09-04）](SEIDEL_COEFFICIENT_REPORT.md)
- [STAROPT 工程格式](STAROPT_FILE_FORMAT.md)
- [表面类型与属性分类面板](SURFACE_PROPERTIES_EDITOR.md)
- [主题包开发规范](THEME_PACKAGES.md)
- [公差分析](TOLERANCING.md)
- [UI 设计规范](UI_DESIGN_SPEC.md)
- [Windows 未签名交付与升级保护（2026-10-12）](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)
- [Zemax 分析设置与实现方式参考](ZEMAX_ANALYSIS_REFERENCE.md)
- [Zemax 基准配置边界](ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md)
- [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)

## 模块、工具和样例入口

- [Project collaboration instructions](../AGENTS.md)
- [光学镀膜设计（产品内置模块）](../labs/CoatingDesign/README.md)
- [初始结构（产品内置模块）](../labs/InitialStructure/README.md)
- [Fixed-protocol search evidence](../labs/InitialStructure/tools/OptilandWorkbench.InitialStructure.Benchmarks/README.md)
- [Optical System Design](../README.md)
- [测试镜头样例](../samples/lenses/README.md)
- [非序列功能测试与教学样例](../samples/non-sequential/README.md)
- [内置玻璃目录](../src/OptilandWorkbench.Core/Materials/Data/README.md)
- [Workbench 历史重算命令](../tools/OptilandWorkbench.ZemaxComparison/README.md#historical-workbench-recapture)
- [C# Zemax / Workbench comparison](../tools/OptilandWorkbench.ZemaxComparison/README.md)
- [冻结证据与准备输入验收](../tools/validation/README.md)
- [Zemax 一致性采集工具](../tools/zemax_parity/README.md)

另外保留 [Windows 随包说明](../packaging/windows/README.txt) 和 [用户镜头原件目录说明](../local-data/lens-library/originals/user-zmx/README.txt)。外部验证工具不是产品运行依赖，普通构建不会启动 Zemax。

## 历史报告与阶段提案

以下报告保留原始设置、计数、差距和失败尝试，不是当前待办的自动授权。历史独立 App 命令仅用于溯源，现行构建均走主程序。

<details>
<summary>初始结构方法、求解与搜索阶段</summary>

- [初始结构实验室 v11：随真实光束调整镜片直径](INITIAL_STRUCTURE_AUTOMATIC_DIAMETER_2026-09-26.md)
- [初始结构实验室：实现方向复核（2026-09-27）](INITIAL_STRUCTURE_DIRECTION_REVIEW_2026-09-27.md)
- [DSEARCH 形式搜索改造（2026-09-26）](INITIAL_STRUCTURE_DSEARCH_2026-09-26.md)
- [DSEARCH 路径修正 R1：统一真实光线目标与连续求导域](INITIAL_STRUCTURE_DSEARCH_R1_2026-09-26.md)
- [DSEARCH 路径修正 R2：缩放与正则化局部步长](INITIAL_STRUCTURE_DSEARCH_R2_2026-09-26.md)
- [DSEARCH 实现路径复核（2026-09-26）](INITIAL_STRUCTURE_DSEARCH_ROUTE_AUDIT_2026-09-26.md)
- [从平板生成定焦初始结构：产品方案与开发计划](INITIAL_STRUCTURE_FROM_FLAT_PROPOSAL.md)
- [初始结构实验室：全视场细化入口与渐进路径复核](INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md)
- [初始结构实验室：文献复核、瞄准设置修复与剩余质量缺口](INITIAL_STRUCTURE_LITERATURE_AUDIT_2026-09-27.md)
- [初始结构实验室 v13：让材料和光阑邻域获得实际预算](INITIAL_STRUCTURE_NEIGHBORHOODS_2026-09-26.md)
- [精确平板启动：P0 / P1 实施与验证记录](INITIAL_STRUCTURE_P1_2026-09-07.md)
- [精确平板生成：共享计算内核与 P2 完整目标流程](INITIAL_STRUCTURE_P2_2026-09-07.md)
- [P3 多家族搜索：实施与验证记录](INITIAL_STRUCTURE_P3_2026-09-07.md)
- [P4 独立实验室界面接入](INITIAL_STRUCTURE_P4_2026-09-07.md)
- [P5：困难规格修复与首轮完整验收（未达发布门槛）](INITIAL_STRUCTURE_P5_2026-09-07.md)
- [P5 续项：孔径恢复与多色继续优化](INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md)
- [实体光阑与共享 Core 瞄准修复（2026-09-27）](INITIAL_STRUCTURE_PHYSICAL_STOP_2026-09-27.md)
- [实体光阑接入初始结构搜索（2026-09-27）](INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md)
- [全视场失追迹后的阶段恢复（2026-09-27）](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)
- [初始结构实验室：光学质量与结构合理性复核](INITIAL_STRUCTURE_QUALITY_AUDIT_2026-09-26.md)
- [初始结构实验室 v12：面积积分、耦合几何约束和实际渐进阶段](INITIAL_STRUCTURE_QUALITY_FIX_2026-09-26.md)
- [S1 通用局部求解器实施记录](INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md)
- [正式数值回归与初始结构 S2–S4 实施记录](INITIAL_STRUCTURE_S2_S4_2026-09-23.md)
- [初始结构自动搜索：策略整改](INITIAL_STRUCTURE_SEARCH_STRATEGY_2026-09-08.md)
- [初始结构实验室 v14：减少重复求导，让同一预算完成更多优化步](INITIAL_STRUCTURE_SECANT_SOLVER_2026-09-27.md)
- [初始结构实验室：宽视场失败路径与搜索预算复核](INITIAL_STRUCTURE_STAGE_AUDIT_2026-09-27.md)

</details>

<details>
<summary>光学、UI、架构和可靠性阶段记录</summary>

- [精度与页面对比验证（2026-09-05 更新）](ACCURACY_VALIDATION_2026-07-31.md)
- [C# Zemax 比较工具首轮历史审计（2026-09-05）](ZEMAX_COMPARISON_TOOL.md)
- [旧、新架构收敛与单一结果链路修正计划](ARCHITECTURE_CONVERGENCE_PLAN.md)
- [软件可靠性问题修复 · 2026-10-05](AUDIT_RELIABILITY_FIXES_2026-10-05.md)
- [SVIG 自动渐晕](AUTOMATIC_VIGNETTING_2026-10-03.md)
- [BFSD 最佳拟合球面接入](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)
- [正蓝 × 雾蓝主题（2026-09-27）](BLUE_THEME_2026-09-27.md)
- [其他计算路径问题定位 2026年10月8日](CALCULATION_PATH_DIAGNOSIS_2026-10-08.md)
- [计算路径修复与结果对比 2026年10月8日](CALCULATION_PATH_REPAIR_2026-10-08.md)
- [CODA 与系统偏振设置](COATING_DATA_OPERAND_2026-10-03.md)
- [物理膜层参数与九项约束](COATING_LAYER_CONSTRAINTS_2026-10-03.md)
- [物理相干镀膜接入正式追迹](COHERENT_COATING_TRANSPORT_2026-10-02.md)
- [商业软件质量与性能审计（2026-09-27）](COMMERCIAL_CODE_AUDIT_2026-09-27.md)
- [商业质量计划第一阶段 · 2026-10-06](COMMERCIAL_PHASE1_2026-10-06.md)
- [紧凑顶部菜单与工具栏（2026-09-27）](COMPACT_TOOLBAR_2026-09-27.md)
- [确认缺陷修复与完整重算](CONFIRMED_ISSUE_REPAIR_2026-10-08.md)
- [第二十九批：指定方向矢高与延伸区](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)
- [FFT 能量网格完整性修复](FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.md)
- [FFT PSF：高 NA 镜头工作 F 数追迹失败修正](FFT_PSF_STOP_AIMING.md)
- [FFT 瞳面相位与边界修复](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)
- [FFT 采样和包围能量参考点修复](FFT_SAMPLING_REFERENCE_REPAIR_2026-10-09.md)
- [无偏振 FFT 振幅选择与剩余数值残差](FFT_SCALAR_AMPLITUDE_REPAIR_2026-10-11.md)
- [冻结证据的 Git 字节完整性修复](FROZEN_GIT_INTEGRITY_REPAIR_2026-10-11.md)
- [玻璃库列表右侧遮挡修复](GLASS_CATALOG_SIDEBAR_2026-10-05.md)
- [Gradient 5 空间折射率与色散基础](GRADIENT5_DISPERSION_2026-10-03.md)
- [GRIN 空间折射率与连续传播基础](GRIN_CONTINUOUS_PROPAGATION_2026-10-03.md)
- [GRIN 材料控制操作数](GRIN_CONTROL_OPERANDS_2026-10-03.md)
- [GRIN 材料编辑、变量与完整状态保存](GRIN_MATERIAL_EDITOR_2026-10-04.md)
- [GRIN 材料、顺序追迹与原生快照](GRIN_MATERIAL_TRANSPORT_2026-10-03.md)
- [GRIN 一阶传递、瞳孔与光阑瞄准](GRIN_PARAXIAL_AIMING_2026-10-03.md)
- [HYLD 与界面折射率追迹数据](HIGH_YIELD_OPERAND_2026-10-03.md)
- [Huygens 与 FFT 包围能量原设置残差修复](HUYGENS_FFT_ENERGY_REPAIR_2026-10-11.md)
- [惠更斯传播与复色合成和参考球修复](HUYGENS_IMPLEMENTATION_REPAIR_2026-10-09.md)
- [Huygens 离轴 PSF、MTF 网格和视场采样修复](HUYGENS_OFFAXIS_REPAIR_2026-10-11.md)
- [Huygens 非偏振残差与相干电场修复](HUYGENS_POLARIZATION_REPAIR_2026-10-12.md)
- [Huygens 标量球面传播与离轴复色认证](HUYGENS_SPHERICAL_REPAIR_2026-10-11.md)
- [RELI / EFNO 的共享照度基础](ILLUMINATION_CORE_2026-10-02.md)
- [RELI / EFNO 评价函数接入](ILLUMINATION_OPERANDS_2026-10-02.md)
- [照度的偏振功率链与像面入射边界](ILLUMINATION_POLARIZATION_2026-10-02.md)
- [像方方向余弦网格与轴上照度参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)
- [多配置单元格变量与联合优化](MCE_CELL_VARIABLES_2026-10-03.md)
- [多配置单元格拾取](MCE_PICKUP_SOLVES_2026-10-03.md)
- [多配置行表与 MCOV / MCOG / MCOL](MCE_ROW_OPERANDS_2026-10-03.md)
- [平铺子窗口交互与标题按钮修复](MDI_INTERACTION_FIX_2026-09-27.md)
- [机械半直径显示精度修复](MECHANICAL_PRECISION_2026-09-27.md)
- [指定频率 MTF 制造公差](MTF_TOLERANCING_2026-10-04.md)
- [多配置操作数 CONF / ZTHI](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)
- [Zemax 数值问题修复（2026-09-06）](NUMERICAL_REPAIR_2026-09-06.md)
- [官方说明对照与轴上参考和几何质心修复](OFFICIAL_DOCUMENTATION_REPAIR_2026-10-09.md)
- [操作数帮助层级导航](OPERAND_HELP_HIERARCHY_2026-10-01.md)
- [正式优化入口与无效评价修复（2026-09-27）](OPTIMIZATION_ENTRY_FIX_2026-09-27.md)
- [优化与自动设计路线：文献和实现交叉复核（2026-09-27）](OPTIMIZATION_ROUTE_LITERATURE_REVIEW_2026-09-27.md)
- [库存匹配与制图容器修复](PANEL_CONTENT_LAYOUT_2026-09-27.md)
- [共享复电场追迹与 RRET](POLARIZATION_RETARDANCE_2026-10-03.md)
- [FFT 准备输入来源与证据核验修复](PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.md)
- [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)
- [项目审查与 Zemax 数值对齐检查（2026-09-05）](PROJECT_AUDIT_2026-09-05.md)
- [项目审计修复与验证（2026-09-05）](PROJECT_REPAIR_2026-09-05.md)
- [项目理解与下一步建议（2026-09-23）](PROJECT_REVIEW_2026-09-23.md)
- [三维出瞳与物理平面刀口扩展](PUPIL_FOUCAULT_EXTENSION_2026-10-12.md)
- [出瞳形状投影与物理 Foucault 刀口实现](PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.md)
- [Python Optiland 移除审计与实施记录](PYTHON_OPTILAND_REMOVAL.md)
- [光线发射坐标基准修复](RAY_LAUNCH_COORDINATE_REPAIR_2026-10-09.md)
- [可靠性与资源边界加固（2026-08-29）](RELIABILITY_HARDENING_2026-08-29.md)
- [Huygens 与衍射包围能量残差分层诊断](RESIDUAL_DIAGNOSIS_2026-10-11.md)
- [RMS 采样参数与偏振链路修正 · 2026-10-08](RMS_SAMPLING_POLARIZATION_REPAIR_2026-10-08.md)
- [系统未完成能力收口计划](SYSTEM_COMPLETION_PLAN_2026-09-02.md)
- [默认只打开镜头数据与二维视图](TWO_PAGE_STARTUP_2026-09-27.md)
- [UI 审查问题修复（2026-09-27—28）](UI_AUDIT_FIXES_2026-09-27.md)
- [UI 符合性审计](UI_CONFORMANCE_AUDIT_2026-08-04.md)
- [UI 设计走查记录](UI_DESIGN_REVIEW.md)
- [波前图修复验收报告 2026年10月8日](WAVEFRONT_ACCEPTANCE_2026-10-08.md)
- [波前图系统瞄准修复 2026年10月8日](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)
- [38668 导入后的公共 Working F/# 修复](ZEMAX_38668_WORKING_F_NUMBER.md)
- [MS-L7 全分析数值对比扩展（2026-09-06）](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md)
- [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)
- [MS-L7 能量计算与相位对照修复（2026-09-06）](ZEMAX_ENERGY_REPAIR_2026-09-06.md)
- [MS-L7 Huygens MTF 采样修复（2026-09-06）](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)
- [Zemax 操作数名称与支持口径复核](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)
- [波前瞄准、带符号渐晕与 RA 网格诊断 · 2026-10-08](ZEMAX_PUPIL_DIAGNOSIS_2026-10-08.md)
- [RA256、多文件光阑接纳与单光线路径复验 · 2026-10-07](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)
- [RMS 光斑视场采样修复与多文件复验 · 2026-10-07](ZEMAX_RMS_FIELD_SAMPLING_REPAIR_2026-10-07.md)
- [标准镜头 OPD 修复与复验 · 2026-10-06](ZEMAX_STANDARD_SAMPLE_OPD_REPAIR_2026-10-06.md)
- [Tessar RMS 积分与连续渐晕修复 · 2026-10-07](ZEMAX_TESSAR_RMS_REPAIR_2026-10-07.md)
- [Tessar 渐晕与相位复验 · 2026-10-06](ZEMAX_TESSAR_VIGNETTING_REPAIR_2026-10-06.md)
- [Calculation path diagnosis 2026年10月8日](../tools/diagnostics/CalculationPaths20261008/README.md)
- [Huygens spherical, spectral and experimental polarization controls](../tools/diagnostics/HuygensExtensions20261011/README.md)
- [Huygens 完整光线平面参考诊断](../tools/diagnostics/HuygensReference20261010/README.md)
- [Diffraction energy window controls](../tools/diagnostics/IssueRepair20261008/README.md)
- [N02 分层诊断工具](../tools/diagnostics/N02Layers20261009/README.md)
- [Pupil comparison diagnostic · 2026-10-08](../tools/diagnostics/PupilComparison20261008/README.md)
- [N01/N02 residual controls and captured-settings verification](../tools/diagnostics/Residuals20261011/README.md)
- [Native factor inspection](../tools/diagnostics/SignedVignetting20261008/README.md)

</details>

<details>
<summary>历次源码同步记录</summary>

- [远端同步复验 · 2026-10-05](PROJECT_SYNC_2026-10-05.md)
- [可靠性与 Zemax 多文件修复同步 · 2026-10-07](PROJECT_SYNC_2026-10-07.md)
- [计算路径修复与问题清单同步 2026年10月8日](PROJECT_SYNC_2026-10-08.md)
- [光学计算修复和后续计划远端同步](PROJECT_SYNC_2026-10-09.md)
- [远端更新与本机同步](PROJECT_SYNC_2026-10-12.md)
- [FFT 相位修复与原生证据同步](PROJECT_SYNC_FFT_PUPIL_2026-10-09.md)
- [出瞳与物理 Foucault 同步完成记录](PROJECT_SYNC_PUPIL_FOUCAULT_2026-10-10.md)
- [工程与安装恢复收尾同步（2026-10-12）](PROJECT_SYNC_RECOVERY_2026-10-12.md)

</details>

原构建指南的 62 段带日期验收原文合并到 [历史构建账本](validation/BUILD_VERIFICATION_HISTORY_2026-10-13.json)。原静态逐文件代码表由实时文件命令取代；旧 Python/Optiland 引用展开表并入 [保留用途说明](PYTHON_OPTILAND_REMOVAL.md#保留引用的用途与历史清单)，大文件拆分记录并入 [架构维护](ARCHITECTURE.md#拆分与架构维护)。两个冗余 Markdown 文件已删除，Git 中保留原文。

## 冻结证据与第三方资料

- [初始结构基准入口](../labs/InitialStructure/benchmarks/README.md)：固定规格、种子、预算与原协议。
- [镀膜历史验收](../validation/coating/README.md) 与 [上游源和授权](../third_party/coating-reference/README.md)。
- [Optiland 冻结辅助历史](../validation/history/optiland-0.5.8/README.md)：仅回归/溯源，不再生成外部对照。
- [主要 Zemax 数值权威](ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md)：仅认证捕获文件、设置和版本。
- [既有六镜头报告](../reports/Zemax_Workbench_Six_Lens_Comparison_Report_2026-10-08.docx) 与 [当日修复 PDF](../output/pdf/Zemax_Workbench_Comparison_Repair_2026-10-08.pdf)：历史报告原件，不改写成新验收。

原始报告、许可证、来源声明和冻结数据保持原字节。当前文档若引用本地安装包或截图，会明确它没有随 Git 分发；已缺失的历史产物只记录原路径和缺失状态，不伪造文件。清单与核验结果见 [文档审计账本](validation/DOCUMENTATION_AUDIT_2026-10-13.json)。

## 后续维护规则

当前测试状态只汇总在状态页并链接对应证据；专题页不再复制无关阶段的验收广播。更新行为时修改对应正文，而不是在过时正文前不断加免责声明。已失效的执行命令应修正或明确归入历史；合并/删除必须同时修复反向链接，并保留有价值的失败证据。

检查命令：`pwsh -File scripts/verify-documentation.ps1 -Strict -OutputPath .tmp/documentation-audit.json`。它检查本地链接、围栏、指纹及至少 100 字符的跨文档段落重复；`-Strict` 会拒绝两份现行指南之间的正文重复，历史警示与混合范围仍报告而不删除。代码块和表格不参与正文重复判定；它不判断语义近似、不在线核验网址、不重跑产品测试，也不认证历史数值。检查器回归命令为 `pwsh -File scripts/tests/verify-documentation.tests.ps1`；两项检查已配置到三平台 CI 的独立 Documentation 任务，远端运行结果需另行核验。
