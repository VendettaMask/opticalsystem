# 项目文档索引

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

最新：[多配置单元格拾取（第三十八批）](MCE_PICKUP_SOLVES_2026-10-03.md)，含实现范围、格式 v7、验证及真实控件渲染。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

- [多配置行表与 MCO 操作数](MCE_ROW_OPERANDS_2026-10-03.md)：绑定行、配置取值、编辑引用及 STAROPT v5。

- [多配置操作数 CONF / ZTHI](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)：隔离配置求值、优化候选链接及保存边界。

同步日期：2026-10-03。索引汇集项目自有 Markdown（包括本索引与状态页）；第三方原文、许可证和 `validation/history` 冻结数据不做重写。不同阶段报告保留各自范围，日期较近不代表已经完成全量验收。

先读 [当前实现与验证状态](CURRENT_STATUS.md)，再按任务进入具体指南。桌面使用看 [GUI 工作流](GUI_QUICKSTART_REFACTOR.md)，维护界面看 [UI 规范](UI_DESIGN_SPEC.md) 与 [主题包规范](THEME_PACKAGES.md)，构建看 [构建与发布](BUILD_AND_RELEASE.md)，定位代码看 [代码地图](CODE_READING_MAP.md)。

## 阅读规则

- 2026-10-03 [物理膜层参数约束](COATING_LAYER_CONSTRAINTS_2026-10-03.md)：335 项受限执行，九项新增；编辑、保存撤销及真实膜层变量优化。默认 Debug/Release 各 **3089/3089**，最终定向 71/71 通过（含两项窗口渲染）。

- 2026-10-03 [CODA 与系统偏振设置](COATING_DATA_OPERAND_2026-10-03.md)（前一阶段）：326 项受限执行，定向 179/179、最终 UI 2/2 通过，默认输出 Debug/Release 合并回归各 **3034/3034**（前批 2962 + 新增 72），零失败、零跳过，构建零警告、零错误；原生相位及文件映射仍待核实。

- 2026-10-03 [复电场追迹与 RRET](POLARIZATION_RETARDANCE_2026-10-03.md)（前一阶段）：325 项受限执行；定向 120/120 已通过，默认输出 Debug/Release 合并回归各 **2962/2962**（前批 2915 + 新增 47），零失败、零跳过，构建零警告、零错误。原生数值、应力双折射和全局设置仍有缺口。

- 2026-10-03 [HYLD 与界面折射率](HIGH_YIELD_OPERAND_2026-10-03.md)（前一阶段）：新增普通折射 HYLD，共 324 项受限执行；默认输出 Debug/Release 合并回归各 **2915/2915**（前批 2869 + 新增 46），零失败、零跳过，构建零警告、零错误；原生捕获仍待完成。
- 2026-10-03 [GRIN 材料控制操作数](GRIN_CONTROL_OPERANDS_2026-10-03.md)：前一阶段新增 21 项；该阶段默认 Debug/Release 各 2869 项通过，独立帮助渲染 2/2、8 张图像。
- 2026-10-03 [GRIN 一阶传递与瞄准](GRIN_PARAXIAL_AIMING_2026-10-03.md)（前阶段）：近轴矩阵、正反向路径、公共瞳孔/光阑瞄准、已有操作数及 DLS；修复 FloatByStopSize 的光阑尺寸漂移。默认 Debug/Release 合并回归各 2799/2799（含 GRIN 105 项），操作数数量不变。
- 2026-10-03 [GRIN 材料与正式顺序追迹](GRIN_MATERIAL_TRANSPORT_2026-10-03.md)（前阶段）：空间材料、局部界面相互作用、严格 STAROPT 保存、缓存及未支持入口约束；默认 Debug/Release 合并回归各 2728/2728，含 GRIN 65 项；操作数支持数量不变。
- 2026-10-03 [GRIN 空间折射率与连续传播基础](GRIN_CONTINUOUS_PROPAGATION_2026-10-03.md)（前阶段）：共享 C# 模型、自适应曲线/光程积分与解析验证；该阶段处方、文件和操作数接入尚未完成，支持数量不变；合并回归各 2594/2594，最后修复后的 GRIN 定向各 36/36，分别保留验证范围。

- 2026-10-02 [像方网格与轴上参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)：显式均匀角度网格、正式追迹求逆、轴上归一化及尚未消除的原生差异；定向 Debug/Release 各 193/193。

- 2026-10-02 [物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)：共享复振幅接入正式顺序追迹、照度与严格材料快照；明确实验室应用、偏振 PSF 和 RELI/EFNO 的未完成边界。

- [操作数帮助层级导航](OPERAND_HELP_HIERARCHY_2026-10-01.md)：按功能、操作数族、代码逐级浏览，含实际控件渲染。

- 2026-10-02 [顺序操作数第二十六批](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)：DIST/DISA 本地执行及网格非径向畸变修正；298 项受限执行、78 项已知功能待接通；Debug/Release 各 2308/2308。
- 2026-10-02 [顺序操作数第二十五批](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十五批对比度操作数与共享损失图部分完成)：MECA 本地执行、共同参考波前对比度与共享损失图；296 项受限执行、80 项已知功能待接通；Debug/Release 各 2264/2264。
- 2026-10-02 [顺序操作数第二十四批](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十四批-zern-与共享拟合修正部分完成)：ZERN 本地路径、Noll 编号与稳定环形拟合；295 项受限执行，81 项已知功能待接通；Debug/Release 各 2197/2197。
- 2026-10-01 [顺序操作数第二十三批](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十三批视场状态与完整瞳孔变换部分完成)：FDMO/FDRE/REQS、共享五因子瞳孔变换与状态保存；294 项受限执行，82 项已知功能仍待接通；Debug/Release 各 2130/2130。
- 2026-10-01 [操作数名称核对](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)：383 名称全部匹配官方 API，4 项本程序扩展已明确标识；当时 291 项受限执行及 2057/2057 测试保留为审计阶段记录，最新范围见第二十四批。

- [光学装调实验室](OPTICAL_ASSEMBLY_LAB.md)：当前镜头的逐面顶点像/球心像，支持无穷远与附加测量物镜两种叉丝模拟，复用正式 Core。

- 当前指南描述已实现行为；计划、兼容入口和未完成能力必须以文内状态为准。
- 阶段记录中的构建日期、测试数、截图和光学比较只证明该阶段列明的范围；不能相加，不能把局部通过当成全量发布门禁通过。
- UI 当前状态包括默认双页启动、240–280 DIP 紧凑侧栏且仅纵向滚动、明确的蓝色主动作、统一禁用说明、材料非空蓝行、三位数字显示、2 DIP 二维镜片边缘、MDI 操作和局部容器测量修复。材料浏览统一为“材料库”Dock，旧玻璃页迁移和去重见 [GUI 工作流](GUI_QUICKSTART_REFACTOR.md#材料库)。
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

- [UI 审查问题修复（09-27 开始，09-28 完成验证）](UI_AUDIT_FIXES_2026-09-27.md) — `docs/UI_AUDIT_FIXES_2026-09-27.md`

- [正蓝 × 雾蓝主题（2026-09-27）](BLUE_THEME_2026-09-27.md) — `docs/BLUE_THEME_2026-09-27.md`
- [紧凑顶部菜单与工具栏（2026-09-27）](COMPACT_TOOLBAR_2026-09-27.md) — `docs/COMPACT_TOOLBAR_2026-09-27.md`
- [平铺子窗口交互与标题按钮修复](MDI_INTERACTION_FIX_2026-09-27.md) — `docs/MDI_INTERACTION_FIX_2026-09-27.md`
- [机械半直径显示精度修复](MECHANICAL_PRECISION_2026-09-27.md) — `docs/MECHANICAL_PRECISION_2026-09-27.md`
- [库存匹配与制图容器修复](PANEL_CONTENT_LAYOUT_2026-09-27.md) — `docs/PANEL_CONTENT_LAYOUT_2026-09-27.md`
- [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md) — `docs/PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md`
- [玻璃库列表右侧遮挡修复](GLASS_CATALOG_SIDEBAR_2026-10-05.md) — `docs/GLASS_CATALOG_SIDEBAR_2026-10-05.md`；实际控件截图与局部回归。
- [远端同步复验](PROJECT_SYNC_2026-10-05.md) — 最终默认构建、累计 Release、装调/玻璃库与镀膜双配置、证据保存范围。
- [默认只打开镜头数据与二维视图](TWO_PAGE_STARTUP_2026-09-27.md) — `docs/TWO_PAGE_STARTUP_2026-09-27.md`

## 光学与全仓阶段记录

- [照度偏振与像面入射边界（2026-10-02）](ILLUMINATION_POLARIZATION_2026-10-02.md) — 透明介质非偏振光功率链；复杂介质、原生采样及操作数联动仍待完成。
- [RELI/EFNO 共享照度基础（2026-10-02）](ILLUMINATION_CORE_2026-10-02.md) — 内部透过率、遮挡、像面投影及验证边界；两项操作数仍未接通。

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
- [镀膜实验室验收记录 · 2026-09-28](../validation/coating/README.md) — `validation/coating/README.md`
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

镀膜当前 Debug/Release 各 44/44、正式相关 Release 226/226，与初始结构实验室 v19 的 260/260 是不同测试集。共享五种主题选择、材料表管理、有限结构搜索、多腔及公差已验证；macOS 原生走查单独记录，不能代替 Windows/Linux 或安装包验收。

- [指定方向矢高与延伸区](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)：TSAG、本地 ChipZone、制造边界及第二十九批验证。

- [多配置单元格变量与联合优化（2026-10-03）](MCE_CELL_VARIABLES_2026-10-03.md)：四类变量、配置身份、求解联动、STAROPT v6 和验证边界。

- [SVIG 自动渐晕与第三十九批验证](AUTOMATIC_VIGNETTING_2026-10-03.md)

- [Gradient 5 空间材料与色散基础](GRADIENT5_DISPERSION_2026-10-03.md)：正式追迹/保存、材料约束与 LPTD 剩余边界。

- [GRIN 材料编辑与系数变量](GRIN_MATERIAL_EDITOR_2026-10-04.md)：桌面入口、严格保存、多配置和实际优化验证。


指定频率 MTF 制造公差：FFT/几何 MTF、逐视场极限/增量反求和联合良率、有界间隔/单表面偏心/倾斜补偿、startol v3（兼容 v1/v2）已实现；未认证原生 Zemax 公差数值。见 [使用、边界与验证](MTF_TOLERANCING_2026-10-04.md)。
