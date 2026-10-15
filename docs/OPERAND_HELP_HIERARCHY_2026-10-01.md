# 操作数帮助层级导航

> 历史阶段报告：保留当时的实现、命令、测试与失败结果，不代表当前产品验收。最新状态见 [当前状态](CURRENT_STATUS.md)，可执行构建入口见 [构建与发布](BUILD_AND_RELEASE.md)；旧独立实验室架构和命令已被统一内置模块取代。

2026-10-03：SVIG 在“一阶与系统数据 → 波长与系统状态”族内升级为受限可计算，详情说明主波长、四条边缘光线和临时状态。8 大类、51 族、387 条目保持不变；Precision 的三档含义放在计算说明中，避免参数名称截断。

2026-10-03：MCO 族帮助更新四类单元格 P 拾取、比例/偏移、源引用顺序与限制，沿用“大类 → 操作数族 → 具体代码”三级目录。仍为 8 大类、51 族、387 条目；未为每个单元格或求解设置增加帮助条目。见[实现记录](MCE_PICKUP_SOLVES_2026-10-03.md)。

2026-10-03 行表更新：MCOV/MCOG/MCOL 在“一阶与系统数据 → 多重结构”中改为受限可计算；详情区说明行号/配置号、四类绑定与原生缺口。8 大类、51 族、387 条目保持不变。 详见[本批记录](MCE_ROW_OPERANDS_2026-10-03.md)。

2026-10-03 多配置更新：CONF/ZTHI 仍位于“一阶与系统数据 → 多重结构”，两项状态改为受限可计算；MCOV/MCOG/MCOL 保持兼容保留。8 大类、51 族、387 条目不变，参数、支持范围及原生缺口分别说明。 详见[本批记录](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。

2026-10-03 膜层约束：CM/CI/CE 的九个官方代码继续归在“光束与专项 → 镀膜与偏振”，共同参数和限制写在详情。8 大类、51 族、387 条目保持不变；新增可计算状态不会产生额外的自定义操作数。 详见[实现与验证](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03 CODA：CODA 与 RRET 同位于“光束与专项 → 镀膜与偏振”，两者均为受限可计算，参数/计算/原生缺口各自说明。CODA 不按 22 种 Data 再拆成目录叶子。8 大类、51 族、387 条目保持不变。 详见[实现与验证](COATING_DATA_OPERAND_2026-10-03.md)。

前一阶段（2026-10-03 RRET）：RRET 仍位于“光束与专项 → 镀膜与偏振”，更新为受限可计算，详情明确八槽、相位单位、局部主值约定与原生缺口；CODA 仍显示兼容保留。8 大类、51 族、387 条目不变。 详见[实现与验证](POLARIZATION_RETARDANCE_2026-10-03.md)。

2026-10-03 HYLD：HYLD 仍位于“光线与坐标 → 光线条件与良率”，已更新为受限可计算，HHCN 仍显示兼容保留。族说明、具体公式、参数和原生验证缺口分别显示，目录层级和面板布局不变。 详见[计算与验证](HIGH_YIELD_OPERAND_2026-10-03.md)。

2026-10-03：GRIN 六点约束已从兼容保留转为受限可计算；仍集中在原三级目录中，点位搜索、状态筛选和参数说明随之更新。下文历史截图/19 项计数保留对应阶段；最新验证见 [GRIN 材料约束](GRIN_CONTROL_OPERANDS_2026-10-03.md)。

2026-10-01：帮助页已由按字母平铺的表格改为“功能大类 → 操作数族 → 具体操作数”的可折叠树，保留原左右分栏与窄窗口上下布局。

## 当前行为

- 387 个现有条目分入 8 个大类、51 个操作数族；包含 383 个已核实的官方代码及 4 个本程序扩展，每个条目只出现一次。分类是本程序的浏览组织，不是 Zemax 官方界面的分类复刻，也不参与计算引擎选择。
- 大类为：一阶与系统数据、光线与坐标、像质与分析、面形与制造、材料与介质、光束与专项、数学与控制、扩展与保留。首次打开折叠各类，右侧显示第一类概览；选择族时显示数量、支持状态概况及族说明。
- 明确标注一级“功能大类”、二级“操作数族”、三级“具体操作数”。左侧保留目录路径提示，并用不同字重区分大类、族与条目；右侧把当前层级、标题、路径及计算状态分开显示。
- 大类概览列出可点击的操作数族，族概览列出可点击的具体代码；每项显示数量或计算支持状态。点击下级目录同步展开左侧树、选中对应节点，并将右侧滚动位置复位到开头。搜索后的目录只包含匹配项；无结果时清除下级目录。
- GRIN 的 18 个编号约束集中在“材料与介质 → GRIN 折射率约束”。具体项补充位置及约束名称，例如 `I3GT · 前端 +X 折射率下限`；族说明解释六个位置和 GT/LT/VA 三种约束。HYLD 显示“真实光线高良率贡献”。
- 可计算、未实现兼容保留、本程序扩展、官方未用、定义待核实等状态与功能分类分别显示；保留名称不代表已实现。兼容条目的参数区标为“保留的原始参数”，说明未核实的槽位不代表已经完成参数定义；不显示可计算行的通用贡献说明。
- 搜索仍覆盖代码、名称、定义、计算说明、参数，并增加大类、族名及位置名称。匹配结果保留父级路径并自动展开；支持状态筛选继续生效。清空搜索后尽量保留当前选择与原展开状态。无结果时清空旧详情，恢复结果后恢复详情区。
- 树支持键盘上下移动、左右展开/折叠，分类与条目有可访问名称及完整提示。采用现有主题及字号令牌；本次未新增计算能力、修改参数存储或改变文件兼容规则。

第二十三批校正：REQS 归入流程控制，RRET 归入偏振与镀膜相关族；8 大类、51 族、387 条目总数不变。下方渲染与 19 项验证是帮助层级初版阶段记录，最新合并回归见[第二十六批](ZEMAX_OPERAND_SUPPORT.md)。

## 2026-10-02 可视层级与下级目录

此前已有三级树，但各层视觉差异较弱，右侧只提供文字概览。本次补充显式层级、可点击下级目录与跳转后的滚动复位；采用类型化层级字段控制呈现，不依赖中文标题判断层级。8 大类、51 族、387 条目和计算支持范围不变。

默认 Debug/Release 输出重新构建，帮助交互、官方名称/支持标识及字号回归各 **19/19** 通过；沿用现有测试用例，增加逐级点击、叶节点隐藏下级目录、无结果清除层级以及切换后的滚动复位断言。检查 Light/Dark 默认目录、GRIN 分组、代码搜索及 680 DIP 窄窗口共 8 张实际 Avalonia/Skia 渲染。见[本次验证记录](../artifacts/validation/operand-help-levels-20261002/verification.json)。本次为帮助界面的定向验证，没有重新执行 2308 项光学与操作数合并回归，没有新增 Zemax 数值捕获。

![当前一级与二级目录](../artifacts/validation/operand-help-levels-20261002/screenshots/operand-help-light-overview.png)

![当前二级与三级目录](../artifacts/validation/operand-help-levels-20261002/screenshots/operand-help-light-grin-family.png)

此前仅复核、未修改代码的阶段证据保留在 [20261001 复核](../artifacts/validation/operand-help-hierarchy-recheck-20261001/verification.json)和 [20261002 复核](../artifacts/validation/operand-help-hierarchy-20261002/verification.json)，其截图属于本次改进前的界面。

## 当前版本复核（2026-10-02）

在第二十八批 Core 改动之后重新构建默认 Debug/Release 输出，帮助交互、名称标识与字号定向回归各 **19/19** 通过。重新检查 Light/Dark 概览、GRIN 功能组、搜索及 680 DIP 窄窗口共 8 张实际 Avalonia/Skia 渲染，确认三级目录、下级跳转与独立支持状态均保留。本次只复核已有层级实现，未修改帮助源码或新增测试，不替代第二十八批 2497 项合并回归，也不涉及 Zemax 数值等价。见[本次记录](../artifacts/validation/operand-help-current-20261002/verification.json)。

![当前版本三级目录](../artifacts/validation/operand-help-current-20261002/screenshots/operand-help-light-grin-family.png)

## 前次工作区复核（历史记录）

2026-10-02 后续 Core 改动构建后，帮助、名称标识及字号子集仍为 Debug/Release 各 **19/19**。Debug 单独运行并保存 8 张当前控件图；Release 含于镀膜快照与帮助的 61 项过滤器，帮助子集逐项核实为 19 项。本次没有再修改帮助源码或增加条目。来源和当前默认二进制见[收尾记录](../artifacts/validation/operand-help-final-20261002/verification.json)。

## 实现与验证

- [分类映射](../src/OpticalSystemDesign.App/Panels/OperandHelpTaxonomy.cs)：稳定代码到浏览层级的映射；未知新代码进入明确的“待分类”，测试阻止发布目录出现漏分项。
- [筛选与树投影](../src/OpticalSystemDesign.App/Panels/OperandHelpProjection.cs)：只从优化服务提供的条目生成树，不补造可执行操作数。
- [帮助面板](../src/OpticalSystemDesign.App/Panels/OperandHelpPanel.cs)：选择、展开、详情和状态展示。
- [交互回归](../tests/OpticalSystemDesign.Tests/OperandHelpTests.cs)：覆盖完整目录唯一性、GRIN 分组、搜索路径、支持状态筛选、空结果清除、选择保留、键盘展开、明暗主题和窄窗口。

验证命令、结果与范围见[构建记录](OPERAND_HELP_HIERARCHY_2026-10-01.md)。此前 [2057 项名称核对及光学回归](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)保留其当时的独立范围，本次没有重跑全部光学回归或新增 Zemax 数值捕获。

## 初版实际控件渲染（历史记录）

下图来自初版 Avalonia/Skia 控件渲染，不是设计稿；覆盖 Light/Dark、默认概览、族说明、搜索结果和 680 DIP 窄窗口共 8 张。当前界面见上方“可视层级与下级目录”。

![默认大类](../artifacts/validation/operand-help-hierarchy-20261001/screenshots/operand-help-light-overview.png)

![GRIN 操作数族](../artifacts/validation/operand-help-hierarchy-20261001/screenshots/operand-help-light-grin-family.png)

![窄窗口](../artifacts/validation/operand-help-hierarchy-20261001/screenshots/operand-help-light-narrow.png)

2026-10-03 单元格变量更新：MCO 仍位于“一阶与系统数据 → 多重结构”；四类行绑定的跨配置独立变量范围及原生缺口写入详情，不按配置号扩充帮助叶子。三级目录和 387 条目数不变。

## Gradient 5 帮助同步 · 2026-10-03

第四十批更新“材料与介质 → GRIN 折射率约束”族的总说明及叶子详情，明确 Gradient 1～5 的材料计算与 Gradient 5 可选色散。LPTD 仍在“材料与介质 → GRIN 范围与梯度”下显示“未实现 · 兼容保留”，说明尚缺单调性残差，不因底层模型具备就自动改成可计算。8 大类、51 族、387 个叶子不变；没有为七个空间系数或色散参数新增操作数名称。实际 Light/Dark/窄窗口渲染见[本批记录](GRADIENT5_DISPERSION_2026-10-03.md#实际帮助界面)。
