# Zemax 顺序模式操作数支持规范

当前状态复核：2026-08-30。本文是顺序模式操作数的目标规范与验收矩阵；“必须支持”不等于当前版本已经实现，实际完成度以文末“当前基线与差距”为准。代码、文件字段和产品专有名词保留原始英文标识。

## 目的

本文定义 Optical System Design 对 Zemax OpticStudio 评价函数操作数的完整支持边界。它既是实现规范，也是验收矩阵，不是对当前代码覆盖率的夸大声明。

目录基线取自 [光学课堂 Zemax OpticStudio 操作数手册](https://www.optkt.com/hb/operands/)，核对日期为 2026-07-29。该页面的分类清单共有 337 个条目；去除跨分类重复的 `CONF`、`MNPD`、`ZTHI` 和 `POPD` 后，本项目必须支持 333 个唯一的顺序模式操作数。

除下述两类明确排除项外，不得因为实现困难、计算成本、需要其他分析引擎或当前 UI 没有对应入口而省略操作数：

1. 参考目录明确列为废弃的 `PnGT`、`PnLT`、`PnVA` 操作数族；
2. 参考目录明确列为非序列物体数据或非序列光线追迹/探测器专用的操作数。

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

操作数代码统一使用大写；参考目录中的 `InGT`、`InLT`、`InVA` 在内部规范化为 `INGT`、`INLT`、`INVA`。


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
| 镜头参数约束 | 50 | `COGT`, `COLT`, `COVA`, `CTGT`, `CTLT`, `CTVA`, `CVGT`, `CVLT`, `CVVA`, `BLTH`, `DMGT`, `DMLT`, `DMVA`, `ETGT`, `ETLT`, `ETVA`, `FTGT`, `FTLT`, `MNCA`, `MNCG`, `MNCT`, `MNCV`, `MNEA`, `MNEG`, `MNET`, `MNPD`, `MXCA`, `MXCG`, `MXCT`, `MXCV`, `MXEA`, `MXEG`, `MXET`, `MNSD`, `MXSD`, `OMMI`, `OMMX`, `OMSD`, `TGTH`, `TTGT`, `TTHI`, `TTLT`, `TTVA`, `XNEA`, `XNET`, `XNEG`, `XXEA`, `XXEG`, `XXET`, `ZTHI` |
| 玻璃数据约束 | 10 | `GCOS`, `GTCE`, `INDX`, `MNAB`, `MNIN`, `MNPD`, `MXAB`, `MXIN`, `MXPD`, `RGLA` |
| 组件位置约束 | 7 | `GLCA`, `GLCB`, `GLCC`, `GLCR`, `GLCX`, `GLCY`, `GLCZ` |
| 参数数据约束 | 3 | `PMGT`, `PMLT`, `PMVA` |
| 额外数据约束 | 3 | `XDGT`, `XDLT`, `XDVA` |
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
| 一阶光学性能 | 20 | `AMAG`, `ENPP`, `EFFL`, `EFLX`, `EFLY`, `EPDI`, `EXPD`, `EXPP`, `ISFN`, `ISNA`, `LINV`, `OBSN`, `PIMH`, `PMAG`, `POWF`, `POWP`, `POWR`, `SFNO`, `TFNO`, `WFNO` |
| 镜头属性约束 | 16 | `CVOL`, `MNDT`, `MXDT`, `SAGX`, `SAGY`, `SSAG`, `STHI`, `TMAS`, `TOTR`, `VOLU`, `NORX`, `NORY`, `NORZ`, `NORD`, `SCUR`, `SDRV` |
| 近轴光线数据约束 | 13 | `PANA`, `PANB`, `PANC`, `PARA`, `PARB`, `PARC`, `PARR`, `PARX`, `PARY`, `PARZ`, `PATX`, `PATY`, `YNIP` |
| 实际光线数据约束 | 43 | `CEHX`, `CEHY`, `CENX`, `CENY`, `CNAX`, `CNAY`, `CNPX`, `CNPY`, `DXDX`, `DXDY`, `DYDX`, `DYDY`, `HHCN`, `IMAE`, `MNRE`, `MNRI`, `MXRE`, `MXRI`, `OPTH`, `PLEN`, `RAED`, `RAEN`, `RAGA`, `RAGB`, `RAGC`, `RAGX`, `RAGY`, `RAGZ`, `RAID`, `RAIN`, `RANG`, `REAA`, `REAB`, `REAC`, `REAR`, `REAX`, `REAY`, `REAZ`, `RENA`, `RENB`, `RENC`, `RETX`, `RETY` |

实现要求：

- 一阶量必须明确子午/弧矢、物方/像方、近轴/实际定义及符号约定。
- 光线操作数使用按需指定面追迹，复用同一评价批次中的光线样本。
- 方向余弦、角度、法线、光程和路径长度必须区分；全反射后的当前介质必须保持在入射侧。
- 最小/最大真实光线与强度类操作数必须定义渐晕、零强度和无有效光线时的错误行为。

### MTF、像差、能量与专项分析

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| MTF 数据 | 15 | `GMTA`, `GMTS`, `GMTT`, `MECA`, `MECS`, `MECT`, `MSWA`, `MSWS`, `MSWT`, `MTFA`, `MTFS`, `MTFT`, `MTHA`, `MTHS`, `MTHT` |
| 傅科分析 | 1 | `FOUC` |
| 像差 | 50 | `ABCD`, `ANAC`, `ANAR`, `ANAX`, `ANAY`, `ANCX`, `ANCY`, `ASTI`, `AXCL`, `BIOC`, `BIOD`, `BSER`, `COMA`, `DIMX`, `DISA`, `DISC`, `DISG`, `DIST`, `FCGS`, `FCGT`, `FCUR`, `LACL`, `LONA`, `OPDC`, `OPDM`, `OPDX`, `OSCD`, `PETC`, `PETZ`, `RSCE`, `RSCH`, `RSRE`, `RSRH`, `RWCE`, `RWCH`, `RWRE`, `RWRH`, `SMIA`, `SPCH`, `SPHA`, `TRAC`, `TRAD`, `TRAE`, `TRAI`, `TRAR`, `TRAX`, `TRAY`, `TRCX`, `TRCY`, `ZERN` |
| 鬼像聚焦控制 | 6 | `GPIM`, `GPRT`, `GPRX`, `GPRY`, `GPSX`, `GPSY` |
| 光纤耦合 | 3 | `FICL`, `FICP`, `POPD` |
| 相对照度 | 1 | `ZPLM` |
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
| 梯度折射率控制 | 7 | `DLTN`, `GRMN`, `GRMX`, `INGT`, `INLT`, `INVA`, `LPTD` |
| 镀膜与偏振追迹 | 10 | `CMGT`, `CMLT`, `CMVA`, `CODA`, `CEGT`, `CELT`, `CEVA`, `CIGT`, `CILT`, `CIVA` |
| 物理光学传播 | 2 | `POPD`, `POPI` |

实现要求：

- 高斯光束和 POP 使用各自传播模型，不得退化为几何 RMS 光斑。
- GRIN 操作数使用弯曲光线路径及局部折射率梯度；缺少所需 GRIN 数据时返回明确的不可计算错误。
- 镀膜/偏振操作数使用 Jones/Fresnel 状态、实际入射角和反射/全反射分支。
- 需要外部文件或网格的数据必须纳入快照资源策略和边界校验。

### 数学、控制、宏和用户扩展

| 分类 | 数量 | 必须支持的代码 |
| --- | ---: | --- |
| 通用数学 | 28 | `ABSO`, `ACOS`, `ASIN`, `ATAN`, `CONS`, `COSI`, `DIFF`, `DIVB`, `DIVI`, `EQUA`, `LOGE`, `LOGT`, `MAXX`, `MINN`, `OPGT`, `OPLT`, `OPVA`, `OSUM`, `PROB`, `PROD`, `QSUM`, `RECI`, `SQRT`, `SUMM`, `SINE`, `TANG`, `ABGT`, `ABLT` |
| 评价函数控制 | 8 | `BLNK`, `DMFS`, `ENDX`, `GOTO`, `OOFF`, `SKIN`, `SKIS`, `USYM` |
| ZPL 宏优化 | 2 | `UDOC`, `UDOP` |
| 用户自定义 | 2 | `RELI`, `EFNO` |

实现要求：

- 数学和控制操作数在稳定的行号模型上执行，支持向前/向后范围、条件跳转、跳过和关闭行。
- 除零、非法对数/反三角输入、循环跳转和越界行引用必须产生确定且可诊断的错误。
- `UDOC`、`UDOP`、`RELI`、`EFNO` 通过受限扩展提供程序执行。完整支持指宿主协议、参数传递、超时、取消、异常隔离和确定性返回均已实现；没有安装用户提供程序时必须明确报告缺失，不能返回伪值。

## 明确排除

### 非序列物体数据约束

以下 23 个分类条目属于非序列物体数据，不在本轮顺序模式操作数范围内：

`FREZ`, `NPGT`, `NPLT`, `NPVA`, `NPXG`, `NPXL`, `NPXV`, `NPYG`, `NPYL`, `NPYV`, `NPZG`, `NPZL`, `NPZV`, `NSRM`, `NTXG`, `NTXL`, `NTXV`, `NTYG`, `NTYL`, `NTYV`, `NTZG`, `NTZL`, `NTZV`

### 非序列光线追迹和探测器

以下 13 个分类条目属于非序列追迹/探测器，不在本轮范围内：

`NSDC`, `NSDD`, `NSDE`, `NSDP`, `NSLT`, `NSRA`, `NSRD`, `NSRM`, `NSRW`, `NSST`, `NSTR`, `NSTW`, `REVR`

`NSRM` 同时出现在两个非序列分类中，因此非序列排除集共有 35 个唯一代码。

### 废弃操作数

参考目录明确标记的 `PnGT`、`PnLT`、`PnVA` 操作数族不实现，不导入为可执行操作数。导入旧文件时应给出包含代码和行号的兼容性诊断。

## 当前基线与差距

截至 2026-08-30，`ZemaxOperandRegistry` 精确注册本文的 333 个唯一顺序目标代码，`MeritFunctionCatalog.Types` 另保留 `RWFE`、`FNUM`、`RADI`、`THIC` 四个 Workbench 友好代码。注册表把当前已连接计算引擎的 27 个 Zemax 代码标为 `Executable`，其余标为 `CompatibilityOnly`；这只是代码级计算连接状态，不自动满足本文“支持”的九项定义。

`MeritOperandDefinition` 和 `MeritOperandSnapshot` 现在独立保存 `Int1`、`Int2` 与 `Data1`–`Data4` 原始槽位，并保存逐行 `CompatibilityOnly` 状态。ZMX 导入器除已有类型化分支外，会识别全部 333 个目标代码并将尚无参数语义的行禁用保留；STAROPT 往返保持原始槽位。即使外部调用强行启用兼容行，评价仍返回不可执行错误和无限贡献，不能成为成功零值。单行原始整数或数据槽位最多 16 项，数据必须有限。

参考镜头 `[MS-L7](10X大NA大视场).ZMX` 的 103 行继续按源顺序全部进入内存；其中 `TRAR` 使用现有类型化光线分支，`TTHI`、`CTGT`、`PMAG`、`DIVI`、`REAR`、`DIMX`、`PETZ`、`MXEG` 和 `SINE` 保持禁用兼容记录。禁用兼容只表示识别和往返，不表示已经具备 Zemax 等价计算。

本次修正消除了目标目录代码静默丢弃和原始槽位借用友好字段作为唯一存储的问题，但没有把“333 项可无损显示”扩大宣称为“333 项完整 Zemax 评价函数支持”。描述符的逐类型参数名、单位、校验规则和计算引擎仍需按下述实施顺序完成。

当前仍需消除以下技术债：

- 编辑器仍以 `Surface/Field/Wavelength/Hx/Hy/Px/Py` 显示友好视图，尚未按描述符切换全部 333 项的列名和单位；
- 对所有类型套用同一表面/视场/波长校验；
- 参考文件之外的未注册代码仍可能被导入器忽略；
- 数学、控制和质心类操作数缺少有序行执行上下文；
- 分析型操作数缺少统一的参数化缓存和取消边界。

2026-08-29 已完成能力真实性闸门：`CanonicalType` 对未知代码明确失败；启用的只读兼容操作数返回不可执行错误；只有禁用兼容行以及显式 `BLNK/DMFS` 才产生零贡献。未实现代码不再被规范化为 `BLNK` 或作为成功零值参与优化。

任何阶段性提交都必须在支持矩阵中标为“部分”，直至满足本文“支持”的九项定义。

## 实施顺序

1. **注册表与快照模型**：333 项代码注册、通用原始参数槽位、行级兼容状态和旧 schema 默认迁移已完成；逐类型参数元数据及验证继续补齐。
2. **ZMX 导入/导出**：元数据驱动解析所有目标代码，保留源顺序和参数，拒绝非序列/废弃项并给出诊断。
3. **基础约束和一阶量**：完成系统、镜头、玻璃、参数、一阶和属性类。
4. **光线与像差**：复用按需追迹、波前、像差和介质状态正确性基线。
5. **分析型操作数**：接入 MTF、能量、鬼像、光纤、POP、高斯、GRIN、镀膜和偏振引擎。
6. **数学与控制流**：实现有序评价函数虚拟机及循环/错误防护。
7. **宏和用户扩展**：实现受限提供程序协议。
8. **全量验收**：对 333 个代码逐项完成导入、参数、数值、往返和非法输入测试。

## 验收门槛

- 注册表包含且只包含 333 个本规范目标代码，另可保留有文档依据的 Workbench 别名。
- 非序列 35 个唯一代码和废弃三族不会进入可执行评价函数。
- 每个目标代码至少一个有效数值用例和一个非法参数用例。
- ZMX 全目录夹具导入后不产生未知代码、列错位或引用误判。
- STAROPT 往返保持所有操作数及其类型化参数。
- 串行/并行评价得到相同的有序结果；取消不会修改活动光学系统。
- 全反射介质、反射吸收、薄透镜方向归一化、OPL/OPD 和现有 Python 数值基线继续通过。
