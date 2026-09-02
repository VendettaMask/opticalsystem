# 文件格式与插件

## 原生 STAROPT 工程

桌面端唯一原生工程扩展名是 `.staropt`。它是版本化二进制容器，不是改后缀的 JSON。固定文件头包含 `STAROPT` 魔数、容器版本、Brotli 标志、压缩/解压长度和负载 SHA-256；保存使用临时文件与原子替换。

当前容器版本为2、工程负载版本为4；顺序光学快照使用 schema 4 `OpticSnapshot`。负载保存：

- 系统名称、孔径、数值后端；
- 视场、波长和全部表面；
- 丰富的表面组件快照；
- 半径拾取、求解、评价操作数；
- 环境设置和有序当前玻璃目录；
- 全部光学配置、活配置索引，以及从属配置按表面/属性记录的继承断开关系；
- 独立非序列文档的波长、追迹默认值、10类类型化原生光源、几何/探测器对象、GUID引用关系，以及内容寻址的STL网格资产。

加载器除校验容器完整性外，还验证顺序主波长、有限数值、表面编号、组件布局，以及非序列对象类型、唯一GUID、引用图、光源参数/径向样本、网格资产哈希和集合上限。容器v1及工程负载v1/v2/v3继续兼容；保存始终写容器v2和负载v4。构建在临时文档中完成，全部成功后才替换活动状态。`OpticSnapshot` schema 1–3 会先迁移到安全的 schema 4 状态。

顺序组件快照的新建内容使用 `approximate_transmission_ripple`、`main_ray_scatter_loss_approximation` 和 `mean_measured_scatter_loss`，明确表示这些是 Experimental 损耗近似。旧 `thin_film_stack`、`lambertian` 和 `measured_bsdf` kind 继续只读兼容，加载后迁移到准确命名的模型；它们不表示已经实现真实薄膜或 BSDF 物理。

非序列光线可另存为 `.starrdb`。该文件保存场景哈希、来源修订、追迹设置、随机种子、可选分裂模式、分支终止状态和分块压缩光线树；它是可重新生成的结果，不嵌入STAROPT。应用分别管理完整分析结果和有界3D布局结果：前者驱动数据库、路径和探测器，后者由非序列3D页面在打开、过期或用户手动刷新时生成。任何布局读取都会核对数据库头与当前场景哈希，过期光线默认不加载；显式查看时仍保留过期标记。详见[非序列第二阶段：杂散光基础链路](NONSEQUENTIAL_PHASE2_STRAY_LIGHT.md)。

旧 `.optiland.json`、`.optic.json`、`.json` 和 `.optiland` 可继续读取用于迁移，但桌面“保存”不再生成这些格式。二进制结构见 [STAROPT 工程格式](STAROPT_FILE_FORMAT.md)。

## Python Optiland JSON

`OpticJsonStore` 可识别 Python Optiland 0.5.8 `Optic.to_dict()` 的递归字典。已验证的双向子集包括：

- EPD、像方 F 数、物方 NA 和按光阑浮动的系统孔径；
- 角度、物高、近轴像高视场以及波长权重；
- 平面、标准面、光栅、可表示的双锥面/环曲面/多项式/Chebyshev/Zernike/高阶非球面及坐标变换；
- 目录、理想和 Abbe 材料；
- 径向、矩形、椭圆、多边形、文件及递归布尔物理孔径；
- uniform、Gaussian、cosine-squared、Hann、polynomial、super-Gaussian 和 Tukey 变迹；
- 折射、反射、薄透镜、相位和衍射交互；
- Workbench 适配路径上的简单 Python 镀膜字典。

桌面应用只保留 Python Optiland JSON 导入兼容，不再提供 Python JSON 导出入口。Python 0.5.8 自身可能在 `from_dict()` 中把任意表面镀膜重连为 Fresnel 镀膜，其光栅字典也存在外部重建限制；这些不属于 Workbench 原生保存承诺。

## 公差文件

公差定义使用 `*.startol.json`，保存版本、操作数顺序和启用状态、类型、表面、上下偏差、分布、注释、评价准则、Monte Carlo 数量/种子、补偿迭代和良率阈值。公差编辑器独立跟踪未保存状态；新建、打开、镜头库载入和退出都会与原生工程一起进入统一保存确认。写入使用同目录临时文件和原子替换。

加载时验证表面范围、有限且有序的偏差、重复操作数以及至少一个有效非补偿操作数。该格式是 Workbench 自有的可读交换格式，不宣称兼容 Zemax 专有 `.TOL`。

## CAD 交换

“文件 > 导出 CAD”输出 AP203 `CONFIG_CONTROL_DESIGN` 的 `.step` / `.stp` 毫米模型：

- 顶层是镜头装配体，每个连续光学材料区间是独立命名的平面三角 `MANIFOLD_SOLID_BREP` 镜片零件；三角面共享边拓扑，胶合组按材料分件。
- 网格直接采样每个表面的真实 `Geometry.Sag(x,y)`，再通过该表面的 `CoordinateSystem` 转换到全局 XYZ；不复用查看器的轴对称旋转网格。
- 未支持的几何类型不会按平面解释。STAROPT和原生 JSON 将其数字、文本及递归子组件保存为不可计算的 opaque payload；追迹、分析、优化、公差、布局和导出前统一报告面号、原始类型及阻断原因。
- opaque 几何只允许保存到能完整保留该 payload 的原生格式。STEP、Python Optiland JSON、ZMX、CODE V SEQ、OSLO LEN及制造图纸等有损导出默认拒绝，不提供静默降级开关。
- 镜片实体外形使用 `SemiDiameter`。物理孔径只表示通光范围，不会被误切成材料孔洞；前后口径不同时，较小表面会在边缘 Sag 高度延伸为平坦环带，再以共同外径侧壁连接，与三维查看器的镜片边缘拓扑保持一致。
- `SurfaceSamples` 和 `AngularSamples` 是最低种子密度，默认继续细分到最大弦高误差不超过 `0.005 mm`；单片默认上限为 `500,000` 个三角形，超限会失败而不是静默降精度。
- 写出前检查非有限 Sag、占位自由曲面、退化面、非流形边、法向、正体积和三角网格自相交。错误包含镜片或表面编号；没有基底实体定义的反射面跳过并返回警告。
- 文件先写入目标目录中的临时文件，再原子替换目标；取消或失败不会破坏已有 STEP。

CI 在固定 `ubuntu-22.04` 生成 Cooke、非球面、偏心双锥面和胶合 Tessar STEP 样例，并把 fixture 作为 artifact 上传。FreeCAD/OpenCascade 导入验证在独立 job 中运行，固定使用 Ubuntu Jammy 的 `FreeCAD 0.19.2+dfsg1-3ubuntu1` 包，检查可导入性、唯一实体数量、形状有效性和正体积，并始终上传安装、版本和验证日志。验证环境安装失败只表示第三方验证环境不可用；商业 CAD 仍需按发布清单抽检。

SolidWorks 启用 3D Interconnect 并保留组件链接时，FeatureManager 会在 SolidWorks 文档根节点下显示一个带链接箭头的 STEP 根组件，再列出镜片零件；外层是宿主文档包装，不是文件中重复的装配体。若需要原生 SolidWorks 层级，可在“工具 > 选项 > 系统选项 > 导入 > 常规”中关闭 3D Interconnect 后重新导入，或对已导入根组件执行“断开链接”。

该路径仍是分面交换，不保留解析球面/非球面、NURBS、光学材料、镀膜、公差、机械倒角或镜筒约束。`.staropt` 仍是无损光学工程格式；进入机械设计或制造前必须在目标 CAD 中复核。

## 商业顺序格式

支持的扩展名：

- Zemax `.zmx`；
- Zemax 玻璃目录 `.agf`，在构建或离线工具中转换为 Workbench 存储；
- CODE V `.seq`；
- OSLO `.len`；
- 通用顺序 `.lens`、`.dat`、`.txt`。

ZMX 导入边界包括编码检测、顺序模式验证、`UNIT` 长度单位缩放、系统孔径、角度/物高/近轴像高/实像高视场、像方无焦标志、渐晕、波长、`GCAT`/`GLAS`、标准面、偶次/奇次非球面、基础环曲面、坐标断点、反射镜材料连续性、光阑、半口径和 `APMN`/`APMX`。长度单位会统一转换为 Workbench 内部毫米；非球面系数按当前几何公式的幂次同步缩放，多配置 `CRVT`、`THIC`、`APMX`、`APMN` 和 `PRAM` 也应用同一转换。

当前 ZMX 顺序导入器明确拒绝 Zemax 非序列文件、未支持的坐标断点顺序、经纬仪视场以及不可表示的环曲面项。有符号厚度按源值保留，不再把负厚度笼统视为非法。未映射到 Workbench 可计算几何的顺序 `TYPE` 不会让整个导入失败；导入器会把原始 Zemax `TYPE`、曲率、圆锥常数和 `PARM` 数据保存为不可计算的只读 opaque payload，UI 显示为不支持面型，追迹、分析、优化、布局和有损导出前由能力检查明确拦截。ZMX 不可靠保存 UI 活动配置，因此导入固定激活配置 1，同时保留全部配置。

`GCAT` 和 `GLAS` 先解析打包 Zemax 数据，再解析 Optiland 兼容数据。无厂商同名玻璃按有序 `GCAT` 消歧；只有 `GLAS` 给出有效 nd/Vd 时，未知玻璃才可回退为 `AbbeMaterial`，否则导入失败。

63 个 AGF 目录转换为一个版本化压缩 `zemax-glass-catalogs.ogdb`，包含 5,502 条记录。解析器支持公式 1–13 以及真实 Glasscat 文件中的 UTF-16、缺失值、旧式短记录和重复名称。用户补充目录转换为用户目录中的 `.ogcat`，成为可复用材料目录。

ZMX 导出写入 `UNIT MM`、系统孔径、视场、像方无焦标志、波长、主波长和有序 `GCAT`。导出只对可无损表达为 Zemax `STANDARD`、`EVENASPH`、`ODDASPHE` 和基础 `TOROIDAL` 的几何写出 `TYPE`；环形物理孔径写为 `APMN`，其它 Workbench 特有或尚未映射到 Zemax 的几何/孔径会明确失败，不再静默降级为 `TYPE STANDARD`。像方无焦按 Zemax 风格作为像空间角度坐标处理：点列图、光线扇形、RMS Spot、FFT/MMDFT/Huygens PSF、MTF 和波前/OPD 会在最终像面使用相对主光线的角度坐标（mrad）、角频率（cycles/mrad）和屈光度离焦（D）；波前参考由参考球切换为垂直主光线的平面。CODE V、OSLO 和通用顺序文本只覆盖公共表面字段。完整状态应使用 STAROPT。

Zemax 顺序操作数的目标边界见 [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)。当前 `[MS-L7]` 参考文件的 103 行评价函数已按源顺序导入；114 个 Zemax 顺序操作数已有定义级可执行路径，覆盖 `TRAR`、范围厚度 `TTHI/TGTH`、实际光线径向坐标 `REAR`、实际光线角度 `RANG`、基础数学与行约束、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`。当前 383 个 2026 R1 实测顺序兼容代码中，`DIMX` 等未完成全部参数语义的类型保持禁用只读。兼容表中出现或能够往返的操作数不等于已经完成参数语义、求值和 Zemax 数值等价；新增执行路径也必须通过 Zemax/ZOS-API golden 对照后才能标为完整兼容。

## 文档服务

桌面统一通过：

```csharp
await application.Documents.OpenAsync(path);
await application.Documents.SaveAsync(path);
```

`OpticalDocumentService` 委托规范 `WorkbenchRuntime` 按内容和扩展名识别 STAROPT、旧 Workbench JSON、Python Optiland JSON 或商业格式适配器。`OptilandConnector` 仅保留为源代码兼容外观，生产 Services 不引用 `Application.Legacy`。

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
