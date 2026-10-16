# 全仓库代码阅读地图

更新：2026-10-13。按职责定位当前代码；旧静态逐文件/行数表已经移除，使用下方命令获取实际文件。验证结果统一见 [当前状态](CURRENT_STATUS.md)。

## 1. 工程边界

- 正式产品：`OpticalSystemDesign.slnx`，由 Core、Application、App、正式测试和离线工具组成（2026-09-25 校正：Compatibility 项目已移除）。
- 内置初始结构与镀膜设计：根 `OpticalSystemDesign.slnx` 统一构建，界面在 `src/OpticalSystemDesign.App/Laboratories/`，显示适配在 `src/OpticalSystemDesign.Modules/`；`labs/` 仅保留内部领域库、测试及历史证据，不是独立产品。
- 桌面启动：`Program.Main → App → MainWindow → WorkbenchApplication.Create`。`MainWindow` 创建工作区、命令和服务，具体功能分布在 `Shell` 与 `Panels`。
- 实际依赖方向：主工作台为 `App → Application → Core`；内置模块为 `App → Modules / 内部领域库 → Application / Core`。App 没有直接 Core 引用，UI 契约不暴露 Core 类型；旧 Compatibility 项目已移除。
- Core 没有第三方 NuGet 计算库依赖；FFT、几何交点、数值优化、材料公式等实现位于仓库内。Application 引用 SkiaSharp，App 引用 Avalonia、Dock 与 SkiaSharp。
- 桌面 UI 大量使用 C# 构造控件和事件订阅。`ViewModels/EditorRows.cs` 主要是编辑行模型，不能把整个 App 理解成纯 XAML/MVVM 绑定架构。
- Python 仅用于产品之外的 Zemax 捕获/报告、FreeCAD 验证和图标维护；Optiland 参考生成器已删除。正式产品无 Python 依赖。

## 2. 核心模型与顺序追迹

`Optic` 通过内部 `OpticState` 组织系统属性、孔径、视场、波长、材料、表面、拾取、求解和评价函数。快照恢复会重建状态，并重新绑定相关服务。

`OpticalSurface` 同时维护编辑器需要的传统字段和组合模型。修改半径、材料或交互时必须关注两套表示的同步；序列化另有 `SurfaceSnapshotCompatibility` 处理旧数据。

`SurfaceGroup.Renumber` 不只修改面号，还按厚度重算 Z 坐标，同时保留 X/Y 偏心与旋转。物面只有正无穷厚度才是无限共轭；有限物距和零物距不能混用无限物距路径。

顺序追迹链：

```text
字段/孔径/波长
  → RayGenerator：视场换算、物方射线、必要时光阑瞄准
  → ApertureSampler：光瞳采样与权重
  → SequentialRayTracer：追迹请求、保留表面、缓存
  → 按表面推进光线状态
      → 可支持组合：批量 CPU 路径
      → 其他组合：OpticalSurface.TraceRayState
  → RequestedTrace / SequentialTrace
```

标量表面路径依次执行局部坐标转换、求交、传播与吸收、孔径裁剪、交互、镀膜和散射。传播距离、累计几何光程、累计光学光程、出射介质及显式交互类型都会进入结果。反射和全反射保留入射介质。

批量路径仅接纳平面/标准面、无孔径或圆孔径、折射反射交互、简单或无镀膜、无散射且均匀传播的组合。`PooledRayStateBuffer`、`PooledDirectionBatch` 与 `SurfaceBatchWorkspace` 管理池化数组；复杂曲面并不会自动获得同等 SIMD 支持。

`TraceRequest` 将计算与结果保留分开：末面、选定表面、完整历史可以拥有不同内存成本。`RequestedTrace` 是需要释放的结果所有者，其视图受同一生命周期约束。

`TraceRequest.IncidentSurface` 选择入射探测面功率：保留传播与孔径检查，停在该面自身作用前；终止面选项参与缓存身份，不能与出射样本混用。约束及回归见[图像与辐射量修复](IMAGE_RADIOMETRY_REPAIR_2026-10-16.md)。

`RayTraceCache` 有条目和样本预算，按插入顺序淘汰，不是访问即更新的 LRU。缓存使用文档修订、后端、精确输入光线、保留面和影响结果的选项。`RayTraceCacheBinding` 订阅模型变化，在追迹相关变更发生时解除共享缓存绑定。

## 3. 几何、材料和物理组件

| 子系统 | 实现重点 |
| --- | --- |
| Geometries | 平面、标准面、非球面、双锥面、环曲面、多项式、Chebyshev、Zernike、Forbes Q；通用交点有阻尼 Newton、前向区间搜索和回退，返回残差、法线与状态 |
| Materials | 常折射率、Cauchy、Sellmeier、Abbe、表格数据与 Zemax 色散公式；按工程玻璃目录顺序消歧，材料注册表支持独立快照 |
| Apertures | 系统孔径与物理孔径分开；圆、环、矩形、椭圆、多边形、文件孔径及布尔组合 |
| Apodization | 光瞳强度加权，包括 Gaussian、Hann、Tukey 等 |
| Interactions | 折射/反射、薄透镜、衍射及相位交互；返回传播方向和交互类型 |
| Phase | 常相位、线性光栅、径向、多项式和网格相位；网格支持插值 |
| Coatings | 无镀膜、标量系数和经验兼容模型保持原义；`CoherentThinFilmSolver` / `ThinFilmSpectrum` 提供真实相干功率及复振幅，`CoherentMultilayerCoating` 接入正式顺序追迹和照度 Jones 链，完整材料快照可保存；实验室 UI 应用与旧电场 Jones 光瞳尚未接通 |
| Scattering | 现有近似主要扣除主光线强度，不生成完整 BSDF 方向分布 |
| Propagation | 均匀传播、旧入口方向近似，以及 Gradient 1～5 连续曲线积分；正式顺序显式光线及限定共轴系统的连续近轴矩阵已接通 |
| Plugins | 通过程序集加载与注册工厂扩展几何、材料、分析；注册机制本身不能证明桌面已接入全部扩展入口 |

`OpaqueGeometryPayload` 保留未知组件树，但其 Sag、法线、求交不可计算。`OpticCapabilityPreflight` 是多种计算与有损输出入口的共同能力检查。

## 4. 分析体系

`AnalysisCatalog` 负责 Core 的规范分析工厂；Application 的 `WorkbenchAnalysisCatalog` 负责产品名称、兼容别名、展示种类和模式目录。`WorkbenchRuntime.Analysis.Parameters` 提供产品参数描述，`WorkbenchRuntime.Analysis` 将参数映射到分析构造器。

`BaseAnalysis.GenerateData` 产生曲线、散点、热图、分面、表格、文字及轴元数据。App 通过 `AnalysisPresentationKind` 分派展示，单位转换和 CSV 格式由类型化轴元数据控制。

主要算法关系：

- 点列与光线像差：`SpotAnalysisEngine` 组织视场、波长、参考点及光瞳采样；Ray Fan、矩阵点列和全视场点列复用追迹能力。
- 波前：`WavefrontEngine` 计算主光线参考球或无焦参考平面的 OPD；`ReferenceSphereWavefrontEngine` 提供其他参考球策略；`ZernikeFitEngine` 负责 Fringe、Standard、Annular 拟合。
- 衍射：`DiffractionEngine` 构造复光瞳，计算 FFT、MMDFT/Huygens PSF 与 OTF/MTF；强度到复振幅使用平方根。偏振加权标量 PSF 与完整矢量衍射应区分。
- MTF 扫描：`MtfMethodEvaluator` 统一 Fourier、Huygens、几何方法的扫描入口；`SampledMtfEngine` 使用拟合波前与光瞳位移计算采样 OTF。
- RMS：`RmsScanSupport` 处理焦移、波长、视场、参考方式和单位；显示名称相近的分析不一定采用相同采样定义。
- 辐射与能量：圈入能量、线/边缘扩散、扩展源、相对照度、辐照度和辐射强度分别处理权重、归一化及采样域。
- 探测面辐照度使用共享入射追迹，不计自身膜层损耗；绝对辐射强度使用 `ProjectedAngleSolidAngle` 的逐箱真实立体角，未缩放光线功率的类型化单位为 W。
- 新增可选均匀像方方向余弦采样见 [IlluminationMetrics.ImageCosine.cs](../src/OpticalSystemDesign.Core/Services/IlluminationMetrics.ImageCosine.cs)：真实追迹求逆、孔径掩码和有界失败；`RelativeToAxis` 单独实现轴上归一化。默认路径与原生等价的边界见[像方网格与轴上参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)。
- 相对照度与图像模拟共用 [IlluminationMetrics](../src/OpticalSystemDesign.Core/Services/IlluminationMetrics.cs) 的内部求积、物理孔径掩码及像面投影。透明介质偏振使用 `FresnelPower` 和 `UnpolarizedPowerTransport`，诊断通过显式选项停在像面自身作用前。后续物理膜层连接见[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)；RELI/EFNO 的评价入口为 [MeritFunction.Illumination](../src/OpticalSystemDesign.Core/Optimization/MeritFunction.Illumination.cs)。吸收入射介质及原生采样一致性仍未完成。
- 图像模拟：`ImageSimulationEngine` 组织测试图、PSF 基、空间变化卷积、畸变映射和重采样；扩展图像分析在此之上构造不同工作流。图像/PSF 行使用高 Y 到低 Y 的共同顺序；偏振接通共享功率/Huygens 服务，固定孔径关闭只修改独立副本的非光阑掩码；畸变公开入口在采样前检查尺寸、阶数和工作量。边界与回归见[图像模拟修复](IMAGE_SIMULATION_REPAIR_2026-10-16.md)。
- 坐标与参考：图像模拟的逆畸变和自动采样间距使用像面局部 X/Y；几何核以 `floor((N-1)/2)` 对齐点核和 same 卷积，Huygens 质心选项调用共享 `HuygensGeometricCenterOffset` 并传入真实采样偏移。见[后续坐标修复](IMAGE_COORDINATE_REFERENCE_REPAIR_2026-10-16.md)。
- 图像文件边界：Application 的 `ImageFileLoader` 负责受限位图读取和原子导出，`WorkbenchRuntime` 按源类型解析实际输入；App 的 `ImageFileViewerWindow` 保留独立查看工具职责。元数据 Skia 包装流不拥有随后解码所需的文件流，BMP 由文件服务写出标准 BGR/填充行。见[文件修复](IMAGE_FILE_REPAIR_2026-10-16.md)。
- 报告：一阶量、基面、Seidel、处方、系统和分类报告均属于分析目录。
- `AnalysisResourceLimits` 在昂贵采样、FFT 网格和图像模拟前约束资源。

## 5. 应用事务、异步与持久化

`WorkbenchApplication` 是组合入口，各服务共享 `WorkspaceCoordinator`。`OpticContext` 分离模型锁与生命周期锁，取消长期计算无需先取得模型锁。

处方事务链为 `PrescriptionService → WorkspaceCoordinator.MutateTransactional → WorkbenchRuntime.ExecuteTransactionalEdit`。事务包括文档快照、撤销重做、状态及延迟事件；自动半口径更新属于提交前工作。失败时恢复，而不是先发布成功再补更新。

`AnalysisService` 在锁内复制光学/非序列快照和修订号，在锁外运行计算。`AnalysisPanel` 同时检查实例、请求代次、取消状态与来源修订。切换文件时，即使页面锁定，也会清除旧内容；普通处方变更与系统/视场/波长变更的自动刷新策略不同。

保存采用捕获快照时的排队顺序。保存过程中发生的新编辑不会因为旧快照写盘完成而被错误标为已保存。`DocumentGeneration` 用于区分整个文档的替换，`Revision` 用于区分编辑版本，两者不能互换。

原生持久化：

```text
完整文档（多配置 + 活动配置 + 断开链接 + 非序列文档）
  → OpticSnapshot / ComponentSnapshot
  → 语义校验和有界迁移
  → STAROPT 压缩负载 + 内容寻址网格资产
  → BoundedFile 原子替换
```

STAROPT 容器版本、工程负载版本、Optic 快照版本是不同概念。交换格式也不同于原生工程：ZMX、SEQ、LEN 的可表达范围有限，不能假定无损。

旧专用 JSON 读取器已删除，原生快照仍执行模式验证。ZMX 读取器处理系统参数、表面、配置、部分求解和有序评价函数；未知及兼容行为必须以具体分支为准。

多配置以第一个配置为基础，其他配置有按属性记录的继承断开。结构增删需要同步所有配置并重映射面号；材料传播必须复制实际材料数据，而不只是名称。

## 6. 非序列系统

新模式由 `NonSequentialDocument` 和 `NonSequentialDocumentTracer` 实现。`Raytrace/NonSequentialRayTracer.cs` 中的旧表面场景追迹器仍存在，不能将它当成新非序列工作区的完整实现。

新文档拥有对象身份、坐标参考/包含关系、光源参数、波长、追迹设置及网格资产。文档验证建立索引并检查引用图；STL 导入经过网格归一化、拓扑与相交检查，三角形级 BVH 与对象级 BVH 配合求交。

追迹以源射线迭代器和活动分支队列推进，维护当前介质、对象进入/离开状态、路径长度、光学光程及能量分类。Fresnel 完整分裂与 Simple Stochastic 使用不同分支策略。段数、分支数、源射线数和强度阈值会影响终止与截断统计。

`NonSequentialRayDatabaseWriter` 通过 `INonSequentialTraceSink` 接收分支流；读取器支持索引和分页。`NonSequentialPathFilter` 是独立的表达式解析/求值实现，路径分析与探测器重建消费数据库分支。重建要避免父子分支共享历史被重复计入。

应用层分别维护分析会话和布局会话，使用独立串行通道。累积追迹要求场景哈希与追迹配置指纹一致；提交还检查文档代次和会话发布代次。临时数据库归会话所有，用户选定文件的所有权不同。

三维布局仅消费有界样本。旋转、缩放和显示选项改变绘制，不重新抽样；过期光线显示由显式选项控制。探测器显示变换、平滑和剖面不应修改原始物理数据。

## 7. 优化与公差

`OptimizationProblem` 组织变量、缩放、上下界、操作数、目标残差与约束残差。正权重进入目标，负权重进入约束；变量向量和试探评价有恢复机制。

`NumericalOptimizers` 实现 DLS、Nelder-Mead、坐标模式搜索、动量梯度下降、贪心随机扰动。DLS 使用有限差分 Jacobian、阻尼调整与约束步求解；可使用独立模型进行并行残差评价。旧算法类名需要查看实际转发目标。

`MeritFunctionCatalog.EvaluateAll` 按行求值，包含前序引用、行范围及控制流；被跳过或不可用行不能作为普通有效前序值。`ZemaxOperandRegistry` 的描述符用于原始槽位、编辑器字段与单位解释。`CompatibilityOnly` 与真正可执行的状态分开。

一个必须记住的实现细节：`EvaluateCore` 可以返回 `NaN + 无限贡献 + Error`；`CreateOperand/CreateOperands` 和 `WorkbenchRuntime.Optimization` 的若干适配分支会将错误转为 `1_000_000` 罚值。页面上的明确错误与优化器内部接收的罚值不是同一条失败传播路径。本文记录现有代码行为，没有在本轮验证或修改该策略。

公差通过扰动、采样器、评价准则和补偿变量组合工作。灵敏度包括正负端点，反求有独立端点状态。Monte Carlo 并行路径按 trial 派生种子并使用独立模型，外层并行会抑制嵌套追迹并行。App 的公差编辑器拥有独立未保存状态，不能只检查 STAROPT 脏状态。

## 8. 桌面、图形和制造

- `ActionManager` 管理命令注册、查找、执行及错误；`Shell/MainWindow.*` 组织 Ribbon、文件、优化与工作区流程。
- `WorkspaceDockFactory` 管理稳定文档 ID、描述符、内容创建与释放；`PanelManager` 管理停靠、浮动、MDI、锁定、保存与恢复。
- `WorkspaceSessionStore` 保存布局图、文档描述、分析设置及窗口边界，不保存大型分析结果；路径哈希需尊重真实文件系统的大小写语义。
- `AnalysisPanel` 拆分参数、结果、图表和导出；`AnalysisPlotControl`、`WavefrontSurfaceControl`、`OpticSceneControl` 等实现各自的绘制与交互。
- `SceneViewport` 保持以指针为中心的缩放锚点；可访问性同伴和键盘操作由专门辅助类提供。
- 主题由色板、图标、字体、Chrome、装饰与强调色组成；`ThemeApplicationService` 在 UI 线程上协调切换。显示字体缩放另由 `DisplayTypography` 管理。
- 制造模块在 App 内消费 DTO，组织单片、胶合组、系统图、规格与标题栏。当前内置 XML 模板决定页面区域、表格和字段绑定，SkiaSharp 输出预览和 PDF。
- 制造模型经 `ManufacturingGeometryService` 消费完整几何参数快照；矢高、法线斜率与厚度采样由 Core `ManufacturingProfileGeometry` 计算。径向评估/单片图只接受共轴、无倾斜的旋转对称面；自由曲面明确拒绝，不用基底圆锥替代。工艺规则和绘图裁切仍属于 App。
- STEP 输出位于 Core，使用实际几何与坐标生成自适应分面实体，检查闭合、体积、方向和自相交。它不是保留解析曲面和全部光学语义的 CAD 导出。

## 9. 初始结构实验室

光学装调实验室另走主程序内独立窗口：`MainWindow.OpticalAssembly.cs` → `Laboratories/OpticalAssemblyWindow.cs` → Application 的 `OpticalAssemblyService` → 正式 Core 的 `Analysis/Assembly/AssemblyConjugates.cs`。Core 复用既有 `OpticalSurface.TraceRayValue` 和薄透镜交互模型，窗口使用 DTO；`AssemblyReticleView` 只渲染固定毫米坐标的光线计数网格。`AssemblyMeasurementMode` / Core `AssemblyProbeMode` 显式区分附加物镜与无穷远配置；无穷远模式不创建附加物镜，结果分别携带自准直仪位置、可空的附加物镜位置和共轭匹配状态。找像由共享内核残差导数外推求解。功能边界和测试见 [光学装调实验室](OPTICAL_ASSEMBLY_LAB.md)。

当前桌面入口在 `Laboratories/InitialStructure/MainWindow.Workflow.cs`：预检查、生成、恢复和追加细化统一调用 `FlatStartSearchService`。形式枚举、v19 渐进恢复、预算和完整目标验收位于内部 Engine；`FlatStartSearchCheckpointStore` 保存有界、带校验的 family checkpoint。当前源文件定位可运行：

```powershell
rg --files labs/InitialStructure/src | rg 'FlatStartSearch|FlatStartSearchCheckpoint|CandidateExport'
```

UI 经 Modules 消费共享 Core 的正式布局/分析 DTO，候选导出由 `CandidateExportService` 写 STAROPT、重开并比对同一快照。操作、数据目录和支持范围分别见 [模块说明](../labs/InitialStructure/README.md) 与 [模块契约](INITIAL_STRUCTURE_LAB_PLAN.md)。

`InitialStructureSearchService`、`FlatRootFactory → FirstOrderSeedGenerator → HybridCandidateRefiner` 和 `RunDirectoryStore` 是保留的历史 hybrid 路径，不是当前桌面的搜索/保存主线。其差分进化也不代表正式 `OptimizerCatalog` 已公开同名算法；旧记录、测试和外部验证入口在 [历史方法索引](../labs/InitialStructure/README.md#历史方法与验证) 中区分。

## 10. 工具与测试如何配合

| 范围 | 代码职责 |
| --- | --- |
| LensLibraryBuilder | 离线转换、索引和库存目录处理；发布器使用替换目录与备份回滚 |
| ZemaxLibraryImporter | 将指定 ZMX 安装为原生样例及镜头库条目，协调多个文件替换 |
| GlassCatalogConverter | 转换 AGF 玻璃目录为本项目可读资源 |
| NonSequentialSamples | 生成确定性的教学场景并验证追迹结果 |
| Benchmarks | 顺序追迹、MTF、公差和 STARRDB 性能测量与有界 CI 烟测 |
| ZemaxComparison | 类型化原生对照；`--recapture-workbench` 复用同一分析工厂输出历史报告格式，已取代 AccuracyCapture；GUI capture 另有 App 入口 |
| zemax_parity | 外部基线捕获、完整性检查、当前结果对照和 GUI 图片报告 |
| step_validation | FreeCAD/OpenCascade 导入 STEP 并检查实体有效性与数量 |
| 同步脚本 | 离线收集公开镜头资料与安全解包，不是桌面启动依赖 |

测试按核心数值、Zemax 对照及冻结历史回归、文件格式、资源边界、事务、并行、架构、GUI 契约和工作区行为分布。部分 GUI 测试读取源码验证契约，部分通过 Avalonia Headless 操作控件；两者均不能自动等同于真实桌面的全流程人工验收。

基线完整性校验只说明捕获资料完整，不代表当前版本重算一致；数值比较与截图比较也分别有独立工具。Zemax 默认精度权威为 `123456.ZMX` 捕获基线，另有 MS-L7 评价函数 golden；必须保留文件、设置和版本限定。

CI 统一构建根解决方案，分别执行主程序和内置模块的三平台测试，并另有格式、依赖审计、性能烟测、STEP fixture 生成及第三方导入验证。

## 11. 后续修改的定位规则

| 修改目标 | 首先定位 | 同时检查 |
| --- | --- | --- |
| 处方字段或表面组件 | Domain、PrescriptionService、Runtime.Prescription/Components | 多配置传播、快照、撤销、半口径 |
| 新分析或参数 | Core Analysis、AnalysisCatalog、Runtime.Analysis* | WorkbenchAnalysisCatalog、轴元数据、展示种类、GUI 契约 |
| Zemax 操作数 | ZemaxOperandRegistry、MeritFunction、ZemaxZmxReader | 原始槽位、快照校验/迁移、帮助、编辑器、golden |
| 新非序列对象 | NonSequentialDocument、TracingEngine、Mesh | DTO 映射、序列化、编辑器、布局、场景哈希、数据库 |
| 优化算法 | OptimizationFramework、NumericalOptimizers | 真实目录名称、停止原因、失败/取消、文档提交 |
| 公差 | TolerancingFramework、Runtime.Tolerancing* | 编辑器独立脏状态、补偿、确定性并行、报告 |
| 图纸 | Manufacturing、DrawingTemplates XML | 字段绑定、规格表、预览/PDF 共用路径、制造测试 |
| 布局/主题 | PanelManager、WorkspaceDockFactory、Theming | 会话恢复、浮动宿主、事件释放、可访问性 |
| 文件保存/导入 | Runtime.Documents、Serialization、FileIO | 资源预算、原子替换、旧版本、失败恢复 |

## 桌面变更定位补充（2026-09-27）

| 修改目标 | 实现入口 | 回归入口 |
| --- | --- | --- |
| 主按钮/禁用提示 | 各页面 `accent`，`Controls/ControlAvailability.cs`，`Theming/BlueThemeTokens.cs` | `PrimaryActionPresentationTests`、`BlueThemeInteractionTests` |
| 侧栏宽度与参数同行 | `Controls/UiDensity.cs`，`Services/AppSettings.cs`、`WorkspaceDockFactory.cs`、`PanelManager.cs`，`Panels/SystemPropertiesPanel.cs` | `CompactSidebarTests`、`SystemPropertiesPanelSectionThemeTests` |
| 结果区重叠/表头截断/单位 | `Controls/ScrollableHeaderGrid.cs`、`DataGridHeaderLayout.cs`、`CompactLabel.cs`，库存/制图面板 | `PanelContentLayoutTests` |
| 双页启动/MDI 点击空白 | `WorkspaceDockFactory.cs`、`PanelManager.cs`、`WorkspaceViewLocator.cs` | `WorkspaceDockModelTests`、`WorkspaceMdiInteractionTests` |
| 材料蓝行/机械半径精度 | `ViewModels/EditorRows.cs`，`LensEditorPanel`，`NumericDisplayFormatter` | `LensMaterialRowThemeTests`、`SurfacePropertiesPanelTests` |

表内 App 源文件均相对 `src/OpticalSystemDesign.App`，测试位于 `tests/OpticalSystemDesign.Tests`。此表用于职责定位；完整文件集合以 `rg --files` 为准。验证数字按 [当前状态](CURRENT_STATUS.md) 的范围分别解释。

## 12. 实时文件索引

```text
rg --files src labs tools tests -g *.cs -g *.csproj
```

不再维护随拆分立即过期的行数与全文件表；下方保留工程配置和关键入口。

## 13. 工程配置与声明式模板

以下补充工程引用、实验室构建规则及图纸模板。除第 12 节已列出的根构建规则外，不并入上述源码行数。

| 文件 | 作用或项目引用 |
| --- | --- |
| [Directory.Build.props](../Directory.Build.props) | MSBuild 构建规则 |
| [Directory.Build.targets](../Directory.Build.targets) | MSBuild 构建规则 |
| [labs/InitialStructure/Directory.Build.props](../labs/InitialStructure/Directory.Build.props) | MSBuild 构建规则 |
| [src/OpticalSystemDesign.Modules/OpticalSystemDesign.Modules.csproj](../src/OpticalSystemDesign.Modules/OpticalSystemDesign.Modules.csproj) | Application、初始结构/镀膜领域服务的内部显示适配；无可执行入口 |
| [labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/OpticalSystemDesign.InitialStructure.Contracts.csproj](../labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Contracts/OpticalSystemDesign.InitialStructure.Contracts.csproj) | OpticalSystemDesign.Core |
| [labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/OpticalSystemDesign.InitialStructure.Engine.csproj](../labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Engine/OpticalSystemDesign.InitialStructure.Engine.csproj) | OpticalSystemDesign.InitialStructure.Contracts, OpticalSystemDesign.Core |
| [labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/OpticalSystemDesign.InitialStructure.Persistence.csproj](../labs/InitialStructure/src/OpticalSystemDesign.InitialStructure.Persistence/OpticalSystemDesign.InitialStructure.Persistence.csproj) | OpticalSystemDesign.InitialStructure.Contracts, OpticalSystemDesign.Core |
| [labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj](../labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj) | 主程序、Contracts、Engine、Persistence；与根解决方案统一构建 |
| [OpticalSystemDesign.slnx](../OpticalSystemDesign.slnx) | 解决方案项目集合 |
| [src/OpticalSystemDesign.App/Assets/DrawingTemplates/gb-13323-1991.xml](../src/OpticalSystemDesign.App/Assets/DrawingTemplates/gb-13323-1991.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OpticalSystemDesign.App/Assets/DrawingTemplates/gb-13323-2009.xml](../src/OpticalSystemDesign.App/Assets/DrawingTemplates/gb-13323-2009.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OpticalSystemDesign.App/Assets/DrawingTemplates/iso-10110.xml](../src/OpticalSystemDesign.App/Assets/DrawingTemplates/iso-10110.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj](../src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj) | Application、Modules、InitialStructure.Engine/Persistence、CoatingDesign.Engine；App 无直接 Core 引用 |
| [src/OpticalSystemDesign.Application/OpticalSystemDesign.Application.csproj](../src/OpticalSystemDesign.Application/OpticalSystemDesign.Application.csproj) | OpticalSystemDesign.Core |
| [src/OpticalSystemDesign.Core/OpticalSystemDesign.Core.csproj](../src/OpticalSystemDesign.Core/OpticalSystemDesign.Core.csproj) | 目标框架、资源或包引用 |
| [tools/OpticalSystemDesign.ZemaxComparison/OpticalSystemDesign.ZemaxComparison.csproj](../tools/OpticalSystemDesign.ZemaxComparison/OpticalSystemDesign.ZemaxComparison.csproj) | 原生对照与离线历史重算统一入口；引用 Application，随根解决方案构建 |
| [tools/OpticalSystemDesign.Benchmarks/OpticalSystemDesign.Benchmarks.csproj](../tools/OpticalSystemDesign.Benchmarks/OpticalSystemDesign.Benchmarks.csproj) | OpticalSystemDesign.Core |
| [tools/OpticalSystemDesign.GlassCatalogConverter/OpticalSystemDesign.GlassCatalogConverter.csproj](../tools/OpticalSystemDesign.GlassCatalogConverter/OpticalSystemDesign.GlassCatalogConverter.csproj) | OpticalSystemDesign.Core |
| [tools/OpticalSystemDesign.LensLibraryBuilder/OpticalSystemDesign.LensLibraryBuilder.csproj](../tools/OpticalSystemDesign.LensLibraryBuilder/OpticalSystemDesign.LensLibraryBuilder.csproj) | OpticalSystemDesign.Application, OpticalSystemDesign.Core |
| [tools/OpticalSystemDesign.NonSequentialSamples/OpticalSystemDesign.NonSequentialSamples.csproj](../tools/OpticalSystemDesign.NonSequentialSamples/OpticalSystemDesign.NonSequentialSamples.csproj) | OpticalSystemDesign.Core |
| [tools/OpticalSystemDesign.ZemaxLibraryImporter/OpticalSystemDesign.ZemaxLibraryImporter.csproj](../tools/OpticalSystemDesign.ZemaxLibraryImporter/OpticalSystemDesign.ZemaxLibraryImporter.csproj) | OpticalSystemDesign.Application, OpticalSystemDesign.Core |
| [global.json](../global.json) | SDK 10.0.300，latestPatch，禁止预览 SDK |
| [packaging/macos/Info.plist](../packaging/macos/Info.plist) | macOS 应用包身份及 STAROPT 文档关联 |

## 镀膜实验室代码入口（2026-09-27）

唯一解决方案为 [OpticalSystemDesign.slnx](../OpticalSystemDesign.slnx)。主程序直接包含模块 UI，并引用 Modules 与必要内部领域库；上游归档不参与产品运行。

| 职责 | 入口 |
| --- | --- |
| 真实 n/k、S/P、稳定散射递推与对数透射 | [CoherentThinFilmSolver.cs](../src/OpticalSystemDesign.Core/Coatings/CoherentThinFilmSolver.cs) |
| 采样、峰值/FWHM 与类型化光谱 | [ThinFilmSpectrum.cs](../src/OpticalSystemDesign.Core/Coatings/ThinFilmSpectrum.cs) |
| 目标、膜层、完整材料快照、输入哈希 | [Experiment.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/Experiment.cs) |
| 材料搜索与共享模型接入 | [MaterialLibrary.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/MaterialLibrary.cs) |
| 模板、目标残差、共享优化器、公差和密集验收 | [DesignService.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.cs) |
| 取消与过期结果隔离 | [ExperimentSession.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/ExperimentSession.cs) |
| 原子保存、重开复算、精确 CSV 导出 | [ExperimentStore.cs](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/ExperimentStore.cs) |
| 中文界面与光谱 | [MainWindow.cs](../src/OpticalSystemDesign.App/Laboratories/CoatingDesign/MainWindow.cs)、[SpectrumPlot.cs](../src/OpticalSystemDesign.App/Laboratories/CoatingDesign/SpectrumPlot.cs) |
| 主程序同进程工作区 | [IntegratedLaboratories.cs](../src/OpticalSystemDesign.App/Services/IntegratedLaboratories.cs) |
| 数值/工作流/控件测试 | [测试目录](../labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests) |

源归档、上游对照脚本和物理验收相互独立；完整依据见[实施文档](COATING_DESIGN_LAB.md)与[验收记录](../validation/coating/README.md)。

镀膜入口（2026-10-13 更新）：[结构搜索](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/DesignService.Search.cs)、[显式单位 n/k 表交换](../labs/CoatingDesign/src/OpticalSystemDesign.CoatingDesign.Engine/MaterialTable.cs)、[材料对话框](../src/OpticalSystemDesign.App/Laboratories/CoatingDesign/MaterialEditorWindow.cs)。UI 已移入主程序，直接使用主程序主题与字体服务，不再通过独立 csproj 源码链接。
