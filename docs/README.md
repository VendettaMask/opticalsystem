# 项目文档索引

索引更新：2026-11-04，本轮补齐材料库与CDGM官网目录入口。先按现行指南操作；历史通过数、旧独立模块命令及内部对齐目标不作为当前验收。各阶段实际结果见[当前状态](CURRENT_STATUS.md)，旧记录见[历史索引](history/README.md)。

## 从这里开始

| 任务 | 入口 |
| --- | --- |
| 使用产品 | [用户指南](USER_GUIDE.md)、[桌面工作流](GUI_QUICKSTART_REFACTOR.md)、[能力矩阵](PARITY_MATRIX.md) |
| 查看材料库与CDGM数据来源 | [材料库操作](USER_GUIDE.md#材料库目录与参数)、[内置数据说明](../src/OpticalSystemDesign.Core/Materials/Data/README.md)、[CDGM官方原件](../third_party/material-catalogs/cdgm/README.md) |
| 构建、测试与安装 | [构建发布](BUILD_AND_RELEASE.md)、[Windows 安装/恢复](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)、[云发布](GITHUB_CLOUD_RELEASE.md)、[Mac OTA](MAC_OTA.md) |
| 核对精度与未关闭问题 | [数值验收规则](NUMERICAL_PARITY.md)、[问题清单](OPEN_ISSUES_2026-10-08.md)、[95 示例分类交接](EXAMPLE_ACCURACY_HANDOFF_2026-11-01.md) |
| 维护实现 | [架构](ARCHITECTURE.md)、[代码地图](CODE_READING_MAP.md)、[UI 规范](UI_DESIGN_SPEC.md)、[协作约束](../AGENTS.md) |
| 设计与改进界面 | [全项目设计语言与工作方法](UI_DESIGN_SPEC.md#全项目设计语言与工作方法)、[走查与确认记录](UI_DESIGN_REVIEW.md)、[主题包规范](THEME_PACKAGES.md)、[图标语义](ICON_SEMANTIC_DESIGN.md) |
| 维护文档 | [文档生命周期与检查](DOCUMENTATION_MAINTENANCE.md)、[历史报告索引](history/README.md) |

## 当前指南与功能契约

### 计算、工程与交付

- [计算指南](COMPUTATION_GUIDE.md)、[数学附录](COMPUTATION_FORMULAS.md)、[目录覆盖](COMPUTATION_COVERAGE.md)
- [格式和插件](FILE_FORMATS_AND_PLUGINS.md)、[STAROPT 版本](STAROPT_FILE_FORMAT.md)、[操作数支持](ZEMAX_OPERAND_SUPPORT.md)
- [CDGM官网2026年9月目录：合并、字段与39项验证](../validation/rechecks/2026-11-04/material-library-details/README.md)，仅对应这一批材料库更新，不代替全仓或全部镜头精度认证。
- [优化算法](OPTIMIZATION_ALGORITHMS.md)、[公差](TOLERANCING.md)、[制造图纸](MANUFACTURING_DRAWINGS.md)
- [设计报告](MARKDOWN_REPORT_EXPORT.md)、[内置模块交付](BUILT_IN_MODULES_2026-10-13.md)
- [分析及二维/三维窗口适配与重复限制审计](DIAGRAM_VIEWPORT_FIT_2026-11-02.md)

### 初始结构、镀膜与相机

- [初始结构当前范围](INITIAL_STRUCTURE_LAB_PLAN.md)、[内置模块操作](../labs/InitialStructure/README.md)、[GPU 支持域](AUTOLENS_GPU.md)
- [镀膜计算契约](COATING_DESIGN_LAB.md)、[膜系编辑/光谱/堆栈/停靠操作](../labs/CoatingDesign/README.md)
- [镀膜OT/QW/FW、历史与双面设计](COATING_EDITOR_DUPLEX_2026-11-02.md)、[该批实施计划](COATING_EDITOR_DUPLEX_PLAN_2026-11-02.md)
- [镀膜独立λ₀、公式、剪贴板、异常恢复与双面解析优化](COATING_WORKFLOW_ANALYTIC_2026-11-02.md)、[实施计划](COATING_WORKFLOW_ANALYTIC_PLAN_2026-11-02.md)
- [膜系编辑器：按TFStudio重排](COATING_EDITOR_LAYOUT_2026-11-03.md)
- [相机与 NSC→RAW/ISP](CAMERA_IMAGING_LAB.md)、[单色/多波长局部斜边 SFR](MONOCHROME_SLANTED_EDGE_SFR.md)
- [物方棋盘：预算内全幅逐源谱、线性交换与多 ROI 双方向 SFR](SCENE_SLANTED_EDGE_SFR.md)，原例 5000×3000/ROM 与独立对照尚未验收
- [非序列工作流](NON_SEQUENTIAL_TRACING.md)、[会话/数据库与杂散光](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)、[光源](NONSEQUENTIAL_SOURCES.md)
- [光学装调](OPTICAL_ASSEMBLY_LAB.md)、[后续非序列路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md)

### 正在推进的计划

- [初始结构搜索阶段计划](INITIAL_STRUCTURE_SEARCH_IMPLEMENTATION_PLAN_2026-10-30.md)：按最新进度区分已交付、暂缓及仍未实现项。
- [Ansys 场景斜边与 ROM/黑盒计划](ANSYS_SCENE_SFR_IMPLEMENTATION_PLAN_2026-11-02.md)：现有局部 PSF/SFR 不代表该物方场景工作流已经完成。

已完成的镀膜停靠计划、M0–M6 首版拓扑计划及其他阶段验收放在历史索引，现行操作分别由模块指南维护。AutoLens 上游对齐只作来源与兼容证据，不再作为产品开发目标。

<details>
<summary>更多现行专门指南</summary>

- [系统架构](ARCHITECTURE.md)
- [AutoLens GPU 计算](AUTOLENS_GPU.md)
- [AutoLens 官方目标的原生预设与验证](AUTOLENS_OFFICIAL_EXAMPLES.md)
- [应用品牌资源](BRANDING.md)
- [构建与发布](BUILD_AND_RELEASE.md)
- [初始结构与镀膜设计：统一产品交付（2026-10-13）](BUILT_IN_MODULES_2026-10-13.md)
- [相机成像仿真实验室](CAMERA_IMAGING_LAB.md)
- [镀膜设计：计算与实现契约](COATING_DESIGN_LAB.md)
- [全仓库代码阅读地图](CODE_READING_MAP.md)
- [分析入口与计算章节对应](COMPUTATION_COVERAGE.md)
- [计算公式附录](COMPUTATION_FORMULAS.md)
- [软件如何计算](COMPUTATION_GUIDE.md)
- [当前实现与验证状态](CURRENT_STATUS.md)
- [文件格式与插件](FILE_FORMATS_AND_PLUGINS.md)
- [GitHub 云端构建与发布](GITHUB_CLOUD_RELEASE.md)
- [GUI 工作流与重构对照](GUI_QUICKSTART_REFACTOR.md)
- [初始结构内置模块：范围与后续计划](INITIAL_STRUCTURE_LAB_PLAN.md)
- [老国标光学零件图逐项核对](LEGACY_GB1991_OPTICAL_PARTS_REVIEW.md)
- [打包镜头库](LENS_LIBRARY.md)
- [界面语言](LOCALIZATION.md)
- [本地矢量图标库](LOCAL_ICONS.md)
- [按功能名称设计图标：语义与五主题造型规范](ICON_SEMANTIC_DESIGN.md)
- [图标风格规范 Word 版](assets/icons/图标风格规范.docx)
- [Mac OTA](MAC_OTA.md)
- [可制造性审查与光学制图](MANUFACTURING_DRAWINGS.md)
- [光学设计报告导出](MARKDOWN_REPORT_EXPORT.md)
- [斜边图像到 SFR：单色与镜头多波长](MONOCHROME_SLANTED_EDGE_SFR.md)
- [非序列第二阶段：杂散光基础链路](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)
- [Lambertian BSDF、采样统计与 NSC→RAW/ISP](LAMBERTIAN_BSDF_2026-10-31.md)
- [实测 BSDF 文件、插值、采样与 BTDF](MEASURED_BSDF_BTDF_2026-10-31.md)
- [商业 BSDF 文本、固定材料轴与相机链路](COMMERCIAL_BSDF_AXES_2026-10-31.md)
- [Speos 二进制、Zemax 镜面中心 BSDF 与相机链路](BINARY_ZEMAX_BSDF_2026-10-31.md)
- [Speos v11、原生镜面配准/White Specular 与校准 XYZ](SPEOS_V11_XYZ_2026-10-31.md)
- [非序列光源](NONSEQUENTIAL_SOURCES.md)
- [非序列模式 Zemax 工作流对齐路线图](NONSEQUENTIAL_ZEMAX_PARITY_ROADMAP.md)
- [非序列工作模式与光线追迹](NON_SEQUENTIAL_TRACING.md)
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
- [产品字体](TYPOGRAPHY.md)
- [界面语言、本地词库与漏译检查](LOCALIZATION.md)
- [UI 设计规范](UI_DESIGN_SPEC.md)
- [用户指南](USER_GUIDE.md)
- [Windows 未签名交付与升级保护（2026-10-12）](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)
- [Zemax 分析设置与实现方式参考](ZEMAX_ANALYSIS_REFERENCE.md)
- [Zemax 基准配置边界](ZEMAX_BASELINE_CONFIGURATION_BOUNDARY.md)
- [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)

</details>

## 模块、工具和样例入口

- [初始结构](../labs/InitialStructure/README.md)、[镀膜设计](../labs/CoatingDesign/README.md)、[相机样例](../samples/camera-imaging/README.md)
- [顺序镜头样例](../samples/lenses/README.md)、[非序列样例](../samples/non-sequential/README.md)、[材料数据](../src/OpticalSystemDesign.Core/Materials/Data/README.md)
- [Zemax/Workbench 比较工具](../tools/OpticalSystemDesign.ZemaxComparison/README.md)、[原生采集工具](../tools/zemax_parity/README.md)、[参考与准备输入核验](../tools/validation/README.md)

验证工具不进入产品运行依赖；普通构建不启动 Zemax。

## 历史报告与阶段提案

[完整历史索引](history/README.md)保留原分组和补齐的阶段报告。旧材料是否保留取决于实际消费者、独有证据与替代情况；不能因标为 frozen 自动永久保留，也不能仅因日期较早删除仍在使用的参考。
