# STAROPT 工程格式

更新：2026-11-01。本页是容器、工程负载和快照版本的唯一文档来源：当前分别为 **2 / 最大 20 / 6**，与 `StarOptProjectStore`、`OpticSnapshotValidator` 一致。复合光学组件或像面口径端口（包括禁用对象）写v20；原有无BSDF场景仍写v7。绝对正值XYZ径向基模型（MeasuredBsdfGrid/11，含禁用对象）写v19；显式对数角插值（Grid/10）写v18；显式实测极角支持域（Grid/9）写v17；正值XYZ颜色模型（Grid/8）写v16；有域XYZ校准（Grid/7）写v15；Speos White运行时（Grid/6）写v14；θ线性v11（Grid/5）写v13；原镜面增强/可分光谱/XYZ（Grid/4）写v12；投影数量或Zemax镜面中心表格写v11；商业文本/固定轴写v10，各向同性CSV实测写v9；仅Lambertian写v8，无BSDF仍v7。读取支持v1–20，低版本夹带新模型拒绝；旧Grid/1–11及冻结摘要不升级。当前范围见[全入射角重建与绝对XYZ校准](XYZ_LOG_RECONSTRUCTION_2026-11-01.md)，此前[有限角表](XYZ_MEASURED_ANGULAR_2026-11-01.md)、[已知条件XYZ](XYZ_KNOWN_CONDITIONS_2026-11-01.md)、[White实际功率](WHITE_SPECULAR_RUNTIME_2026-10-31.md)保留原阶段范围。文件入口与交换限制见[文件格式指南](FILE_FORMATS_AND_PLUGINS.md)。

1.0.2 恢复副本使用正式 STAROPT 完整工程序列化，不是镜头表局部导出；原子写入副本后才发布独立 SHA-256 元数据。恢复打开不使用副本位置作为用户保存路径，而是保持未保存并要求另存。新增容器 1 / 载荷版本 1–7 与旧 JSON 版本 1–6 的读取、当前格式另存及原字节保留回归；不承诺未知或任意损坏格式迁移，见[恢复与迁移验收](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)。

2026-10-03 行表更新：工程负载升级到 v5，新增有序 OperandRows（Kind、SurfaceNumber）列表。值仍存储在各配置的表面数据中，不另存数值副本。v1–4 缺少列表时按空表读取；v4 或更早版本带非空行表明确拒绝。未知类型、重复绑定、无效/非有限引用会拒绝；半口径断开链接纳入校验。容器 v2、顺序快照 schema 6 不变。旧应用应拒绝 v5；有行表的工程只能保存 STAROPT。 详见[本批记录](MCE_ROW_OPERANDS_2026-10-03.md)。

2026-10-03 多配置更新：本批没有升级格式版本。CONF/ZTHI 复用已有操作数整数/数据槽位，完整配置、活动配置号及断开链接仍由 STAROPT 保留。包含活动跨配置评价函数的多配置文档，保存为仅含活动镜头的 JSON/文本会明确拒绝，现有目标文件不被覆盖；本地 CONF/ZTHI 未核实文本列也禁止导出。 详见[本批记录](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。

2026-10-03 膜层约束：coherent_multilayer 的 numbers 新增每层 multiplier/indexOffset/extinctionOffset 和三个 *Variable 标记，变量为 0/1。旧快照省略时使用倍率 1、偏移 0、固定状态。基础材料仍以完整子快照保存，基础厚度不被倍率覆盖；层数、有限数、倍率范围和变量标记严格验证。文本 ZMX/SEQ/LEN/普通处方尚无物理膜层映射，拒绝导出含物理膜层的工程并提示保存 STAROPT。 详见[实现与验证](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03 CODA：schema 6 增加可选 polarization 对象，保存 unpolarized、jx/jy、xPhaseDegrees/yPhaseDegrees、referenceAxis。旧快照缺省为本地非偏振、Jones=(1,0)、零相位及 X 参考；这不是对原生默认的声明。已出现的非法状态严格拒绝。CODA 六槽可本地编辑保存，原生行不自动升级；尚无原生全局偏振文件映射，带自定义偏振的 ZMX/SEQ/LEN/普通处方文本导出拒绝静默丢失设置。 详见[实现与验证](COATING_DATA_OPERAND_2026-10-03.md)。

2026-10-03 RRET：RRET 本地八槽 Ring/Wave/Hx/Hy/Jx/Jy/X-Phase/Y-Phase 可编辑保存，Phase 的单位为度；原生 ZMX 及旧兼容行保持禁用只读，不自动补齐未核实的尾列，也不自动升级。新增复电场是运行时数据，工程 schema 不变。 详见[实现与验证](POLARIZATION_RETARDANCE_2026-10-03.md)。

2026-10-03 HYLD：HYLD 本地六槽 Surf/Wave/Hx/Hy/Px/Py 可编辑保存；原生导入及旧兼容行保持禁用只读，未验证前不自动升级。新增界面折射率是运行时追迹数据，不改变工程 schema。 详见[计算与验证](HIGH_YIELD_OPERAND_2026-10-03.md)。

`.staropt` 是 Optical System Design 的原生无损工程格式，是版本化二进制容器而不是改名 JSON。

## 目标

- 不依赖扩展名识别工程；
- 构造光学模型前拒绝截断或被修改文件；
- 拒绝校验和正确但语义非法的负载；
- 保存全部顺序配置、活动配置、配置属性继承断开关系和独立非序列文档；
- 容器版本与光学 schema 独立演进；
- 原子写入，失败保存不替换旧文件。

## 容器布局

整数均为小端：

| 偏移 | 大小 | 含义 |
| ---: | ---: | --- |
| 0 | 8 | `STAROPT` 加 `0x1A` 魔数 |
| 8 | 2 | 容器版本，当前为 2 |
| 10 | 2 | 标志，当前 1 表示 Brotli |
| 12 | 4 | 未压缩负载长度 |
| 16 | 4 | 压缩负载长度 |
| 20 | 32 | 未压缩负载 SHA-256 |
| 52 | 负载头声明值 | Brotli 压缩 UTF-8 工程清单 |
| 后续 | 可变 | 容器v2网格资产头和Brotli压缩的 `STARMESH` 数据 |

工程清单未压缩上限为256 MiB。容器v2的每个网格资产头为64字节：16字节GUID、8字节未压缩长度、8字节压缩长度和32字节SHA-256，随后为压缩资产。网格资产总解压上限512 MiB、数量上限4096。加载器验证长度、版本、解压结果、清单/资产交叉引用和固定时间SHA-256比较。内存中的网格资产保存为不可变内容快照：附加或导入的字节会被复制，公开 API 只提供只读字节视图或显式复制，解码几何不暴露可改写的底层数组。保存先写目标目录中的临时文件，完整刷新后原子替换。

## 工程负载

当前工程负载最大版本为 20（读取 v1–20）。v20保存`OpticalAssemblyParameters`的逐面几何、完整介质/镀膜/口径、原面号/标签及绕轴旋转，或探测器可选`IncidentSurface`口径端口；任一载荷包括禁用对象都要求v20，低版本夹带即拒绝。组件采用只读冻结数据，场景哈希绑定实际参数。转换支持域见[胶合与浸没镜组](CEMENTED_IMMERSION_NSC_2026-11-01.md)。v19保存`ZemaxXyz.Calibration.AbsoluteRbfModel`及`FitEvidence.AbsoluteRbfModelSha256`，进入MeasuredBsdfGrid/11摘要；训练绝对XYZ/辐射度、特征方式/尺度、近邻/平滑/响应偏置、强度域和原条件证据绑定，不能任意按光强缩放。v18保存`LogInterpolation`的尺度和方法，进入MeasuredBsdfGrid/10摘要，仅用于普通辐射度网格；和原极角支持域一起保留，不兼容投影/Zemax/Speos特殊分布。低版本夹带新模型拒绝，包括禁用对象。v17保存普通辐射度网格的`AngularSupport=MeasuredPolarRangeZeroOutside`，进入MeasuredBsdfGrid/9摘要；保留实测极角域、选中原入射/波长节点及零域外支持，不声明缺测区域的真实物理散射为零。v16保存`ZemaxXyz.Calibration.PositiveModel`及`FitEvidence.Conditions/PositiveModelSha256`，进入MeasuredBsdfGrid/8摘要，绑定训练颜色/非负光谱比值、近邻数、实际CMF/SPD表摘要和声明观察条件；新TIS由校准后角表积分计算，低版本夹带该模型明确拒绝。v15保存`ZemaxXyz.Calibration.FitEvidence.Gamut`，进入MeasuredBsdfGrid/7摘要，保留来源绑定的凸颜色域和有符号系数；仅在域内非负、独立验证通过且条件核实后可计算。v14 的 `SpeosSpectral.RuntimeWhite=true` 进入 MeasuredBsdfGrid/6 摘要，声明共享入射单元上端参考的 White 运行时功率和条件角密度，并要求 θ 线性及 White 基准。v13 的 `SpeosSpectral.ThetaLinear=true`、RuntimeWhite 缺省/false 继续为协议5，保留原 θ 线性、归档标量和节点 White 分配；旧载荷不隐式升级。v12 保留协议4的原生峰/锥/光谱和校准 XYZ；校准可附带不可变拟合来源、验证策略、每波段留出统计及系数摘要，空证据不改旧序列化/指纹。v11 保存投影数量类型、Zemax 原始镜面中心角表/TIS、测量波长及介质声明。v10 保存商业格式转换后的 BSDF、固定材料坐标/局部轴、来源格式与转换说明；低版本夹带新模型会拒绝。v9 内嵌原各向同性 CSV v1 实测 BSDF 表格、文件名与原文件/规范数据摘要。v8 为非序列对象增加可选 `SurfaceBsdf`，包括模型类型和反射率；普通工程按 v7 保存。v7 新增 `OperandPickups`：目标 `ConfigurationIndex`/`Operand`、源 `SourceConfigurationIndex`/`SourceOperand` 以及 `Scale`/`Offset`。配置索引从 0 开始，源目标绑定均引用已有行；源行及配置号不晚于目标，禁止自身、循环、非有限系数和多重控制。加载时在临时配置图中解析与检查，完整成功后才替换文档。v1–6 缺失拾取表按空表读取；低版本夹带非空拾取表明确拒绝。v6 新增 `OperandVariables`，按配置与参数绑定记录显式变量；v5 新增 `OperandRows`；沿用 v4 的以下字段：

- 应用标识、活动顺序配置索引和全部 `OpticSnapshot`，包括系统孔径、视场定义、像方无焦标志、波长和顺序表面；表面同时保存独立的净半直径与可选机械半直径，快照缺少机械值时使用净半直径加延伸区（旧快照延伸区缺省为 0）；
- 可选的多配置继承断开表；
- 独立 `NonSequentialDocumentSnapshot`，包括非序列波长、追迹设置、类型化对象和网格资产清单。

加载器接受容器v1/v2和工程负载v1–17。负载v1会迁移为空非序列场景，并从当时活动顺序配置复制一次波长；负载v2恢复第一阶段类型化非序列对象；负载v3增加网格资产清单；负载v4增加椭圆面、双角度、径向分布及三类体光源。容器v1不能携带网格资产。保存始终写容器v2，负载按实测极角支持域/正值颜色XYZ/有域约束XYZ/运行时 White/原 θ 线性 Speos/旧增强或原非负校准 XYZ/投影或 Zemax/原商业文本或固定轴/原实测 v1/Lambertian/无 BSDF 选择 v17/v16/v15/v14/v13/v12/v11/v10/v9/v8/v7。

## 光学快照验证

2026-09-06 起，已解析目录玻璃使用 `catalog_glass` 组件保存完整色散公式/系数、折射率与消光表格、波长范围、制造商和 AGF 元数据，恢复时不再按名称选择另一目录的材料。旧 `catalog` 名称快照仍可读，但无法恢复旧文件未保存的系数；旧版应用可能拒绝新组件。容器版本不变。详见 [材料快照修复与兼容边界](NUMERICAL_REPAIR_2026-09-06.md)。

当前顺序光学快照 schema 为 6；schema 1–5 经有界迁移后使用同一 schema 6 验证器。2026-10-01 新增视场 `VignetteDecenterX/Y` 和 `VignetteAngleDegrees`，与原有 X/Y 压缩因子一起无损保存；旧文件缺失的新字段默认 0。新字段必须有限，旧版应用应拒绝未知快照版本，避免静默忽略会改变光线的数据。容器 2 / 工程负载 4 不变。FDMO/FDRE 本地行支持八槽/视场编号保存；未经原生列核实的 ZMX 导入行保持兼容只读，REQS 仅保存需求起点标记。[实现和验证边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-01-第二十三批视场状态与完整瞳孔变换部分完成)。主要不变量：

- 视场、波长、表面表存在且非空；
- 波长有限且为正，恰有一个主波长；
- 表面编号从 0 连续；
- 厚度、半口径、坐标、环境、拾取/求解和操作数参数在允许域内；
- 类型化引用指向存在的表面、视场和波长；
- Zemax 兼容行中的不透明整数槽不被误判为 Workbench 引用；
- Zemax 只读兼容行保留导入时的八个参数槽位，编辑其他行时不会把这些原始数值裁剪到光瞳坐标范围；只读状态不表示该操作数已实现计算；
- Zemax 顺序导入遇到未映射 `TYPE` 时，可把原始类型、曲率、圆锥常数和 `PARM` 槽位作为不可计算几何组件保存；该 payload 只保证原生 STAROPT 往返，不表示追迹或有损商业格式导出已支持；
- 递归组件种类、子节点和集合数量在工厂运行前验证。
- 多配置继承表只能引用存在的从属配置、表面以及 `radius`、`thickness`、`conic`、`semiDiameter`、`material` 属性；旧文件没有该可选表时，从基础配置与从属配置的实际差异推断断开关系，避免下一次传播覆盖历史定制值。

普通参数不接受 `NaN`。无穷只在有明确光学意义的位置允许，例如平面曲率半径、无限光栅周期、无限薄透镜焦距和物面正无限厚度；其余厚度以及波长、孔径、坐标、环境、视场渐晕因子和引用不允许无穷。

## GRIN 空间材料组件

2026-10-03 初期记录：顺序 schema 6 新增 `gradient_index` 材料 kind，当时容器 2 / 工程负载 4 不变。Text 必须且仅包含 `name` 与 `profile`；初期支持 `gradient1`～`gradient4`，当前还支持下文的 `gradient5`。Numbers 必须精确包含该分布的全部系数、有限路径预算和全部积分误差/步长/次数设置。Gradient 2 保存 `n0Squared`（n²），不能当 n 使用。完整字段表见[材料保存契约](GRIN_MATERIAL_TRANSPORT_2026-10-03.md#保存契约)。

未知字段、缺项、非有限参数、非法模型/名称、非法预算、非整数或越界尝试次数及非空子组件均拒绝。没有“缺失系数补零”或“未识别材料退回目录名称”的分支。运行时绑定的体坐标不作为材料组件保存；重建后的正式顺序追迹在透射入射时重新绑定入口坐标。旧读取器遇到未知材料 kind 会拒绝。原四种分布的真实 STAROPT 往返和[限定共轴一阶接入](GRIN_PARAXIAL_AIMING_2026-10-03.md)均有阶段验证；[GRIN 材料约束](GRIN_CONTROL_OPERANDS_2026-10-03.md)使用既有槽位，兼容只读行不自动升级。后续已经增加 Gradient 5 及其显式色散，并完成 Gradient 1～5 的桌面材料创建、编辑、保存和受支持系数变量，见[编辑与边界](GRIN_MATERIAL_EDITOR_2026-10-04.md)。完整分析/偏振覆盖及原生 ZMX GRIN 映射仍未完成，不能再将已实现的桌面编辑列为缺口。

## 事务构建

加载顺序：

1. 验证文件框架、长度、压缩、版本和校验和；
2. 反序列化并迁移旧 schema；
3. 验证完整顺序/非序列状态、网格资产哈希和交叉引用；
4. 在临时 `Optic` 中构建全部对象；
5. 仅在全部成功后替换当前系统。

保存前也运行同一快照验证，非法内存状态不能写成原生工程。

## 兼容边界

桌面“保存”只写 `.staropt`。当前 `.optic.json` 快照可导入；ZMX、SEQ、LEN 和通用顺序文本是交换格式。旧品牌后缀及普通 `.json` 不作为工程路由。导入后保存会要求 STAROPT 目标，不覆盖原交换源。

工作区布局、应用偏好、计算缓存、`.starrdb` 光线数据库、公差定义和插件二进制不进入工程文件；只有非序列STL规范网格作为工程资产内嵌。非空非序列场景只能由 STAROPT 无损保存；旧 JSON、ZMX、SEQ、LEN 等顺序交换格式会明确拒绝导出，不会静默丢弃非序列数据。STAROPT 可往返 Workbench 评价函数代码以及当前注册的 383 个 Zemax 2026 R1 顺序兼容类型；这只是数据模型和只读兼容边界，不代表这些 Zemax 操作数已经完整实现。

## 物理相干镀膜组件

2026-10-02：schema 6 新增 `coherent_multilayer` kind；容器和工程负载版本不变。`Numbers.count` 为 0–4096 的整数，`Numbers.thickness_i` 为各层非负有限 nm 厚度，`Children.material_i` 为完整材料快照，顺序由入射侧至基底。保存不依赖材料显示名重新查库；缺失层、厚度或材料内部必要系数均拒绝，重建后核对规范快照，不能补默认折射率。旧版不认识此 kind 时应拒绝，不替换为空膜层。旧经验膜层 kind 含义不变，详见[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)。

## 镀膜模块实验格式

内置镀膜模块的 `.coating.json` 是单独编辑的实验文件，不是 STAROPT 新版本或独立软件；不自动应用到当前镜头。其字段与兼容约定只在 [镀膜文件契约](FILE_FORMATS_AND_PLUGINS.md#镀膜实验文件) 维护，STAROPT 原有镀膜 kind 的含义不变。

## 延伸区与 TSAG（2026-10-02）

顺序 `SurfaceSnapshot.ChipZone` 为 mm 单位的有限非负数，缺省为 0；旧文件缺字段继续可读，字段及净半径之和必须有限。容器和 schema 版本不变。TSAG 本地八槽完整保存；兼容导入的四个数据槽不补造为六个、不自动升级。原生外部延伸区列尚未验证，含非零值的文本处方导出会失败，使用 STAROPT 保留。见[实现与验证](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。

## Gradient 5 色散组件 · 2026-10-03

第四十批扩展 `gradient_index` 的 profile 值为 `gradient5`。Numbers 使用 `n0/nr2/nr4/nz1/nz2/nz3/nz4` 和既有九项积分设置；Text 仍仅 `name/profile`。无色散时不保存子组件；有色散时仅允许名为 `dispersion` 的 `gradient5_dispersion` 子组件，Text 为空、不可再有子组件。

色散 Numbers 必须完整包含 `referenceWavelengthNanometers/minimumWavelengthNanometers/maximumWavelengthNanometers/kCount/lCount`，以及三行 K/L 系数 `k1_1`…`k3_kCount`、`l1_1`…`l3_lCount`。计数为 1～8 的整数，系数按参考折射率升幂排列，每组行长相同；L 采用 μm² 约定。参数表严格检查，不默补缺失项或忽略多余项。工程负载仍为 v7、顺序快照 schema 6；不识别此 profile 的旧程序明确拒绝。原生 SGRIN.DAT/ZMX 映射尚未实现，详见[范围与验证](GRADIENT5_DISPERSION_2026-10-03.md)。
