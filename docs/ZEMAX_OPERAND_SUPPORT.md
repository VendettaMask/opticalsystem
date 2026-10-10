# Zemax 顺序模式操作数支持规范

2026-10-10 已接通有焦波前图的出瞳 X/Y 工作 F 数显示投影，并将 Foucault 改为复场焦面刀口与逆 FFT 理想光瞳再成像，支持正式 Jones 可执行范围及线性/对数显示。默认 Debug/Release 完整构建零警告、零错误；正式完整 Release **4608/4608**、Debug 相关 **236/236**、Release 专项 **60/60**、实际控件渲染 **1/1**，均零跳过。此前 4577 项身份与次数全部保留，新增 31 项；既有 Foucault 契约按物理模型更新，集合重叠不相加。完整比较工具 **179 通过 / 1 失败 / 共 180 项**，原 Huygens 失败身份、消息与输出不变，N01/N02 仍为 Close/Difference。历史冻结清单仍为 **2660/2668**，8 项差异保留为 E04；165 份原生捕获及旧账本字节未改，原参考、设置和容差未改。本机不支持 Zemax，未新增原生认证；完整 Debug、实验室、安装包、人工桌面和六镜头矩阵未重跑。任意三维出瞳、无焦焦面投影及未支持的 Jones 介质等边界仍开放；本轮代码、测试、文档与选定证据已推送至 origin/main，见[同步完成记录](PROJECT_SYNC_PUPIL_FOUCAULT_2026-10-10.md)。详见[当前实现与验收](PUPIL_SHAPE_FOUCAULT_IMPLEMENTATION_2026-10-10.md)。

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

2026-10-02 第二十八批：新增共享最小体积球面拟合及 BFSD 本地评价、编辑保存、优化路径。该阶段 **301/383 项受限执行、82 项兼容保留**（75 项已知功能、2 项定义待核实、5 项 Unused）。默认 Debug/Release 输出构建成功，合并回归各 **2497/2497**（含新增 26 项），零失败/跳过。固定 Zemax 球面矢高表只验证该文件的球面情况，原生 MFE 与非球面拟合仍有验证缺口；见[本批实现与证据](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)。以下记录保留各自阶段范围。

2026-10-02 第二十七批：RELI/EFNO 已接通本地编辑、保存、评价函数和优化，显式复用共享像方网格及 Jones 功率链。该阶段 **300/383 项受限执行、83 项兼容保留**（76 项已知功能、2 项定义待核实、5 项 Unused）；仍有原生数值和模式缺口，两项 ZMX 导入保持只读。默认 Debug/Release 输出构建成功，合并回归各 **2471/2471**（含新增 31 项），见[本批实现与证据](ILLUMINATION_OPERANDS_2026-10-02.md)。以下记录保留各自阶段范围。

此前采样阶段（2026-10-02）：RELI/EFNO 的共享 Core 新增均匀像方方向余弦采样和轴上参考；低密度边界计数仍导致明显绝对 F 数差异，两项没有升级为可执行，仍为 298/383 项受限执行、85 项兼容保留。见[像方网格与轴上参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)。

此前膜层阶段（2026-10-02）：物理相干膜层已接入正式追迹与照度 Jones 功率链，含有限吸收膜层、透明外侧介质透射、吸收基底反射和严格材料快照。RELI/EFNO 仍保留，计数不变；原生采样和 MFE 联动尚未完成。见[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)。下方透明介质基础段落保留其阶段范围。

当前状态复核：2026-10-02。本文是顺序模式操作数的目标规范与验收矩阵；“必须支持”不等于当前版本已经实现，实际完成度以文末“当前基线与差距”为准。代码、文件字段和产品专有名词保留原始英文标识。

## 目的

此前基础阶段（2026-10-02）：RELI/EFNO 的全光瞳标量积分已进入共享 Core，并替换相对照度分析和图像模拟中的边缘强度近似；透明介质的非偏振光 Jones 功率链已在后续阶段加入；复杂介质偏振、原生采样约定和操作数联动仍待完成，两项保持兼容保留，未增加可执行计数。固定 Zemax 曲线仍有可见误差，详见[照度基础与验证边界](ILLUMINATION_CORE_2026-10-02.md)及[偏振功率链](ILLUMINATION_POLARIZATION_2026-10-02.md)。

本文定义 Optical System Design 对 Zemax OpticStudio 评价函数操作数的完整支持边界。它既是实现规范，也是验收矩阵，不是对当前代码覆盖率的夸大声明。

当前名称证据是官方发布的 OpticStudio 2026 R1 `MeritOperandType` 448 名称 API 参考；本项目 383 个注册代码逐项匹配。此前文档记录 2026-09-02 本机顺序 MFE 可选择 442 项，本次未找到该完整原始导出，不能作为新复核的实机证据。383 是本项目兼容范围，不是当前所有 Zemax 功能的总数；公开手册与 API 参考也存在版本差异。完整核对及来源见 [专项审计](ZEMAX_OPERAND_AUTHENTICITY_AUDIT_2026-10-01.md)。

除下述三类明确排除项外，不得因为实现困难、计算成本、需要其他分析引擎或当前 UI 没有对应入口而省略操作数：

1. 参考目录明确列为废弃的 `PnGT`、`PnLT`、`PnVA` 操作数族；
2. 参考目录明确列为非序列物体数据或非序列光线追迹/探测器专用的操作数；
3. 官方明确标记 Unused 的 BIPF、COSA、HACG、QOAC、TRAN，仅保存旧文件，不作为待实现计算功能。

帮助界面现按功能大类、操作数族、具体代码逐级浏览，分类与支持状态独立，见[帮助层级](OPERAND_HELP_HIERARCHY_2026-10-01.md)。此呈现变更不增加可执行操作数数量。

## “支持”的统一定义

一个操作数只有同时满足以下条件，才能在支持矩阵中标记为“完成”：

1. **识别**：ZMX 导入器、原生项目和编辑器都能识别规范化后的四字符代码。
2. **参数语义**：每个整数列和浮点列按该操作数自身的含义解释，不能把所有 `Int1`、`Int2` 通用地当成表面、视场或波长。
3. **校验**：按类型校验表面、视场、波长、结构、采样、数据列和枚举范围；所有数值仍须满足有限值及资源上限要求。
4. **计算**：能从当前 `Optic`、指定结构和所需分析上下文计算当前值；不得用常数零、注释行或“只读保留”代替。
5. **评价函数**：目标值、权重、贡献量、启用状态和行顺序参与评价函数，控制/数学操作数遵守其状态及前后行依赖。
6. **往返**：ZMX → 内存 → STAROPT → 内存不丢失代码、参数、目标、权重、注释、启用状态或参数单位。
7. **交互**：桌面编辑器能显示正确列名、单位、默认值和有效范围；不适用的列不可伪装成其他参数。
8. **工程属性**：支持取消、确定性执行和异常隔离；昂贵分析使用缓存并服从并行度控制。
9. **测试**：至少有参数映射、数值计算、非法引用、ZMX 导入、STAROPT 往返和评价函数贡献测试。

仅满足“识别”或“原样保存”属于兼容占位，不属于完整支持。

## 数据模型要求

当前 `Surface/Field/Wavelength/Hx/Hy/Px/Py` 固定字段只适合部分成像操作数，不能承载完整 Zemax 目录。完整实现应采用元数据驱动的类型注册表：

```text
ZemaxOperandDescriptor
  Code
  Category
  Parameters[]
    Slot
    Name
    ValueKind
    Unit
    DefaultRule
    ValidationRule
  EvaluationEngine
  RowDependency
  ConfigurationDependency
```

导入时先保存 ZMX 行的通用槽位，再由描述符生成类型化访问。快照应保存通用槽位及必要的扩展数据，而不是把第二个整数槽位无条件写进 `Wavelength`。已有 Workbench 友好字段可以继续作为类型化视图，但不能成为原始数据的唯一存储。

操作数代码统一使用大写。官方手册中的 `InGT`、`InLT`、`InVA` 是族名，其中 `n` 为 1–6；实际代码必须展开为 `I1GT`–`I6GT`、`I1LT`–`I6LT` 和 `I1VA`–`I6VA`，不得注册并不存在的 `INGT/INLT/INVA`。


## 行颜色语义

Zemax 在编辑器首选项启用 `Color Rows` 时按操作数类型显示默认行色，ZOS-API 还通过 `IMFERow.RowColor` 暴露逐行颜色。行色是编辑器元数据，不得影响操作数值、目标、权重、贡献或执行顺序。

当前已实现：

- 评价函数编辑器按操作数代码应用类型色；导入参考 ZMX 后，参考截图中的主要操作数色系与 Zemax 对齐；
- `BLNK` 使用白色，`DMFS` 使用品红色，错误状态优先使用红色；
- 选中状态只强化边框并保留行底色，避免全局选中样式抹掉颜色语义；
- 未识别或尚无专用映射的操作数使用确定性的中性回退色。

当前尚未实现：STAROPT/ZMX 对 ZOS-API `Color1`–`Color16` 自定义行色、逐行“无颜色”和全局 `Color Rows` 偏好的往返。完成这些能力时必须扩展通用操作数元数据及快照校验，不能把颜色塞入任何数值参数槽位。
## 必须支持的操作数目录

下表中的状态均为“必须实现”，不表示当前版本已经通过验收。重复出现在多个 Zemax 分类中的代码只注册一次，但可保留多个分类标签。

### 系统和参数数据

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| 系统数据 | 8 | `CONF`, `IMSF`, `PRIM`, `SVIG`, `WLEN`, `CVIG`, `FDMO`, `FDRE` |
| 镜头参数约束 | 52 | `COGT`, `COLT`, `COVA`, `CTGT`, `CTLT`, `CTVA`, `CVGT`, `CVLT`, `CVVA`, `BLTH`, `DCRV`, `DMGT`, `DMLT`, `DMVA`, `DPHS`, `DSAG`, `DSLP`, `ETGT`, `ETLT`, `ETVA`, `FTGT`, `FTLT`, `MNCA`, `MNCG`, `MNCT`, `MNCV`, `MNEA`, `MNEG`, `MNET`, `MNPD`, `MXCA`, `MXCG`, `MXCT`, `MXCV`, `MXEA`, `MXEG`, `MXET`, `MNSD`, `MXSD`, `QSLP`, `TGTH`, `TTGT`, `TTHI`, `TTLT`, `TTVA`, `XNEA`, `XNET`, `XNEG`, `XXEA`, `XXEG`, `XXET`, `ZTHI` |
| 玻璃数据约束 | 10 | `GCOS`, `GTCE`, `INDX`, `MNAB`, `MNIN`, `MNPD`, `MXAB`, `MXIN`, `MXPD`, `RGLA` |
| 组件位置约束 | 7 | `GLCA`, `GLCB`, `GLCC`, `GLCR`, `GLCX`, `GLCY`, `GLCZ` |
| 参数数据约束 | 3 | `PMGT`, `PMLT`, `PMVA` |
| 热膨胀系数数据 | 3 | `TCGT`, `TCLT`, `TCVA` |
| 多重结构数据 | 5 | `CONF`, `MCOG`, `MCOL`, `MCOV`, `ZTHI` |

实现要求：

- 系统修改类操作数在评价函数批次内使用隔离的配置上下文，不得永久修改活动光学系统。
- 大于/小于/等于约束返回实际物理量；约束方向由目标和权重计算处理，不能通过伪造符号实现。
- 空气、玻璃、表面范围、边缘厚度和机械/有效口径必须使用材料过渡及真实边缘几何判断。
- 多重结构操作数必须读取指定结构，而不是始终读取活动结构。

### 一阶、镜头属性与光线数据

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| 一阶光学性能 | 22 | `AMAG`, `CARD`, `ENPP`, `EFFL`, `EFLA`, `EFLX`, `EFLY`, `EPDI`, `EXPD`, `EXPP`, `ISFN`, `ISNA`, `LINV`, `OBSN`, `PIMH`, `PMAG`, `POWF`, `POWP`, `POWR`, `SFNO`, `TFNO`, `WFNO` |
| 镜头属性约束 | 20 | `CVOL`, `MNDT`, `MXDT`, `PSLP`, `SAGX`, `SAGY`, `SCRV`, `SPHS`, `SSLP`, `SSAG`, `STHI`, `TMAS`, `TOTR`, `VOLU`, `NORX`, `NORY`, `NORZ`, `NORD`, `SCUR`, `SDRV` |
| 近轴光线数据约束 | 13 | `PANA`, `PANB`, `PANC`, `PARA`, `PARB`, `PARC`, `PARR`, `PARX`, `PARY`, `PARZ`, `PATX`, `PATY`, `YNIP` |
| 实际光线数据约束 | 44 | `CEHX`, `CEHY`, `CENX`, `CENY`, `CNAX`, `CNAY`, `CNPX`, `CNPY`, `DXDX`, `DXDY`, `DYDX`, `DYDY`, `HHCN`, `HYLD`, `IMAE`, `MNRE`, `MNRI`, `MXRE`, `MXRI`, `OPTH`, `PLEN`, `RAED`, `RAEN`, `RAGA`, `RAGB`, `RAGC`, `RAGX`, `RAGY`, `RAGZ`, `RAID`, `RAIN`, `RANG`, `REAA`, `REAB`, `REAC`, `REAR`, `REAX`, `REAY`, `REAZ`, `RENA`, `RENB`, `RENC`, `RETX`, `RETY` |

实现要求：

- 一阶量必须明确子午/弧矢、物方/像方、近轴/实际定义及符号约定。
- 光线操作数使用按需指定面追迹，复用同一评价批次中的光线样本。
- 方向余弦、角度、法线、光程和路径长度必须区分；全反射后的当前介质必须保持在入射侧。
- 最小/最大真实光线与强度类操作数必须定义渐晕、零强度和无有效光线时的错误行为。

### MTF、像差、能量与专项分析

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| MTF 数据 | 23 | `GMTA`, `GMTN`, `GMTS`, `GMTT`, `GMTX`, `MECA`, `MECS`, `MECT`, `MSWA`, `MSWN`, `MSWS`, `MSWT`, `MSWX`, `MTFA`, `MTFN`, `MTFS`, `MTFT`, `MTFX`, `MTHA`, `MTHN`, `MTHS`, `MTHT`, `MTHX` |
| PSF/Strehl 数据 | 1 | `STRH` |
| 傅科分析 | 1 | `FOUC` |
| 像差 | 58 | `ABCD`, `ANAC`, `ANAR`, `ANAX`, `ANAY`, `ANCX`, `ANCY`, `ASTI`, `AXCL`, `BIOC`, `BIOD`, `BSER`, `COMA`, `DIMX`, `DISA`, `DISC`, `DISG`, `DIST`, `FCGS`, `FCGT`, `FCUR`, `GSCE`, `GSCH`, `GSRE`, `GSRH`, `LACL`, `LONA`, `MWCE`, `MWCH`, `MWRE`, `MWRH`, `OPDC`, `OPDM`, `OPDX`, `OSCD`, `PETC`, `PETZ`, `RSCE`, `RSCH`, `RSRE`, `RSRH`, `RWCE`, `RWCH`, `RWRE`, `RWRH`, `SMIA`, `SPCH`, `SPHA`, `TRAC`, `TRAD`, `TRAE`, `TRAI`, `TRAR`, `TRAX`, `TRAY`, `TRCX`, `TRCY`, `ZERN` |
| 鬼像聚焦控制 | 7 | `GAOI`, `GPIM`, `GPRT`, `GPRX`, `GPRY`, `GPSX`, `GPSY` |
| 光纤耦合 | 3 | `FICL`, `FICP`, `POPD` |
| 圈入能量 | 7 | `DENC`, `DENF`, `ERFP`, `GENC`, `GENF`, `XENC`, `XENF` |
| 光学制造全息图约束 | 1 | `CMFV` |
| 最佳拟合球面 | 1 | `BFSD` |
| 灵敏度公差 | 1 | `TOLR` |

实现要求：

- 质心/主光线/未参考、矩形/高斯采样、子午/弧矢/平均以及多色参考必须逐项区分。
- `TRAC` 等依赖同行分组和顺序的操作数必须在有序批次中求值，不能独立重排。
- MTF、圈入能量、最佳拟合球、相对照度和公差结果调用对应核心分析引擎，并使用参数完整的缓存键。
- 鬼像操作数使用显式反射路径和介质状态；不能用仅含主顺序透射路径的普通最终面追迹代替。

### 高斯、GRIN、镀膜偏振和物理光学

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| 高斯光束数据 | 11 | `GBPD`, `GBPP`, `GBPR`, `GBPS`, `GBPW`, `GBPZ`, `GBSD`, `GBSP`, `GBSR`, `GBSS`, `GBSW` |
| 梯度折射率控制 | 22 | `DLTN`, `GRMN`, `GRMX`, `I1GT`, `I1LT`, `I1VA`, `I2GT`, `I2LT`, `I2VA`, `I3GT`, `I3LT`, `I3VA`, `I4GT`, `I4LT`, `I4VA`, `I5GT`, `I5LT`, `I5VA`, `I6GT`, `I6LT`, `I6VA`, `LPTD` |
| 镀膜与偏振追迹 | 10 | `CMGT`, `CMLT`, `CMVA`, `CODA`, `CEGT`, `CELT`, `CEVA`, `CIGT`, `CILT`, `CIVA` |
| 物理光学传播 | 2 | `POPD`, `POPI` |

实现要求：

- 高斯光束和 POP 使用各自传播模型，不得退化为几何 RMS 光斑。
- GRIN 点位约束读取指定空间位置的折射率；依赖传播的项目必须使用真实连续曲线路径。当前已建立材料/追迹/快照基础，并接通其中 21 项受限材料控制，LPTD 仍为兼容保留，缺少所需模型或功能时不能返回伪成功。
- 镀膜/偏振操作数使用 Jones/Fresnel 状态、实际入射角和反射/全反射分支。
- 需要外部文件或网格的数据必须纳入快照资源策略和边界校验。

### 数学、控制、宏和用户扩展

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| 通用数学 | 28 | `ABSO`, `ACOS`, `ASIN`, `ATAN`, `CONS`, `COSI`, `DIFF`, `DIVB`, `DIVI`, `EQUA`, `LOGE`, `LOGT`, `MAXX`, `MINN`, `OPGT`, `OPLT`, `OPVA`, `OSUM`, `PROB`, `PROD`, `QSUM`, `RECI`, `SQRT`, `SUMM`, `SINE`, `TANG`, `ABGT`, `ABLT` |
| 评价函数控制 | 8 | `BLNK`, `DMFS`, `ENDX`, `GOTO`, `OOFF`, `SKIN`, `SKIS`, `USYM` |
| 相对照度/有效 F/# | 2 | `RELI`, `EFNO` |
| ZPL 宏优化 | 1 | `ZPLM` |
| 用户自定义 | 1 | `UDOC` |

实现要求：

- 数学和控制操作数在稳定的行号模型上执行，支持向前/向后范围、条件跳转、跳过和关闭行。
- 除零、非法对数/反三角输入、循环跳转和越界行引用必须产生确定且可诊断的错误。
- `UDOC` 通过受限扩展提供程序执行。`RELI/EFNO` 是内置分析操作数，参数依次为 `Samp/Wave/Field/Pol?`，不得再归入用户扩展。

### 官方名称、未用名称与定义待核实项

BIPF、COSA、HACG、QOAC、TRAN 在官方手册明确标为 Unused，只做兼容保存。OGSS、SPHD 在官方 API 枚举中存在，但公开计算定义尚未核实，不能猜测实现。

MNAI、MXAI、REQS、RRET、TSAG 都有公开定义，现已分别接入受限路径；RRET 的原生数值及应力双折射仍待完成。它们不是“仅有名字”的同一类。HYLD 在分类页面有定义，字母表遗漏不能推断该操作数不存在。

## 明确排除

### 非序列物体数据约束

以下 23 个分类条目属于非序列物体数据，不在本轮顺序模式操作数范围内：

`FREZ`, `NPGT`, `NPLT`, `NPVA`, `NPXG`, `NPXL`, `NPXV`, `NPYG`, `NPYL`, `NPYV`, `NPZG`, `NPZL`, `NPZV`, `NSRM`, `NTXG`, `NTXL`, `NTXV`, `NTYG`, `NTYL`, `NTYV`, `NTZG`, `NTZL`, `NTZV`

### 非序列光线追迹和探测器

以下 15 个非序列或遗留分类条目不在本轮范围内：

`NPAF`, `NSDC`, `NSDD`, `NSDE`, `NSDP`, `NSLT`, `NSRA`, `NSRD`, `NSRM`, `NSRW`, `NSST`, `NSTR`, `NSTW`, `REVR`, `RSNC`

`NSRM` 同时出现在两个非序列分类中，因此非序列/遗留排除集共有 37 个唯一代码。此前记录称 `NSRW/NSTW` 只出现在非序列 MFE、其余 35 项也会由顺序 MFE 返回；本次未重做运行时可选集合采集，仍依据公开类别排除。

### 当前 API 不可选择或非真实代码

`INGT/INLT/INVA` 是误读族名产生的非真实代码；`OMMI/OMMX/OMSD` 不存在于 2026 R1 ZOS-API 枚举；`UDOP/XDGT/XDLT/XDVA` 保留于官方枚举；此前运行记录称它们在新建顺序 MFE 中不可选，本次未独立重验此运行时状态。这 10 项不进入当前注册表。

### 废弃操作数

参考目录明确标记的 `PnGT`、`PnLT`、`PnVA` 操作数族不实现，不导入为可执行操作数。导入旧文件时应给出包含代码和行号的兼容性诊断。

## 当前基线与差距

截至 2026-10-03，`ZemaxOperandRegistry` 注册 383 个与官方 2026 R1 API 名称相符的顺序兼容代码，`MeritFunctionCatalog.Types` 另保留 `RWFE`、`FNUM`、`RADI`、`THIC` 四个本程序自定义操作数（不属于该官方 MFE 枚举）。注册表把当前已连接计算引擎的 324 个 Zemax 代码标为 `Executable`，其余 59 个标为 `CompatibilityOnly`；这只是代码级计算连接状态，不自动满足本文“支持”的九项定义。

`MeritOperandDefinition` 和 `MeritOperandSnapshot` 现在独立保存 `Int1`、`Int2` 与 `Data1`–`Data4` 及可选 `Data5`/`Data6` 原始槽位，并保存逐行 `CompatibilityOnly` 状态。`ZemaxOperandDescriptor` 进一步记录槽位名称、引用类型和单位，用于区分 `Int2` 是波长、终止表面、行引用还是普通整数。Application 合同把这些描述符、基础六槽及可选第七/八槽直接发布给桌面编辑器；编辑器按当前行显示参数名与单位，把 `Unused` 和兼容只读槽位锁定，并在保存时保持原始槽位为权威数据，再由描述符恢复 Workbench 类型化字段。ZMX 导入器除已有类型化分支外，会识别全部 383 个目标代码并将尚无参数语义的行禁用保留；STAROPT 往返保持原始槽位。即使外部调用强行启用兼容行，评价仍返回不可执行错误和无限贡献，不能成为成功零值。单行原始整数或数据槽位最多 16 项，数据必须有限。

参考镜头 `[MS-L7](10X大NA大视场).ZMX` 的 103 行继续按源顺序全部进入内存；其中 `TRAR` 使用现有类型化光线分支，`TTHI` 按起止表面计算轴向范围厚度，`REAR` 按实际光线位置计算径向坐标，`RANG` 按实际光线方向角计算弧度值，`CONS`、`SINE`、`COSI`、`TANG`、`ASIN`、`ACOS`、`ATAN`、`ABSO`、`SQRT`、`RECI`、`LOGE`、`LOGT`、`SUMM`、`PROD`、`DIVB`、`DIVI`、`DIFF`、`EQUA`、`MAXX`、`MINN`、`OSUM`、`PROB` 和 `QSUM` 已接入有序评价函数上下文。`SUMM/PROD/DIFF/DIVI` 按 Zemax 双行引用求值，`DIVB/PROB` 按 `Int1` 前序行和 `Data1` Factor 求值，`MAXX/MINN/OSUM/QSUM/EQUA` 按闭区间行范围求值，`EQUA` 把 Target 解释为相等容差且贡献为 `|Weight| × Value²`，三角函数 Flag 按 Zemax 的弧度/角度语义处理，`LOGE/LOGT` 对非正输入返回 0。常见镜头/厚度/一阶/玻璃数据束已完成定义级可执行接入：`CTGT/CTLT/CTVA`、`ETGT/ETLT/ETVA`、`FTGT/FTLT`、`STHI`、`TTGT/TTLT/TTVA`、`TTHI/TGTH`、`MNCA/MXCA/MNEA/MXEA/MNCG/MXCG/MNEG/MXEG/MNCT/MXCT/MNET/MXET`、`XNEA/XXEA/XNEG/XXEG/XNET/XXET`、`CVGT/CVLT/CVVA/MNCV/MXCV`、`COGT/COLT/COVA`、`MNSD/MXSD`、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、`EFFL/EFLX/EFLY/ENPP/EPDI/EXPP/EXPD/ISNA/ISFN/SFNO/WFNO` 以及 `PMAG/PETZ`。边界操作数采用 Zemax 风格“满足时返回目标值、越界时返回实际值”；`MNIN/MXIN` 在 `Surf1..Surf2` 范围内约束玻璃 d 线 Nd，`MNAB/MXAB` 约束玻璃 Vd，空气、真空与反射空间不参与；`POWR` 按标准折射面 `(n_after − n_before) / Radius` 计算表面光焦度，平面返回 0，非标准面或反射面明确报错。`TTGT/TTLT/TTVA` 按官方定义计算指定表面至下一表面、指定边缘方向处的总厚度，不再误用系统总长。`DIMX` 已在第十一批按 `Field/Wave/Absolute` 接通指定视场、轴上参考及百分比/绝对长度上限，限定有焦模式；`EFNO/RELI` 在第二十七批按本地 `Samp/Wave/Field/Pol?` 槽位接通，原生导入保持只读。上述新增路径仍必须经过 Zemax/ZOS-API golden 数值对照后，才能标为完整兼容。

有序评价控制流已接入 `GOTO`、`ENDX`、`OOFF`、`SKIN`、`SKIS` 和 `USYM`：`GOTO` 仅允许跳向函数内的后续行，被跳过的行不进入引用上下文；`ENDX` 终止后续评价；`OOFF` 保留为零贡献惰性行；`SKIN/SKIS` 按系统旋转对称性选择是否跳转，`USYM` 在整份评价函数中强制采用对称分支。非法向后或越界跳转返回明确错误。对称检测只认可可证明的同轴旋转对称几何、坐标和孔径，不确定类型保守判为非对称；对称分支已通过本机 OpticStudio 2026 R1 ZOS-API 贡献值探针验证。

`[MS-L7]` 的 103 行已通过本机 OpticStudio 2026 R1 ZOS-API 采集为可重复 golden：源 SHA-256、行顺序和 400 余个活动参数槽已锁定；除当时禁用只读的 `DIMX` 外，当时 82 个可执行数值行通过对照，包括 63 个高 NA `TRAR` 行，以及 `RANG/SINE`、两行 `TTHI`、`OPLT`、`CTGT`、`EFFL`、`PMAG/CONS/DIVI`、两行 `REAR`、`PETZ` 和各项范围约束。由此修正了 ray aiming 传播、`TRAR` 默认像面与 `REAR` 物面零号面的分类型语义、`PMAG` 所选波长近轴像面、相邻表面各自半口径边厚、`PETZ` 像方曲率符号和 `TTHI/TGTH` 包含终止端点语义。这只完成该固定系统中已执行行的数值闭环；其余操作数和其它系统仍必须经过独立 Zemax/ZOS-API golden 后才能标为完整兼容。

补充说明：本次修正仅覆盖以下路径中的像面语义修正：

- 非连续 `Surface.Number` 场景下 `OPDX` 的 `Surface=0` 解析；`MECS/MECT` 的 Int1 不使用，计算当前像面，不按旧 Surface 别名选面；
- RMS 主光线参考路径（如 `RSCH/RSRH`）在图像语义下的单条和混排一致性；
- 本修复范围内，`RayAimingEnabled` 与追迹结果缓存键的联合键控，避免像差、波前及主光线在同一批次内互相污染。

同时，上述行在 `Surface=0` 与显式像面号下结果一致，并已按本次修复统一对应缓存目标面。该表述不代表其它运行场景下对任意光学模型修改后的通用缓存安全性结论。

本机实测还校正了已执行项的槽位：`RSCE/RSCH/RSRE/RSRH` 使用 `Ring/Wave/Hx/Hy`；`MECS/MECT` 的第一个参数为空；`CT*/CV*/CO*` 只使用 `Surf`；`ET*/TT*` 的 `Mode` 位于 `Data2`，`FT*` 的 `Mode` 位于 `Data2`，`STHI` 的 `Mode` 位于 `Data3`；中心厚度范围、半口径范围和玻璃范围项只使用 `Surf1/Surf2`；`POWR` 使用 `Surf/Wave`；行边界操作数只使用 `Op#`；`DIVB/PROB` 使用 `Op#/Factor`，其中 Factor 位于 `Data1`；`EQUA/OSUM/QSUM` 使用 `Op#1/Op#2` 行范围。

本次修正消除了目标目录代码静默丢弃、编辑保存时原始槽位被固定友好字段覆盖的问题，并开始为已知操作数补充参数描述符，但没有把“383 项可无损显示”扩大宣称为“383 项完整 Zemax 评价函数支持”。描述符的逐类型参数名、单位、校验规则和计算引擎仍需按下述实施顺序完成。

当前仍需消除以下技术债：

- 编辑器已经消费描述符并按当前行切换六个原始槽位的列名、单位和只读状态；尚未补齐全部 383 项的专用参数语义、范围、默认值和枚举选择器；
- 快照校验已经能区分 `TTHI/TGTH`、常见 `MN*/MX*/X*` 厚度范围项、玻璃范围项的终止表面槽位以及基础数学操作数的行引用、行范围和 Factor 槽位，但大多数 Zemax 操作数仍需逐类型校验规则；
- 参考文件之外的未注册代码仍可能被导入器忽略；
- 六项几何质心已具备独立束采样求值；TRAC 等依赖行分组的完整集体参考语义及高级控制仍待完善；基础数学行已具备前序行读取、Flag 角度处理、Zemax 双行/范围/Factor 区分和错误报告，且 `OPLT/OPGT/ABGT/ABLT/OPVA`、`DIVB/PROB/OSUM/QSUM/EQUA` 已可按前序行约束求值；`EQUA` 已采用 Target 容差与专用贡献语义，但仍需真实 ZOS-API golden 覆盖更多边界；
- 分析型操作数缺少统一的参数化缓存和取消边界。

2026-08-29 已完成能力真实性闸门：`CanonicalType` 对未知代码明确失败；启用的只读兼容操作数返回不可执行错误；只有禁用兼容行以及显式 `BLNK/DMFS` 才产生零贡献。未实现代码不再被规范化为 `BLNK` 或作为成功零值参与优化。

任何阶段性提交都必须在支持矩阵中标为“部分”，直至满足本文“支持”的九项定义。

## 2026-09-28 常用顺序优化扩展（部分完成）

第一批阶段记录：按光线、面形、玻璃、一阶量的优先顺序，当时把以下 **28 个**代码接入真实计算，注册状态从 **124 → 152 个 Executable**；目标目录仍为 383 个，当时 **231 个仅兼容保留**；当前总数见下方第二十四批。Executable 表示已连接计算，不等于已完成所有 Zemax 参数模式与实机数值验收。完整目录审计见 [383 项状态表](../artifacts/validation/sequential-operands-20260928/operand-audit.csv)。

| 类别 | 新增代码 | 当前计算范围 |
| --- | --- | --- |
| 实际光线 | `REAZ`, `REAA/REAB/REAC`, `RENA/RENB/RENC`, `RETX/RETY`, `RAID/RAIN`, `RAED/RAEN` | 共享顺序追迹的局部交点、出射方向、单位法线、斜率、入射/出射角；角度为度，余弦及方向无量纲 |
| 光程 | `OPTH`, `PLEN` | 含折射率与相位的累计光程差；有限物面/无穷物方参考分开，PLEN 使用主波长 |
| 面形 | `SAGX/SAGY`, `NORX/NORY/NORZ`, `NORD` | 净半口径处矢高、给定 X/Y 的单位法线、沿法线与下一面的前向求交距离 |
| 玻璃 | `GTCE`, `MNPD/MXPD` | 目录 Alpha1、指定表面范围内目录 ΔPg,F 的下限/上限约束 |
| 一阶量 | `AMAG`, `LINV`, `PIMH`, `OBSN` | 共享近轴引擎的角放大率、拉格朗日不变量、近轴焦面像高、有限物方数值孔径 |

参数槽位：光线族和 OPTH 使用 `Surf/Wave/Hx/Hy/Px/Py`；PLEN 使用 `Surf1/Surf2/Hx/Hy/Px/Py`，第二整数列不是波长。NOR 系列使用 `Int1=Surf, Data1=X, Data2=Y, Data3=Global`；NORD 不使用 Global。SAGX/SAGY、GTCE 只使用 Surf；MNPD/MXPD 使用 Surf1/Surf2。AMAG/LINV/PIMH 使用 Int2 波长，OBSN 使用主波长且无活动参数。Wave=0 在这些新增单波长操作数中选择主波长。

ZMX 导入按注册表执行状态分流，六个原始槽位、目标、权重、启用状态通过 STAROPT 容器往返。旧快照中已实现代码的 CompatibilityOnly 标记会清除，但原先禁用的行保持禁用，用户可自行启用。参数描述符、中文帮助及正常优化入口同步；SAGY 已通过实际 DLS 曲率优化回归。非法表面/波长、非有限参数、失追迹、渐晕及不可计算数据返回错误和无限贡献，不能充当成功零值。

追迹样本新增明确的入射方向与归一化前含相位光程，标量和批量路径一致；新增光线操作数只通过已有有界、精确输入和修订键控的共享追迹缓存复用，不另建跨修订样本缓存。相位交互原有微米光程贡献现转换为毫米再累计（2π 相位在 587.6 nm 下对应 0.0005876 mm）；相位梯度/方向模型未在本轮重定义。近轴场映射保留显式角斜率，修复入瞳与第一面重合时角度/像高视场主光线错误归零。

明确边界：

- `NOR*` 的法线统一为局部 +Z 朝向，Global=1 使用 Core 固定世界坐标；尚未实现 Zemax 可任意指定的全局参考面。不能将本轮法线/反射符号测试当成 Zemax 实机符号全覆盖。
- GTCE 与 MNPD/MXPD 需要玻璃目录相应元数据；缺失时明确报错，不用零、近似经验线或另一材料替代。满足上下限时返回目标值，违反时返回实际极值。
- 新增一阶量当前支持同轴旋转对称几何的折射/反射或理想薄透镜、均匀传播；偏心、非对称、相位/衍射及 GRIN 系统明确拒绝。PIMH 无有限近轴焦面时报错；OBSN 对无穷物距或物面与入瞳重合时报错，零厚度不当作无穷远。
- `SSAG/SCRV/SSLP` 的多模式、全局参考面系列、完整近轴光线族、其余高级分析及材料替换仍未在本轮补齐。新 28 项尚无单独 OpticStudio 实机 golden，不宣称完整数值等价。

定义参考：[实际光线](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)、[镜头属性](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Lens_Properties.html)、[一阶量](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/First_Order_Optical_Properties.html)、[玻璃数据](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Glass_Data.html)。这些手册定义与本项目的解析/数值测试分别使用；参考文件的捕获设置没有改成通用默认值。

验证命令与最终范围见 [本轮构建与回归](BUILD_AND_RELEASE.md#顺序操作数扩展复验2026-09-28)。已提交 Zemax 捕获只覆盖各自固定系统；冻结历史相位参考文件未修改，仅在既有测试读取处把微米换成毫米。未生成或扩充 Optiland 对照。

## 2026-09-28 第二批近轴光线与制造约束（部分完成）

第二批阶段记录：当时接入 **20 项**，可执行数 **152 → 172**，383 项目录中 **211 项 CompatibilityOnly**。当时两批累计新增 48 项；没有把待实现代码换成零值。完整状态见 [383 项审计](../artifacts/validation/sequential-operands-second-20260928/operand-audit.csv)。

| 组别 | 新增代码 | 已实现行为与参数 |
| --- | --- | --- |
| 近轴交点与方向（9） | `PARX/PARY/PARZ/PARR`, `PARA/PARB/PARC`, `PATX/PATY` | `Surf/Wave/Hx/Hy/Px/Py`；调用共享近轴追迹，局部顶点切平面交点、径向高度、归一化方向余弦与方向比 |
| 近轴法线与 YNI（4） | `PANA/PANB/PANC`, `YNIP` | PANA/B/C 使用同一近轴交点调用共享法线接口；YNIP 使用 `Surf/Wave`，取轴上 +Y 边缘光线的入射折射率、近轴入射角与高度 |
| 直径（3） | `DMVA/DMGT/DMLT` | `Int1=Surf, Data2=Mode`；所选半口径的两倍，值/下限/上限 |
| 径厚比（2） | `MNDT/MXDT` | `Int1=Surf1, Int2=Surf2, Data2=Mode`；闭区间内主波长折射率非 1 的透射空间，直径/正中心厚度的下限/上限 |
| 毛坯（1） | `BLTH` | `Int1=Surf, Int2=Code, Data2=Mode`；两面按各自半口径，每条径向轴取 200 点（含顶点、边缘），得到轴向包围厚度；Code 0/1/2/3 对应 +Y/+X/−Y/−X，4 为四轴 |
| 单片一阶量（1） | `EFLA` | `Surf/Wave`；所选面与下一面构成单片，使用夹层材料和厚度、两侧空气 n=1 的共享折射/平移矩阵求焦距 |

Wave=0 选择主波长；Mode=0 为机械半口径，Mode=1 为净半口径。Surf2=0 沿用当前范围操作数约定，指最后一个物理面；负数或反向范围报错。满足边界时返回目标值，违反时返回实际极值。六槽位及行状态通过 ZMX/STAROPT 容器往返；旧兼容行升级后仍保留原启用状态。应用帮助按近轴光线、制造尺寸与一阶量分类。

本批边界：

- 13 个近轴量及 EFLA 当前只接受共轴、旋转对称、均匀介质的透射系统。折叠反射、偏心倾斜、非对称几何、相位/衍射及 GRIN 明确报错。PARZ 在所支持模型中是顶点切平面的 0，不能当作实际光线矢高；法线在近轴交点求值。有限物面与入瞳重合、无穷物面交点、非法坐标或波长均报错。
- EFLA 需要相邻两个实体折射面；不能用理想薄透镜顶替，厚度须有限且非负。两侧介质按空气计算，与原系统周围介质无关；零光焦度没有有限焦距，明确报错。
- 径厚比不靠目录名称判断材料；反射空间不参与，折射率无效、玻璃厚度非正或范围无适用空间时报错。BLTH 当前要求夹层为玻璃、中心厚度正、两面共轴且横轴对齐；超出有限矢高定义域报错。未实现任意姿态毛坯、倒角和机械实体布尔运算。
- 本批参数槽位与数值通过工程解析测试；尚无针对这 20 项的新原生 OpticStudio 槽位捕获或逐项数值 golden，不能将定义级实现宣称为完整 Zemax 兼容。`MNRI/MXRI/MNRE/MXRE` 等需七个活动输入的代码，及 `SSAG/SCRV/SSLP` 多模式、全局参考面、高级像质和材料替换，仍在兼容保留范围。

定义参考：[近轴光线操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Paraxial_Ray_Data.html)、[近轴追迹模型](https://optics.ansys.com/hc/en-us/articles/42661756008083-Understanding-paraxial-ray-tracing)、[镜头数据约束](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Lens_Data.html)、[镜头属性](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Lens_Properties.html)、[一阶量](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/First_Order_Optical_Properties.html)。

验证：本批新增 **104 项**，与第一批及相邻回归合计 Debug/Release 各 **391/391**。包含厚透镜解析式、球面/平板/非对称毛坯、径向内部极值、有限/无限共轭、色散、坐标、错误与取消、导入保存和正式 DLS。构建、现有固定 Zemax 比较及未运行项目分别记录在 [第二批复验](BUILD_AND_RELEASE.md#顺序操作数第二批复验2026-09-28)；默认桌面二进制已更新。

## 2026-09-29 第三批入射角与基点约束（部分完成）

第三批阶段记录：当时接入 **8 项**，可执行数 **172 → 180**，383 项目录中 **203 项 CompatibilityOnly**；当时三批累计新增 56 项。完整状态见 [383 项审计](../artifacts/validation/sequential-operands-third-20260929/operand-audit.csv)。

| 组别 | 新增代码 | 已实现行为与参数 |
| --- | --- | --- |
| 入射角极值（2） | `MNAI/MXAI` | `Int1=Surf, Int2=Wave, Data1=Field, Data2=Symmetry, Data3=Data`；主光线和 ±Y、±X 边缘光线入射角最小/最大约束，或返回极值所在编号 |
| 面形参数（3） | `PMVA/PMGT/PMLT` | `Int1=Surf, Int2=Param`；读取、下限、上限约束当前 Even/Odd Asphere 的 1..8 系数，未填写的多项式系数为 0 |
| 玻璃成本（1） | `GCOS` | `Int1=Surf`；当前目录材料的 AGF `OD` 首项相对成本，无量纲，不从备注或牌号推测 |
| 包围体积（1） | `CVOL` | `Int1=Surf1, Int2=Surf2, Data2=Mode`；闭区间顶点 Z 跨度和最大所选半口径组成的圆柱体积，单位为镜头单位³，不计矢高 |
| 基点（1） | `CARD` | `Int1=Surf1, Int2=Surf2, Data1=Wave, Data2=Orientation, Data3=Data`；共享近轴矩阵返回物/像方焦距及五对基点位置 |

参数与计算边界：

- MNAI/MXAI 的 Surf/Wave/Field=0 分别遍历除物面外全部表面、全部已定义波长、全部已定义视场；Symmetry=0 使用五条光线，1 使用主光线与 ±Y，2 使用主光线与 ±X。Data=0 返回角度边界值（度，满足边界时为目标值）；1/2/3/4 返回光线/视场/波长/表面编号。光线编号 0/1/2/3/4 分别为主光线/+Y/−Y/+X/−X；视场、波长从 1 开始。当前精确相等时按波长、视场、光线、表面的遍历次序取首次出现者；该并列规则尚无原生 Zemax 对照。任何所需光线失追迹或渐晕均报错，不静默排除。只比较这些光线，不作全瞳极值搜索。
- PM 系列直接读取当前几何模型；编辑系数后立即更新，不使用 ZMX 遗留 PARM 原文。其他面型、超出 1..8 的参数或非有限系数明确报错。本批未添加系数优化变量、通用扩展参数或其它面型参数映射。
- GCOS 缺失、负值或非有限数值报错；目录明确提供的 0 有效，缺失成本不能用 0 代替。未新增材料替换或 Glass Expert。
- CVOL 的 Mode=0 机械半口径、1 净半口径，Surf2=0 沿用当前范围约定，取最后一个物理面；不包含末面的后续厚度。同一面可得零体积；无穷物面、反射、偏心和倾斜明确拒绝。它不是含矢高、倒角或布尔运算的实体体积。
- CARD 的 Wave=0 选主波长；Orientation=0 YZ、1 XZ。Data=0..11 依次为物/像方焦距、焦面、主面、反主面、节点、反节点，每对先物后像；位置分别相对 Surf1、Surf2 顶点。所选组不包含末面后的传播，考虑两侧不同折射率。当前限共轴旋转对称、均匀介质透射系统，所选组限标准球面/圆锥、平面及理想薄透镜；两种取向在该范围等效。物面起点、反向范围、无焦、非球面、反射、GRIN、相位/衍射、不一致顶点/厚度及非法参数明确报错，不以近似值填补。

上述接口均在正式共享 Core；MNAI/MXAI 按同一条光线一次保留多个目标面，复用已有修订和精确输入键控的有界缓存。八项注册、类型化槽位、中文帮助、ZMX/STAROPT 往返、旧快照迁移及应用层 CARD 终止面/波长分离已验证；旧禁用行不会自动启用。

验证：本批新增 **92 项**，含解析薄透镜、单折射面不等介质、Snell 入射角、面形实时系数、目录 OD 成本、圆柱尺寸、非法输入、取消、缓存修订和正式 CARD/DLS 优化；与前三批及材料/追迹/导入回归合计 Debug/Release 各 **499/499**。其中 CARD 12 个输出对照已有 `123456.ZMX` 的 OpticStudio 2026 R1 基点捕获通过，设置为 **440 nm、1–22 面、YZ**，绝对误差上限 **1e-6 镜头单位**（原文本六位小数）。该比较只覆盖这份捕获，不是本批八项的新原生 MFE 或全参数数值验收。

定义参考：[实际光线](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)、[参数约束](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Parameter_Data.html)、[偶次非球面](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Even_Asphere.html)、[玻璃约束](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Glass_Data.html)、[AGF 格式](https://optics.ansys.com/hc/en-us/articles/46891219767827-Zemax-AGF-material-catalog-file-format)、[镜头属性](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Lens_Properties.html)、[一阶量](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/First_Order_Optical_Properties.html)。

构建证据及未验证范围见 [第三批复验](BUILD_AND_RELEASE.md#顺序操作数第三批复验2026-09-29)。`MNRI/MXRI/MNRE/MXRE` 多输入约束、`SSAG/SCRV/SSLP` 多模式、全局参考面、高级像质和材料替换等仍仅兼容保留，未宣称实现全部 383 项。

## 2026-09-29 第四批面形导数与光线约束（部分完成）

第四批阶段记录：当时接入 **4 项**，可执行数 **180 → 184**；383 项目录中 **199 项 CompatibilityOnly**，当时四批累计新增 60 项。[当时全目录状态](../artifacts/validation/sequential-operands-fourth-20260929/operand-audit.csv)按已连接计算与兼容保留分别标注。

| 代码 | 参数槽位 | 当前计算 |
| --- | --- | --- |
| `SDRV` | `Int1=Surf, Int2=Data, Data1=X, Data2=Y` | Data=0/1 为子午/弧矢一阶矢高导数，2/3 为相应二阶导数 |
| `SCUR` | 同 SDRV | Data=0/1/2 为子午/弧矢/两者差；4/5/6 为 X/Y/两者差；8 为半径与弧矢曲率乘积的绝对值；3/7/9 为对应最大绝对值 |
| `TRAI` | `Surf/Wave/Hx/Hy/Px/Py` | 指定面局部 XY 中，所选光线相对主波长主光线的距离 |
| `BSER` | `Int2=Wave`，其余槽位不使用 | 轴上主光线在像面局部坐标中的径向距离除以系统主波长有效焦距，返回无量纲比值 |

面形计算由正式共享 `SurfaceDifferentialMetrics` 提供解析梯度与 Hessian，再投影为方向导数和法截曲率。当前支持平面、标准球面/圆锥、Even/Odd Asphere 和 XY 多项式；不使用有限差分、截断点替代或原始导入文本。X/Y 为局部镜头单位坐标，允许超出净口径但必须在面形有限可微定义域内。子午沿径向向外，弧矢在 XY 中逆时针垂直于子午；顶点约定子午 +Y、弧矢 −X。非旋转面轴上方向约定尚无原生 Zemax 对照。

SCUR 的最大值在顶点至目标的 **50 个等距点（含两端）**采样，返回绝对值最大者，不只比较边缘，也不宣称连续全域极值。SCUR 普通曲率保留符号，Data=8/9 无量纲，其余为镜头单位的倒数。SDRV 一阶无量纲、二阶为镜头单位倒数。非零 r 项造成的轴上尖点、有限定义域边界、非有限参数、双锥/环面及尚未映射面型明确报错；BFS、去除项、离轴加工坐标和 `SSAG/SCRV/SSLP` 多模式未借此标为实现。

TRAI 的 Surf=0 保留物面语义，无穷物面报错；Wave=0 选主波长。所选光线和主光线都保留 ray aiming，并通过既有有界共享追迹缓存获取，失追迹或渐晕返回错误。BSER 当前限有焦、均匀介质、平行光轴的透射标准面和理想薄透镜，允许横向偏心；倾斜、反射、非标准面、GRIN 及无有效焦距明确拒绝。它使用轴上光线，与视场编辑器首行是否离轴无关，不将该比值再做反三角变换。

本批已接入中文帮助、参数描述符、ZMX/实际 STAROPT 容器往返、旧兼容行迁移（保留禁用状态）和应用层编辑；SCUR 的 Int2 是 Data 标志，不是波长。新增 **64 项**，包括解析面形、正负曲率、内部极值、局部坐标、实时系数、跨波长主光线、偏心视轴、非法输入、取消、缓存修订和正式 SCUR/DLS 优化。Debug/Release 各 **570/570** 通过；[命令和证据](BUILD_AND_RELEASE.md#顺序操作数第四批复验2026-09-29)。

定义参考：[镜头属性操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Lens_Properties.html)、[像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)。本批参数槽位及数值仍需新增原生 MFE 捕获来完成逐项兼容验收；现有解析和工程测试不替代实机比较。未改变任何 Zemax 或冻结历史捕获资产。

## 2026-09-30 第五批焦移与色差（部分完成）

第五批阶段记录：当时新增 **4 项**，可执行数 **184 → 188**；383 项中 **195 项 CompatibilityOnly**，当时五批累计新增 64 项。[当时全目录状态](../artifacts/validation/sequential-operands-fifth-20260930/operand-audit.csv)。

| 代码 | 当前参数槽位 | 计算与符号 |
| --- | --- | --- |
| `LONA` | `Int2=Wave, Data1=Zone`，Int1 不使用 | 焦点 Z 减当前像面 Z |
| `AXCL` | `Int1=Wave1, Int2=Wave2, Data1=Zone` | 同一瞳带的焦点 Wave1−Wave2 |
| `LACL` | `Int1=Minw, Int2=Maxw`，数据槽不使用 | 正向最大子午视场的近轴主光线当前像面 Y 像高 Maxw−Minw，沿用正式倍率色差分析符号 |
| `SPCH` | `Int1=Minw, Int2=Maxw, Data1=Zone` | 该瞳带真实轴向色差减近轴轴向色差，两者均按 Minw−Maxw |

全部返回镜头长度单位。波长参数是当前波长表的序号，0 选择主波长，不自动按物理波长重新排序；交换两波长使色差符号反转。Zone=0 使用共享近轴边缘光线的焦点，0<Zone≤1 使用轴上真实光线的 Py；SPCH 的 Zone=0 返回经过有效焦点校验后的零值。LACL 使用最大视场模长沿 +Y 的近轴参考，与离散视场行方向无关，不能把当前像面像高替换为各色自己的近轴焦面像高。

计算位于正式共享 Core `ChromaticFocusMetrics`，复用 `Paraxial` 与 `TraceGenericFinalSample`；没有另一套追迹引擎或运行时解析近似。真实光线保留 ray aiming 和有界缓存语义，光学编辑后缓存按既有修订机制失效。像面平移对 LONA 按相反距离变化，对 AXCL/SPCH 的焦点差不产生变化；LACL 在当前像面求值，通常随像面移动而变化。

当前模型范围是共轴、未旋转、均匀介质的有焦透射系统，支持平面、标准球面/圆锥及平面理想薄透镜；要求非折射平面像面、有限非负顺序间距且与顶点坐标一致。仅物面的正无穷厚度代表无穷共轭，零厚度仍为有限共轭；物面与入瞳重合、空波长表、非法介质、非有限焦点、真实光线失追迹或渐晕会报错。偏心/倾斜、折叠反射、GRIN、相位/衍射、带多项式项的非球面及无焦屈光度/角度单位尚未实现，不用成功零值替代。

新增 **70 项**测试含单球面独立 Snell 焦点、近轴单面焦距、有限/无穷共轭、波长顺序与主波长选择、像面移动、符号反转、模型失败、ZMX/原生 STAROPT 往返、禁用兼容行迁移、应用参数编辑、取消、缓存复用/失效及正式 LONA/DLS 厚度调焦。相邻回归合计 Debug/Release 各 **644/644**；[命令和证据](BUILD_AND_RELEASE.md#顺序操作数第五批复验2026-09-30)。

外部数值检查复用提交的 `123456.ZMX`、OpticStudio 2026 R1 的轴向像差捕获：420/440/460 nm，各 101 个 Py=0..1 点，LONA 曲线峰值归一化 RMSE 均≤1%。这是 Workbench 当前重算与既有捕获的曲线级比较；不是本批四项的原生 MFE 槽位、全部参数或逐点精确等价验证。新项仍需要原生 MFE 捕获确认参数列和带符号约定；所有外部捕获资产未修改，未新增 Optiland 比较。

定义参考：[2026 R1 像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)。第五批结束时尚未接入 Seidel；当前状态见第六批，广义场曲及高级像质操作数仍按实际注册状态区分可执行与兼容保留。

## 2026-09-30 第六批赛德尔与佩兹伐约束（部分完成）

第六批阶段记录：当时新增 **5 项**，可执行数 **188 → 193**；383 项中 **190 项 CompatibilityOnly**，当时六批累计新增 69 项。另外修正既有 PETZ，未计入新增数量。[当时全目录状态](../artifacts/validation/sequential-operands-sixth-20260930/operand-audit.csv)。

| 代码 | 当前参数槽位 | 计算与单位 |
| --- | --- | --- |
| `SPHA` | `Int1=Surf, Int2=Wave` | 球差波前系数 S1/(8λ)，波长 |
| `COMA` | `Int1=Surf, Int2=Wave` | 彗差波前系数 S2/(2λ)，波长 |
| `ASTI` | `Int1=Surf, Int2=Wave` | 像散波前系数 S3/(2λ)，波长 |
| `FCUR` | `Int1=Surf, Int2=Wave` | 佩兹伐场曲波前系数 S4/(4λ)，波长 |
| `PETC` | `Int2=Wave`，Int1 不使用 | 佩兹伐曲率 −n像·Σ[(n后−n前)/(n前·n后·R)]，镜头长度倒数 |
| `PETZ`（修正） | `Int2=Wave`，Int1 不使用 | 1/PETC，镜头长度 |

Surf=0 取全系统累计，其他值为当前表面编号；Wave 为波长表序号，0 取主波长。λ 换算为与 Core 长度一致的 mm。四项三阶操作数返回波前系数，不能直接把原始 S1…S4 除以 λ。边缘光线以主波长光阑高度固定归一化，主光线校正至光阑中心；不改变视场或重新选择像差定义。

正式共享 Core `SeidelMetrics` 提供 `SeidelCoefficients`、逐面贡献、累计结果与佩兹伐曲率。计算由既有赛德尔报告抽取，继续使用共享 `Paraxial` 光线；报告、柱状图与评价函数共用计算。未增加另一套追迹引擎、近似回退或静态缓存。

四项赛德尔操作数及报告当前限共轴、未旋转、均匀介质的球面/平面折射系统，要求非折射平面像面、正入瞳直径与一致的有限非负顺序间距。仅物面正无穷厚度代表无穷共轭，零厚度仍为有限共轭；有限物面与入瞳重合、无效波长/折射率、光阑归一化失败和非有限结果明确报错。圆锥/多项式非球面、偏心/倾斜、反射、GRIN、理想薄透镜及相位/衍射的三阶贡献尚未实现；报告和图表返回不可用及原因，不再静默使用球面贡献或将非法值置零。

PETC/PETZ 不需要近轴光线，支持共轴均匀标准圆锥/球面/平面折射系统；物面和像面的半径不进入求和，但保留两端介质。像方折射率因子现已统一到报告与操作数；PETC=0 是有效平场约束，PETZ 在零曲率时明确报告半径非有限。反射、薄透镜、GRIN、相位/衍射和多项式非球面尚未实现。

新增 **67 项**测试覆盖正负单球面独立解析参考、波前单位与孔径/半径尺度、逐面/累计、主波长/光阑归一化、非空气像方介质、边界半径、零曲率、模型失败、有限/无穷共轭、ZMX/STAROPT 往返、兼容禁用行迁移、应用编辑、取消、报告不可用状态与正式 SPHA/DLS 半径优化。相邻回归合计 Debug/Release 各 **722/722**；[命令和证据](BUILD_AND_RELEASE.md#顺序操作数第六批复验2026-09-30)。

外部数值检查复用已提交的 `123456.ZMX`、OpticStudio 2026 R1、440 nm 赛德尔波前系数捕获：SPHA、COMA、ASTI、FCUR 的每个物理面及累计值绝对误差均≤1e−6 波长。捕获源文件与赛德尔文本夹具内容未改变；这是当前 Workbench 重算与已有捕获的表值比较，并非四项操作数原生 MFE 槽位或所有面型的完整兼容认证。PETC/PETZ 使用独立解析与报告一致性检查，尚无新增原生 MFE 数值捕获；未新增 Optiland 比较。

定义参考：[2026 R1 像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)、[赛德尔与佩兹伐定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Seidel_Coefficients.html)、[Zemax 官方员工关于 MFE 波前换算的说明](https://community.zemax.com/got-a-question-7/seidel-diagram-and-values-in-merit-function-operand-5121?sort=mostLiked)。其余畸变、广义场曲、非球面三阶修正及高级像质操作数按注册状态区分可执行与兼容保留。

## 2026-09-30 第七批全局坐标与光线扇导数（部分完成）

第七批历史记录：新增 **17 项**，当时可执行数 **193 → 210**；383 项中剩 **173 项 CompatibilityOnly**，七批累计新增 86 项。代码接通比例约 54.8%，不等于全参数验证比例或剩余工作量比例。[当时 383 项审计](../artifacts/validation/sequential-operands-seventh-20260930/operand-audit.csv)、[剩余分类明细](../artifacts/validation/sequential-operands-seventh-20260930/remaining-by-category.csv)。

| 代码 | 参数 | 当前计算 |
| --- | --- | --- |
| `GLCX/GLCY/GLCZ` | Int1=Surf | 表面顶点相对全局参考面的 X/Y/Z，镜头单位 |
| `GLCA/GLCB/GLCC` | Int1=Surf | 表面局部 +Z 轴在参考系中的 X/Y/Z 单位方向分量 |
| `GLCR` | Int1=Surf，Int2=Data | 局部到参考系的 3×3 旋转矩阵；Data=1..9 按行排列 |
| `RAGX/RAGY/RAGZ` | Surf/Wave/Hx/Hy/Px/Py | 真实交点转换到全局参考系的坐标，镜头单位 |
| `RAGA/RAGB/RAGC` | Surf/Wave/Hx/Hy/Px/Py | 交互后有向单位光线转换到参考系的方向分量 |
| `DXDX/DXDY/DYDX/DYDY` | Int1 不使用；Int2=Wave；Data1..4=Hx/Hy/Px/Py | 像面局部 X/Y 截距对归一化 Px/Py 的导数，镜头单位 |

`GlobalReferenceSurfaceNumber` 默认 1，参考面的原点和旋转均参与换算；ZMX `GLRS`、快照、STAROPT 保存和克隆保留该选择。该字段用于坐标测量，不移动实际追迹几何；查看器与场景继续使用既有内部坐标。桌面的“设为全局坐标参考面”复选框尚未开放编辑，不能据 Core 支持宣称 UI 已完成。

ZMX 坐标断点仍由既有导入器折叠进后续物理面的坐标系。本批 GLC/RAG 的活动 Surf 及 GLRS 同时映射为导入后的物理面编号；没有坐标断点时原始槽位保持一致，存在坐标断点时只转换活动引用，其余槽位和目标/权重保持。指向被折叠坐标断点或不存在表面的 GLC/RAG 行、非法 GLRS 明确拒绝导入；不静默指向另一面。旧 STAROPT 中此前丢失的 GLRS 信息无法恢复，需用来源 ZMX 重新导入；旧文件缺省字段取 1。

RAG 复用正式共享真实追迹及有界修订缓存；反射与全反射使用实际出射方向，不按 Z 顺序或法线朝向猜测。Surf=0 保留有限物面语义，正无穷物面不可作为有限交点或全局参考面；Wave=0 为主波长。无效参考、非有限坐标、渐晕、零强度及失追迹明确报错。现有 NORX/NORY/NORZ 的 Global=1 同步改为所选参考面，保持与 GLC/RAG 一致；Global=0 仍为局部法线。

光线扇导数以正式真实追迹的像面局部截距求值，主光线参考不随瞳坐标改变，因此其导数为零。使用五点四阶差分，初始步长不大于 0.001，逐次减半最多 8 次，相邻导数满足 `1e−7 + 1e−5·|当前值|` 时接受。圆瞳边缘向内单边采样，导数单位是每单位归一化瞳坐标的镜头长度；切点无可用邻域、圆瞳外、采样失追迹/渐晕及未收敛明确报错。该数值求导不构成另一套光学引擎，也不替代缺失追迹能力。

兼容行升级补充正式参数/引用校验：已实现类型且引用有效的旧行解除 CompatibilityOnly，但保留 Enabled 原值；表面/波长引用无效的旧行继续只读并可再次保存，不因注册状态改变而误升级。

新增 **122 项**测试，包含 17 项的导入/原生保存、兼容迁移、应用编辑、取消和无效引用，以及组合旋转/矩阵顺序、Snell/反射/TIR、参考面及几何编辑后缓存、零/有限/无穷共轭、坐标断点编号、同参考系法线、薄透镜导数/孔径尺度、交叉导数、圆瞳边缘、失败与未收敛、GLCZ/DXDX 正式 DLS。加前六批 722 项和既有快照校验 13 项，Debug/Release 各 **857/857**。[验证证据](BUILD_AND_RELEASE.md#顺序操作数第七批复验2026-09-30)。

这些新项目前完成定义级计算及解析/工程验证，尚无原生 MFE 槽位或逐项数值捕获。既有 `123456.ZMX`、OpticStudio 2026 R1 固定回归仍通过；未修改捕获或新增 Optiland 比较，未将新项视为完整数值兼容。

第七批结束时剩余类别：像差 27、MTF 21、实际光线 15、镜头属性 7、玻璃 1、一阶量 3、GRIN 22、高斯光束 11、镀膜偏振 10、系统修改 7、热膨胀 3；完整分类见 CSV。类别可重复，不能相加替代去重后的 **173**。TCGT/TCLT/TCVA 尚缺独立表面 TCE 数据和编辑/保存路径，不能用 GTCE 的玻璃 Alpha1 替代；更多像差、MTF/能量和专项传播仍待接入。

定义参考：[2026 R1 实际光线操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)、[元件位置操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v26102/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Element_Positions.html)、[全局参考面规则](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Global_Coordinate_Reference_Surface.html)。

## 2026-09-30 第八批光线束质心与几何点列半径（部分完成）

第八批历史记录：新增 **10 项**，当时可执行数 **210 → 220**，剩余 **163/383 项 CompatibilityOnly**；八批累计新增 96 项。此数量仅表示文档限定模式可执行，不是完整 Zemax 兼容比例。[383 项审计](../artifacts/validation/sequential-operands-eighth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-eighth-20260930/remaining-by-category.csv)。

| 代码 | 原始槽位 | 当前计算 |
| --- | --- | --- |
| CENX / CENY | Surf / Wave / Field / Pol / Samp / Unused | 编号视场的局部位置质心 |
| CNPX / CNPY | Surf / Wave / Hx / Hy / Pol / Samp | 归一化视场的局部位置质心 |
| CNAX / CNAY | Surf / Wave / Hx / Hy / Pol / Samp | 单位出射方向加权平均后，取 atan2(X 或 Y, Z)，弧度 |
| GSCE / GSCH | Ring / Wave / Hx / Hy / Unused / Unused | 高斯采样点至质心／主波长主光线的最大横向距离 |
| GSRE / GSRH | Samp / Wave / Hx / Hy / Unused / Unused | 矩形采样有效点至相同参考的最大横向距离 |

共享 Core `RayBundleMetrics` 只汇总 `SequentialRayTracer` 的真实交点、出射方向和强度，复用 `ApertureSampler`。不复制追迹、不增加光学近似回退或另一份结果缓存；相邻 X/Y 通过既有按修订、输入光线、后端及保留面键控的有界缓存复用。几何、变迹和光谱权重编辑后的当前求值已覆盖。质心和半径均使用目标表面的局部坐标，反射角保持真实出射方向；后端和追迹能力限制继续适用。

质心 Surf=0 指像面，非零号须存在；CEN 的 Field 必须为有效的一起始编号，CNP/CNA 显式 Hx=Hy=0 为轴上。Wave=0 合并所有正波长权重；单波长不受该波长在多色配置中的权重影响。束权重包括采样、变迹和正式追迹强度，矩形网格跳过被遮挡、渐晕、未到达及零强度光线；全部无效时报错。Samp 为全瞳方形网格边长 1..513，仅保留单位圆内点；Samp=1 是主光线，Samp=2 没有圆内点而报错。Pol 仅支持 0，非零明确拒绝，未声称完成偏振质心。

GS 当前仅支持正 Wave 编号；Wave=0 的合成与参考尚未验证，明确拒绝。结果是有限采样集合的最大距离，不是 RMS，也不是连续瞳孔上的严格包络。当前高斯配置为 Ring=1..32、6 方位；矩形为 `(2·Samp+1)²` 网格的圆内点，Samp=1..256。采样沿用共享 Workbench 约定，尚无 Zemax 原生网格逐点捕获。高斯采样中失追迹／渐晕明确失败，矩形允许剔除；主光线参考要求主波长主光线有效。Gaussian 半径随着采样改变而改变，不能把离散最大值当作解析边缘半径。

ZMX 为六项质心的非零 Surf 映射坐标断点折叠后的物理面；0 保持像面别名。指向折叠断点的引用拒绝导入。GS 的 Int1 是 Ring/Samp，不按面号转换。类型化参数、禁用兼容行升级、STAROPT、应用编辑、参数标签及中文帮助已接通。

新增 **76 项**测试，覆盖 10 项导入/保存/迁移/编辑/取消/非法波长，以及独立 Snell 平板位置与角度、局部旋转、视场选择、多波长权重、截瞳变迹、反射方向、薄透镜散焦与孔径缩放、质心/主光线差别、主波长色差参考、坐标断点、有限与零厚度物面、缓存与正式 GSRE/DLS。含前七批 857 项，Debug/Release 各 **933/933**；[命令与证据](BUILD_AND_RELEASE.md#顺序操作数第八批复验2026-09-30)。

本批没有新的 Zemax 原生 MFE 槽位、角质心、GS 网格或数值捕获；以上为官方定义指导下的有限模式实现与解析/工程验证。既有 `123456.ZMX`、OpticStudio 2026 R1 固定回归继续覆盖原捕获设置；未修改基线或冻结历史、未新增 Optiland 比较。MNRE/MNRI/MXRE/MXRI 仍仅兼容：其完整 Surf1/Surf2/Wave/Hx/Hy/Px/Py 参数需要扩展现有六槽链路，不能丢参接入。其余像差、MTF、材料 TCE、玻璃替换与专项传播继续待实现。

定义参考：[2026 R1 实际光线操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Constraints_on_Real_Ray_Data.html)、[像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)、[优化参考点](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Function_Reference_Points.html)。

## 2026-09-30 第九批 RMS 与峰谷波前（部分完成）

第九批历史记录：新增 **8 项**，当时可执行数 **220 → 228**，剩余 **155/383 项 CompatibilityOnly**；九批累计新增 104 项。可执行计数仅表示下述限定模式，不能当作完整 Zemax 兼容比例。[383 项审计](../artifacts/validation/sequential-operands-ninth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-ninth-20260930/remaining-by-category.csv)。

| 代码 | 当前计算 | 参考 |
| --- | --- | --- |
| RWCE / RWCH | 高斯求积 RMS 波前，waves | 质心 / 主光线 |
| RWRE / RWRH | 矩形采样 RMS 波前，waves | 质心 / 主光线 |
| MWCE / MWCH | 高斯采样峰谷波前，waves | 质心 / 主光线 |
| MWRE / MWRH | 矩形采样峰谷波前，waves | 质心 / 主光线 |

六个原始槽位统一为 `Ring 或 Samp / Wave / Hx / Hy / Unused / Unused`。Int1 是采样数而不是表面引用；Hx=Hy=0 明确为轴上，不回退到编号视场。ZMX 元数据导入、STAROPT 往返、兼容行升级、桌面编辑和中文帮助均接通。快照校验新增原始 Int2 波长引用检查，防止旧 Wavelength 字段合法却掩盖实际求值的越界波长。非法采样、视场、波长、非有限样本、全无效瞳孔或取消不会产生成功零值。

`WavefrontMetrics` 调用正式 `WavefrontEngine` 获取相对参考球面（有焦）或参考平面（无焦）的 OPD，不以原始像面累计光程的离散度代替波前。系统主波长定义主光线位置/方向参考，即使该波长不在当前单波长选择中。瞳孔追迹尊重当前 ray aiming 设置，沿用正式引擎能力；需要可计算的主光线和参考球面/平面，不以近似零值回退。

`WavefrontStatistics` 与原有 RMS 扫描共用：主光线参考去除加权活塞；质心参考通过加权正交投影去除活塞、X/Y 两方向倾斜。退化的共线采样去除可观测倾斜，不再因矩阵奇异而整组放弃去倾斜。权重为几何瞳孔积分权重；追迹强度用于筛除零强度样本，不再叠加为偏振或变迹权重。输入长度不匹配、负权重和有效样本非有限值明确报错。

RMS 的 Wave=0 使用所有正权重波长，逐波长去参考项后按光谱权重合成方差再开方；正 Wave 单色不受该波长光谱权重影响。当前多色参考处理沿用共享 RMS 分析口径，但仍待原生 MFE 多色捕获验证。PV 当前仅支持正 Wave，Wave=0 明确拒绝；取去参考后的有限采样残差最大值减最小值，不是 RMS 或连续全瞳严格极值。

高斯 Ring=1..32，每环 6 方位；出现失追迹或渐晕明确失败。矩形 Samp=1..256，使用 `(2·Samp+1)²` 网格的单位圆内点，剔除失追迹/零强度点；全部失效时报错。此为共享 Workbench 采样实现，没有把波前图捕获的专用偶数网格当作 MFE 通用默认值。未新增结果缓存，复用正式追迹的按修订/输入状态有界缓存。

新增 **63 项测试**：八项注册/导入/保存/升级/编辑/非法输入/取消；解析平面波与球面弧矢（原始像面 OPL 恒定但波前非零）；无焦平面波；加权多项式独立 RMS/PV；共线去倾斜；渐晕；多色权重与主波长参考；轴上视场、几何修订和正式 RWRE/DLS 收敛。扩展到前八批 933 项及相邻波前/RMS/无焦回归共 21 项，Debug/Release 各 **1017/1017**，零失败/跳过。[验证命令与结果](BUILD_AND_RELEASE.md#顺序操作数第九批复验2026-09-30)。

本批没有新增 Zemax 原生 MFE 槽位、网格或多色数值捕获。既有 `123456.ZMX` 的波前图、RMS 视场/离焦比较仅证明原文件、设置及 OpticStudio 2026 R1 捕获范围；资产完整性核对、当前重算和数值比较分别记录，未作截图审查或完整外部矩阵认证。未修改冻结历史，未新增 Optiland 比较。下一阶段仍有 MTF、畸变/场曲、范围入射角、RGLA 合理玻璃约束等重要缺口。

定义参考：[2026 R1 像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)、[优化参考点](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Function_Reference_Points.html)。

## 2026-09-30 第十批几何、衍射与方波 MTF（部分完成）

第十批历史记录：新增 **15 项**，当时可执行数 **228 → 243**，剩余 **140/383 项 CompatibilityOnly**；十批累计新增 119 项。可执行计数仅表示以下限定模式，不是完整 Zemax 兼容比例。[383 项审计](../artifacts/validation/sequential-operands-tenth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-tenth-20260930/remaining-by-category.csv)。

| 族及代码 | 原始槽位 Int1 / Int2 / Data1..4 | 当前计算 |
| --- | --- | --- |
| GMTA / GMTS / GMTT / GMTN / GMTX | Samp / Wave / Field / Freq / !Scl / Grid | 几何点列复 OTF；!Scl=0 乘衍射极限，其它整数不缩放 |
| MTFA / MTFS / MTFT | Samp / Wave / Field / Freq / Grid / Data Type | FFT 衍射；Data Type=0 模值、1 实部、2 虚部、3 相位（度） |
| MTFN / MTFX | Samp / Wave / Field / Freq / Grid / Unused | 两方向衍射调制的最小/最大值 |
| MSWA / MSWS / MSWT / MSWN / MSWX | Samp / Wave / Field / Freq / Grid / Unused | FFT OTF 生成的方波传递函数 |

A/S/T/N/X 依次表示子午与弧矢的平均、弧矢、子午、最小、最大。当前仅 **Grid=1**；Grid=0 的原生 MFE 专用稀疏求积尚未接入，不静默更换算法。Samp=1..5 对应 Workbench 网格每边 32/64/128/256/512 点；FFT 零填充到两倍边长避免循环相关混叠，使用通用网格，未套用捕获文件的专用 FFT 显示预设。采样级别未完成原生 MFE 逐点核对。

`MtfMetrics` 复用正式 `MtfMethodEvaluator`、`SpotAnalysisEngine` 与 `DiffractionEngine`，不另写追迹或另一套 FFT。几何计算采用当前真实像面/无焦角坐标与瞳孔/变迹权重，不启用偏振透射加权；几何本身是其定义要求的模型。衍射计算由正式参考波前生成 PSF/复 OTF，并使用系统主波长参考。无新增结果缓存；修订、光谱权重、采样修改会重新求值，基础追迹缓存沿用既有有界身份规则。

Wave=0 使用正光谱权重；正 Wave 单色忽略配置中的光谱权重。零总权重、负权重、无有效光线、非法频率或编号明确失败。新增共享单频合成先在各波长独立的物理频率轴上采样复 OTF，再加权合成，最后取模值、实/虚部或相位；避免先把曲线重采样到共同网格、再取指定频率导致的双重插值误差。相位在 Core 中为弧度，操作数出口转为度。

方波复用共享 Coltman 奇次谐波展开，最多 999 次谐波，超过频率网格范围的谐波截断。多色每个谐波先合成复 OTF 后取幅值；有限带宽/有限谐波下仍有采样误差，不能称为原生方波算法逐点等价。它不是正弦 MTF 的别名。

几何 Field 必须为正编号。FFT/方波 Field=0 表示轴上去 OPD 的无像差瞳孔响应，保留实际孔径/变迹；正编号使用实际定义视场。有焦频率为 cycles/mm，无焦为 cycles/mrad；其它 Zemax 显示单位尚未适配。Grid=0、惠更斯 MTH* 的七个参数及多配置组合继续待实现，不能由 Core 已有相应分析推断其 MFE 操作数已完成。

原始六槽、目标/权重、禁用状态、兼容行升级、ZMX/STAROPT 及编辑器元数据和帮助均接通。快照对原始 Data1 为 Field 的行增加整数/范围校验，旧 Field 别名不能掩盖非法原始字段。两项旧 MNAI/MXAI 往返夹具的分数视场改为有效整数，并补充非法字段仍被拒绝保存和保持兼容只读的测试。

新增 **108 项测试**；独立离散傅里叶薄透镜散焦、圆瞳衍射与方波参考、子午/弧矢聚合、不同频率轴的复数精确合成、相位角度、DC/截止、多色零权重、全遮挡、无焦单位、采样/几何修订、格式往返及 GMTA/DLS 均覆盖。加上前九批 1017 项与另外纳入的 MTF/PSF 相邻回归 48 项，Debug/Release 各 **1173/1173**。[验证命令与记录](BUILD_AND_RELEASE.md#顺序操作数第十批复验2026-09-30)。

已有 Zemax `123456.ZMX` 惠更斯 MTF、视场和离焦捕获比较继续通过，只覆盖原文件/设置/2026 R1 版本；不构成本批 FFT/几何 MFE 的新原生数值证据。未改基线和冻结历史，未新增 Optiland 比较，也未作截图审查或完整外部矩阵认证。当前 MTF 类尚余 MECA 和五项 MTH*；其它重要缺口仍有畸变/场曲、范围入射角、RGLA 合理玻璃约束。

定义参考：[2026 R1 MTF 操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/MTF_Data.html)、[字母顺序操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。

## 2026-09-30 第十一批畸变与场曲（部分完成）

第十一批阶段新增 **6 项**，当时可执行数 **243 → 249**，剩余 **134/383 项 CompatibilityOnly**；十一批累计新增 125 项。这里的可执行计数只代表以下限定模式，不是完整 Zemax 兼容比例。[383 项审计](../artifacts/validation/sequential-operands-eleventh-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-eleventh-20260930/remaining-by-category.csv)。

| 代码 | Int1 / Int2 / Data1..4 | 当前计算 |
| --- | --- | --- |
| ABCD | Ref Field / Wave / Data / Unused / Unused / Unused | 网格畸变参考矩阵；Data=0/1/2/3 对应 A/B/C/D |
| DISG | Ref Field / Signed Wave / Hx / Hy / Px / Py | 非对称放大率下的有符号矢量畸变；正 Wave 输出 %，负 Wave 输出 mm |
| DIMX | Field / Wave / Absolute / Unused / Unused / Unused | 指定视场畸变绝对值的上限约束；Absolute=0 为 %、1 为 mm |
| SMIA | Ref Field / Wave / X Width / Y Width / Unused / Unused | 矩形边缘六条主光线的 SMIA-TV 畸变百分比 |
| FCGS / FCGT | Unused / Wave / Hx / Hy / Unused / Unused | 弧矢/子午傍轴焦点相对当前平面像面的位移，mm |

`DistortionMetrics` 复用正式 `AnalysisTrace.BuildDistortionReferenceMapping`，对线性物高或角度的正切计算局部二维参考矩阵。参考 Field=0 为轴上，不能用第一个已定义离轴视场替代。`DIMX` 的 Field=0 取最大径向已定义视场坐标；它始终以轴上主光线为参考，不扫描全视场极值。满足上限返回 Target，越界返回实际绝对值。`DISG` 采用实际与预测相对像点之差的矢量长度，实际径向像高更小时取负；参考主光线返回零，其它零参考像高的百分比未定义并报错。绝对长度模式仍保留畸变正负号。

`SMIA` 当前仅支持轴上参考（Field=0 或轴上视场编号），矩形中心为轴上；离轴参考约定未完成原生验证，明确报错。X/Y Width 是全宽；以左右边高 A1/A2 和中间边高 B 计算 `100×((A1+A2)/(2B)−1)`。当前矩形必须位于已定义归一化视场 [-1,1] 内，超界明确报错。任何必需光线失追迹、渐晕或 B 为零均报错；未沿用原生分析失追迹显示零的约定，避免优化得到成功零值。

本组目前仅有焦像空间。畸变要求角度视场最大角小于 90°；实像高通过正式 Core 反求物方视场后计算，不修改原文档和共享缓存身份。ABCD/DISG 的矩阵校准需要非零视场范围和非奇异映射。`DIMX` 只有轴上场时可直接返回真实轴上主光线的零畸变。除 DISG 外 Wave=0 使用当前主波长；DISG 的 Wave 必须非零，负值通过类型化 `SignedWavelength` 保留单位选择，非法极小整数不会溢出。Wave=0 可作为未完成编辑行保存，但求值明确报错，不能静默选主波长。

`FieldCurvatureMetrics` 提取并共享既有正式场曲傍轴焦点计算。操作数采用瞳孔扰动 ±1e−5，子午方向沿物方实际径向（角度视场按 tan(角度) 定向），弧矢方向正交；轴上分别使用 Y/X。结果计入当前像面离焦。当前限未倾斜的平面像面，支持像面绕 Z 旋转；倾斜/曲面像面、无焦、平行光线、失追迹和渐晕明确失败。既有分析扫描保留其明确配置的步长和历史平行焦点行为；操作数采取严格报错。

原先参考场位于视场边缘时的一阶单边差分造成矩阵偏差，已在共享网格畸变计算中改为三点二阶单边差分；内部点继续中心差分。平面折射的独立 Snell 解析值验证了矩阵、非对称放大率、百分比/绝对畸变和 SMIA。参数元数据、中文帮助、ZMX/STAROPT、旧兼容行升级、编辑往返、非法原始 Field/Wave 校验、修订重算、取消以及正式 FCGT/DLS 像面调焦均覆盖。

新增 **94 项测试**，加前十批 1173 项与纳入的场曲页脚、相邻界面/兼容行回归 14 项，Debug/Release 各 **1281/1281**。这包含对已知残差的回归监控，不是所有数值精度目标达标。[验证命令与记录](BUILD_AND_RELEASE.md#顺序操作数第十一批复验2026-09-30)。

外部数值证据采用已提交 `123456.ZMX` / OpticStudio 2026 R1 的场曲和畸变分析数据。测试显式设置该捕获的角度视场和三条波长，各取轴上、半视场和最大视场，共每项九点；DISG/DIMX 使用 2e−5 百分点目标，FCGS/FCGT 使用 1e−5 mm 目标。DISG、DIMX、FCGS 达到该局部比较目标；**FCGT 最大残差约 2.15333e−5 mm，状态仍为 Difference**。步长扫描 1e−2..1e−6 未消除该差异，来源尚未确定；未改正式精度目标。另设 3e−5 mm 仅防止当前已知残差扩大，不能用它声称原生数值等价。详细逐点输出保存在 TRX，最大误差和状态在 [验证摘要](../artifacts/validation/sequential-operands-eleventh-20260930/verification.json)。

基线选定九份资产与 HEAD 内容一致；未新增原生 MFE 槽位/数值捕获，未作截图审查、完整外部矩阵或全产品/实验室全量验证，未修改冻结历史或新增 Optiland 比较。`DIST` 的单面三阶与零号面系统语义仍待完成并独立验证；`DISA/DISC/ZERN`、范围入射角、RGLA、惠更斯 MTH* 等仍未作为已完成项计入。

定义参考：[2026 R1 像差操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Aberrations_optimization_operands_by_category.html)、[网格畸变](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Grid_Distortion.html)、[场曲和畸变](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)。

## 2026-09-30 第十二批光线角度约束与工作 F 数（部分完成）

第十二批阶段新增 **5 项**（MNRI、MNRE、MXRI、MXRE、TFNO），修正已有 SFNO，**249 → 254 可执行、129/383 仅兼容保留**，十二批累计新增 130 项。可执行计数仅表示下列已实现模式；四个角度范围的原生 ZMX 扩展导入仍未完成。[383 项审计](../artifacts/validation/sequential-operands-twelfth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twelfth-20260930/remaining-by-category.csv)。

| 代码 | 当前参数 | 已实现行为 |
| --- | --- | --- |
| MNRI / MXRI | Surf1 / Surf2 / Wave / Hx / Hy / Px / Py | 对所选光线在含首尾面的范围内累计入射角下限/上限违例 |
| MNRE / MXRE | 同上 | 对正式追迹的出射方向计算出射角，累计各面边界违例 |
| TFNO / SFNO | Field / Wave / 四个保留槽 | 真实子午/弧矢工作 F 数；已有 SFNO 不再返回近轴系统 F 数 |

角度由共享正式追迹的 `IncidentDirection` 或 `Direction` 与该面局部单位法线求得，取正角度，单位度；折返反射不从坐标先后或 Z 顺序推测。Wave=0 使用当前主波长，非零按一基编号。视场坐标须在 [-1,1] 内，瞳孔坐标须在单位圆内。范围按物理表面列表包含两端；倒置/不存在范围、无穷远物面的有限交点请求、失追迹、渐晕、非法数字和取消均不会产生成功零值。

范围约束按官方边界规则累计每个面，而不是只取极值：上限返回 `Target + Σmax(0, angle−Target)`，下限返回 `Target − Σmax(0, Target−angle)`。例如两个入射角为 30° 和 19.471220634°，下限目标 40° 时返回 9.471220634°。满足全部边界则返回 Target。此前其他范围操作数是否全部采用相同累计约定不属于本批验收，不能据此声称全目录边界数值等价。

七槽经过 Core 原始数组、应用 DTO、编辑器已有第七列与 STAROPT 保存；原始 Int2 是终止面，Data1 才是 Wave。快照校验分别检查，不让旧别名掩盖无效引用。新增可选 Data5 不改变 STAROPT 容器格式。**原生 ZMX 扩展行的序列化位置尚无已提交捕获验证，四个范围代码导入后保持禁用只读，参数尾部保存在原记录文本中；不猜测第七参数位置，也不补零后执行。** 可在编辑器新建完整七参数行；缺少第五个原始 Data 值的旧兼容行继续只读。完整有效七参数旧行可升级，保留禁用状态。

SFNO/TFNO 共用 `DiffractionEngine.WorkingFNumbers`；当前支持有焦像空间的轴上及 Y 方向视场。Field=0 为轴上，Wave=0 为主波长（本产品明确的零号选择），任意 X 方位暂明确拒绝。主光线和对应 ±1 瞳边缘方向的数值孔径取算术平均，结果为 `1/(2×平均 NA)`，使用像空间折射率；忽略物理表面孔径、考虑已有视场渐晕因子和当前光线瞄准设置。操作数严格追迹全瞳，失败不会缩小瞳区代替；零/非法 NA 报错，不封顶为 10000。严格路径用归一化叉积长度求夹角正弦，避免大 F 数的小角度精度损失。共享分析入口既有缺省采样/回退行为保持其原边界。`ISFN/WFNO/FNUM` 当前原有近轴入口未在本批改为真实工作 F 数。

新增 **78 项测试**：独立平面 Snell 解析角、单面和多面边界、非零第七参数、反射/倾斜面方向、材料修订、完整 STAROPT/编辑往返、原生扩展行只读和旧行升级、非法引用/数值/取消、理想薄透镜独立方向几何、视场/渐晕/物理孔径、严格全瞳失败和大 F 数，以及 MXRI 驱动正式 DLS 优化。一个旧厚度夹具全瞳真实光线无法通过，SFNO 现在明确报错，其测试不再断言与 ISFN 相等。

Debug/Release 各 **1382/1382**，含新增 78 项与相邻回归；默认输出已更新。[验证记录](BUILD_AND_RELEASE.md#顺序操作数第十二批复验2026-09-30)。没有新增原生 MFE 数值或槽位捕获，不宣称完整 Zemax 等价。既有固定 `123456.ZMX` 分析对照继续运行：FCGT 最大残差约 2.15333e−5 mm 仍高于 1e−5 mm 目标，状态 Difference；本批没有消除或放宽该差异。选定基线资产保持不变，未作新截图审查、完整外部矩阵或全产品/实验室全量验证，也未新增 Optiland 对照。

定义来源：[2026 R1 操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[工作 F 数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Working_F.html)、[Zemax 官方人员说明范围边界累计](https://community.zemax.com/people-pointers-9/do-you-find-weird-behavior-for-mnca-mnea-mneg-mneg-mxre-mnri-3641)。

## 2026-09-30 第十三批惠更斯 MTF（部分完成）

新增 **5 项**：`MTHA/MTHS/MTHT/MTHN/MTHX`，**254 → 259 可执行，124/383 仅兼容保留**；十三批累计新增 135 项。计数表示下述受限模式可求值，不是完整 Zemax MFE 兼容认证。[383 项审计](../artifacts/validation/sequential-operands-thirteenth-20260930/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-thirteenth-20260930/remaining-by-category.csv)。

| 参数槽 | 参数 | 当前支持 |
| --- | --- | --- |
| Int1 | Samp | 1=32×32，2=64×64；瞳孔和像面使用相同大小 |
| Int2 | Wave | 0 为全部正权重波长；正数为单色编号 |
| Data1 | Field | 有效的正视场编号，0 不代表衍射极限 |
| Data2 | Freq | 有限非负空间频率，cycles/mm |
| Data3 | Pol | 仅 0；完整偏振求值未接入 |
| Data4 | All Conf | 仅 0；多配置合成未接入 |
| Data5 | Ima Delta | µm；0 按当前所选视场、最长有效波长调用共享引擎计算间隔 |

五种输出分别为两个方向的平均、弧矢、子午、最小和最大 MTF。正式 `MtfMetrics.EvaluateHuygens` 复用已有 Huygens PSF、光谱 PSF 合成和频率图路径；不是 FFT MTF 别名，也没有新的实验室或 Python 光学引擎。按现有共享分析实现，光谱权重乘逆波长平方后先叠加 PSF，再求复变换模值；采用 2N 变换与端点跨度三次插值。该约定有固定文件的分析捕获回归证据，尚无原生 MFE 数值证据，不作为所有系统的 Zemax 通用规范。通用分析构造默认值未改变。

当前仅有焦像空间。32/64 采样使用现有直接 PSF 预算，更高 Samp 明确报错，不静默降采样。Pol 非零不转用标量偏振近似，All Conf 非零不忽略。频率高于所生成的网格上限返回零；没有实现原生 MFE 的全部采样充分性检测。非有限/负频率或间隔、无法表示的正间隔、无有效视场/波长、无正光谱权重、无有效 PSF 能量、取消均明确失败。正 Wave 单色不继承零光谱权重；权重归一化使用副本，不改模型。当前相邻五种操作数仍分别进行 PSF 求值，没有新增跨行 PSF 缓存。

七槽类型元数据、编辑帮助、DTO 和 STAROPT 均接通，第七列是图像间隔；Target/Weight 位置不变。完整且引用有效的旧 STAROPT 兼容行可以升级，保留禁用状态；缺少 Data5 的旧行继续只读。**五种 MTH 的原生 ZMX 扩展参数位置未获得已提交捕获验证，导入后保持禁用兼容行，完整原始参数尾部保存在 Comment 中；不能声称原生扩展导入已实现。**

新增 **67 项测试**：七槽编辑/帮助/保存/启用求值、旧行升级与缺参只读、独立 DFT 校验五种方向和多色先合成、权重缩放不变性、自动/显式间隔、32/64 采样、零频/超频、非法参数/模式、孔径遮挡、取消、模型厚度变更后重算，以及 MTHA 驱动正式 DLS 恢复调焦。独立 DFT 只用于测试，不供应运行时结果。

验证分层：

- **基线完整性**：验证记录列出的既有捕获文件逐字节匹配提交版本，没有新增或改写 Zemax/Optiland 捕获。
- **当前重算**：默认 Debug/Release 各 **1449/1449**（前十二批 1382 项 + 本批 67 项）；默认 Core/Application/App 二进制已更新。
- **数值比较**：针对固定 `123456.ZMX` 的已有惠更斯 MTF 分析捕获，显式使用捕获的 32×32、复色光和对应间隔，选择视场 1/3/5、频率点 0/30/90，共 18 个方向值，最大绝对差 **0.011900434932046483**，低于本测试分析级回归预算 **0.03**。预算只是有限点回归门槛，不是新的全系统精度目标，也不是原生 MFE 认证。既有整条分析曲线 NRMSE/相关性测试继续运行。`FCGT` 原有最大残差 **2.1533288104098533e−5 mm** 仍超过 **1e−5 mm** 精度目标，状态保持 Difference。
- **截图审查**：本批没有截图审查或原生桌面逐页验收；也未运行全产品/实验室全量测试、完整外部比较矩阵或安装包验收。

[构建与验证](BUILD_AND_RELEASE.md#顺序操作数第十三批复验2026-09-30)。参数定义来源：[OpticStudio 2026 R1 官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。`STRH/CEHX/CEHY` 等 PSF 指标仍仅兼容保留，不能因已有 Huygens 引擎就标记为完成。

## 2026-10-01 第十四批近轴高斯光束（部分完成）

新增 **6 项**，**259 → 265 可执行、118/383 仅兼容保留**；十四批累计新增 141 项。此前的受限模式与外部精度缺口仍有效，本次没有达到用户要求的“全部完成”。[383 项审计](../artifacts/validation/sequential-operands-fourteenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-fourteenth-20261001/remaining-by-category.csv)。

| 代码 | 返回物理量 | 单位 |
| --- | --- | --- |
| GBPD | 发散半角 | radians |
| GBPP | 从输出束腰到指定表面的有符号距离 | mm |
| GBPR | 该面的相位曲率半径 | mm |
| GBPS | 该面的光束半径 | mm |
| GBPW | 输出束腰半径 | mm |
| GBPZ | 输出瑞利范围 | mm |

共同参数为 `Surf/Wave/UseX/W0/S1toW/M²`：Surf 是面 1 及之后的表面，求值位于该面作用之后、其后厚度传播之前。Wave 按一基编号，0 使用当前主波长。UseX=0 选 Y，非零整数选 X；当前支持的旋转对称范围内两方向相同。W0 为嵌入 TEM00 模的正束腰半径，S1toW 为面 1 到输入束腰的有符号距离，负值代表束腰在左侧。M² 必须至少为 1。

共享 `Paraxial.GaussianBeam` 使用现有正式近轴矩阵传播复光束参数，考虑输入/输出介质折射率。束腰、尺寸和发散半角乘 `sqrt(M²)`；瑞利范围、位置和相位曲率半径保持嵌入模结果。M² 不通过改变波长或输入瑞利范围模拟。忽略孔径截断是近轴高斯模型的明确假设。

当前仅支持共轴、未倾斜的标准球面/圆锥、平面及理想薄透镜透射系统，传播间隔为非负有限值；非球面顶点功率、非对称面、反射及偏心/倾斜模式仍待补齐。坐标必须与厚度一致。精确束腰处相位半径为无穷；共享数据可表达该状态，GBPR 有限评价函数明确报错，不返回零值或人造有限上限。非法束腰/M²/距离/波长/表面、数值溢出和取消均失败。新代码限 Core C#，没有复制引擎到实验室。

ZMX 通用六槽、STAROPT 和编辑器往返已接通；完整引用有效的禁用兼容行可升级并保持禁用状态。参数帮助解释 W0 的嵌入模语义和 M² 缩放，不能将 UseX 误认成 Field。

新增 **67 项测试**，包括自由传播解析解、独立双近轴光线矩校验薄透镜、折射率变化、M² 缩放、两个方向、原生保存/六槽导入、参数编辑、非法输入、束腰奇点、物面零厚度与无穷厚度、主波长及材料变更、孔径假设、未支持模型和坐标检查，以及 GBPP 驱动正式 DLS 调焦。默认 Debug/Release 各 **1516/1516**（此前 1449 项 + 本批 67 项），默认桌面输出已更新。[验证记录](BUILD_AND_RELEASE.md#顺序操作数第十四批复验2026-10-01)。

外部验证边界：未新增高斯束原生 MFE 捕获，解析/功能测试不等于 Zemax 数值对齐；既有固定 `123456.ZMX` 分析回归继续运行，Huygens MTF 18 点最大绝对差仍为 0.011900434932046483，处于 0.03 分析级回归预算内；FCGT 仍约 2.15333e−5 mm、超过未改变的 1e−5 mm 目标。选定已提交基线资产未改变；无新截图审查、全产品/实验室全量测试、安装包验收或新 Optiland 对照。

定义来源：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[近轴高斯光束与嵌入模/M² 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Paraxial_Gaussian_Beam.html)。

## 2026-10-01 第十五批表面热膨胀系数（部分完成）

新增 TCVA、TCGT、TCLT 三项，**265 → 268 可执行，115/383 仅兼容保留**；十五批累计新增 144 项。[383 项审计](../artifacts/validation/sequential-operands-fifteenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-fifteenth-20261001/remaining-by-category.csv)。整体完成目标仍在进行。

TCVA 读取指定面的表面/隔圈热膨胀系数，TCGT/TCLT 分别约束下限/上限。唯一活动参数是 Int1=Surf，目标及返回值单位均为 10⁻⁶/°C，保留负系数。满足边界时返回目标、贡献为零；违反时返回实值，贡献按现有权重规则计算。它与玻璃目录 Alpha1/GTCE 独立；改材料不覆盖表面 TCE。此批仅提供属性与评价，未宣称温度变化、热膨胀追迹或新增 TCE 优化变量。

`OpticalSurface.ThermalExpansionPpmPerC` 贯通克隆、快照、STAROPT、JSON、DTO、表面服务及撤销/重做。原镜头表 TCE 列可以编辑，宽度和布局保持。非数值/非有限输入保留错误边框、提示和可访问性帮助，不发布成功更新；Esc 还原，合法值提交一次修订。新建面默认 0；旧项目缺项或尚未验证的 ZMX 源数据为 null，显示“未提供”，求值明确失败。

ZMX 的原生表面 TCE 记录尚未取得验证，因此没有猜测文件关键字/槽位；这三类导入 MFE 行保持禁用只读，并保留原参数、目标和权重。保存后重载只有在引用面具有明确 TCE 且参数有效时才升级，仍保留原禁用状态。旧 DTO 调用方省略追加属性时不会清除现有值。

本批新增 **37 项**：36 项数值/贡献、未知与零、负值、材料独立性、原生文件往返、兼容升级、元数据编辑、撤销和非法输入测试，加 1 项真实控件输入回归。默认 Debug/Release 各 **1622/1622**（此前 1516 项 + 新增 37 项 + 新纳入相邻回归 69 项）。原生 TCE 数值及文件记录兼容未验证；已有 FCGT Difference 与 Huygens 分析预算边界不变。无新截图、全产品/实验室全量、安装包或 Optiland 对照。[验证记录](BUILD_AND_RELEASE.md#顺序操作数第十五批复验2026-10-01)。

定义参考：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[TCE 数据单位与玻璃/非玻璃来源](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Adding_TCE_data.html)。

## 2026-10-01 第十六批惠更斯 PSF 测量（部分完成）

新增 STRH、CEHX、CEHY 三项，**268 → 271 可执行，112/383 仅兼容保留**；十六批累计新增 147 项。[383 项审计](../artifacts/validation/sequential-operands-sixteenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-sixteenth-20261001/remaining-by-category.csv)。全量完成目标仍在进行。

| 代码 | 参数 | 计算 |
| --- | --- | --- |
| STRH | Samp/Wave/Field/Pol/All Conf | 使用半默认图像间隔；返回 PSF 网格峰值与理想峰值之比 |
| CEHX、CEHY | Wave/Field/Pol/Pupil Samp/Image Samp/All Conf | 默认图像间隔；返回像面局部绝对 X/Y 质心，mm |

共享 `HuygensPsfMetrics` 复用正式 PSF 积分和 MTF 的多波长强度合成，按光谱及逆波长平方权重合并。Strehl 在合成后取峰值，再除以同权重理想峰值；不平均单色 Strehl。质心在主光线交点的真实像面切平面计算，再转换至像面局部坐标。共享切平面构造从现有衍射代码抽出，未复制光学追迹算法。波长筛选同时供 PSF 与 MTF 使用，保留单色忽略光谱权重、多色零权重剔除及整体权重缩放不变的语义。

Wave=0 多波长，Field 为正视场编号；当前仅有焦、Pol=0、All Conf=0。STRH 同时使用 32/64 的瞳孔与像面；质心两采样代码可独立选 1..4（32..256），但组合必须满足共享 100,000,000 次直接 PSF 工作预算，超限明确失败，不会降采样。自动图像间隔沿用共享默认公式，取最长有效波长；孔径和变迹由正式 PSF 处理。无有效能量、非法引用、无焦模式或未支持标志均明确失败。相邻 CEHX/CEHY 的 PSF 积分复用、完整偏振、多配置相干合成及原生数值等价仍待补齐。

通用六槽 ZMX 导入、原生 STAROPT、编辑器和帮助可用；快照现已按 Int1=Wave、Int2=Field 校验质心引用，禁用兼容行只在引用有效时升级。新增 **57 项测试**，覆盖单色矩统计、多色强度、半间隔峰值、绝对位置、倾斜像面、权重缩放、变迹/处方更新、独立采样、资源上限、往返和明确错误。默认 Debug/Release 各 **1684/1684**（此前 1622 项 + 新增 57 项 + 新纳入相邻回归 5 项），默认桌面二进制已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第十六批复验2026-10-01)。

选定已提交 `123456.ZMX` 基线资产保持不变，原有 PSF/截面/MTF 分析回归已复验；这些分析级检查不能替代 STRH/CEHX/CEHY 原生 MFE 数值捕获。FCGT 仍为 Difference，Huygens MTF 18 点最大绝对差仍为 0.011900434932046483、处于 0.03 分析回归预算内；精度目标未放宽。无新截图、全产品/实验室全量、安装包或 Optiland 比较。

定义参考：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[Huygens PSF 坐标与归一化](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Huygens_PSF.html)。

## 2026-10-01 第十七批批次内系统状态（部分完成）

新增 PRIM、CVIG、IMSF 三项，**271 → 274 可执行，109/383 仅兼容保留**；十七批累计新增 150 项。[383 项审计](../artifacts/validation/sequential-operands-seventeenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-seventeenth-20261001/remaining-by-category.csv)。全量完成目标仍在进行。

| 代码 | 本程序参数 | 执行范围 |
| --- | --- | --- |
| PRIM | Int1=Wave，正波长编号 | 后续操作数使用新的主波长 |
| CVIG | 无 | 清除副本所有视场现有 X/Y 渐晕压缩因子 |
| IMSF | Int1=Surface、Int2=Refocus | 0 恢复完整原像面；正编号选择中间像面；Refocus=1 追加同介质平面并近轴调焦 |

有序评价上下文按需创建独立光学副本，保留运行时材料定义；成功后才发布新状态。IMSF 恢复原像面仍保留批次内主波长/渐晕状态。普通结束、ENDX、跳过、异常或取消均不会写回原系统。状态行忽略 Target/Weight，不可作为数学行的数值输入；单行独立求值明确报错。优化器的有序值与逐行适配器保持相同活动行和零贡献语义。副本不共享原系统的光线缓存绑定，状态变更清空批次缓存，下一次评价读取当前处方。

IMSF 当前限定角度/物高视场、原光阑处或之后；Refocus=0 保持所选表面几何、材料和显式坐标，Refocus=1 仅支持共享近轴服务允许的共轴透射模型和有限正向焦点。像高视场换算、光阑前虚拟入瞳重建、无焦调焦及虚焦仍明确拒绝。CVIG 的偏移/旋转渐晕模型尚未完成。CONF、多配置状态与 FDMO/FDRE/SVIG 仍待补齐。

**导入边界**：PRIM/IMSF 的原生参数列尚缺捕获验证；ZMX 记录保留原参数、禁用且兼容只读，STAROPT 不会自动升级这些记录。应用内创建的行和原生 STAROPT 可正常执行；CVIG 无有效参数列，可导入执行。不得将本程序参数表描述为已经验证的原生列布局。

新增 **46 项测试**：主波长/渐晕的后续影响、中间像面与调焦、恢复组合、控制流、失败回滚、取消、并发隔离、原光线缓存保护、运行时玻璃保真、编辑器/项目往返、导入保留。默认 Debug/Release 各 **1730/1730**（原 1684 + 新增 46），默认桌面二进制已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第十七批复验2026-10-01)。

既有选定 Zemax 基线资产保持不变；FCGT 仍为 Difference，Huygens MTF 既有分析差异未改变。本批没有原生 MFE 数值捕获、截图、全产品/实验室全量或安装包验收。

定义参考：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[中间像面处理](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Ray_Aberration_rays_and_spots.html)。

## 2026-10-01 第十八批制造与面形统计（部分完成）

新增 VOLU、TMAS、DSAG、DSLP、DCRV 五项，**274 → 279 可执行，104/383 仅兼容保留**；十八批累计新增 155 项。[383 项审计](../artifacts/validation/sequential-operands-eighteenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-eighteenth-20261001/remaining-by-category.csv)。全量完成目标仍在进行。

| 代码 | 本程序参数 | 计算 |
| --- | --- | --- |
| VOLU、TMAS | Int1/Int2 起止面；Data2 Mode=0 机械、1 净半口径 | 闭区间每个面到下一面围成的体积；单片起止面相同；cm³ / g |
| DSAG | Surface/Data/Samp/Off-axis/Remove/BFS | 矢高统计；mm |
| DSLP、DCRV | 同上，另有 Data5=Orientation | 方向斜率（无量纲）、法曲率（mm⁻¹）；极值点坐标为 mm |

`ElementVolumeMetrics` 在共享 Core 计算实体体积；当前支持圆形边缘的共轴无倾斜标准面及偶/奇次非球面，较小面的边缘平延伸到较大面半口径。球面使用稳定解析球冠积分，其余受支持面形采用有误差和工作预算限制的自适应积分；单位由 mm³ 换成 cm³。使用 1025 个径向位置筛查负厚度，不能把该采样检查当作任意高阶曲面的连续相交证明。VOLU 包含空气间隙；TMAS 对空气返回物理零质量，对玻璃读取目录 g/cm³ 密度。缺少/非法密度、非有限结果或未收敛均报错，不能假定密度为零或套包围圆柱。非圆/偏心孔径、不同裁切边界、反射空间、倾斜/偏心面、物面和像面端点仍未支持；GRIN 密度不采用经验常数。

`SurfaceProfileMetrics` 直接读取共享面形，斜率和曲率复用正式解析导数。按物理圆/环形/偏心圆/矩形/椭圆孔径的包围矩形建立均匀 XY 网格，并裁掉孔径外点；未显式定义孔径时使用净半口径圆。Samp=1..5 为 33..513；Data=1..8 为关于零的均方根、峰谷、最小/最大及两极值的 X/Y 坐标，并列极值取先 Y 后 X 扫描首点。此为本程序明确采用的统计语义，尚无原生数值捕获。RMS 采用缩放累加防止平方溢出。当前 Off-axis=0，Remove=0/1（原面形/先减基准球面再取导数），Orientation=0..3（子午、弧矢、X、Y）。最佳拟合球面、离轴坐标重建、方向模值、更多采样和 Data=9..16 尚未完成；相邻统计行尚未共用积分缓存。

五项的原生参数列缺少实机捕获，**ZMX 导入仍禁用且兼容只读，快照不会自动升级**；本程序创建的行可以执行，编辑器及 STAROPT 六/七槽往返已验证。新增 **74 项测试**覆盖独立平板/球冠/抛物面/多项式积分、密度、口径、单位、统计矩与极值坐标、方向、基准球面移除、溢出、取消、更新、非法参数及往返。默认 Debug/Release 各 **1804/1804**（此前 1730 + 新增 74），默认桌面输出已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第十八批复验2026-10-01)。

选定提交基线资产保持不变；FCGT 仍为 Difference，既有 Huygens MTF 分析差异保持原值。没有新增原生 MFE 捕获、截图、全产品/实验室全量、安装包或 Optiland 比较。

定义参考：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[元件体积与密度](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Prescription_Data_reports_group_the_analyze_tab_sequential.html)、[表面矢高与真实孔径](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Surface_Sag.html)。

## 2026-10-01 第十九批几何能量与边缘响应（部分完成）

新增 GENC、GENF、ERFP，**279 → 282 可执行，101/383 仅兼容保留**；十九批累计新增 158 项。[383 项审计](../artifacts/validation/sequential-operands-nineteenth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-nineteenth-20261001/remaining-by-category.csv)。全部完成目标仍在进行。

| 代码 | 本程序参数顺序 | 返回量 |
| --- | --- | --- |
| GENC | Samp / Wave / Field / Type / Refp / Fraction / No Diff Lim | 达到给定分数的最小距离，µm |
| GENF | 同上，Fraction 槽改为 Distance | 给定距离内的能量分数 |
| ERFP | Samp / Wave / Field / Type / Fraction / Max Radius | 达到边缘响应分数的位置，mm |

`GeometricEnergyMetrics` 通过共享顺序追迹获得像面局部坐标，使用入射瞳孔/渐晕/变迹权重，不额外按表面或体吸收改变几何统计权重。多波长先归一化光谱权重再合并光线，单色选择独立于该波长的全局权重；无正能量、非有限数值和失效主光线明确报错。Samp=1..5 使用 32..512 个瞳孔区间、端点在内的圆瞳网格；总采样受一百万光线预算限制。这是本程序明确的采样约定，尚无原生操作数采样/分位插值捕获。

Type=1 圆、2 X 狭缝、3 Y 狭缝、4 方框；狭缝与方框距离采用半宽。Refp=0/1/2/3 为主光线、加权质心、顶点、最小包围圆圆心。Middle 使用有工作预算、确定性打乱顺序的增量最小包围圆并复查包含性，不采用包围矩形中心。光线权重汇总为经验累积分布：GENF 包含距离边界上的点，GENC 返回首次达到所需分数的距离，不对绘图采样点插值。GENC 纯几何分数允许 0..1；衍射缩放时 1 对应无限距离，明确失败。

No Diff Lim=0 把几何分数乘理想旋转对称 Airy 能量，非零关闭缩放。每个波长使用轴上全瞳工作 F 数，不能以缩小瞳孔的值回退；圆积分复用正式 Airy/Bessel 实现，狭缝和方框用极角积分，逐级加密至相邻结果差不大于 1e-7，最多 16384 点，否则报错。该收敛检查是工程误差预算，并非所有振荡积分的严格误差证明。带衍射的逆分位采用有界二分。此 Airy 乘积是文档定义的近似模式，不是完整衍射传播，也不证明遮拦/非对称瞳孔的衍射精度。

ERFP 的 Type=0/1 为相对主光线 X/Y，2/3 为相对像面顶点 X/Y；Wave=0 仍用主波长主光线作为参考，即使其光谱权重为零。Fraction=0.01..0.99；正向坐标为亮侧，采用完整光斑的加权经验 CDF 逆分位。当前只接通 Max Radius=0；自定义窗口、原生直方图/插值规则、无焦单位仍未完成。本批均仅有焦、标量；不增加跨行结果缓存。

六/七槽编辑与 STAROPT 往返已验证，但原生列缺少实机捕获，**三项 ZMX 导入仍禁用兼容只读，快照不自动升级**。新增 **62 项测试**覆盖独立离散瞳孔、分位与单位、最小圆的穷举边界校验、Airy Bessel 级数及区域大小关系、共享分析一致性、单/多波长、巨大权重、变迹、真实孔径、失效参考、预算、取消、更新和文件/编辑器往返。默认 Debug/Release 各 **1873/1873**（此前 1804 + 新增 62 + 新纳入能量分析相邻回归 7），默认桌面输出已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第十九批复验2026-10-01)。

既有选定基线资产不变；FCGT 仍为 Difference，既有 Huygens MTF 分析差异保持原值。未新增原生 MFE 数值捕获、截图、完整产品/实验室测试或 Optiland 比较。

定义参考：[官方操作数表](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[几何能量参考点与半宽](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v242/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Geometric.html)。

## 2026-10-01 第二十批指定点面形（部分完成）

新增 SSAG、SSLP、SCRV，**282 → 285 可执行，98/383 仅兼容保留**；二十批累计新增 161 项。[383 项审计](../artifacts/validation/sequential-operands-twentieth-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentieth-20261001/remaining-by-category.csv)。全部完成目标仍在进行。

| 代码 | 本程序参数 | 返回量 |
| --- | --- | --- |
| SSAG | Surf / Mode / X / Y / Off-axis / Remove / BFS | 局部矢高，mm |
| SSLP、SCRV | 同上，另有第八槽 Data6=Orientation | 方向斜率（无量纲）/法曲率（mm⁻¹） |

`SurfaceProfileMetrics.AtPoint` 复用前批面形/解析微分能力，在局部 XY 坐标直接求值；没有第二套追迹或有限差分公式。当前 Off-axis=0、Remove=0/1、Orientation=0..3（径向、正交、X、Y）。移除基准球面先作用于矢高及导数，再计算曲率。Mode=0/1 分别为机械/净半口径，但当前没有拟合步骤，所以不改变点坐标或结果；不按物理孔径裁切指定点。BFS=0..3 保留而不执行拟合。几何定义域外、非有限值和待实现模式明确报错。

SSAG 使用可计算的正式面形；SSLP/SCRV 目前支持标准、偶/奇次非球面及 XY 多项式的解析导数。BFS、离轴零件坐标重建、方向模值、其它面形解析导数及原生参数列/数值捕获仍待完成。

Application 增加可选 `ZemaxData6`，编辑器增加 `Parameter8`，只在对应行显示第八列。原始槽位保持权威：七/八槽分别验证，不把模式或方向误作波长；缺少第八槽的可执行快照失败。旧禁用兼容行缺失的 Data5/Data6 不会因打开/保存而补零，不自动升级。三项原生 ZMX 列仍缺少实机捕获，导入保持禁用只读，本程序创建行可以执行。

新增 **55 项测试**覆盖带符号球面、非对称多项式的混合导数、先减球面再计算、局部坐标/口径模式、非法模式/定义域、更新/取消/贡献、STAROPT 七/八槽往返、缺槽校验，以及 Headless 第八列方向绑定与切换隐藏。默认 Debug/Release 各 **1928/1928**（此前 1873 + 新增 55），默认桌面输出已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第二十批复验2026-10-01)。

选定提交基线资产不变；FCGT 保持 Difference，既有 Huygens MTF 分析差异保持原值。没有新原生捕获、截图、完整产品/实验室测试或 Optiland 比较。

定义参考：[官方指定点面形操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。

## 2026-10-01 第二十一批衍射圈入能量（部分完成）

新增 DENC、DENF，**285 → 287 可执行，96/383 仅兼容保留**；二十一批累计新增 163 项。[383 项审计](../artifacts/validation/sequential-operands-twentyfirst-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentyfirst-20261001/remaining-by-category.csv)。全部完成目标仍在进行。

`DiffractionEnergyMetrics` 复用正式 FFT/惠更斯 PSF 与主光线；`DiffractionEnergyDistribution` 对完整规则像素网格积分，DENC 返回给定分数的距离（µm），DENF 返回指定距离内分数。圆形积分改为在矩形边与单位圆交点处分段的解析计算，避免原固定 16 点求积的边界误差；没有放宽解析测试或外部回归预算。共享分析也使用此积分。

本程序八槽为 Samp/Wave/Field/Type/Refp/Fraction 或 Distance/I Samp/I Delta。Type=1 圆、2 X 狭缝、3 Y 狭缝、4 方框；狭缝和方框距离为半宽。Refp=0/1/2 是 FFT 的主光线/质心/像面顶点，3/4/5 是惠更斯对应参考。Samp=1..4；FFT 使用正式双倍图像网格的中央窗口，惠更斯 I Samp=1..4、I Delta 单位 µm（0 使用正式默认）。单波长即使全局权重为零也能计算；多波长规范化正权重，保留原主波长参考，惠更斯使用正式波长平方缩放。

当前有焦、标量、单配置，受共享网格及惠更斯工作预算限制。完整像素包括零强度样本，禁止缺格、重复、非规则或非有限数据。能量按有限窗口归一；圆/方框/狭缝超出窗口或目标分数无法覆盖时明确报错。窗口覆盖检查不保证未捕获 PSF 尾部误差。未模拟原生 1e10 哨兵的未明确阈值；偏轴顶点参考可能落在窗口外。2026-10-09 新 DENF 原生 API 审计列 2..9 为 Samp/Wave/Field/Dist/Type/Refp/I Samp/I Delta，与上述既有本地顺序不同；56 项原生标量已另存，但数值等价未认证。本次不迁移本地槽位或升级旧快照，ZMX 导入仍禁用只读，见[分层捕获与边界](FFT_PUPIL_PHASE_REPAIR_2026-10-09.md)。

新增 **51 项测试**覆盖解析像素面积/逆积分、非对称及偏心圆、不同参考/区域、共享 FFT 分析一致性、惠更斯显式/默认窗口、谱权重、缺格/非法数据、资源约束、取消/实时编辑、STAROPT 和编辑器八槽往返。另纳入既有固定 Zemax 衍射圈入能量回归 1 项（原 0.02 RMS 分数预算不变）。默认 Debug/Release 各 **1980/1980**（此前 1928 + 新增 51 + 相邻 1），默认桌面输出已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第二十一批复验2026-10-01)。

选定提交基线资产不变；FCGT 保持 Difference，既有 Huygens MTF 分析差异保持原值。没有新原生捕获、截图、完整产品/实验室测试或 Optiland 比较。

定义参考：[官方圈入能量操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。

## 2026-10-01 第二十二批相位与斜率模值（部分完成）

新增 SPHS、PSLP、DPHS、QSLP，**287 → 291 可执行，92/383 仅兼容保留**；二十二批累计新增 167 项。同时扩展已可执行 SSLP、DSLP 的 Orientation=4；它们不重复计入新增代码。[383 项审计](../artifacts/validation/sequential-operands-twentysecond-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentysecond-20261001/remaining-by-category.csv)。全部完成目标仍在进行。

`SurfacePhaseMetrics` 读取正式 `IPhaseProfile.Phase/Gradient`，在主波长下输出相位 waves、斜率 waves/mm；零谱权重主波长仍是参考，不把操作数的 Mode/Data 槽误作波长。常量、线性光栅、径向多项式、XY 多项式及网格样条复用已有函数，没有局部复制光学公式或有限差分。

| 代码 | 本程序六槽 | 当前支持 |
| --- | --- | --- |
| SPHS | Surf / Mode / X / Y / Data / Remove | Data=0，指定点相位 |
| PSLP | Surf / Mode / X / Y / Remove / Orientation | 指定点解析斜率，方向 0..4 |
| DPHS | Surf / Data / Samp / Remove / 未用 / 未用 | 孔径相位统计，Data=1..12 |
| QSLP | Surf / Data / Samp / Remove / Orientation / 未用 | 孔径斜率统计，Data=1..12，方向 0..4 |

Mode=0/1 使用局部 mm，Mode=2 按净半口径缩放 XY；指定点不按物理孔径裁切。统计使用正式物理孔径（圆、环、偏心圆、矩形或椭圆）内均匀 XY 网格，Samp=1..5（33..513）；面形和相位现在共享 `SurfaceMetricSampling`，RMS 为关于零的均方根。极值并列取先 Y 后 X 扫描首点。方向为径向/正交/X/Y/梯度模值；顶点径向采用 +Y、正交采用 −X。有限极大坐标先归一再求方向，避免半径溢出。

Remove=0 不移除，1 移除顶点常量；Data=9 返回实际移除常量，10..12 在这两个模式下为未移除的零系数。非相位面、非有限值、无采样点及超过 1 亿估算计算预算均明确报错；网格相位预算包含每次密集样条求解成本。Zernike 倾斜/光焦度项移除、对应 SPHS Data=1/2、更多采样及原生列/数值仍待完成。原生六列未捕获核对，ZMX 导入禁用只读，不自动升级。

SSLP/DSLP Orientation=4 现在返回解析梯度长度，先移除基准球面导数再求长度，结果非负；SCRV/DCRV 的曲率模值仍未实现，不冒用斜率模值。早期批次的模式描述保留当时范围。

新增 **65 项测试**（相位 61、斜率模值 4），包含独立多项式/光栅/样条解析值、离散矩形均方根、五种方向和多种孔径、主波长/实时编辑、非法数据/预算/取消、STAROPT 与编辑器往返。原有非法斜率方向测试改为越界 5，曲率方向 4 继续验证报错。默认 Debug/Release 各 **2045/2045**（此前 1980 + 新增 65），默认桌面输出已更新。[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第二十二批复验2026-10-01)。

选定提交基线资产不变；FCGT 保持 Difference，既有 Huygens MTF 差异保持原值。相位函数计算测试不等于相位光线追迹单位或原生 MFE 数值等价认证。没有新增外部捕获、截图、完整产品/实验室测试或 Optiland 比较。

定义参考：[官方相位操作数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)。

## 实施顺序

1. **注册表与快照模型**：383 项名称与官方 2026 R1 API 枚举相符的代码注册、通用原始参数槽位、行级兼容状态和旧 schema 默认迁移已完成；已补充首批光线、RMS、Moore-Elliott、`EFFL/TOTR/TTHI/TGTH`、常见镜头/厚度/曲率/圆锥/半口径/玻璃折射率/玻璃范围/表面光焦度与基础数学行描述符，并补入 `DIVB/PROB` 的 Factor 槽位、`EQUA/OSUM/QSUM` 的行范围槽位、`MNIN/MXIN/MNAB/MXAB` 的 `Surf1/Surf2` 槽位及 `POWR` 的 `Surf/Wave` 槽位；逐类型参数元数据及验证继续补齐。
2. **ZMX 导入/导出**：元数据驱动解析所有目标代码，保留源顺序和参数，拒绝非序列/废弃项并给出诊断。
3. **基础约束和一阶量**：完成系统、镜头、玻璃、参数、一阶和属性类。
4. **光线与像差**：复用按需追迹、波前、像差和介质状态正确性基线。
5. **分析型操作数**：接入 MTF、能量、鬼像、光纤、POP、高斯、GRIN、镀膜和偏振引擎。
6. **数学与控制流**：基础数学行已接入有序评价函数入口，支持前序行引用、行范围、Factor、除零和非法数学域错误；`EQUA` 已接入 Target 容差与专用贡献语义；六项几何质心已接入，后续集体行参考、高级控制流和分析型操作数仍需继续实现。
7. **宏和用户扩展**：实现受限提供程序协议。
8. **全量验收**：对公开定义明确的功能逐项完成导入、参数、数值、往返和非法输入测试；5 个 Unused 只验兼容保留，2 个 API 定义未核实项先补证据。全部注册代码均保持名称来源校验。

## 验收门槛

- 注册表保持当前 383 个已核对的官方名称；目录变更需新增官方来源证据。四个本程序扩展独立标识，不冒充 Zemax 操作数或直接等价别名。
- 非序列/遗留 37 个唯一代码和 24 个废弃 `Pn*` 代码不会进入顺序注册表。
- 每个有公开定义的功能至少一个有效计算或控制流用例和一个非法参数用例；5 个 Unused 只验兼容保存及禁止执行，2 个定义待核实项在取得定义前不执行。
- ZMX 全目录夹具导入后不产生未知代码、列错位或引用误判。
- STAROPT 往返保持所有操作数及其类型化参数。
- 串行/并行评价得到相同的有序结果；取消不会修改活动光学系统。
- 全反射介质、反射吸收、薄透镜方向归一化、OPL/OPD 和冻结辅助历史数值回归继续通过。

## 镀膜实验室不扩展 Zemax 操作数支持

2026-09-27 新增的共享薄膜求解器服务独立镀膜实验室，不会自动启用上文镀膜/偏振操作数，也不构成 Zemax 镀膜数值等价声明。现有操作数注册、兼容保留和实际可执行范围仍以正式 Core 注册表为准；接入真实膜系、追迹偏振和 ZOS-API golden 对照须独立实施。[镀膜实现与验证范围](COATING_DESIGN_LAB.md)。

## 2026-10-01 第二十三批视场状态与完整瞳孔变换（部分完成）

新增 FDMO、FDRE 和 REQS，**291 → 294 项受限可执行，89/383 兼容保留**。REQS 是无数值贡献的控制标记，不计作一个新的光学分析引擎。兼容保留中 82 项有已知功能待接通、2 项仅核实名称、5 项官方明确 Unused；二十三批累计接通 170 个代码，仍未达到全部完成。[逐项状态](../artifacts/validation/sequential-operands-twentythird-20261001/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentythird-20261001/remaining-by-category.csv)。

本程序 FDMO 的八槽为 Field/Unused/Hx/Hy/VDX/VDY/VCX/VCY。Field 为正的一基编号；Hx/Hy 各限 [-1,1]，四个因子必须有限。使用本批评价开始时的最大视场半径还原实际坐标，保存该视场第一次覆盖前的完整数据，保留标签、权重与 TAN。FDRE 恢复同编号数据；其他视场、临时主波长和像面继续保留。禁用或控制流跳过的行不改变状态，非法参数在隔离副本中失败，不发布半次变更。评价结束、取消或失败不改变活动光学文件。CONF 行会清除 FDMO 覆盖，但配置切换本身仍返回不支持；不能把恢复动作当作多配置优化支持。重复 FDMO 的归一化基准和状态交互尚无原生数值捕获，以上是当前本程序的显式规则。

正式 `PupilVignetting` 统一先压缩并偏移、再按角度旋转的五因子变换。`GenerateGeneric` 原来提前压缩后又在共用入口压缩一次，现统一只变换一次；单光线、批量光线和光阑瞄准目标采用同一变换。CVIG 与“忽略渐晕因子”清除全部五因子，保留原副本外的状态；分析副本保留运行时材料，并与源追迹缓存断开。新字段改变时源缓存按现有修订规则失效。

视场新增 `VignetteDecenterX/Y` 和 `VignetteAngleDegrees`，贯通 Core、快照、STAROPT、ZMX 的 VDXN/VDYN/VANN、应用 DTO 和侧栏。快照 schema 从 5 升为 6，旧缺失字段默认为 0；STAROPT 容器仍为 2、工程负载仍为 4。侧栏保持 240–280 DIP、有垂直滚动且无横向滚动。FDMO/FDRE 的本地编辑与保存可执行，原生 MFE 列位置没有捕获验证，因此导入禁用只读，快照也不自动升级。

REQS 保留位置、注释和顺序，无活动参数、贡献或优化残差，也不能作为数学行引用的有效数值。Requirements Editor 自动生成/插入需求行尚未实现。该阶段帮助层级将 REQS 放到流程控制，将当时尚未实现的 RRET 放到偏振/镀膜相关族；官方 RRET 定义为 RMS 延迟量，不能误作光线控制代码。

边界：非定义视场仍采用既有最近视场的渐晕因子，尚未补齐官方对于轴对称/Y 轴视场的插值规则；新非零偏移/角度 ZMX 往返通过本地验证，未新增外部捕获。不承诺所有 FDMO 模式、归一化类型或 native MFE 数值等价。

新增 **48 项测试**（视场状态 27、瞳孔变换 20、侧栏编辑 1），另强化原侧栏宽度断言。合并回归 Debug/Release 各 **2130/2130**，包含此前 2057、帮助净增 2、本批 48 和相邻已有 23；独立 Skia 5/5 属于子集。见[构建证据](BUILD_AND_RELEASE.md#顺序操作数第二十三批复验2026-10-01)。

官方依据：[FDMO/FDRE/REQS 与 RRET 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[渐晕因子变换与非定义视场边界](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Vignetting_Factors.html)。这些文档验证名称和语义范围；新增原生参数及数值等价仍待实机捕获。既有 FCGT 残差继续标为 Difference，未放宽精度门槛。

## 2026-10-02 第二十四批 ZERN 与共享拟合修正（部分完成）

新增一个官方代码 ZERN：**294 → 295 项受限执行，88/383 兼容保留**。其中 81 项有已知功能待接通、2 项定义待核实、5 项 Unused 不计入实现任务；已有执行项仍存在模式和原生验证缺口。不能把 ZERN 的三个基底或各项系数分别计为多个操作数。见[逐项状态](../artifacts/validation/sequential-operands-twentyfourth-20261002/operand-audit.csv)和[剩余分类](../artifacts/validation/sequential-operands-twentyfourth-20261002/remaining-by-category.csv)。

本地七槽为 Term/Wave/Samp/Field/Type/Epsilon/Vertex；Term 位于 Int1，Wave 位于 Int2，Field 位于 Data2。正波长、正视场编号，采样代码 1..5 对应 32..512 点每边的均匀归一瞳孔网格，受共享矩阵及运算预算限制，不会自动降采样。Type=0/1/2 为 Fringe/Standard/Annular；分别支持正项 1..37、1..231、1..231，环形遮拦比 0..0.95，遮拦内数据不参加拟合。当前只实现 Vertex=0，Vertex=1 明确报错，未以其他参考模式替代。

Term=-8..0 返回质心/主光线峰谷差、零参考 RMS、主光线/质心 RMS、方差、指数近似 Strehl、RMS 拟合误差、最大绝对拟合误差。统计取有效原始 OPD 的几何等权样本；主光线参考仅去均值，质心参考去最佳拟合平面，避免错误地用完整高阶拟合的前三项代替原始采样统计。Strehl 为指数估计，不是惠更斯 PSF 峰值比。正式 Zernike 分析报告复用这套共享统计，通用分析的默认采样方式保持原设置。

共享拟合器修正 Standard 和 Annular 的 Noll 编号：例如 Z5 为 sin(2φ)，Z6 为 cos(2φ)，Z7 为 sin(φ)，Z8 为 cos(φ)。环形基底改用缩放半径平方变量上的正交三项递推，通过正式高斯积分构建归一化；不再以相消严重的单项式矩计算范数并钳为小正数。严格 QR 路径增加二次正交化和相对秩检查，欠采样或不满秩时不返回伪系数。矩阵不超过 2000 万元素，样本数乘项数平方不超过 5 亿；这是本程序资源保护，不是 Zemax 的限制。

相同参数的相邻启用 ZERN 行共同拟合所需最高项，至少 11 项；仅项号、目标、权重、注释不同可复用。缓存局限于当前有序评价上下文的最近一次拟合，分隔行、参数变化与临时像面等状态变化分开处理。Standard/Annular 的相邻最高项规则有公开说明；Fringe 的本程序最高项规则仍缺原生验证。相邻复用、不同波长/视场/采样/基底/遮拦的隔离、像面切换/恢复、并发批次和编辑失效均有测试。

七槽编辑与 STAROPT 往返保留负 Term 与 Data2 视场，快照校验遍历元数据声明的所有 DataN 视场引用。原生 ZMX 扩展列尚无实机捕获，导入保持禁用只读、原记录尾部保留在注释，不自动升级为可执行参数；不声称原生七槽列语义已核实。

新增 45 项测试，合并之前 2130 项及相邻既有 22 项，Debug/Release 各 **2197/2197**。独立解析多项式验证编号、低阶环形系数和完整 231 项拟合；独立积分表检查全阶径向基底正交性，覆盖遮拦比 0、0.5、0.95。见[验证证据](BUILD_AND_RELEASE.md#顺序操作数第二十四批复验2026-10-02)。

外部证据分层：选定 22 份提交的 Zemax 捕获逐字节保持不变，其中新增核对三种 Zernike 的文本和二进制 CFG；这些检查只证明基线完整性。现有固定设置的波前、MTF、照度、畸变/场曲回归使用当前 Core 重算，FCGT 继续为 Difference。Zernike 捕获的瞳孔采样尚未明确解码核实，因此本轮未做其新数值等价比较，也没有根据结果反推或挑选采样。所查看 Zernike 图片来自捕获数据渲染，不是原生设置对话框截图。

官方定义：[ZERN 参数与项号](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[Standard 编号](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Standard_Coefficients.html)、[Annular 定义与归一化](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Zernike_Annular_Coefficients.html)。定义和数学回归不替代原生参数与数值验收；全部完成目标保持进行中。

## 2026-10-02 第二十五批对比度操作数与共享损失图（部分完成）

新增一个官方代码 MECA 的本地路径，同时修复已有 MECS/MECT：**295 → 296 项受限执行，87/383 兼容保留**。剩余 80 项已知功能、2 项定义待核实及 5 项 Unused；已执行项的未完成模式和原生等价仍在任务范围内。见[逐项状态](../artifacts/validation/sequential-operands-twentyfifth-20261002/operand-audit.csv)和[剩余分类](../artifacts/validation/sequential-operands-twentyfifth-20261002/remaining-by-category.csv)。

正式 Core 的 `ContrastMetrics` 使用 `WavefrontEngine` 在共同参考下计算 OPD 差。旧 MECS/MECT 直接相减累计光程，混入参考球面与离轴发射光程差异；现按原始归一瞳孔点与负 X/负 Y 移位点取有符号波前差，单位 waves。移位为完整归一瞳孔直径 2 乘以频率/截止频率；有焦使用正式工作 F 数，无焦使用共享无焦截止频率，分别为 cycles/mm、cycles/mrad。显式遵循系统 ray aiming，避免操作数与分析各用不同设置。

本地六槽为 Unused/Wave/Field/Freq/Px/Py，原始槽位优先于旧友好字段；Field 为正编号，Wave=0 在本程序选择主波长，正编号选择该波长。MECA 按公开“弧矢/切向平均”在本地取两项有符号值的算术平均，帮助明确可能相消，分别约束方向可用 MECS/MECT。**MECA 原生平均细节与列位置、三项原生移位符号及数值等价尚未捕获验证**；不能把本地定义回归或损失图余弦一致性当作这些问题已经解决。

无效引用、非有限/负频率、超过截止频率、原始或移位点超出瞳孔、失追迹和零强度均明确失败；零频仅在光线有效时返回零差。MECA 任一方向无效便整体失败。取消向外传播，不生成成功零值。主波长、FDMO/FDRE、IMSF 临时状态、编辑修订、并发和有界光线缓存均纳入验证。

`ContrastLossMapAnalysis` 改为共享同一波前光线对，继续使用中心坐标两侧的点绘图；损失为 0.5×[1−cos(2πΔOPD)]，可选归一化。平均光线相位与原中心点相位仍为两个独立输出，尚未作原生相位指示器等价验收。分析 frequency=0 保留自动选择 5% 截止频率的通用默认行为；非法设置、超截止和全无效样本返回不可用，不再钳到另一个频率或视场。通用默认采样未改变。

优化向导对每个视场/波长只计算一次截止频率，把中心采样位置转换为操作数的原始端点，并检查有效移位范围；本程序向导采样不声称与 Zemax 向导逐行相同。**既有 MECS/MECT 值会按修正语义重算，保存的原始 Px/Py 不被改写；此前由旧向导生成并按中点解释的行如需保留原光线对，应使用新向导重新生成。**本地六槽、目标/权重与编辑/STAROPT 往返已验证；既有 MECS/MECT 的已知六槽 ZMX 导入保持可执行，MECA 原生导入禁用只读并保留原记录，不自动升级。快照 schema 仍为 6。

新增 **61 项测试**，含光程参考、方向/平均、原始槽优先级、波长与系统状态、错误传播、焦/无焦及瞄准设置、损失图同点一致、原生导入隔离、编辑/保存和正式 DLS 像面恢复。合并此前 2197 与相邻既有 6 项，默认 Debug/Release 各 **2264/2264**；见[构建与证据](BUILD_AND_RELEASE.md#顺序操作数第二十五批复验2026-10-02)。

外部证据分层：25 份选定已提交捕获与 HEAD 字节一致，仅证明完整性子集。当前 Workbench 对固定 `123456.ZMX` / 2026 R1 损失图设置（13×13、频率 100、波长 2、视场 1、不归一化）重算，186 个有效点 NRMSE 9.268083437096168e-9、最大绝对误差 2.5588614560589917e-9，既有 0.03 回归预算未放宽。此比较仅覆盖损失图，余弦丢失符号，不能证明原生 MFE 移位方向或 MECA 平均规则。既有 FCGT 继续标为 Difference；没有新原生捕获、Optiland 比较或本批 UI 渲染。

官方依据：[操作数参数与平均/方向描述](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[波前差与频率对应关系](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimizing_for_MTF.html)、[光线对与损失图说明](https://optics.ansys.com/hc/en-us/articles/42661802617491-Optimizing-for-MTF-performance-using-Contrast-Optimization)。这些依据不替代未完成的原生验收；全部完成目标继续进行。

## 2026-10-02 第二十六批三阶与指定矩阵畸变（部分完成）

新增 DIST、DISA 两项本地执行路径：**296 → 298 项受限执行，85/383 兼容保留**。剩余 78 项已知功能、2 项定义待核实、5 项 Unused；计数不代表已执行项的所有模式或原生数值验收完成。见[逐项状态](../artifacts/validation/sequential-operands-twentysixth-20261002/operand-audit.csv)和[剩余分类](../artifacts/validation/sequential-operands-twentysixth-20261002/remaining-by-category.csv)。

### DIST：共享赛德尔计算

本地槽位为 Surf/Wave/Absolute/Unused/Unused/Unused。Surf>0 返回该面 `W311=S5/(2λ)`，单位波长；Absolute 不改变单面单位。Surf=0 返回全系统三阶百分比 `50S5/H`，Absolute=1 返回 `-S5/(2n′u′)` mm。公式由正式共享 `SeidelMetrics` 的 S5、拉格朗日不变量 H、像方折射率和边缘光线斜率计算；其参考为近轴焦点，因此不随仅移动像面产生的离焦改变，不代用 DISG 的真实光线畸变。Wave=0 本地选择主波长，逐面量按所选波长换算，沿用主波长光阑高度归一化。

当前模型范围与共享赛德尔报告一致：共轴、未旋转、均匀介质的球面/平面折射系统；反射、GRIN、圆锥及多项式非球面、理想薄透镜和相位/衍射贡献仍未支持。全系统无焦或边缘光线出射斜率为零明确失败。零视场且三阶畸变为零可返回零；无效 Surf/Wave/Absolute、非有限值和取消不生成成功数值。**Surf=0 原生百分比及离焦参考约定仍待实机核对**，不能仅凭赛德尔报告 TDIS 一致宣称 DIST 全模式兼容。

### DISA：用户指定参考矩阵

本地八槽为 RefField/Wave/Field/Data/A/B/C/D：前两项整数槽，后六项 Data 槽。RefField=0 为轴上，Field 必须为已定义的正视场编号；Data=0/1/2 选择径向/X/Y。物高使用线性坐标差，角度使用 tan(角度) 差，实际像点扣除参考主光线像点。矩阵由用户完整给定，不再调用 ABCD 自动拟合，也不求逆，因此一维扫描可使用秩一矩阵。

径向百分比使用正式网格畸变定义的有符号向量差范数除以预测径向像高。X/Y 本地采用对应坐标差除以对应带符号预测坐标；**官方条目未明确展开这两个方向的分母公式，本地约定已在帮助显示，仍需原生数值捕获确认**。目标恰为参考视场时三种模式返回零；其它零预测分母、非法枚举/引用、非有限矩阵、渐晕或失追迹明确失败，不返回伪零。限定有焦模式，实像高先经共享正式转换；尊重系统 ray aiming 与像面局部坐标，不改动源视场。

`GridDistortionAnalysis.MaximumDistortionPercent` 原用实际/预测径向长度之差，非径向偏差可能被抵消；本批改为与 DISG/DISA 共用的有符号向量差。绘制点和网格缩放不变。独立平界面 Snell 计算验证离轴参考与非径向峰值，并检查等半径纯方向偏差不会成为零。

### 编辑、导入与验证边界

本地原始槽位优先于旧 Surface/Field/Wavelength 等别名；DISA 缺少完整六个 Data 槽位不能执行或保存为有效扩展行。编辑器可编辑 C/D，第八槽和目标、权重可经 STAROPT 往返。两项原生 ZMX 参数列尚无捕获证明，因此导入禁用只读、完整原记录文本保留、不补造 Data5/Data6、不自动升级旧兼容行。快照 schema 仍为 6。已有泛型只读测试改为显式兼容行状态，继续阻止强行启用兼容行；注册表保留 DISC 不可执行断言。

新增 **40 项测试**，覆盖独立单折射面三阶解析值、孔径/波长/视场/像面变化、ABCD 交叉项与离轴参考、有限物高和实像高转换、像面旋转、全槽编辑保存、原生只读保留、非法参数/取消以及正式 DLS 厚度优化。与前批 2264 项及相邻既有网格分析 4 项合并，默认 Debug/Release 各 **2308/2308**，零失败/跳过；见[构建记录](BUILD_AND_RELEASE.md#顺序操作数第二十六批复验2026-10-02)。

外部证据分层：25 份选定捕获资产与 HEAD 字节一致，仅证明该子集完整性。当前 Workbench 对已提交 `123456.ZMX` / OpticStudio 2026 R1 / 440 nm 赛德尔报告重新计算，22 个逐面 W311 值最大误差 **4.91866764207316e-7 波长**，累计 TDIS 误差 **3.84701699496226e-7 mm**，各自 1e-6 测试预算通过。这是固定报告量的数值比较，**不是新原生 DIST/DISA MFE 参数列、百分比或方向归一化认证**。既有 FCGT 的比较目标 1e-5 mm、实际误差约 2.153e-5 mm 仍标为 Difference；未放宽预算。没有新原生捕获、Optiland 比较、全产品/实验室测试、安装包或本批截图。

### 尚未接通的 DISC

官方定义要求沿 Y 视场计算校准 f-theta 的最大偏离，并区分百分比、有焦长度和无焦方向余弦偏离。当前公开说明不足以确定原生校准拟合准则、扫描采样和列布局；已有校准畸变分析只对离散视场行做最小二乘且在轴上强制零值，不能把它直接包装成完成的 DISC。DISC 本批继续兼容保留，后续需在共享 Core 中补齐全视场校准、轴上极限和无焦处理并核验。

官方依据：[DIST/DISA/DISC 定义](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Optimization_Operands_Alphabetically.html)、[网格畸变参考矩阵及有符号向量差](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Grid_Distortion.html)、[校准畸变及轴上极限](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Field_Curvature_and_Distortion.html)。全部完成目标保持进行。

## 2026-10-02 第二十七批：照度与有效 F 数（部分完成）

RELI、EFNO 新增本地计算路径：**298 → 300 项受限执行，83/383 兼容保留**。剩余 76 项已知功能、2 项定义待核实、5 项 Unused；已执行项的模式和数值验证也仍在目标范围内。

Samp/Wave/Field/Pol 采用显式像方网格和共享功率链，支持有序评价、编辑及 STAROPT；原生导入保持只读。RELI 真实轴上归一化并清除五个渐晕因子；EFNO 非零渐晕原生规则未核实，当前需先 CVIG。31 项新增验证包含独立圆锥、实际 DLS、错误边界与固定原生分析差异观测。详见[实现与限制](ILLUMINATION_OPERANDS_2026-10-02.md)、[逐项审计](../artifacts/validation/sequential-operands-twentyseventh-20261002/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentyseventh-20261002/remaining-by-category.csv)。

## 2026-10-02 第二十八批：最佳拟合球面（部分完成）

BFSD 新增本地最小去除体积拟合及 Data=0..7：**300 → 301 项受限执行、82/383 兼容保留**。当前限旋转对称标准及奇偶非球面、明确空气/材料边界；原生 ZMX 保持只读。SSAG 等已有操作数的最佳拟合球面移除模式尚未接入，不因共享基础完成而自动标为支持。详见[实现与验证边界](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)、[逐项审计](../artifacts/validation/sequential-operands-twentyeighth-20261002/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentyeighth-20261002/remaining-by-category.csv)。

## 2026-10-02 第二十九批：指定方向矢高与延伸区

TSAG 接通本地八槽、共享 Core 求交、编辑保存及生产优化；**301 → 302 项受限执行、81/383 兼容保留**。延伸区作为正式表面数据保存和撤销，制造体积、毛坯、CAD 与自动口径使用同一光学延伸边界。原生 TSAG 列、复合倾角/根选择与 Chip Zone 文件映射尚未核实；导入保持只读，非零延伸区文本处方导出明确拒绝丢失数据。验证为 Debug/Release 各 **2560/2560**，范围和未完成模式见[本批说明](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)、[逐项审计](../artifacts/validation/sequential-operands-twentyninth-20261002/operand-audit.csv)、[剩余分类](../artifacts/validation/sequential-operands-twentyninth-20261002/remaining-by-category.csv)。

## 2026-10-03 第三十批：GRIN 材料控制（受限执行）

新增 21 项：`I1…6 GT/LT/VA`、`GRMN/GRMX/DLTN`。从 302 → 323 项受限执行，余 60 项兼容保留。六点、毛坯端点、原始参数、错误行为及原生验证边界见[完整记录](GRIN_CONTROL_OPERANDS_2026-10-03.md)。GRIN 专用类仍有 LPTD 未实现；已有执行模式也需继续扩充和原生验证。

## 2026-10-03 第三十一批：HYLD 普通折射界面

新增 1 项受限执行代码 HYLD，该阶段 324 项执行、59 项兼容保留。共享追迹保留局部界面折射率，包含 GRIN、标量/批量及反射后介质状态；入射像面停止模式不发布交互后的折射率。HHCN 仍待正式超半球分支支持。计算范围、原生只读规则及验证见[本批记录](HIGH_YIELD_OPERAND_2026-10-03.md)。

## 第三十二批：RRET 与共享复电场追迹（2026-10-03）

新增 RRET 一项受限执行，该阶段 325 项执行、58 项兼容保留（51 已知功能、2 定义待核实、5 Unused）。本地八槽、谱权重、高斯圆瞳、界面/膜层相位、真实 DLS 和 STAROPT 接通；原生导入只读，原生绕相及数值、应力双折射、CODA 和全局偏振状态仍待完成。详见[本批记录](POLARIZATION_RETARDANCE_2026-10-03.md)。

## 第三十三批：CODA 与系统偏振设置（2026-10-03）

CODA 新增受限执行，该阶段 326 项执行、57 项兼容保留（50 已知功能、2 定义待核实、5 Unused）。单光线的界面 R/T/A、复振幅、Ex/Ey/Ez、相位和局部椭圆来自共享正式追迹；系统偏振可编辑保存撤销，RRET 与照度同步读取相关设置。原生列/系统偏振文件映射/绝对相位原点与数值等价仍待核实，原生导入行继续只读。见[本批记录](COATING_DATA_OPERAND_2026-10-03.md)。

## 第三十四批：物理膜层参数约束（2026-10-03）

CMGT/CMLT/CMVA、CIGT/CILT/CIVA、CEGT/CELT/CEVA 新增九项受限执行，该阶段 335 项执行、48 项兼容保留（41 已知功能、2 定义待核实、5 Unused）。共享层调整、物理计算、编辑、变量优化及保存撤销接通；GT/LT 的全体选择本地使用最违反约束的一层，原生聚合数值及文件映射未验证，导入行保持只读。见[本批记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

## 第三十五批：多配置求值（2026-10-03）

CONF 按从 1 开始的配置编号切换有序评价副本，FDMO 随配置恢复；ZTHI 比较全部配置的表面闭区间厚度和。337 项受限执行、46 项兼容保留（39 已知功能、2 定义待核实、5 Unused）。MCOV/MCOG/MCOL 依赖 MCE 行表而非表面号，尚不执行；高级状态组合及原生数值仍有缺口。见[本批记录](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。此前章节描述各自历史实现范围。

## 第三十六批：多配置行表（2026-10-03）

新增 MCOV/MCOG/MCOL 受限执行；Op# 读取独立的有序绑定行，Cfg# 读取指定配置。四类绑定是 THIC/CRVT/CONN/SDIA，取实际表面数据；原生 MCE 行序/列映射未核实，不把 ZMX 表面编号替代行号。340 项受限执行、43 项兼容保留（36 已知功能、2 定义待核实、5 Unused）。更多 MCE 行类型、单元格变量/配置拾取解及原生对照仍有缺口，见[本批记录](MCE_ROW_OPERANDS_2026-10-03.md)。

## 2026-10-03 第三十八批：多配置拾取

THIC/CRVT/CONN/SDIA 支持比例/偏移拾取，单元格显示 P，依赖图联通编辑、联合优化与单面优化；STAROPT v7 保存并验证源目标绑定。没有增加操作数名称，340 项受限执行的统计不变。自动半口径源、更多行类型、高级求解和原生 MCE 捕获仍有缺口。默认 Debug/Release 输出的累计回归各 **3291/3291** 通过（完整保留前批 3236，新增 55），零失败、零跳过、零编译警告/错误。界面/数值/架构子集 **173/173**，12 张实际控件截图已检查。详见[实现与限制](MCE_PICKUP_SOLVES_2026-10-03.md)。

## 2026-10-03 第三十九批：SVIG 自动渐晕

新增 SVIG 受限执行，隔离副本中的四边缘孔径搜索、失败状态保留、原生导入只读保护、参数/保存及优化路径已接通。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。见[实现与限制](AUTOMATIC_VIGNETTING_2026-10-03.md)。
