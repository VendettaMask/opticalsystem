# 文件格式与插件

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

历史行表阶段：2026-10-03 多配置行表：新增 MCOV/MCOG/MCOL 三项受限执行，按独立行号和配置号读取 THIC/CRVT/CONN/SDIA 绑定参数；行重排引用、输入验证、优化候选和 STAROPT v5 保存撤销已接通。**340/383 项受限执行、43 项兼容保留**（36 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3183/3183**（前批 3135 + 新增 48），零失败、零跳过、零编译警告/错误。更多 MCE 类型、单元格变量/配置拾取解以及原生行序、列和数值映射仍未完成；原生 MCO 导入只读。见[行表实现与验证](MCE_ROW_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 多重配置：新增 CONF、ZTHI 两项受限执行，配置上下文贯通有序评价、应用显示及优化候选；基准几何链接和拾取在候选副本内同步，活动配置保持不变。**337/383 项受限执行、46 项兼容保留**（39 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3135/3135**（前批 3089 + 新增 46），零失败、零跳过、零编译警告/错误。PRIM/CVIG/IMSF 后接 CONF 的组合、其他配置变量联合搜索及原生数值/列映射仍未完成，原生导入只读。见[多配置实现与验证](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 膜层约束：CM/CI/CE 九项可本地编辑及 STAROPT 保存；原生 ZMX 行保持禁用只读并保留尾列。所有已有物理膜层及调整参数在未映射的文本导出时明确拒绝，避免从完整层定义降成名称。 详见[实现与验证](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03 膜层约束：新增 CMGT/CMLT/CMVA、CIGT/CILT/CIVA、CEGT/CELT/CEVA 九项受限执行；共享薄膜求解读取每层倍率与 n/k 偏移，物理膜层编辑、变量优化和 STAROPT 保存撤销已接通。**335/383 项受限执行、48 项兼容保留**（41 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认输出 Debug/Release 合并回归各 **3089/3089**（前批 3034 + 新增 54 + 既有样式检查 1），零失败、零跳过、零编译警告/错误。原生膜层字段、聚合边界数值、倍率开关/拾取仍有缺口，ZMX 行保持只读。见[膜层约束记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

2026-10-02：原生 JSON/STAROPT 快照 schema 6 新增 `coherent_multilayer` 物理膜层组件，逐层完整保存材料和厚度，严格拒绝缺失材料系数；旧经验类型含义不变。这不解析 ZMX `COAT` 名称为真实膜系，也没有把独立 `.coating.json` 自动应用到镜头，见[原生格式](STAROPT_FILE_FORMAT.md#物理相干镀膜组件)及[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)。

2026-09-30 全局坐标数据：快照新增可选 `GlobalReferenceSurfaceNumber`（缺省 1），STAROPT 与克隆保留。ZMX `GLRS` 与本批 GLC/RAG 活动表面槽位按坐标断点折叠后的物理面编号同步转换；被折叠坐标断点的直接引用尚未支持，明确拒绝。旧 STAROPT 已丢失的原始 GLRS 不能自动恢复。旧兼容行仅在类型已实现且正式参数/引用校验通过时解除只读，保留启用状态；无效引用继续可保存。详见 [第七批](ZEMAX_OPERAND_SUPPORT.md#2026-09-30-第七批全局坐标与光线扇导数部分完成)。

2026-10-01 操作数帮助已改为三级导航，帮助阶段默认桌面 Debug/Release 输出已更新；当时帮助、名称标识及字号检查各 **19/19** 通过，含明暗主题和窄窗口渲染。见[层级与验证](OPERAND_HELP_HIERARCHY_2026-10-01.md)。该 19 项记录保留原阶段范围，后续合并回归见下方第二十六批。

2026-10-02 第二十六批接通 DIST/DISA 本地计算，并修正网格畸变对非径向偏差的统计。该阶段 **298/383 项有受限可执行路径，85 项兼容保留**；其中 78 项已知功能待接通、2 项定义待核实、5 项官方未用名称。默认 Debug/Release 合并回归各 **2308/2308**（此前 2264 + 新增 40 + 相邻既有 4）。固定 123456 / 440 nm 赛德尔报告的 22 个 W311 值最大误差约 4.92e-7 波长，总 TDIS 误差约 3.85e-7 mm；这不认证原生 MFE 列、DIST 全系统百分比/离焦约定或 DISA 方向归一化，两项原生导入保持只读。DISC 仍待实现；既有 FCGT 保持 Difference。见[支持边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)、[验证记录](BUILD_AND_RELEASE.md#顺序操作数第二十六批复验2026-10-02)。另 4 项本程序扩展不计入 383；全部完成目标仍在进行。以下历史记录保留各自范围。

Huygens MTF 后处理修复只改变相关分析数值，并增加变换大小、频率取值约定的元数据，
不改变镜头文件格式。STAROPT、ZMX、SEQ、LEN 入口和通用 JSON 基础设施保持原有边界；
见 [本轮兼容影响](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。

2026-09-06 能量/相位修复没有更改 STAROPT、ZMX、SEQ、LEN 或通用 JSON 的格式入口。兼容能量图的输出点数变为 396，分析 CSV/JSON 消费者应读取实际点数和类型化单位，不硬编码旧点数；这不恢复 Python Optiland 专用格式。详见 [数值输出边界](ZEMAX_ENERGY_REPAIR_2026-09-06.md)。

2026-10-02 第二十六批：DIST 本地六槽、DISA 本地八槽（RefField/Wave/Field/Data/A/B/C/D）可经编辑器与 STAROPT 往返；两项原生列未捕获验证，ZMX 导入禁用只读且不自动升级，保留完整原记录文本，不补造缺失 Data5/Data6。快照仍为 schema 6。

2026-10-02 第二十五批：MECA 本地六槽、编辑与 STAROPT 往返可用；原生 MECA 列和平均规则未捕获，ZMX 导入禁用只读且不自动升级。MECS/MECT 已知六槽保持；改为共同参考波前差，原始 Px/Py 不改写，旧向导中点行需按新向导重建以保持原光线对。快照仍为 schema 6。

2026-10-02 第二十四批：ZERN 的本地七槽 Term/Wave/Samp/Field/Type/Epsilon/Vertex 贯通编辑与 STAROPT；Term 可为负诊断项，Field 位于 Data2，按元数据校验 DataN 视场引用。原生扩展列未捕获，ZMX 行禁用只读并保留原记录注释，不自动升级。快照仍为 schema 6。

2026-10-01 第二十三批：视场快照 schema 6 保存可选 `VignetteDecenterX/Y` 与 `VignetteAngleDegrees`；旧 schema 1–5 缺失字段默认 0。ZMX VDXN/VDYN/VANN 与现有 VCXN/VCYN 双向保存。FDMO 本地八槽贯通 DTO/编辑器/STAROPT，FDRE 保留 Field 引用；两项原生 MFE 列仍只读，禁止自动升级。REQS 是无贡献位置标记，需求自动插入未实现。

2026-10-01 第二十二批历史：SPHS/PSLP 与 DPHS/QSLP 均使用本程序六槽；Mode、Data、Samp 保持整数模式语义，不被误读成波长。主波长取自当前光学配置。本地编辑/项目保存可执行，原生列未核对的行仍禁用只读且不自动升级。

2026-10-01 第二十一批历史：本程序 DENC/DENF 使用 Samp/Wave/Field/Type/Refp/Fraction 或 Distance/I Samp/I Delta 八槽。Wave=0 按严格谱权重汇总；FFT 忽略后两槽，惠更斯使用后两槽。原生八列尚未实机核对，导入禁用只读且不自动升级。

2026-10-01 第二十批历史：本程序 SSAG 使用 Surf/Mode/X/Y/Off-axis/Remove/BFS 七槽，SSLP/SCRV 再使用 Data6=Orientation，第八槽从 DTO、编辑器、应用服务到 Core 快照完整往返。Int2 为模式整数，不是波长。缺少参数的可执行快照被拒绝；旧兼容行不补虚构 Data5/Data6，原生导入继续禁用只读。

2026-10-01 第十九批历史：本程序 GENC/GENF 使用 Samp/Wave/Field/Type/Refp/Fraction 或 Distance/No Diff Lim 七槽，ERFP 使用 Samp/Wave/Field/Type/Fraction/Max Radius 六槽。Int1 是采样代码，Int2 是波长。编辑与 STAROPT 往返已验证；三项原生列映射缺少实机捕获，ZMX 保持禁用兼容只读，快照不自动升级。

2026-10-01 第十八批历史：本程序 VOLU/TMAS 使用 Int1/Int2=闭区间起止面、Data2=Mode；DSAG 使用 Surface/Data/Samp/Off-axis/Remove/BFS，DSLP/DCRV 再使用 Data5=Orientation。六/七槽在本程序与 STAROPT 内可编辑往返；五项原生列布局未捕获核实，ZMX 记录禁用只读并保持兼容状态，不自动升级。

2026-10-01 第十七批历史：本程序 PRIM 使用 Int1=Wave，IMSF 使用 Int1=Surface（0 恢复）/Int2=Refocus；这两类原生 ZMX 列位置尚未捕获核实，导入记录保持兼容只读，快照也不会自动升级。应用创建的行可执行并通过 STAROPT 与编辑器往返。CVIG 无有效参数列，可直接导入并清除当前批次已有 X/Y 压缩因子；当时偏移/旋转渐晕未实现，现已由第二十三批补齐。

2026-10-01 第十六批历史：STRH 使用 Samp/Wave/Field/Pol/All Conf；CEHX/CEHY 使用 Wave/Field/Pol/Pupil Samp/Image Samp/All Conf。通用六槽与 STAROPT 往返已验证，Int1=Wave、Int2=Field 的引用校验已补齐；不可把质心操作数 Int1 当作表面号。原生 MFE 实机槽位/数值对照仍未验证。

2026-10-01 第十五批历史：表面快照追加可空 `ThermalExpansionPpmPerC`（10⁻⁶/°C），显式 0 与未知 null 分开保存。旧 STAROPT/JSON 缺项为 null；新建表面默认 0。ZMX 表面 TCE 记录尚无已验证映射，因此导入表面保留 null，TCVA/TCGT/TCLT 行保留原参数、目标、权重并禁用只读；仅引用面已有明确 TCE 时，原生项目再加载可升级并保留禁用状态。

2026-10-01 第十四批历史：GBPD/GBPP/GBPR/GBPS/GBPW/GBPZ 使用通用六槽 Surf/Wave/UseX/W0/S1toW/M²；W0 和 S1toW 作为原始数值保存，不能按视场编号处理。ZMX 六槽导入、STAROPT 及编辑器往返已验证；仍无原生 MFE 槽位/数值捕获。

2026-09-30 第十三批历史：MTHA/MTHS/MTHT/MTHN/MTHX 采用已有七槽链路，第七列为 Ima Delta（µm），不能当作 Py。快照对所有七槽已实现类型统一要求完整 Data1..Data5；缺失扩展参数的旧行不自动补零升级。五种 MTH 原生 ZMX 扩展文本位置未验证，保持禁用只读、保留原始尾部文本；完整 STAROPT 行支持往返。

2026-09-30 第十二批历史：应用 DTO 增加可选 `ZemaxData5`；编辑器按类型支持六或七槽，七槽的第七列为 Py。STAROPT 继续使用已有原始参数数组，无须容器版本迁移。面范围的两项原始整数和 Data1 波长分别验证，不能将终止面混作波长。四个角度范围的原生 ZMX 扩展位置未获得已提交捕获验证，整行参数文本继续只读保留；缺少 Data5 的旧行不能自动升级。编辑其他行、刷新或保存不会填零并解除只读。SFNO/TFNO 当前使用 Field/Wave 两个整数槽，0 分别解释为轴上/主波长；原生 MFE 槽位/数值对齐仍待捕获。

2026-09-30 第十一批：ABCD/DISG/DIMX/SMIA/FCGS/FCGT 六槽及目标/权重接通。新增类型化 `SignedWavelength`：DISG 的负 Int2 按绝对编号选择波长并输出 mm，不能被当作非法负编号或普通波长而丢失符号；0 仅作可保存的编辑占位，求值报错，选定波长后可继续编辑。快照校验原始 Int1 视场引用，旧 Field 别名不能掩盖无效引用。有效旧兼容行可升级但保留禁用状态；无效引用继续兼容保留。

2026-09-30 第十批：GMT/MTF/MSW 的 Samp/Wave/Field/Freq 与 Grid、!Scl、Data Type 按六个原始槽位保留；Samp 不作为面号映射。原始 Data1 为 Field 时新增整数/范围检查，不能由旧 Field 字段掩盖非法引用。仅 Grid=1 可执行，Grid=0 保留后求值明确报错。

2026-09-30 第九批：RW/MW 的 Int1 为 Ring/Samp，不参与表面编号映射；Int2 波长引用按原始槽位校验，旧字段有效但原始 Int2 越界时不能自动升级兼容行。六槽、目标、权重及禁用状态经 ZMX/STAROPT 和编辑往返保留。

2026-09-30 第八批：CEN/CNP/CNA 的非零 Surf 随坐标断点映射至物理面号，Surf=0 仍为像面别名；GS 的 Int1 是采样数，不参与表面编号转换。六个原始槽位经 ZMX、STAROPT 与桌面编辑往返保留。

## 原生 STAROPT 工程

桌面端唯一原生工程扩展名是 `.staropt`。它是版本化二进制容器，不是改后缀的 JSON。固定文件头包含 `STAROPT` 魔数、容器版本、Brotli 标志、压缩/解压长度和负载 SHA-256；保存使用临时文件与原子替换。

当前容器版本为2、工程负载版本为 6（新增显式多配置单元格变量）；顺序光学快照使用 schema 6 `OpticSnapshot`。负载保存：

- 系统名称、孔径、数值后端；
- 视场、波长和全部表面；
- 丰富的表面组件快照；
- 半径拾取、求解、评价操作数；
- 环境设置和有序当前玻璃目录；
- 全部光学配置、活配置索引，以及从属配置按表面/属性记录的继承断开关系；
- 独立非序列文档的波长、追迹默认值、10类类型化原生光源、几何/探测器对象、GUID引用关系，以及内容寻址的STL网格资产。

加载器除校验容器完整性外，还验证顺序主波长、有限数值、表面编号、组件布局，以及非序列对象类型、唯一GUID、引用图、光源参数/径向样本、网格资产哈希和集合上限。容器v1及工程负载v1–6继续兼容；保存始终写容器v2和负载v7。构建在临时文档中完成，全部成功后才替换活动状态。`OpticSnapshot` schema 1–5 会先迁移到 schema 6；新增瞳孔偏移和旋转缺省为 0。

顺序组件快照的新建内容使用 `approximate_transmission_ripple`、`main_ray_scatter_loss_approximation` 和 `mean_measured_scatter_loss`，明确表示这些是 Experimental 损耗近似。旧 `thin_film_stack`、`lambertian` 和 `measured_bsdf` kind 继续只读兼容，加载后迁移到准确命名的模型；它们不表示已经实现真实薄膜或 BSDF 物理。

非序列光线可另存为 `.starrdb`。该文件保存场景哈希、来源修订、追迹设置、随机种子、可选分裂模式、分支终止状态和分块压缩光线树；它是可重新生成的结果，不嵌入STAROPT。应用分别管理完整分析结果和有界3D布局结果：前者驱动数据库、路径和探测器，后者由非序列3D页面在打开、过期或用户手动刷新时生成。任何布局读取都会核对数据库头与当前场景哈希，过期光线默认不加载；显式查看时仍保留过期标记。详见[非序列第二阶段：杂散光基础链路](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)。

旧 `.optiland.json`、`.optic.json`、`.json` 和 `.optiland` 可继续读取用于迁移，但桌面“保存”不再生成这些格式。二进制结构见 [STAROPT 工程格式](STAROPT_FILE_FORMAT.md)。

## 镀膜实验文件

独立镀膜实验室使用 `.coating.json` schema 1，与主程序 `.staropt` 分开。保存膜系、完整材料公式/n/k 表和来源/有效范围、目标、采样/约束、候选、运行算法与版本、停止原因、评价次数、求解器版本、输入 SHA-256、光谱和逐项指标；公差结果还记录种子与输入哈希。正式 `BoundedFile` 提供有界读取和原子替换；格式/输入非法或结果哈希不匹配明确拒绝。重开有结果的实验后以冻结快照重新计算。此格式不承诺兼容 TFStudio 文件，也不进入当前镜头。

导出目录含 `layers.csv`、`spectrum.csv`、`metrics.csv`、`run.json`、`experiment.coating.json`。CSV 使用 nm、原始 R/T/A 比例以及 ln(T)/OD，保留 G17 数值，不使用界面舍入值或 OD 显示饱和值；每个文件原子写入，整个导出目录不是多文件事务。参见[镀膜实验室说明](../labs/CoatingDesign/README.md)。

## 公差文件

公差定义使用 `*.startol.json`，保存版本、操作数顺序和启用状态、类型、表面、上下偏差、分布、注释、评价准则、Monte Carlo 数量/种子、补偿迭代和良率阈值。公差编辑器独立跟踪未保存状态；新建、打开、镜头库载入和退出都会与原生工程一起进入统一保存确认。写入使用同目录临时文件和原子替换。

加载时验证表面范围、有限且有序的偏差、重复操作数以及至少一个有效非补偿操作数。该格式是 Workbench 自有的可读交换格式，不宣称兼容 Zemax 专有 `.TOL`。

## CAD 交换

“文件 > 导出 CAD”输出 AP203 `CONFIG_CONTROL_DESIGN` 的 `.step` / `.stp` 毫米模型：

- 顶层是镜头装配体，每个连续光学材料区间是独立命名的平面三角 `MANIFOLD_SOLID_BREP` 镜片零件；三角面共享边拓扑，胶合组按材料分件。
- 网格直接采样每个表面的真实 `Geometry.Sag(x,y)`，再通过该表面的 `CoordinateSystem` 转换到全局 XYZ；不复用查看器的轴对称旋转网格。
- 未支持的几何类型不会按平面解释。STAROPT和原生 JSON 将其数字、文本及递归子组件保存为不可计算的 opaque payload；追迹、分析、优化、公差、布局和导出前统一报告面号、原始类型及阻断原因。
- opaque 几何只允许保存到能完整保留该 payload 的原生格式。STEP、ZMX、CODE V SEQ、OSLO LEN及制造图纸等有损导出默认拒绝，不提供静默降级开关。
- 镜片实体外形使用 `SemiDiameter`。物理孔径只表示通光范围，不会被误切成材料孔洞；前后口径不同时，较小表面会在边缘 Sag 高度延伸为平坦环带，再以共同外径侧壁连接，与三维查看器的镜片边缘拓扑保持一致。
- `SurfaceSamples` 和 `AngularSamples` 是最低种子密度，默认继续细分到最大弦高误差不超过 `0.005 mm`；单片默认上限为 `500,000` 个三角形，超限会失败而不是静默降精度。
- 写出前检查非有限 Sag、占位自由曲面、退化面、非流形边、法向、正体积和三角网格自相交。错误包含镜片或表面编号；没有基底实体定义的反射面跳过并返回警告。
- 文件先写入目标目录中的临时文件，再原子替换目标；取消或失败不会破坏已有 STEP。

CI 在固定 `ubuntu-22.04` 生成 Cooke、非球面、偏心双锥面和胶合 Tessar STEP 样例，并把 fixture 作为 artifact 上传。FreeCAD/OpenCascade 导入验证在独立 job 中运行，固定使用 Ubuntu Jammy 的 `FreeCAD 0.19.2+dfsg1-3ubuntu1` 包，检查可导入性、唯一实体数量、形状有效性和正体积，并始终上传安装、版本和验证日志。验证环境安装失败只表示第三方验证环境不可用；商业 CAD 仍需按发布清单抽检。

SolidWorks 启用 3D Interconnect 并保留组件链接时，FeatureManager 会在 SolidWorks 文档根节点下显示一个带链接箭头的 STEP 根组件，再列出镜片零件；外层是宿主文档包装，不是文件中重复的装配体。若需要原生 SolidWorks 层级，可在“工具 > 选项 > 系统选项 > 导入 > 常规”中关闭 3D Interconnect 后重新导入，或对已导入根组件执行“断开链接”。

该路径仍是分面交换，不保留解析球面/非球面、NURBS、光学材料、镀膜、公差、机械倒角或镜筒约束。`.staropt` 仍是无损光学工程格式；进入机械设计或制造前必须在目标 CAD 中复核。

## 商业顺序格式

ZMX 切趾设置现已读取 `GFAC <因子> <类型>`，支持均匀（0）、高斯（1）和余弦立方（2），并在 STAROPT 保存、重开和 ZMX 导出中保留原始类型及因子。系统属性用带“Zemax”后缀的选项显示这些设置，参数标为“因子”；原有高斯模型仍使用 σ。高斯光线强度为 `exp(-2Gρ²)`，G=0 为均匀强度；余弦立方按当前入瞳半径和物面到入瞳的距离计算，因子仅保留、不参与计算。验证范围和依据见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

支持的扩展名：

- Zemax `.zmx`；
- Zemax 玻璃目录 `.agf`，在构建或离线工具中转换为 Workbench 存储；
- CODE V `.seq`；
- OSLO `.len`；
- 通用顺序 `.lens`、`.dat`、`.txt`。

ZMX 导入边界包括编码检测、顺序模式验证、`UNIT` 长度单位缩放、系统孔径、角度/物高/近轴像高/实像高视场、像方无焦标志、渐晕、波长、`GCAT`/`GLAS`、标准面、偶次/奇次非球面、基础环曲面、坐标断点、反射镜材料连续性、光阑、净半直径 `DIAM`、机械半直径 `MEMA` 和 `APMN`/`APMX`。布局光线按系统入瞳的归一化坐标发射；固定 `DIAM` 的有光焦度表面按 Zemax 规则建立同半径的浮动圆形口径，光阑面也按 `DIAM` 截光，`APMN` 继续建立中心遮拦。自动 `DIAM` 是包络真实光线的估算值，不额外变成截光孔径。`MEMA` 作为独立表面属性保留，只控制镜片实体外边界，绝不扩大入瞳或光线通光范围，也不从相邻 `DIAM` 推算；原生工程保存/重开、处方表、布局机械边界、制造数据、CAD 网格和 ZMX 再导出使用该值。源文件没有 `MEMA` 时才回退为该面的净半直径加本地延伸区（当前 ZMX 延伸区映射未实现，导入默认为 0）。长度单位会统一转换为 Workbench 内部毫米；非球面系数按当前几何公式的幂次同步缩放，多配置 `CRVT`、`THIC`、`APMX`、`APMN` 和 `PRAM` 也应用同一转换。

当前 ZMX 顺序导入器明确拒绝 Zemax 非序列文件、未支持的坐标断点顺序、经纬仪视场以及不可表示的环曲面项。有符号厚度按源值保留，不再把负厚度笼统视为非法。未映射到 Workbench 可计算几何的顺序 `TYPE` 不会让整个导入失败；导入器会把原始 Zemax `TYPE`、曲率、圆锥常数和 `PARM` 数据保存为不可计算的只读 opaque payload，UI 显示为不支持面型，追迹、分析、优化、布局和有损导出前由能力检查明确拦截。ZMX 不可靠保存 UI 活动配置，因此导入固定激活配置 1，同时保留全部配置。

`GCAT` 和 `GLAS` 先解析打包 Zemax 数据，再解析 独立的 refractiveindex.info 静态材料目录。无厂商同名玻璃按有序 `GCAT` 消歧。未匹配玻璃不会终止导入：保留原名称和其余处方数据，以 `UnresolvedMaterial` 标识，底部状态栏持续提示材料名与表面号；禁止用 GLAS 的 nd/Vd 参数或空气替代。STAROPT 保存/重开保留该状态。几何布局正常显示，但不生成依赖缺失材料的光线或分析结果；补选有效材料后恢复计算。缺失材料时保留文件中的厚度值，不执行依赖光线的 MAZH 厚度求解。`CORNING3` 中的 `C7980` 显式匹配本地目录的 `C79-80`，使用目录色散数据，不是参数近似。验证与示例见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

63 个 AGF 目录转换为一个版本化压缩 `zemax-glass-catalogs.ogdb`，包含 5,502 条记录。解析器支持公式 1–13 以及真实 Glasscat 文件中的 UTF-16、缺失值、旧式短记录和重复名称。用户补充目录转换为用户目录中的 `.ogcat`，成为可复用材料目录。

ZMX 导出写入 `UNIT MM`、系统孔径、视场、像方无焦标志、波长、主波长和有序 `GCAT`。导出只对可无损表达为 Zemax `STANDARD`、`EVENASPH`、`ODDASPHE` 和基础 `TOROIDAL` 的几何写出 `TYPE`；环形物理孔径写为 `APMN`，其它 Workbench 特有或尚未映射到 Zemax 的几何/孔径会明确失败，不再静默降级为 `TYPE STANDARD`。像方无焦按 Zemax 风格作为像空间角度坐标处理：点列图、光线扇形、RMS Spot、FFT/MMDFT/Huygens PSF、MTF 和波前/OPD 会在最终像面使用相对主光线的角度坐标（mrad）、角频率（cycles/mrad）和屈光度离焦（D）；波前参考由参考球切换为垂直主光线的平面。CODE V、OSLO 和通用顺序文本只覆盖公共表面字段。完整状态应使用 STAROPT。

Zemax 顺序操作数的目标边界见 [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)。当前 `[MS-L7]` 参考文件的 103 行评价函数已按源顺序导入；324 个 Zemax 顺序操作数已有受限可执行路径，覆盖 `TRAR`、范围厚度 `TTHI/TGTH`、实际光线径向坐标 `REAR`、实际光线角度 `RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`。当前 383 个名称已与官方 2026 R1 API 枚举核对的顺序兼容代码中，`DISC` 等未实现类型保持兼容只读；`DIST/DISA` 本地受限执行，原生导入仍只读，`DISG` 的负波长编号和 `DIMX` 的 Field/Absolute 已接通。兼容表中出现或能够往返的操作数不等于已经完成参数语义、求值和 Zemax 数值等价；新增执行路径也必须通过 Zemax/ZOS-API golden 对照后才能标为完整兼容。

## 文档服务

桌面统一通过：

```csharp
await application.Documents.OpenAsync(path);
await application.Documents.SaveAsync(path);
```

`OpticalDocumentService` 委托规范 `WorkbenchRuntime` 按内容和扩展名识别 STAROPT、旧 Workbench JSON 或商业格式适配器。旧连接器及 Compatibility 项目已删除，生产调用和行为测试统一使用 `WorkbenchRuntime`。

## 插件模型

插件实现：

```csharp
public interface IOptilandPlugin
{
    string Name { get; }
    void Register(PluginRegistry registry);
}
```

可注册几何工厂、材料实例和分析工厂。示例：

```csharp
public sealed class ExamplePlugin : IOptilandPlugin
{
    public string Name => "example";

    public void Register(PluginRegistry registry)
    {
        registry.RegisterGeometry("example-plane", () => new PlaneGeometry());
        registry.RegisterMaterial(new ConstantIndexMaterial("EXAMPLE-N", 1.52));
        registry.RegisterAnalysis("example-report", optic =>
            new SpotDiagramAnalysis(optic));
    }
}
```

目录发现使用 `new PluginLoader().LoadFromDirectory("plugins")`，进程内测试可使用 `LoadFromAssembly`。单个插件加载或注册失败只记录到 `PluginRegistry.Warnings`，不得阻止其他插件。当前插件模型是进程内全信任模型：DLL 会被加载到 Workbench 进程中执行，适用于本地受信任扩展，不适合作为运行未知第三方代码的沙箱。注册表对外发布只读集合视图，插件仍只能通过 `RegisterGeometry`、`RegisterMaterial` 和 `RegisterAnalysis` 增加能力。

## 已退役格式

Python Optiland 字典格式不再导入或导出；`.optiland-python.json`、`.python-optiland.json`（大小写不敏感）会在读取和保存前拒绝，也不再提供专用文件筛选器。扩展名拒绝检查只防止它们落入通用 `.json` 分派，不是兼容开关。把旧字典改名为 `.json` 也不能绕过原生快照的模式校验。旧 Workbench `.optiland`/`.optiland.json` 名称属于本项目原生格式，继续保留。

`zemax-zmx` 是 ZMX 的格式标识；不再附带历史参考软件版本。通用 JSON、原生快照、STAROPT、ZMX、SEQ、LEN、通用顺序文本和 .NET 插件基础设施保留。

## 镀膜快照读取的严格性

镀膜 schema 1 只接受可完整重建的材料模型；共享组件读取器用于旧工程的默认值补齐不能进入实验室快照路径。重建后核对规范字段和系数，缺失系数明确报错。文档读写受 64 MiB 共享上限约束；该实验格式未改变 STAROPT 版本或现有兼容 kind。数据字段和资源约束见[计算契约](COATING_DESIGN_LAB.md#资源和材料限制)。

## 镀膜日常设计扩展 · 2026-09-28

schema 1 新增可选 Structure/Search/Tolerancing 设置，缺省时不参与序列化，以保持旧实验指纹和重开兼容。候选可保存局部 RunRecord；公差保存实际膜厚、角度、逐项检查和错误，可额外导出 tolerance.csv 与 tolerance.json。材料交换为显式 wavelength_nm,n,k 或 wavelength_um,n,k 表头的 CSV/TSV，2–10000 行、4 MiB 上限；原有解析材料不会静默改为插值表。数据范围、正态误差和浮点边界说明见[计算契约](COATING_DESIGN_LAB.md)。

## 延伸区的导出边界（2026-10-02）

本地 `ChipZone` 已进入 JSON/STAROPT；CAD/STEP 曲面延伸到净半径加延伸区，然后生成机械平环。外部 ZMX/CODE V/OSLO/通用处方的非零延伸区列尚未验证，当前明确拒绝导出，避免静默丢失。TSAG 原始 MFE 行仍禁用保留，本地八槽可执行。见[完整范围](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。


指定频率 MTF 制造公差：FFT/几何 MTF、逐视场极限/增量反求和联合良率、有界间隔/单表面偏心/倾斜补偿、startol v3（兼容 v1/v2）已实现；未认证原生 Zemax 公差数值。见 [使用、边界与验证](MTF_TOLERANCING_2026-10-04.md)。
