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

所有几何实现统一返回结构化 `IntersectionResult`，包含状态、距离、交点、法线、最终残差、迭代次数和条件估计。通用非球面使用自适应中心差分导数、阻尼 Newton、前向区间搜索及割线/二分回退；返回前必须重新计算 Sag、尺度相关残差和法线，未收敛结果不得标记为成功。调用方只接受 `Success` 或 `Tangent`，并可区分 `NoRoot`、`DomainError`、`MaxIterations`、`InvalidNormal` 和 `InvalidInput`。标准面半径为零或无穷时按平面处理；只有物面厚度为正无穷才表示无限共轭，零厚度仍是有限共轭。

支持平面、标准面、偶次/奇次非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q 等模型。非物理平方根域返回 `NaN` 并拒绝交点，不延伸不存在的曲面分支。

现有膜层和散射近似不作为物理 Thin Film、Lambertian 或 Measured BSDF 对外提供。生产名称为 `ApproximateTransmissionRippleCoating`、`MainRayScatterLossApproximation` 和 `MeanMeasuredScatterLoss`，界面统一标记 `Experimental`；旧类名和旧序列化 kind 仅为兼容入口并带弃用警告。稳定 S-matrix、复折射率/角度/偏振膜层响应，以及 BSDF `Evaluate + Sample + Pdf` 尚未实现。

## 数值后端与光线追迹

`INumericBackend` 定义标量运算，`IBatchedNumericBackend` 定义批量运算。内置 CPU 后端在常见顺序路径使用 `System.Numerics.Vector<double>`，不支持的曲面回退到标量内核。

`TraceRequest` 明确指定：

- 仅保留最后表面；
- 保留选定表面；
- 保留完整历史。

缓存键由光学系统修订号、后端、输入光线状态、保留表面和影响结果的选项组成；分析名称和本地化标题不能参与缓存身份。光段方向和 `RayInteractionKind` 是传播语义的唯一依据，不能从点序、Z 坐标或颜色反推。

## 分析、优化与公差

Core 通过 `AnalysisCatalog` 注册当前 `72` 个规范分析，其中包含非序列光线追迹和探测器查看器。应用层 `IWorkbenchModeService` 只负责顺序/非序列模式边界，`INonSequentialDocumentService` 负责独立非序列文档的对象、波长和显式转换事务。`AnalysisService` 按模式分别暴露顺序 70 项或非序列 2 项并拒绝跨模式执行；桌面端根据同一状态重建 Ribbon、主编辑文档和左侧工具页。Workbench 的规范键、中文显示名、兼容别名、展示类型和两套 Ribbon 目录由 `WorkbenchAnalysisCatalog` 统一描述。独立 `Distortion` 已退出公开目录，旧名称兼容映射到 `Field Curvature and Distortion`。结果 DTO 使用 `AnalysisPresentationKind` 选择专用控件，并通过 `AnalysisAxisQuantity` 与 `AnalysisAxisUnit` 描述坐标量和单位；显示字符串不能决定控件、缩放、缓存身份或导出逻辑。

`WorkbenchRuntime` 与顺序多配置并列持有 `NonSequentialDocument`。整文档快照、撤销/重做、保存排队、脏状态和事务回滚都包含该文档。STAROPT容器版本为2、工程负载版本为4；旧容器/负载继续有界迁移，v3保存内容寻址的网格资产，v4增加扩展原生光源。正常非序列追迹直接接收该文档；旧的顺序表面投影只作为显式转换器的可复用逻辑。STL对象使用对象级BVH之外的三角形级BVH；`INonSequentialTraceSink`把完整分支流式送入独立STARRDB，Core不接收输出路径。应用层把分析结果和3D布局结果保存在不同会话：分析会话服务探测器、分页数据库和路径分析，布局会话只保存有界确定性3D样本。打开/刷新窗口不创建会话；用户显式准备布局光线后才追迹。布局数据库头的场景哈希必须与当前文档一致才会默认加载；过期结果只能在显式选择后显示，并携带结果哈希、当前哈希、来源修订和红色过期状态。两类会话均不进入工程脏状态。

未知顺序几何由 `OpaqueGeometryPayload` 保存完整组件类型、数值、文本和递归子组件；其 Sag、交点和法线接口始终抛出确定错误。`OpticCapabilityPreflight` 是实光线追迹、全部公共旁轴/一阶入口、分析、优化、公差、顺序转非序列、二维/三维布局和导出的统一阻断入口，错误包含面号、原始类型及原因。STAROPT和原生光学快照可无损往返 opaque 数据；STEP、Python Optiland JSON、ZMX、SEQ、LEN和制造图纸等有损目标默认禁止输出。

报告入口属于分析体系，不是独立旧页面：“表面数据报告”逐面输出处方，“系统数据报告”按系统/孔径/视场/波长/近轴量组织，“分类数据报告”按角色/材料/几何类型汇总，“系统数据摘要”复用规范 `First Order` 分析，“基面数据”使用规范 `Cardinal Points Data`。旧“一阶量、处方报告”仅作为兼容别名解析。

优化由变量、操作数、缩放器和优化器组成。公开算法名称严格对应当前实现：阻尼最小二乘、Nelder-Mead、坐标模式搜索、动量梯度下降和贪心随机扰动。历史上使用的 BFGS、L-BFGS-B、COBYLA、差分进化、双重退火和盆地跳跃名称只作为带警告的兼容别名解析，结果始终记录实际算法、版本、停止原因、函数评价次数、梯度范数（适用时）和随机种子（适用时）。优化运行以包含全部配置和断开链接的完整文档为事务边界：取消、求解异常或最终自动半口径刷新失败都会恢复初始状态且不增加撤销记录；只有刷新成功后才发布优化变更。文档生命周期令牌使用独立同步门，取消不等待长期持有的光学模型锁。ZMX 评价函数导入保持源行顺序；当前 `[MS-L7]` 参考文件的 103 行均可见，其中 63 行 `TRAR` 进入现有光线像差执行路径，九类尚无计算引擎的约束/数学操作数按八个 Zemax 参数槽位禁用只读保留。当前目录只有 51 个 Workbench 代码或兼容类型，不能表述为 333 项 Zemax 顺序操作数已完整实现。公差复用变量和操作数模型，支持扰动、采样、补偿、双侧灵敏度及按种子确定的 Monte Carlo。外层并行拥有执行权时，内层追迹会抑制嵌套并行。

## 应用边界

```text
WorkbenchApplication
  WorkspaceCoordinator
    OpticContext
      WorkbenchRuntime
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

`LensLibraryService` 只读加载两类明确分离的资源：版本 2 的设计镜头库 `index.json`/`projects/*.staropt`，以及库存镜头目录。库存目录合并版本 1 的离线 `commercial-index.json` 与当前用户 `Documents/Zemax/Stockcat` 中版本 1001 ZMF 的目录头，但 `StockLensCatalogPolicy` 在读取入口和合并出口都限制为 Thorlabs、Edmund Optics、Daheng Optics、Newport、Sigma Koki 五家；`ZemaxStockCatalogReader` 跳过 ZMF 处方正文，只发布料号、分类、EFL 和 EPD 等目录元数据。`StockLensMatcher` 使用当前文档快照的一阶 EFL/EPD 做厂商、方向和公差过滤，并按归一化双参数偏差排序。设计库条目具有可校验的原生工程和预览；库存目录只有条目显式指向库内 `.staropt` 且文件存在时才提供载入能力，不能从目录元数据或受限 ZMF 正文伪造光学处方。

`WorkspaceCoordinator` 串行化写操作、控制文档生命周期取消令牌、递增修订号并发布分类事件。模型读写锁与生命周期取消锁分离，因此打开、新建或切换配置可以立即向正在运行的优化发出取消。撤销/重做快照包含所有配置、活动配置索引和断开链接；表面、组件、视场、波长、系统环境和玻璃库等全部处方写入以完整文档、撤销/重做历史、状态和延迟事件为事务边界。材料解析、多配置传播或自动半口径刷新任一失败都会恢复更新前状态，不增加修订号或虚假撤销记录。分析、可视化和公差针对快照运行，生产 Services 统一调用 `Application.Runtime.WorkbenchRuntime`，不得引用 `Application.Legacy`。`OptilandConnector` 只作为无新增成员的源码兼容入口保留；它复用同一个 `WorkbenchRuntime`，不再形成第二条生产执行链。架构测试同时检查 Services 的命名空间依赖和运行时类型词汇。

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
