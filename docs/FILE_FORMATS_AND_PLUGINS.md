# 文件格式与插件

## 原生 STAROPT 工程

桌面端唯一原生工程扩展名是 `.staropt`。它是版本化二进制容器，不是改后缀的 JSON。固定文件头包含 `STAROPT` 魔数、容器版本、Brotli 标志、压缩/解压长度和负载 SHA-256；保存使用临时文件与原子替换。

当前负载使用 schema 4 `OpticSnapshot`，保存：

- 系统名称、孔径、数值后端；
- 视场、波长和全部表面；
- 丰富的表面组件快照；
- 半径拾取、求解、评价操作数；
- 环境设置和有序当前玻璃目录；
- 全部光学配置与活动配置索引。

加载器除校验容器完整性外，还验证主波长、有限数值、表面编号、集合上限、组件布局和类型化引用。构建在临时 `Optic` 中完成，全部成功后才替换活动状态。schema 1–3 会先迁移到安全的 schema 4 状态。

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

显式导出使用“文件 > 导出 Python Optiland JSON”或 `.optiland-python.json`。不支持的组件必须报错，不能静默替换。Python 0.5.8 自身可能在 `from_dict()` 中把任意表面镀膜重连为 Fresnel 镀膜，其光栅字典也存在外部重建限制；这些不属于 Workbench 原生保存承诺。

## 公差文件

公差定义使用 `*.startol.json`，保存版本、操作数顺序和启用状态、类型、表面、上下偏差、分布、注释、评价准则、Monte Carlo 数量/种子、补偿迭代和良率阈值。

加载时验证表面范围、有限且有序的偏差、重复操作数以及至少一个有效非补偿操作数。该格式是 Workbench 自有的可读交换格式，不宣称兼容 Zemax 专有 `.TOL`。

## CAD 交换

“文件 > 导出 CAD”输出 AP203 `CONFIG_CONTROL_DESIGN` 的 `.step` / `.stp` 毫米模型：

- 顶层是镜头装配体，每个连续光学材料区间是独立命名的平面三角 `MANIFOLD_SOLID_BREP` 镜片零件；三角面共享边拓扑，胶合组按材料分件。
- 网格直接采样每个表面的真实 `Geometry.Sag(x,y)`，再通过该表面的 `CoordinateSystem` 转换到全局 XYZ；不复用查看器的轴对称旋转网格。
- 镜片实体外形使用 `SemiDiameter`。物理孔径只表示通光范围，不会被误切成材料孔洞；前后口径不同时，较小表面会在边缘 Sag 高度延伸为平坦环带，再以共同外径侧壁连接，与三维查看器的镜片边缘拓扑保持一致。
- `SurfaceSamples` 和 `AngularSamples` 是最低种子密度，默认继续细分到最大弦高误差不超过 `0.005 mm`；单片默认上限为 `500,000` 个三角形，超限会失败而不是静默降精度。
- 写出前检查非有限 Sag、占位自由曲面、退化面、非流形边、法向、正体积和三角网格自相交。错误包含镜片或表面编号；没有基底实体定义的反射面跳过并返回警告。
- 文件先写入目标目录中的临时文件，再原子替换目标；取消或失败不会破坏已有 STEP。

CI 在 Linux 上生成 Cooke、非球面、偏心双锥面和胶合 Tessar 样例，并使用 FreeCAD/OpenCascade 检查可导入性、唯一实体数量、形状有效性和正体积。商业 CAD 仍需按发布清单抽检。

SolidWorks 启用 3D Interconnect 并保留组件链接时，FeatureManager 会在 SolidWorks 文档根节点下显示一个带链接箭头的 STEP 根组件，再列出镜片零件；外层是宿主文档包装，不是文件中重复的装配体。若需要原生 SolidWorks 层级，可在“工具 > 选项 > 系统选项 > 导入 > 常规”中关闭 3D Interconnect 后重新导入，或对已导入根组件执行“断开链接”。

该路径仍是分面交换，不保留解析球面/非球面、NURBS、光学材料、镀膜、公差、机械倒角或镜筒约束。`.staropt` 仍是无损光学工程格式；进入机械设计或制造前必须在目标 CAD 中复核。

## 商业顺序格式

支持的扩展名：

- Zemax `.zmx`；
- Zemax 玻璃目录 `.agf`，在构建或离线工具中转换为 Workbench 存储；
- CODE V `.seq`；
- OSLO `.len`；
- 通用顺序 `.lens`、`.dat`、`.txt`。

ZMX 导入边界包括编码检测、顺序模式验证、系统孔径、角度/物高/近轴像高/实像高视场、渐晕、波长、`GCAT`/`GLAS`、标准面、偶次/奇次非球面、基础环曲面、坐标断点、反射镜材料连续性、光阑、半口径和 `APMN`/`APMX`。

当前明确拒绝非顺序模式、未知表面类型、未支持的坐标断点顺序、经纬仪视场以及不可表示的环曲面项。有符号厚度按源值保留，不再把负厚度笼统视为非法。ZMX 不可靠保存 UI 活动配置，因此导入固定激活配置 1，同时保留全部配置。

`GCAT` 和 `GLAS` 先解析打包 Zemax 数据，再解析 Optiland 兼容数据。无厂商同名玻璃按有序 `GCAT` 消歧；只有 `GLAS` 给出有效 nd/Vd 时，未知玻璃才可回退为 `AbbeMaterial`，否则导入失败。

63 个 AGF 目录转换为一个版本化压缩 `zemax-glass-catalogs.ogdb`，包含 5,502 条记录。解析器支持公式 1–13 以及真实 Glasscat 文件中的 UTF-16、缺失值、旧式短记录和重复名称。用户补充目录转换为用户目录中的 `.ogcat`，成为可复用材料目录。

ZMX 导出写入系统孔径、视场、波长、主波长和有序 `GCAT`。CODE V、OSLO 和通用顺序文本只覆盖公共表面字段。完整状态应使用 STAROPT。

Zemax 顺序操作数的目标边界见 [Zemax 顺序模式操作数支持规范](ZEMAX_OPERAND_SUPPORT.md)。当前 `[MS-L7]` 参考文件的 103 行评价函数已按源顺序导入：63 行 `TRAR` 使用现有执行路径，九类未实现计算的操作数按 `Int1`、`Int2`、`Data1`–`Data4`、`Target`、`Weight` 原样禁用只读保留。兼容表中出现或能够往返的操作数不等于已经完成参数语义、求值和 Zemax 数值等价。

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

目录发现使用 `new PluginLoader().LoadFromDirectory("plugins")`，进程内测试可使用 `LoadFromAssembly`。单个插件加载或注册失败只记录到 `PluginRegistry.Warnings`，不得阻止其他插件。
