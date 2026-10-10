# 项目文档索引

当前总结：[出瞳形状与物理 Foucault 实现](PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.md)、[问题清单与完成判据](OPEN_ISSUES_2026-10-08.md)。[FFT 能量网格](FFT_ENERGY_GRID_INTEGRITY_REPAIR_2026-10-10.md)、[准备 FFT 输入来源](PREPARED_PUPIL_INTEGRITY_REPAIR_2026-10-10.md)、[FFT 瞳面相位](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)、[Huygens 传播](HUYGENS_IMPLEMENTATION_REPAIR_2026-10-09.md)、[轴上参考](OFFICIAL_DOCUMENTATION_REPAIR_2026-10-09.md)及此前修复保留各自阶段范围。10 月 10 日修改未提交或推送；[10 月 9 日完成记录](PROJECT_SYNC_FFT_PUPIL_2026-10-09.md)不能代表最新工作区。公开说明、内部回归、原生精度与完整性分别阅读。

原计划已推进至确认问题修复与原档位剩余诊断：此前捕获电脑取得 N02 同源二维/复场/瞳孔及范围控制，修复 FFT 相位与边界；N01 Auto/Planar 二维逐值一致。此前零像素网格错误已修复，五组完整光线平面参考仍未达到精度要求、未替换产品。本轮按用户指定优先接通有焦出瞳显示与物理 Foucault；用户确认 Zemax 在另一台电脑、本机不支持，后续原生补采须在那里完成。N02 私有积分与理想定义、N01 完整方向/参考/权重，以及新功能原生认证、采样收敛与发布范围仍待推进。残差条件继续遵循[原生定位计划](FFT_SAMPLING_REFERENCE_REPAIR_2026-10-09.md#后续执行计划)；分层见[阶段执行报告](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)，最新实现以[本轮报告](PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.md)为准。

其他计算路径修复后报告：[修复、前后数值与剩余问题](CALCULATION_PATH_REPAIR_2026-10-08.md)，含完整 Release 验收、492 条原生光扇记录和可阅读 PDF。此前[计算路径问题定位](CALCULATION_PATH_DIAGNOSIS_2026-10-08.md)保留为修复前诊断证据；其未修复描述不是当前状态。

修复前诊断记录：波前图出瞳形状开关误控瞄准已复现；负 Y 场表存在最近行跳变，RA 波前端点 / 单元中心敏感性已量化。该诊断轮产品计算未修改，原生新捕获因本机缺少 ZOS-API 未执行；原 132 项分类未重写，见[问题定位与证据](ZEMAX_PUPIL_DIAGNOSIS_2026-10-08.md)。波前图后续修复与最新验证见下方。

2026-10-10 已接通有焦波前图的出瞳 X/Y 工作 F 数显示投影，并将 Foucault 改为复场焦面刀口与逆 FFT 理想光瞳再成像，支持正式 Jones 可执行范围及线性/对数显示。默认 Debug/Release 完整构建零警告、零错误；正式完整 Release **4608/4608**、Debug 相关 **236/236**、Release 专项 **60/60**、实际控件渲染 **1/1**，均零跳过。此前 4577 项身份与次数全部保留，新增 31 项；既有 Foucault 契约按物理模型更新，集合重叠不相加。完整比较工具 **179 通过 / 1 失败 / 共 180 项**，原 Huygens 失败身份、消息与输出不变，N01/N02 仍为 Close/Difference。历史冻结清单仍为 **2660/2668**，8 项差异保留为 E04；165 份原生捕获及旧账本字节未改，原参考、设置和容差未改。本机不支持 Zemax，未新增原生认证；完整 Debug、实验室、安装包、人工桌面和六镜头矩阵未重跑。任意三维出瞳、无焦焦面投影及未支持的 Jones 介质等边界仍开放；本轮未提交、未推送。详见[当前实现与验收](PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.md)。

历史验收记录（2026-10-08）：正式默认 Debug/Release 构建零警告、零错误，完整主测试两配置各 **4501/4501** 通过；比较工具完整 Release **178 通过 / 2 失败 / 共 180 项**，均零跳过。保留原 4494 个正式测试身份并新增 7 项；负向渐晕、Headless 会话竞态与辅助历史有限物距契约已处理，四份派生 Tessar 原生发射共 **512/512** 通过。六原文件 132 项完整重算为 **100 Pass / 9 Close / 14 Difference / 8 Incomparable / 1 Error**，此前 95 个 Pass 无回退，冻结证据、设置和容差保持不变。Huygens 截面与 DEE 残差仍开放；实验室结果和发布检查分别记录，整体发布门禁未关闭。该阶段修复的提交与远端状态见同步记录，详见[该阶段修复与完整验收](CONFIRMED_ISSUE_REPAIR_2026-10-08.md)；下方旧计数保留历史范围。

历史波前图验收记录：2026-10-08 波前图修复验收：默认 Debug/Release 构建零警告、零错误；正式完整 Release **4430 通过 / 4 失败 / 共 4434 项**，比较工具完整 Release **178 通过 / 2 失败 / 共 180 项**，均零跳过。新增 **25/25** 与原相关 **743/743** 在正式全量中均通过，本轮 Debug 定向 **49/49**；集合不相加。正式失败包括既有有限物距历史参考差异及 3 项界面会话关闭异常，其中 2 个界面失败身份本轮新增观察；两次隔离各 **13/13** 不替代全量失败。波前图瞄准修复通过专项验收，整体发布不通过。六组原生精确共节点与参考哈希复核一致，主 Zemax 基准完整性通过；新原生对照因缺少 ZOS-API 未执行。原 132 项矩阵、完整 Debug、实验室及发布包未验收，其旧计数保留历史范围；其他瞄准旁路、出瞳显示形状、带符号渐晕和 RA 波前节点仍待验证，见[完整结果与整改优先级](WAVEFRONT_ACCEPTANCE_2026-10-08.md)。

修复阶段记录：2026-10-08 波前图系统瞄准修复：均匀与六角采样均遵循系统瞄准，不再由出瞳形状显示开关决定物理光线；实际图与六组冻结原生 OPD 光扇的精确共节点复验通过，参考与容差未修改。默认 Debug/Release 构建零警告、零错误；正式相关回归两配置各 **743/743**（包含新增 **25/25**），比较工具相关各 **45/45**，零失败、零跳过，集合不相加。完整全量与原 132 项外部矩阵未重跑，下方计数均保留修复前历史范围，发布门禁未关闭。FFT 与零离焦瞄准、实际出瞳形状投影、带符号 Y 渐晕及 RA 波前节点仍未完成，见[修复与验证](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)。

历史阶段记录：2026-10-08 RMS 采样与偏振链路修正：五个 RMS 入口按 GQ 环数 / RA 每边点数验证，RA64/128/256 不再静默降到 32；桌面支持方法相关标签、范围和显式 GQ 角向点数，波前 RMS 开启偏振时使用正式系统透过强度。默认 Debug/Release 构建零警告、零错误，新增两配置各 **25/25**、相关回归各 **222/222** 通过；集合重叠，不相加。正式完整 Release **4407 通过 / 2 失败 / 共 4409 项**（1 项既有历史参考、1 项界面会话关闭异常），比较工具完整 Release **178 通过 / 2 个既有失败 / 共 180 项**，均零跳过。图纸 6 项与新增设置 5 项隔离复跑 **11/11** 通过；最后的测试隔离调整后未重跑完整全量，发布门禁仍未通过，详见[本轮验证记录](RMS_SAMPLING_POLARIZATION_REPAIR_2026-10-08.md)。吸收接口/GRIN 等未支持的偏振明确报错；RA 波前节点、带符号 Y 渐晕插值及更高密度原生收敛仍待认证。未重跑六文件 132 项外部矩阵或实验室全量，旧外部分类和此前计数保留历史范围。

历史记录：2026-10-07 RA256 与单光线深入复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **4383 通过 / 1 失败 / 共 4384 项**，比较工具完整 Release **178 通过 / 2 个既有失败 / 共 180 项**，均零跳过；新增正式 **20/20**、工具 **16/16** 通过。正式导入/单光线/RMS/GRIN 定向两配置各 **153/153**，工具定向 Debug **54/54**。六份官方镜头原设置 132 项的旧快照与重新导入两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，已有 85 Pass 无回退；独立五文件 RMS 控制为旧 17 项加新 RA256 6 项，**23/23 Pass**，分开计数。六个完整边缘光瞳共 308808 条输入、2521932 个逐面结果，修复自动 STOP 后接纳/首次截断差异均归零；更高密度收敛、Relay 瞄准、衍射、其余 21 份镜头及发布门禁未完成，见[修复与证据](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。完整 Debug 和实验室本轮未重跑，2026-10-06 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜两配置各 **47/47** 保留历史范围。此前阶段计数不相加。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

历史：[RA256、多文件光阑与单光线路径复验](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)，含双路径 132 项矩阵、23 项独立控制和六组完整光瞳诊断。此前报告保留各自历史范围。

- [2026-10-07 远端同步范围](PROJECT_SYNC_2026-10-07.md)：累计修复、冻结夹具、最终验证记录及大型原始数据的保存边界。

历史功能批次：[多配置单元格拾取（第三十八批）](MCE_PICKUP_SOLVES_2026-10-03.md)，含实现范围、格式 v7、验证及真实控件渲染。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

- [多配置行表与 MCO 操作数](MCE_ROW_OPERANDS_2026-10-03.md)：绑定行、配置取值、编辑引用及 STAROPT v5。

- [多配置操作数 CONF / ZTHI](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)：隔离配置求值、优化候选链接及保存边界。

索引更新日期：2026-10-10。索引汇集项目自有 Markdown（包括本索引与状态页）；第三方原文、许可证和 `validation/history` 冻结数据不做重写。不同阶段报告保留各自范围，日期较近不代表已经完成全量验收或远端同步。

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
