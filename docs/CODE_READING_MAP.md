# 全仓库代码阅读地图

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

2026-10-04 MTF 制造公差：指定频率 FFT/几何 MTF、逐视场反求与联合良率、有界间隔/单表面偏心/倾斜补偿及 startol v3 已实现；参数变更清除旧结果。默认 Debug/Release 累计回归各 **3500/3500** 通过（保留此前 3426 项，新增 38 项功能/界面用例并纳入 36 项相邻回归），构建零警告、零错误；独立渲染 **3/3**、9 张实际控件截图已检查。操作数统计仍为 **341/383 项受限执行、42 项兼容保留**，另 4 项扩展；没有新增原生 Zemax 公差数值认证。见[实现、边界与验证](MTF_TOLERANCING_2026-10-04.md)。下方保留各历史阶段的范围和计数。

历史 GRIN 材料编辑阶段（2026-10-04）：Gradient 1～5 桌面系数、显式色散、积分设置及受支持系数变量已接入；修复整行编辑和多配置同名材料状态保留。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展；LPTD 残差与原生捕获仍未完成。默认 Debug/Release 累计回归各 **3426/3426** 通过（保留此前 3389 项，新增 34 项功能和 3 项界面用例），零失败、零跳过；默认双配置构建零警告、零错误。界面/架构 **45/45**、独立渲染 **3/3** 通过，12 张真实控件截图已检查。见[本批实现与验证](GRIN_MATERIAL_EDITOR_2026-10-04.md)。下方保留历史阶段记录。

历史 Gradient 5 基础阶段：2026-10-03 Gradient 5：共享 Core 新增四次轴向分布、广义 Sellmeier 色散、连续/近轴追迹和严格保存；已有六点材料约束读取所选波长。LPTD 约束残差、边界倾斜项、桌面系数编辑和原生捕获仍未完成。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项扩展。默认 Debug/Release 累计回归各 **3389/3389** 通过（保留全部 3342 项，新增 44 项功能和 3 项帮助测试），零失败、零跳过、零编译警告/错误。帮助/架构 **26/26**，独立渲染 **3/3**，三张真实控件截图已检查。见[本批实现与验证](GRADIENT5_DISPERSION_2026-10-03.md)。下方保留历史阶段记录。

历史自动渐晕阶段：2026-10-03 自动渐晕：新增 SVIG 受限执行，按当前主波长和实际孔径计算四条边缘光线的渐晕因子，隔离到后续评价行；参数编辑、优化重算和 STAROPT 保存已接通。当前 **341/383 项受限执行、42 项兼容保留**（35 项已知功能、2 项定义待核实、5 项 Unused），另 4 项本程序扩展。默认 Debug/Release 输出累计回归各 **3342/3342** 通过（保留前批全部 3291 项，新增 51 项），零失败、零跳过、零编译警告/错误。状态/帮助/架构子集 **167/167**，独立渲染 **3/3**，三张真实控件截图已检查。多波长包络、复杂瞳孔全局最优、SVIG 后 CONF 和原生数值/列映射仍未完成。见[本批实现与验证](AUTOMATIC_VIGNETTING_2026-10-03.md)。下方保留历史阶段范围。

历史行表阶段：2026-10-03 多配置行表：新增 MCOV/MCOG/MCOL 三项受限执行，按独立行号和配置号读取 THIC/CRVT/CONN/SDIA 绑定参数；行重排引用、输入验证、优化候选和 STAROPT v5 保存撤销已接通。**340/383 项受限执行、43 项兼容保留**（36 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3183/3183**（前批 3135 + 新增 48），零失败、零跳过、零编译警告/错误。更多 MCE 类型、单元格变量/配置拾取解以及原生行序、列和数值映射仍未完成；原生 MCO 导入只读。见[行表实现与验证](MCE_ROW_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 多重配置：新增 CONF、ZTHI 两项受限执行，配置上下文贯通有序评价、应用显示及优化候选；基准几何链接和拾取在候选副本内同步，活动配置保持不变。**337/383 项受限执行、46 项兼容保留**（39 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认 Debug/Release 合并回归各 **3135/3135**（前批 3089 + 新增 46），零失败、零跳过、零编译警告/错误。PRIM/CVIG/IMSF 后接 CONF 的组合、其他配置变量联合搜索及原生数值/列映射仍未完成，原生导入只读。见[多配置实现与验证](MULTI_CONFIGURATION_OPERANDS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

前批阶段记录：2026-10-03 膜层约束：新增 CoatingLayerParameters.cs、CoatingLayerMetrics.cs、MeritFunction.CoatingLayers.cs；CoatingLayerEditorWindow.cs 提供材料/厚度/倍率/偏移及固定/变量编辑。WorkbenchRuntime.Optimization 的类型化绑定区分曲率、间隔和膜层参数，独立评价快照一致写入。CoatingLayerConstraintTests 与 CoatingLayerEditorTests 覆盖解析比较、实际优化、文件及界面。 详见[实现与验证](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

2026-10-03 CODA：系统偏振模型在 Core/Rays/SystemPolarization.cs，快照及应用事务通过 PrescriptionService 接通；侧栏在 SystemPropertiesPanel.Polarization.cs。CODA 数据投影为 CoatingRayMetrics.cs，定义入口为 MeritFunction.Polarization.cs。CoatingDataOperandTests 和 PolarizationSettingsPanelTests 覆盖计算、保存、撤销、编辑与渲染。 详见[实现与验证](COATING_DATA_OPERAND_2026-10-03.md)。

2026-10-03 RRET：偏振输入/电场在 Core/Rays/ComplexElectricField.cs，顺序复振幅运输在 SequentialRayTracer.Polarization.cs，RRET 高斯/谱 RMS 在 Services/PolarizationMetrics.cs，参数入口在 MeritFunction.Polarization.cs；解析与工作流参考在 PolarizationOperandTests.cs。 详见[实现与验证](POLARIZATION_RETARDANCE_2026-10-03.md)。

2026-10-03 膜层约束：新增 CMGT/CMLT/CMVA、CIGT/CILT/CIVA、CEGT/CELT/CEVA 九项受限执行；共享薄膜求解读取每层倍率与 n/k 偏移，物理膜层编辑、变量优化和 STAROPT 保存撤销已接通。**335/383 项受限执行、48 项兼容保留**（41 项已知功能、2 项定义待核实、5 项 Unused）；另 4 项本程序扩展。默认输出 Debug/Release 合并回归各 **3089/3089**（前批 3034 + 新增 54 + 既有样式检查 1），零失败、零跳过、零编译警告/错误。原生膜层字段、聚合边界数值、倍率开关/拾取仍有缺口，ZMX 行保持只读。见[膜层约束记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。下方保留历史阶段范围，不是全量发布验收。

2026-10-02 第二十九批：TSAG 指定方向矢高及延伸区数据贯通；体积、毛坯、CAD 和自动口径同步处理光学延伸边界。该阶段 **302/383 项受限执行、81 项兼容保留**（74 项已知功能、2 项定义待核实、5 项 Unused）。默认 Debug/Release 合并回归各 **2560/2560**（前批 2497 + 本批新增 39 + 扩展既有 24），无失败/跳过或编译警告/错误。原生 TSAG 列、复合倾角与非零延伸区文件映射仍待核实，详见[本批范围与证据](DIRECTIONAL_SAG_OPERAND_2026-10-02.md)。以下记录保留各自阶段范围。

2026-10-02 第二十八批：新增共享最小体积球面拟合及 BFSD 本地评价、编辑保存、优化路径。该阶段 **301/383 项受限执行、82 项兼容保留**（75 项已知功能、2 项定义待核实、5 项 Unused）。默认 Debug/Release 输出构建成功，合并回归各 **2497/2497**（含新增 26 项），零失败/跳过。固定 Zemax 球面矢高表只验证该文件的球面情况，原生 MFE 与非球面拟合仍有验证缺口；见[本批实现与证据](BEST_FIT_SPHERE_OPERAND_2026-10-02.md)。以下记录保留各自阶段范围。

2026-10-02 第二十七批：RELI/EFNO 已接通本地编辑、保存、评价函数和优化，显式复用共享像方网格及 Jones 功率链。该阶段 **300/383 项受限执行、83 项兼容保留**（76 项已知功能、2 项定义待核实、5 项 Unused）；仍有原生数值和模式缺口，两项 ZMX 导入保持只读。默认 Debug/Release 输出构建成功，合并回归各 **2471/2471**（含新增 31 项），见[本批实现与证据](ILLUMINATION_OPERANDS_2026-10-02.md)。以下记录保留各自阶段范围。

操作数帮助的新导航入口为 `OperandHelpTaxonomy`（分类）、`OperandHelpProjection`（搜索与树）及 `OperandHelpPanel`（交互）；默认 Debug/Release 定向验证各 19/19，见[层级说明](OPERAND_HELP_HIERARCHY_2026-10-01.md)。

2026-10-02 第二十六批入口：`Services/SeidelMetrics.Distortion` 和 `DistortionMetrics.WithReferenceMatrix` 接通 DIST/DISA；网格分析共用有符号向量差。298 受限执行/85 兼容保留，Debug/Release 各 2308/2308。[实现边界](ZEMAX_OPERAND_SUPPORT.md#2026-10-02-第二十六批三阶与指定矩阵畸变部分完成)。下方行数表保留原扫描日期。

2026-09-27 初始结构实验室 v19 阶段验证：修复扩大视场后失追迹仍继续推进的问题，加入有预算上限的过渡恢复；最终验收门槛不变。实验室完整 Release **260/260** 通过，默认实验室 Debug/Release 构建零警告、零错误。40/40 冻结处方同设置重算与导出回读一致，包含明确失败案例；一个固定起点恢复为完整可追迹，但宽角 10,000 次评价的最佳 RMS 仍为 377.79 μm，未达到 50 μm。正式 Core 源码未变，正式全量和外部比较本轮未重跑。见 [阶段恢复、文献复核与实测](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)。下文保留各阶段历史验证范围。

2026-09-27 商业代码审计记录（初始结构实验室为 v18 阶段）：默认正式及实验室 Release 构建均为 0 警告、0 错误；正式全量 **1346 通过 / 5 失败 / 共 1351 项**，实验室 **258/258** 通过，Zemax 比较工具 **103 通过 / 1 失败 / 共 104 项**。当前不满足发布门禁；此前全通过记录保留各自历史范围。本轮只审计并同步文档，未修复产品代码，未新建 Zemax 捕获或执行完整外部比较矩阵。问题、性能观测和复现证据见 [商业审计报告](COMMERCIAL_CODE_AUDIT_2026-09-27.md)。

2026-09-27 v18 阶段验证（本轮商业审计前）：v18 已把实体光阑标定、瞄准及保留净口径的自动镜片变径接入实验室搜索，新建实验默认启用，旧实验保留原模式。共享 Core 定向 **39/39**、实验室完整 Release **258/258** 通过；默认主程序及实验室 Debug/Release 构建零警告、零错误。完成两规格、新旧模式各 10,000 次评估的四次对照及全部 40 个保留候选的同设置重算/导出验证；不等于多种子发布验收。正式全量 **1311/1311**、此前光学定向 **315/315**、比较工具 **104/104** 保留历史范围；没有新建 Zemax 捕获或完整外部数值对照。见 [实体光阑搜索接入与实测](INITIAL_STRUCTURE_PHYSICAL_STOP_SEARCH_2026-09-27.md)。下文更早验证保留历史范围。

初始结构实验室当前入口为 `FlatStartSearchService`（v19），形式枚举及实际根数在 `DesignFormSearch`，全局操作/分支轮转在 `DesignFormScheduler`，预算预留在 `NeighborhoodBudget`，桌面入口为实验室 `MainWindow`。共享 Core 的 `Analysis/SpotMetricEvaluator.cs` 提供面积积分与同质心边缘检查，`Services/RayEnvelopeApertureSizing.cs` 提供自动尺寸；`FlatStartDesignProblem` 选择采样并组装几何/RMS 残差，`FlatStartContinuation` 分配渐进窗口。`EvaluatedOptic` 保持评价与导出的同快照一致。`BoundedTrustRegionLeastSquares` 负责受保护的 Broyden 更新与刷新，`FlatStartDesignService.Continue` 在完整光线域内保持完整目标，`FlatStartLocalSolver` 在有限连续光线域内保留 V2 联合目标，并按阶段/策略选择是否启用，`RegularizedLeastSquaresModel` 保留 QR 阻尼步。v16 入口变更见[阶段记录](INITIAL_STRUCTURE_JOINT_RESTART_2026-09-27.md)，当前 v19 恢复路径见[最新实现记录](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)。

原完整索引阅读日期：2026-09-04；2026-09-27 追加镀膜模块入口；2026-09-28 更新材料面板条目，独立 `GlassCatalogPanel` 已删除，旧工作区类型由 `WorkspaceDockFactory` 迁移到 `MaterialLibraryPanel`。下方其余旧文件行数为历史索引，不表示本轮重建了整个源码统计。

本文记录全仓库源文件索引、主要实现链路和容易混淆的边界，供后续开发定位使用。阅读方式为全部源文件枚举、类型和方法文本索引，以及关键实现与相关测试的交叉阅读。文件进入索引不等于该文件的每一行已经完成审计；本文也不构成全部数值算法正确性证明或新的测试通过基线。本轮没有运行构建、测试、外部基准捕获或 GUI 验证。

## 1. 工程边界

- 正式产品：`OptilandWorkbench.slnx`，由 Core、Application、App、正式测试和离线工具组成（2026-09-25 校正：Compatibility 项目已移除）。
- 独立初始结构实验室：`labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx`，独立构建和验收，不在正式产品运行时中。
- 桌面启动：`Program.Main → App → MainWindow → WorkbenchApplication.Create`。`MainWindow` 创建工作区、命令和服务，具体功能分布在 `Shell` 与 `Panels`。
- 实际依赖方向：`App → Application → Core`；旧 Compatibility 项目已移除，实验室通过单向引用复用 Core。契约不向 App 暴露 Core 类型。
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
| Propagation | 均匀传播、旧入口方向近似，以及独立的 Gradient 1～4 连续曲线积分；正式顺序显式光线及限定共轴系统的连续近轴矩阵已接通 |
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
- 新增可选均匀像方方向余弦采样见 [IlluminationMetrics.ImageCosine.cs](../src/OptilandWorkbench.Core/Services/IlluminationMetrics.ImageCosine.cs)：真实追迹求逆、孔径掩码和有界失败；`RelativeToAxis` 单独实现轴上归一化。默认路径与原生等价的边界见[像方网格与轴上参考](IMAGE_COSINE_ILLUMINATION_2026-10-02.md)。
- 相对照度与图像模拟共用 [IlluminationMetrics](../src/OptilandWorkbench.Core/Services/IlluminationMetrics.cs) 的内部求积、物理孔径掩码及像面投影。透明介质偏振使用 `FresnelPower` 和 `UnpolarizedPowerTransport`，诊断通过显式选项停在像面自身作用前。后续物理膜层连接见[物理镀膜追迹与保存](COHERENT_COATING_TRANSPORT_2026-10-02.md)；RELI/EFNO 的评价入口为 [MeritFunction.Illumination](../src/OptilandWorkbench.Core/Optimization/MeritFunction.Illumination.cs)。吸收入射介质及原生采样一致性仍未完成。
- 图像模拟：`ImageSimulationEngine` 组织测试图、PSF 基、空间变化卷积、畸变映射和重采样；扩展图像分析在此之上构造不同工作流。
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
- 制造模型中可直接看到基于半径/圆锥的 Sag 与尺寸检查；不能未经核对就宣称所有制造计算都复用 Core 任意自由曲面的求值。
- STEP 输出位于 Core，使用实际几何与坐标生成自适应分面实体，检查闭合、体积、方向和自相交。它不是保留解析曲面和全部光学语义的 CAD 导出。

## 9. 初始结构实验室

光学装调实验室另走主程序内独立窗口：`MainWindow.OpticalAssembly.cs` → `Laboratories/OpticalAssemblyWindow.cs` → Application 的 `OpticalAssemblyService` → 正式 Core 的 `Analysis/Assembly/AssemblyConjugates.cs`。Core 复用既有 `OpticalSurface.TraceRayValue` 和薄透镜交互模型，窗口使用 DTO；`AssemblyReticleView` 只渲染固定毫米坐标的光线计数网格。`AssemblyMeasurementMode` / Core `AssemblyProbeMode` 显式区分附加物镜与无穷远配置；无穷远模式不创建附加物镜，结果分别携带自准直仪位置、可空的附加物镜位置和共轭匹配状态。找像由共享内核残差导数外推求解。功能边界和测试见 [光学装调实验室](OPTICAL_ASSEMBLY_LAB.md)。

`FlatRootFactory → FirstOrderSeedGenerator → HybridCandidateRefiner → CandidateDiversityOrdering` 是搜索主线。根结构为精确平行平面，后续以曲率和厚度参数化形成候选。

搜索按镜片数和光阑位置形成结构族，使用确定性随机序列、差分进化及预算允许的局部细化，再进行更密集的独立验收。`LabAccepted` 表示满足实验室约束，不表示制造设计已完成。

`InitialStructureSearchService` 管理规格验证、并行种子、预算、取消、检查点和诊断。检查点恢复已完成种子；细化中断后并非恢复完整种群内部状态。

`RunDirectoryStore` 先写 staging 树，再发布不可变运行目录；加载检查候选集合、哈希和光学快照。`CandidateExportService` 写临时 STAROPT 后重新打开并比对快照，再发布用户目标文件。

实验室内存在差分进化实现，不代表正式产品 `OptimizerCatalog` 已公开同名算法。

## 10. 工具与测试如何配合

| 范围 | 代码职责 |
| --- | --- |
| LensLibraryBuilder | 离线转换、索引和库存目录处理；发布器使用替换目录与备份回滚 |
| ZemaxLibraryImporter | 将指定 ZMX 安装为原生样例及镜头库条目，协调多个文件替换 |
| GlassCatalogConverter | 转换 AGF 玻璃目录为本项目可读资源 |
| NonSequentialSamples | 生成确定性的教学场景并验证追迹结果 |
| Benchmarks | 顺序追迹、MTF、公差和 STARRDB 性能测量与有界 CI 烟测 |
| AccuracyCapture | 捕获 Workbench 当前计算数据；GUI capture 另有 App 入口 |
| zemax_parity | 外部基线捕获、完整性检查、当前结果对照和 GUI 图片报告 |
| step_validation | FreeCAD/OpenCascade 导入 STEP 并检查实体有效性与数量 |
| 同步脚本 | 离线收集公开镜头资料与安全解包，不是桌面启动依赖 |

测试按核心数值、Zemax 对照及冻结历史回归、文件格式、资源边界、事务、并行、架构、GUI 契约和工作区行为分布。部分 GUI 测试读取源码验证契约，部分通过 Avalonia Headless 操作控件；两者均不能自动等同于真实桌面的全流程人工验收。

基线完整性校验只说明捕获资料完整，不代表当前版本重算一致；数值比较与截图比较也分别有独立工具。Zemax 默认精度权威为 `123456.ZMX` 捕获基线，另有 MS-L7 评价函数 golden；必须保留文件、设置和版本限定。

CI 分开执行正式产品和实验室构建/三平台测试，并另有格式、依赖审计、性能烟测、STEP fixture 生成及第三方导入验证。

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

表内 App 源文件均相对 `src/OptilandWorkbench.App`，测试位于 `tests/OptilandWorkbench.Tests`。此表是当前补充；下方完整文件索引保留其整理阶段，新增文件以 `rg --files` 为准。验证数字按 [当前状态](CURRENT_STATUS.md) 的范围分别解释。

## 12. 文件索引

以下索引由本次工作区源码生成。行数含空行与注释；类型名称使用文本提取，方便定位，不替代 C# 语义分析。完整方法文本索引另保存在本地 `.tmp/code-reading/inventory.json`，其中可能包含主构造器或类似方法的文本。

共 486 个源码、脚本及构建流程文件，156,634 行。二进制资源、镜头数据、构建产物和参考结果不计入代码行数。

### 启动、脚本与构建

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [.github/workflows/ci.yml](../.github/workflows/ci.yml) | 399 | 入口、程序集属性、构建或辅助脚本 |
| [Convert-Zemax-Lens.cmd](../Convert-Zemax-Lens.cmd) | 4 | 入口、程序集属性、构建或辅助脚本 |
| [Directory.Build.props](../Directory.Build.props) | 9 | 入口、程序集属性、构建或辅助脚本 |
| [Directory.Build.targets](../Directory.Build.targets) | 11 | 入口、程序集属性、构建或辅助脚本 |
| [Run-Optiland.cmd](../Run-Optiland.cmd) | 66 | 入口、程序集属性、构建或辅助脚本 |
| [Run-Optiland.command](../Run-Optiland.command) | 65 | 入口、程序集属性、构建或辅助脚本 |
| [scripts/import-game-icons.ps1](../scripts/import-game-icons.ps1) | 142 | 入口、程序集属性、构建或辅助脚本 |
| [scripts/publish-cross-platform.sh](../scripts/publish-cross-platform.sh) | 29 | 入口、程序集属性、构建或辅助脚本 |

### labs/InitialStructure

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/App.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/App.cs) | 29 | App |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/CandidatePreviewControl.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/CandidatePreviewControl.cs) | 214 | CandidatePreviewControl, CandidatePreviewAutomationPeer |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/MainWindow.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/MainWindow.cs) | 717 | MainWindow, CandidateRow |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/Program.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/Program.cs) | 17 | Program |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Contracts/InitialStructureModels.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Contracts/InitialStructureModels.cs) | 253 | InitialStructureLimits, ObjectConjugateMode, CandidateStatus, SearchRunState, ConstraintSeverity, WavelengthSpecification, SearchBudget, InitialStructureSpecification, AlgorithmIdentity, ConstraintViolation, EvaluationVector, CandidateLineage, CandidateSnapshot, SearchDiagnostic, SearchRunManifest, SearchCheckpoint |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateDiversityOrdering.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateDiversityOrdering.cs) | 125 | CandidateDiversityOrdering, RankedCandidate |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateObjective.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateObjective.cs) | 67 | CandidateObjective |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateParameterization.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/CandidateParameterization.cs) | 136 | CandidateParameterization |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/ContentFingerprint.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/ContentFingerprint.cs) | 21 | ContentFingerprint |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/DeterministicRandom.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/DeterministicRandom.cs) | 35 | 入口、程序集属性、构建或辅助脚本 |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FirstOrderSeedGenerator.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FirstOrderSeedGenerator.cs) | 454 | FirstOrderSeedGenerator |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatRootFactory.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/FlatRootFactory.cs) | 144 | FlatRootFactory |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/HybridCandidateRefiner.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/HybridCandidateRefiner.cs) | 327 | CandidateRefinementResult, HybridCandidateRefiner, FamilyRefinementResult |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/InitialStructureSearchService.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/InitialStructureSearchService.cs) | 307 | SearchProgress, InitialStructureSearchService, SearchWorkItem |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/Properties/AssemblyInfo.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/Properties/AssemblyInfo.cs) | 3 | 入口、程序集属性、构建或辅助脚本 |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/SpecificationValidator.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/SpecificationValidator.cs) | 204 | InitialStructureSpecificationException, SpecificationValidator |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/CandidateExportService.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/CandidateExportService.cs) | 81 | CandidateExportService |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/RunDirectoryStore.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/RunDirectoryStore.cs) | 456 | RunDirectoryStore, MaximumLengthWriteStream |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/SearchCheckpointStore.cs](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/SearchCheckpointStore.cs) | 160 | SearchCheckpointStore |
| [labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/InitialStructureLabTests.cs](../labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/InitialStructureLabTests.cs) | 698 | InitialStructureLabTests |

### src/OptilandWorkbench.App

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [src/OptilandWorkbench.App/App.cs](../src/OptilandWorkbench.App/App.cs) | 740 | App |
| [src/OptilandWorkbench.App/BrandAssets.cs](../src/OptilandWorkbench.App/BrandAssets.cs) | 183 | BrandAssets |
| [src/OptilandWorkbench.App/CommandPaletteWindow.cs](../src/OptilandWorkbench.App/CommandPaletteWindow.cs) | 112 | CommandPaletteWindow |
| [src/OptilandWorkbench.App/Controls/AnalysisPlotControl.cs](../src/OptilandWorkbench.App/Controls/AnalysisPlotControl.cs) | 1469 | AnalysisPlotControl, PlotViewport, HoverSample |
| [src/OptilandWorkbench.App/Controls/DielectricGlassMaterial.cs](../src/OptilandWorkbench.App/Controls/DielectricGlassMaterial.cs) | 59 | DielectricGlassSample, DielectricGlassMaterial |
| [src/OptilandWorkbench.App/Controls/DrawingPreviewControl.cs](../src/OptilandWorkbench.App/Controls/DrawingPreviewControl.cs) | 217 | DrawingPreviewControl |
| [src/OptilandWorkbench.App/Controls/FoucaultPlotControl.cs](../src/OptilandWorkbench.App/Controls/FoucaultPlotControl.cs) | 158 | FoucaultPlotControl |
| [src/OptilandWorkbench.App/Controls/FullFieldAberrationControl.cs](../src/OptilandWorkbench.App/Controls/FullFieldAberrationControl.cs) | 187 | FullFieldAberrationControl |
| [src/OptilandWorkbench.App/Controls/InteractiveCanvasAccessibility.cs](../src/OptilandWorkbench.App/Controls/InteractiveCanvasAccessibility.cs) | 114 | InteractiveCanvasCommand, InteractiveCanvasKeyboard, IInteractiveCanvasAutomationSource, InteractiveCanvasAutomationPeer, InteractiveCanvasFocus |
| [src/OptilandWorkbench.App/Controls/LocalIcon.cs](../src/OptilandWorkbench.App/Controls/LocalIcon.cs) | 398 | LocalIcon, LocalIconLabel, LocalIconLibrary, IconDefinition, IThemeIconPack, StandardThemeIconPack, ThemeIconResolver, IconPrimitive, PathPrimitive, FilledPathPrimitive, LinePrimitive, EllipsePrimitive, RectanglePrimitive, PolylinePrimitive |
| [src/OptilandWorkbench.App/Controls/OperationStatusBar.cs](../src/OptilandWorkbench.App/Controls/OperationStatusBar.cs) | 165 | OperationStatusKind, OperationStatusBar |
| [src/OptilandWorkbench.App/Controls/OpticSceneControl.cs](../src/OptilandWorkbench.App/Controls/OpticSceneControl.cs) | 1956 | OpticSceneViewMode, OpticSceneRenderMode, OpticSceneRayColorMode, OpticSceneVisualStyle, OpticSceneViewPreset, OpticSceneAnnotationPlacement2D, OpticSceneAnnotation2D, OpticSceneControl, ProjectedFace, ObjectProjectedFace, GlassRenderParameters |
| [src/OptilandWorkbench.App/Controls/OptionalSquarePlotHost.cs](../src/OptilandWorkbench.App/Controls/OptionalSquarePlotHost.cs) | 86 | OptionalSquarePlotHost |
| [src/OptilandWorkbench.App/Controls/ReadOnlyChartAccessibility.cs](../src/OptilandWorkbench.App/Controls/ReadOnlyChartAccessibility.cs) | 53 | IReadOnlyChartAutomationSource, ReadOnlyChartAutomationPeer, ReadOnlyChartSummary |
| [src/OptilandWorkbench.App/Controls/ResponsiveSettingsGrid.cs](../src/OptilandWorkbench.App/Controls/ResponsiveSettingsGrid.cs) | 55 | ResponsiveSettingsGrid |
| [src/OptilandWorkbench.App/Controls/ResponsiveTwoPaneGrid.cs](../src/OptilandWorkbench.App/Controls/ResponsiveTwoPaneGrid.cs) | 70 | ResponsiveTwoPaneGrid |
| [src/OptilandWorkbench.App/Controls/SceneViewport.cs](../src/OptilandWorkbench.App/Controls/SceneViewport.cs) | 54 | SceneViewport |
| [src/OptilandWorkbench.App/Controls/SeidelDiagramControl.cs](../src/OptilandWorkbench.App/Controls/SeidelDiagramControl.cs) | 165 | SeidelDiagramControl |
| [src/OptilandWorkbench.App/Controls/SettingsPanelChrome.cs](../src/OptilandWorkbench.App/Controls/SettingsPanelChrome.cs) | 81 | SettingsPanelChrome |
| [src/OptilandWorkbench.App/Controls/SpectralColorMap.cs](../src/OptilandWorkbench.App/Controls/SpectralColorMap.cs) | 63 | SpectralColorMap, SpectralStop |
| [src/OptilandWorkbench.App/Controls/UiDensity.cs](../src/OptilandWorkbench.App/Controls/UiDensity.cs) | 9 | UiDensity |
| [src/OptilandWorkbench.App/Controls/ViewCubeIcon.cs](../src/OptilandWorkbench.App/Controls/ViewCubeIcon.cs) | 144 | ViewCubeFace, ViewCubeIcon |
| [src/OptilandWorkbench.App/Controls/WavefrontSurfaceControl.cs](../src/OptilandWorkbench.App/Controls/WavefrontSurfaceControl.cs) | 977 | WavefrontSurfaceControl, ContourSegment, SurfaceGrid, ProjectedPoint, SurfaceTriangle, SurfaceDragMode |
| [src/OptilandWorkbench.App/DisplaySettingsWindow.cs](../src/OptilandWorkbench.App/DisplaySettingsWindow.cs) | 418 | DisplaySettingsWindow, DisplaySettingsSnapshot, FontShapeOption |
| [src/OptilandWorkbench.App/MacOsBranding.cs](../src/OptilandWorkbench.App/MacOsBranding.cs) | 100 | MacOsBranding |
| [src/OptilandWorkbench.App/MainWindow.cs](../src/OptilandWorkbench.App/MainWindow.cs) | 254 | MainWindow |
| [src/OptilandWorkbench.App/Manufacturing/OpticalDrawingModel.cs](../src/OptilandWorkbench.App/Manufacturing/OpticalDrawingModel.cs) | 131 | OpticalDrawingPageSize, OpticalDrawingStandard, OpticalSystemDrawingSheet, OpticalDrawingSheet |
| [src/OptilandWorkbench.App/Manufacturing/OpticalDrawingRenderer.cs](../src/OptilandWorkbench.App/Manufacturing/OpticalDrawingRenderer.cs) | 151 | OpticalDrawingRenderer |
| [src/OptilandWorkbench.App/Manufacturing/OpticalDrawingTemplate.cs](../src/OptilandWorkbench.App/Manufacturing/OpticalDrawingTemplate.cs) | 252 | OpticalDrawingTemplate, OpticalDrawingPageTemplate, OpticalDrawingGeometryTemplate, OpticalDrawingTitleBlockTemplate, OpticalDrawingSpecificationTemplate, OpticalDrawingColumnTemplate, OpticalDrawingMaterialRowTemplate, OpticalDrawingTemplateCatalog |
| [src/OptilandWorkbench.App/Manufacturing/OpticalManufacturingModel.cs](../src/OptilandWorkbench.App/Manufacturing/OpticalManufacturingModel.cs) | 452 | ManufacturabilitySeverity, OpticalElementDefinition, OpticalDrawingElementDefinition, ManufacturabilitySettings, ManufacturabilityFinding, ManufacturabilityGeometryMetric, ManufacturabilityReport, OpticalManufacturingModel |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Dimensions.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Dimensions.cs) | 472 | OpticalDrawingRendererCore, ManufacturingProfilePoint, ManufacturingComponentProfile |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Element.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Element.cs) | 240 | OpticalDrawingRendererCore |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Specifications.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Specifications.cs) | 945 | OpticalDrawingRendererCore, OpticalDrawingFieldContext |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.System.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.System.cs) | 202 | OpticalDrawingRendererCore, SystemLensGeometry |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Text.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.Text.cs) | 136 | OpticalDrawingRendererCore |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.TitleBlock.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRenderer.TitleBlock.cs) | 207 | OpticalDrawingRendererCore |
| [src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRendererCore.State.cs](../src/OptilandWorkbench.App/Manufacturing/Rendering/OpticalDrawingRendererCore.State.cs) | 50 | OpticalDrawingRendererCore |
| [src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Export.cs](../src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Export.cs) | 161 | AnalysisPanel |
| [src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs](../src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs) | 959 | AnalysisPanel, FilePathInput |
| [src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Plots.cs](../src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Plots.cs) | 1124 | AnalysisPanel |
| [src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs](../src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs) | 1441 | AnalysisPanel, CompactAnalysisSummary |
| [src/OptilandWorkbench.App/Panels/Analysis/AnalysisSemanticColors.cs](../src/OptilandWorkbench.App/Panels/Analysis/AnalysisSemanticColors.cs) | 13 | AnalysisSemanticColors |
| [src/OptilandWorkbench.App/Panels/AnalysisPanel.cs](../src/OptilandWorkbench.App/Panels/AnalysisPanel.cs) | 438 | AnalysisPanel |
| [src/OptilandWorkbench.App/Panels/CommercialLensCatalogPanel.cs](../src/OptilandWorkbench.App/Panels/CommercialLensCatalogPanel.cs) | 649 | CommercialLensCatalogPanel |
| [src/OptilandWorkbench.App/Panels/CommercialLensCatalogProjection.cs](../src/OptilandWorkbench.App/Panels/CommercialLensCatalogProjection.cs) | 115 | CommercialLensCatalogFilter, CommercialLensCatalogFilterResult, CommercialLensCatalogProjection, CommercialLensRow |
| [src/OptilandWorkbench.App/Panels/ImageFileViewerWindow.cs](../src/OptilandWorkbench.App/Panels/ImageFileViewerWindow.cs) | 390 | ImageFileViewerWindow, ZemaxImageData, ZemaxImageFile |
| [src/OptilandWorkbench.App/Panels/LensEditorPanel.cs](../src/OptilandWorkbench.App/Panels/LensEditorPanel.cs) | 554 | LensEditorPanel |
| [src/OptilandWorkbench.App/Panels/ManufacturabilityPanel.cs](../src/OptilandWorkbench.App/Panels/ManufacturabilityPanel.cs) | 256 | ManufacturabilityPanel |
| [src/OptilandWorkbench.App/Panels/MaterialAnalysisPanel.cs](../src/OptilandWorkbench.App/Panels/MaterialAnalysisPanel.cs) | 303 | MaterialAnalysisPanel, GlassOption |
| [src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs](../src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs) | 1032 | MaterialLibraryPanel, LensLibraryPanel |
| [src/OptilandWorkbench.App/Panels/MeritOperandRowPalette.cs](../src/OptilandWorkbench.App/Panels/MeritOperandRowPalette.cs) | 129 | MeritOperandRowPalette, MeritOperandRowVisual |
| [src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs](../src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs) | 161 | MultiConfigurationPanel |
| [src/OptilandWorkbench.App/Panels/NonSequentialDetectorDisplay.cs](../src/OptilandWorkbench.App/Panels/NonSequentialDetectorDisplay.cs) | 183 | DetectorDisplayNormalization, DetectorProfileAxis, DetectorDisplayFrame, NonSequentialDetectorDisplay |
| [src/OptilandWorkbench.App/Panels/NonSequentialObjectEditorPanel.cs](../src/OptilandWorkbench.App/Panels/NonSequentialObjectEditorPanel.cs) | 587 | NonSequentialObjectEditorPanel, ObjectChoice, ObjectRow, NonSequentialModePanel |
| [src/OptilandWorkbench.App/Panels/NonSequentialStrayLightWindows.cs](../src/OptilandWorkbench.App/Panels/NonSequentialStrayLightWindows.cs) | 1027 | NonSequentialStlImportWindow, NonSequentialTraceControlWindow, SourceChoice, NonSequentialRayDatabaseWindow, PathRow, BranchRow, NonSequentialDetectorViewerPanel, DetectorChoice, WavelengthChoice |
| [src/OptilandWorkbench.App/Panels/OperandHelpPanel.cs](../src/OptilandWorkbench.App/Panels/OperandHelpPanel.cs) | 344 | OperandHelpPanel |
| [src/OptilandWorkbench.App/Panels/OperandHelpTaxonomy.cs](../src/OptilandWorkbench.App/Panels/OperandHelpTaxonomy.cs) | 本次新增 | 操作数帮助的大类、族和状态展示映射 |
| [src/OptilandWorkbench.App/Panels/OperandHelpProjection.cs](../src/OptilandWorkbench.App/Panels/OperandHelpProjection.cs) | 50 | OperandHelpSupportFilter, OperandHelpProjection |
| [src/OptilandWorkbench.App/Panels/OpticalDrawingPanel.cs](../src/OptilandWorkbench.App/Panels/OpticalDrawingPanel.cs) | 765 | OpticalDrawingPanel, ElementChoice |
| [src/OptilandWorkbench.App/Panels/OptimizationPanel.cs](../src/OptilandWorkbench.App/Panels/OptimizationPanel.cs) | 568 | OptimizationPanel |
| [src/OptilandWorkbench.App/Panels/OptimizationVariableSliderWindow.cs](../src/OptilandWorkbench.App/Panels/OptimizationVariableSliderWindow.cs) | 255 | OptimizationVariableSliderWindow, VariableChoice |
| [src/OptilandWorkbench.App/Panels/OptimizationWizardWindow.cs](../src/OptilandWorkbench.App/Panels/OptimizationWizardWindow.cs) | 419 | OptimizationWizardWindow |
| [src/OptilandWorkbench.App/Panels/StockLensMatchingPanel.cs](../src/OptilandWorkbench.App/Panels/StockLensMatchingPanel.cs) | 471 | StockLensMatchingPanel, MatchRow |
| [src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs](../src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs) | 1147 | SystemPropertiesPanel |
| [src/OptilandWorkbench.App/Panels/ToleranceChartDocumentPanel.cs](../src/OptilandWorkbench.App/Panels/ToleranceChartDocumentPanel.cs) | 281 | ToleranceChartView, ToleranceChartBuilder, ToleranceChartDocumentPanel |
| [src/OptilandWorkbench.App/Panels/ToleranceOperandEditorRow.cs](../src/OptilandWorkbench.App/Panels/ToleranceOperandEditorRow.cs) | 231 | ToleranceOperandEditorRow |
| [src/OptilandWorkbench.App/Panels/ToleranceTextDocumentPanel.cs](../src/OptilandWorkbench.App/Panels/ToleranceTextDocumentPanel.cs) | 125 | ToleranceTextDocumentPanel |
| [src/OptilandWorkbench.App/Panels/ToleranceWizardWindow.cs](../src/OptilandWorkbench.App/Panels/ToleranceWizardWindow.cs) | 623 | ToleranceWizardWindow |
| [src/OptilandWorkbench.App/Panels/TolerancingPanel.cs](../src/OptilandWorkbench.App/Panels/TolerancingPanel.cs) | 1422 | TolerancingPanel, ToleranceKindChoice, ToleranceFileDto |
| [src/OptilandWorkbench.App/Panels/TolerancingRunWindow.cs](../src/OptilandWorkbench.App/Panels/TolerancingRunWindow.cs) | 299 | TolerancingRunOptions, TolerancingRunWindow |
| [src/OptilandWorkbench.App/Panels/ViewerPanel.cs](../src/OptilandWorkbench.App/Panels/ViewerPanel.cs) | 941 | ViewerPresentationMode, ViewerPanel, SelectorItem |
| [src/OptilandWorkbench.App/Program.cs](../src/OptilandWorkbench.App/Program.cs) | 26 | Program |
| [src/OptilandWorkbench.App/Properties/AssemblyInfo.cs](../src/OptilandWorkbench.App/Properties/AssemblyInfo.cs) | 3 | 入口、程序集属性、构建或辅助脚本 |
| [src/OptilandWorkbench.App/Services/ActionManager.cs](../src/OptilandWorkbench.App/Services/ActionManager.cs) | 70 | ActionManager, ActionExecutionFailedEventArgs, AppAction |
| [src/OptilandWorkbench.App/Services/AdaptiveMdiLayout.cs](../src/OptilandWorkbench.App/Services/AdaptiveMdiLayout.cs) | 89 | AdaptiveMdiLayout, GridPlan |
| [src/OptilandWorkbench.App/Services/AppSettings.cs](../src/OptilandWorkbench.App/Services/AppSettings.cs) | 229 | AppSettings, WorkspaceLayoutState |
| [src/OptilandWorkbench.App/Services/DisplayTypography.cs](../src/OptilandWorkbench.App/Services/DisplayTypography.cs) | 137 | DisplayTypography, LocalFontSizeState |
| [src/OptilandWorkbench.App/Services/GuiAnalysisCaptureRequest.cs](../src/OptilandWorkbench.App/Services/GuiAnalysisCaptureRequest.cs) | 73 | GuiAnalysisCaptureRequest |
| [src/OptilandWorkbench.App/Services/GuiAnalysisCaptureRunner.cs](../src/OptilandWorkbench.App/Services/GuiAnalysisCaptureRunner.cs) | 292 | GuiAnalysisCaptureRunner, CaptureSettingsManifest, CaptureSettingsAnalysis, GuiCaptureRun, GuiCaptureManifest |
| [src/OptilandWorkbench.App/Services/IDisplaySettingsAware.cs](../src/OptilandWorkbench.App/Services/IDisplaySettingsAware.cs) | 6 | IDisplaySettingsAware |
| [src/OptilandWorkbench.App/Services/PanelManager.cs](../src/OptilandWorkbench.App/Services/PanelManager.cs) | 913 | WorkspacePanelId, PanelManager, FloatingWindowWorkArea, WorkspacePersistenceFailedEventArgs |
| [src/OptilandWorkbench.App/Services/StartupRequest.cs](../src/OptilandWorkbench.App/Services/StartupRequest.cs) | 31 | StartupRequest |
| [src/OptilandWorkbench.App/Services/SurfaceSelectionService.cs](../src/OptilandWorkbench.App/Services/SurfaceSelectionService.cs) | 24 | SurfaceSelectionChangedEventArgs, SurfaceSelectionService |
| [src/OptilandWorkbench.App/Services/ThemeAssetBindings.cs](../src/OptilandWorkbench.App/Services/ThemeAssetBindings.cs) | 6 | ThemeAssetBindings |
| [src/OptilandWorkbench.App/Services/ThemeLayoutResources.cs](../src/OptilandWorkbench.App/Services/ThemeLayoutResources.cs) | 27 | ThemeLayoutResources |
| [src/OptilandWorkbench.App/Services/ThemeResourceBindings.cs](../src/OptilandWorkbench.App/Services/ThemeResourceBindings.cs) | 78 | ThemeResourceBindings |
| [src/OptilandWorkbench.App/Services/WindowsStarOptFileAssociation.cs](../src/OptilandWorkbench.App/Services/WindowsStarOptFileAssociation.cs) | 165 | WindowsStarOptFileAssociation, NativeMethods, FileAssociationRegistration |
| [src/OptilandWorkbench.App/Services/WorkspaceDockFactory.cs](../src/OptilandWorkbench.App/Services/WorkspaceDockFactory.cs) | 776 | WorkspaceDockFactory |
| [src/OptilandWorkbench.App/Services/WorkspaceSessionStore.cs](../src/OptilandWorkbench.App/Services/WorkspaceSessionStore.cs) | 654 | WorkspaceDocumentTypes, WorkspaceDocumentDescriptor, WorkspaceSession, WorkspaceSessionStore, WorkspaceDockLayoutSerializer |
| [src/OptilandWorkbench.App/Services/WorkspaceViewLocator.cs](../src/OptilandWorkbench.App/Services/WorkspaceViewLocator.cs) | 70 | WorkspaceViewLocator, WorkspaceContentHost |
| [src/OptilandWorkbench.App/Shell/MainWindow.Actions.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Actions.cs) | 292 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.Documents.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Documents.cs) | 250 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.ImageViewers.cs](../src/OptilandWorkbench.App/Shell/MainWindow.ImageViewers.cs) | 68 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.Import.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Import.cs) | 54 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.Optimization.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Optimization.cs) | 86 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs) | 661 | MainWindow |
| [src/OptilandWorkbench.App/Shell/MainWindow.Workspace.cs](../src/OptilandWorkbench.App/Shell/MainWindow.Workspace.cs) | 278 | MainWindow |
| [src/OptilandWorkbench.App/SplashWindow.cs](../src/OptilandWorkbench.App/SplashWindow.cs) | 238 | SplashWindow |
| [src/OptilandWorkbench.App/Theming/IsekaiTheme.cs](../src/OptilandWorkbench.App/Theming/IsekaiTheme.cs) | 99 | IsekaiTheme |
| [src/OptilandWorkbench.App/Theming/IsekaiThemeDecorationRenderer.cs](../src/OptilandWorkbench.App/Theming/IsekaiThemeDecorationRenderer.cs) | 259 | IsekaiThemeDecorationRenderer |
| [src/OptilandWorkbench.App/Theming/IsekaiThemeIconPack.cs](../src/OptilandWorkbench.App/Theming/IsekaiThemeIconPack.cs) | 106 | IsekaiThemeIconPack, ImportedGameIcon, GameIconAttribution |
| [src/OptilandWorkbench.App/Theming/PixelTheme.cs](../src/OptilandWorkbench.App/Theming/PixelTheme.cs) | 314 | PixelTheme |
| [src/OptilandWorkbench.App/Theming/PixelThemeDecorationRenderer.cs](../src/OptilandWorkbench.App/Theming/PixelThemeDecorationRenderer.cs) | 121 | PixelThemeDecorationRenderer |
| [src/OptilandWorkbench.App/Theming/PixelThemeIconPack.cs](../src/OptilandWorkbench.App/Theming/PixelThemeIconPack.cs) | 104 | PixelThemeIconPack, ImportedPixelIcon |
| [src/OptilandWorkbench.App/Theming/ThemeApplicationService.cs](../src/OptilandWorkbench.App/Theming/ThemeApplicationService.cs) | 27 | ThemeApplicationService |
| [src/OptilandWorkbench.App/Theming/ThemeChrome.cs](../src/OptilandWorkbench.App/Theming/ThemeChrome.cs) | 231 | ThemeChromeRole, ThemeChromeStyle, ThemeChromeProfile, ThemeChromeResources, ThemeChrome, ThemeChromeLayer, IThemeDecorationRenderer, NoThemeDecorationRenderer, ThemeChromeOverlay |
| [src/OptilandWorkbench.App/Theming/ThemePalette.cs](../src/OptilandWorkbench.App/Theming/ThemePalette.cs) | 263 | ThemePalette |
| [src/OptilandWorkbench.App/Theming/ThemeRegistry.cs](../src/OptilandWorkbench.App/Theming/ThemeRegistry.cs) | 190 | ThemeDefinition, ThemeRegistry, StandardTheme |
| [src/OptilandWorkbench.App/UnsavedChangesWindow.cs](../src/OptilandWorkbench.App/UnsavedChangesWindow.cs) | 90 | UnsavedChangesChoice, UnsavedChangesGuard, UnsavedChangesWindow |
| [src/OptilandWorkbench.App/ViewModels/EditorRows.cs](../src/OptilandWorkbench.App/ViewModels/EditorRows.cs) | 411 | SurfaceEditorRow, MeritOperandEditorRow, FieldEditorRow, WavelengthEditorRow |

### src/OptilandWorkbench.Application

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [src/OptilandWorkbench.Application/Contracts/ServiceContracts.cs](../src/OptilandWorkbench.Application/Contracts/ServiceContracts.cs) | 321 | IWorkspaceEventStream, IWorkbenchModeService, INonSequentialDocumentService, INonSequentialAnalysisService, IOpticalDocumentService, IPrescriptionService, IAnalysisService, IVisualizationService, ICadExportService, IOptimizationService, ITolerancingService, IMultiConfigurationService, IMaterialCatalogService, ILensLibraryService, IWorkbenchApplication |
| [src/OptilandWorkbench.Application/Contracts/WorkspaceContracts.cs](../src/OptilandWorkbench.Application/Contracts/WorkspaceContracts.cs) | 1615 | NonSequentialObjectKind, NonSequentialSourceApertureShape, NonSequentialSurfaceSourceAngularDistribution, NonSequentialVolumeSourceAngularDistribution, NonSequentialSurfaceBehavior, NonSequentialVector3, NonSequentialTraceSettings, NonSequentialObjectParameters, SourceParameters, SourceRayParameters, SourcePointParameters, SourceRectangleParameters, SourceGaussianParameters, SourceEllipseParameters, SourceTwoAngleParameters, SourceRadialSample, SourceRadialParameters, SourceVolumeRectangleParameters, SourceVolumeEllipseParameters, SourceVolumeCylinderParameters, PlaneRectangleParameters, SphereParameters, CylinderParameters, BoxParameters, StandardLensParameters, MeshObjectParameters, DetectorRectangleParameters, OpticalWorkbenchMode, WorkbenchModeChangedEventArgs, NonSequentialObjectRowDto, NonSequentialWavelengthDto, NonSequentialDocumentDto, NonSequentialObjectUpdateDto, NonSequentialConversionResultDto, NonSequentialMeshUnit, NonSequentialMeshImportOptionsDto, NonSequentialMeshImportResultDto, NonSequentialTraceOutputMode, NonSequentialTraceCommand, NonSequentialSplittingMode, NonSequentialTraceSessionState, NonSequentialTraceRunRequestDto, NonSequentialTraceRunResultDto, NonSequentialTraceSessionDto, NonSequentialDetectorSpace, NonSequentialDetectorDataType, NonSequentialDetectorViewRequestDto, NonSequentialDetectorStatisticsDto, NonSequentialDetectorViewDto, NonSequentialRaySegmentDto, NonSequentialRayBranchDto, NonSequentialRayDatabasePageDto, NonSequentialPathSummaryDto, NonSequentialRayDatabaseDto, WorkspaceChangeCategory, WorkspaceChangedEventArgs, OpticalDocumentSnapshot, MaterialCatalogDto, MaterialCatalogImportResultDto, GlassMaterialDto, MaterialAnalysisKind, MaterialAnalysisRequestDto, LensLibraryEntryDto, LensLibraryCatalogDocument, CommercialLensEntryDto, CommercialLensCatalogDocument, StockLensMatchRequestDto, StockLensMatchResultDto, SurfaceRowDto, SurfaceComponentUpdateDto, FieldRowDto, WavelengthRowDto, SystemSettingsDto, EnvironmentSettingsDto, PrescriptionOptionsDto, AnalysisParameterKind, AnalysisParameterDescriptor, AnalysisSeriesKind, AnalysisLineStyle, AnalysisMarkerStyle, AnalysisColorMap, AnalysisAxisQuantity, AnalysisAxisUnit, AnalysisPointDto, AnalysisSeriesDto, AnalysisPlotOptionsDto, AnalysisPlotPaneDto, AnalysisPlotMetricDto, AnalysisRowDto, AnalysisTableDto, AnalysisPresentationKind, AnalysisViewDto, AnalysisRequestDto, AnalysisResultDto, AnalysisExecutionProvenanceDto, SceneDimension, VisualizationSelectorOptionDto, VisualizationOptionsDto, VisualizationRequestDto, ScenePoint2Dto, ScenePoint3Dto, SceneRayDirection2Dto, SceneRayDirection3Dto, SceneRayInteractionType, SceneRaySegmentType, SceneRaySegment2Dto, SceneRaySegment3Dto, SceneSurfaceFace3Dto, SceneSurfaceRenderRole, SceneSurface2Dto, SceneLensEdge2Dto, SceneLensElement2Dto, SceneRay2Dto, Scene2Dto, SceneSurface3Dto, SceneLensElement3Dto, SceneRay3Dto, Scene3Dto, NonSequentialLayoutResultDto, SceneDto, CadExportFormat, CadExportOptionsDto, CadExportResultDto, OptimizationResultDto, OptimizationVariableUpdateMode, OptimizationVariableUpdateResultDto, QuickFocusResultDto, MeritFunctionPreset, OptimizationImageQuality, OptimizationPupilSampling, OptimizationSpotReference, OptimizationWizardSettingsDto, MeritOperandTypeDto, MeritOperandParameterDto, MeritOperandRowDto, OptimizationVariableKind, OptimizationVariableResultDto, OptimizationRunResultDto, ToleranceOperandKind, ToleranceDistribution, ToleranceCriterion, ToleranceAnalysisMode, ToleranceInverseEndpointStatus, RadiusToleranceMode, ToleranceOperandDto, ToleranceWizardSettingsDto, ToleranceValidationResultDto, TolerancingRequestDto, TolerancingSensitivityRowDto, TolerancingTrialRowDto, TolerancingStatisticsDto, TolerancingSensitivityStatisticsDto, TolerancingInverseEndpointDto, TolerancingInverseRowDto, TolerancingResultDto, MultiConfigurationRowDto |
| [src/OptilandWorkbench.Application/Formatting/AnalysisAxisFormatting.cs](../src/OptilandWorkbench.Application/Formatting/AnalysisAxisFormatting.cs) | 206 | AnalysisAxisFormatting, AnalysisCsvFormatter |
| [src/OptilandWorkbench.Application/Formatting/NumericDisplayFormatter.cs](../src/OptilandWorkbench.Application/Formatting/NumericDisplayFormatter.cs) | 73 | NumericDisplayOptions, NumericDisplayFormatter |
| [src/OptilandWorkbench.Application/Properties/AssemblyInfo.cs](../src/OptilandWorkbench.Application/Properties/AssemblyInfo.cs) | 3 | 入口、程序集属性、构建或辅助脚本 |
| [src/OptilandWorkbench.Application/Runtime/DocumentUndoRedoManager.cs](../src/OptilandWorkbench.Application/Runtime/DocumentUndoRedoManager.cs) | 105 | DocumentUndoRedoCheckpoint, DocumentUndoRedoManager |
| [src/OptilandWorkbench.Application/Runtime/RuntimeModels.cs](../src/OptilandWorkbench.Application/Runtime/RuntimeModels.cs) | 117 | AnalysisView, AnalysisRow, AnalysisParameterKind, AnalysisParameterDescriptor, TolerancingView, TolerancingSensitivityRow, TolerancingTrialRow, TolerancingStatistics, TolerancingSensitivityStatistics, TolerancingInverseEndpoint, TolerancingInverseRow, MultiConfigurationRow |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.cs) | 992 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Helpers.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Helpers.cs) | 231 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.Parameters.cs) | 1290 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Common.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Common.cs) | 30 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Components.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Components.cs) | 466 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Configuration.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Configuration.cs) | 107 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.cs) | 234 | LoadedOpticalDocument, WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Documents.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Documents.cs) | 243 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Localization.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Localization.cs) | 480 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.NonSequential.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.NonSequential.cs) | 16 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Optimization.cs) | 284 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Prescription.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Prescription.cs) | 408 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.cs) | 305 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Helpers.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Helpers.cs) | 288 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Parallel.cs](../src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Parallel.cs) | 281 | WorkbenchRuntime |
| [src/OptilandWorkbench.Application/Services/AnalysisService.cs](../src/OptilandWorkbench.Application/Services/AnalysisService.cs) | 244 | AnalysisService |
| [src/OptilandWorkbench.Application/Services/BoundedApplicationFile.cs](../src/OptilandWorkbench.Application/Services/BoundedApplicationFile.cs) | 50 | BoundedApplicationFile |
| [src/OptilandWorkbench.Application/Services/CadExportService.cs](../src/OptilandWorkbench.Application/Services/CadExportService.cs) | 62 | CadExportService |
| [src/OptilandWorkbench.Application/Services/CommercialLensCatalogStore.cs](../src/OptilandWorkbench.Application/Services/CommercialLensCatalogStore.cs) | 149 | CommercialLensCatalogStore |
| [src/OptilandWorkbench.Application/Services/ImageFileLoader.cs](../src/OptilandWorkbench.Application/Services/ImageFileLoader.cs) | 102 | ImageFileLoader |
| [src/OptilandWorkbench.Application/Services/LensLibraryCatalogEntryFactory.cs](../src/OptilandWorkbench.Application/Services/LensLibraryCatalogEntryFactory.cs) | 243 | LensLibraryCatalogEntryFactory |
| [src/OptilandWorkbench.Application/Services/LensLibraryService.cs](../src/OptilandWorkbench.Application/Services/LensLibraryService.cs) | 274 | LensLibraryService |
| [src/OptilandWorkbench.Application/Services/Mapping/WorkbenchMapper.cs](../src/OptilandWorkbench.Application/Services/Mapping/WorkbenchMapper.cs) | 360 | WorkbenchMapper |
| [src/OptilandWorkbench.Application/Services/MaterialCatalogService.Analysis.cs](../src/OptilandWorkbench.Application/Services/MaterialCatalogService.Analysis.cs) | 572 | MaterialCatalogService |
| [src/OptilandWorkbench.Application/Services/MaterialCatalogService.cs](../src/OptilandWorkbench.Application/Services/MaterialCatalogService.cs) | 241 | MaterialCatalogService |
| [src/OptilandWorkbench.Application/Services/MeritOperandReferenceCatalog.cs](../src/OptilandWorkbench.Application/Services/MeritOperandReferenceCatalog.cs) | 207 | MeritOperandReference, MeritOperandReferenceCatalog |
| [src/OptilandWorkbench.Application/Services/MultiConfigurationService.cs](../src/OptilandWorkbench.Application/Services/MultiConfigurationService.cs) | 62 | MultiConfigurationService |
| [src/OptilandWorkbench.Application/Services/NonSequentialAnalysisService.cs](../src/OptilandWorkbench.Application/Services/NonSequentialAnalysisService.cs) | 975 | NonSequentialAnalysisService, OffsetTraceSink, FilterIndexCacheEntry, DetectorFrameCacheEntry, DatabaseCacheKey |
| [src/OptilandWorkbench.Application/Services/NonSequentialAnalysisSession.cs](../src/OptilandWorkbench.Application/Services/NonSequentialAnalysisSession.cs) | 289 | NonSequentialResultSession, Selection, NonSequentialAnalysisSession, NonSequentialLayoutSession, NonSequentialLayoutBranchSet |
| [src/OptilandWorkbench.Application/Services/NonSequentialDocumentService.cs](../src/OptilandWorkbench.Application/Services/NonSequentialDocumentService.cs) | 653 | NonSequentialDocumentService, CancellationTraceSink |
| [src/OptilandWorkbench.Application/Services/NonSequentialVisualizationBuilder.cs](../src/OptilandWorkbench.Application/Services/NonSequentialVisualizationBuilder.cs) | 373 | NonSequentialVisualizationBuilder |
| [src/OptilandWorkbench.Application/Services/OpticalDocumentService.cs](../src/OptilandWorkbench.Application/Services/OpticalDocumentService.cs) | 149 | OpticalDocumentService |
| [src/OptilandWorkbench.Application/Services/OpticContext.cs](../src/OptilandWorkbench.Application/Services/OpticContext.cs) | 78 | IOpticContext, OpticContext |
| [src/OptilandWorkbench.Application/Services/OptimizationService.cs](../src/OptilandWorkbench.Application/Services/OptimizationService.cs) | 405 | OptimizationService |
| [src/OptilandWorkbench.Application/Services/OptimizationService.Run.cs](../src/OptilandWorkbench.Application/Services/OptimizationService.Run.cs) | 269 | OptimizationService |
| [src/OptilandWorkbench.Application/Services/PrescriptionService.cs](../src/OptilandWorkbench.Application/Services/PrescriptionService.cs) | 316 | PrescriptionService |
| [src/OptilandWorkbench.Application/Services/StockLensMatching.cs](../src/OptilandWorkbench.Application/Services/StockLensMatching.cs) | 93 | StockLensCatalogPolicy, StockLensMatcher |
| [src/OptilandWorkbench.Application/Services/TolerancingService.cs](../src/OptilandWorkbench.Application/Services/TolerancingService.cs) | 499 | TolerancingService |
| [src/OptilandWorkbench.Application/Services/VisualizationService.cs](../src/OptilandWorkbench.Application/Services/VisualizationService.cs) | 201 | VisualizationService |
| [src/OptilandWorkbench.Application/Services/WorkbenchAnalysisCatalog.cs](../src/OptilandWorkbench.Application/Services/WorkbenchAnalysisCatalog.cs) | 253 | WorkbenchAnalysisDescriptor, WorkbenchAnalysisCatalog |
| [src/OptilandWorkbench.Application/Services/WorkbenchAnalysisRibbonCatalog.cs](../src/OptilandWorkbench.Application/Services/WorkbenchAnalysisRibbonCatalog.cs) | 310 | AnalysisRibbonCommandKind, AnalysisRibbonCommand, AnalysisRibbonMenu, WorkbenchAnalysisCatalog |
| [src/OptilandWorkbench.Application/Services/WorkbenchApplication.cs](../src/OptilandWorkbench.Application/Services/WorkbenchApplication.cs) | 106 | WorkbenchApplication |
| [src/OptilandWorkbench.Application/Services/WorkbenchModeService.cs](../src/OptilandWorkbench.Application/Services/WorkbenchModeService.cs) | 34 | WorkbenchModeService |
| [src/OptilandWorkbench.Application/Services/WorkbenchServiceBase.cs](../src/OptilandWorkbench.Application/Services/WorkbenchServiceBase.cs) | 66 | WorkbenchServiceBase |
| [src/OptilandWorkbench.Application/Services/WorkspaceCoordinator.cs](../src/OptilandWorkbench.Application/Services/WorkspaceCoordinator.cs) | 433 | WorkspaceCoordinator |

### src/OptilandWorkbench.Core

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [src/OptilandWorkbench.Core/Analysis/AnalysisCatalog.cs](../src/OptilandWorkbench.Core/Analysis/AnalysisCatalog.cs) | 197 | AnalysisCatalog, UnknownAnalysisException |
| [src/OptilandWorkbench.Core/Analysis/AnalysisModels.cs](../src/OptilandWorkbench.Core/Analysis/AnalysisModels.cs) | 198 | AnalysisSeriesKind, AnalysisLineStyle, AnalysisMarkerStyle, AnalysisColorMap, AnalysisAxisQuantity, AnalysisAxisUnit, AnalysisPoint, AnalysisSeries, AnalysisPlotOptions, AnalysisPlotPane, AnalysisPlotMetric, AnalysisTable, AnalysisData |
| [src/OptilandWorkbench.Core/Analysis/AnalysisResourceLimits.cs](../src/OptilandWorkbench.Core/Analysis/AnalysisResourceLimits.cs) | 212 | AnalysisResourceLimits |
| [src/OptilandWorkbench.Core/Analysis/BaseAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/BaseAnalysis.cs) | 28 | BaseAnalysis |
| [src/OptilandWorkbench.Core/Analysis/BestFitSphereEngine.cs](../src/OptilandWorkbench.Core/Analysis/BestFitSphereEngine.cs) | 33 | BestFitSphereResult, BestFitSphereEngine |
| [src/OptilandWorkbench.Core/Analysis/Diffraction/HuygensDiffractionAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Diffraction/HuygensDiffractionAnalyses.cs) | 528 | MmdftPsfAnalysis, HuygensPsfAnalysis, HuygensMtfAnalysis, DiffractionAnalysisPresentation |
| [src/OptilandWorkbench.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs) | 537 | PsfAnalysis, MtfAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Diffraction/PsfProfileAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Diffraction/PsfProfileAnalyses.cs) | 465 | FftPsfCrossSectionAnalysis, FftLineEdgeSpreadAnalysis, HuygensPsfCrossSectionAnalysis, PsfProfilePresentation |
| [src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs](../src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs) | 1678 | PsfResult, MtfResult, FftMtfDataType, DiffractionEngine |
| [src/OptilandWorkbench.Core/Analysis/Extended/ExtendedSceneImageAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Extended/ExtendedSceneImageAnalyses.cs) | 390 | GeometricImageAnalysis, GeometricBitmapImageAnalysis, LightSourceAnalysis, PartiallyCoherentImageAnalysis, ExtendedDiffractionImageAnalysis, ExtendedSceneImageSupport |
| [src/OptilandWorkbench.Core/Analysis/Extended/ImageAndPolarizationAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Extended/ImageAndPolarizationAnalyses.cs) | 206 | ImageSimulationAnalysis, JonesPupilAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/AxialAberrationAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/AxialAberrationAnalysis.cs) | 147 | AxialAberrationAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/ColorFocusShiftAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/ColorFocusShiftAnalysis.cs) | 150 | ColorFocusShiftAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/DistortionAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Fields/DistortionAnalyses.cs) | 473 | DistortionAnalysis, FieldCurvatureAndDistortionAnalysis, FieldCurvatureAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/FieldSweepAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Fields/FieldSweepAnalyses.cs) | 769 | RmsVsFieldAnalysis, RmsWavefrontVsFieldAnalysis, ZernikeVsFieldAnalysis, AngleScanMode, IncidentAngleVsImageHeightAnalysis, IncidentAngleVsHeightAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/FullFieldAberrationAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/FullFieldAberrationAnalysis.cs) | 227 | FullFieldAberrationAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/GridDistortionAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/GridDistortionAnalysis.cs) | 318 | GridDistortionAnalysis, DistortionReferenceMapping |
| [src/OptilandWorkbench.Core/Analysis/Fields/LateralColorAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/LateralColorAnalysis.cs) | 247 | LateralColorAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Fields/YYbarAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Fields/YYbarAnalysis.cs) | 166 | YYbarAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Focus/ThroughFocusAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Focus/ThroughFocusAnalyses.cs) | 528 | ThroughFocusSpotSettings, ThroughFocusAnalysis, ThroughFocusMtfAnalysis |
| [src/OptilandWorkbench.Core/Analysis/FootprintDiagramAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/FootprintDiagramAnalysis.cs) | 371 | FootprintDiagramAnalysis, SelectedField, PlotExtent |
| [src/OptilandWorkbench.Core/Analysis/GeometricMtfAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/GeometricMtfAnalysis.cs) | 275 | GeometricMtfAnalysis |
| [src/OptilandWorkbench.Core/Analysis/ImageSimulationEngine.cs](../src/OptilandWorkbench.Core/Analysis/ImageSimulationEngine.cs) | 1397 | ImageSimulationSourcePattern, ImageSimulationConfig, RgbImage, PsfBasisResult, ImageSimulationResult, ImageSimulationEngine |
| [src/OptilandWorkbench.Core/Analysis/JonesPupilEngine.cs](../src/OptilandWorkbench.Core/Analysis/JonesPupilEngine.cs) | 286 | JonesPupilSample, JonesPupilResult, JonesPupilEngine, ComplexMatrix3x3 |
| [src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs) | 1213 | MtfComputationMethod, MtfComputationSettings, MtfThroughFocusAnalysis, MtfVsFieldAnalysis, MtfMethodEvaluator |
| [src/OptilandWorkbench.Core/Analysis/NonSequentialDetectorViewerAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/NonSequentialDetectorViewerAnalysis.cs) | 121 | NonSequentialDetectorViewerAnalysis |
| [src/OptilandWorkbench.Core/Analysis/NonSequentialRayTraceAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/NonSequentialRayTraceAnalysis.cs) | 173 | NonSequentialRayTraceAnalysis |
| [src/OptilandWorkbench.Core/Analysis/RadiantIntensityAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/RadiantIntensityAnalysis.cs) | 310 | RadiantIntensityAnalysis, IntensityMap |
| [src/OptilandWorkbench.Core/Analysis/Radiometry/EncircledEnergyVariants.cs](../src/OptilandWorkbench.Core/Analysis/Radiometry/EncircledEnergyVariants.cs) | 1093 | DiffractionEncircledEnergyAnalysis, DiffractionEnergyCurve, PsfPixelEnergyGrid, Pixel, GeometricLineEdgeSpreadAnalysis, ExtendedSourceEncircledEnergyAnalysis, EnergySample, EnergyCurveSupport |
| [src/OptilandWorkbench.Core/Analysis/Radiometry/ExtendedSourceImage.cs](../src/OptilandWorkbench.Core/Analysis/Radiometry/ExtendedSourceImage.cs) | 116 | ExtendedSourceImage |
| [src/OptilandWorkbench.Core/Analysis/Radiometry/IncoherentIrradianceAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Radiometry/IncoherentIrradianceAnalysis.cs) | 249 | IncoherentIrradianceAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Radiometry/PupilAndEnergyAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Radiometry/PupilAndEnergyAnalyses.cs) | 409 | EncircledEnergyAnalysis, FieldEnergyCurve, PupilAberrationAnalysis, RayFanSample, PupilWave |
| [src/OptilandWorkbench.Core/Analysis/Rays/SpotAnalysisSupport.cs](../src/OptilandWorkbench.Core/Analysis/Rays/SpotAnalysisSupport.cs) | 361 | AnalysisFieldSample, SpotRayData, SpotWavelengthData, SpotFieldData, SpotAnalysisResult, MtfPresentation, SpotAnalysisEngine |
| [src/OptilandWorkbench.Core/Analysis/Rays/SpotAndRayAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Rays/SpotAndRayAnalyses.cs) | 612 | SpotDiagramSettings, SpotDiagramAnalysis, RayFanAnalysis, RayFanSample, RayFanWave, RayFanAberrationComponent |
| [src/OptilandWorkbench.Core/Analysis/Rays/SpotDiagramVariantAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Rays/SpotDiagramVariantAnalyses.cs) | 331 | SpotDiagramVariant, SpotDiagramVariantAnalysis |
| [src/OptilandWorkbench.Core/Analysis/RealImageFieldConversion.cs](../src/OptilandWorkbench.Core/Analysis/RealImageFieldConversion.cs) | 42 | RealImageFieldConversion |
| [src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontAnalysis.cs) | 175 | ReferenceSphereWavefrontAnalysis |
| [src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontEngine.cs](../src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontEngine.cs) | 239 | ReferenceSphereStrategy, ReferenceSphereWavefrontResult, ReferenceSphereWavefrontEngine, PreparedRay, PropagatedRay, Sphere |
| [src/OptilandWorkbench.Core/Analysis/RelativeIlluminationAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/RelativeIlluminationAnalysis.cs) | 164 | RelativeIlluminationAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Reports/CardinalAndVignettingAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Reports/CardinalAndVignettingAnalyses.cs) | 155 | CardinalPointsDataAnalysis, VignettingDiagramAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Reports/FirstOrderAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Reports/FirstOrderAnalysis.cs) | 25 | FirstOrderAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Reports/SeidelCoefficientsAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Reports/SeidelCoefficientsAnalysis.cs) | 350 | SeidelCoefficientsAnalysis, SeidelDiagramAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Reports/SystemReportAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Reports/SystemReportAnalyses.cs) | 219 | PrescriptionReportAnalysis, SystemDataReportAnalysis, ClassifiedDataReportAnalysis |
| [src/OptilandWorkbench.Core/Analysis/RmsScanAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/RmsScanAnalyses.cs) | 873 | RmsVsWavelengthAnalysis, RmsVsFocusAnalysis, RmsFieldMapAnalysis, RmsScanSupport |
| [src/OptilandWorkbench.Core/Analysis/SampledMtfAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/SampledMtfAnalysis.cs) | 350 | SampledMtfAnalysis, ContrastLossMapAnalysis, ContrastLossMap |
| [src/OptilandWorkbench.Core/Analysis/SampledMtfEngine.cs](../src/OptilandWorkbench.Core/Analysis/SampledMtfEngine.cs) | 148 | SampledMtfEngine, SampledMtfEvaluator |
| [src/OptilandWorkbench.Core/Analysis/Shared/AiryDiskSupport.cs](../src/OptilandWorkbench.Core/Analysis/Shared/AiryDiskSupport.cs) | 60 | AiryDiskSupport |
| [src/OptilandWorkbench.Core/Analysis/Shared/AnalysisTrace.cs](../src/OptilandWorkbench.Core/Analysis/Shared/AnalysisTrace.cs) | 537 | AnalysisTrace |
| [src/OptilandWorkbench.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs](../src/OptilandWorkbench.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs) | 270 | ImageSpaceCoordinateKind, ImageSpaceCoordinateDescriptor, ImageSpaceAnalysisSupport |
| [src/OptilandWorkbench.Core/Analysis/Shared/MtfDataTypeSupport.cs](../src/OptilandWorkbench.Core/Analysis/Shared/MtfDataTypeSupport.cs) | 40 | MtfDataTypeSupport |
| [src/OptilandWorkbench.Core/Analysis/Shared/QrLeastSquares.cs](../src/OptilandWorkbench.Core/Analysis/Shared/QrLeastSquares.cs) | 79 | QrLeastSquares |
| [src/OptilandWorkbench.Core/Analysis/SingleRayTraceAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/SingleRayTraceAnalysis.cs) | 659 | SingleRayTraceAnalysis, TraceDisplayRow |
| [src/OptilandWorkbench.Core/Analysis/SpotMetricEvaluator.cs](../src/OptilandWorkbench.Core/Analysis/SpotMetricEvaluator.cs) | 242 | SpotMetricSummary, FocusMetricPoint, FocusMetricSummary, AnalysisDataUnavailableException, SpotMetricEvaluator, WeightedRadius, FocusMetricEvaluator, FocusSweepResult, FocusSweepEvaluator |
| [src/OptilandWorkbench.Core/Analysis/Wavefront/FoucaultAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Wavefront/FoucaultAnalysis.cs) | 159 | FoucaultAnalysis |
| [src/OptilandWorkbench.Core/Analysis/Wavefront/OpticalPathDifferenceAnalysis.cs](../src/OptilandWorkbench.Core/Analysis/Wavefront/OpticalPathDifferenceAnalysis.cs) | 230 | OpticalPathDifferenceAnalysis, OpdWave |
| [src/OptilandWorkbench.Core/Analysis/Wavefront/WavefrontAnalyses.cs](../src/OptilandWorkbench.Core/Analysis/Wavefront/WavefrontAnalyses.cs) | 926 | ZernikeAnalysisKind, WavefrontAnalysis, ZernikeAnalysis |
| [src/OptilandWorkbench.Core/Analysis/WavefrontEngine.cs](../src/OptilandWorkbench.Core/Analysis/WavefrontEngine.cs) | 489 | WavefrontSample, WavefrontResult, WavefrontReferenceSphere, WavefrontEngine |
| [src/OptilandWorkbench.Core/Analysis/ZernikeFitEngine.cs](../src/OptilandWorkbench.Core/Analysis/ZernikeFitEngine.cs) | 377 | ZernikeCoefficient, ZernikeFitEngine |
| [src/OptilandWorkbench.Core/Apertures/IPhysicalAperture.cs](../src/OptilandWorkbench.Core/Apertures/IPhysicalAperture.cs) | 411 | IPhysicalAperture, CircularAperture, AnnularAperture, OffsetRadialAperture, RectangularAperture, EllipticalAperture, PolygonAperture, FileAperture, BooleanAperture, UnionAperture, IntersectionAperture, DifferenceAperture, PhysicalApertureBounds, PhysicalApertureBoundsCalculator |
| [src/OptilandWorkbench.Core/Apertures/SystemAperture.cs](../src/OptilandWorkbench.Core/Apertures/SystemAperture.cs) | 58 | ApertureKind, SystemAperture |
| [src/OptilandWorkbench.Core/Apodization/ApodizationModels.cs](../src/OptilandWorkbench.Core/Apodization/ApodizationModels.cs) | 215 | IApodizationModel, UniformApodization, GaussianApodization, CosineSquaredApodization, HannApodization, PolynomialApodization, SuperGaussianApodization, TukeyApodization, ApodizationValidation |
| [src/OptilandWorkbench.Core/Backend/IBatchedNumericBackend.cs](../src/OptilandWorkbench.Core/Backend/IBatchedNumericBackend.cs) | 417 | IBatchedNumericBackend, ScalarBatchedNumericBackendAdapter, BatchValidation |
| [src/OptilandWorkbench.Core/Backend/INumericBackend.cs](../src/OptilandWorkbench.Core/Backend/INumericBackend.cs) | 40 | INumericBackend |
| [src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.Batched.cs](../src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.Batched.cs) | 232 | ManagedCpuBackend |
| [src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.BatchedInteractions.cs](../src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.BatchedInteractions.cs) | 163 | ManagedCpuBackend |
| [src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.BatchedIntersections.cs](../src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.BatchedIntersections.cs) | 204 | ManagedCpuBackend |
| [src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.cs](../src/OptilandWorkbench.Core/Backend/ManagedCpuBackend.cs) | 53 | ManagedCpuBackend |
| [src/OptilandWorkbench.Core/Backend/Matrix3x3.cs](../src/OptilandWorkbench.Core/Backend/Matrix3x3.cs) | 42 | Matrix3x3 |
| [src/OptilandWorkbench.Core/Backend/NumericBackendProvider.cs](../src/OptilandWorkbench.Core/Backend/NumericBackendProvider.cs) | 48 | NumericBackendProvider |
| [src/OptilandWorkbench.Core/Backend/Vector3D.cs](../src/OptilandWorkbench.Core/Backend/Vector3D.cs) | 38 | Vector3D |
| [src/OptilandWorkbench.Core/Capabilities/OpticCapabilityPreflight.cs](../src/OptilandWorkbench.Core/Capabilities/OpticCapabilityPreflight.cs) | 122 | OpticCapabilityOperation, OpticCapabilityIssue, OpticCapabilityException, OpticCapabilityPreflight |
| [src/OptilandWorkbench.Core/Coatings/CoatingModels.cs](../src/OptilandWorkbench.Core/Coatings/CoatingModels.cs) | 130 | ICoatingModel, NoneCoatingModel, SimpleCoatingModel, ThinFilmLayer, ApproximateTransmissionRippleCoating, ThinFilmStackCoating, ApproximateTransmissionRippleDesigner, NeedleSynthesisDesigner |
| [src/OptilandWorkbench.Core/Coordinates/CoordinateSystem.cs](../src/OptilandWorkbench.Core/Coordinates/CoordinateSystem.cs) | 57 | CoordinateSystem |
| [src/OptilandWorkbench.Core/Domain/FieldCoordinates.cs](../src/OptilandWorkbench.Core/Domain/FieldCoordinates.cs) | 32 | FieldCoordinates |
| [src/OptilandWorkbench.Core/Domain/FieldDefinitionKind.cs](../src/OptilandWorkbench.Core/Domain/FieldDefinitionKind.cs) | 9 | FieldDefinitionKind |
| [src/OptilandWorkbench.Core/Domain/FieldPoint.cs](../src/OptilandWorkbench.Core/Domain/FieldPoint.cs) | 91 | FieldPoint |
| [src/OptilandWorkbench.Core/Domain/NotifyObject.cs](../src/OptilandWorkbench.Core/Domain/NotifyObject.cs) | 26 | NotifyObject |
| [src/OptilandWorkbench.Core/Domain/NumericParameterGuard.cs](../src/OptilandWorkbench.Core/Domain/NumericParameterGuard.cs) | 48 | NumericParameterGuard |
| [src/OptilandWorkbench.Core/Domain/ObjectConjugate.cs](../src/OptilandWorkbench.Core/Domain/ObjectConjugate.cs) | 17 | ObjectConjugate |
| [src/OptilandWorkbench.Core/Domain/OpticalEnvironment.cs](../src/OptilandWorkbench.Core/Domain/OpticalEnvironment.cs) | 28 | OpticalEnvironment |
| [src/OptilandWorkbench.Core/Domain/OpticalSurface.cs](../src/OptilandWorkbench.Core/Domain/OpticalSurface.cs) | 493 | OpticalSurface, SurfaceRayTraceResult, SurfaceRayTraceValueResult |
| [src/OptilandWorkbench.Core/Domain/OpticalSurface.StateTracing.cs](../src/OptilandWorkbench.Core/Domain/OpticalSurface.StateTracing.cs) | 195 | OpticalSurface, SurfaceInteractionStateContext, RayStateInteractionResult, SurfaceRayTraceStateResult |
| [src/OptilandWorkbench.Core/Domain/RayPath.cs](../src/OptilandWorkbench.Core/Domain/RayPath.cs) | 13 | RayPoint, RaySegment, RayPath, RayTraceResult |
| [src/OptilandWorkbench.Core/Domain/SurfaceGroup.cs](../src/OptilandWorkbench.Core/Domain/SurfaceGroup.cs) | 156 | SurfaceGroup |
| [src/OptilandWorkbench.Core/Domain/Wavelength.cs](../src/OptilandWorkbench.Core/Domain/Wavelength.cs) | 55 | Wavelength |
| [src/OptilandWorkbench.Core/FileIO/BoundedFile.cs](../src/OptilandWorkbench.Core/FileIO/BoundedFile.cs) | 365 | BoundedFile, MaximumLengthWriteStream |
| [src/OptilandWorkbench.Core/FileIO/CadLensMeshBuilder.cs](../src/OptilandWorkbench.Core/FileIO/CadLensMeshBuilder.cs) | 877 | CadLensMesh, CadLensMeshBuildResult, CadTriangle, CadLensMeshBuilder, SurfaceGrid, MeshAssembler, MeshIntersectionValidator, BvhNode, TriangleEntry, Bounds3, ValidatedMesh, EdgeKey, EdgeUse, VertexKey |
| [src/OptilandWorkbench.Core/FileIO/CommercialFormatIO.cs](../src/OptilandWorkbench.Core/FileIO/CommercialFormatIO.cs) | 847 | IOpticalFormatImporter, IOpticalFormatExporter, SequentialSurfaceRecord, SequentialLensDocument, OpticalFormatCatalog, SequentialLensTextImporter, SequentialLensTextExporter, ZemaxZmxImporter, ZemaxZmxImportResult, ZemaxZmxExporter, CodeVSeqImporter, CodeVSeqExporter, OsloLenImporter, OsloLenExporter, SequentialLensParser, SequentialSurfaceBuilder |
| [src/OptilandWorkbench.Core/FileIO/StepCadAssemblyWriter.cs](../src/OptilandWorkbench.Core/FileIO/StepCadAssemblyWriter.cs) | 369 | StepCadDocument, StepCadAssemblyWriter, StepModel, ProductDefinitionIds, PendingPart |
| [src/OptilandWorkbench.Core/FileIO/StepCadExporter.cs](../src/OptilandWorkbench.Core/FileIO/StepCadExporter.cs) | 34 | StepCadExportOptions, StepCadExporter |
| [src/OptilandWorkbench.Core/FileIO/ZemaxZmxReader.cs](../src/OptilandWorkbench.Core/FileIO/ZemaxZmxReader.cs) | 1695 | ZemaxZmxReader, ZemaxDocument, ZemaxSurface, ZemaxAperture, ZemaxMarginalRayHeightSolve, ZemaxWavelength, ZemaxConfigurationOperand, ParsedField, ConvertedSurface |
| [src/OptilandWorkbench.Core/Geometries/GeometryIntersectionSolver.cs](../src/OptilandWorkbench.Core/Geometries/GeometryIntersectionSolver.cs) | 335 | GeometryIntersectionSolver, Evaluation, Bracket, SearchResult |
| [src/OptilandWorkbench.Core/Geometries/GeometryModels.cs](../src/OptilandWorkbench.Core/Geometries/GeometryModels.cs) | 807 | PlaneGeometry, PlaneGratingGeometry, StandardGratingGeometry, StandardGeometry, EvenAsphereGeometry, OddAsphereGeometry, BiconicGeometry, SeparableBiconicGeometry, ToroidalGeometry, PolynomialGeometry, ChebyshevGeometry, ZernikeGeometry, ForbesQGeometry, INonComputableGeometry, OpaqueGeometryPayload, GeometryMath |
| [src/OptilandWorkbench.Core/Geometries/IGeometry.cs](../src/OptilandWorkbench.Core/Geometries/IGeometry.cs) | 65 | IntersectionStatus, IntersectionResult, IGeometry, IGratingGeometry |
| [src/OptilandWorkbench.Core/Interactions/IInteractionModel.cs](../src/OptilandWorkbench.Core/Interactions/IInteractionModel.cs) | 39 | IInteractionModel, RayInteractionKind, RealRayInteractionResult, SurfaceInteractionContext |
| [src/OptilandWorkbench.Core/Interactions/InteractionModels.cs](../src/OptilandWorkbench.Core/Interactions/InteractionModels.cs) | 360 | RefractiveReflectiveInteractionModel, ThinLensInteractionModel, DiffractiveInteractionModel, PhaseInteractionModel |
| [src/OptilandWorkbench.Core/Materials/BundledZemaxGlassCatalogDatabase.cs](../src/OptilandWorkbench.Core/Materials/BundledZemaxGlassCatalogDatabase.cs) | 32 | BundledZemaxGlassCatalogDatabase |
| [src/OptilandWorkbench.Core/Materials/CatalogGlassMaterial.cs](../src/OptilandWorkbench.Core/Materials/CatalogGlassMaterial.cs) | 232 | CatalogGlassMaterial |
| [src/OptilandWorkbench.Core/Materials/GlassCatalogDatabase.cs](../src/OptilandWorkbench.Core/Materials/GlassCatalogDatabase.cs) | 173 | GlassCatalogDatabase, GlassCatalogResource, GlassCatalogDefinition |
| [src/OptilandWorkbench.Core/Materials/IMaterial.cs](../src/OptilandWorkbench.Core/Materials/IMaterial.cs) | 293 | IMaterial, AirMaterial, ConstantIndexMaterial, CauchyMaterial, SellmeierMaterial, PolynomialDispersionMaterial, AbbeMaterial, MaterialInterpolation |
| [src/OptilandWorkbench.Core/Materials/MaterialRegistry.cs](../src/OptilandWorkbench.Core/Materials/MaterialRegistry.cs) | 216 | MaterialRegistry |
| [src/OptilandWorkbench.Core/Materials/ZemaxGlassCatalog.cs](../src/OptilandWorkbench.Core/Materials/ZemaxGlassCatalog.cs) | 612 | OpticalGlassCatalogDocument, OpticalGlassCatalogBundle, OpticalGlassDefinition, OpticalGlassTransmission, OpticalGlassStressData, ZemaxAgfCatalogReader, OptilandGlassCatalogStore, ExternalGlassCatalogDatabase |
| [src/OptilandWorkbench.Core/Multiconfig/MultiConfiguration.cs](../src/OptilandWorkbench.Core/Multiconfig/MultiConfiguration.cs) | 363 | MultiConfigurationLinkOverride, MultiConfiguration |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialDetectorReconstruction.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialDetectorReconstruction.cs) | 199 | NonSequentialDetectorReconstruction, DetectorHitWithinBranch, Accumulator |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialDocument.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialDocument.cs) | 1114 | NonSequentialObjectKind, NonSequentialSourceApertureShape, NonSequentialSurfaceSourceAngularDistribution, NonSequentialVolumeSourceAngularDistribution, NonSequentialSurfaceBehavior, NonSequentialWavelength, NonSequentialTraceSettings, NonSequentialObjectParameters, SourceParameters, SourceRayParameters, SourcePointParameters, SourceRectangleParameters, SourceGaussianParameters, SourceEllipseParameters, SourceTwoAngleParameters, SourceRadialSample, SourceRadialParameters, SourceVolumeRectangleParameters, SourceVolumeEllipseParameters, SourceVolumeCylinderParameters, PlaneRectangleParameters, SphereParameters, CylinderParameters, BoxParameters, StandardLensParameters, MeshObjectParameters, DetectorRectangleParameters, NonSequentialObjectDefinition, NonSequentialDocument |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialMesh.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialMesh.cs) | 1428 | NonSequentialMeshUnit, NonSequentialMeshTriangle, NonSequentialMeshHit, NonSequentialMeshAsset, NonSequentialMeshGeometry, NonSequentialMeshCodec, NonSequentialStlImporter, RawTriangle, Point2, VertexKey, FaceKey, EdgeKey, EdgeOccurrence, NormalizedMesh, NonSequentialTriangleBvh, Node, Bounds3 |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialPathFilter.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialPathFilter.cs) | 502 | NonSequentialPathFilterException, NonSequentialPathFilter, EvaluationContext, FilterNode, ConstantNode, AtomNode, NotNode, AndNode, OrNode, SequenceNode, Atom, PathEvent, AtomKind, Parser, NonSequentialPathSummary, NonSequentialPathAnalyzer, PathAccumulator |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialRayDatabase.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialRayDatabase.cs) | 577 | NonSequentialRayDatabaseObject, NonSequentialRayDatabaseHeader, NonSequentialRayDatabaseWriter, ChunkIndex, NonSequentialRayDatabaseReader, ChunkIndex, NonSequentialSceneHasher |
| [src/OptilandWorkbench.Core/NonSequential/NonSequentialTracingEngine.cs](../src/OptilandWorkbench.Core/NonSequential/NonSequentialTracingEngine.cs) | 1437 | NonSequentialTracePurpose, NonSequentialTraceOutputMode, NonSequentialSplittingMode, INonSequentialTraceSink, NonSequentialDocumentTraceRequest, NonSequentialRaySegment, NonSequentialRayBranch, NonSequentialDetectorFrame, NonSequentialEnergyBalance, NonSequentialDocumentTraceResult, NonSequentialDocumentTracer, RadialDirectionSampler, GeneratedRay, LocalHit, SceneHit, OpticalResult, BranchState, DetectorAccumulator, Aabb, BoundedObject, BvhNode |
| [src/OptilandWorkbench.Core/Optic.cs](../src/OptilandWorkbench.Core/Optic.cs) | 844 | Optic, OpticState |
| [src/OptilandWorkbench.Core/Optimization/MeritFunction.cs](../src/OptilandWorkbench.Core/Optimization/MeritFunction.cs) | 3137 | MeritOperandDefinition, MeritImageQuality, MeritPupilSampling, MeritSpotReference, MeritFunctionWizardSettings, MeritOperandType, MeritOperandEvaluation, MeritFunctionCatalog, EvaluationBatch, EvaluationBatchScope, RaySampleCacheKey, AberrationReferenceCacheKey, WavefrontReferenceCacheKey, OrderedMeritEvaluationContext, ThicknessMaterialFilter |
| [src/OptilandWorkbench.Core/Optimization/NumericalOptimizers.cs](../src/OptilandWorkbench.Core/Optimization/NumericalOptimizers.cs) | 1095 | DampedLeastSquaresOptimizer, LeastSquaresOptimizer, DampedLeastSquaresSearch, Jacobian, SvdFactor, MomentumGradientDescentOptimizer, GradientOptimizer, CoordinatePatternSearchOptimizer, PowellOptimizer, NelderMeadOptimizer, GreedyRandomPerturbationOptimizer, PopulationSearchOptimizer, GradientSearch, OptimizationResults |
| [src/OptilandWorkbench.Core/Optimization/OptimizationFramework.cs](../src/OptilandWorkbench.Core/Optimization/OptimizationFramework.cs) | 648 | OptimizationLimits, IOptimizationVariable, OptimizationGuards, DelegateVariable, Operand, OptimizationEvaluation, IVariableScaler, LinearScaler, UnitRangeScaler, OptimizationProblem, OptimizerResult, IOptimizer, OrthogonalDescentOptimizer, OptimizerCatalog, CompatibilityAliasOptimizer |
| [src/OptilandWorkbench.Core/Optimization/ZemaxOperandRegistry.cs](../src/OptilandWorkbench.Core/Optimization/ZemaxOperandRegistry.cs) | 545 | ZemaxOperandSupportLevel, ZemaxOperandParameterValueKind, ZemaxOperandParameterDescriptor, ZemaxOperandDescriptor, ZemaxOperandRegistry |
| [src/OptilandWorkbench.Core/Phase/PhaseProfiles.cs](../src/OptilandWorkbench.Core/Phase/PhaseProfiles.cs) | 532 | PhaseProfileLimits, IPhaseProfile, ConstantPhaseProfile, LinearGratingPhaseProfile, RadialPhaseProfile, GridPhaseProfile, PolynomialPhaseProfile, NotAKnotCubicSpline |
| [src/OptilandWorkbench.Core/Plugins/PluginSystem.cs](../src/OptilandWorkbench.Core/Plugins/PluginSystem.cs) | 137 | IOptilandPlugin, PluginRegistry, PluginLoader |
| [src/OptilandWorkbench.Core/Propagation/IPropagationModel.cs](../src/OptilandWorkbench.Core/Propagation/IPropagationModel.cs) | 109 | IPropagationModel, HomogeneousPropagationModel, EntranceDirectionApproximationPropagationModel, GrinPropagationModel |
| [src/OptilandWorkbench.Core/Properties/AssemblyInfo.cs](../src/OptilandWorkbench.Core/Properties/AssemblyInfo.cs) | 3 | 入口、程序集属性、构建或辅助脚本 |
| [src/OptilandWorkbench.Core/Rays/RayModels.cs](../src/OptilandWorkbench.Core/Rays/RayModels.cs) | 67 | RealRay, ParaxialRay, PolarizedRay, RayTraceSample, RealRayBundle |
| [src/OptilandWorkbench.Core/Rays/RayState.cs](../src/OptilandWorkbench.Core/Rays/RayState.cs) | 52 | RayState |
| [src/OptilandWorkbench.Core/Rays/RayTraceSampleValue.cs](../src/OptilandWorkbench.Core/Rays/RayTraceSampleValue.cs) | 47 | RayTraceSampleValue |
| [src/OptilandWorkbench.Core/Raytrace/ApertureSampler.cs](../src/OptilandWorkbench.Core/Raytrace/ApertureSampler.cs) | 363 | PupilSampling, PupilSample, ApertureSampler, GaussianSamplingKey, SamplingKey |
| [src/OptilandWorkbench.Core/Raytrace/NonSequentialRayTracer.cs](../src/OptilandWorkbench.Core/Raytrace/NonSequentialRayTracer.cs) | 398 | NonSequentialTerminationReason, NonSequentialTraceOptions, NonSequentialObject, NonSequentialScene, NonSequentialInteraction, NonSequentialRayPath, NonSequentialTrace, NonSequentialRayTracer, CandidateHit |
| [src/OptilandWorkbench.Core/Raytrace/PooledDirectionBatch.cs](../src/OptilandWorkbench.Core/Raytrace/PooledDirectionBatch.cs) | 102 | PooledDirectionBatch |
| [src/OptilandWorkbench.Core/Raytrace/PooledRayStateBuffer.cs](../src/OptilandWorkbench.Core/Raytrace/PooledRayStateBuffer.cs) | 135 | PooledRayStateBuffer |
| [src/OptilandWorkbench.Core/Raytrace/RayGenerator.cs](../src/OptilandWorkbench.Core/Raytrace/RayGenerator.cs) | 1282 | RayGenerationSettings, RayGenerator, FieldRayContext, RayAimingException |
| [src/OptilandWorkbench.Core/Raytrace/RayTraceCache.cs](../src/OptilandWorkbench.Core/Raytrace/RayTraceCache.cs) | 247 | RayTraceCacheStatistics, RayTraceCache, RayTraceCacheKey, RaySignature, RayTraceCacheEntry |
| [src/OptilandWorkbench.Core/Raytrace/RayTraceCacheBinding.cs](../src/OptilandWorkbench.Core/Raytrace/RayTraceCacheBinding.cs) | 73 | RayTraceCacheBinding |
| [src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.Batched.cs](../src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.Batched.cs) | 208 | SequentialRayTracer |
| [src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.BatchedSurface.cs](../src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.BatchedSurface.cs) | 409 | SequentialRayTracer, SurfaceBatchWorkspace |
| [src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.cs](../src/OptilandWorkbench.Core/Raytrace/SequentialRayTracer.cs) | 481 | SequentialTrace, SequentialRayTracer |
| [src/OptilandWorkbench.Core/Raytrace/SurfaceTraceData.cs](../src/OptilandWorkbench.Core/Raytrace/SurfaceTraceData.cs) | 43 | SurfaceTraceData, SurfaceTraceRecord |
| [src/OptilandWorkbench.Core/Raytrace/TraceRequest.cs](../src/OptilandWorkbench.Core/Raytrace/TraceRequest.cs) | 264 | TraceRetention, SequentialTraceLimits, TraceRequest, RequestedTrace, RaySampleView, SurfaceSampleView |
| [src/OptilandWorkbench.Core/Scattering/ScatteringModels.cs](../src/OptilandWorkbench.Core/Scattering/ScatteringModels.cs) | 78 | IScatteringModel, MainRayScatterLossApproximation, LambertianScatteringModel, MeanMeasuredScatterLoss, MeasuredBsdfScatteringModel |
| [src/OptilandWorkbench.Core/Serialization/ComponentSnapshot.cs](../src/OptilandWorkbench.Core/Serialization/ComponentSnapshot.cs) | 771 | ComponentSnapshot, ComponentSnapshotFactory |
| [src/OptilandWorkbench.Core/Serialization/OpticJsonStore.cs](../src/OptilandWorkbench.Core/Serialization/OpticJsonStore.cs) | 40 | OpticJsonStore |
| [src/OptilandWorkbench.Core/Serialization/OpticSnapshot.cs](../src/OptilandWorkbench.Core/Serialization/OpticSnapshot.cs) | 120 | OpticSnapshot, EnvironmentSnapshot, MeritOperandSnapshot, ApertureSnapshot, FieldPointSnapshot, WavelengthSnapshot, RadiusPickupSnapshot, SolveSettingsSnapshot, SurfaceSnapshot, CoordinateSystemSnapshot, SurfaceComponentSnapshot |
| [src/OptilandWorkbench.Core/Serialization/OpticSnapshotMigration.cs](../src/OptilandWorkbench.Core/Serialization/OpticSnapshotMigration.cs) | 164 | OpticSnapshotMigration |
| [src/OptilandWorkbench.Core/Serialization/OpticSnapshotValidator.cs](../src/OptilandWorkbench.Core/Serialization/OpticSnapshotValidator.cs) | 1226 | OpticSnapshotValidator, ComponentRole |
| [src/OptilandWorkbench.Core/Serialization/StarOptProjectStore.cs](../src/OptilandWorkbench.Core/Serialization/StarOptProjectStore.cs) | 465 | StarOptProjectDocument, StarOptProjectStore, StarOptProjectSnapshot |
| [src/OptilandWorkbench.Core/Serialization/SurfaceSnapshotCompatibility.cs](../src/OptilandWorkbench.Core/Serialization/SurfaceSnapshotCompatibility.cs) | 226 | SurfaceSnapshotCompatibility |
| [src/OptilandWorkbench.Core/Services/AutomaticSemiDiameterSolver.cs](../src/OptilandWorkbench.Core/Services/AutomaticSemiDiameterSolver.cs) | 97 | AutomaticSemiDiameterSolver |
| [src/OptilandWorkbench.Core/Services/ComputationCancellation.cs](../src/OptilandWorkbench.Core/Services/ComputationCancellation.cs) | 28 | ComputationCancellation, Scope |
| [src/OptilandWorkbench.Core/Services/ComputationParallelism.cs](../src/OptilandWorkbench.Core/Services/ComputationParallelism.cs) | 30 | ComputationParallelism, SuppressionScope |
| [src/OptilandWorkbench.Core/Services/Paraxial.cs](../src/OptilandWorkbench.Core/Services/Paraxial.cs) | 623 | ParaxialTrace, CardinalPointEstimate, Paraxial, RayMatrix |
| [src/OptilandWorkbench.Core/Services/PickupManager.cs](../src/OptilandWorkbench.Core/Services/PickupManager.cs) | 109 | RadiusPickup, PickupManager |
| [src/OptilandWorkbench.Core/Services/RealRayTracer.cs](../src/OptilandWorkbench.Core/Services/RealRayTracer.cs) | 61 | RealRayTracer |
| [src/OptilandWorkbench.Core/Services/SolveManager.cs](../src/OptilandWorkbench.Core/Services/SolveManager.cs) | 36 | SolveManager |
| [src/OptilandWorkbench.Core/Services/UndoRedoManager.cs](../src/OptilandWorkbench.Core/Services/UndoRedoManager.cs) | 76 | UndoRedoManager |
| [src/OptilandWorkbench.Core/Sources/SourceModels.cs](../src/OptilandWorkbench.Core/Sources/SourceModels.cs) | 112 | ISource, PointSource, SingleModeFiberSource |
| [src/OptilandWorkbench.Core/Tolerancing/TolerancingFramework.cs](../src/OptilandWorkbench.Core/Tolerancing/TolerancingFramework.cs) | 921 | IPerturbation, ISampledPerturbation, IRangePerturbation, IScaledRangePerturbation, DelegatePerturbation, ISampler, NormalSampler, UniformSampler, ConstantSampler, VariablePerturbation, VariableRangePerturbation, Tolerancing, SensitivityResult, InverseToleranceEndpointStatus, InverseToleranceEndpointResult, InverseSensitivityResult, SensitivityAnalysis, ToleranceEvaluation, TolerancingTrialResult, MonteCarlo |
| [src/OptilandWorkbench.Core/Visualization/VisualizationModels.cs](../src/OptilandWorkbench.Core/Visualization/VisualizationModels.cs) | 1192 | Layout2DPoint, Layout3DPoint, Layout2DDirection, Layout3DDirection, LayoutRayInteractionType, LayoutRaySegmentType, Layout2DRaySegment, Layout3DRaySegment, Layout3DSurfaceFace, Layout2DSurfaceCurve, Layout2DLensEdge, LayoutBuildOptions, Layout2DLensElement, Layout2DRayPath, Layout2DScene, Layout3DSurfacePrimitive, Layout3DLensElement, Layout3DRayPath, Layout3DScene, Layout2DBuilder, ViewerRaySpec |

### tests

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [tests/OptilandWorkbench.Tests/AccessibilityAndResponsiveLayoutTests.cs](../tests/OptilandWorkbench.Tests/AccessibilityAndResponsiveLayoutTests.cs) | 490 | AccessibilityAndResponsiveLayoutTests, LayoutLensLibraryService, EmptyMaterialCatalogService |
| [tests/OptilandWorkbench.Tests/ActualFieldSamplingTests.cs](../tests/OptilandWorkbench.Tests/ActualFieldSamplingTests.cs) | 98 | ActualFieldSamplingTests |
| [tests/OptilandWorkbench.Tests/AfocalImageSpaceAnalysisTests.cs](../tests/OptilandWorkbench.Tests/AfocalImageSpaceAnalysisTests.cs) | 241 | AfocalImageSpaceAnalysisTests |
| [tests/OptilandWorkbench.Tests/AnalysisGuiContractTests.cs](../tests/OptilandWorkbench.Tests/AnalysisGuiContractTests.cs) | 2883 | AnalysisGuiContractTests |
| [tests/OptilandWorkbench.Tests/AnalysisPresetBoundaryTests.cs](../tests/OptilandWorkbench.Tests/AnalysisPresetBoundaryTests.cs) | 44 | AnalysisPresetBoundaryTests |
| [tests/OptilandWorkbench.Tests/AppStartupTests.cs](../tests/OptilandWorkbench.Tests/AppStartupTests.cs) | 52 | AppStartupTests |
| [tests/OptilandWorkbench.Tests/ArchitectureConvergenceTests.cs](../tests/OptilandWorkbench.Tests/ArchitectureConvergenceTests.cs) | 399 | ArchitectureConvergenceTests, ClosedAperture |
| [tests/OptilandWorkbench.Tests/AxialAberrationAnalysisTests.cs](../tests/OptilandWorkbench.Tests/AxialAberrationAnalysisTests.cs) | 45 | AxialAberrationAnalysisTests |
| [tests/OptilandWorkbench.Tests/BatchedBackendKernelTests.cs](../tests/OptilandWorkbench.Tests/BatchedBackendKernelTests.cs) | 80 | BatchedBackendKernelTests |
| [tests/OptilandWorkbench.Tests/BatchedTirMediumTests.cs](../tests/OptilandWorkbench.Tests/BatchedTirMediumTests.cs) | 92 | BatchedTirMediumTests |
| [tests/OptilandWorkbench.Tests/BatchedTraceParityTests.cs](../tests/OptilandWorkbench.Tests/BatchedTraceParityTests.cs) | 119 | BatchedTraceParityTests |
| [tests/OptilandWorkbench.Tests/BrandAssetTests.cs](../tests/OptilandWorkbench.Tests/BrandAssetTests.cs) | 105 | BrandAssetTests |
| [tests/OptilandWorkbench.Tests/CadExportFreeCadFixtureTests.cs](../tests/OptilandWorkbench.Tests/CadExportFreeCadFixtureTests.cs) | 64 | CadExportFreeCadFixtureTests |
| [tests/OptilandWorkbench.Tests/CadExportGeometryCoverageTests.cs](../tests/OptilandWorkbench.Tests/CadExportGeometryCoverageTests.cs) | 133 | CadExportGeometryCoverageTests |
| [tests/OptilandWorkbench.Tests/CadExportReliabilityTests.cs](../tests/OptilandWorkbench.Tests/CadExportReliabilityTests.cs) | 216 | CadExportReliabilityTests, NonFiniteGeometry |
| [tests/OptilandWorkbench.Tests/CadExportTests.cs](../tests/OptilandWorkbench.Tests/CadExportTests.cs) | 98 | CadExportTests |
| [tests/OptilandWorkbench.Tests/ColorFocusShiftAnalysisTests.cs](../tests/OptilandWorkbench.Tests/ColorFocusShiftAnalysisTests.cs) | 48 | ColorFocusShiftAnalysisTests |
| [tests/OptilandWorkbench.Tests/CommercialLensCatalogPanelTests.cs](../tests/OptilandWorkbench.Tests/CommercialLensCatalogPanelTests.cs) | 93 | CommercialLensCatalogPanelTests |
| [tests/OptilandWorkbench.Tests/Validation/History/CookeTripletGoldenTests.cs](../tests/OptilandWorkbench.Tests/Validation/History/CookeTripletGoldenTests.cs) | 1013 | CookeTripletGoldenTests |
| [tests/OptilandWorkbench.Tests/CoreArchitectureTests.cs](../tests/OptilandWorkbench.Tests/CoreArchitectureTests.cs) | 387 | CoreArchitectureTests |
| [tests/OptilandWorkbench.Tests/DielectricGlassMaterialTests.cs](../tests/OptilandWorkbench.Tests/DielectricGlassMaterialTests.cs) | 21 | DielectricGlassMaterialTests |
| [tests/OptilandWorkbench.Tests/DocumentRevisionAndSaveTests.cs](../tests/OptilandWorkbench.Tests/DocumentRevisionAndSaveTests.cs) | 185 | DocumentRevisionAndSaveTests |
| [tests/OptilandWorkbench.Tests/EncircledEnergyVariantTests.cs](../tests/OptilandWorkbench.Tests/EncircledEnergyVariantTests.cs) | 120 | EncircledEnergyVariantTests |
| [tests/OptilandWorkbench.Tests/ExtendedImageAnalysisTests.cs](../tests/OptilandWorkbench.Tests/ExtendedImageAnalysisTests.cs) | 123 | ExtendedImageAnalysisTests |
| [tests/OptilandWorkbench.Tests/Validation/History/FieldDefinitionParityTests.cs](../tests/OptilandWorkbench.Tests/Validation/History/FieldDefinitionParityTests.cs) | 394 | FieldDefinitionParityTests |
| [tests/OptilandWorkbench.Tests/FootprintDiagramLegendTests.cs](../tests/OptilandWorkbench.Tests/FootprintDiagramLegendTests.cs) | 63 | FootprintDiagramLegendTests |
| [tests/OptilandWorkbench.Tests/FormatFuzzTests.cs](../tests/OptilandWorkbench.Tests/FormatFuzzTests.cs) | 133 | FormatFuzzTests |
| [tests/OptilandWorkbench.Tests/FoucaultAnalysisTests.cs](../tests/OptilandWorkbench.Tests/FoucaultAnalysisTests.cs) | 49 | FoucaultAnalysisTests |
| [tests/OptilandWorkbench.Tests/FullFieldAberrationAnalysisTests.cs](../tests/OptilandWorkbench.Tests/FullFieldAberrationAnalysisTests.cs) | 59 | FullFieldAberrationAnalysisTests |
| [tests/OptilandWorkbench.Tests/GeometryIntersectionResultTests.cs](../tests/OptilandWorkbench.Tests/GeometryIntersectionResultTests.cs) | 113 | GeometryIntersectionResultTests |
| [tests/OptilandWorkbench.Tests/GlassCatalogTests.cs](../tests/OptilandWorkbench.Tests/GlassCatalogTests.cs) | 349 | GlassCatalogTests |
| [tests/OptilandWorkbench.Tests/GuiAnalysisCaptureRequestTests.cs](../tests/OptilandWorkbench.Tests/GuiAnalysisCaptureRequestTests.cs) | 42 | GuiAnalysisCaptureRequestTests |
| [tests/OptilandWorkbench.Tests/HighPerformanceTracingTests.cs](../tests/OptilandWorkbench.Tests/HighPerformanceTracingTests.cs) | 288 | HighPerformanceTracingTests |
| [tests/OptilandWorkbench.Tests/HighPriorityReliabilityTests.cs](../tests/OptilandWorkbench.Tests/HighPriorityReliabilityTests.cs) | 478 | HighPriorityReliabilityTests |
| [tests/OptilandWorkbench.Tests/IncidentAngleVsImageHeightParityTests.cs](../tests/OptilandWorkbench.Tests/IncidentAngleVsImageHeightParityTests.cs) | 74 | IncidentAngleVsImageHeightParityTests |
| [tests/OptilandWorkbench.Tests/LateralColorAnalysisTests.cs](../tests/OptilandWorkbench.Tests/LateralColorAnalysisTests.cs) | 47 | LateralColorAnalysisTests |
| [tests/OptilandWorkbench.Tests/LayeringArchitectureTests.cs](../tests/OptilandWorkbench.Tests/LayeringArchitectureTests.cs) | 522 | LayeringArchitectureTests |
| [tests/OptilandWorkbench.Tests/LensEditorLayoutTests.cs](../tests/OptilandWorkbench.Tests/LensEditorLayoutTests.cs) | 49 | LensEditorLayoutTests |
| [tests/OptilandWorkbench.Tests/LensLibraryPublisherTests.cs](../tests/OptilandWorkbench.Tests/LensLibraryPublisherTests.cs) | 137 | LensLibraryPublisherTests |
| [tests/OptilandWorkbench.Tests/LensLibraryTests.cs](../tests/OptilandWorkbench.Tests/LensLibraryTests.cs) | 412 | LensLibraryTests |
| [tests/OptilandWorkbench.Tests/ManufacturingDrawingTests.cs](../tests/OptilandWorkbench.Tests/ManufacturingDrawingTests.cs) | 617 | ManufacturingDrawingTests |
| [tests/OptilandWorkbench.Tests/MaterialAnalysisTests.cs](../tests/OptilandWorkbench.Tests/MaterialAnalysisTests.cs) | 123 | MaterialAnalysisTests |
| [tests/OptilandWorkbench.Tests/MeritFunctionRmsSpotTests.cs](../tests/OptilandWorkbench.Tests/MeritFunctionRmsSpotTests.cs) | 325 | MeritFunctionRmsSpotTests |
| [tests/OptilandWorkbench.Tests/MeritOperandRowPaletteTests.cs](../tests/OptilandWorkbench.Tests/MeritOperandRowPaletteTests.cs) | 82 | MeritOperandRowPaletteTests |
| [tests/OptilandWorkbench.Tests/MtfMaximumFrequencyTests.cs](../tests/OptilandWorkbench.Tests/MtfMaximumFrequencyTests.cs) | 333 | MtfMaximumFrequencyTests |
| [tests/OptilandWorkbench.Tests/NonSequentialDocumentTests.cs](../tests/OptilandWorkbench.Tests/NonSequentialDocumentTests.cs) | 780 | NonSequentialDocumentTests |
| [tests/OptilandWorkbench.Tests/NonSequentialRayTracerTests.cs](../tests/OptilandWorkbench.Tests/NonSequentialRayTracerTests.cs) | 775 | NonSequentialRayTracerTests |
| [tests/OptilandWorkbench.Tests/NonSequentialStrayLightTests.cs](../tests/OptilandWorkbench.Tests/NonSequentialStrayLightTests.cs) | 1108 | NonSequentialStrayLightTests, StubWorkspaceEvents |
| [tests/OptilandWorkbench.Tests/NonSequentialTeachingSampleTests.cs](../tests/OptilandWorkbench.Tests/NonSequentialTeachingSampleTests.cs) | 186 | NonSequentialTeachingSampleTests, SampleManifest, SampleManifestEntry, DetectorResult, DoubleArrayComparer |
| [tests/OptilandWorkbench.Tests/OperandHelpTests.cs](../tests/OptilandWorkbench.Tests/OperandHelpTests.cs) | 141 | OperandHelpTests |
| [tests/OptilandWorkbench.Tests/OpticalPathDifferenceAnalysisTests.cs](../tests/OptilandWorkbench.Tests/OpticalPathDifferenceAnalysisTests.cs) | 54 | OpticalPathDifferenceAnalysisTests |
| [tests/OptilandWorkbench.Tests/OpticCapabilityPreflightTests.cs](../tests/OptilandWorkbench.Tests/OpticCapabilityPreflightTests.cs) | 231 | OpticCapabilityPreflightTests |
| [tests/OptilandWorkbench.Tests/OpticSnapshotValidationTests.cs](../tests/OptilandWorkbench.Tests/OpticSnapshotValidationTests.cs) | 716 | OpticSnapshotValidationTests |
| [tests/OptilandWorkbench.Tests/OptilandParityTests.cs](../tests/OptilandWorkbench.Tests/OptilandParityTests.cs) | 2029 | OptilandParityTests, TestOptilandPlugin, TestPluginAnalysis, FailingOptilandPlugin |
| [tests/OptilandWorkbench.Tests/ParallelMonteCarloConfigurationTests.cs](../tests/OptilandWorkbench.Tests/ParallelMonteCarloConfigurationTests.cs) | 44 | ParallelMonteCarloConfigurationTests |
| [tests/OptilandWorkbench.Tests/PolarizationPsfLabelTests.cs](../tests/OptilandWorkbench.Tests/PolarizationPsfLabelTests.cs) | 36 | PolarizationPsfLabelTests |
| [tests/OptilandWorkbench.Tests/Validation/History/FrozenAnalysisRegressionTests.cs](../tests/OptilandWorkbench.Tests/Validation/History/FrozenAnalysisRegressionTests.cs) | 1881 | FrozenAnalysisRegressionTests, ZeroApodization |
| [tests/OptilandWorkbench.Tests/RelativeIlluminationTests.cs](../tests/OptilandWorkbench.Tests/RelativeIlluminationTests.cs) | 134 | RelativeIlluminationTests |
| [tests/OptilandWorkbench.Tests/ReliabilityHardeningTests.cs](../tests/OptilandWorkbench.Tests/ReliabilityHardeningTests.cs) | 290 | ReliabilityHardeningTests |
| [tests/OptilandWorkbench.Tests/RmsAnalysisTests.cs](../tests/OptilandWorkbench.Tests/RmsAnalysisTests.cs) | 146 | RmsAnalysisTests |
| [tests/OptilandWorkbench.Tests/ScalarBackendAdapterTests.cs](../tests/OptilandWorkbench.Tests/ScalarBackendAdapterTests.cs) | 66 | ScalarBackendAdapterTests, DelegatingScalarBackend |
| [tests/OptilandWorkbench.Tests/SeidelCoefficientsAnalysisTests.cs](../tests/OptilandWorkbench.Tests/SeidelCoefficientsAnalysisTests.cs) | 252 | SeidelCoefficientsAnalysisTests |
| [tests/OptilandWorkbench.Tests/SingleRayTraceAnalysisTests.cs](../tests/OptilandWorkbench.Tests/SingleRayTraceAnalysisTests.cs) | 162 | SingleRayTraceAnalysisTests |
| [tests/OptilandWorkbench.Tests/SourceModelTests.cs](../tests/OptilandWorkbench.Tests/SourceModelTests.cs) | 52 | SourceModelTests |
| [tests/OptilandWorkbench.Tests/SpotDiagramFieldTests.cs](../tests/OptilandWorkbench.Tests/SpotDiagramFieldTests.cs) | 179 | SpotDiagramFieldTests |
| [tests/OptilandWorkbench.Tests/StockLensMatcherTests.cs](../tests/OptilandWorkbench.Tests/StockLensMatcherTests.cs) | 77 | StockLensMatcherTests |
| [tests/OptilandWorkbench.Tests/SurfaceComponentMappingTests.cs](../tests/OptilandWorkbench.Tests/SurfaceComponentMappingTests.cs) | 76 | SurfaceComponentMappingTests |
| [tests/OptilandWorkbench.Tests/SurfaceEditorRowTests.cs](../tests/OptilandWorkbench.Tests/SurfaceEditorRowTests.cs) | 55 | SurfaceEditorRowTests |
| [tests/OptilandWorkbench.Tests/SurfaceSelectionServiceTests.cs](../tests/OptilandWorkbench.Tests/SurfaceSelectionServiceTests.cs) | 22 | SurfaceSelectionServiceTests |
| [tests/OptilandWorkbench.Tests/ThemeResourceTests.cs](../tests/OptilandWorkbench.Tests/ThemeResourceTests.cs) | 474 | ThemeResourceTests, TestThemeIconPack |
| [tests/OptilandWorkbench.Tests/ThemeRuntimeTests.cs](../tests/OptilandWorkbench.Tests/ThemeRuntimeTests.cs) | 433 | ThemeRuntimeTests |
| [tests/OptilandWorkbench.Tests/TolerancingPanelRevisionTests.cs](../tests/OptilandWorkbench.Tests/TolerancingPanelRevisionTests.cs) | 95 | TolerancingPanelRevisionTests |
| [tests/OptilandWorkbench.Tests/TolerancingWorkflowTests.cs](../tests/OptilandWorkbench.Tests/TolerancingWorkflowTests.cs) | 747 | TolerancingWorkflowTests |
| [tests/OptilandWorkbench.Tests/TracingEdgeCaseTests.cs](../tests/OptilandWorkbench.Tests/TracingEdgeCaseTests.cs) | 168 | TracingEdgeCaseTests, ThrowingGeometry |
| [tests/OptilandWorkbench.Tests/UnsavedChangesGuardTests.cs](../tests/OptilandWorkbench.Tests/UnsavedChangesGuardTests.cs) | 66 | UnsavedChangesGuardTests |
| [tests/OptilandWorkbench.Tests/ViewerInteractionTests.cs](../tests/OptilandWorkbench.Tests/ViewerInteractionTests.cs) | 261 | ViewerInteractionTests |
| [tests/OptilandWorkbench.Tests/WavefrontMapAnalysisTests.cs](../tests/OptilandWorkbench.Tests/WavefrontMapAnalysisTests.cs) | 36 | WavefrontMapAnalysisTests |
| [tests/OptilandWorkbench.Tests/WavefrontSurfaceRenderTests.cs](../tests/OptilandWorkbench.Tests/WavefrontSurfaceRenderTests.cs) | 146 | WavefrontSurfaceRenderTests, HeadlessAvaloniaCollection, HeadlessTestApplication, SafeHeadlessUnitTestSession |
| [tests/OptilandWorkbench.Tests/WindowsFileAssociationTests.cs](../tests/OptilandWorkbench.Tests/WindowsFileAssociationTests.cs) | 76 | WindowsFileAssociationTests |
| [tests/OptilandWorkbench.Tests/WorkbenchApplicationTests.cs](../tests/OptilandWorkbench.Tests/WorkbenchApplicationTests.cs) | 1003 | WorkbenchApplicationTests |
| [tests/OptilandWorkbench.Tests/WorkspaceDockModelTests.cs](../tests/OptilandWorkbench.Tests/WorkspaceDockModelTests.cs) | 773 | WorkspaceDockModelTests, TestHostWindow |
| [tests/OptilandWorkbench.Tests/WorkspaceSessionTests.cs](../tests/OptilandWorkbench.Tests/WorkspaceSessionTests.cs) | 193 | WorkspaceSessionTests |
| [tests/OptilandWorkbench.Tests/ZemaxContrastLossParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxContrastLossParityTests.cs) | 70 | ZemaxContrastLossParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxEncircledEnergyParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxEncircledEnergyParityTests.cs) | 208 | ZemaxEncircledEnergyParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxHuygensMtfParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxHuygensMtfParityTests.cs) | 109 | ZemaxHuygensMtfParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxHuygensMtfVsFieldParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxHuygensMtfVsFieldParityTests.cs) | 100 | ZemaxHuygensMtfVsFieldParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxHuygensPsfCrossSectionParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxHuygensPsfCrossSectionParityTests.cs) | 57 | ZemaxHuygensPsfCrossSectionParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxHuygensPsfParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxHuygensPsfParityTests.cs) | 61 | ZemaxHuygensPsfParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxHuygensThroughFocusMtfParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxHuygensThroughFocusMtfParityTests.cs) | 56 | ZemaxHuygensThroughFocusMtfParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxImportTests.cs](../tests/OptilandWorkbench.Tests/ZemaxImportTests.cs) | 2701 | ZemaxImportTests |
| [tests/OptilandWorkbench.Tests/ZemaxLateralColorParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxLateralColorParityTests.cs) | 51 | ZemaxLateralColorParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxLibraryImporterTests.cs](../tests/OptilandWorkbench.Tests/ZemaxLibraryImporterTests.cs) | 190 | ZemaxLibraryImporterTests |
| [tests/OptilandWorkbench.Tests/ZemaxOpticalPathDifferenceParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxOpticalPathDifferenceParityTests.cs) | 54 | ZemaxOpticalPathDifferenceParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxPupilAberrationParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxPupilAberrationParityTests.cs) | 47 | ZemaxPupilAberrationParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxRelativeIlluminationParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxRelativeIlluminationParityTests.cs) | 68 | ZemaxRelativeIlluminationParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxRmsWavefrontVsFieldParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxRmsWavefrontVsFieldParityTests.cs) | 139 | ZemaxRmsWavefrontVsFieldParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxRmsWavefrontVsFocusParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxRmsWavefrontVsFocusParityTests.cs) | 68 | ZemaxRmsWavefrontVsFocusParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxWavefrontMapParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxWavefrontMapParityTests.cs) | 66 | ZemaxWavefrontMapParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxYYbarParityTests.cs](../tests/OptilandWorkbench.Tests/ZemaxYYbarParityTests.cs) | 67 | ZemaxYYbarParityTests |
| [tests/OptilandWorkbench.Tests/ZemaxZernikeFringeTests.cs](../tests/OptilandWorkbench.Tests/ZemaxZernikeFringeTests.cs) | 98 | ZemaxZernikeFringeTests |

### tools

| 文件 | 行数 | 类型或脚本函数 |
| --- | ---: | --- |
| [tools/Import-Public-ZemaxCorpus.ps1](../tools/Import-Public-ZemaxCorpus.ps1) | 207 | Get-SafeName |
| [tools/OptilandWorkbench.AccuracyCapture/Program.cs](../tools/OptilandWorkbench.AccuracyCapture/Program.cs) | 208 | SettingsManifest, SettingsAnalysis, AnalysisRun, CurrentManifest |
| [tools/OptilandWorkbench.Benchmarks/Program.cs](../tools/OptilandWorkbench.Benchmarks/Program.cs) | 345 | 入口、程序集属性、构建或辅助脚本 |
| [tools/OptilandWorkbench.GlassCatalogConverter/Program.cs](../tools/OptilandWorkbench.GlassCatalogConverter/Program.cs) | 88 | 入口、程序集属性、构建或辅助脚本 |
| [tools/OptilandWorkbench.LensLibraryBuilder/LensLibraryPublisher.cs](../tools/OptilandWorkbench.LensLibraryBuilder/LensLibraryPublisher.cs) | 184 | LensLibraryPublishPhase, LensLibraryPublisher |
| [tools/OptilandWorkbench.LensLibraryBuilder/Program.cs](../tools/OptilandWorkbench.LensLibraryBuilder/Program.cs) | 341 | LensLibraryBuildManifest, LensLibraryBuildSource |
| [tools/OptilandWorkbench.LensLibraryBuilder/Properties/AssemblyInfo.cs](../tools/OptilandWorkbench.LensLibraryBuilder/Properties/AssemblyInfo.cs) | 3 | 入口、程序集属性、构建或辅助脚本 |
| [tools/OptilandWorkbench.LensLibraryBuilder/StockLensCatalogConverter.cs](../tools/OptilandWorkbench.LensLibraryBuilder/StockLensCatalogConverter.cs) | 271 | StockLensCatalogConverter |
| [tools/OptilandWorkbench.NonSequentialSamples/Program.cs](../tools/OptilandWorkbench.NonSequentialSamples/Program.cs) | 833 | TeachingSample, SampleManifest, SampleManifestEntry, DetectorResult |
| [tools/OptilandWorkbench.ZemaxLibraryImporter/Program.cs](../tools/OptilandWorkbench.ZemaxLibraryImporter/Program.cs) | 234 | ParsedArguments |
| [tools/OptilandWorkbench.ZemaxLibraryImporter/ZemaxLibraryInstaller.cs](../tools/OptilandWorkbench.ZemaxLibraryImporter/ZemaxLibraryInstaller.cs) | 362 | ZemaxLibraryInstallOptions, ZemaxLibraryInstallResult, ZemaxLibraryInstaller, PreparedFile |
| [tools/round_brand_icon.py](../tools/round_brand_icon.py) | 60 | rounded_icon, main |
| [tools/step_validation/validate_freecad_import.py](../tools/step_validation/validate_freecad_import.py) | 91 | solid_key, validate, main |
| [tools/Sync-DanReileyLensExchange.ps1](../tools/Sync-DanReileyLensExchange.ps1) | 350 | Get-SafePathSegment, Get-RelativePath, Test-DownloadedContent |
| [tools/Sync-Public-ZemaxCorpus.ps1](../tools/Sync-Public-ZemaxCorpus.ps1) | 377 | Test-OpenLicense, Get-SafePathSegment, Get-RelativePath, Get-Json, Save-PublicFile, Expand-ZipSafely |
| [tools/zemax_parity/generate_gui_image_report.py](../tools/zemax_parity/generate_gui_image_report.py) | 216 | _relative, _zemax_screenshot, _reference_kind, build_report, _image_figure, render_html, render_markdown, main |
| [tools/zemax_parity/generate_workbench_comparison.py](../tools/zemax_parity/generate_workbench_comparison.py) | 1287 | configure_fonts, stable_curve_details, numeric_mapping_exclusion, read_json, slug, finite_array, current_series, zemax_series, select_named, interpolate_parameter, curve_value_scales, nrmse, compare_curves, current_grids, series_grid, zemax_centered_wavefront_grids, zemax_grids, resize_grid, orientations, compare_grids, classification, render_numeric, is_five_field_two_direction_layout, pane_grid_shape, five_field_position, apply_axis_options, render_series_axis, render_pane_placeholder, render_current_plot_panes, render_current_page, fit_image, compose_screenshot, format_percent, main |
| [tools/zemax_parity/tests/test_generate_gui_image_report.py](../tools/zemax_parity/tests/test_generate_gui_image_report.py) | 57 | GuiImageReportTests |
| [tools/zemax_parity/tests/test_generate_workbench_comparison.py](../tools/zemax_parity/tests/test_generate_workbench_comparison.py) | 303 | NumericMappingSemanticsTests |
| [tools/zemax_parity/verify_baseline.py](../tools/zemax_parity/verify_baseline.py) | 101 | read_json, parse_args, main |
| [tools/zemax_parity/zosapi_capture_baseline.py](../tools/zemax_parity/zosapi_capture_baseline.py) | 906 | utc_now, sha256, slug, finite, vector, matrix, simple_object, serialize_results, write_json, render_fallback, wait_for_analysis, capture_data, capture_zpl_screenshots, parse_args, main |
| [tools/zemax_parity/zosapi_export.py](../tools/zemax_parity/zosapi_export.py) | 396 | load_zosapi, ensure_no_existing_instance, net_vector, net_matrix_column, read_fft_mtf_series, export_reference_rays, export_fft_mtf, parse_args, main |
| [tools/zemax_parity/zosapi_merit_control_probe.py](../tools/zemax_parity/zosapi_merit_control_probe.py) | 97 | configure_operand, add_operand, main |
| [tools/zemax_parity/zosapi_merit_function_export.py](../tools/zemax_parity/zosapi_merit_function_export.py) | 175 | sha256, enum_name, required_property, integer_cell, double_cell, finite_number, read_row, export_merit_function, main |
| [tools/zemax_parity/zosapi_through_focus_export.py](../tools/zemax_parity/zosapi_through_focus_export.py) | 676 | enum_name, read_series, export_wavefront_samples, export_single_ray_history, export_defocused_wavefront_samples, export_mtf_operand_samples, export_reference, main |

## 13. 工程配置与声明式模板

以下补充工程引用、实验室构建规则及图纸模板。除第 12 节已列出的根构建规则外，不并入上述源码行数。

| 文件 | 作用或项目引用 |
| --- | --- |
| [Directory.Build.props](../Directory.Build.props) | MSBuild 构建规则 |
| [Directory.Build.targets](../Directory.Build.targets) | MSBuild 构建规则 |
| [labs/InitialStructure/Directory.Build.props](../labs/InitialStructure/Directory.Build.props) | MSBuild 构建规则 |
| [labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx](../labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx) | 解决方案项目集合 |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/OptilandWorkbench.InitialStructure.App.csproj](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.App/OptilandWorkbench.InitialStructure.App.csproj) | OptilandWorkbench.InitialStructure.Contracts, OptilandWorkbench.InitialStructure.Engine, OptilandWorkbench.InitialStructure.Persistence, OptilandWorkbench.Core |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Contracts/OptilandWorkbench.InitialStructure.Contracts.csproj](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Contracts/OptilandWorkbench.InitialStructure.Contracts.csproj) | OptilandWorkbench.Core |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/OptilandWorkbench.InitialStructure.Engine.csproj](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Engine/OptilandWorkbench.InitialStructure.Engine.csproj) | OptilandWorkbench.InitialStructure.Contracts, OptilandWorkbench.Core |
| [labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/OptilandWorkbench.InitialStructure.Persistence.csproj](../labs/InitialStructure/src/OptilandWorkbench.InitialStructure.Persistence/OptilandWorkbench.InitialStructure.Persistence.csproj) | OptilandWorkbench.InitialStructure.Contracts, OptilandWorkbench.Core |
| [labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj](../labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj) | OptilandWorkbench.InitialStructure.Contracts, OptilandWorkbench.InitialStructure.Engine, OptilandWorkbench.InitialStructure.Persistence |
| [OptilandWorkbench.slnx](../OptilandWorkbench.slnx) | 解决方案项目集合 |
| [src/OptilandWorkbench.App/Assets/DrawingTemplates/gb-13323-1991.xml](../src/OptilandWorkbench.App/Assets/DrawingTemplates/gb-13323-1991.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OptilandWorkbench.App/Assets/DrawingTemplates/gb-13323-2009.xml](../src/OptilandWorkbench.App/Assets/DrawingTemplates/gb-13323-2009.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OptilandWorkbench.App/Assets/DrawingTemplates/iso-10110.xml](../src/OptilandWorkbench.App/Assets/DrawingTemplates/iso-10110.xml) | 内置图纸布局、规格与字段绑定 |
| [src/OptilandWorkbench.App/OptilandWorkbench.App.csproj](../src/OptilandWorkbench.App/OptilandWorkbench.App.csproj) | OptilandWorkbench.Application |
| [src/OptilandWorkbench.Application/OptilandWorkbench.Application.csproj](../src/OptilandWorkbench.Application/OptilandWorkbench.Application.csproj) | OptilandWorkbench.Core |
| [src/OptilandWorkbench.Core/OptilandWorkbench.Core.csproj](../src/OptilandWorkbench.Core/OptilandWorkbench.Core.csproj) | 目标框架、资源或包引用 |
| [tools/OptilandWorkbench.AccuracyCapture/OptilandWorkbench.AccuracyCapture.csproj](../tools/OptilandWorkbench.AccuracyCapture/OptilandWorkbench.AccuracyCapture.csproj) | OptilandWorkbench.Application, OptilandWorkbench.Core |
| [tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj](../tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj) | OptilandWorkbench.Core |
| [tools/OptilandWorkbench.GlassCatalogConverter/OptilandWorkbench.GlassCatalogConverter.csproj](../tools/OptilandWorkbench.GlassCatalogConverter/OptilandWorkbench.GlassCatalogConverter.csproj) | OptilandWorkbench.Core |
| [tools/OptilandWorkbench.LensLibraryBuilder/OptilandWorkbench.LensLibraryBuilder.csproj](../tools/OptilandWorkbench.LensLibraryBuilder/OptilandWorkbench.LensLibraryBuilder.csproj) | OptilandWorkbench.Application, OptilandWorkbench.Core |
| [tools/OptilandWorkbench.NonSequentialSamples/OptilandWorkbench.NonSequentialSamples.csproj](../tools/OptilandWorkbench.NonSequentialSamples/OptilandWorkbench.NonSequentialSamples.csproj) | OptilandWorkbench.Core |
| [tools/OptilandWorkbench.ZemaxLibraryImporter/OptilandWorkbench.ZemaxLibraryImporter.csproj](../tools/OptilandWorkbench.ZemaxLibraryImporter/OptilandWorkbench.ZemaxLibraryImporter.csproj) | OptilandWorkbench.Application, OptilandWorkbench.Core |
| [global.json](../global.json) | SDK 10.0.300，latestPatch，禁止预览 SDK |
| [packaging/macos/Info.plist](../packaging/macos/Info.plist) | macOS 应用包身份及 STAROPT 文档关联 |

## 镀膜实验室代码入口（2026-09-27）

独立解决方案为 [CoatingDesign.slnx](../labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx)，显式包含共享 Core/Application 以保持 Debug/Release 配置一致。正式 App 不引用实验室程序集；上游归档不参与产品运行。

| 职责 | 入口 |
| --- | --- |
| 真实 n/k、S/P、稳定散射递推与对数透射 | [CoherentThinFilmSolver.cs](../src/OptilandWorkbench.Core/Coatings/CoherentThinFilmSolver.cs) |
| 采样、峰值/FWHM 与类型化光谱 | [ThinFilmSpectrum.cs](../src/OptilandWorkbench.Core/Coatings/ThinFilmSpectrum.cs) |
| 目标、膜层、完整材料快照、输入哈希 | [Experiment.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/Experiment.cs) |
| 材料搜索与共享模型接入 | [MaterialLibrary.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/MaterialLibrary.cs) |
| 模板、目标残差、共享优化器、公差和密集验收 | [DesignService.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/DesignService.cs) |
| 取消与过期结果隔离 | [ExperimentSession.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/ExperimentSession.cs) |
| 原子保存、重开复算、精确 CSV 导出 | [ExperimentStore.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/ExperimentStore.cs) |
| 中文界面与光谱 | [MainWindow.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App/MainWindow.cs)、[SpectrumPlot.cs](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App/SpectrumPlot.cs) |
| 主程序独立进程启动 | [CoatingDesignLabLauncher.cs](../src/OptilandWorkbench.App/Services/CoatingDesignLabLauncher.cs) |
| 数值/工作流/控件测试 | [测试目录](../labs/CoatingDesign/tests/OptilandWorkbench.CoatingDesign.Tests) |

源归档、上游对照脚本和物理验收相互独立；完整依据见[实施文档](COATING_DESIGN_LAB.md)与[验收记录](../validation/coating/README.md)。

2026-09-28 镀膜入口补充：[结构搜索](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/DesignService.Search.cs)、[显式单位 n/k 表交换](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.Engine/MaterialTable.cs)、[材料对话框](../labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App/MaterialEditorWindow.cs)。ThemeRegistry 等显示源码在实验室 csproj 链接，主程序不新增实验室程序集引用。


指定频率 MTF 制造公差：FFT/几何 MTF、逐视场极限/增量反求和联合良率、有界间隔/单表面偏心/倾斜补偿、startol v3（兼容 v1/v2）已实现；未认证原生 Zemax 公差数值。见 [使用、边界与验证](MTF_TOLERANCING_2026-10-04.md)。
