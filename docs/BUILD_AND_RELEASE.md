# 构建与发布

## FFT 瞳面相位计划执行复验（2026-10-09）

默认 `dotnet build OptilandWorkbench.slnx -c Debug/Release --no-restore` 均零警告、零错误。正式完整 `OptilandWorkbench.Tests` Release 4546/4546，最终 Debug 的 FFT/能量/Huygens 相关定向 77/77；比较工具完整 Release 179/1/180，均零跳过。正式原 4539 项身份和次数全部保留，恰好新增 7 项，77 项包含于完整 Release，集合不相加。

最后使用相同 Release Core 对四份原生输入及原 N02 分层重算。所有默认 Release 输出与诊断加载的 Core 哈希一致，Debug 输出相互一致。原冻结参考、设置与门槛未改；DEE 通过旧误差预算但实际/理想仍为 Difference，唯一工具失败是原 Huygens 残差。完整 Debug、实验室、安装包和人工桌面本轮未跑。

具体命令、TRX、构建日志、逐身份保留、源/二进制身份与 165 份新原始捕获哈希见[修复报告](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)和[机器账目](validation/FFT_PUPIL_PHASE_REPAIR_2026-10-09.json)。以下记录保留各阶段范围；本轮修复与选定证据已推送，见[同步完成记录](PROJECT_SYNC_FFT_PUPIL_2026-10-09.md)。

2026-10-09 按原生残差计划推进：新增 165 份原生原始文件，修复正式 FFT 瞳面棋盘相位与 Nyquist 边界舍入。默认 Debug/Release 构建零警告、零错误；正式完整 Release **4546/4546**、最终 Debug 定向 **77/77**，均零跳过。原 4539 项身份与次数全部保留，新增 7 项；完整 Release 包含同一 77 项专项。完整比较工具 **179 通过 / 1 失败 / 共 180 项**，零跳过；DEE 满足原误差预算但实际/理想仍为 Difference，Huygens 旧失败保留。新原生控制确认 N01 Auto 与 Planar 的 1024 像素逐值相同，尚未修改 Huygens 模型。冻结参考、原设置和容差不变；完整 Debug、实验室、安装包、人工桌面及六镜头矩阵本轮未重跑。本轮修复已提交并推送，完成状态见[本轮同步记录](PROJECT_SYNC_FFT_PUPIL_2026-10-09.md)；既有记录保留历史范围。当前实现、证据和缺口见[FFT 瞳面相位修复报告](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)。

历史验收记录（2026-10-08）：正式默认 Debug/Release 构建零警告、零错误，完整主测试两配置各 **4501/4501** 通过；比较工具完整 Release **178 通过 / 2 失败 / 共 180 项**，均零跳过。保留原 4494 个正式测试身份并新增 7 项；负向渐晕、Headless 会话竞态与辅助历史有限物距契约已处理，四份派生 Tessar 原生发射共 **512/512** 通过。六原文件 132 项完整重算为 **100 Pass / 9 Close / 14 Difference / 8 Incomparable / 1 Error**，此前 95 个 Pass 无回退，冻结证据、设置和容差保持不变。Huygens 截面与 DEE 残差仍开放；实验室结果和发布检查分别记录，整体发布门禁未关闭。该阶段修复的提交与远端状态见同步记录，详见[该阶段修复与完整验收](CONFIRMED_ISSUE_REPAIR_2026-10-08.md)；下方旧计数保留历史范围。

历史波前图验收记录：2026-10-08 波前图修复验收：默认 Debug/Release 构建零警告、零错误；正式完整 Release **4430 通过 / 4 失败 / 共 4434 项**，比较工具完整 Release **178 通过 / 2 失败 / 共 180 项**，均零跳过。新增 **25/25** 与原相关 **743/743** 在正式全量中均通过，本轮 Debug 定向 **49/49**；集合不相加。正式失败包括既有有限物距历史参考差异及 3 项界面会话关闭异常，其中 2 个界面失败身份本轮新增观察；两次隔离各 **13/13** 不替代全量失败。波前图瞄准修复通过专项验收，整体发布不通过。六组原生精确共节点与参考哈希复核一致，主 Zemax 基准完整性通过；新原生对照因缺少 ZOS-API 未执行。原 132 项矩阵、完整 Debug、实验室及发布包未验收，其旧计数保留历史范围；其他瞄准旁路、出瞳显示形状、带符号渐晕和 RA 波前节点仍待验证，见[完整结果与整改优先级](WAVEFRONT_ACCEPTANCE_2026-10-08.md)。

修复阶段记录：2026-10-08 波前图系统瞄准修复：均匀与六角采样均遵循系统瞄准，不再由出瞳形状显示开关决定物理光线；实际图与六组冻结原生 OPD 光扇的精确共节点复验通过，参考与容差未修改。默认 Debug/Release 构建零警告、零错误；正式相关回归两配置各 **743/743**（包含新增 **25/25**），比较工具相关各 **45/45**，零失败、零跳过，集合不相加。完整全量与原 132 项外部矩阵未重跑，下方计数均保留修复前历史范围，发布门禁未关闭。FFT 与零离焦瞄准、实际出瞳形状投影、带符号 Y 渐晕及 RA 波前节点仍未完成，见[修复与验证](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)。

历史阶段记录：2026-10-08 RMS 采样与偏振链路修正：五个 RMS 入口按 GQ 环数 / RA 每边点数验证，RA64/128/256 不再静默降到 32；桌面支持方法相关标签、范围和显式 GQ 角向点数，波前 RMS 开启偏振时使用正式系统透过强度。默认 Debug/Release 构建零警告、零错误，新增两配置各 **25/25**、相关回归各 **222/222** 通过；集合重叠，不相加。正式完整 Release **4407 通过 / 2 失败 / 共 4409 项**（1 项既有历史参考、1 项界面会话关闭异常），比较工具完整 Release **178 通过 / 2 个既有失败 / 共 180 项**，均零跳过。图纸 6 项与新增设置 5 项隔离复跑 **11/11** 通过；最后的测试隔离调整后未重跑完整全量，发布门禁仍未通过，详见[本轮验证记录](RMS_SAMPLING_POLARIZATION_REPAIR_2026-10-08.md)。吸收接口/GRIN 等未支持的偏振明确报错；RA 波前节点、带符号 Y 渐晕插值及更高密度原生收敛仍待认证。未重跑六文件 132 项外部矩阵或实验室全量，旧外部分类和此前计数保留历史范围。

历史记录：2026-10-07 RA256 与单光线深入复验：正式默认 Debug/Release 构建零警告、零错误；正式完整 Release **4383 通过 / 1 失败 / 共 4384 项**，比较工具完整 Release **178 通过 / 2 个既有失败 / 共 180 项**，均零跳过；新增正式 **20/20**、工具 **16/16** 通过。正式导入/单光线/RMS/GRIN 定向两配置各 **153/153**，工具定向 Debug **54/54**。六份官方镜头原设置 132 项的旧快照与重新导入两条路径均为 **95 Pass / 12 Close / 16 Difference / 8 Incomparable / 1 Error**，已有 85 Pass 无回退；独立五文件 RMS 控制为旧 17 项加新 RA256 6 项，**23/23 Pass**，分开计数。六个完整边缘光瞳共 308808 条输入、2521932 个逐面结果，修复自动 STOP 后接纳/首次截断差异均归零；更高密度收敛、Relay 瞄准、衍射、其余 21 份镜头及发布门禁未完成，见[修复与证据](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。完整 Debug 和实验室本轮未重跑，2026-10-06 Debug **4336/1/4337**、初始结构 Release **258/2/260**、镀膜两配置各 **47/47** 保留历史范围。此前阶段计数不相加。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

历史行表阶段：2026-10-03 多配置行表：新增 MCOV/MCOG/MCOL 三项受限执行，按独立行号和配置号读取 THIC/CRVT/CONN/SDIA 绑定参数；行重排引用、输入验证、优化候选和 STAROPT v5 保存撤销已接通。**340/383 项受限执行、43 项兼容保留**（36 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3183/3183**（前批 3135 + 新增 48），零失败、零跳过、零编译警告/错误。更多 MCE 类型、单元格变量/配置拾取解以及原生行序、列和数值映射仍未完成；原生 MCO 导入只读。见[行表实现与验证](MCE_ROW_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 多重配置：新增 CONF、ZTHI 两项受限执行，配置上下文贯通有序评价、应用显示及优化候选；基准几何链接和拾取在候选副本内同步，活动配置保持不变。**337/383 项受限执行、46 项兼容保留**（39 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3135/3135**（前批 3089 + 新增 46），零失败、零跳过、零编译警告/错误。PRIM/CVIG/IMSF 后接 CONF 的组合、其他配置变量联合搜索及原生数值/列映射仍未完成，原生导入只读。见[多配置实现与验证](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 膜层约束：新增 CMGT/CMLT/CMVA、CIGT/CILT/CIVA、CEGT/CELT/CEVA 九项受限执行；共享薄膜求解读取每层倍率与 n/k 偏移，物理膜层编辑、变量优化和 STAROPT 保存撤销已接通。**335/383 项受限执行、48 项兼容保留**（41 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认输出 Debug/Release 合并回归各 **3089/3089**（前批 3034 + 新增 54 + 既有样式检查 1），零失败、零跳过、零编译警告/错误。原生膜层字段、聚合边界数值、倍率开关/拾取仍有缺口，ZMX 行保持只读。见[膜层约束记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

2026-10-02 第二十九批：TSAG 指定方向矢高及延伸区数据贯通；体积、毛坯、CAD 和自动口径同步处理光学延伸边界。该阶段 **302/383 项受限执行、81 项兼容保留**（74 项已知功能、2 项定义待核实、5 项 Unused）。默认 Debug/Release 合并回归各 **2560/2560**（前批 2497 + 本批新增 39 + 扩展既有 24），无失败/跳过或编译警告/错误。原生 TSAG 列、复合倾角与非零延伸区文件映射仍待核实，详见[本批范围与证据](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。以下记录保留各自阶段范围。

2026-10-02 操作数帮助当前版本复核：第二十八批之后默认 Debug/Release 输出构建成功，帮助交互、官方名称/状态标识及字号子集各 **19/19** 通过。过滤器为 `FullyQualifiedName~OperandHelpTests|FullyQualifiedName~OperandAuthenticityTests|FullyQualifiedName~LayeringArchitectureTests.AppUiFontSizesUseTypographyTokens`；Debug 通过 `OPTILAND_OPERAND_HELP_CAPTURE_DIR` 保存 Light/Dark 概览、功能组、搜索与窄窗口共 8 张实际控件渲染并完成检查。本次未修改帮助源码或新增测试，全量与光学基线范围仍按下方阶段记录区分，见[帮助复核证据](../artifacts/validation/operand-help-current-20261002/verification.json)。

2026-10-02 第二十八批：新增共享最小体积球面拟合及 BFSD 本地评价、编辑保存、优化路径。该阶段 **301/383 项受限执行、82 项兼容保留**（75 项已知功能、2 项定义待核实、5 项 Unused）。默认 Debug/Release 输出构建成功，合并回归各 **2497/2497**（含新增 26 项），零失败/跳过。固定 Zemax 球面矢高表只验证该文件的球面情况，原生 MFE 与非球面拟合仍有验证缺口；见[本批实现与证据](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)。以下记录保留各自阶段范围。

2026-10-02 第二十七批：RELI/EFNO 已接通本地编辑、保存、评价函数和优化，显式复用共享像方网格及 Jones 功率链。该阶段 **300/383 项受限执行、83 项兼容保留**（76 项已知功能、2 项定义待核实、5 项 Unused）；仍有原生数值和模式缺口，两项 ZMX 导入保持只读。默认 Debug/Release 输出构建成功，合并回归各 **2471/2471**（含新增 31 项），见[本批实现与证据](ILLUMINATION_OPERANDS_2026-10-02.md)。以下记录保留各自阶段范围。

2026-09-29 光学装调无穷远模式：默认 Debug/Release Core、Application、App 与主测试项目构建各零警告、零错误；装调各 **23/23**，含相邻回归各 **80 通过 / 1 个既有主题失败 / 共 81 项**；独立 Skia 窗口子集 **3/3**。见 [本轮命令与范围](#光学装调无穷远模式复验2026-09-29)。以下初版及其他阶段保留各自范围。

2026-10-01 操作数帮助已改为三级导航，帮助阶段默认桌面 Debug/Release 输出已更新；当时帮助、名称标识及字号检查各 **19/19** 通过，含明暗主题和窄窗口渲染。见[层级与验证](OPERAND_HELP_HIERARCHY_2026-10-01.md)。该 19 项记录保留原阶段范围，后续合并回归见下方第二十六批。

2026-10-02 第二十六批接通 DIST/DISA 本地计算，并修正网格畸变对非径向偏差的统计。该阶段 **298/383 项有受限可执行路径，85 项兼容保留**；其中 78 项已知功能待接通、2 项定义待核实、5 项官方未用名称。默认 Debug/Release 合并回归各 **2308/2308**（此前 2264 + 新增 40 + 相邻既有 4）。固定 123456 / 440 nm 赛德尔报告的 22 个 W311 值最大误差约 4.92e-7 波长，总 TDIS 误差约 3.85e-7 mm；这不认证原生 MFE 列、DIST 全系统百分比/离焦约定或 DISA 方向归一化，两项原生导入保持只读。DISC 仍待实现；既有 FCGT 保持 Difference。见[支持边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)、[验证记录](BUILD_AND_RELEASE.md#顺序操作数第二十六批复验2026-10-02)。另 4 项本程序扩展不计入 383；全部完成目标仍在进行。以下历史记录保留各自范围。

2026-09-28 光学装调有限共轭初版阶段：默认 Debug/Release 的 Core、Application、App 与主测试项目均零警告、零错误；新增功能测试各 **14/14**，含它们的相邻回归各 **71 通过 / 1 个既有主题架构失败 / 共 72 项**；独立 Skia 窗口子集 **2/2**。见 [阶段命令与范围](#光学装调实验室复验2026-09-28)。不是全解决方案或正式全量通过声明。

2026-09-28 制图材料读取与公差显示：默认 Debug/Release App、Application 与测试输出已更新，相关回归各 **55/55** 通过；三套模板的 12 份 PDF 数值提取核对及实际渲染检查完成，见 [本轮验证](#制图材料数值与公差复验2026-09-28)。以下各阶段保留原范围。

2026-09-28 标准面与平面统一：默认 Debug/Release App、Application 与测试输出已更新，表面属性相关回归各 **27/27**、相邻组件回归各 **11/11** 通过；实际控件渲染子集 **1/1**，见 [本轮验证](#标准面与平面统一复验2026-09-28)。以下阶段结果保留各自范围。

2026-09-28 色散系数双列调整：默认 Debug/Release App 和测试输出已更新，已有响应式/可访问性回归各 **13/13** 通过；四种窗口宽度的真实控件渲染见 [本轮验证](#色散系数双列复验2026-09-28)。

此前材料库 Dock 合并阶段：正式解决方案默认 Debug/Release 构建各零警告零错误，工作区、会话、材料分析及响应式布局回归各 **59/59** 通过，含 5 个新增迁移用例。命令与产物见 [阶段验证](#材料库-dock-合并复验2026-09-28)；下面历史结果保留各自范围。

历史记录（2026-09-27，当前镀膜增补见本文新记录）：正式解决方案与镀膜实验室默认 Release 构建各零警告、零错误；既有 UI 回归 70/70、镀膜实验室回归 29/29 通过。此次未重跑正式全量、初始结构实验室全量或外部数值比较；历史失败范围保持原说明。详见 [当前状态](CURRENT_STATUS.md)。


此前主动作/侧栏阶段桌面 UI 构建与回归：默认 Debug/Release 解决方案构建各 **0 警告、0 错误**，相关回归各 **70/70**，独立 Skia 截图回归 **15/15**；不是全量测试通过声明。统一验证账目见 [当前状态](CURRENT_STATUS.md)，全部指南见 [文档索引](README.md)。


2026-09-27 正式优化入口修复：共享 Core 曲率变量、逐光线默认目标与显式无效评价已接入正式优化；DLS 版本为 `/2`，动量梯度下降为 `/2`。默认 Debug 优化子集 **112/112** 通过；含公差的扩展检查 **144 通过 / 2 个既有显示精度失败 / 共 146 项**，零跳过。最终正式解决方案默认 Release 构建 **0 警告、0 错误**，本次源文件格式及差异检查通过。正式全量、实验室全量及宽角搜索未重跑。该结果不替代下面注明阶段的全量基线。详细命令见 [优化修复记录](OPTIMIZATION_ENTRY_FIX_2026-09-27.md)。

2026-09-27 初始结构实验室 v19 阶段验证：修复扩大视场后失追迹仍继续推进的问题，加入有预算上限的过渡恢复；最终验收门槛不变。实验室完整 Release **260/260** 通过，默认实验室 Debug/Release 构建零警告、零错误。40/40 冻结处方同设置重算与导出回读一致，包含明确失败案例；一个固定起点恢复为完整可追迹，但宽角 10,000 次评价的最佳 RMS 仍为 377.79 μm，未达到 50 μm。正式 Core 源码未变，正式全量和外部比较本轮未重跑。见 [阶段恢复、文献复核与实测](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)。下文保留各阶段历史验证范围。

2026-09-27 商业代码审计记录（初始结构实验室为 v18 阶段）：默认正式及实验室 Release 构建均为 0 警告、0 错误；正式全量 **1346 通过 / 5 失败 / 共 1351 项**，实验室 **258/258** 通过，Zemax 比较工具 **103 通过 / 1 失败 / 共 104 项**。当前不满足发布门禁；此前全通过记录保留各自历史范围。本轮只审计并同步文档，未修复产品代码，未新建 Zemax 捕获或执行完整外部比较矩阵。问题、性能观测和复现证据见 [商业审计报告](COMMERCIAL_CODE_AUDIT_2026-09-27.md)。

2026-09-27 v18 阶段验证（本轮商业审计前）：v18 已把实体光阑标定、瞄准及保留净口径的自动镜片变径接入实验室搜索，新建实验默认启用，旧实验保留原模式。共享 Core 定向 **39/39**、实验室完整 Release **258/258** 通过；默认主程序及实验室 Debug/Release 构建零警告、零错误。完成两规格、新旧模式各 10,000 次评估的四次对照及全部 40 个保留候选的同设置重算/导出验证；不等于多种子发布验收。正式全量 **1311/1311**、此前光学定向 **315/315**、比较工具 **104/104** 保留历史范围；没有新建 Zemax 捕获或完整外部数值对照。见 [实体光阑搜索接入与实测](INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md)。下文更早验证保留历史范围。

2026-09-08 独立实验室 S1 历史验证：锁定还原、默认 Debug/Release 构建、完整格式及差异检查通过，完整 Release 测试 **150/150** 通过。Debug 定向测试 **35/35** 通过，Debug 全量曾主动中断，未完成；正式完整测试该阶段未重跑，当时的 5 项既有失败尚未解决。配置、命令与证据见 [S1 实施记录](INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md)。该通用求解器当时尚未接入光学搜索或正式产品。

## 文档同步规则

每项已完成代码修改必须在同一任务中更新相关文档。文档必须区分已实现、计划和仅兼容行为。测试数量或验证日期变化时，所有引用该基线的文档必须同步；代码、测试、文档和最终报告必须一致。

## 本地构建

### 顺序操作数第三十四批复验（2026-10-03）

九项膜层参数约束及物理膜层编辑/保存/优化链路；本批新增测试 54 项，并纳入既有共享卡片样式检查 1 项；前阶段 3034 项按原测试名集合保留。默认 Debug/Release 合并回归各 **3089/3089**，零失败、零跳过、零编译警告/错误。最终定向 **71/71**（新增 54 + 既有架构 17）通过，含 2 项实际 Skia 窗口渲染，四张明暗主题草稿/错误状态已检查。定向与合并集合重叠，不累加。

```sh
/usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -c Debug --filter "$(cat artifacts/validation/coating-layer-constraints-20261003/test-filter.txt)" --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/validation/coating-layer-constraints-20261003
```

Release 使用同一筛选器并替换配置及结果名。实际渲染使用绝对路径 `OPTILAND_COATING_LAYER_CAPTURE_DIR` 和 `CoatingLayerEditorTests` 筛选器。本批未执行实验室全量或新增原生捕获，见[范围记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)、[验证清单](../artifacts/validation/coating-layer-constraints-20261003/verification.json)。

### 顺序操作数第三十三批复验（2026-10-03）

CODA、系统 Jones 状态、侧栏及 RRET/照度联动新增 72 项，前阶段 2962 项全部保留。默认 Debug/Release 输出合并回归各 **3034/3034**，零失败、零跳过、零编译警告/错误。定向 179/179、最终 Skia UI 2/2 与合并集合重叠，不累加。四张明暗主题图像验证保存状态及视口内的非法输入提示。

```sh
/usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -c Debug --filter "$(cat artifacts/validation/coating-data-20261003/test-filter.txt)" --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/validation/coating-data-20261003
```

Release 使用同一筛选器，配置/结果名改为 Release / release.trx。最终渲染使用 `OPTILAND_POLARIZATION_CAPTURE_DIR` 的绝对路径及 `FullyQualifiedName~PolarizationSettingsPanelTests` 筛选器。未运行实验室全量验收或新原生捕获；固定 29 个 Zemax 文件、源码、默认二进制和测试证据见[机器清单](../artifacts/validation/coating-data-20261003/verification.json)，范围见[实现记录](COATING_DATA_OPERAND_2026-10-03.md)。

### 顺序操作数第三十二批复验（2026-10-03）

共享复电场追迹与 RRET 新增 47 项，前阶段 2915 项全部保留。默认 Debug/Release 输出合并回归各 **2962/2962**，零失败、零跳过、零编译警告/错误；Debug 测试约 7 分 9 秒，Release 约 4 分 8 秒。定向复验 120/120 与合并集合重叠，不累加。没有重跑实验室全量验收，也没有新增 Zemax 原生捕获。

```sh
/usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -c Debug --filter "$(cat artifacts/validation/polarization-retardance-20261003/test-filter.txt)" --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/validation/polarization-retardance-20261003
```

Release 使用同一筛选器，配置和结果名改为 Release / release.trx。格式整理、差异检查及固定 29 个 Zemax 文件完整性检查通过。[实现与边界](POLARIZATION_RETARDANCE_2026-10-03.md)、[测试/源码/默认二进制清单](../artifacts/validation/polarization-retardance-20261003/verification.json)。

### 顺序操作数第二十八批复验（2026-10-02）

BFSD 本地接入及共享最小体积拟合新增 26 项。首次定向 24/26，两项测试夹具缺陷修复后 26/26；生产实现没有为通过捕获而调整结果。合并过滤器为前批 2471 项与新增 26 项的并集，默认 Debug/Release 输出构建成功，同一过滤器各 **2497/2497**，零失败/跳过、无编译警告或错误。格式及 git diff --check 通过。

```sh
/usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentyeighth-20261002/test-filter.txt)" --logger 'trx;LogFileName=combined-debug.trx' --results-directory artifacts/validation/sequential-operands-twentyeighth-20261002
```

Release 使用同一过滤器，将配置和日志名改为 Release / combined-release.trx。所有构建使用默认输出。没有重跑全产品或实验室验收，没有新增外部捕获或 Optiland 对照。固定球面矢高表半径按原文本精度核对；此证据不等于非球面或原生 MFE 验收。[实现说明](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)、[机器摘要](../artifacts/validation/sequential-operands-twentyeighth-20261002/verification.json)。

### 顺序操作数第二十七批复验（2026-10-02）

RELI/EFNO 接通，共享 Core 算法未变。先运行新增入口定向 30/30，再补充一个固定文件差异观测；最终合并过滤器为第二十六批、193 项照度基础及 31 项新用例的并集，重叠用例不累加。默认 Debug/Release 输出构建成功，同一过滤器各 **2471/2471**、零失败/跳过；其中第二十六批 2308 项、照度基础新增纳入 132 项、本批新增 31 项。合并后仅修正测试对象初始化器换行，新增 31 项再次定向验证；不重复累加。格式检查与 git diff --check 通过，编译日志无警告或错误。

```sh
/usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentyseventh-20261002/test-filter.txt)" --logger 'trx;LogFileName=combined-debug.trx' --results-directory artifacts/validation/sequential-operands-twentyseventh-20261002
```

Release 使用同一命令，将配置和日志名改为 Release / combined-release.trx。默认桌面输出；未运行实验室、全产品或新增原生捕获。原有照度与 FCGT 门槛不变，已提交基线的完整性、当前重算和数值差异分项记录。[实现说明](ILLUMINATION_OPERANDS_2026-10-02.md)、[机器摘要](../artifacts/validation/sequential-operands-twentyseventh-20261002/verification.json)。

### 像方方向余弦采样复验（2026-10-02）

新增可选均匀像方网格、正式光线求逆和轴上参考；默认自适应分析路径保留，RELI/EFNO 仍兼容。默认 Debug/Release Core、Application、App 与主测试项目构建成功；合并照度、镀膜、快照及追迹回归各 **193/193**，含新增 23 项。不是 2308 项操作数合并回归、实验室或全产品验证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/image-cosine-illumination-20261002/test-filter.txt)" --logger 'trx;LogFileName=image-cosine-debug.trx' --results-directory artifacts/validation/image-cosine-illumination-20261002
```

Release 更换配置和结果文件名。首次新测试 18 通过/1 失败，原因是透明偏振测试误用了含吸收目录玻璃的夹具；换用显式透明材料，保留不支持边界。新增轴上参考与异常测试后 23/23，再运行上述合并过滤器。最终日志无编译警告/错误，格式与 git diff --check 通过。新网格低密度有明显边界计数误差，没有替换默认算法或放宽原生门槛。[实现与数值观察](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)、[机器摘要](../artifacts/validation/image-cosine-illumination-20261002/verification.json)。没有新增 UI 渲染、Zemax 捕获或 Optiland 比较。

### 物理镀膜追迹与保存复验（2026-10-02）

共享相干求解器新增复振幅，正式追迹/照度使用实际入射角、完整 Jones 功率链和原生材料快照。默认 Debug/Release Core、Application、App 与主测试输出构建成功；阶段过滤器各 **169/169**。其后严格材料内部系数检查新增一项用例，最终膜层与快照定向各 **42/42**。帮助页相关各 **19/19**，Debug 另渲染 8 张实际控件图；Release 将这两组一次运行，共 **61/61**。最终实验室物理/工作流定向各 **25/25**，包含冻结 120 组参考的单项用例。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~OpticSnapshotValidationTests' --logger 'trx;LogFileName=coherent-persistence-debug.trx' --results-directory artifacts/validation/coherent-coating-transport-20261002
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~CoherentCoatingTransportTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~OperandAuthenticityTests|FullyQualifiedName~LayeringArchitectureTests.AppUiFontSizesUseTypographyTokens' --logger 'trx;LogFileName=coherent-persistence-help-release.trx' --results-directory artifacts/validation/coherent-coating-transport-20261002
dotnet test labs/CoatingDesign/tests/OptilandWorkbench.CoatingDesign.Tests/OptilandWorkbench.CoatingDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ThinFilmPhysicsTests|FullyQualifiedName~WorkflowTests' --logger 'trx;LogFileName=coating-lab-final-release.trx' --results-directory artifacts/validation/coherent-coating-transport-20261002
```

实验室 Debug 更换配置/文件名；169 项阶段过滤器保存在同目录 test-filter.txt。最终帮助 Debug 命令与截图范围见[帮助记录](../artifacts/validation/operand-help-final-20261002/verification.json)。日志无最终编译警告/错误；最初局部变量重名编译失败日志保留，改名后通过。最终格式与 git diff --check 通过。169 项未在最后追加系数缺失测试后完整重跑为 170 项，42 项是该修正的定向复验，不与重复范围相加。不是 2308 项回归、实验室完整 44 项、全产品或发布验证。RELI/EFNO 仍未接通，固定 RI 差异和 FCGT Difference 不变。[实现范围](COHERENT_COATING_TRANSPORT_2026-10-02.md)、[机器摘要](../artifacts/validation/coherent-coating-transport-20261002/verification.json)。

### 照度偏振与入射边界复验（2026-10-02）

透明介质的非偏振光 Jones 功率链、类型化反射/全反射、标量镀膜单次加权及像面自身作用前的入射功率已接通。默认 Debug/Release Core、Application、App 和主测试项目构建成功，相同定向过滤器各 **126/126**（本轮新增 26 项包含其中），零失败/跳过，编译无警告/错误，格式及差异检查通过。该过滤器包含前一阶段的 83 项和 17 项既有共享追迹/后端用例，不是 2308 项合并回归或全产品发布复验。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/illumination-polarization-20261002/test-filter.txt)" --logger 'trx;LogFileName=polarized-illumination-debug.trx' --results-directory artifacts/validation/illumination-polarization-20261002
```

Release 更换配置与结果名。运行窗口约 185 / 50 秒，不能作为严格性能比较。已有密度 10 RI 比较保持原值和门槛；本机密度 5/20/40 观测显示加密当前光瞳网格未消除绝对 F 数差异。没有新建 Zemax 捕获，未认证原生 Samp、Pol 全模式或 MFE 调用；RELI/EFNO 仍为兼容保留。[实现与边界](ILLUMINATION_POLARIZATION_2026-10-02.md)、[机器记录](../artifacts/validation/illumination-polarization-20261002/verification.json)、[Debug](../artifacts/validation/illumination-polarization-20261002/polarized-illumination-debug.trx)、[Release](../artifacts/validation/illumination-polarization-20261002/polarized-illumination-release.trx)。以下照度基础节保留第一阶段范围。

### 共享照度基础复验（2026-10-02）

全光瞳标量积分、内部遮挡、像面投影、有效 F 数及图像模拟全暗处理；RELI/EFNO 本轮仍未接通。默认 Debug/Release Core、Application、App 与测试项目构建成功，同一直接相关过滤器各 **83/83**（新增 26 项包含其中），零失败/跳过、无编译警告/错误。既有相对照度用例随后各重跑 **1/1** 以保存绝对 F 数观测，不与 83 相加。格式和 git diff --check 通过。该过滤器不是此前 2308 项合并回归或全产品发布验证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/illumination-core-20261002/test-filter.txt)" --logger 'trx;LogFileName=illumination-debug-verified.trx' --results-directory artifacts/validation/illumination-core-20261002
```

Release 更换配置和结果名。Debug 约 3 分 2 秒、Release 约 54 秒；两种配置不是性能等价对照。原有 RI 门槛保持不变并通过，但相比此前方法误差增大；绝对有效 F 数另有约 0.309% 最大相对差异，尚未认证。[实现边界](ILLUMINATION_CORE_2026-10-02.md)、[机器记录](../artifacts/validation/illumination-core-20261002/verification.json)、[Debug](../artifacts/validation/illumination-core-20261002/illumination-debug-verified.trx)、[Release](../artifacts/validation/illumination-core-20261002/illumination-release.trx)。

### 光学装调无穷远模式复验（2026-09-29）

```sh
dotnet build tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~OpticalAssembly|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~WorkspaceDockModelTests|FullyQualifiedName~LayeringArchitectureTests' --logger 'trx;LogFileName=assembly-regression-debug.trx' --results-directory artifacts/validation/optical-assembly-infinity-20260929
```

增加 `-c Release` 并更换 TRX 名称为 `assembly-regression-release.trx`，复验相同范围。最终默认输出构建两配置各零警告、零错误；回归各 80/81，唯一失败仍为既有主题字面圆角检查。装调 23 项均通过，本次新增 9 项，不与总数相加。此前中间构建遇到工作区另一任务的元组名称警告，最终构建已无该警告；一次测试权限自动审核超时后重试成功。

独立 Skia 运行过滤器 `FullyQualifiedName~OpticalAssemblyWindowTests`，增加 `--environment OPTICAL_ASSEMBLY_CAPTURE_DIR=<仓库绝对路径>/artifacts/validation/optical-assembly-infinity-20260929/rendered`，TRX 为 `assembly-skia.trx`，3/3 通过。核对附加物镜和无穷远模式、正常与最小窗口的实际控件图像；未作原生桌面或实物仪器标定。

证据位于 `artifacts/validation/optical-assembly-infinity-20260929/`；[机器摘要](../artifacts/validation/optical-assembly-infinity-20260929/verification.json)含最终源码/默认二进制哈希，使用和物理边界见 [装调指南](OPTICAL_ASSEMBLY_LAB.md)。不改变正式全量与外部 Zemax 基线。下方装调记录为有限共轭初版的历史范围。

### 顺序操作数第二十六批复验（2026-10-02）

DIST/DISA 本地执行与网格非径向偏差修正；默认 Debug/Release Core/Application/App 与测试项目编译成功，同一过滤器各 **2308/2308**，零失败/跳过，日志无编译警告或错误。包含前批 2264、新增 40 和相邻既有网格显示回归 4 项，不是全部产品/实验室或发布验证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentysixth-20261002/test-filter.txt)" --logger 'trx;LogFileName=twentysixth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentysixth-20261002
```

Release 更换配置和结果名。初次编译修正测试中的不可用应用构造函数；首次 smoke 36/37，修正编辑器参数索引从一基到零基，并增加 3 项坐标/视场测试后 40/40。首次合并 2305/2308，修正 3 处将所有 DISA 当作占位类型的旧断言，改查显式兼容行及仍未实现的 DISC；原失败日志/TRX 保留，数值门槛未放宽。格式与 git diff --check 通过。

[实现边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)、[机器摘要](../artifacts/validation/sequential-operands-twentysixth-20261002/verification.json)、[Debug TRX](../artifacts/validation/sequential-operands-twentysixth-20261002/twentysixth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentysixth-20261002/twentysixth-batch-release.trx)。25 份选定捕获与 HEAD 字节一致；固定 123456/440 nm 的 22 个逐面 W311 最大误差 4.919e-7 波长、累计 TDIS 误差 3.847e-7 mm。原生 DIST/DISA MFE 参数列与数值仍有待验边界，两项原生导入只读；DISC 未接通；FCGT 仍为 Difference。没有新原生捕获、Optiland 比较或本批截图。

### 顺序操作数第二十五批复验（2026-10-02）

MECA 本地平均路径、MECS/MECT 共同参考波前差及共享对比度损失图；默认 Debug/Release Core/Application/App 与测试项目编译成功，同一过滤器各 **2264/2264**，零失败/跳过，日志无编译警告或错误。包含前批 2197、新增 61 和相邻既有 6 项，不是全部产品/实验室或发布验证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentyfifth-20261002/test-filter.txt)" --logger 'trx;LogFileName=twentyfifth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentyfifth-20261002
```

Release 更换配置和结果名。新增测试初次编译修复了命名空间、辅助方法签名及向导必填参数；第一次 smoke 53 通过/2 个测试夹具失败，分别是选点两方向均超瞳孔、主波长预期模型未同步完整状态，修正后定向 67/67。原失败日志/TRX 保留，没有放宽数值门槛。格式与 git diff --check 通过。

[实现边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十五批对比度操作数与共享损失图部分完成)、[机器摘要](../artifacts/validation/sequential-operands-twentyfifth-20261002/verification.json)、[Debug TRX](../artifacts/validation/sequential-operands-twentyfifth-20261002/twentyfifth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentyfifth-20261002/twentyfifth-batch-release.trx)。25 份捕获字节等于 HEAD；固定损失图 186 点的 NRMSE 为 9.268e-9、最大绝对误差 2.559e-9。三项原生 MFE 符号/数值与 MECA 聚合/列语义尚未验收，原生 MECA 只读；既有 FCGT 仍为 Difference。没有新原生捕获、Optiland 比较或本批截图。

### 顺序操作数第二十四批复验（2026-10-02）

ZERN 本地七槽、系数/统计、有序拟合复用、Noll 编号修正与稳定环形基底；默认 Debug/Release 的 Core/Application/App 和测试项目编译成功，同一过滤器各 **2197/2197**，零失败/跳过，日志无编译警告或错误。45 项新增、22 项相邻已有验证，加上前批 2130 项；不是全产品、实验室或发布认证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentyfourth-20261002/test-filter.txt)" --logger 'trx;LogFileName=twentyfourth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentyfourth-20261002
```

Release 更换配置与结果名。初次 smoke 为 41 通过/1 个夹具失败：直接更改厚度后未调用正常坐标刷新 Renumber，修正夹具后再运行合并回归；原 TRX 保留，未改数值门槛。更早的测试编译因数组误用 RemoveAt 修正。本批格式与 git diff --check 通过。

[实现边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十四批-zern-与共享拟合修正部分完成)、[机器摘要](../artifacts/validation/sequential-operands-twentyfourth-20261002/verification.json)、[Debug TRX](../artifacts/validation/sequential-operands-twentyfourth-20261002/twentyfourth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentyfourth-20261002/twentyfourth-batch-release.trx)。22 份选定捕获字节等于 HEAD，属于完整性子集；Zernike 原生采样设置未核实，未新增其数值比较。已有 FCGT 保持 Difference，没有新原生捕获、Optiland 比较或本批 UI 渲染。Vertex=1、Fringe 原生组规则和扩展列验收尚未完成。

### 顺序操作数第二十三批复验（2026-10-01）

FDMO/FDRE 隔离视场状态、REQS 标记、共享完整瞳孔变换与保存链路；294 项受限执行、89 项兼容保留。默认 Debug/Release Core/Application/App 和测试输出成功编译，同一过滤器各 **2130/2130**，无失败/跳过或编译警告。新增 48 项，另纳入帮助净增 2 及既有相邻 23 项，原 2057 项阶段范围保留。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentythird-20261001/test-filter.txt)" --logger 'trx;LogFileName=twentythird-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentythird-20261001
```

Release 使用相同过滤器，更换配置和结果名。第一轮两配置各 2129 通过、1 个旧注册数量断言失败（291 已变为 294）；修正断言后完整重跑上述范围，初次日志保留在 `initial-*`，没有改动数值门槛。格式和 `git diff --check` 通过。

独立 Skia 过滤器 `FullyQualifiedName~CompactSidebarTests`，增加 `--environment OPTILAND_ACTION_CAPTURE_DIR=<仓库绝对路径>/artifacts/validation/sequential-operands-twentythird-20261001/screenshots`，**5/5**，属于上述回归子集，不与 2130 相加。检查窄侧栏、已编辑的偏移/角度，以及旧宽布局恢复；[参数渲染](../artifacts/validation/sequential-operands-twentythird-20261001/screenshots/sidebar-pupil-factors-256.png)。这是实际控件测试渲染，未做原生桌面走查。

[实现边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十三批视场状态与完整瞳孔变换部分完成)、[机器摘要](../artifacts/validation/sequential-operands-twentythird-20261001/verification.json)、[Debug TRX](../artifacts/validation/sequential-operands-twentythird-20261001/twentythird-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentythird-20261001/twentythird-batch-release.trx)。选定 16 份已提交 Zemax 捕获保持逐字节不变，属于基线完整性子集；当前 Core 重算既有固定文件/设置回归通过，FCGT 仍为 Difference。没有新增原生 MFE 参数/数值捕获，不是全部产品、实验室或安装包验证，没有新增 Optiland 比较。

## 操作数帮助层级复验（2026-10-01）

2026-10-02 可视层级改进：帮助页明确显示一级大类、二级操作数族和三级具体代码；概览中的下级目录支持点击导航，保持树选择同步并复位详情滚动。默认 Debug/Release 输出构建成功，同一帮助过滤器各 **19/19**，零失败或跳过；检查 8 张当前 Avalonia/Skia 明暗主题和窄窗口渲染。扩充现有交互断言，没有新增测试用例或改变 2308 项合并回归的历史范围，也没有重跑该合并回归。见[本次证据](../artifacts/validation/operand-help-levels-20261002/verification.json)及[当前渲染](../artifacts/validation/operand-help-levels-20261002/screenshots/operand-help-light-grin-family.png)。以下是此次产品改进之前的复核记录。

2026-10-02 当前工作区再次检查：默认 Debug/Release 输出重新构建，同一帮助过滤器各 **19/19** 通过，零失败或跳过；8 张当前 Avalonia/Skia 控件图检查完成。此次未修改产品源码，仅核实三级目录、名称/支持标识、交互和布局；不认证在途操作数计算，也不改变既有全量/阶段测试基线。见[最新证据](../artifacts/validation/operand-help-hierarchy-20261002/verification.json)及[当前渲染](../artifacts/validation/operand-help-hierarchy-20261002/screenshots/operand-help-light-grin-family.png)。

2026-10-02 再次复核完成（沿用 20261001 启动目录）：默认 Debug/Release 编译成功，同一帮助过滤器各 **19/19**；Debug 重新生成并检查 8 张 Light/Dark 与窄窗口实际控件渲染。未修改产品代码，未重跑全部光学回归，未改变后续操作数的验证状态或全量基线。[复核摘要](../artifacts/validation/operand-help-hierarchy-recheck-20261001/verification.json)。以下保留初版阶段记录。

帮助目录按“功能大类 → 操作数族 → 具体操作数”浏览：8 类、51 族、387 项；默认折叠，搜索保留路径，GRIN 编号约束集中显示，实现状态独立标注。仅修改呈现和帮助投影，不新增计算能力。

默认 Debug/Release Core、Application、App 与测试项目编译成功，编译零警告/错误；定向回归各 **19/19**，零失败/跳过（帮助 6、官方名称/状态 12、全局字号守护 1）。覆盖完整目录唯一归类、族与位置搜索、状态过滤、空结果与选择恢复、键盘展开、Light/Dark 和 680 DIP 窄窗口。Debug 同步保存 8 张实际 Avalonia/Skia 渲染图，已人工检查层级、状态和布局。格式检查与 git diff --check 通过。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~OperandHelpTests|FullyQualifiedName~OperandAuthenticityTests|FullyQualifiedName~LayeringArchitectureTests.AppUiFontSizesUseTypographyTokens' --logger 'trx;LogFileName=hierarchy-debug.trx' --results-directory artifacts/validation/operand-help-hierarchy-20261001
```

Release 更换配置和结果文件名；设置 `OPTILAND_OPERAND_HELP_CAPTURE_DIR` 可在该测试过滤器下启用实际 Skia 渲染。见[说明与图片](OPERAND_HELP_HIERARCHY_2026-10-01.md)、[验证摘要](../artifacts/validation/operand-help-hierarchy-20261001/verification.json)、[Debug](../artifacts/validation/operand-help-hierarchy-20261001/hierarchy-debug.trx)、[Release](../artifacts/validation/operand-help-hierarchy-20261001/hierarchy-release.trx)。本轮不是全部光学回归、原生 Zemax 数值比较或全产品发布验证；此前 2057 项审计阶段记录保留原范围。

### 操作数官方名称核对复验（2026-10-01）

逐项对比官方 2026 R1 的 448 名称 API 枚举：383 个注册代码全部匹配；独立夹具与回归防止新增虚构名称。明确标识 4 个本程序扩展、5 个官方 Unused 名称和 2 个公开定义未核实项，没有新增光学计算功能。

默认 Debug/Release 各 **2057/2057**，零失败/跳过（此前 2045 + 新增 12），Core/Application/App 默认输出已更新，日志无编译警告或错误。格式及 git diff --check 通过。[审计结论](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)、[383 行核对](../artifacts/validation/zemax-operand-authenticity-20261001/registered-codes.csv)、[验证摘要](../artifacts/validation/zemax-operand-authenticity-20261001/verification.json)、[Debug TRX](../artifacts/validation/zemax-operand-authenticity-20261001/authenticity-debug.trx)、[Release TRX](../artifacts/validation/zemax-operand-authenticity-20261001/authenticity-release.trx)。

```sh
operand_filter=$(cat artifacts/validation/zemax-operand-authenticity-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=authenticity-debug.trx' --results-directory artifacts/validation/zemax-operand-authenticity-20261001
```

Release 使用相同过滤器更换配置与结果文件名。本次是官方名称及实现范围核对，不是新 Zemax 运行、MFE 参数捕获或全操作数数值认证。既有选定捕获 14 项字节不变，FCGT 仍为 Difference。以下保留第二十二批及更早历史范围。

### 顺序操作数第二十二批复验（2026-10-01）

新增 SPHS/PSLP/DPHS/QSLP，291 可执行、92 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十二批相位与斜率模值部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **2045/2045**，零失败/跳过（前批 1980 + 新增 65）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-twentysecond-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=twentysecond-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentysecond-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-twentysecond-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-twentysecond-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-twentysecond-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-twentysecond-20261001/twentysecond-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentysecond-20261001/twentysecond-batch-release.trx)。

新操作数原生列及数值未验证，导入只读。选定捕获完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、完整产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第二十一批复验（2026-10-01）

新增 DENC/DENF，287 可执行、96 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十一批衍射圈入能量部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **1980/1980**，零失败/跳过（前批 1928 + 新增 51 + 相邻 1）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-twentyfirst-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=twentyfirst-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentyfirst-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-twentyfirst-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-twentyfirst-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-twentyfirst-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-twentyfirst-20261001/twentyfirst-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentyfirst-20261001/twentyfirst-batch-release.trx)。

新操作数原生列及数值未验证，导入只读。选定捕获完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、完整产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第二十批复验（2026-10-01）

新增 SSAG/SSLP/SCRV，285 可执行、98 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十批指定点面形部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **1928/1928**，零失败/跳过（前批 1873 + 新增 55）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-twentieth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=twentieth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twentieth-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-twentieth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-twentieth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-twentieth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-twentieth-20261001/twentieth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twentieth-20261001/twentieth-batch-release.trx)。

新操作数原生列及数值未验证，导入只读。选定捕获完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、完整产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第十九批复验（2026-10-01）

新增 GENC/GENF/ERFP，282 可执行、101 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十九批几何能量与边缘响应部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **1873/1873**，零失败/跳过（前批 1804 + 新增 62 + 新纳入相邻回归 7）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-nineteenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=nineteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-nineteenth-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-nineteenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-nineteenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-nineteenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-nineteenth-20261001/nineteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-nineteenth-20261001/nineteenth-batch-release.trx)。

首次 Release 为 1872/1873：既有 `FieldCurvatureFooterTests.ProductAndDocumentRegionKeepsTheSameBoundsAcrossAnalysisKinds(1200, Isekai)` 在 Avalonia Headless 会话销毁中抛出空且已关闭队列异常。未改代码或测试规避；同一过滤器完整重跑全部通过。[首次日志](../artifacts/validation/sequential-operands-nineteenth-20261001/release-first.log)、[首次 TRX](../artifacts/validation/sequential-operands-nineteenth-20261001/nineteenth-batch-release-first.trx)。这次重跑不能证明该测试设施的时序问题已修复。

新操作数原生列及数值/分位规则未验证，导入只读。选定捕获完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、完整产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第十八批复验（2026-10-01）

新增 VOLU/TMAS/DSAG/DSLP/DCRV，279 可执行、104 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十八批制造与面形统计部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **1804/1804**，零失败/跳过（前批 1730 + 新增 74）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-eighteenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=eighteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-eighteenth-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-eighteenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-eighteenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-eighteenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-eighteenth-20261001/eighteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-eighteenth-20261001/eighteenth-batch-release.trx)。

本批五项原生参数列及数值等价未验证，导入保持只读。选定捕获资产完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、全产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第十七批复验（2026-10-01）

新增 PRIM/CVIG/IMSF，274 可执行、109 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十七批批次内系统状态部分完成)。

默认 Debug/Release Core、Application、App 与测试输出成功编译，同一过滤器各 **1730/1730**，零失败/跳过（前批 1684 + 新增 46）。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-seventeenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=seventeenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-seventeenth-20261001
```

Release 替换配置和文件名。[摘要与哈希](../artifacts/validation/sequential-operands-seventeenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-seventeenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-seventeenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-seventeenth-20261001/seventeenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-seventeenth-20261001/seventeenth-batch-release.trx)。

PRIM/IMSF 原生参数列及数值等价未验证，导入保持只读。选定捕获资产完整性及既有分析回归通过，FCGT 仍为 Difference。无新增外部捕获、截图、全产品/实验室全量或安装包验证。以下各批保留历史范围。

### 顺序操作数第十六批复验（2026-10-01）

新增 STRH/CEHX/CEHY，271 可执行、112 兼容保留。[行为与限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十六批惠更斯-psf-测量部分完成)。

默认 Debug/Release Core、Application、App 及测试输出成功编译，同一过滤器各 **1684/1684**，零失败/跳过，含前批 1622 项、本批新增 57 项、新纳入 PSF/截面/偏振标签/分析预设相邻回归 5 项。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-sixteenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=sixteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-sixteenth-20261001
```

Release 替换配置和 TRX 文件名。[摘要与哈希](../artifacts/validation/sequential-operands-sixteenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-sixteenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-sixteenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-sixteenth-20261001/sixteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-sixteenth-20261001/sixteenth-batch-release.trx)。

选定已提交捕获文件完整性与现有分析回归通过，不等于新增 PSF 操作数的原生 MFE 数值等价。FCGT 仍为 Difference，既有 Huygens MTF 分析预算不变。未新增外部捕获、截图、全产品/实验室全量或安装包验收。以下各批保留历史范围。

### 顺序操作数第十五批复验（2026-10-01）

新增 TCVA/TCGT/TCLT，268 可执行、115 兼容保留。[行为及导入限制](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十五批表面热膨胀系数部分完成)。

默认 Debug/Release Core、Application、App 和测试输出编译成功；同一过滤器各 **1622/1622**，零失败/跳过，含前批 1516 项、本批新增 37 项和新纳入相邻回归 69 项。真实控件验证非法 TCE 不发布更新、纠正后一次提交、撤销恢复。格式及差异检查通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-fifteenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=fifteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-fifteenth-20261001
```

Release 替换配置和 TRX 文件名。[摘要及源码/默认二进制哈希](../artifacts/validation/sequential-operands-fifteenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-fifteenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-fifteenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-fifteenth-20261001/fifteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-fifteenth-20261001/fifteenth-batch-release.trx)。

未新增 Zemax TCE 原生记录或数值捕获，未执行截图验收、全产品/实验室全量、安装包验收。FCGT 的既有 Difference 与 Huygens 分析级回归预算均未改变。以下各批为历史记录。

### 顺序操作数第十四批复验（2026-10-01）

新增 GBPD/GBPP/GBPR/GBPS/GBPW/GBPZ 六项，265 可执行、118 兼容保留。[实现模式及边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第十四批近轴高斯光束部分完成)。

默认 Debug/Release Core、Application、App 与测试输出编译成功，最终日志无编译警告或错误；同一过滤器各 **1516/1516**，零失败/跳过，含此前 1449 项和本批新增 **67 项**。GBPP 已驱动正式 DLS 调焦。格式校验及 `git diff --check` 通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-fourteenth-20261001/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=fourteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-fourteenth-20261001
```

Release 替换配置与 TRX 文件名。[摘要/源码和二进制哈希](../artifacts/validation/sequential-operands-fourteenth-20261001/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-fourteenth-20261001/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-fourteenth-20261001/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-fourteenth-20261001/fourteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-fourteenth-20261001/fourteenth-batch-release.trx)。

本批没有新的高斯束原生 MFE 捕获；有限模式解析/集成通过不能标为完整 Zemax 对齐。既有 Huygens 分析与 FCGT 差异状态保持原边界。没有截图、全产品/实验室全量或安装包验收。以下第十三批及以前为历史记录。

### 顺序操作数第十三批复验（2026-09-30）

新增 MTHA/MTHS/MTHT/MTHN/MTHX 五项，当前 259 可执行、124 兼容保留。当前仅有焦、Samp=1..2、Pol=0、All Conf=0；七槽编辑/STAROPT 可用，原生 ZMX 扩展行仍只读。[实现与限制](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第十三批惠更斯-mtf部分完成)。

默认 Debug/Release Core、Application、App 与测试输出均编译成功，最终日志无编译警告或错误；使用相同过滤器各 **1449/1449**，零失败/跳过，包含前十二批 1382 项与本批新增 **67 项**。正式 DLS 调焦及七参数启用编辑重新计算通过。格式校验与 `git diff --check` 通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-thirteenth-20260930/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=thirteenth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-thirteenth-20260930
```

Release 使用相同命令替换配置和 TRX 文件名。证据：[摘要及哈希](../artifacts/validation/sequential-operands-thirteenth-20260930/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-thirteenth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-thirteenth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-thirteenth-20260930/thirteenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-thirteenth-20260930/thirteenth-batch-release.trx)。

固定 Huygens MTF 捕获 18 点最大绝对差 0.011900434932046483，满足 0.03 分析级回归预算，非原生 MFE 等价证据。FCGT 仍为 Difference，精度目标未放宽。未新增原生 MFE 捕获、截图审查、完整外部比较矩阵、全产品/实验室全量测试或安装包验收。以下第十二批及更早记录保留当时范围。

### 顺序操作数第十二批复验（2026-09-30）

新增 5 项（MNRI/MNRE/MXRI/MXRE/TFNO），修正 SFNO；254 可执行、129 兼容保留。七参数编辑与 STAROPT 可用，四个角度范围的原生 ZMX 扩展行仍只读。[实现与限制](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第十二批光线角度约束与工作-f-数部分完成)。

默认 Debug/Release Core、Application、App 与测试输出均成功编译，最终日志无编译警告或错误；相同过滤器各 **1382/1382**，零失败/跳过，包括新增 **78 项**、前十一批 1281 项和新增纳入的相邻回归 23 项。格式检查及 `git diff --check` 通过。

```sh
operand_filter=$(cat artifacts/validation/sequential-operands-twelfth-20260930/test-filter.txt)
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$operand_filter" --logger 'trx;LogFileName=twelfth-batch-debug.trx' --results-directory artifacts/validation/sequential-operands-twelfth-20260930
```

Release 用相同命令替换配置和 TRX 文件名。证据：[摘要及哈希](../artifacts/validation/sequential-operands-twelfth-20260930/verification.json)、[Debug 日志](../artifacts/validation/sequential-operands-twelfth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-twelfth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-twelfth-20260930/twelfth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-twelfth-20260930/twelfth-batch-release.trx)。

固定捕获回归继续执行；FCGT 仍为 Difference，精度目标未放宽。没有新增原生 MFE 捕获、截图审查、完整外部比较矩阵、全产品/实验室全量测试或安装包验收。以下第十一批及更早验证保留各自历史范围。

### 顺序操作数第十一批复验（2026-09-30）

新增 ABCD、DISG、DIMX、SMIA、FCGS、FCGT 共 **6 项**；当时 **249/383 可执行、134 项仅兼容保留**，十一批累计新增 125 项。[支持边界](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第十一批畸变与场曲部分完成)。

默认 Debug/Release Core、Application、App 和测试输出成功编译，最终日志无编译警告或错误；相同过滤器各 **1281/1281**，零失败/跳过，包括新增 `DistortionOperandTests` **94 项**、前十批 1173 项、相邻场曲/界面/兼容行回归 14 项。格式检查及 `git diff --check` 通过。功能回归通过与数值对照状态独立，FCGT 的已知残差未达到精度目标。

```sh
operand_filter='FullyQualifiedName~DistortionOperandTests|FullyQualifiedName~FieldCurvatureFooterTests|FullyQualifiedName~AnalysisGuiContractTests.CombinedFieldCurvatureAndDistortionExposesConvertedAngularModel|FullyQualifiedName~AnalysisGuiContractTests.GridDistortionExposesZemaxGridSettings|FullyQualifiedName~OptilandParityTests.MeritFunctionNeverConvertsUnknownOrCompatibilityOperandsToSuccessfulZero|FullyQualifiedName~WorkbenchApplicationTests.MeritFunctionEditorPreservesReadOnlyZemaxParameterSlotsWithoutClamping|FullyQualifiedName~MtfOperandTests|FullyQualifiedName~MtfMaximumFrequencyTests|FullyQualifiedName~PsfWorkingFNumberRegressionTests|FullyQualifiedName~HuygensMtfSamplingTests|FullyQualifiedName~ZemaxHuygensMtfParityTests|FullyQualifiedName~ZemaxHuygensMtfVsFieldParityTests|FullyQualifiedName~ZemaxHuygensThroughFocusMtfParityTests|FullyQualifiedName~WavefrontOperandTests|FullyQualifiedName~ActualFieldSamplingTests|FullyQualifiedName~RmsAnalysisTests|FullyQualifiedName~WavefrontMapAnalysisTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~ZemaxWavefrontMapParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFieldParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFocusParityTests|FullyQualifiedName~BundleMetricOperandTests|FullyQualifiedName~GlobalRayOperandTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=eleventh-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-eleventh-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=eleventh-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-eleventh-20260930
```

首轮定向子集 154 通过/5 失败：两项暴露边缘视场一阶差分偏差，已改共享二阶单边差分；两项角度双轴测试混淆角度向量长度和实际径向方向，已按 tan(角度) 定向并按同一真实入射角比较；一项 SMIA 测试越出现有归一化视场范围，已补明确范围报错和范围内解析验证。修复后当时 78/78。随后增加四项固定捕获比较，发现 FCGT 最大残差约 2.15333e−5 mm；步长/视场诊断日志保留，来源尚未确定。正式精度目标仍为 1e−5 mm，数值状态 Difference；新增 3e−5 mm 的测试上界仅监控现有残差不扩大。再增加主波长/实像高快照等覆盖后，本批 94 项纳入最终 1281 项。

证据：[验证摘要及源码/默认 DLL 哈希](../artifacts/validation/sequential-operands-eleventh-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-eleventh-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-eleventh-20260930/remaining-by-category.csv)、[Debug 日志](../artifacts/validation/sequential-operands-eleventh-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-eleventh-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-eleventh-20260930/eleventh-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-eleventh-20260930/eleventh-batch-release.trx)。

固定捕获比较按三条波长、每波长轴上/半视场/最大角各九点：DISG/DIMX 在 2e−5 百分点目标内，FCGS 在 1e−5 mm 目标内；FCGT 未达标。未使用分析标题或轴标签猜单位。既有已纳入的固定 Zemax 回归继续通过。九份选定基线资产与 HEAD 字节一致，并非全基线审计；未新增原生 MFE 捕获或 Optiland 比较、未改冻结历史；未作截图、原生桌面走查、完整产品/实验室或完整外部矩阵认证。

### 顺序操作数第十批复验（2026-09-30）

第十批历史记录（以下为该批结束时状态）。

新增 GMT/MTF/MSW 三族 A/S/T/N/X 共 **15 项**；当时 **243/383 可执行、140 项仅兼容保留**，十批累计新增 119 项。[支持边界](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第十批几何衍射与方波-mtf部分完成)。

默认 Debug/Release Core、Application、App 和测试输出成功编译，最终日志无编译警告或错误。同一过滤器各 **1173/1173**，零失败/跳过，包含新增 `MtfOperandTests` **108 项**、前九批 1017 项与相邻 MTF/PSF 回归 48 项。格式验证及 `git diff --check` 通过。

```sh
operand_filter='FullyQualifiedName~MtfOperandTests|FullyQualifiedName~MtfMaximumFrequencyTests|FullyQualifiedName~PsfWorkingFNumberRegressionTests|FullyQualifiedName~HuygensMtfSamplingTests|FullyQualifiedName~ZemaxHuygensMtfParityTests|FullyQualifiedName~ZemaxHuygensMtfVsFieldParityTests|FullyQualifiedName~ZemaxHuygensThroughFocusMtfParityTests|FullyQualifiedName~WavefrontOperandTests|FullyQualifiedName~ActualFieldSamplingTests|FullyQualifiedName~RmsAnalysisTests|FullyQualifiedName~WavefrontMapAnalysisTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~ZemaxWavefrontMapParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFieldParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFocusParityTests|FullyQualifiedName~BundleMetricOperandTests|FullyQualifiedName~GlobalRayOperandTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=tenth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-tenth-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=tenth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-tenth-20260930
```

首轮子集 129 通过/3 失败：两项暴露多色曲线重复插值偏差，已增加正式 Core 单频复 OTF 合成接口；另一项测试用纯 Y 视场却要求弧矢相位非零，已改为 X/Y 均非零的视场。修复后子集 132/132。随后扩展回归 1168 通过/2 失败，是严格原始 Field 校验拒绝两项旧 MNAI/MXAI 往返夹具的 `0.125` 分数视场；已改为合法整数且新增三项非法字段拒绝测试。最终两种配置各 1173/1173；早期日志仅作诊断。

证据：[验证摘要及哈希](../artifacts/validation/sequential-operands-tenth-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-tenth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-tenth-20260930/remaining-by-category.csv)、[Debug 日志](../artifacts/validation/sequential-operands-tenth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-tenth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-tenth-20260930/tenth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-tenth-20260930/tenth-batch-release.trx)。

本批增加已有惠更斯 MTF/视场/离焦固定捕获的 Workbench 重算和原设置数值比较；这不证明新增 FFT/几何 MFE 等价。验证摘要核对前批五份资产及三份惠更斯 MTF JSON 与提交内容，共八份，不是全基线完整性审计。无新增原生 MFE 槽位、采样或数值捕获，无截图审查；未执行全产品/实验室全量、完整外部矩阵或安装包验证。冻结历史资产未改，未新增 Optiland 比较。

### 顺序操作数第九批复验（2026-09-30）

第九批历史记录（以下为该批结束时状态）。

新增 RMS 波前 RWCE/RWCH/RWRE/RWRH 和峰谷波前 MWCE/MWCH/MWRE/MWRH 共 **8 项**；当时 **228/383 可执行、155 项仅兼容保留**，九批累计新增 104 项。[支持边界](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第九批-rms-与峰谷波前部分完成)。

默认 Debug/Release Core、Application、App 与测试输出均成功编译，最终日志没有编译警告或错误。同一过滤器各 **1017/1017**，零失败/跳过；含新增 `WavefrontOperandTests` **63 项**、前八批 933 项、另外纳入的相邻波前/RMS/无焦回归 21 项。格式验证和 `git diff --check` 通过。测试行对象初始化的换行格式修正不改变计算逻辑。

```sh
operand_filter='FullyQualifiedName~WavefrontOperandTests|FullyQualifiedName~ActualFieldSamplingTests|FullyQualifiedName~RmsAnalysisTests|FullyQualifiedName~WavefrontMapAnalysisTests|FullyQualifiedName~AfocalImageSpaceAnalysisTests|FullyQualifiedName~ZemaxWavefrontMapParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFieldParityTests|FullyQualifiedName~ZemaxRmsWavefrontVsFocusParityTests|FullyQualifiedName~BundleMetricOperandTests|FullyQualifiedName~GlobalRayOperandTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=ninth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-ninth-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=ninth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-ninth-20260930
```

首轮子集 62 通过、8 失败，均来自新测试揭示的旧兼容行升级只检查 Wavelength 别名、未检查实际求值原始 Int2 的问题。已修复共享快照波长校验；最终 Debug/Release 扩展回归均通过。早期子集日志为诊断记录，不是最终验证结果。

证据：[验证摘要及哈希](../artifacts/validation/sequential-operands-ninth-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-ninth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-ninth-20260930/remaining-by-category.csv)、[Debug 日志](../artifacts/validation/sequential-operands-ninth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-ninth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-ninth-20260930/ninth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-ninth-20260930/ninth-batch-release.trx)。

原有 Zemax 固定捕获通过当前 Workbench 重算及原设置数值比较；本批额外纳入波前图、RMS 视场/离焦测试。验证摘要逐字节核对所选 ZMX、赛德尔文本及三份波前 JSON 与提交内容，共五个资产，不是整个基线的完整性审计。新操作数仅有定义级实现与解析/集成证据，无新原生 MFE 网格/多色数值捕获；无截图审查，未执行全产品/实验室全量、完整外部比较矩阵或安装包验证。冻结历史资产未修改，未新增 Optiland 比较。

### 顺序操作数第八批复验（2026-09-30）

第八批历史记录（以下为该批结束时的状态）。

新增质心 CENX/CENY/CNPX/CNPY/CNAX/CNAY 和几何点列半径 GSCE/GSCH/GSRE/GSRH，共 **10 项**；当时 **220/383 可执行、163 项仅兼容保留**，八批累计新增 96 项。支持范围见 [第八批清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第八批光线束质心与几何点列半径部分完成)。

默认 Debug/Release Core、Application、App 及测试输出均成功编译，日志无编译警告或错误。相同过滤器各 **933/933**，零失败、零跳过；新增 `BundleMetricOperandTests` **76 项**和前七批 857 项全部包含，不另加。

```sh
operand_filter='FullyQualifiedName~BundleMetricOperandTests|FullyQualifiedName~GlobalRayOperandTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=eighth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-eighth-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=eighth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-eighth-20260930
```

首轮新增子集 67/67；扩大后一次 932 通过/1 失败，原因是新测试把 NaN 直接写入已有拒绝非有限数的波长 setter。测试已改为验证入口拒绝及单波长不受零光谱权重影响，最终 Debug/Release 各 933/933。该过程未改变既有有限数保护。早期日志仅供诊断，不作为最终结果。

证据：[验证摘要及哈希](../artifacts/validation/sequential-operands-eighth-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-eighth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-eighth-20260930/remaining-by-category.csv)、[Debug 日志](../artifacts/validation/sequential-operands-eighth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-eighth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-eighth-20260930/eighth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-eighth-20260930/eighth-batch-release.trx)。本批格式验证和 `git diff --check` 通过。

新项通过独立解析、真实追迹和正式优化入口回归；尚无新的原生 MFE 槽位/采样/数值捕获。既有主基准固定测试属于当前 Workbench 重算和原捕获设置数值比较；摘要对所用 ZMX 与赛德尔文本逐字节核对提交内容，非全基线完整性审计。未修改捕获或历史资产，未新增 Optiland 比较。未执行全产品/实验室全量、完整外部比较矩阵、桌面截图或安装包验证。

### 顺序操作数第七批复验（2026-09-30）

第七批历史记录：新增 GLCX/Y/Z、GLCA/B/C、GLCR，RAGX/Y/Z、RAGA/B/C，以及 DXDX/DXDY/DYDX/DYDY 共 **17 项**；当时 **210/383 可执行，173 项仅兼容保留**，七批累计新增 86 项。同时统一全局法线参考系、修复无效兼容行迁移，并保存/导入全局参考面。

默认 Debug/Release Core、Application、App 及测试输出成功编译，日志无编译警告或错误。相同过滤器各 **857/857**，零失败、零跳过：本批 `GlobalRayOperandTests` **122 项**、前六批 722 项、既有 `OpticSnapshotValidationTests` 13 项全部包含，不另加。

```sh
operand_filter='FullyQualifiedName~GlobalRayOperandTests|FullyQualifiedName~OpticSnapshotValidationTests|FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=seventh-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-seventh-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=seventh-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-seventh-20260930
```

独立解析和集成检查包括平移/组合旋转/矩阵顺序、Snell 折射/反射/TIR、参考面变化与缓存、有限/零/无穷物面、坐标断点引用映射、导入/保存/应用编辑、取消、非法参考只读迁移、薄透镜及孔径尺度、交叉导数、圆瞳边缘、未收敛和正式 GLCZ/DXDX 厚度 DLS。首次扩大回归暴露的旧兼容行误升级问题已修复，现有快照 13 项全部通过。限制见 [第七批清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第七批全局坐标与光线扇导数部分完成)。

证据：[验证摘要及源码哈希](../artifacts/validation/sequential-operands-seventh-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-seventh-20260930/operand-audit.csv)、[剩余类别](../artifacts/validation/sequential-operands-seventh-20260930/remaining-by-category.csv)、[Debug 日志](../artifacts/validation/sequential-operands-seventh-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-seventh-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-seventh-20260930/seventh-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-seventh-20260930/seventh-batch-release.trx)。源文件空白格式验证和 `git diff --check` 通过；前六批摘要哈希保留各自历史状态，第七批结束时修改见该摘要；当前见第二十二批摘要。

既有 Zemax 固定夹具继续覆盖各自捕获文件、版本和设置；本批没有新增原生 MFE 槽位/数值捕获，也没有以截图作新项数值认证。验证摘要核对所用既有 ZMX 与赛德尔文本的提交内容，非全基线完整性审计；未修改外部捕获或冻结历史资产，未新增 Optiland 比较。未执行全产品/实验室全量、完整外部比较矩阵、原生桌面/截图走查或安装包验证。

### 顺序操作数第六批复验（2026-09-30）

第六批阶段记录：新增 SPHA、COMA、ASTI、FCUR、PETC 共 5 项并修正 PETZ；当时 **193/383 可执行，190 项仅兼容保留**，六批累计新增 69 项。默认 Debug/Release Core、Application、App 及测试输出成功编译，日志无编译警告或错误；相同过滤器各 **722/722**，零失败、零跳过。第五批 644 项加本批 `SeidelOperandTests` **67 项**、既有赛德尔报告 10 项及分析 GUI 契约 1 项，均已包含，不另加。

```sh
operand_filter='FullyQualifiedName~SeidelOperandTests|FullyQualifiedName~SeidelCoefficientsAnalysisTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorExposesAndAppliesAnalysisParameters|FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=sixth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-sixth-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=sixth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-sixth-20260930
```

覆盖单球面独立解析、波前单位/孔径/半径尺度、选定波长光阑归一化、逐面累计、非空气像方介质、边界半径与零曲率、非法输入/模型、有限/无穷共轭、导入/保存/应用编辑、取消、报告不可用原因、正式 SPHA/DLS 半径优化及前五批回归。能力限制及槽位见 [第六批支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第六批赛德尔与佩兹伐约束部分完成)。

证据：[验证摘要及源码哈希](../artifacts/validation/sequential-operands-sixth-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-sixth-20260930/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-sixth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-sixth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-sixth-20260930/sixth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-sixth-20260930/sixth-batch-release.trx)。本批源码空白格式验证和 `git diff --check` 通过。前五批摘要哈希保留各自历史状态，第六批结束时使用该摘要核对源码；当前第二十六批修改见上方记录。

四项赛德尔操作数对照已有 `123456.ZMX`、OpticStudio 2026 R1、440 nm 捕获的每个物理面及累计波前系数，绝对误差≤1e−6 波长；既有报告四张系数表及 CARD、OPD、YYbar、轴向/倍率色差等固定夹具回归继续通过。这些是列明文件/设置的数值比较，尚无新项原生 MFE 槽位捕获。验证摘要对所用 ZMX 和赛德尔捕获文本与已提交 HEAD 做逐字节核对，不代表全基线完整性审计；Debug/Release 测试夹具与该文本一致。没有修改捕获、创建外部数据或新增 Optiland 比较。本批未执行全产品/实验室全量、完整外部比较矩阵、原生桌面/截图走查或安装包认证；历史失败不据此关闭。

### 顺序操作数第五批复验（2026-09-30）

第五批阶段记录：新增 LONA、AXCL、LACL、SPCH 共 4 项；当时 **188/383 可执行，195 项仅兼容保留**，五批累计新增 64 项。默认 Debug/Release Core、Application、App 及测试输出成功编译，日志无编译警告或错误；相同过滤器各 **644/644**，零失败、零跳过。其中 `ChromaticFocusOperandTests` 新增 **70 项**，前四批 570 项及既有轴向/倍率色差分析 4 项均已包含，不另加。

```sh
operand_filter='FullyQualifiedName~ChromaticFocusOperandTests|FullyQualifiedName~AxialAberrationAnalysisTests|FullyQualifiedName~LateralColorAnalysisTests|FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=fifth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-fifth-20260930
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=fifth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-fifth-20260930
```

覆盖解析 Snell/近轴焦点、有限物距与零厚度语义、波长/瞳带、像面平移、横向色差符号、失追迹、非法参数、取消、导入/原生保存/应用编辑、缓存修订和正式 LONA/DLS。能力限制及槽位见 [第五批支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第五批焦移与色差部分完成)。

证据：[验证摘要及源码哈希](../artifacts/validation/sequential-operands-fifth-20260930/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-fifth-20260930/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-fifth-20260930/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-fifth-20260930/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-fifth-20260930/fifth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-fifth-20260930/fifth-batch-release.trx)。本批源码空白格式验证和 `git diff --check` 通过。前四批摘要哈希保留各自历史状态，第五批结束时使用该摘要核对源码；当前第二十六批修改见上方记录。

LONA 在所选基线的 420/440/460 nm 三条曲线上逐个 Py 重算，每条 101 点，峰值归一化 RMSE≤1%；既有 CARD、OPD、YYbar、倍率色差等固定夹具回归继续通过。这些是列明文件/设置的数值比较，不是全部新操作数的原生 MFE 数值认证。新复制的轴向像差夹具与已提交捕获内容一致；没有改写捕获、创建外部数据或新增 Optiland 比较。本批未执行全产品/实验室全量、完整外部比较矩阵、原生桌面/截图走查或安装包认证；历史失败不据此关闭。

### 顺序操作数第四批复验（2026-09-29）

第四批阶段记录：SCUR、SDRV、TRAI、BSER 共 4 项，当时可执行数 **184/383**，199 项仅兼容保留；四批累计新增 60 项。默认 Debug/Release Core、Application、App 及测试输出成功编译，日志无编译警告或错误；以下过滤器各 **570/570**，零失败、零跳过，其中新增 `SurfaceDifferentialOperandTests` **64 项**。前三批及相邻 499 项和本轮纳入的既有几何交点 7 项均在总数内，不另加。

```sh
operand_filter='FullyQualifiedName~SurfaceDifferentialOperandTests|FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~GeometryIntersectionResultTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=fourth-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-fourth-20260929
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=fourth-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-fourth-20260929
```

覆盖球面/非球面/XY 多项式解析导数、法截曲率与内部极值、波长和局部坐标、偏心薄透镜视轴、ZMX/原生工程及编辑往返、禁用兼容行迁移、失败/取消、缓存复用与失效及正式 SCUR/DLS。新四项没有新增 OpticStudio 槽位或数值捕获，定义级实现与已验证边界见 [支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-29-第四批面形导数与光线约束部分完成)。

证据：[验证摘要和源码哈希](../artifacts/validation/sequential-operands-fourth-20260929/verification.json)、[383 项审计](../artifacts/validation/sequential-operands-fourth-20260929/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-fourth-20260929/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-fourth-20260929/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-fourth-20260929/fourth-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-fourth-20260929/fourth-batch-release.trx)。本批源文件空白格式验证和 `git diff --check` 通过；前三批摘要的哈希保留各自历史状态，第四批结束时使用该摘要核对源码；当前第二十六批修改见上方记录。

已有固定 Zemax 基线完整性、过滤器内 Workbench 重算与比较继续通过，含 CARD 12 个捕获输出，但这些只证明各自固定文件和设置，不验证新四项完整等价。捕获资产和冻结历史资产未修改，没有新增 Optiland 对照。本批未运行正式全量、实验室全量、完整外部比较矩阵、原生桌面/截图走查或安装包认证；既有全量失败不据此关闭。

### 顺序操作数第三批复验（2026-09-29）

第三批阶段记录：当时新增 8 项、累计 56 项；当时 **180 可执行 / 203 兼容保留 / 383 总计**。实现范围与拒绝条件见 [支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-29-第三批入射角与基点约束部分完成)。

默认 Debug/Release Core、Application、App 和测试输出已更新，编译日志无警告或错误。以下同一过滤器各 **499/499**，零失败、零跳过；第三批 `IncidenceCardinalOperandTests` **92 项**已包含其中。覆盖八项 ZMX/实际 STAROPT 往返及迁移、应用层 CARD 参数编辑、实时面形参数、AGF 成本、体积、入射角及获胜编号、解析基点、缓存命中与修订失效、非法输入、取消和正式 CARD/DLS 优化。前两批 197 项及本轮纳入的既有材料目录/面型映射 16 项均包含在 499 中，不另加。

```sh
operand_filter='FullyQualifiedName~IncidenceCardinalOperandTests|FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests|FullyQualifiedName~GlassCatalogTests|FullyQualifiedName~SurfaceComponentMappingTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=third-batch-debug.trx' \
  --results-directory artifacts/validation/sequential-operands-third-20260929
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
  -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
  --filter "$operand_filter" --logger 'trx;LogFileName=third-batch-release.trx' \
  --results-directory artifacts/validation/sequential-operands-third-20260929
```

证据：[验证摘要及源码哈希](../artifacts/validation/sequential-operands-third-20260929/verification.json)、[383 项状态](../artifacts/validation/sequential-operands-third-20260929/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-third-20260929/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-third-20260929/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-third-20260929/third-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-third-20260929/third-batch-release.trx)。修改文件空白格式验证及 `git diff --check` 通过。前两批机器摘要中的源码哈希保留历史状态，第三批结束时使用该摘要核对源码；当前第二十六批修改见上方最新记录。

分开解释结果：已有 Zemax 固定夹具完整性及本过滤器内 Workbench 重算/数值比较通过；新 CARD 用例读取原提交 `123456-zemax-2026-r1-baseline/analyses/079-cardinalpoints/data.txt`，在 **440 nm、1–22 面、YZ** 下比较 12 个输出，绝对误差不超过 **1e-6 镜头单位**，匹配捕获文本六位小数精度。这是已有捕获的数值对照，不是新增实机采集或 MFE 槽位捕获；没有改写基线/冻结历史资产、添加 Optiland 对照或执行截图审查。未运行正式全量、实验室全量、完整外部比较矩阵、原生桌面或安装包验收；其他已知失败不据此关闭。

### 顺序操作数第二批复验（2026-09-28）

第二批阶段记录：当时新增 20 项，两批累计新增 48 项；当时 **172 可执行 / 211 兼容保留 / 383 总计**。范围与拒绝条件见 [支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-28-第二批近轴光线与制造约束部分完成)。

默认 Debug/Release Core、Application、App、测试项目成功编译，未出现编译警告或错误。同一过滤器各 **391/391**，零失败、零跳过，其中本批 `ParaxialManufacturingOperandTests` **104 项**，第一批测试 93 项，均为 391 项的子集。覆盖 20 项导入与真实 STAROPT 容器往返、20 项非法表面拒绝，以及近轴光线解析值、有限/无限物距、坐标旋转、色散、法线、材料折射率筛选、单双边径厚约束、毛坯四轴与内部极值、厚透镜空气焦距、取消与正式 EFLA/DLS 优化。首次专用测试 102/103，唯一失败为测试夹具误设第二个主波长；修正夹具后通过，复核增加一项非单位折射率筛选测试。

```sh
operand_filter='FullyQualifiedName~ParaxialManufacturingOperandTests|FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests'
for operand_configuration in Debug Release; do
  dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
    -c "$operand_configuration" --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
    --filter "$operand_filter"
done
```

证据：[验证摘要与源码哈希](../artifacts/validation/sequential-operands-second-20260928/verification.json)、[383 项状态](../artifacts/validation/sequential-operands-second-20260928/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-second-20260928/debug.log)、[Release 日志](../artifacts/validation/sequential-operands-second-20260928/release.log)、[Debug TRX](../artifacts/validation/sequential-operands-second-20260928/second-batch-debug.trx)、[Release TRX](../artifacts/validation/sequential-operands-second-20260928/second-batch-release.trx)。修改文件空白格式验证及 `git diff --check` 通过。

已有固定 Zemax 文件的导入完整性、当前 Workbench 重算及过滤器内数值比较通过，各自仅覆盖捕获文件和设置；新增 20 项没有新的实机槽位/数值捕获。未修改冻结历史资产，未添加 Optiland 对照。未运行正式全量、完整外部比较、实验室全量、原生 UI 走查或安装包认证，其他已知失败不据此关闭。

### 顺序操作数扩展复验（2026-09-28）

第一批阶段记录：当时新增 28 项，注册表 383 项中 152 可执行、231 兼容保留；当前总数见第二十二批。完整代码与能力边界见 [支持清单](ZEMAX_OPERAND_SUPPORT.md#2026-09-28-常用顺序优化扩展部分完成)。

默认 Debug/Release 的 Core、Application、App 与测试项目编译成功，日志无编译警告或错误；以下同一过滤器各 **287/287** 通过，零失败、零跳过。新增 `SequentialOperandExpansionTests` **93 项**已包含其中：28 项 ZMX/实际 STAROPT 容器往返、28 项非法引用/共轭拒绝，以及 Snell 折射、反射/全反射、局部坐标、球面与双锥、法线求交、含相位光程、玻璃目录上下限、近轴望远镜/焦面、选定波长、主波长、缓存修订、取消与真实 DLS 优化等数值检查。

```sh
operand_filter='FullyQualifiedName~SequentialOperandExpansionTests|FullyQualifiedName~ZemaxImportTests|FullyQualifiedName~OperandHelpTests|FullyQualifiedName~MeritOperandRowPaletteTests|FullyQualifiedName~MeritFunctionRmsSpotTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~HighPerformanceTracingTests|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~TracingEdgeCaseTests|FullyQualifiedName~ZemaxYYbarParityTests|FullyQualifiedName~ZemaxOpticalPathDifferenceParityTests|FullyQualifiedName~ZemaxLateralColorParityTests|FullyQualifiedName~OpticalNumericalConsistencyTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~CookeTripletGoldenTests'
for operand_configuration in Debug Release; do
  dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj \
    -c "$operand_configuration" --no-restore -m:1 -nr:false -p:UseSharedCompilation=false \
    --filter "$operand_filter"
done
```

证据：[验证摘要与源码哈希](../artifacts/validation/sequential-operands-20260928/verification.json)、[全目录审计](../artifacts/validation/sequential-operands-20260928/operand-audit.csv)、[Debug 日志](../artifacts/validation/sequential-operands-20260928/regression-debug-final.log)、[Release 日志](../artifacts/validation/sequential-operands-20260928/regression-release-final.log)、[Debug TRX](../artifacts/validation/sequential-operands-20260928/regression-debug-final.trx)、[Release TRX](../artifacts/validation/sequential-operands-20260928/regression-release-final.trx)。修改文件 C# 空白格式检查及 `git diff --check` 通过。

分开解释验证结论：已有 MS-L7 源哈希/操作数槽位和固定数值回归，以及 123456 的 Y-Ybar、OPD、垂轴色差等夹具比较通过；这是对应捕获文件与设置范围的 Workbench 重算。新增 28 项使用独立解析例与工程回归，尚无新增 OpticStudio 实机捕获。冻结历史相位数据不变，既有读取处仅做 μm→mm 单位转换。未运行正式全量、完整外部比较矩阵、初始结构实验室全量或安装包验收；已有其他测试失败不据此关闭。本轮没有 UI 截图验证。

### 光学装调实验室复验（2026-09-28）

```sh
dotnet build tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~OpticalAssembly|FullyQualifiedName~BatchedTraceParityTests|FullyQualifiedName~SequentialTraceMeasurementTests|FullyQualifiedName~WorkspaceDockModelTests|FullyQualifiedName~LayeringArchitectureTests' --logger 'trx;LogFileName=assembly-regression-debug.trx' --results-directory artifacts/validation/optical-assembly-20260928
```

两条命令增加 `-c Release`，TRX 名称改为 `assembly-regression-release.trx` 复验 Release 默认目录。没有以替代输出目录代替可运行桌面二进制。Debug/Release 均 71/72，唯一失败是任务之前 HEAD 已有的 `AppUiCardsUseSharedChromeTokens`（两个主题文件的字面圆角 6），装调的 14 项均通过。

截图使用独立测试进程，以免其他 Headless 测试的绘图后端影响实际 Skia 渲染：过滤器设为 `FullyQualifiedName~OpticalAssemblyWindowTests`，增加 `--environment OPTICAL_ASSEMBLY_CAPTURE_DIR=<仓库绝对路径>/artifacts/validation/optical-assembly-20260928/rendered`，TRX 名称为 `assembly-skia.trx`，结果 2/2。这是上述用例的子集，不与 72 相加。截图、实现范围及未验证边界见 [装调指南](OPTICAL_ASSEMBLY_LAB.md#验证)；[机器摘要](../artifacts/validation/optical-assembly-20260928/verification.json)记录本轮源码与默认输出哈希。

### 常规构建命令

解决方案新增独立 `OptilandWorkbench.ZemaxComparison` 和其离线测试项目；普通构建/CI 不启动 Zemax，也不需要其程序集。真实捕获时才使用安装目录的 .NET Framework 编译器和 ZOS-API DLL 构建隔离工作进程。发布 App 不包含验证工具。命令及锁定还原说明见 [工具 README](../tools/OptilandWorkbench.ZemaxComparison/README.md)。

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --locked-mode
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
```

仓库通过 `Directory.Build.targets` 关闭 Avalonia BuildServices 的构建统计目标；这不是编译能力的一部分，关闭后受限本地环境不需要写入用户 AppData/Home 目录即可构建 App 和测试程序集。

有意修改依赖时再更新锁文件：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --force-evaluate
```

## 测试

```bash
dotnet test OptilandWorkbench.slnx --no-build --no-restore /m:1 /nr:false
```

VSTest 会打开本地套接字；受限沙箱可能需要额外权限。普通修改优先运行相关定向子集，只有跨模块、高风险或发布验证才要求全量测试。

普通 CI 的 Linux、macOS、Windows 主测试作业均单独运行 `OptilandWorkbench.ZemaxComparison.Tests`；这些测试只使用固定原始夹具和普通子进程，不探测或启动 OpticStudio。其 hang 诊断阈值为 `3m`。许可证集成验证只在明确运行比较工具的 Zemax 机器上执行。

CI 中正式产品与 Initial Structure Lab 测试均启用 hang 诊断：测试进程长时间无响应时会产生日志/转储线索，而不是无限等待或让后续结果失真。主测试 hang 阈值为 `12m`，Initial Structure Lab 为 `8m`；这个阈值覆盖已知 4 分钟级长测试在较慢 CI 机器上的波动，不把接近完成的慢测试误判成挂死。

## 制图材料数值与公差复验（2026-09-28）

默认 Debug/Release App、Application 与测试项目编译成功，日志无编译警告或错误；相同过滤器各 **55/55** 通过，零失败、零跳过。新增 8 个用例：2 个材料解析用例，以及三套模板 × 单片/胶合的 6 个制图用例。覆盖带厂商前缀的名称、当前工程目录顺序、未知材料、实际面板生成图纸对象、修改公差后名义值不变、预览变化及 PDF 导出；旧版 GB 胶合行还验证逐片材料和缺失材料不借用首片。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MaterialAnalysisTests|FullyQualifiedName~ManufacturingDrawingTests|FullyQualifiedName~PanelContentLayoutTests|FullyQualifiedName~AccessibilityAndResponsiveLayoutTests'
```

配置改为 `Release` 复验另一配置。设置 `OPTILAND_DRAWING_MATERIAL_CAPTURE_DIR` 可保存 6 种组合的默认/修改公差 PNG、PDF 和材料 JSON，本轮已在 Debug 同一次测试中生成，无额外累计测试数。12 份 PDF 的每片材料 nd/Vd 及两组公差均通过文本核对，Poppler 144 dpi 渲染后检查材料区和代表整页；`SCHOTT:F2` 显示 `1.620040 ±0.000500`、`36.366 ±0.5`，编辑后对应为 `±0.001200`、`±1.25`。这只是当前材料服务与制图的一致性检查，不是 Zemax 外部数值比较或标准认证。修改 C# 文件 whitespace 与 `git diff --check` 通过。

证据：[Debug 日志](../artifacts/validation/drawing-material-values-20260928/build-test-debug.log)、[Release 日志](../artifacts/validation/drawing-material-values-20260928/build-test-release.log)、[Debug TRX](../artifacts/validation/drawing-material-values-20260928/material-drawing-debug.trx)、[Release TRX](../artifacts/validation/drawing-material-values-20260928/material-drawing-release.trx)、[PDF 字段核对](../artifacts/validation/drawing-material-values-20260928/pdf-field-checks.json)、[ISO 实际预览](../artifacts/validation/drawing-material-values-20260928/rendered/Iso10110-F2-default.png)、[旧版 GB 胶合 PDF 渲染](../artifacts/validation/drawing-material-values-20260928/rendered/GbT13323_1991-cemented-edited-pdf.png)、[机器记录](../artifacts/validation/drawing-material-values-20260928/verification.json)。未执行正式全量、原生桌面走查、实验室、Zemax 基线完整性或外部数值比较；此前失败范围不因此关闭。

## 标准面与平面统一复验（2026-09-28）

表面选择器、镜头表及属性摘要统一显示“标准面”；无穷半径的面保持平面，旧几何及文件存储继续兼容。默认 Debug/Release App、Application 和测试项目编译成功，日志无编译警告或错误。两种配置的主过滤器各 **27/27**，相邻过滤器各 **11/11**，零失败、零跳过；两过滤器不重叠。新增 4 个用例验证有限/零编码/正无穷半径的标准面转换及真实键盘编辑后直接应用、撤销/重做、物面/像面、特殊面型切换与 STAROPT 保存重载。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~SurfacePropertiesPanelTests|FullyQualifiedName~SurfacePropertiesServiceTests|FullyQualifiedName~SurfaceEditorRowTests'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~SurfaceComponentMappingTests|FullyQualifiedName~SurfaceSolveTests|FullyQualifiedName~AnalysisGuiContractTests.ConnectorCreates'
```

配置一起改为 `Release` 复验另一配置。设置 `OPTILAND_SURFACE_PROPERTIES_CAPTURE_DIR` 后在单独测试进程运行 `StandardSurfaceIncludesPlanesAndKeepsRadiusThroughApplyUndoAndFileReload`，实际 Skia 渲染 **1/1** 通过；它是主过滤器的重复子集，不累计。修改 C# 文件 whitespace 与 `git diff --check` 通过。未执行正式全量、原生桌面走查、实验室、Zemax 基线完整性或外部数值比较；Core 追迹实现未修改。

证据：[Debug 编译/测试日志](../artifacts/validation/standard-surface-plane-20260928/build-test-debug.log)、[Release 日志](../artifacts/validation/standard-surface-plane-20260928/build-test-release.log)、[Debug 主 TRX](../artifacts/validation/standard-surface-plane-20260928/surface-debug.trx)、[Release 主 TRX](../artifacts/validation/standard-surface-plane-20260928/surface-release.trx)、[Debug 相邻 TRX](../artifacts/validation/standard-surface-plane-20260928/components-debug.trx)、[Release 相邻 TRX](../artifacts/validation/standard-surface-plane-20260928/components-release.trx)、[Skia TRX](../artifacts/validation/standard-surface-plane-20260928/surface-skia.trx)、[实际控件渲染](../artifacts/validation/standard-surface-plane-20260928/rendered/standard-surface-infinite-radius.png)、[机器记录](../artifacts/validation/standard-surface-plane-20260928/verification.json)。

## 色散系数双列复验（2026-09-28）

仅调整材料库的系数字段排列与标签宽度；默认 Debug/Release App 及测试项目编译成功，日志无编译警告或错误。已有 `AccessibilityAndResponsiveLayoutTests` 两种配置各 **13/13** 通过、零失败、零跳过，未新增测试用例。720、1200、1440、2000 DIP 宽度下实际 Avalonia/Skia 控件渲染已检查，十个系数保持双列五行、字段高度 34 DIP；N-BK7 样本数值可见。局部预览使用与卡片一致的白底。不是原生桌面截图、全量回归或光学精度验证。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~AccessibilityAndResponsiveLayoutTests'
```

将配置改为 `Release` 复验另一配置。证据：[Debug 编译/测试日志](../artifacts/validation/material-coefficients-two-columns-20260928/build-test-debug.log)、[Release 日志](../artifacts/validation/material-coefficients-two-columns-20260928/build-test-release.log)、[Debug TRX](../artifacts/validation/material-coefficients-two-columns-20260928/layout-debug.trx)、[Release TRX](../artifacts/validation/material-coefficients-two-columns-20260928/layout-release.trx)、[渲染尺寸](../artifacts/validation/material-coefficients-two-columns-20260928/render-measurements.json)、[双列预览](../artifacts/validation/material-coefficients-two-columns-20260928/dispersion-two-columns.png)。本轮 `git diff --check` 通过，未重跑正式解决方案全量或新增外部比较。

## 材料库 Dock 合并复验（2026-09-28）

重复“玻璃”Dock 已删除，旧类型只作读取兼容；产品行为见 [材料库工作流](GUI_QUICKSTART_REFACTOR.md#材料库)。本轮正式解决方案默认 Debug/Release 构建各 **0 警告、0 错误**，同一组回归各 **59/59** 通过、零失败、零跳过。5 个新增用例覆盖旧页单独停靠/浮动、双页停靠、隐藏重复页、浮动重复页以及迁移后保存再载入。其余覆盖 Dock 模型、会话持久化、材料分析与面板响应式/可访问性；未执行正式全量、原生桌面走查、截图渲染、实验室或外部数值比较。

```sh
dotnet build OptilandWorkbench.slnx -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~WorkspaceDockModelTests|FullyQualifiedName~WorkspaceSessionTests|FullyQualifiedName~MaterialAnalysisTests|FullyQualifiedName~AccessibilityAndResponsiveLayoutTests'
```

将两条命令配置一起改为 `Release` 即复验 Release。修改文件的 C# whitespace 检查及 `git diff --check` 通过。构建与测试产物：[Debug 构建](../artifacts/validation/material-library-dock-20260928/build-debug.log)、[Release 构建](../artifacts/validation/material-library-dock-20260928/build-release.log)、[Debug TRX](../artifacts/validation/material-library-dock-20260928/material-dock-debug.trx)、[Release TRX](../artifacts/validation/material-library-dock-20260928/material-dock-release.trx)、[机器记录](../artifacts/validation/material-library-dock-20260928/verification.json)。

## 桌面 UI 审查修复复验（2026-09-28）

此前 UI 审查修复阶段默认 Debug/Release 解决方案构建各零警告零错误，同一 UI 与相邻分析过滤器各 **124/124** 通过。锁定修订、报告默认页、数字校验与滚动复用的验证命令、构建日志和 TRX 见 [修复记录](UI_AUDIT_FIXES_2026-09-27.md)。下面保留主动作/侧栏阶段的独立过滤器和结果，不同范围的数量不能相加。

## 桌面 UI 定向复验（2026-09-27）

对应 [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)，产物目录为仓库默认输出。以下 70 项覆盖按钮、侧栏、主题交互、容器测量、库存匹配与 Dock；不含全量光学数值验证、独立实验室或安装包验收。

```sh
dotnet build OptilandWorkbench.slnx -c Debug --no-restore /m:1 /nr:false -p:UseSharedCompilation=false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~PrimaryActionPresentationTests|FullyQualifiedName~CompactSidebarTests|FullyQualifiedName~SystemPropertiesPanelSectionThemeTests|FullyQualifiedName~BlueThemeInteractionTests|FullyQualifiedName~PanelContentLayoutTests|FullyQualifiedName~CommercialLensCatalogPanelTests|FullyQualifiedName~StockLensMatcher|FullyQualifiedName~WorkspaceDockModelTests'
```

将两条命令的配置一起改成 `Release` 复验 Release。上述两个配置已分别通过 70/70。设置 `OPTILAND_ACTION_CAPTURE_DIR` 后，在独立测试进程中仅运行 `PrimaryActionPresentationTests|CompactSidebarTests` 可重现 15/15 Skia 截图验证；不要混用假绘制和 Skia 字体缓存。历史 171、14、18、87 项及其他模块的验证记录按原范围保留，不与本集合相加。

## 换行符

仓库文本默认使用 LF；Windows 批处理脚本保留 CRLF。`.editorconfig` 和 `.gitattributes` 必须保持一致。Windows 开发者建议使用仓库属性控制换行，而不是依赖全局 `core.autocrlf=true`。

## 性能基准

```bash
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj -- --non-sequential 1000000
```

第一条基准覆盖 10,000 和 100,000 条顺序光线、20 个表面、不同历史保留模式、几何 MTF 和 Monte Carlo。其 `PsfMtfSampling` 输出名称实际对应 `GeometricMtfAnalysis`，未覆盖 FFT/Huygens PSF。第二条是独立非序列百万光线 STARRDB 流式写入基准，记录吞吐、托管堆、峰值工作集和数据库大小。输出均为 CSV；性能结果用于同机同运行时比较，不是普通 CI 硬阈值。平台返回 0 的峰值工作集应视为不可用，不能解释为零占用。

## 非序列教学样例

```bash
dotnet run --project tools/OptilandWorkbench.NonSequentialSamples/OptilandWorkbench.NonSequentialSamples.csproj -- samples/non-sequential
```

生成器使用固定对象GUID和随机种子，逐个追迹验证场景、能量平衡及建议路径筛选，再分别原子写入12个STAROPT工程、6张光源效果SVG和`index.json`。样例清单、课堂步骤与预期结果见[`samples/non-sequential/README.md`](../samples/non-sequential/README.md)。

## 镀膜实验室独立构建与验收

镀膜实验室位于 `labs/CoatingDesign`，不纳入正式解决方案或现有安装包。独立脚本 `Run-CoatingDesign.command` / `.cmd` 构建并运行 Release；主程序入口要求实验室与主程序 Debug/Release 配置一致，或通过 `OPTILAND_COATING_DESIGN_LAB_PATH` 指定已部署绝对路径。

```sh
dotnet build labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release
dotnet test labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release --no-build
```

2026-09-27 镀膜实验室完整 Debug/Release 各 **29/29**，正式相关 Release **198/198** 通过（涵盖入口、材料、共享数值、优化与界面契约；不是正式全量）。上游 120 个固定案例属于其中一项测试，不另加到通过总数。三个示例均完成真实计算、优化、导出、保存重开一致性；高反和窄带仍有未达标项。[完整证据、命令和边界](../validation/coating/README.md)。这不覆盖或更新上面的初始结构实验室 260 项以及历史全量发布门禁。

## 启动桌面应用

独立顶层“实验室 → AI 初始结构”提供独立初始结构实验室的进程启动入口。实验室仍须通过 `labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx` 单独构建，正式解决方案和安装包不包含其实验程序集。开发目录的入口使用与主程序相同配置的实验室输出；独立部署和路径配置见 [实验室 README](../labs/InitialStructure/README.md#run)。

- Windows：`Run-Optiland.cmd`
- macOS：`Run-Optiland.command`

脚本依次执行 `dotnet restore`、`dotnet clean`、`dotnet build --no-restore` 和 `dotnet run --no-build`。还原放在清理之前，因为 `dotnet clean` 同样会解析现有 NuGet 资产文件；全局包缓存缺失时，这个顺序会先补齐依赖，避免清理阶段触发 `NETSDK1064`。清理只涉及项目构建输出，不删除 `%APPDATA%/OptilandWorkbench` 或 macOS 对应用户目录中的工程、主题和会话数据。

终端等价命令：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## 发布目标

### Windows 一键生成带安装向导的 EXE

在仓库根目录双击 `Build-Installer.cmd`（原入口 `Build-Exe.cmd` 等效）。默认生成 Windows x64 Release **Setup 安装程序**，不再只是便携目录。需要安装 `global.json` 指定的 .NET SDK（当前为 `10.0.300`，允许同功能带更新补丁）；首次打包需联网还原 NuGet、Windows 运行时和准备安装包编译器。

```text
artifacts/installers/OpticalSystemDesign-win-x64-<时间戳>-<唯一编号>/
  OpticalSystemDesign-1.0.0-win-x64-Setup.exe
  OpticalSystemDesign-1.0.0-win-x64-Setup.exe.sha256
  payload/app-<唯一编号>/
```

分发时只需提供 `*-Setup.exe`；SHA-256 文件可用于核验，不需要携带 `payload`。安装程序包含 .NET 运行时与全部资源，目标电脑不必另装 .NET；安装时无需联网。安装包使用 Inno Setup 的现代向导，提供简体中文/英文、欢迎页、安装目录、开始菜单目录、可选桌面快捷方式、安装进度和完成页。完成页的启动程序选项默认不勾选。

默认按当前用户安装至 `%LOCALAPPDATA%\Programs\S.T.A.R. Labs\Optical System Design`，不要求管理员权限；在 Windows“设置 > 应用”或开始菜单卸载。安装目录与快捷方式选项在重复安装时保留。升级/卸载前请正常退出应用；安装程序不会自动关闭正在运行且可能有未保存内容的窗口。

安装包与厂商镜头 JSON 均不压缩，厂商资源仍分别保存于 `LensLibrary/StockCatalogs/<厂商名>.json`。卸载使用已安装文件日志，不递归清空安装目录，也不删除用户设置、会话或另外创建的工程文件。**随安装包分发的文件归安装程序管理**，修改过的内置示例/目录仍可能在重复安装时被覆盖、卸载时被移除；编辑后应另存到用户工程目录。

`scripts/build-installer.ps1` 先调用 `scripts/publish-windows.ps1` 发布，再编译 `packaging/windows/OpticalSystemDesign.iss`。优先使用指定路径或本机已安装的 Inno Setup；否则从官方 GitHub 发布下载固定 `6.7.3`，验证固定 SHA-256 后，以便携模式准备到忽略版本控制的 `artifacts/tools/inno-setup-6.7.3`，不注册系统安装、快捷方式或文件关联。语言文件、授权和来源见 `packaging/windows/inno/`；商用前应查阅上游授权与商业许可政策。

可选命令（相对输出路径按仓库根目录解析）：

```powershell
# 自定义输出目录，支持路径包含空格；建议保持路径较短
.\Build-Installer.cmd -OutputRoot "D:\Releases\Optical System Design"
# 使用已准备好的编译器（Inno Setup 6.3+），不触发编译器下载
.\Build-Installer.cmd -InnoSetupCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
# Windows ARM64（脚本支持，尚未在 ARM64 机器上验证安装/运行）
.\Build-Installer.cmd -Runtime win-arm64
# 只预览，不下载工具、不执行还原、不创建输出目录
.\Build-Installer.cmd -WhatIf
# 仍需便携版时，直接使用原发布脚本
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
```

自动化可直接调用 PowerShell 脚本，或者为 CMD 入口设置 `OPTILAND_PACKAGE_NO_PAUSE=1`。脚本先验证已提交的平台无关锁文件，再把 RID 专用锁文件写入各项目 `obj` 目录，不覆盖源代码中的 `packages.lock.json`。发布关闭裁剪、AOT 和单文件合并，保留 Avalonia/Skia 原生依赖、外部资源及授权说明。每次使用新目录；失败输出保留 `.partial` 后缀，不删除旧发布包和用户数据。安装包内部暂存目录使用短名称以避免 Inno Setup 6 的源文件路径长度限制。成功前检查 EXE、运行时、品牌/授权资源和厂商目录，编译后生成安装包 SHA-256。

便携版仍输出到 `artifacts/windows/OpticalSystemDesign-win-x64-<时间戳>-<唯一编号>/`，运行其中的 `OptilandWorkbench.App.exe`，分发时必须复制**整个目录**。Windows 打包专用 `WindowsPackage=true` 使 App 使用 `WinExe` 子系统，启动时不附带控制台；日常构建和其他平台不受影响。安装程序与应用目前均未签名，Windows 可能提示未知发布者；不要绕过组织安全策略。

2026-09-04 安装包验证：实际生成 `win-x64` Setup；使用独立测试 AppId 在项目临时目录完成安装、同版本重复安装和卸载，退出码均为 `0`。安装后的 **1178 个载荷文件**逐一哈希一致，授权文件存在，卸载后测试安装注册与应用 EXE 已移除，另行创建的工程文件保留。测试没有启动产品、覆盖正式安装或创建用户快捷方式；这是静默安装链路验证，不等同于安装向导逐页操作或目标电脑上的完整 GUI 验收。`WindowsPackagingTests` **7/7** 定向通过（便携发布契约、中文安装/安全卸载契约、编译器来源/哈希校验、含空格路径预览、非法 RID、缺失指定编译器），CMD 入口 `-WhatIf` 通过；未执行全量测试，不替代下文历史全量基线。

最终重建的 Setup 约 **260.4 MiB**，SHA-256 核对通过；与上述安装烟测载荷相比，仅补充了 `README.txt` 中对随包文件的卸载/覆盖说明，全部二进制及镜头资源哈希保持一致。隔离测试安装已正常卸载，临时安装目录内仅保留测试自建工程。

同日早先便携版验证：约 258 MiB，PE 头确认 x64 / Windows GUI，`runtimeconfig.json` 声明随包携带 .NET 10.0.8，931 个镜头库文件逐一哈希核对与源资源一致。这是早先的便携输出验证记录，不是当前默认入口的产物类型。

### 跨平台发布

一次发布主要平台：

```bash
bash scripts/publish-cross-platform.sh
```

自包含发布：

```bash
SELF_CONTAINED=true bash scripts/publish-cross-platform.sh
```

手工命令示例：

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-arm64 --self-contained false
```

输出位于：

```text
src/OptilandWorkbench.App/bin/Release/net10.0/<runtime>/publish
```

macOS 脚本还会生成 `Optical System Design.app`，声明 `.staropt` 文档类型并转发 Finder 打开的工程路径。

## 2026-09-06 历史验证基线

当前构建和测试基线见本页顶部；以下保留当时数值验证的范围与限制。

2026-09-06 Huygens MTF 后处理修复后的正式解决方案 Release 默认输出构建 `0` 警告、`0` 错误，完整主测试 `1233/1233`、工具测试 `104/104`，合计 `1337` 项通过，零失败、零跳过；锁定依赖从本地缓存还原，格式检查和 git diff --check 通过。MS-L7 重新执行全部 72 项，结果为 44 Pass、6 Close、2 Difference、17 Incomparable、3 Skipped，0 执行错误；主基准独立复验三项 Huygens MTF 均 Pass。新增 14 项回归包含原生 PSF 后处理重建，不能代替全链条精度结论。视场 MTF 的一处分量由 Pass 变为 Close，局部退步及未解决误差监控预算已明确记录，正式数值容差不变。未执行在线漏洞审计、独立实验室、旧外部报告工具、GUI 截图或安装包实测。该阶段证据见 `ZEMAX_HUYGENS_REPAIR_2026-09-06.md`，以下更早的移除审计仅作历史记录。详见 [移除审计与验证](PYTHON_OPTILAND_REMOVAL.md)。

## 历史验证记录

以下保留各日期当时的定向和全量结果，不作为当前数量：

- 2026-08-28变更前历史基线为正式产品严格构建 `0` 警告、`0` 错误、全量回归 `837/837`；该数量已经由下文 2026-09-03 的 `1015/1015` 完整基线取代，不作为当前结果；
- 2026-08-29可靠性加固前，相关桌面可访问性、分析 GUI、镜头编辑器和 Dock 契约子集记录为 `95/95`；本轮修改后未把该旧数量沿用为当前结果；
- 2026-08-29可靠性加固前，非序列定向记录为 `80/80`、独立智能初始结构实验室定向记录为 `9/9`；本轮新增数据库、资源预算和路径表达式回归后，这两个数字只作为历史记录，不代表当前测试总数；
- 最近一次架构、可访问性和响应式布局定向结果为 `14/14`；
- 2 项新增 Avalonia 首帧/主题回归通过相关 16 项定向子集；
- 4 项新增 Dock 空宿主、会话和锁定回归通过 `12/12` 窗口布局子集；
- 配对 fan 布局契约覆盖光线像差图和瞳孔像差图：同一视场的 `P_y/P_x` 位于同一卡片，卡片按视场数平衡排列，结果区不再暴露手动方形开关；
- 方形单图契约覆盖全视场点列图、光迹图和干涉图：X/Y 数据等比例，外层视口默认保持正方形；色焦移保留普通曲线图的自适应布局；
- 平铺/层叠修复复用了现有测试，验证浮动页自动回收到主文档区并进入内部 MDI；合并命令验证回收后恢复标签模式；
- 报告菜单、72 项 Core 分析目录、顺序 70 项/非序列 2 项模式隔离、三类真实报告输出以及独立“畸变”入口退场由对应定向契约测试覆盖；
- 最近一次相关目录子集为 `3/3`，同时覆盖旧 `Distortion`/“畸变”名称迁移、组合页畸变曲线、干涉图方形视口和色焦移非方形契约；
- Zemax 评价函数导入子集为 `6/6`，覆盖实际 `[MS-L7]` 的 103 行源顺序、`TRAR` 参数映射、只读兼容槽位、快照校验和编辑往返不裁剪；
- 最近 App 项目构建结果为 0 警告、0 错误。
- 2026-08-29可靠性加固后，正式解决方案（含 Core、Application、App、测试程序集和工具项目）与独立实验室解决方案均在默认输出目录构建为 `0` 警告、`0` 错误；此前正式产品组合子集为 `14/14`、实验室不可变发布子集为 `3/3`。本轮最终新增/高风险正式产品筛选为 `13/13`，实验室持久化与规格边界筛选为 `4/4`，限定静态分析规则无残余诊断。只运行相关定向测试，不运行正式全量测试，详细边界见[可靠性与资源边界加固](RELIABILITY_HARDENING_2026-08-29.md)。
- 同轮正式产品分别通过可靠性主子集 `11/11`、非序列光源采样 `3/3`、分析 GUI 参数与轴元数据 `3/3`、限额读取/响应式守护/STAROPT/会话 `5/5`、可访问性与响应式布局 `5/5`；这些是独立筛选结果，不是新的全量基线。
- 实验室资源预算最初单项为 `1/1`，预算与持久化往返组合子集为 `2/2`，唯一运行 ID、不可变目录发布和取消清理子集为 `3/3`，本轮防篡改/大小写冲突/发布/规格边界筛选为 `4/4`；筛选重叠，不累计为全量数量。
- 2026-08-29 对正式解决方案执行 NuGet.org 直接与传递依赖漏洞查询，当前未报告已知易受攻击包；后续发布仍应重新查询。
- 2026-08-29 能力真实性修复后，评价函数/优化器目录、ZMX 只读行、快照校验和应用优化入口定向子集通过 `8/8`；未运行正式全量测试。
- CI 现将正式解决方案与独立 Initial Structure Lab 的构建、三平台测试拆成独立 job：主测试失败不会跳过 Lab 测试，Lab 构建成功也不再被解释为 Lab 测试已运行。两套测试分别输出 TRX 并以独立 artifact 上传；仓库质量任务分别验证两套解决方案格式。固定种子格式模糊回归覆盖 STAROPT、STARMESH、STARRDB 和 ZMX，当前定向结果 `6/6`。
- STEP CAD CI 分为“生成 fixture”和“FreeCAD/OpenCascade 第三方导入验证”两个 job。生成出的 STEP fixture 始终上传；验证 job 固定在 `ubuntu-22.04`，安装固定版本 `FreeCAD 0.19.2+dfsg1-3ubuntu1`，通过 GitHub Actions 缓存复用固定 FreeCAD `.deb` 包，并上传 apt、FreeCAD 版本、`.deb` SHA-256 和 OpenCascade 验证日志。若验证环境安装失败，红灯表示第三方验证环境未建立，不等同于 STEP 输出已被证明错误。完全脱离 apt 镜像变化仍需要后续维护预构建容器或随仓库托管的校验运行时。
- 独立 CI 性能烟测覆盖 10,000 条顺序末面追迹、2,000 条几何 MTF、20 次公差 Monte Carlo 和 10,000 条非序列 STARRDB；本机当前约 `0.8 s`、累计分配约 `705 MiB`，闸门上限为 `2 min`、`2 GiB` 和 `128 MiB` 数据库。该宽松闸门用于捕获数量级退化，不替代正式性能基准。
- CI 在提交、拉取请求、手动运行和每周计划任务中执行直接与传递 NuGet 漏洞查询；在线查询结果具有时效性。
- 2026-08-30最终收口重新构建正式产品和独立实验室，均为`0`警告、`0`错误；两套`dotnet format --verify-no-changes`、启动/发布脚本语法和`git diff --check`通过。评价函数/优化器、当时的333代码注册、公差逆向/元件/非球面、非序列探测器及其类型化物理轴、兼容程序集、Application/App分层、可访问性、响应式布局和主题的高风险组合筛选为`56/56`；受限文件与固定种子格式模糊组合为`14/14`；实验室冻结基准、密集验收、断点续算、STAROPT导出和响应式源码关键路径为`5/5`。这些仍是历史定向结果。
- 同日通过 NuGet.org 重新查询正式解决方案和独立实验室的直接与传递依赖，所有项目均未报告已知易受攻击包；性能烟测在本机约`0.79 s`完成，累计分配约`705.5 MB`，10,000射线STARRDB为`877,339 bytes`，均在CI宽松闸门内。
- 2026-08-31 后续高风险加固覆盖非序列材料快照、文档图验证复杂度、不可变网格资产、STL 流式/可取消导入与预计工作集预算，以及优化框架严格有限数/维度校验。正式产品完整主测试 `935/935` 通过，独立 Initial Structure Lab 测试 `21/21` 通过。
- 2026-08-31 文档切换事务、优化迭代/变量回滚、非序列结果代次/探测器重建和运行时字号刷新修复后，正式解决方案默认输出构建为 `0` 警告、`0` 错误；文档与保存 `22/22`、优化相关 `23/23`、非序列文档/追迹/杂散光 `88/88`、主题运行时 `6/6`、分层架构 `15/15` 定向筛选通过。筛选存在交集，不能相加；按用户要求未运行完整测试，不替代上条 `935/935` 完整基线。
- 2026-09-01 设置保存事务、非序列锁外观察者通知与大页码分页、相位/扩展光源输入边界、自绘只读图辅助功能、窄视口约束和 Initial Structure Lab 算法版本 2 修复后，正式解决方案和独立实验室解决方案默认输出构建均为 `0` 警告、`0` 错误。正式产品相关定向测试 `7/7`、实验室定向测试 `4/4` 通过；按用户要求未运行完整测试，不替代 2026-08-31 的正式产品 `935/935` 和实验室 `21/21` 完整基线。
- 2026-09-01 修复 CI 的 STEP 验证 job 在 job 级环境变量中使用不可用 `runner` 上下文的问题；日志目录改用该阶段允许的 `github.workspace`，不改变 STEP 生成、固定 FreeCAD 版本或 OpenCascade 验证门槛。GitHub 上连续零秒失败的运行均未创建任何 job，属于工作流解析失败，不代表这些提交已执行或未通过代码测试。修复后的工作流通过 YAML 解析、`actionlint 1.7.12` 和 shell 脚本语法校验。
- 同日工作流恢复执行后，Linux 主测试实际运行 `972` 项并发现 2 个非序列教学清单仍使用分裂父分支重复发布时期的旧计数。Fresnel 与全反射光管的确定性分支基准分别更新为 `1200` 和 `1195`，12 个教学工程重新载入/追迹子集 `13/13` 通过；STAROPT 工程内容和能量基准未改变。
- 2026-09-03 Zemax 顺序评价函数继续收敛后，正式主测试 `1006/1006` 通过，解决方案 Release 构建 `0` 错误；构建仅有 NuGet 漏洞数据源 SSL 警告。目录保持 383 个顺序兼容代码、114 个已连接计算引擎的代码；`[MS-L7]` 103 行的源哈希/顺序和 400 余个活动参数槽已锁定，除兼容只读 `DIMX` 外，全部 82 个当前可执行数值行与本机 OpticStudio 2026 R1 golden 对齐。此次补齐 63 个高 NA `TRAR`、`RANG/SINE`、`PMAG/DIVI`、`REAR` 与边厚范围行，并修正 ray aiming、零号面分类型语义、所选波长近轴像面和相邻表面各自半口径边厚；完整 Zemax 等价仍限于已捕获系统和设置。

- 2026-09-03 后续独立审核发现评价函数缓存混用物面/像面、RMS 主光线默认面解析错误及单光线/多光线瞄准设置不一致。本批修复按实际目标面及瞄准设置区分采样缓存，补齐 RMS、Moore–Elliott 和波前相关默认像面路径。新增 9 个回归用例覆盖独立与批量求值、两种行序、默认与显式像面、非连续面号及瞄准开关；正式全量主测试 `1015/1015` 通过，零跳过。锁定还原成功，Release 构建为 `0` 警告、`0` 错误，两个改动源文件的格式检查及 `git diff --check` 通过。上条 `1006/1006` 为前一批历史结果；固定 `[MS-L7]` 的 82 行 golden 容差和数值未调整。本轮不执行后续计划阶段。
- 2026-09-04 新增“帮助 > 操作数帮助”可停靠文档，并从优化服务的当前操作数目录发布定义、参数、支持状态和实际计算说明。搜索、可计算/兼容保留筛选、Dock 类型契约、Headless 宽窄布局与字体令牌守护组合测试 `5/5` 通过，相邻评价函数描述符与工作区会话子集 `7/7` 通过；正式解决方案 Debug 构建为 `0` 警告、`0` 错误。按要求未在本机运行完整测试，不替代 2026-09-03 的 `1015/1015` 本机全量基线；此变更不调整任何操作数数值算法或 Zemax golden。
- 2026-09-04 继续 Zemax 顺序评价函数计划，新增 `DIVB`、`PROB`、`OSUM`、`QSUM` 和 `EQUA` 五个通用数学操作数的定义级执行路径，该批结束时为 383 个顺序兼容代码、119 个已连接计算引擎代码。ZMX 导入、参数描述符、行序求值、错误报告、STAROPT 快照往返、帮助说明和行色归类已同步；定向测试 `ZemaxImportTests|MeritOperandRowPaletteTests` 为 `78/78` 通过，`OperandHelpTests` 为 `4/4` 通过，解决方案 Debug 构建 `0` 警告、`0` 错误，`dotnet format --verify-no-changes` 和 `git diff --check` 通过。当前环境未找到可用 OpticStudio/ZOS-API 运行时，因此本批新增语义尚未形成 Zemax golden 数值闭环，也不替代 2026-09-03 的 `1015/1015` 本机全量基线。
- 2026-09-04 后续继续同一计划，新增 `MNIN`、`MXIN`、`MNAB`、`MXAB` 和 `POWR` 五个常见玻璃/表面功率操作数的定义级执行路径，当时注册表为 383 个顺序兼容代码、124 个已连接计算引擎代码。`MNIN/MXIN` 按 `Surf1..Surf2` 范围约束玻璃 d 线 Nd，`MNAB/MXAB` 约束玻璃 Vd，空气/真空/反射空间不参与；`POWR` 按标准折射面 `(n_after − n_before) / Radius` 计算表面光焦度，平面返回 0，非标准面或反射面报告错误。ZMX 导入、参数描述符、错误路径、STAROPT 快照往返、帮助说明和行色归类已同步；定向测试 `ZemaxImportTests|MeritOperandRowPaletteTests|OperandHelpTests` 为 `84/84` 通过，解决方案 Debug 构建 `0` 警告、`0` 错误，`dotnet format --verify-no-changes` 和 `git diff --check` 通过。当前环境未找到可用 OpticStudio/ZOS-API 运行时，因此本批新增语义尚未形成 Zemax golden 数值闭环，也不替代 2026-09-03 的 `1015/1015` 本机全量基线。

- 2026-09-04 远端同步前复核：ZMX 切趾、实际 266 nm 扩束文件、缺失玻璃保留与底部提示、文档编辑/保存和相邻追迹路径的定向回归 `280/280` 通过；制图模板、架构约束、操作数行色及帮助的补充定向回归 `56/56` 通过。桌面默认输出目录构建 `0` 警告、`0` 错误；扩束布局预览随仓库保存至 `artifacts/validation/beam-expander-266nm-6x-layout.png`。这些结果不是全量测试或新的 Zemax 数值基线，具体修复边界见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

- 2026-09-05 镜头数据移除“添加 / 删除”工具栏，新增右键“下插入、上插入、删除”。默认 App 项目已重建为 `0` 警告、`0` 错误，旧进程不再占用默认输出；插入/删除与相邻结构回归 `21/21`，此前待跑的切趾、数值框、表面属性和文档标签界面回归 `19/19`，组合筛选 `40/40` 通过。独立 Skia 右键子集 `5/5` 再次通过，并复核菜单截图，重复用例不累加。未运行全量测试、打包或新的 Zemax 数值对标；不替代历史完整基线。界面截图测试不得与使用模拟字体的 Headless 测试混用渲染后端，应另起测试进程，避免 `HeadlessPlatformTypeface`/`SkiaTypeface` 混用错误。详细边界见 [镜头数据右键操作](UI_DESIGN_REVIEW.md#2026-09-05-镜头数据右键插入与删除)。

- 2026-09-05 普通主题默认操作按钮改为淡蓝底，悬停/按下依次加深；其他主题及专用按钮样式保留。默认 App 构建 `0` 警告、`0` 错误，状态、交互、动态控件、主题隔离及相邻布局定向检查 `8/8` 通过；独立 Skia 真实鼠标用例 `1/1` 再次通过并核对三种状态截图。未运行全量测试或打包，不修改历史全量基线。详见 [操作按钮状态](UI_DESIGN_REVIEW.md#2026-09-05-普通主题操作按钮蓝色状态)。

- 2026-09-05 半径求解入口改为单元格右侧标记区，支持固定、变量和前序面曲率拾取。默认 App Debug 构建 `0` 警告、`0` 错误；计算/服务、撤销保存、插入、界面和优化组合定向检查 **43/43** 通过，0 跳过。独立 Skia 界面复跑 **3/3** 通过，并复核变量/拾取截图；重复用例不累加。本轮没有运行全量测试、发布或打包，不更新历史全量数量，也不构成新的 Zemax 实机数值对标。详细边界见[曲率半径求解入口](RADIUS_SOLVE_EDITOR.md)。

2026-09-05 审计修复后已补跑上述完整验证；安装包构建和 Windows 安装/卸载实测不包含在本次数值与源码回归中。

- 2026-09-05 表面属性标题简化为“展开/收起 + 表面 N 属性 + 上一面/下一面”圆形按钮横条。默认 App Debug 构建 `0` 警告、`0` 错误；属性布局/导航、半径求解和右键行操作定向组合 **14/14** 通过，0 跳过，独立 Skia 属性面板 **6/6** 重复验证通过并复核紧凑横条截图。没有运行全量测试或打包，不更新历史全量数量；[属性面板文档](SURFACE_PROPERTIES_EDITOR.md) 记录功能与验证边界。

Optiland 0.5.8 历史资料只保留在 validation/history，禁止重新生成或新增对照。锁定还原、构建和产品发布不依赖 Python、pythonnet、tools/python-reference 或历史测试数据。Zemax 捕获/报告与 FreeCAD STEP 检查属于产品之外的验证工具，使用它们不意味着产品运行需要 Python。

- 2026-09-07 修复 Zemax `MEMA` 机械半直径导入：该值不再由相邻 `DIAM` 推算，并贯通 STAROPT 快照、处方显示、布局机械边界、制造数据、CAD 网格和 ZMX 再导出；未提供 `MEMA` 的文件继续随该面净半直径变化。包含单位换算、独立 `DIAM/MEMA`、保存往返及相关既有功能的定向回归 `136/136` 通过，格式检查通过；此结果不是新的全量测试基线。
- 2026-09-07 对齐 Zemax 顺序布局的有效口径语义：布局仍按系统入瞳采样，固定 `DIAM` 的有光焦度表面和光阑面建立圆形截光口径，`APMN` 保持环形遮拦，`MEMA` 仅控制实体外形。新增用例验证入瞳边缘光线在 `DIAM` 渐晕、机械外缘不放大通光束、自动 `DIAM` 不被错误用作额外光阑及平面光阑截光。默认目录相关导入、布局和追迹定向回归 `126/126` 通过，整个解决方案 Debug 构建 `0` 警告、`0` 错误。本次不更新历史全量测试基线。
- 2026-09-07 修复导入固定 `DIAM` 后标准点列图的艾里斑计算：几何点列仍按有效口径排除渐晕光线；用于艾里斑的 Working F/# 探测按 Zemax 规则忽略表面口径，并在边缘探测光线失败时缩小瞳孔采样后外推。实际 `zemax-123456.ZMX` 与独立 `DIAM/MEMA` 回归均已覆盖；默认目录相关点列、艾里斑、PSF、无焦和 Zemax 导入测试 `139/139` 通过，整个解决方案 Debug 构建 `0` 警告、`0` 错误，格式检查与 `git diff --check` 通过。本次不更新历史全量测试基线。
- 2026-09-07 镜头数据的曲率、厚度和净口径统一为 Zemax 风格的单元格求解入口：固定曲率/厚度为空白、变量 `V`、拾取 `P`；自动净口径为空白、用户固定 `U`、拾取 `P`。移除独立“T 变量”、净口径“固定”列及表面属性页的重复开关，右键数值单元格打开对应设置。厚度与净口径拾取已连接计算、撤销、保存、表面重编号、多配置和优化链路，ZMX `DIAM` 的 `0/1/2` 可导入为自动/用户固定/拾取。默认目录统一求解及相邻高风险回归 `207/207` 通过，整个解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误；格式和差异检查通过。本次不更新历史全量测试基线，详见[镜头数据求解入口](RADIUS_SOLVE_EDITOR.md)。
- 2026-09-07 修复全视场像差在单一轴上视场中把极小默认宽度除以零最大视场、生成越界归一化坐标的问题。全视场采样现在按径向归一化单位圆裁剪边界外点，零宽度轴上网格只计算中心点，绘图坐标围绕所选视场中心显示。全视场像差、视场定义与相邻波前图定向回归 `22/22` 通过，整个解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误；格式和差异检查通过。本次不更新历史全量测试基线。
- 2026-09-07 使用用户实际 `zmax_38668.ZMX` 复现 12 个默认分析入口中 11 个共享 Working F/# 异常。将忽略表面口径的尺度探测统一到公共实现，同时保留真实波前/偏振光瞳截光、整体瞄准和无通光样本的失败状态；MTF 继承源 PSF 视场尺度，高采样 PSF 归一化避免整数溢出。原文件入口、斯涅尔定律/解析圆孔 MTF、相邻高 NA/无焦/衍射及已提交 Zemax 数值基准定向回归 `86/86` 通过；默认目录完整解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误，格式和差异检查通过。实际 Avalonia MTF 面板截图成功。原文件新一轮 Zemax 捕获因本机缺少 ZOS-API 未完成，不能计为实机一致性通过。本次不更新历史全量测试基线，详见 [38668 公共计算修复](ZEMAX_38668_WORKING_F_NUMBER.md)。
- 2026-09-07 增加“实验室 → AI 初始结构”的独立进程入口。正式 App 仅含路径定位与启动代码，正式 Core/Application/App 不引用实验室程序集；实验算法、App、数据、测试和解决方案继续独立。正式启动器/架构定向检查 `33/33`、实验室隔离/界面源契约 `2/2` 通过；Windows 独立程序窗口启动检查通过。正式及实验室解决方案分别在默认 Debug/Release 输出构建为 `0` 警告、`0` 错误，正式解决方案格式检查与差异检查通过。未修改默认安装包、实验算法或历史全量测试基线。
- 2026-09-07 独立实验室 P0/P1：计划先保存、12 个完整目标规格先冻结，再实现严格零曲率启动引擎。新路径定向 `18/18`、实验室全量 `42/42`，均无失败/跳过；独立解决方案默认 Debug/Release 构建及格式、差异检查通过。原十个算法基准未改。此时只验证轴上、主波长、30% 入瞳启动，完整规格及新界面未完成，正式源码和安装包不变。见 [P0/P1 实施记录](INITIAL_STRUCTURE_P1_2026-09-07.md)。
- 2026-09-07 独立实验室 P2：先将 P1 截距指标迁移到正式 Core 点列接口（启动算法 v2），再实现完整口径／视场／光谱的单家族引擎。实验室全量 `63/63`、正式相关定向 `46/46`，均无失败／跳过；正式和实验室默认 Debug/Release 构建均为 `0` 警告、`0` 错误。12 个冻结规格各测一个固定家族，4 个达标、8 个明确报告差距，规格和容差未改。新引擎尚未接入新桌面工作流，多种子发布协议及新候选外部对照仍未完成；不改写正式全量数值基线。见 [P2 实施记录](INITIAL_STRUCTURE_P2_2026-09-07.md)。
- 2026-09-07 独立实验室 P3：新增真实目录玻璃、离散表面光阑、片数家族和有父来源的细化分支，全部光学评价仍调用正式 Core。原子检查点预扣预算，正常完成后结算，未知中断工作保守计费；串行／并行及完成批次恢复一致性通过。实验室全量 `91/91`，最终去重调整后的相关子集 `16/16`（包含于 91 项），均无失败／跳过；独立实验室最终默认 Debug/Release 构建均为 `0` 警告、`0` 错误，格式及差异检查通过。12 个冻结规格按原种子 101 和各 10,000 次预算运行，4 个找到多个达标家族，8 个仍有差距；不表示发布协议通过，也未全面改善 P2 的像质结果。本次未修改正式 Core、未重跑正式测试，旧桌面 v3 尚未接入新搜索。见 [P3 实施记录](INITIAL_STRUCTURE_P3_2026-09-07.md)。
- 2026-09-07 独立实验室 P4：新界面接入严格平板多家族搜索；二维面形与光线全部来自正式 Core，新增目标／处方／视场表、A/B 同尺度比较、保留预算恢复、独立追加细化和停止后导出。参数修改清空旧图，损坏记录显示错误，无效数值不再失焦回退后误用旧参数。实验室全量 `104/104`，约 11 分 19 秒；最后输入修正后的相关 `14/14` 在最终 Release 重放（属于 104 项），均无失败／跳过。默认 Debug/Release 构建 `0` 警告、`0` 错误，格式与差异检查通过；1240/600/480 px 实际 Avalonia 交互渲染及独立 Windows 程序启动／关闭检查通过。原生人工跨平台 UI 验收、60 次发布协议和新候选外部数值对照未完成；正式源码、冻结规格和安装包未改，未重跑正式数值测试。见 [P4 实施记录](INITIAL_STRUCTURE_P4_2026-09-07.md)。

## 镀膜构建的运行依赖与发布边界

镀膜独立解决方案显式列出共享 Core/Application，构建配置与实验室一致；正式主解决方案仍不包含实验室项目。TFStudio/tmmcore 源归档不进入编译依赖，Node 仅供维护参考数据；应用输出复制 MIT 原始许可证和材料 CC0 来源说明。缓存锁定还原及本机验证命令见[镀膜验收](../validation/coating/README.md)。

Windows/Linux 原生交互和标准安装包未验收，macOS 原生自动化服务超时未完成；不得从通过构建或 Headless 控件测试推断这些平台已验收。当前构建及测试证据不替代正式全量发布门禁。

## 镀膜日常设计构建更新 · 2026-09-28

2026-09-28 日常设计增强的镀膜实验室 Debug/Release 各 **44/44**、正式相关 Release **226/226**（定向，非正式全量）；固定 tmmcore 的 120 案例包含在实验室一项测试中。共享主题、材料表导入编辑、多腔和结构搜索、公差明细已实现；减反达标，高反/窄带仍有未达标项。macOS 原生生成/优化与主题切换已走查，Windows/Linux 与安装包尚未验收。详见[镀膜验收](../validation/coating/README.md)。

实验室现在源码链接正式主题文件及字体/图标许可证，依赖已有 Dock.Avalonia 控件包；正式程序仍不反向引用实验室。默认 Debug/Release 输出已更新。macOS 的临时验收 app bundle 只用于走查，不是签名公证安装包。新批处理入口 `--batch-daily-examples <目录>` 导出结构搜索与公差示例；原 `--batch-examples` 保留。

## 顺序操作数第二十九批复验（2026-10-02）

默认 Core、Application、App 和主测试输出构建成功，Debug/Release 合并各 **2560/2560**。新增 39 项；扩展 24 项既有 CAD、自动口径及镜头表回归。旧 2497 项集合全部保留；相同 BLTH 测试的机械边缘期望按平环定义修正，不增加计数。源码格式及差异空白检查通过。详见[实现与限制](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)和[证据清单](../artifacts/validation/sequential-operands-twentyninth-20261002/verification.json)。

```sh
for config in Debug Release; do
  /usr/local/share/dotnet/dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c "$config" --no-restore -m:1 -nr:false -p:UseSharedCompilation=false --filter "$(cat artifacts/validation/sequential-operands-twentyninth-20261002/test-filter.txt)"
done
```

实际 TRX/log 在上述证据目录中分别保存；定向 217、59 项有重叠，不与 2560 合并相加。不是全产品或实验室发布验收，没有新增外部原生捕获；TSAG 零倾角球面表比较只覆盖既有捕获文件和采样。

## 顺序操作数第三十五批复验（2026-10-03）

CONF/ZTHI 本地受限执行，配置副本、优化链接及保存保护，TTHI 允许同一面闭区间。默认输出 Debug/Release 合并回归各 **3135/3135**，零失败、零跳过、零编译警告/错误；包含前批 3089 项及本批新增 46 项。过滤器与日志见 [本批证据](../artifacts/validation/multi-configuration-operands-20261003/test-filter.txt)；默认 Debug/Release 输出更新，不使用备用输出目录。详情见 [实现范围](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。

## 顺序操作数第三十六批复验（2026-10-03）

新增 MCOV/MCOG/MCOL、本地 MCE 数值行表、行引用维护和 STAROPT v5；配置面板增设行表页，支持输入保护和真实明暗/窄窗口渲染。默认 Debug/Release 合并回归各 **3183/3183**，包含前批 3135 与新增 48 项，零失败、零跳过、零编译警告/错误。当前定向 Core/应用 45 项、界面 3 项加架构 17 项共 65/65 通过。过滤器见[本批证据](../artifacts/validation/mce-row-operands-20261003/test-filter.txt)，实现范围见[详细记录](MCE_ROW_OPERANDS_2026-10-03.md)。

## 顺序操作数第三十七批复验（2026-10-03）

本批完善多配置单元格变量、联合优化与项目 v6；不增加操作数代码。默认 Debug/Release 合并回归各 **3236/3236**，完整保留前批 3183、新增 35 并扩展既有求解 18 项；零失败、零跳过、零编译警告/错误。最后界面/数值/架构子集 118/118，6 张实际控件截图已检查。29 份基线逐字节不变，不表示新增原生数值对照。命令过滤器见[本批清单](../artifacts/validation/mce-cell-variables-20261003/test-filter.txt)，功能与限制见[详细记录](MCE_CELL_VARIABLES_2026-10-03.md)。

## 多配置拾取第三十八批复验（2026-10-03）

默认 Debug/Release 输出的累计回归各 **3291/3291** 通过（完整保留前批 3236，新增 55），零失败、零跳过、零编译警告/错误。界面/数值/架构子集 **173/173**，12 张实际控件截图已检查。范围与限制见[本批记录](MCE_PICKUP_SOLVES_2026-10-03.md)。

## 2026-10-03 第三十九批：SVIG 自动渐晕

新增 SVIG 受限执行，隔离副本中的四边缘孔径搜索、失败状态保留、原生导入只读保护、参数/保存及优化路径已接通。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。见[实现与限制](AUTOMATIC_VIGNETTING_2026-10-03.md)。

## Gradient 5 空间材料与色散复验（2026-10-03）

本批默认输出命令与日志保存在 `artifacts/validation/gradient5-dispersion-20261003/`。累计过滤器延续自动渐晕阶段所有测试身份并加入 `FullyQualifiedName~Gradient5`，覆盖材料、连续/近轴追迹、应用保存打开、优化及帮助。该累计子集不是完整解决方案测试，也不是原生 Zemax 数值比较。

```sh
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -c Debug --filter "$(cat artifacts/validation/gradient5-dispersion-20261003/test-filter.txt)" --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/validation/gradient5-dispersion-20261003
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-restore -c Release --filter "$(cat artifacts/validation/gradient5-dispersion-20261003/test-filter.txt)" --logger 'trx;LogFileName=release.trx' --results-directory artifacts/validation/gradient5-dispersion-20261003
dotnet build src/OptilandWorkbench.App/OptilandWorkbench.App.csproj --no-restore -c Release
```

独立 Skia 会话以 `OPTILAND_GRADIENT5_CAPTURE_DIR` 指向本批 screenshots 目录，只运行 `Gradient5HelpPanelTests`；帮助和分层架构另用 `OperandHelpTests|Gradient5HelpPanelTests|LayeringArchitectureTests` 过滤器。首轮 Debug 3389 项通过后发现族摘要还写着 Gradient 1～4，修正并追加断言后重新验证；早期通过日志保留为 `debug-before-family-summary`，不代替最终版本的证据。最终数据见[验证记录](../artifacts/validation/gradient5-dispersion-20261003/verification.json)。


## MTF 制造公差复验（2026-10-04）

默认 Debug/Release 测试项目构建同时更新 App、Application 与 Core 输出，零警告/错误。最终累计回归各 3500/3500：此前 3426 项均保留，新增 33 项数值/工作流及 5 项界面/保存/报告用例；另外纳入已有公差 25 项和主动作 11 项。初始双配置通过后又修正概率轴和研究设置失效保护，最终完整复跑通过；不是仅对初始二进制的计数。格式检查和 git diff --check 通过。独立 Skia 渲染 3/3、9 张最终窗口截图检查通过。没有新增 Zemax 原生公差数值捕获，29 个固定基线文件保持与 HEAD 字节一致。见 [实现记录](MTF_TOLERANCING_2026-10-04.md)及 [日志/TRX/清单](../artifacts/validation/mtf-tolerancing-20261004)。
