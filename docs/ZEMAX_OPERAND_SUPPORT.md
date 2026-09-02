# Zemax 顺序模式操作数支持规范

当前状态复核：2026-09-02。本文是顺序模式操作数的目标规范与验收矩阵；“必须支持”不等于当前版本已经实现，实际完成度以文末“当前基线与差距”为准。代码、文件字段和产品专有名词保留原始英文标识。

## 目的

本文定义 Optical System Design 对 Zemax OpticStudio 评价函数操作数的完整支持边界。它既是实现规范，也是验收矩阵，不是对当前代码覆盖率的夸大声明。

目录基线以本机 Ansys Zemax OpticStudio 2026 R1（26.1.0）实际运行的 ZOS-API 为准，并对照官方 `Optimization Operands Summary`。2026-09-02 实测 `MeritOperandType` 枚举有 448 项，新建顺序系统的 MFE 可选择 442 项；剔除其中 24 个 `PnGT/PnLT/PnVA` 废弃代码和 35 个在该列表中出现的非序列/遗留代码后，本项目注册 383 个可用于顺序兼容的代码。`NSRW/NSTW` 只出现在非序列 MFE，不计入上述 442 项。

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
- GRIN 操作数使用弯曲光线路径及局部折射率梯度；缺少所需 GRIN 数据时返回明确的不可计算错误。
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

### 2026 R1 专用兼容项

以下 12 项由当前 ZOS-API 顺序 MFE 实际暴露，但官方 quick-reference 分类表未完整归类；先作为只读兼容项注册：

`BIPF`, `COSA`, `HACG`, `MNAI`, `MXAI`, `OGSS`, `QOAC`, `REQS`, `RRET`, `SPHD`, `TRAN`, `TSAG`

## 明确排除

### 非序列物体数据约束

以下 23 个分类条目属于非序列物体数据，不在本轮顺序模式操作数范围内：

`FREZ`, `NPGT`, `NPLT`, `NPVA`, `NPXG`, `NPXL`, `NPXV`, `NPYG`, `NPYL`, `NPYV`, `NPZG`, `NPZL`, `NPZV`, `NSRM`, `NTXG`, `NTXL`, `NTXV`, `NTYG`, `NTYL`, `NTYV`, `NTZG`, `NTZL`, `NTZV`

### 非序列光线追迹和探测器

以下 15 个非序列或遗留分类条目不在本轮范围内：

`NPAF`, `NSDC`, `NSDD`, `NSDE`, `NSDP`, `NSLT`, `NSRA`, `NSRD`, `NSRM`, `NSRW`, `NSST`, `NSTR`, `NSTW`, `REVR`, `RSNC`

`NSRM` 同时出现在两个非序列分类中，因此非序列/遗留排除集共有 37 个唯一代码。其中 `NSRW/NSTW` 只出现在非序列系统 MFE，其余 35 项也会由顺序 MFE 的 `AvailableOperandTypes()` 返回，仍必须依据类别排除。

### 当前 API 不可选择或非真实代码

`INGT/INLT/INVA` 是误读族名产生的非真实代码；`OMMI/OMMX/OMSD` 不存在于 2026 R1 ZOS-API 枚举；`UDOP/XDGT/XDLT/XDVA` 虽保留于枚举但无法在新建顺序系统的 MFE 中选择。这 10 项不进入当前注册表。

### 废弃操作数

参考目录明确标记的 `PnGT`、`PnLT`、`PnVA` 操作数族不实现，不导入为可执行操作数。导入旧文件时应给出包含代码和行号的兼容性诊断。

## 当前基线与差距

截至 2026-09-02，`ZemaxOperandRegistry` 精确注册本机 2026 R1 实测边界内的 383 个顺序兼容代码，`MeritFunctionCatalog.Types` 另保留 `RWFE`、`FNUM`、`RADI`、`THIC` 四个 Workbench 友好代码。注册表把当前已连接计算引擎的 111 个 Zemax 代码标为 `Executable`，其余标为 `CompatibilityOnly`；这只是代码级计算连接状态，不自动满足本文“支持”的九项定义。

`MeritOperandDefinition` 和 `MeritOperandSnapshot` 现在独立保存 `Int1`、`Int2` 与 `Data1`–`Data4` 原始槽位，并保存逐行 `CompatibilityOnly` 状态。`ZemaxOperandDescriptor` 进一步记录槽位名称、引用类型和单位，用于区分 `Int2` 是波长、终止表面、行引用还是普通整数。Application 合同把这些描述符和六个原始槽位直接发布给桌面编辑器；编辑器按当前行显示参数名与单位，把 `Unused` 和兼容只读槽位锁定，并在保存时保持原始槽位为权威数据，再由描述符恢复 Workbench 类型化字段。ZMX 导入器除已有类型化分支外，会识别全部 383 个目标代码并将尚无参数语义的行禁用保留；STAROPT 往返保持原始槽位。即使外部调用强行启用兼容行，评价仍返回不可执行错误和无限贡献，不能成为成功零值。单行原始整数或数据槽位最多 16 项，数据必须有限。

参考镜头 `[MS-L7](10X大NA大视场).ZMX` 的 103 行继续按源顺序全部进入内存；其中 `TRAR` 使用现有类型化光线分支，`TTHI` 按起止表面计算轴向范围厚度，`REAR` 按实际光线位置计算径向坐标，`RANG` 按实际光线方向角计算弧度值，`CONS`、`SINE`、`COSI`、`TANG`、`ASIN`、`ACOS`、`ATAN`、`ABSO`、`SQRT`、`RECI`、`LOGE`、`LOGT`、`SUMM`、`PROD`、`DIVI`、`DIFF`、`MAXX` 和 `MINN` 已接入有序评价函数上下文。`SUMM/PROD/DIFF/DIVI` 按 Zemax 双行引用求值，`MAXX/MINN` 按行范围求值，三角函数 Flag 按 Zemax 的弧度/角度语义处理，`LOGE/LOGT` 对非正输入返回 0。常见镜头/厚度/一阶数据束已完成定义级可执行接入：`CTGT/CTLT/CTVA`、`ETGT/ETLT/ETVA`、`FTGT/FTLT`、`STHI`、`TTGT/TTLT/TTVA`、`TTHI/TGTH`、`MNCA/MXCA/MNEA/MXEA/MNCG/MXCG/MNEG/MXEG/MNCT/MXCT/MNET/MXET`、`XNEA/XXEA/XNEG/XXEG/XNET/XXET`、`CVGT/CVLT/CVVA/MNCV/MXCV`、`COGT/COLT/COVA`、`MNSD/MXSD`、`WLEN/INDX`、`EFFL/EFLX/EFLY/ENPP/EPDI/EXPP/EXPD/ISNA/ISFN/SFNO/WFNO` 以及 `PMAG/PETZ`。边界操作数采用 Zemax 风格“满足时返回目标值、越界时返回实际值”；`TTGT/TTLT/TTVA` 按官方定义计算指定表面至下一表面、指定边缘方向处的总厚度，不再误用系统总长。`DIMX` 已按实测改为 `Field/Wave/Absolute` 描述符，但在指定视场和绝对长度模式尚未实现前降为兼容只读；`EFNO` 也按内置 `Samp/Wave/Field/Pol?` 协议兼容保留。上述新增路径仍必须经过 Zemax/ZOS-API golden 数值对照后，才能标为完整兼容。

有序评价控制流已接入 `GOTO`、`ENDX` 和 `OOFF`：`GOTO` 仅允许跳向函数内的后续行，被跳过的行不进入引用上下文；`ENDX` 终止后续评价；`OOFF` 保留为零贡献惰性行。非法向后或越界跳转返回明确错误。`SKIN/SKIS` 的条件对称控制仍保持兼容只读，不能据此宣称控制行已经全部完成。

`[MS-L7]` 的 103 行已通过本机 OpticStudio 2026 R1 ZOS-API 采集为可重复 golden：源 SHA-256、行顺序和 400 余个活动参数槽已锁定；两行 `TTHI` 以及 `OPLT`、`CTGT`、`EFFL`、`CONS`、`REAR`、`PETZ`、`MNCA`、`MNCG`、`MNEG`、`MXCG` 共 12 个代表行通过当前数值对照。`PETZ` 已据此修正像方曲率符号；`TTHI/TGTH` 按官方“起止表面厚度均包含”定义修正终止端点。其余新增路径仍必须继续经过 golden 收敛，才能标为完整兼容；当前已确认的差异集中在高 NA 光线、边厚、近轴放大率及依赖数学行。

本机实测还校正了已执行项的槽位：`RSCE/RSCH/RSRE/RSRH` 使用 `Ring/Wave/Hx/Hy`；`MECS/MECT` 的第一个参数为空；`CT*/CV*/CO*` 只使用 `Surf`；`ET*/TT*` 的 `Mode` 位于 `Data2`，`FT*` 的 `Mode` 位于 `Data2`，`STHI` 的 `Mode` 位于 `Data3`；中心厚度范围和半口径范围项只使用 `Surf1/Surf2`；行边界操作数只使用 `Op#`。

本次修正消除了目标目录代码静默丢弃、编辑保存时原始槽位被固定友好字段覆盖的问题，并开始为已知操作数补充参数描述符，但没有把“383 项可无损显示”扩大宣称为“383 项完整 Zemax 评价函数支持”。描述符的逐类型参数名、单位、校验规则和计算引擎仍需按下述实施顺序完成。

当前仍需消除以下技术债：

- 编辑器已经消费描述符并按当前行切换六个原始槽位的列名、单位和只读状态；尚未补齐全部 383 项的专用参数语义、范围、默认值和枚举选择器；
- 快照校验已经能区分 `TTHI/TGTH`、常见 `MN*/MX*/X*` 厚度范围项的终止表面槽位以及基础数学操作数的行引用槽位，但大多数 Zemax 操作数仍需逐类型校验规则；
- 参考文件之外的未注册代码仍可能被导入器忽略；
- 控制和质心类操作数仍缺少完整有序行执行语义；基础数学行已具备前序行读取、Flag 角度处理、Zemax 双行/范围区分和错误报告，且 `OPLT/OPGT/ABGT/ABLT/OPVA` 已可按前序行约束求值；`EQUA/PROB/OSUM/QSUM/DIVB` 的 Zemax 特殊贡献语义仍需继续实现；
- 分析型操作数缺少统一的参数化缓存和取消边界。

2026-08-29 已完成能力真实性闸门：`CanonicalType` 对未知代码明确失败；启用的只读兼容操作数返回不可执行错误；只有禁用兼容行以及显式 `BLNK/DMFS` 才产生零贡献。未实现代码不再被规范化为 `BLNK` 或作为成功零值参与优化。

任何阶段性提交都必须在支持矩阵中标为“部分”，直至满足本文“支持”的九项定义。

## 实施顺序

1. **注册表与快照模型**：383 项 2026 R1 实测代码注册、通用原始参数槽位、行级兼容状态和旧 schema 默认迁移已完成；已补充首批光线、RMS、Moore-Elliott、`EFFL/TOTR/TTHI/TGTH`、常见镜头/厚度/曲率/圆锥/半口径/玻璃折射率与基础数学行描述符；逐类型参数元数据及验证继续补齐。
2. **ZMX 导入/导出**：元数据驱动解析所有目标代码，保留源顺序和参数，拒绝非序列/废弃项并给出诊断。
3. **基础约束和一阶量**：完成系统、镜头、玻璃、参数、一阶和属性类。
4. **光线与像差**：复用按需追迹、波前、像差和介质状态正确性基线。
5. **分析型操作数**：接入 MTF、能量、鬼像、光纤、POP、高斯、GRIN、镀膜和偏振引擎。
6. **数学与控制流**：基础数学行已接入有序评价函数入口，支持前序行引用、除零和非法数学域错误；控制流和 Zemax 特殊约束数学仍需继续实现。
7. **宏和用户扩展**：实现受限提供程序协议。
8. **全量验收**：对 383 个代码逐项完成导入、参数、数值、往返和非法输入测试。

## 验收门槛

- 注册表包含且只包含 383 个本规范目标代码，另可保留有文档依据的 Workbench 别名。
- 非序列/遗留 37 个唯一代码和 24 个废弃 `Pn*` 代码不会进入顺序注册表。
- 每个目标代码至少一个有效数值用例和一个非法参数用例。
- ZMX 全目录夹具导入后不产生未知代码、列错位或引用误判。
- STAROPT 往返保持所有操作数及其类型化参数。
- 串行/并行评价得到相同的有序结果；取消不会修改活动光学系统。
- 全反射介质、反射吸收、薄透镜方向归一化、OPL/OPD 和现有 Python 数值基线继续通过。
