# 多配置操作数 CONF / ZTHI

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

2026-10-03 行表更新：后续已补本地四类 MCE 行表与 MCOV/MCOG/MCOL，保存格式升级为 STAROPT v5。本页 337 项执行、3135 项回归及“尚无行表”保留 CONF/ZTHI 阶段的历史边界；PRIM/CVIG/IMSF 后接 CONF 的限制仍有效。 详见[本批记录](MCE_ROW_OPERANDS_2026-10-03.md)。

2026-10-03。两项新增受限执行；337/383 个已核实官方代码连接计算，46 项兼容保留（39 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。目录仍为 8 大类、51 族、387 条目。

## 定义与已实现行为

按 [Ansys OpticStudio 2026 R1.03 操作数说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26103/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)核对 CONF 的切换含义、FDMO 配置边界，以及 ZTHI 的厚度差约束。官方文档核对不是原生数值捕获。

| 操作数 | 本地参数 | 本地实现 |
| --- | --- | --- |
| CONF | Int1=Cfg#，从 1 开始，其他五槽未用 | 后续行读取指定配置；目标和权重不参与贡献、优化权重及应用归一化。禁用/跳过不切换；无效号返回明确错误，不改变计算状态。只能用于有序评价。 |
| ZTHI | Int1/Int2=Surf1/Surf2，其他四槽未用 | 对文档所有配置求指定表面闭区间的有符号厚度和，返回 max(Target, max(total)−min(total))，Target 是非负允许差，单位 mm。支持同一表面；限定正面号，不含物面。 |

单镜头有序入口等价于一个配置，因此 CONF=1 有效，其余配置号错误；单配置 ZTHI 的差为零。TTHI 现允许起止面相同，保留原有闭区间、Int2=0 默认像面及正无穷物面厚度跳过规则；修正帮助中与已实现闭区间不符的旧文字。

CONF 切换到同一配置也恢复全部 FDMO 临时字段；再次 FDMO 使用所选配置原始最大视场半径。计算副本和逐批追迹缓存隔离，用户的活动镜头、字段和主波长不被改写。跨配置数学行引用保留原有前序行规则，CONF 状态行不能作数值输入。

ZTHI 读取整个文档配置集合的原始几何，不按临时 IMSF 截断。任一配置缺面、非有限厚度/总和或差值溢出均失败，优化不能把失败当零值。

## 应用与优化

应用评价页传入完整配置集合及当前配置号。候选计算每线程拥有独立配置副本；当前基准配置（内部索引 0）的半径/厚度变量及同类拾取同步到未断开的链接。非基准配置变量仅更新该配置；物理膜层变量沿用当前配置范围，不新增隐式链接。

优化仍只搜索当前配置中标记的变量，其他配置的独立变量联合搜索尚未实现。已有整个文档的提交、失败回滚与撤销保留。配置本身不会因评价中的 CONF 被切走。保存与撤销复用 STAROPT 现有配置、链接及操作数字段，没有添加第二套光学引擎或改变文件格式版本。

当前活动评价函数依赖 CONF/ZTHI 且有多个配置时，保存为仅含活动镜头的 JSON 或文本会明确失败，原文件不受影响。本地新行的文本列尚未核实，文本导出也明确拒绝；使用 STAROPT 保存。原生 ZMX 行仍禁用只读，快照恢复不自动升级。

## 明确未完成

- PRIM/CVIG/IMSF 后再执行 CONF 的组合未取得原生状态交互证据，本批明确拒绝，不猜测跨配置保留或重置规则。CONF 后可在最后一个配置使用这些状态行。
- MCOV/MCOG/MCOL 的 Op# 是多配置编辑器行号。现有多配置厚度编辑还不是 MCE 行表，不能用表面号替代；这三项继续兼容保留。
- 原生 CONF/ZTHI 参数列、多个配置中的数值及优化结果尚无新增 ZOS-API 捕获；仅核对手册定义和本地一致性。
- 其余 39 项已知功能与已执行操作数的未完成模式，仍属于总任务，不标记“全部完成”。

## 验证

默认输出 Debug/Release 合并回归各 **3135/3135**，零失败、零跳过、零编译警告/错误；包含前批 3089 项及本批新增 46 项。本批 46 项测试覆盖：有序切换和数学行、目标/权重忽略、非法与跳过配置、未核实状态组合拒绝、FDMO 配置尺度与恢复、全配置厚度差/目标边界/有符号闭区间、并发隔离、4 个生产 DLS 案例、回滚与取消、原生只读、六槽编辑、STAROPT 保存及撤销、禁止有损导出。

前一批 3089 项回归延续；一项已有 FDMO 测试由“配置边界仍不支持”更新为“同配置切换恢复字段”，其余既有测试身份保留。使用默认 Debug/Release Core/Application/App 二进制。没有新增布局、主题或屏幕图，本批不是截图审核。

证据：[机器核对记录](../artifacts/validation/multi-configuration-operands-20261003/verification.json)、[完整过滤器](../artifacts/validation/multi-configuration-operands-20261003/test-filter.txt)、[操作数清单](../artifacts/validation/multi-configuration-operands-20261003/operand-audit.csv)、[剩余分类](../artifacts/validation/multi-configuration-operands-20261003/remaining-by-category.csv)。基线完整性、Workbench 重算、局部数值校验与原生等价分别报告，已提交 29 个基线文件不作修改。

## 代码入口

- [配置上下文](../src/OptilandWorkbench.Core/Optimization/MeritConfigurationContext.cs)、[ZTHI 求值](../src/OptilandWorkbench.Core/Optimization/MeritFunction.MultiConfiguration.cs)、[临时状态](../src/OptilandWorkbench.Core/Optimization/MeritFunction.SystemState.cs)。
- [生产优化接线](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs)、[评价页服务](../src/OptilandWorkbench.Application/Services/OptimizationService.cs)。
- [跨配置回归](../tests/OptilandWorkbench.Tests/MultiConfigurationOperandTests.cs)。
