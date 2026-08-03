# 系统架构

Optical System Design 在模块边界上参考公开的 Optiland 结构，但实现完全位于 .NET 中。

## 核心对象模型

`Optic` 是计算中心，拥有系统孔径、视场、波长、表面组、材料注册表、数值后端、光线追迹器、分析、优化、公差和多配置入口。`Optic` 只由 `OptilandWorkbench.Application` 持有；Avalonia 层只能通过应用服务和不可变 DTO 访问。

所有编辑走统一命令路径，以保证验证、撤销/重做、拾取和求解刷新、修订号递增及结构化失效通知一致。

## 表面组合模型

```text
OpticalSurface
  Geometry
  MaterialBefore
  MaterialAfter
  CoatingModel
  InteractionModel
  PhysicalAperture
  ScatteringModel
  CoordinateSystem
```

半径、厚度、材料、镀膜、半口径、二次曲面常数和光阑标记等表格字段仍然保留，并与组合对象同步。未知玻璃不会静默退化为常折射率材料。

材料注册表合并 Optiland 兼容数据、63 个 Zemax AGF 目录转换的数据和工程自定义材料。每个工程保存有序的当前玻璃目录列表；无厂商玻璃名按该列表消歧。

## 几何与交点

所有几何实现统一提供弧矢、交点距离和局部法线。解析曲面与自由曲面共用受控的 Newton 回退。标准面半径为零或无穷时按平面处理；只有物面厚度为正无穷才表示无限共轭，零厚度仍是有限共轭。

支持平面、标准面、偶次/奇次非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q 等模型。非物理平方根域返回 `NaN` 并拒绝交点，不延伸不存在的曲面分支。

## 数值后端与光线追迹

`INumericBackend` 定义标量运算，`IBatchedNumericBackend` 定义批量运算。内置 CPU 后端在常见顺序路径使用 `System.Numerics.Vector<double>`，不支持的曲面回退到标量内核。

`TraceRequest` 明确指定：

- 仅保留最后表面；
- 保留选定表面；
- 保留完整历史。

缓存键由光学系统修订号、后端、输入光线状态、保留表面和影响结果的选项组成；分析名称和本地化标题不能参与缓存身份。光段方向和 `RayInteractionKind` 是传播语义的唯一依据，不能从点序、Z 坐标或颜色反推。

## 分析、优化与公差

Core 通过 `AnalysisCatalog` 注册当前 `70` 个规范分析；Workbench 的规范键、中文显示名、兼容别名、展示类型和 Ribbon 命令由 `WorkbenchAnalysisCatalog` 统一描述。独立 `Distortion` 已退出公开目录，旧名称兼容映射到 `Field Curvature and Distortion`，底层 `DistortionAnalysis` 只作为组合分析的计算组件保留。结果 DTO 使用 `AnalysisPresentationKind` 选择专用控件，并通过 `AnalysisAxisQuantity` 与 `AnalysisAxisUnit` 描述坐标量和单位；显示字符串不能决定控件、缩放、缓存身份或导出逻辑。

报告入口属于分析体系，不是独立旧页面：“表面数据报告”逐面输出处方，“系统数据报告”按系统/孔径/视场/波长/近轴量组织，“分类数据报告”按角色/材料/几何类型汇总，“系统数据摘要”复用规范 `First Order` 分析，“基面数据”使用规范 `Cardinal Points Data`。旧“一阶量、处方报告”仅作为兼容别名解析。

优化由变量、操作数、缩放器和优化器组成，支持快速聚焦、局部优化、差分进化和盆地跳跃。ZMX 评价函数导入保持源行顺序；当前 `[MS-L7]` 参考文件的 103 行均可见，其中 63 行 `TRAR` 进入现有光线像差执行路径，九类尚无计算引擎的约束/数学操作数按八个 Zemax 参数槽位禁用只读保留。当前目录只有 51 个 Workbench 代码或兼容类型，不能表述为 333 项 Zemax 顺序操作数已完整实现。公差复用变量和操作数模型，支持扰动、采样、补偿、双侧灵敏度及按种子确定的 Monte Carlo。外层并行拥有执行权时，内层追迹会抑制嵌套并行。

## 应用边界

```text
WorkbenchApplication
  WorkspaceCoordinator
    OpticContext
      OpticalWorkspaceModel
        Core Optic
  OpticalDocumentService
  PrescriptionService
  AnalysisService
  VisualizationService
  OptimizationService
  TolerancingService
  MultiConfigurationService
  MaterialCatalogService
  LensLibraryService
  CadExportService
```

`WorkspaceCoordinator` 串行化写操作、控制文档生命周期取消令牌、递增修订号并发布分类事件。分析和可视化针对快照运行。原来的大型 `OptilandConnector` 已缩减为兼容旧调用者的薄外观。

## Avalonia 与 Dock 工作区

```text
MainWindow
  ActionManager
  PanelManager
    WorkspaceDockFactory
      ToolDock
      DocumentDock
  IWorkbenchApplication
```

`WorkspaceDockFactory` 提供稳定文档 ID、内容重建和 Dock 模型。普通分析命令聚焦稳定实例；“克隆分析”创建带 GUID 的独立实例。

`PanelManager` 提供批量停靠、合并、独立浮动、平铺、层叠、关闭、锁定和布局命令：

- “保留分栏停靠”重新停靠但保留分栏结构；
- “合并单窗格”把全部页面（包括原生浮动页）移入主文档区，并切换为标签模式；
- “全部独立浮动”为每个已打开页面创建一个原生宿主；
- “平铺全部”与“层叠全部”先把所有页面收回主文档区，再切换为 Dock 内部 MDI 模式并调用对应布局器；
- 平铺、层叠和合并不会在软件外保留内容窗口，只有独立浮动命令会创建原生宿主；
- 空宿主在操作后、保存前和旧会话恢复时过滤。

主题由 `ThemePalette`、`IsekaiTheme` 和 `ThemeResourceBindings` 管理。明亮、暗夜、异世界主题必须提供相同资源键；布局、文案和分析语义色独立于主题。

## 会话与持久化

光学工程和 Dock 会话分开保存。全局默认布局位于 `%APPDATA%\OptilandWorkbench\workspace-default.json`；按文件会话使用规范化绝对路径的 SHA-256 哈希作为文件名。

会话保存 Dock 图、文档描述符、分析设置、实例 ID、活动文档、锁定状态和浮动边界，不保存大型计算结果。布局修改经 500 ms 防抖保存并在退出时刷新。损坏会话会备份并回退；未知分析会跳过；空浮动窗口会过滤。
