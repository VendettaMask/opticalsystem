# 商业软件质量与性能审计（2026-09-27）

后续桌面 UI 的 70/70 定向回归和 15/15 截图回归只覆盖其列明范围，没有重跑本报告的正式全量或比较工具测试，不能据此关闭下列审计问题。各条验证链见 [当前状态](CURRENT_STATUS.md)。


后续实验室 v19 修复已完成 **260/260** 回归，见 [阶段恢复记录](INITIAL_STRUCTURE_PROGRESSIVE_RECOVERY_2026-09-27.md)。以下实验室 258/258 是商业审计时的 v18 结果；本次实验室修复不覆盖本报告的正式产品失败项。

## 结论与范围

当前工作区不满足发布验收条件。正式 Release 构建通过，但正式全量测试为 **1346 通过、5 失败、共 1351 项**；实验室 **258/258** 通过；Zemax 比较工具 **103 通过、1 失败、共 104 项**。此外，本轮复现了既有测试没有覆盖的缓存失效、文件切换竞争和有损保存问题。报告记录 **14 项问题及 14 项优化方向**，问题修复优先于扩大并行度或加入新计算后端。

本轮进行了全仓结构与风险模式扫描，并深入审阅 Core 追迹/衍射/图像仿真、Application 文档/优化/分析/公差、App 编辑器/图表/会话，以及实验室搜索/检查点链路。扫描对象包括 669 个 C# 源码/项目文件，`src` 与 `labs` 下 C# 文件合计约 12.6 万行。**这是风险导向审计，不是逐行完备证明，也不能宣称已经找出所有缺陷。** 下文区分可复现缺陷、静态确认的缺陷路径和仍需端到端剖析的提速建议。

审阅包含开始时已有的未提交主题、工具栏和数值显示修改。本轮没有修改产品或实验室实现，没有放宽断言，也没有修复下列缺陷；只增加审计说明和隔离复现程序。现有材料行持续蓝底要求应保持，优化不得破坏选中、悬停、焦点、禁用与验证状态。

优先级：P1 为可能导致错误结果、状态覆盖、数据丢失或长时间无法操作的问题；P2 为接口可靠性、契约、资源边界和发布门禁问题。未发现并确认需要归类为 P0 的问题。

位置中的 `Core/`、`Application/`、`App/` 分别简写 `src/OptilandWorkbench.Core/`、`src/OptilandWorkbench.Application/`、`src/OptilandWorkbench.App/`；只给文件名的性能条目可用 `rg --files` 定位。行号对应本轮工作区。

## 验证记录

环境：macOS ARM64；.NET SDK 10.0.300、Runtime 10.0.8。使用仓库默认 Release 输出目录，没有通过替代输出目录规避桌面构建。

仓库 HEAD 为 `c69a1106db12c86a8b80836a1e588eeba6a678a6`，但结果针对包含既有未提交修改的工作区，不代表该提交单独的状态。源文件/项目哈希清单保存在 `artifacts/code-audit/2026-09-27/source-sha256.json`。文档同步后 `git diff --check` 通过；25 处原“当前验证”摘要已补充本轮结果，原 v18 记录明确标为阶段历史。

| 项目 | 本轮结果 | 证据/边界 |
|---|---|---|
| 正式解决方案 Release build | 0 警告、0 错误 | `artifacts/code-audit/2026-09-27/product-build.log` |
| 正式完整 Release tests | 1346 通过 / 5 失败 / 1351 总数 | `product-tests.log`、`product.trx`；首次受限沙箱测试因回环套接字被拒绝而中止，允许本机通信后完成重跑 |
| 实验室 Release build | 0 警告、0 错误 | `lab-build.log` |
| 实验室完整 Release tests | 258 通过 / 0 失败 / 258 总数 | `lab-tests.log`、`lab.trx`；约 7 分 56 秒 |
| Zemax 比较工具离线 tests | 103 通过 / 1 失败 / 104 总数 | `comparison-tests.log`、`comparison.trx`；不启动 OpticStudio；失败项定向复跑仍失败，见 F14 |
| 隔离缺陷复现与微基准 | 见下文 | `artifacts/code-audit/2026-09-27/probe/`、`probe.log`；不属于正式测试数量 |
| 现有 Release 性能基准 | 完成 10,000 / 100,000 光线及 100 次 Monte Carlo 场景 | `benchmark.log`；单次观测，不建立统计性能基线 |
| Zemax 主基准源文件完整性 | SHA-256 与 manifest 一致 | `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`；只校验 `source/123456.ZMX`，未全量校验捕获目录 |
| 当前 Workbench 重算 | 正式回归、比较工具夹具测试及隔离复现中的计算 | 未另行执行完整分析比较矩阵 |
| 本轮数值比较 | 离线重算与已提交 Zemax 夹具对比 | Huygens PSF 误差预算失败；未新建捕获、启动 OpticStudio 或运行完整 72 项比较矩阵；不将其当作新的全面外部精度认证 |
| 原生 GUI / 截图验收 | 未执行 | 部分正式测试使用 Headless；不等于原生交互验收 |

未执行在线依赖漏洞审计、Windows/Linux 原生桌面安装验收、安装包签名/公证验证、全仓格式验证、长时间内存浸泡或崩溃恢复测试。已有 CI 包含跨平台测试、锁文件还原及漏洞审计作业，不能把“本轮未执行”写成“项目没有”。本轮失败结果不建立新的全通过基线；历史 1311/1311 等记录仍只代表其原先提交/工作区和范围。

本轮正式测试的五个失败：

1. `LayeringArchitectureTests.AppUiCardsUseSharedChromeTokens`：两个新主题文件直接写入 `new CornerRadius(6)`。
2. `TolerancingWorkflowTests.ImageSpacingCompensatorChangesRealOpticalCriterion`：显示值不能体现补偿改善。
3. `TolerancingWorkflowTests.MonteCarloRowsAndStatisticsUseActualCriterion`：差值一致性误差 `8.666000000000009e-4`，阈值 `2e-6`。
4. `InterferogramFooterTests.MetadataUsesActualSelectedFieldCoordinatesAndTypedUnits` 的 Angle 分支：打印 PV 与 typed PV 差 `4.362478765655986e-4`。
5. 同一测试的 ObjectHeight 分支：差 `5.158597189747205e-5`。

后四项涉及显示/数据契约，不能直接解释为 Core 的追迹精度回退；也不能仅降低测试精度就视为修复。

## 已确认的问题

### F01 · P1 · 几何对象内部修改绕过光线缓存失效

位置：`Core/Geometries/GeometryModels.cs:175`、`Core/Raytrace/RayTraceCacheBinding.cs:24`、`Core/Raytrace/SequentialRayTracer.cs:285`。

`StandardGeometry.Radius/Conic` 是公开可变属性；缓存绑定只订阅表面等外层对象的通知。执行 `((StandardGeometry)surface.Geometry).Radius *= 1.5` 没有通知表面，也没有解除共享缓存。使用相同修订号和输入光线再次追迹会直接命中旧结果。

**复现结果：**命中计数 1；缓存方向与解除缓存后重算方向的最大向量差为 `0.03007478144550365`。这是同一 Core 的旧/新状态对比，不是 Zemax 精度比较。

整改：使组件不可变、统一经表面 setter 替换，或让所有嵌套可变组件完整上报追迹修订。审查材料/相位/后端同名替换的同类入口。验收须覆盖直接修改 StandardGeometry、asphere 的 Base/系数以及后端更换；保持缓存键不依赖显示标签。

### F02 · P1 · 已取消的旧打开操作仍能覆盖后来新建的文档

位置：`Application/Services/OpticalDocumentService.cs:72–81`、`Application/Services/WorkspaceCoordinator.cs:92–119`。

读文件后只在进入提交锁之前检查 `linked.Token`；真正调用 `ReplaceDocument` 时传入的是原始 `cancellationToken`。若旧操作在等待 Gate 期间发生新建/其他打开，文档生命周期令牌虽被取消，旧操作仍可提交。

**确定性复现：**旧导入完成后阻塞于 Gate；在锁内新建 `Untitled optic`；释放锁后最终文档变为 `Old pending open`，没有异常。

整改：在 Gate 内校验打开请求代次/预期文档状态，再执行替换；不要简单把会在 `ReplaceDocument` 内自行取消的令牌贯穿整个事务。验收须覆盖旧 open 与 new/open/编辑交错，不依赖计时碰运气。

### F03 · P1 · 镜头数值输入的验证异常从 LostFocus 事件逸出

位置：`App/Panels/LensEditorPanel.cs:339–359`、`App/Panels/LensEditorPanel.RadiusSolves.cs:24–27`、`App/ViewModels/EditorRows.cs:57–79`。

`RadiusDisplay` 用 `double.TryParse` 接受 `NaN`；失焦回调直接调用 `_prescription.UpdateSurface`，没有异常处理或验证提示。Core 拒绝 NaN 并回滚，但异常继续抛到 UI 事件分发。非法普通文本还可能静默保留旧值，却把输入文本记作已提交。

**复现结果：**真实行解析器得到 NaN；真实 Application 提交抛 `ArgumentOutOfRangeException`；Core 原半径仍有限。这里确认了异常路径，未通过原生桌面故意触发进程崩溃。

整改：使用统一的解析/验证结果，保留错误输入并标识无效，在事件边界处理可预期异常；只有成功后更新 committedText。检查 Enter、Escape、Tab 和失焦的一致行为，并保持材料行蓝底的验证状态可辨认。

### F04 · P1 · 后台优化在整个计算期间持有共享文档锁

位置：`Application/Services/OptimizationService.Run.cs:45–86,119–143,174–236`；读侧例子 `WorkspaceCoordinator.cs:55–84`、`PrescriptionService.cs:36`。

QuickFocus、单半径优化及多变量优化虽然使用 `Task.Run`，计算主体仍位于 `lock (Gate)` 内。界面同步请求文档摘要、表面、另一个优化任务或保存时也要获取同一个锁。一次长优化期间只要 UI 命中这些入口，就会卡在锁上；UI 线程卡住后取消按钮也无法处理输入。

整改：在短锁内捕获快照/修订和任务令牌，锁外计算，最终短事务验证修订并提交。验收包括优化期间读取/缩放/取消，以及用户编辑后旧优化结果不得覆盖新状态。当前为静态锁路径确认，未测原生 UI 卡顿时长。

### F05 · P1 · “保存并继续”没有确认保存后仍然存在的新修改

位置：`App/Shell/MainWindow.Documents.cs:75–105`、`App/MainWindow.cs:220–228`；下层 `OpticalDocumentService.cs:126–137`。

Application 正确支持“保存期间又编辑”：旧快照写入成功，当前文档仍 Dirty。但 `TrySaveProjectAsync` 在 await 完成后直接返回 true，`TrySaveAllChangesAsync` 没有再次检查文档修订/Dirty，关闭或切换可继续并丢弃保存过程中产生的新编辑。异步保存期间窗口并未统一禁止编辑。

证据：既有 `DocumentRevisionAndSaveTests.EditWhileSnapshotIsBeingSavedKeepsCurrentRevisionDirty` 已覆盖下层语义；本轮交叉审查发现 UI 消费方遗漏。整改需要修订条件或冻结编辑的关闭事务，不能把“写完旧快照”等同于“当前文档已持久化”。补充关闭/切文件的延迟保存集成测试。

### F06 · P2 · 有损格式导出被服务接口视为完整保存

位置：`Application/Runtime/WorkbenchRuntime.Documents.cs:61–95`、`Application/Services/OpticalDocumentService.cs:133–137`。

`.zmx` 和旧 JSON 等分支只写 `ActiveOptic`，没有针对多个配置的丢失保护，而保存服务更新 CurrentPath 并清除 Dirty。

**复现结果：**2 个配置保存 ZMX，重新打开只有 1 个，保存后的 IsDirty 为 false。当前桌面默认 Save/Save As 只选 STAROPT，降低了常规 UI 触发概率；公开应用接口仍有此缺陷。

整改：把工程保存与外部格式导出分成不同契约。有损导出保留工程路径/Dirty；或在保存接口显式拒绝不能往返的文档。补多配置、断开链接和非序列场景的往返测试。

### F07 · P1 · 公差结果用显示字符串存数值，图表又把它解析回数值

位置：`Application/Formatting/NumericDisplayFormatter.cs:5–10`、`Application/Runtime/WorkbenchRuntime.Tolerancing.cs:140–169,277–284`、`App/Panels/ToleranceChartDocumentPanel.cs:166–188`。

默认三位小数会在 DTO 层舍入试验值、名义值、变化量及统计值，直方图和良率再用这些字符串计算。阈值附近的试验分类、标准差和曲线会随显示精度变化。解析器还先用逗号分割字符串；在逗号小数文化下，`0,012` 会被当作 `0`。

**同一公差请求复现：**三位输出为 nominal `0.011`、compensated `0.012`、degradation `1.334E-4`，算术误差 `8.666e-4`；六位输出误差约 `3.752e-7`。并非 Core 在两次运行中换了算法。

整改：公差 DTO 保存 double 和 typed quantity/unit，控件最后一步才格式化；图表、阈值和数值导出只能消费原始数据。用户三位显示偏好可以保留。Interferogram 的 typed 数据已保留，相关打印/报告断言应另行明确展示精度契约，不要把四个失败混成光学数值错误。

### F08 · P1 · 衍射和卷积重循环缺少取消检查

位置：`Core/Analysis/ImageSimulationEngine.cs:815–843,983–1017`、`Core/Analysis/DiffractionEngine.cs:950–971,1383–1450,1565–1602,1640–1669`。

BaseAnalysis 的前后取消检查无法中断中间这些纯计算循环。取消、重算或文件切换后，旧计算可继续占用线程和 CPU。连续刷新还会叠加多个已经失去显示价值的任务。

**复现：**给真实公开 `SpatiallyVariableConvolution` 设置已取消的 `ComputationCancellation` token，256×256 图像、64×64 核仍完整计算后正常返回。具体耗时见 `probe.log`。

整改：在外层行/分块边界检查取消，统一传播 OperationCanceledException，并限制分析任务并发。验收用可控开始信号覆盖“计算中取消”，而不仅是调用前取消。

### F09 · P1 · 资源预算遗漏卷积、PCA 和非二次幂变换的主成本

位置：`Core/Analysis/AnalysisResourceLimits.cs:134–211`、`Core/Analysis/ImageSimulationEngine.cs:534–550,983–1017`、`Core/Analysis/DiffractionEngine.cs:1165–1186,1416–1441`。

图像仿真主要预算只计入 pupil²×PSF²×field×wavelength。512×512 图像、256×256 核、2×2 pupil、一个 field/波长通过检查，但**仅均值卷积的循环上界已有 17,179,869,184 次**，尚未计入各 EigenPSF 卷积。Gram 矩阵工作随 fieldCount²×PSF² 增长，也未直接预算。

此外 `Transform2D` 对非二次幂 N 使用四重循环 O(N⁴)，而 `ComputePsfMtf` 未按该成本拒绝请求。255×255 即超过 42 亿个 DFT 累加项。此处只进行了算量及代码检查，没有实际运行超大任务。

整改：为各阶段分别估算内存、工作量和总并发预算；选择合适的卷积与变换算法，再调整上限。验收包含合法边界和最坏配置的快速拒绝、取消响应以及合理大小的完成时间。

### F10 · P2 · STARRDB 对压缩输入长度没有独立内存上限

位置：`Core/NonSequential/NonSequentialRayDatabase.cs:393–400,459–472,537–544`。

头部/分块限定了解压长度，却只用文件剩余长度或索引位置约束 compressedLength；随后直接 `new byte[compressedLength]`。一个足够大的损坏/构造文件可以要求远超 64/256 MiB 解压上限的单次压缩缓冲分配，SHA 校验发生在分配之后。

整改：独立约束压缩长度，或按有界流解压；在分配前校验长度和索引。建议用报告大 Length 的测试 Stream 验证拒绝，不需要制造真实巨型资产。本轮为静态确认，未故意触发 OOM。

### F11 · P2 · 公差图表未发布 typed 轴元数据

位置：`App/Panels/ToleranceChartDocumentPanel.cs:68–75,111–138`；默认值见 `Application/Contracts/WorkspaceContracts.cs:980`。

直方图、累计通过率及合格线仅提供含 mm/waves/% 的标签，没有设置 X/Y Quantity 和 Unit，因而保持 Unspecified。这违反项目“非空轴必须发布 typed 元数据”的要求，使格式、单位缩放与导出无法可靠使用语义。

整改：按 ToleranceCriterion 设置 Length/Millimeter 或波前 waves 的对应类型，试验数及百分比也显式设置。标签重命名和本地化必须不改变数值缩放，补全三条图表构建路径的契约测试。

### F12 · P2 · 图像仿真 Core 默认配置无法通过自己的资源验证

位置：`Core/Analysis/ImageSimulationEngine.cs:65–85,135–139`、`Core/Analysis/AnalysisResourceLimits.cs:184–211`。

`Simulate(optic, source, config: null)` 使用 `new ImageSimulationConfig()`，其 5×5 field、64 pupil、128 PSF 的 basis 工作量为 1,677,721,600，超过 200,000,000 的上限。公开 API 不传可选配置就会拒绝。`ImageSimulationAnalysis` 另有较小的默认值，桌面分析入口受到保护，但两套默认行为不一致。

整改：统一 Core 的可执行通用默认配置；应用预设在 Application 层选择，精度基准显式指定捕获文件设置。不要扩大安全上限来掩盖默认值冲突。

### F13 · P2 · 当前主题实现没有通过已有架构门禁

位置：`App/Theming/BlueThemeStyles.cs:292`、`App/Theming/LightTheme.cs:43`；失败测试 `tests/OptilandWorkbench.Tests/LayeringArchitectureTests.cs:223`。

新主题仍直接创建两个 `CornerRadius(6)`，与共享 Chrome 角色规范冲突。属于可定位的工程一致性失败，不是光学计算问题。

整改：用已经存在的语义圆角资源；若确实新增设计角色，应同步角色定义与规范，而不是删除测试。保留材料行蓝底、聚焦和禁用验证回归。

### F14 · P2 · Huygens PSF 误差监控未通过，需要解释跨环境数值边界

位置：`tests/OptilandWorkbench.ZemaxComparison.Tests/ExtendedAnalysisTests.cs:45,84`。

全量及两项定向复跑中，Huygens PSF Cross Section 均得到 NRMSE `0.0037256670590166776`，高于记录上限 `0.003725666990` 约 `6.90167e-11`；比较结论仍为 `Close`，不是原生精度 `Pass`。定向复跑 1 通过 / 1 失败，不与完整 104 项相加。

这里确认的是当前误差门禁失败，**尚未确定该极小变化来自平台/运行时差异还是计算变更**，不能将它夸大为实质光学退化，也不能直接忽略。应固定相同提交与设置在支持的平台上复核，区分原生精度容差、已知误差预算及浮点噪声边界；若确需调整监控方式，须给出误差来源证据，保持原生比较容差及 `Close` 状态，不直接放宽断言冒充修复。

## 可加速与可优化项

以下是已定位的成本来源及建议。O01 只在隔离微基准比较了候选路径，所有产品优化均未实施，也未给出未经测量的加速倍数。性能整改必须保留纯 C#/.NET 的共享 Core，不得把实验室拆成第二套光学引擎。

| 编号 / 建议优先度 | 位置与成本 | 改进方向与验收 |
|---|---|---|
| O01 / 高 | `Core/Coordinates/CoordinateSystem.cs:13–50` 每次点/方向变换重算 6 个三角函数和矩阵乘法；批量追迹逐光线调用 | 在一次追迹的面准备阶段预计算旋转/逆旋转和原点，轴对齐面走等价快路径；包括偏心、旋转和反射测试。微基准见下文 |
| O02 / 高 | `SequentialRayTracer.BatchedSurface.cs:88–91` 同一面/材料/波长每条光线重复色散计算；`NonSequentialTracingEngine.cs:239,305` 每段 Resolve 还克隆材料 | 每次追迹按材料实例及精确波长准备光学常数，保留环境/色散语义；非序列使用本次请求的只读材料表，不扩展为无界全局缓存 |
| O03 / 高 | `OpticalSurface.StateTracing.cs:20,142–162` 标量回退逐面做能力检查并反复 RayState↔RealRay 分配；asphere/grating 易进入该路径 | 面级预检提升到已验证快照准备层，提供共享 Core 的值类型交互路径；用非球面、光栅、镀膜的分配/数值对照验收 |
| O04 / 高 | `ImageSimulationEngine.cs:983` 直接空间卷积；`DiffractionEngine.cs:950` MMDFT 四重循环与 `1416` 非二次幂 DFT | 按尺寸选择直接/FFT 卷积；MMDFT 使用两阶段可分离矩阵乘法；非二次幂采用可分离/通用 FFT。保持边界、核中心、归一化、频率坐标一致，不能为追求速度改采样定义 |
| O05 / 高 | `DiffractionEngine.cs:1572–1597,1646–1664` 每个 image pixel 重算 pupil 不变量、振幅、相位及偏振字典键 | 先生成紧凑 pupil 数据，预计算每个 pupil 的常量，按图像块有界并行；保留累加顺序/误差边界并复跑捕获设置的 PSF/MTF 对照 |
| O06 / 高 | `App/Controls/AnalysisPlotControl.cs:365–387,418–421,483,849–906` 鼠标移动触发整图重绘、过滤/复制全点集，heatmap/raster 每点创建 brush | 按数据修订缓存单位转换、边界和绘制数据；位图承载 raster/heatmap；hover 单独轻量层，空间索引找近点。以高密度图的帧耗时和分配验收 |
| O07 / 高 | `App/Controls/WavefrontSurfaceControl.cs:464–507` 每帧逐格建数组、三角形、排序、Geometry 和 brush | 缓存拓扑及颜色，按相机状态重投影；必要时按显示分辨率抽样并明确仅影响显示；保持相机/文件修订重置契约 |
| O08 / 高 | `WorkbenchRuntime.Documents.cs:189–225` 事务与撤销重复 Capture；`DocumentUndoRedoManager.cs:13` 只按 100 条限制；`StarOptProjectStore.cs:45–85` 序列化/压缩发生于首个 await 前 | 使用可共享不可变快照或差量撤销，增加字节预算；锁内捕获、锁外后台编码和原子写入。保持失败回滚、保存顺序与修订语义；实测见下文 |
| O09 / 高 | `NonSequentialDocument.cs:389,402,888–892` Clone 会 Validate，每次重新 SHA-256 全部网格；`AnalysisService.cs:90` 顺序分析也无条件 Clone 非序列文档 | 不可变资产在导入/反序列化验证一次并记录可信状态；只验证变化部分；顺序分析避免复制无关非序列文档。不得跳过文件边界完整性检查 |
| O10 / 中 | `SequentialRayTracer.cs:246–248` 每次追迹乃至缓存命中前都克隆所有面；`RayTraceCache.cs:90–100,118–136,174` 大数组复制在单锁内，精确键每次再分配 | 在不可变修订内复用准备好的面数据；缓存条目保持不可变，短锁取得引用后锁外复制；研究 single-flight 合并同键并发请求，保持精确输入身份和内存预算 |
| O11 / 中 | `PooledRayStateBuffer.cs:16–36,113–134` 总是租用偏振数组，纯数值数组每次整桶清零归还；非序列分裂 `NonSequentialTracingEngine.cs:353` 复制完整前缀 | 按请求延迟分配可选状态，基于初始化证明减少无效清零；引用数组仍须正确清理。分裂历史采用内部共享不可变前缀，输出时保持完整路径/去重语义 |
| O12 / 中 | `MaterialCatalogService.cs:43–68,103–127` 每次重新枚举/解析/克隆/排序；`MaterialDatabasePanels.cs:225–226` 加载时连续执行两遍；`WorkspaceSessionStore.cs:133–159` 哈希路径逐级枚举目录 | 以目录版本缓存只读玻璃 DTO；一次获得分类和玻璃。缓存文件规范路径，精确路径可确定时避免目录全枚举；检查大小写/符号链接与文件切换 |
| O13 / 中 | 实验室 `FlatStartDesignProblem.cs:83,113,264` 每评价重建光学对象/采样数组/快照；`FlatStartSearchCheckpointStore.cs:22,49` 同一 checkpoint 两次 JSON 序列化，每批两次保存整个累计历史 | 在请求或工作线程内复用只读采样和受控 Core 工作对象；用单次规范化序列化同时计算 hash，考虑分段/追加检查点。必须保留同快照导出、预留预算先落盘、崩溃后扣账与确定性排序 |
| O14 / 中 | `.github/workflows/ci.yml:250–260` 性能 smoke 未指定 Release；`tools/OptilandWorkbench.Benchmarks/Program.cs:293` 单次 warm-up + 单次计时，阈值 2 分钟/2 GiB | Release 性能作业分配套场景：标准面/非球面、PSF/MTF、缓存冷热、公差、实验室、百万源、复杂分裂、UI 帧与取消延迟。记录多次分布与分配，不把 smoke 当性能回归基线 |

### 已测量的两个局部成本

O01 的隔离 Release 微基准每组调用 `ToLocalPoint` 一百万次；候选路径复用**现有私有 RotationMatrix 方法的结果**，没有另写光学公式。最后三次观测原路径为 45.941 / 44.694 / 44.268 ms，预计算路径为 3.512 / 3.578 / 2.643 ms，累加校验差为 0，约 12.5–16.7 倍。**这只是坐标转换内核，不能称为整机追迹加速十余倍。** 原始数字见 `probe.log`；运行期间实验室回归仍在执行，因此不作为独占机器性能基线。

O08 使用 24 配置的 Cooke 工程，`Documents.SaveAsync` 从调用到返回 Task 之前就产生约 9 MB 的当前线程分配，并同步占用约 50–70 ms。这里测量的是无巨型网格的小工程；异步方法名称并不代表编码已经离开 UI 线程。保存网格时 `StarOptProjectStore` 还同时保留 `CopyCanonicalData` 和所有压缩资产，建议流式逐资产处理。非序列 Clone 目前共享资产实例，**不能把撤销内存夸大为每条记录都复制全部网格二进制**。

### 现有 Release 基准的本机观测

使用仓库现有 20 面基准，每项一次预热、一次计时，Runtime 10.0.8 / 10 个逻辑处理器；未修改计算代码。分配指测量窗口全部线程的累计托管分配，不是峰值占用。

| 场景 | 耗时 | 累计分配 |
|---|---:|---:|
| 100,000 光线 FinalOnly | 646.674 ms | 1,399,607,008 bytes |
| 100,000 光线 SelectedSurface（只请求 3 号面） | 121.969 ms | 276,302,376 bytes |
| 100,000 光线 FullHistory | 716.285 ms | 1,399,612,952 bytes |
| 100,000 光线几何 MTF | 760.831 ms | 1,544,075,848 bytes |
| Monte Carlo 100 次 × 每次 128 光线 | 313.620 ms | 517,577,280 bytes |

SelectedSurface 的请求工作不同，不能拿该耗时比值当作等价全路径加速。`PsfMtfSampling` 是基准的旧输出名称，实际调用 `GeometricMtfAnalysis`，没有测 FFT/Huygens PSF；输出峰值工作集在本机为 0，故没有可用的峰值内存结论。分配量提示应优先剖析 O03/O08 等来源；该观测尚不能证明具体方法占比分布。

## 商业验收应补齐的证据

1. 首先修复 F01–F05、F07–F09 并加入故障复现回归；解决所有当前失败。每个修复需要代码、测试、文档同步，不能只让测试数字变绿。
2. 统一“原始数值、显示格式、typed 单位、导出精度”的契约，覆盖 zh-CN/en-US/de-DE、小数位设置变化及阈值边界。用户选择三位小数不应改变良率或数据导出。
3. 文档操作用可控异步屏障验证 save/open/new/close/取消交错；验证事务回滚时的事件、撤销栈、CurrentPath 和 Revision 一致性。
4. 性能先实施 O01/O02/O06/O08/O09，再评估 O04/O05/O13。每项先固定同输入/同设置，测时间、分配、峰值内存与取消延迟；数值优化同时做原实现对比和捕获设置的权威对照。
5. 发布验收另补原生 GUI、复杂非序列/高 NA/有限共轭/不同波长范围、安装升级/签名及崩溃诊断。当前库中存在已声明的功能边界：非序列体吸收/偏振/散射，以及实验室尚未实现的研究路线，不能从基础场景通过推导这些能力已经完成。

## 复跑与证据保留

构建及测试命令：

```sh
dotnet build OptilandWorkbench.slnx -c Release --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-build --no-restore
dotnet build labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx -c Release --no-restore /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OptilandWorkbench.InitialStructure.Tests/OptilandWorkbench.InitialStructure.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/OptilandWorkbench.ZemaxComparison.Tests/OptilandWorkbench.ZemaxComparison.Tests.csproj -c Release --no-build --no-restore
dotnet run -c Release --no-build --no-restore --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj -- 10000 100000
```

隔离复现程序位于 `artifacts/code-audit/2026-09-27/probe`，直接引用默认 Release Core/Application DLL，链接真实 `EditorRows.cs`；为测试内部文档服务使用仓库已有的 friend assembly 名称，但输出完全位于该隔离目录，不覆盖正式 Tests。程序只在临时路径写入并清理自己的示例文件。

```sh
dotnet restore artifacts/code-audit/2026-09-27/probe/AuditProbe.csproj --source /tmp -p:NuGetAudit=false
dotnet run -c Release --no-restore --project artifacts/code-audit/2026-09-27/probe/AuditProbe.csproj
```

关闭 probe 的 NuGet 在线审计仅用于此无 PackageReference 的离线复现程序，不改变产品的审计配置。`artifacts` 是本地证据目录，默认被 Git 忽略；本报告保留关键结论与数值，若需把复现升级为长期回归，应将其转成正式测试并随修复提交。
