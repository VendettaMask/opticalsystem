# 文件格式与插件

更新：2026-10-14。本页说明文件入口、导出边界和插件接口。STAROPT 的二进制布局、版本与迁移只在 [格式契约](STAROPT_FILE_FORMAT.md) 维护；操作数逐项参数和历史验证只在 [支持规范](ZEMAX_OPERAND_SUPPORT.md) 维护。

## 分析 CSV 的输出来源（2026-10-13）

有实际绘图点时保留既有 12 列 CSV：`pane,series,x_quantity,x_unit,x,y_quantity,y_unit,y,value_quantity,value_unit,value,label`，数量和单位来自类型化元数据，数值保持 invariant / round-trip 精度。空绘图子窗不会遮蔽仍存在的顶层曲线。图表与表格并存时仍导出绘图数据，保持旧消费方式；本轮没有增加混合多表 CSV 或完整工程数值交换协议。

没有绘图点时，优先逐列、逐行导出 `AnalysisTableDto`；若含非空行分组，追加 `row_group` 列。无表格时导出指标/值摘要，表头为 `metric,value`。逗号、引号、换行、空格和已格式化单元格文本保留，不从显示字符串推断量纲或重新计算数值。表格行列数不一致时明确拒绝，无可导出数据时也报错；有明确列定义的零行表格可以只输出表头。桌面继续使用已有大小限制和原子写入，格式化失败不会覆盖旧导出文件。验证状态与源/证据哈希见[当前状态](CURRENT_STATUS.md)及[本轮账本](validation/RELIABILITY_FIXES_2026-10-13.json)。

## 原生 STAROPT 工程

桌面“保存”只写 `.staropt`；当前 `.optic.json` 数据快照和商业顺序文本用于导入或显式交换，不覆盖源文件。STAROPT 完整保留顺序配置、多配置行/变量/拾取及非序列场景和网格资产；容器、负载、快照版本、校验和临时构建顺序见 [唯一格式契约](STAROPT_FILE_FORMAT.md#工程负载)。

旧经验膜层与损耗模型保留兼容含义，不自动变为真实膜系或 BSDF。物理相干膜层使用单独的 `coherent_multilayer` 组件，见 [组件契约](STAROPT_FILE_FORMAT.md#物理相干镀膜组件)。

`.starrdb` 保存可重新生成的非序列追迹结果，不嵌入工程。完整分析与有界 3D 布局各自使用结果会话；格式、生命周期和过期校验见 [数据库契约](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md#分支输出和-starrdb)。

按用户决定不提供旧名称兼容。原生 JSON 路由只接受大小写不敏感的 `.optic.json`；普通 `.json`、旧品牌后缀和退役外部格式均在工程读取/写入前拒绝，不通过内容嗅探绕过后缀边界。底层 `OpticJsonStore` 仍校验当前原生快照结构，它不是通用外部字典读取器。

## 镀膜实验文件

内置镀膜模块使用 `.coating.json` schema 1，实验编辑状态与镜头 `.staropt` 分开；文件隔离不是独立产品或进程。保存膜系、完整材料公式/n/k 表和来源/有效范围、目标、采样/约束、候选、运行算法与版本、停止原因、评价次数、求解器版本、输入 SHA-256、光谱和逐项指标；公差结果还记录种子与输入哈希。正式 `BoundedFile` 提供有界读取和原子替换；格式/输入非法或结果哈希不匹配明确拒绝。重开有结果的实验后以冻结快照重新计算。此格式不承诺兼容 TFStudio 文件，也不进入当前镜头。

导出目录含 `layers.csv`、`spectrum.csv`、`metrics.csv`、`run.json`、`experiment.coating.json`。CSV 使用 nm、原始 R/T/A 比例以及 ln(T)/OD，保留 G17 数值，不使用界面舍入值或 OD 显示饱和值；每个文件原子写入，整个导出目录不是多文件事务。参见[镀膜实验室说明](../labs/CoatingDesign/README.md)。

## 公差文件

公差定义使用 `*.startol.json`，保存版本、操作数顺序和启用状态、类型、表面、上下偏差、分布、注释、评价准则、Monte Carlo 数量/种子、补偿迭代和良率阈值。公差编辑器独立跟踪未保存状态；新建、打开、镜头库载入和退出都会与原生工程一起进入统一保存确认。写入使用同目录临时文件和原子替换。

加载时验证表面范围、有限且有序的偏差、重复操作数以及至少一个有效非补偿操作数。文件版本、指定频率 MTF 准则和旧版本迁移见 [公差保存契约](TOLERANCING.md)。该格式不宣称兼容 Zemax 专有 `.TOL`。

## CAD 交换

“文件 > 导出 CAD”输出 AP203 `CONFIG_CONTROL_DESIGN` 的 `.step` / `.stp` 毫米模型：

- 顶层是镜头装配体，每个连续光学材料区间是独立命名的平面三角 `MANIFOLD_SOLID_BREP` 镜片零件；三角面共享边拓扑，胶合组按材料分件。
- 网格直接采样每个表面的真实 `Geometry.Sag(x,y)`，再通过该表面的 `CoordinateSystem` 转换到全局 XYZ；不复用查看器的轴对称旋转网格。
- 未支持的几何类型不会按平面解释。STAROPT和原生 JSON 将其数字、文本及递归子组件保存为不可计算的 opaque payload；追迹、分析、优化、公差、布局和导出前统一报告面号、原始类型及阻断原因。
- opaque 几何只允许保存到能完整保留该 payload 的原生格式。STEP、ZMX、CODE V SEQ、OSLO LEN及制造图纸等有损导出默认拒绝，不提供静默降级开关。
- 镜片实体外径使用前后面的 `MechanicalSemiDiameter`，光学曲面采样覆盖净半径加 `ChipZone`；机械半径不得小于该覆盖范围。物理孔径只表示通光范围，不被误切成材料孔洞；曲面边缘外的机械延伸形成平坦环带并连接侧壁，不扩大有效通光口径。
- `SurfaceSamples` 和 `AngularSamples` 是最低种子密度，默认继续细分到最大弦高误差不超过 `0.005 mm`；单片默认上限为 `500,000` 个三角形，超限会失败而不是静默降精度。
- 写出前检查非有限 Sag、占位自由曲面、退化面、非流形边、法向、正体积和三角网格自相交。错误包含镜片或表面编号；没有基底实体定义的反射面跳过并返回警告。
- 文件先写入目标目录中的临时文件，再原子替换目标；取消或失败不会破坏已有 STEP。

CI 在固定 `ubuntu-22.04` 生成 Cooke、非球面、偏心双锥面和胶合 Tessar STEP 样例，并把 fixture 作为 artifact 上传。FreeCAD/OpenCascade 导入验证在独立 job 中运行，固定使用 Ubuntu Jammy 的 `FreeCAD 0.19.2+dfsg1-3ubuntu1` 包，检查可导入性、唯一实体数量、形状有效性和正体积，并始终上传安装、版本和验证日志。验证环境安装失败只表示第三方验证环境不可用；商业 CAD 仍需按发布清单抽检。

SolidWorks 启用 3D Interconnect 并保留组件链接时，FeatureManager 会在 SolidWorks 文档根节点下显示一个带链接箭头的 STEP 根组件，再列出镜片零件；外层是宿主文档包装，不是文件中重复的装配体。若需要原生 SolidWorks 层级，可在“工具 > 选项 > 系统选项 > 导入 > 常规”中关闭 3D Interconnect 后重新导入，或对已导入根组件执行“断开链接”。

该路径仍是分面交换，不保留解析球面/非球面、NURBS、光学材料、镀膜、公差、机械倒角或镜筒约束。`.staropt` 仍是无损光学工程格式；进入机械设计或制造前必须在目标 CAD 中复核。

## 商业顺序格式

2026-10-05 保存边界修复：多个配置必须通过 STAROPT 完整保存。应用 SaveAsync/SaveDocumentAsync 在写入 ZMX、SEQ、LEN、旧原生 JSON 等单镜头格式前拒绝操作，原文件、路径和脏状态不变。活动镜头导出不能等同于整工程保存。保存期间继续编辑时，“保存并继续”仍阻止关闭/切换，直到当前修订已保存。见[修复与回归](AUDIT_RELIABILITY_FIXES_2026-10-05.md)。

ZMX 切趾设置现已读取 `GFAC <因子> <类型>`，支持均匀（0）、高斯（1）和余弦立方（2），并在 STAROPT 保存、重开和 ZMX 导出中保留原始类型及因子。系统属性用带“Zemax”后缀的选项显示这些设置，参数标为“因子”；原有高斯模型仍使用 σ。高斯光线强度为 `exp(-2Gρ²)`，G=0 为均匀强度；余弦立方按当前入瞳半径和物面到入瞳的距离计算，因子仅保留、不参与计算。验证范围和依据见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

支持的扩展名：

- Zemax `.zmx`；
- Zemax 玻璃目录 `.agf`，在构建或离线工具中转换为 Workbench 存储；
- CODE V `.seq`；
- OSLO `.len`；
- 通用顺序 `.lens`、`.dat`、`.txt`。

ZMX 导入边界包括编码检测、顺序模式验证、`UNIT` 长度单位缩放、系统孔径、角度/物高/近轴像高/实像高视场、像方无焦标志、渐晕、波长、`GCAT`/`GLAS`、标准面、偶次/奇次非球面、基础环曲面、坐标断点、反射镜材料连续性、光阑、净半直径 `DIAM`、机械半直径 `MEMA` 和 `APMN`/`APMX`。布局光线按系统入瞳的归一化坐标发射；用户固定 `DIAM` 的有光焦度表面或光阑面建立同半径的圆形口径；光焦度结合几何曲率与材料变化/反射判断，同介质曲面虚拟参考面不因此建立隐含孔径（显式孔径仍生效），固定的平面光阑仍截光，`APMN` 继续建立中心遮拦。自动 `DIAM` 是包络真实光线的估算值，包括自动光阑半口径，不额外变成截光孔径。Tessar RA256 的原生逐光线捕获验证了自动光阑估值不能充当硬边界，见[复验报告](ZEMAX_RA256_SINGLE_RAY_REPAIR_2026-10-07.md)。`MEMA` 作为独立表面属性保留，只控制镜片实体外边界，绝不扩大入瞳或光线通光范围，也不从相邻 `DIAM` 推算；原生工程保存/重开、处方表、布局机械边界、制造数据、CAD 网格和 ZMX 再导出使用该值。源文件没有 `MEMA` 时才回退为该面的净半直径加本地延伸区（当前 ZMX 延伸区映射未实现，导入默认为 0）。长度单位会统一转换为 Workbench 内部毫米；非球面系数按当前几何公式的幂次同步缩放，多配置 `CRVT`、`THIC`、`APMX`、`APMN` 和 `PRAM` 也应用同一转换。

当前 ZMX 顺序导入器明确拒绝 Zemax 非序列文件、未支持的坐标断点顺序、经纬仪视场以及不可表示的环曲面项。有符号厚度按源值保留，不再把负厚度笼统视为非法。未映射到 Workbench 可计算几何的顺序 `TYPE` 不会让整个导入失败；导入器会把原始 Zemax `TYPE`、曲率、圆锥常数和 `PARM` 数据保存为不可计算的只读 opaque payload，UI 显示为不支持面型，追迹、分析、优化、布局和有损导出前由能力检查明确拦截。ZMX 不可靠保存 UI 活动配置，因此导入固定激活配置 1，同时保留全部配置。

`GCAT` 和 `GLAS` 先解析打包 Zemax 数据，再解析 独立的 refractiveindex.info 静态材料目录。无厂商同名玻璃按有序 `GCAT` 消歧。未匹配玻璃不会终止导入：保留原名称和其余处方数据，以 `UnresolvedMaterial` 标识，底部状态栏持续提示材料名与表面号；禁止用 GLAS 的 nd/Vd 参数或空气替代。STAROPT 保存/重开保留该状态。几何布局正常显示，但不生成依赖缺失材料的光线或分析结果；补选有效材料后恢复计算。缺失材料时保留文件中的厚度值，不执行依赖光线的 MAZH 厚度求解。`CORNING3` 中的 `C7980` 显式匹配本地目录的 `C79-80`，使用目录色散数据，不是参数近似。验证与示例见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

63 个 AGF 目录转换为一个版本化压缩 `zemax-glass-catalogs.ogdb`，包含 5,502 条记录。解析器支持公式 1–13 以及真实 Glasscat 文件中的 UTF-16、缺失值、旧式短记录和重复名称。用户补充目录转换为用户目录中的 `.ogcat`，成为可复用材料目录。

ZMX 导出写入 `UNIT MM`、系统孔径、视场、像方无焦标志、波长、主波长和有序 `GCAT`。导出只对可无损表达为 Zemax `STANDARD`、`EVENASPH`、`ODDASPHE` 和基础 `TOROIDAL` 的几何写出 `TYPE`；环形物理孔径写为 `APMN`，其它 Workbench 特有或尚未映射到 Zemax 的几何/孔径会明确失败，不再静默降级为 `TYPE STANDARD`。像方无焦按 Zemax 风格作为像空间角度坐标处理：点列图、光线扇形、RMS Spot、FFT/MMDFT/Huygens PSF、MTF 和波前/OPD 会在最终像面使用相对主光线的角度坐标（mrad）、角频率（cycles/mrad）和屈光度离焦（D）；波前参考由参考球切换为垂直主光线的平面。CODE V、OSLO 和通用顺序文本只覆盖公共表面字段。完整状态应使用 STAROPT。

ZMX 评价函数保留代码、参数、目标、权重、注释和禁用状态。可读取或往返不等于可求值；未经核实的原生列保持只读，不能补零并自动升级。注册范围、受限执行数量、逐项参数与原生验证缺口统一见 [操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)。

## 文档服务

桌面统一通过：

```csharp
await application.Documents.OpenAsync(path);
await application.Documents.SaveAsync(path);
```

`OpticalDocumentService` 委托规范 `WorkbenchRuntime` 识别 STAROPT、当前 `.optic.json` 快照或商业格式适配器。旧连接器、Compatibility 项目及旧名称兼容层已删除，生产调用和行为测试统一使用 `WorkbenchRuntime`。

## 插件模型

程序集与接口已随产品统一命名；旧插件需针对当前 `OpticalSystemDesign.Core` 和下述接口重新编译，不承诺旧程序集的二进制兼容。旧工程格式的兼容导入不受此接口更名影响。

插件实现：

```csharp
public interface IOpticalSystemDesignPlugin
{
    string Name { get; }
    void Register(PluginRegistry registry);
}
```

可注册几何工厂、材料实例和分析工厂。示例：

```csharp
public sealed class ExamplePlugin : IOpticalSystemDesignPlugin
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

退役外部字典和旧品牌快照均不再导入或导出，没有旧名称筛选器、兼容开关或通用 `.json` 工程分派。把旧字典改名为 `.optic.json` 也不能绕过当前原生快照的结构校验。不得恢复旧品牌后缀支持。

`zemax-zmx` 是 ZMX 的格式标识；不再附带历史参考软件版本。通用 JSON、原生快照、STAROPT、ZMX、SEQ、LEN、通用顺序文本和 .NET 插件基础设施保留。

## 镀膜快照读取的严格性

镀膜 schema 1 只接受可完整重建的材料模型；共享组件读取器用于旧工程的默认值补齐不能进入实验室快照路径。重建后核对规范字段和系数，缺失系数明确报错。文档读写受 64 MiB 共享上限约束；该实验格式未改变 STAROPT 版本或现有兼容 kind。数据字段和资源约束见[计算契约](COATING_DESIGN_LAB.md#资源和材料限制)。

## 镀膜日常设计扩展 · 2026-09-28

schema 1 新增可选 Structure/Search/Tolerancing 设置，缺省时不参与序列化，以保持旧实验指纹和重开兼容。候选可保存局部 RunRecord；公差保存实际膜厚、角度、逐项检查和错误，可额外导出 tolerance.csv 与 tolerance.json。材料交换为显式 wavelength_nm,n,k 或 wavelength_um,n,k 表头的 CSV/TSV，2–10000 行、4 MiB 上限；原有解析材料不会静默改为插值表。数据范围、正态误差和浮点边界说明见[计算契约](COATING_DESIGN_LAB.md)。

## 镀膜输入指纹的跨平台兼容（2026-10-05）

镀膜工程的输入指纹自 2026-10-05 起固定使用 LF，避免随平台换行改变。读取、保存和导出仍验证实际输入，可兼容此前 LF/CRLF 指纹；验证成功后规范化结果与公差指纹。计算重开仍重新计算光谱，修改输入不会因兼容旧换行而获准复用旧结果。见[修复与验证](AUDIT_RELIABILITY_FIXES_2026-10-05.md)。

## 延伸区的导出边界（2026-10-02）

本地 `ChipZone` 已进入 JSON/STAROPT；CAD/STEP 曲面延伸到净半径加延伸区，然后生成机械平环。外部 ZMX/CODE V/OSLO/通用处方的非零延伸区列尚未验证，当前明确拒绝导出，避免静默丢失。TSAG 原始 MFE 行仍禁用保留，本地八槽可执行。见[完整范围](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。
