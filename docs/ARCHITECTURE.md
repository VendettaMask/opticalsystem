# 系统架构

## 工程与安装恢复边界（1.0.3）

安装维护采用 `WindowsInstallationGate` 与 Inno Setup 共同的目录文件锁协议，覆盖应用启动、恢复、清理、安装和卸载，不依赖登录会话命名空间。`WindowsInstallationCleanup` 在恢复完成、普通应用存在标记建立之前尝试维护；依照成功提交后发布的外部清单，仅清理非当前/上一版/启动版本的可证明归属载荷。未知、额外或修改文件及重解析路径保留，非递归逐项删除，外部清单支持中断续做。一键版本回退按用户要求不提供；跨真实登录会话及硬件故障验收仍独立记录。

2026-10-13：`DocumentRecoveryStore` 对本会话元数据逐文件读取并隔离已知格式/IO 错误，保留坏记录，使独立新副本和正常 owner 锁释放继续执行；整个目录错误和未预期程序异常仍传播。分析 CSV formatter 根据真实数据可用性选择绘图/表格/摘要，不依赖分析标题，也不从字符串解析量纲；完整契约见[文件输出说明](FILE_FORMATS_AND_PLUGINS.md#分析-csv-的输出来源2026-10-13)。

Application 的文档服务提供轻量恢复状态、完整恢复快照与未保存恢复打开接口；快照在工作区锁下捕获，磁盘写入不更改保存修订号、原路径或未保存标记。Desktop 的 `DocumentRecoveryStore` 管理会话文件锁、最近两份带哈希副本及后台写入，主窗口负责 30 秒计时与恢复选择。安装恢复在普通应用生命周期互斥之前运行，与 Inno Setup 共用持久日志格式；先核验备份、身份和路径，再重放本安装拥有状态。详细适用范围与验收见[恢复说明](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)。

## 核心对象模型

独立 Zemax 验证工具依赖方向为 `ZemaxComparison -> Application/Core`，没有生产项目反向引用。Application 的 `BuildAnalysisData` 和原 `BuildAnalysisView` 共用同一规范分析工厂；前者保留未舍入的类型化数据，后者继续生成 UI。ZOS-API 仅在验证目录的 C# Framework 工作进程中加载，单项超时以 Windows job 隔离。详见 [验证工具边界](ZEMAX_COMPARISON_TOOL.md)。

2026-09-05 数值与结果契约修复见 [项目修复报告](PROJECT_REPAIR_2026-09-05.md)。分析结果以 `AnalysisOutcome` 发布成功、不可计算或不适用状态及原因，并通过 Runtime/DTO 保留；状态页不可统计为数值计算成功。`NormalizedField` 是独立的无量纲轴类型，DTO 映射不依赖不同程序集枚举的相同序号。光斑显示光线与含波长权重的统计视图分开，质心和 RMS 共用统计权重；MTF 在共同物理坐标/频率中合成复数 OTF。串行公差和工作线程共用显式 optic 参数的变量工厂。

`Optic` 是计算中心，拥有系统孔径、视场、波长、表面组、材料注册表、数值后端、光线追迹器、分析、优化、公差和多配置入口。主工作台的可变工程由 `OptilandWorkbench.Application` 协调；内部模块引擎可在自己的隔离计算中使用共享 Core 模型，但不能原地改写主工作台工程。Avalonia 层通过应用服务、模块适配和不可变 DTO 访问，不直接引用 Core。

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

现有膜层和散射近似不作为物理 Thin Film、Lambertian 或 Measured BSDF 对外提供。生产名称为 `ApproximateTransmissionRippleCoating`、`MainRayScatterLossApproximation` 和 `MeanMeasuredScatterLoss`，界面统一标记 `Experimental`；旧类名和旧序列化 kind 仅为兼容入口并带弃用警告。2026-09-27 新增共享 `CoherentThinFilmSolver` 和 `ThinFilmSpectrum`，采用稳定散射递推计算真实 n/k、角度、S/P 与 R/T/A，供独立镀膜实验室使用。2026-10-02 新增明确的 `CoherentMultilayerCoating` 接入正式顺序追迹和照度 Jones 链；非序列集成未在本轮验证，旧经验类型含义不变。BSDF `Evaluate + Sample + Pdf` 仍未实现。

## 数值后端与光线追迹

`INumericBackend` 定义标量运算，`IBatchedNumericBackend` 定义批量运算。内置 CPU 后端在常见顺序路径使用 `System.Numerics.Vector<double>`，不支持的曲面回退到标量内核。

`TraceRequest` 明确指定：

- 仅保留最后表面；
- 保留选定表面；
- 保留完整历史。

缓存键由光学系统修订号、后端、输入光线状态、保留表面和影响结果的选项组成；分析名称和本地化标题不能参与缓存身份。光段方向和 `RayInteractionKind` 是传播语义的唯一依据，不能从点序、Z 坐标或颜色反推。

## 分析、优化与公差

2026-09-27 正式优化入口采用 Core `SurfaceCurvatureParameter` 保留平面并允许曲率经过零；默认评价复用 Core 逐光线 RMS 向导。Core 操作数适配和应用独立评价统一以 `OptimizationEvaluationException` 表达无效结果，DLS 处理无效试探步，应用事务继续负责异常回滚。实验室仍使用独立搜索编排与数值求解器，并共用正式光学 Core。实现边界见 [优化算法](OPTIMIZATION_ALGORITHMS.md)和[本次验证](OPTIMIZATION_ENTRY_FIX_2026-09-27.md)。

Core 通过 `AnalysisCatalog` 注册当前 `72` 个规范分析，其中包含非序列光线追迹和探测器查看器。应用层 `IWorkbenchModeService` 只负责顺序/非序列模式边界，`INonSequentialDocumentService` 负责独立非序列文档的对象、波长和显式转换事务。`AnalysisService` 按模式分别暴露顺序 70 项或非序列 2 项并拒绝跨模式执行；桌面端根据同一状态重建 Ribbon、主编辑文档和左侧工具页。Workbench 的规范键、中文显示名、兼容别名、展示类型和两套 Ribbon 目录由 `WorkbenchAnalysisCatalog` 统一描述。独立 `Distortion` 已退出公开目录，旧名称兼容映射到 `Field Curvature and Distortion`。结果 DTO 使用 `AnalysisPresentationKind` 选择专用控件，并通过 `AnalysisAxisQuantity` 与 `AnalysisAxisUnit` 描述坐标量和单位；显示字符串不能决定控件、缩放、缓存身份或导出逻辑。

`WorkbenchRuntime` 与顺序多配置并列持有 `NonSequentialDocument`。整文档快照、撤销/重做、保存排队、脏状态和事务回滚都包含该文档。STAROPT容器版本为2、工程负载版本为 6（新增显式多配置单元格变量）；旧容器/负载继续有界迁移，v3保存内容寻址的网格资产，v4增加扩展原生光源。正常非序列追迹直接接收该文档；旧的顺序表面投影只作为显式转换器的可复用逻辑。非序列文档全量验证先建立对象和网格资产索引，再用迭代图遍历检查参考和包含关系；长链不会因重复线性查找退化。STL对象使用对象级BVH之外的三角形级BVH，网格资产在导入或附加时复制内容并只公开只读字节视图，解码几何不暴露可改写数组。`INonSequentialTraceSink`把完整分支流式送入独立STARRDB，Core不接收输出路径。Fresnel 分裂父分支只发布一次，父子共享历史用段自身的分支 ID 表示。探测器数据库重建只累计当前分支拥有的段，并用受单分支段数约束的集合去重；波长号使用一次建立的有序索引查找，不为每个命中重新分配波长列表。应用层把分析结果和3D布局结果保存在不同会话：分析会话服务探测器、分页数据库和路径分析，布局会话只保存有界确定性3D样本。两个会话使用独立串行通道，但每次追迹都会取得材料注册表快照，避免并发布局和分析追迹共享可写玻璃缓存。每次追迹同时捕获文档代次和结果会话代次，最终数据库移动与会话发布只在两者仍有效时执行；文件切换、清空或手动选择数据库后，旧后台结果不能重新成为当前结果。非序列3D页面打开或发现布局会话过期时会自动准备布局样本；旋转、缩放和显示设置只重绘，不改变随机追迹结果。手动“准备布局光线”执行强制刷新，成功后原子替换布局会话，失败或取消保留旧会话。布局数据库头的场景哈希必须与当前文档一致才会默认加载；过期结果只能在显式选择后显示，并携带结果哈希、当前哈希、来源修订和红色过期状态。两类会话均不进入工程脏状态。

未知顺序几何由 `OpaqueGeometryPayload` 保存完整组件类型、数值、文本和递归子组件；其 Sag、交点和法线接口始终抛出确定错误。`OpticCapabilityPreflight` 是实光线追迹、全部公共旁轴/一阶入口、分析、优化、公差、顺序转非序列、二维/三维布局和导出的统一阻断入口，错误包含面号、原始类型及原因。STAROPT和原生光学快照可无损往返 opaque 数据；STEP、ZMX、SEQ、LEN和制造图纸等有损目标默认禁止输出。

报告入口属于分析体系，不是独立旧页面：“表面数据报告”逐面输出处方，“系统数据报告”按系统/孔径/视场/波长/近轴量组织，“分类数据报告”按角色/材料/几何类型汇总，“系统数据摘要”复用规范 `First Order` 分析，“基面数据”使用规范 `Cardinal Points Data`。旧“一阶量、处方报告”仅作为兼容别名解析。

优化由变量、操作数、缩放器和优化器组成。公开算法名称严格对应当前实现：阻尼最小二乘、Nelder-Mead、坐标模式搜索、动量梯度下降和贪心随机扰动。`LM / DLS`、`Least Squares` 和 `Orthogonal Descent` 只作为真实同义实现的带警告兼容别名；BFGS、L-BFGS-B、COBYLA、Powell、差分进化、双重退火和盆地跳跃在独立实现完成前明确返回不支持。结果记录实际算法、版本、停止原因、函数评价次数、梯度范数（适用时）和随机种子（适用时）。所有规范优化器和应用服务统一接受 `1..1000` 次迭代，零、负数和超限值在评价前明确失败，不再静默夹取或产生零迭代伪成功。优化变量、缩放器、操作数和向量入口都执行严格有限数和维度校验；上下界非法、步长非正、缩放正反变换非有限、向量长度不匹配或操作数返回 `NaN`/无穷都会立即失败。变量向量按事务提交，任一 setter 或操作数失败都会恢复原向量；若恢复本身失败，聚合异常同时保留原始失败和恢复失败。优化运行以包含全部配置和断开链接的独立完整快照为计算边界：捕获和最终提交使用短锁，求解及自动半口径刷新在锁外执行；取消、求解异常或刷新失败均丢弃计算副本且不增加撤销记录。源修订或文档代次变化时不提交旧结果，保留用户当前编辑。成功时一次提交完整配置并发布优化变更，支持一次撤销。文档生命周期令牌使用独立同步门，取消不等待长期持有的光学模型锁。ZMX 评价函数导入保持源行顺序；`ZemaxOperandRegistry` 注册 383 个顺序兼容代码，名称已与官方 2026 R1 API 目录逐项核对，快照为每行独立保存原始整数/数据槽位及 `CompatibilityOnly` 状态，描述符记录槽位引用类型。注册和无损往返不等于完整计算支持；当前受限可执行与兼容保留数量见 [操作数支持](ZEMAX_OPERAND_SUPPORT.md)，已有代码连接相应计算路径，其中 `TTHI/TGTH`、`REAR/RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR` 和若干一阶量已进入定义级可执行路径，`MNIN/MXIN/MNAB/MXAB` 只约束范围内玻璃 Nd/Vd，`POWR` 仅对标准折射面计算 `(n_after − n_before) / Radius`；`TTGT/TTLT/TTVA` 按指定表面到下一表面的边缘总厚度计算而不是系统总长；`GOTO/ENDX/OOFF/SKIN/SKIS/USYM` 已接入有序控制流并通过本机 ZOS-API 对称分支实测；`DIMX` 的 Field/Absolute 语义已在第十一批接通有焦计算。新增路径仍需 Zemax/ZOS-API golden 对照后才能标为完整兼容。其余未完成目标代码禁用兼容保留；禁用兼容行不参与计算；未知类型或被错误启用的兼容行返回明确错误，不会转成 `BLNK` 或成功零值。公差复用变量和操作数模型，支持扰动、采样、补偿、双侧灵敏度及按种子确定的 Monte Carlo。外层并行拥有执行权时，内层追迹会抑制嵌套并行。

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

`LensLibraryService` 只读加载两类明确分离、随应用版本分发的资源：版本 2 的设计镜头库 `index.json`/`projects/*.staropt`，以及 `StockCatalogs/<厂商>.json` 下版本 1 的库存镜头。库存资源按 Thorlabs、Edmund Optics、Daheng Optics、Newport、Sigma Koki 分文件保存目录头元数据；加载时校验文件名与条目厂商一致并拒绝重复 ID。桌面运行时不发现、不读取用户目录中的 ZMF，也不依赖 Zemax 安装。`StockLensMatcher` 使用当前文档快照的一阶 EFL/EPD 做厂商、方向和公差过滤，并按归一化双参数偏差排序。设计库条目具有可校验的原生工程和预览；库存目录只有条目显式指向库内 `.staropt` 且文件存在时才提供载入能力，不能从目录元数据伪造光学处方。ZMF 到自有目录的转换只存在于离线维护工具中，不属于应用运行时依赖。

`WorkspaceCoordinator` 串行化写操作、控制文档生命周期取消令牌、递增修订号并发布分类事件。模型读写锁与生命周期取消锁分离，因此打开、新建或切换配置可以立即向正在运行的优化发出取消。文件读取在锁外完成；实际替换在工作区事务内再次取消读取期间启动的计算，并将当前路径、完整文档、撤销/重做历史、状态、文档代次和延迟事件一起提交。应用文档或自动半口径刷新失败时全部恢复，不发布文件切换事件。撤销/重做快照包含所有配置、活动配置索引和断开链接；表面、组件、视场、波长、系统环境和玻璃库等全部处方写入以完整文档、撤销/重做历史、状态和延迟事件为事务边界。材料解析、多配置传播或自动半口径刷新任一失败都会恢复更新前状态，不增加修订号或虚假撤销记录。分析、可视化和公差针对快照运行，生产 Services 统一调用 `Application.Runtime.WorkbenchRuntime`，不得引用 `Application.Legacy`。旧连接器及 Compatibility 项目已删除；架构测试检查程序集、项目引用、源码、打包和启动输入，阻止 Python 运行时和外部验证资产进入产品依赖图。

有界读取和原子输出的唯一底层实现保留在 Core `BoundedFile`。Avalonia App 不直接引用 Core，而通过 Application 的 `BoundedApplicationFile` 薄代理访问只含路径、流、字符串、字节数和取消令牌的操作；代理不复制限额或写盘算法。程序集架构测试同时阻止 App 重新获得 Core 引用。

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

`WorkspaceContentHost` 只允许位于可见 `DeferredContentPresenter` 内的宿主承载缓存页面。Dock 12 同时保留标签和 MDI 两套视觉容器，隐藏的标签容器仍响应 `ActiveDockable`，不能在 MDI 激活时转移页面。宿主在附加时监听祖先链的 `IsVisible`，隐藏/分离时释放内容与订阅，重新显示时接回原控件；不重建业务面板或选择模型。MDI 标题按钮单独覆盖普通按钮的尺寸，内容边界局部裁剪。实现和验证见 [平铺交互修复](MDI_INTERACTION_FIX_2026-09-27.md)。

主题由 `ThemeRegistry` 注册完整主题包，每个具体 `ThemeDefinition` 同时拥有色板、强调色应用器、实际图标包、Chrome 配置、装饰渲染器和明暗特征。`ThemeApplicationService` 是运行时切换的唯一入口；`ThemeResourceBindings` 保持颜色语义，`ThemeIconResolver` 按实际主题解析稳定图标名，`ThemeChromeRole` 为 Ribbon、工作区、卡片、控件框、状态栏、对话框和视口提供语义边框。明亮、暗夜、异世界和像素风格主题提供相同颜色和 Chrome 资源键；`System` 仅作为跟随操作系统的选择代理。结构边框厚度、布局、文案、命令 ID 和分析语义色独立于主题。扩展规则见 [主题包开发规范](THEME_PACKAGES.md)。

## 桌面局部布局与动作语义（已实现）

- `UiDensity` 统一侧栏宽度 240/256/280 DIP（最小/默认/最大）；`WorkspaceDockFactory` 约束新建/恢复 ToolDock，`PanelManager` 约束布局应用和保存，`AppSettings` 使用同一默认值。`SystemPropertiesPanel` 使用有限宽度 Grid 参数行，禁止侧栏横向滚动，保留原分组与业务事件。
- 主要按钮声明 `accent`，`BlueThemeTokens`、`LightTheme`、`BlueThemeStyles` 提供各状态，`ControlAvailability` 只连接既有可用条件与提示/无障碍说明。
- `ScrollableHeaderGrid` 在有限高度内测量顶部区域和正文，防止库存匹配重新打开后说明/按钮与表头重叠；短视口只滚动受限区域。
- `DataGridHeaderLayout` 只调整 Fluent 排序图标槽位的测量，`CompactLabel` 保留短标签完整提示并独立布局单位；不修改表格模型、排序与输入校验。三套制图继续使用原分栏和断点。
- 机械半直径显示接入 `NumericDisplayFormatter`，材料行背景使用 `MaterialDisplay`/`HasOpticalMaterial` 业务语义，不能把显示字符串用于数值排序。

实现与证据见 [容器测量修复](PANEL_CONTENT_LAYOUT_2026-09-27.md)、[主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)。

## 会话与持久化

顺序模式的系统初始布局由 `WorkspaceDockFactory` 创建“镜头数据”和“二维视图”两个文档，镜头数据为活动页，左侧系统选项保留。`PanelManager` 的顺序启动及自动文件切换不再加载保存会话中的额外页面；显式载入默认布局/槽位仍使用完整反序列化路径，非序列模式的恢复流程保持原样。

光学工程和 Dock 会话分开保存。全局默认布局位于 `%APPDATA%\OptilandWorkbench\workspace-default.json`；按文件会话使用规范化绝对路径的 SHA-256 哈希作为文件名。

会话保存 Dock 图、文档描述符、分析设置、实例 ID、活动文档、锁定状态和浮动边界，不保存大型计算结果。布局修改经 500 ms 防抖保存并在退出时刷新。损坏会话会备份并回退；未知分析会跳过；空浮动窗口会过滤。

## 光学镀膜设计实验室

`labs/CoatingDesign` 保存内部 Engine 和测试，`.coating.json` schema 1 仍是模块自己的实验格式。UI 已移入 `src/OptilandWorkbench.App/Laboratories/CoatingDesign`，由 `IntegratedLaboratories` 创建同进程、受主窗口管理的窗口；不存在外部启动器或独立 App。主程序引用 Modules 和必要领域库，App 本身仍不直接引用 Core。Engine 复用 Core 的材料、薄膜求解、优化器、取消、公差与原子文件；Modules 通过规范映射输出显示 DTO，UI 直接共享主程序主题和字体。模板、残差、候选、采样收敛和目标判定属于流程层，不产生第二套光学实现。

后台深拷贝输入，按实验 ID 与代次提交；输入失效、取消、切换实验使旧回调无效。保存结果核对输入哈希，打开用冻结材料复算。主程序关闭统一协调取消、等待和未保存确认。两个模块拥有各自编辑状态和测试集，但构成一个必装产品。已实现能力与真实未达标示例见[镀膜计算契约](COATING_DESIGN_LAB.md)，最新工程验收见[统一交付](BUILT_IN_MODULES_2026-10-13.md)；早期数值资料见[历史验收](../validation/coating/README.md)。

2026-10-02：`ZernikeMetrics` 在正式 Core 汇总共享 OPD、Fringe/Standard/Annular 拟合与原始样本统计；操作数与分析报告共用，不在应用层复制波前计算。Noll 编号和稳定环形递推的适用范围及验证见[第二十四批](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十四批-zern-与共享拟合修正部分完成)。

2026-10-02：正式 Core `ContrastMetrics` 共用 `WavefrontEngine`、工作 F 数与无焦截止频率，供对比度操作数和损失图使用。应用层不重复光学计算；原生符号/聚合与数值验证边界见[第二十五批](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十五批对比度操作数与共享损失图部分完成)。

2026-10-02 第二十六批：DIST 复用 `SeidelMetrics`，DISA 的用户矩阵和主光线位于 `DistortionMetrics`，网格分析与 DISG/DISA 共用有符号向量差。应用层仅提供槽位元数据与帮助，未增加第二光学引擎。原生验收边界见[本批说明](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)。

## 拆分与架构维护

本节合并原《大文件拆分实施记录》；旧独立文件已删除，原文可由 Git 恢复。

已完成的边界包括按分析族拆分的 AnalysisFramework、规范 WorkbenchRuntime 与应用服务、WorkbenchApplication 组合根、WorkspaceCoordinator 事务/修订/取消、OpticalDrawingRenderer 绘制分部、MainWindow/Shell 和 AnalysisPanel 的生命周期/结果/绘图/导出分部。ToleranceOperandEditorRow 已独立为可测试编辑模型；NonSequentialDetectorDisplay 承担无 UI 状态的探测器显示计算。Python 专用格式与旧 Compatibility 空壳已退出产品，不能重新引入。

后续拆分按计算、DTO 映射、状态、命令、视图或渲染职责进行，不为降低文件行数合并不同算法。Core 不依赖 Application/App，Application 不依赖 Avalonia/Dock，App 不直接引用 Core；公共协调规则留在 WorkspaceCoordinator。Modules 适配和内部领域库不构成独立产品或另一套计算内核。

机械迁移不得改变数值语义、序列化格式、兼容默认值或 GUI 文案。保留架构、跨入口一致性和数值回归；不删除测试换取拆分成功，不用本地化标题决定类型/缓存。跨层或发布变更需按 [构建与发布](BUILD_AND_RELEASE.md) 建立相应完整验收，局部定向通过不能冒充全仓通过。
