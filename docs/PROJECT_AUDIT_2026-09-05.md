# 项目审查与 Zemax 数值对齐检查（2026-09-05）

> 历史记录：本文描述当时的代码、命令和测试计数。后续 Python Optiland 格式、连接器及专用兼容分支已移除；当前边界和验证见 [移除审计](PYTHON_OPTILAND_REMOVAL.md)。历史命令中的已删除 API/测试名称不能直接用于当前版本。

当前工作区存在会影响计算结果的问题，尚不能宣称整体与 Zemax 对齐。最优先处理的是复色 MTF 合成、浮动光阑的入瞳换算、点列图权重/参考点/局部坐标，以及精度对比工具的误判机制。

本次是检查与复现，没有修改业务代码、已有测试、镜头处方或 Zemax 基准。审查对象为 `D:/Projects/opticalsystem` 当前工作区，包含开始检查前已有的未提交修改；Git HEAD 为 `cedddf31fcabe5c855e97c9349fd211a1331e64f`，不能将此报告当作该提交的纯净版本审查。

本文件保留修复前的历史审计证据；后续修复、当前测试和严格对比结果见 [项目修复报告](PROJECT_REPAIR_2026-09-05.md)，下文失败数量不代表修复后状态。

## 范围与证据

扫描 `src / tests / tools / scripts / labs / packaging` 中 529 个 C#、Python、PowerShell 和项目文件，共 162,042 行；进行主解决方案、实验室解决方案构建，主测试、实验室测试、Python 对比工具测试，以及计算核心定向阅读和独立反例复现。这是全仓库扫描与重点逻辑审查，不代表逐行证明全部代码正确。

重点检查了追迹与光线生成、旁轴量、点列/RMS、PSF/MTF、赛德尔、ZMX 导入和材料、序列化、缓存绑定、公差、应用服务、镜头预览、GUI 测试契约和打包脚本。反例程序独立存放，不加入产品或原有测试集。

主要精度依据为已提交的 `123456.ZMX / OpticStudio 2026 R1` 捕获基准。没有重新启动 OpticStudio；官方帮助用于核对定义，不能替代同镜头、同设置的数值 golden。

- [机器可读汇总与失败详情](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/summary.json)
- [文件与重复块扫描](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/inventory.json)
- [C# 反例程序](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/Probe/Program.cs)、[反例结果](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/probe-results.json)
- [对比算法反例](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/comparison_probe.py)、[反例结果](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/comparison-probe-results.json)

## 已确认的问题

P1 表示应优先修复的计算或验收错误；P2 表示功能可靠性或适用范围问题。以下定位均为本次工作区行号。

### F01 · P1：复色 MTF 返回值与复色 OTF 的模不一致

位置：[MtfScanAnalysis.cs:863](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs:863)，相关路径为该文件的 `CombinePolychromatic` 和 `EvaluateFourierThroughFocusPolychromatic`，以及 [GeometricMtfAnalysis.cs:118](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/GeometricMtfAnalysis.cs:118)。

`CombinePolychromatic` 已经计算加权复数 OTF，但 `Tangential/Sagittal` 仍返回各单色 MTF 模值的加权平均。普通 Modulation 显示使用这个标量数组，Real/Imaginary 使用另一个复数数组，因此同一结果违反 `MTF = |OTF|`。离焦 FFT 分支也分别累加 `.Magnitude`；几何 MTF 在合成前同样已经丢弃单色 OTF 相位。

独立反例输入两个等权波长：在非零频率处单色 OTF 分别为 `+1` 和 `−1`，两个单色 MTF 都为 1。合成后的 OTF 为 0，函数却返回 Modulation=1。该反例验证合成函数，不冒充新的 Zemax 镜头捕获。

影响：存在色差、相位差或对比度反转时可能高估复色响应，并使 MTF、实部、虚部之间不自洽。修复应在统一物理频率及共同参考原点上合成复数 OTF，最后求模，并检查离焦、几何和采样分支是否保留相位。光学关系的官方背景见 [Ansys 对 PSF/OTF 计算的说明](https://optics.ansys.com/hc/en-us/articles/42661791331859-What-does-the-sampling-correspond-to-in-wavefront-based-calculations)。

### F02 · P1：Float by Stop Size 把光阑直径直接当作入瞳直径

位置：[Paraxial.cs:84](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Services/Paraxial.cs:84)、[SurfaceGroup.cs:106](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Domain/SurfaceGroup.cs:106)。

`fallbackDiameter` 是 `2 × stop.SemiDiameter`，`FloatByStopSize` 直接返回它，遗漏光阑前光学系统对光阑的成像倍率。光阑是第一物理面时测试容易通过；光阑位于有光焦度的前组之后时不成立。

Cooke 反例：光阑半径 4.6 mm，当前入瞳直径 9.2 mm，所定义旁轴边缘光线到达光阑时高度仅 3.4967816832 mm。按同一线性旁轴追迹反算，应使用约 12.1025571037 mm 入瞳直径，才能到达指定光阑边缘。

影响：F 数、归一化光瞳采样和依赖入瞳的分析尺度都可能偏差。应由前组旁轴映射求入瞳尺寸，并补充“光阑前有透镜”的测试。参见 [Ansys Ray Aiming / Float by Stop Size 定义](https://optics.ansys.com/hc/en-us/articles/42661778056083-How-to-use-Ray-Aiming)。

### F03 · P1：点列图及共用 RMS 路径遗漏波长权重

位置：[SpotAnalysisSupport.cs:112](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/Rays/SpotAnalysisSupport.cs:112)、[RayGenerator.cs:173](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Raytrace/RayGenerator.cs:173)、[RmsScanAnalyses.cs:553](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/RmsScanAnalyses.cs:553)。

每个波长生成相同的 pupil samples，传入生成器的是波长数值，没有把 `Wavelength.Weight` 应用于 `SpotRayData.Intensity` 或统计。RMS 汇总直接拼接各波长光线；权重为零的波长仍参与。

在主基准镜头第 5 视场设置波长权重为 `0,1,0`，全波长 RMS 为 `0.0004010965414 mm`，只选择主波长的 RMS 为 `0.0005928872290 mm`，两者本应一致，实际相差约 32.35%。

影响：点列 RMS、共享 SpotMetricEvaluator、几何 RMS 扫描及部分离焦指标。不能笼统推广到所有优化操作数，部分操作数另有自己的统计路径。修复时需同时规定显示光线密度、统计权重和零权重波长处理，避免在已单独加权的消费者中重复乘权重。官方点列图明确考虑波长权重和 apodization，见 [Standard Spot Diagram](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Standard_Spot_Diagram.html)。

### F04 · P1：复色 centroid 实际取主波长光斑中心

位置：[SpotAnalysisSupport.cs:163](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/Rays/SpotAnalysisSupport.cs:163)，尤其 178–181 行。

选中 `centroid` 时，代码只对 `referenceRays`（主波长或第一个波长）做简单平均，然后把这个中心应用于所有波长。它既不是全波长质心，也没有与 RMS 的强度权重保持一致。

主基准第 5 视场、原始等权波长、centroid 模式下，输出全部点的 Y 均值仍为 `6.1564738980e−5 mm`；主波长点列均值接近零。按所有已追迹光线定义的等权质心应为零。与 F03 不同，这个错误在波长等权时也存在。

修复应让参考点与统计采用同一套有效光线和权重；`IgnoreLateralColor` 的逐波长去中心需要作为明确的另一种行为保留。依据同上官方点列图的全光线 centroid 定义。

### F05 · P1：普通点列与方向余弦未转换为选定面的局部坐标

位置：[ImageSpaceAnalysisSupport.cs:109](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/Shared/ImageSpaceAnalysisSupport.cs:109)，普通位置返回在 139 行。

afocal 分支使用 `targetSurface.CoordinateSystem`，但普通像高和方向余弦分支直接使用全局 `Position.X/Y`、`Direction.X/Y`；焦移也沿全局 Z 投影。这会使旋转、偏心、倾斜像面和中间面的结果不符合所选面的坐标系。

使用固定角度视场的 Cooke 镜头复现：仅将像面局部原点平移 `(1,2,0) mm`，absolute/vertex 点列坐标变化为 `(0,0)`，应为 `(-1,-2) mm`；将像面绕 Z 旋转 90°，点列仍为 `(0.0016555771,0.0067628379) mm`，应转换成 `(0.0067628379,-0.0016555771) mm`。

本反例使用角度视场，排除了 RealImageHeight 重新瞄准移动像面带来的混淆。修复应先转换点和方向到目标面局部坐标，再按定义做焦移与参考点扣除。官方 vertex 定义为选定面的局部 `(0,0)`，见上述 Standard Spot Diagram。

### F06 · P1：主 Zemax 基准的赛德尔结果仍存在符号与幅值差异

位置：[SeidelCoefficientsAnalysis.cs:43](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/Reports/SeidelCoefficientsAnalysis.cs:43)、[Paraxial.cs:257](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Services/Paraxial.cs:257)。

本次实际导入不可改写的 `123456.ZMX`、显式选择波长 2（0.44 µm）重算：

| 数值 | 当前计算 | Zemax 2026 R1 捕获 |
| --- | ---: | ---: |
| 第一面 COMA/S2 | −0.000448 | +0.000428 |
| 累计 COMA/S2 | −0.000392 | +0.000374 |
| 累计 DIST/S5 | −0.088102 | +0.076620 |
| 累计 CTR/CT | +0.000245 | −0.000233 |
| 物空间主光线斜率 | −0.438723695 | +0.4188 |
| Petzval 半径 | −376.285033351 | −376.2850 |

Petzval 在本次工作区已经吻合，不能继续照抄旧文档中的 −376.1420。主光线/视场链路及相应系数仍未对齐，完整根因修复尚未验证，不能通过整体反号或放宽阈值解决。

`Captured123456ReportMatchesAllFourCoefficientTablesAtCapturedWavelengthTwo` 当前明确跳过。表格换算测试通过不等于从镜头导入到计算结果的端到端测试通过。原始依据：[基准 data.txt](D:/Projects/opticalsystem/artifacts/zemax/123456-zemax-2026-r1-baseline/analyses/010-seidelcoefficients/data.txt:19)。

### F07 · P2：近轴主光线只取 Y 视场，纯 X 视场退化为轴上

位置：[Paraxial.cs:324](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Services/Paraxial.cs:324)。

`ChiefRay` 取最大的 `abs(field.Y)`，未处理 X 分量。对旋转对称 Cooke 系统，把原 Y 视场等幅旋转到 X 方向，赛德尔累计 COMA/ASTI/FCUR/DIST/CTR 全部变为零，例如 ASTI 从 −0.008907 变为 0。

这是已复现的方向适用范围问题；本次没有该旋转案例的 Zemax 捕获，不将它冒充主基准的逐值对比。应明确支持的子午面约定，或在旋转对称系统使用径向视场映射；若分析仅支持 Y-Z，应在入口和输出说明限制，避免给出具有一般含义的零像差。

### F08 · P1：精度报告按数组位置比较，可能把错误坐标与方向判为一致

位置：[generate_workbench_comparison.py:285](D:/Projects/opticalsystem/tools/zemax_parity/generate_workbench_comparison.py:285)、[同文件:467](D:/Projects/opticalsystem/tools/zemax_parity/generate_workbench_comparison.py:467)、[同文件:500](D:/Projects/opticalsystem/tools/zemax_parity/generate_workbench_comparison.py:500)。

曲线按数组归一化序号插值，不按真实物理横坐标对齐；计算了 `coordinateNrmse`，最终分级却只用 `valueNrmse`。网格比较枚举翻转/转置并选择误差最小方向；指定系列名字找不到时，`select_named` 又自动取下一个未使用系列。

独立反例：将参考曲线横坐标放大 100 倍，数值不变，`coordinateNrmse=57.2134674651`，仍判 `high-agreement`；左右镜像的非对称 3×3 网格也被自动翻转后判零误差；缺失的指定系列被替换成 `wrong field`。

本次真实重算中 Fourier/Geometric MTF vs Field 的最大坐标 NRMSE 约 2.41，仍被旧算法判为高度一致。该数字是坐标未对齐的警报，需先检查单位、视场定义、设置和映射，不能直接当作内核数值误差。

修复应要求物理量/单位/视场/波长/参考点/坐标方向与源设置一致，在共同物理坐标上插值，并使缺失系列、坐标超差、有效数据不足导致比较失败或标记不可比。方向变换必须来自明确的坐标约定；探索最佳翻转只能留在诊断结果中。

### F09 · P2：采集成功被误当成分析计算成功

位置：[AccuracyCapture/Program.cs:83](D:/Projects/opticalsystem/tools/OptilandWorkbench.AccuracyCapture/Program.cs:83)、[generate_workbench_comparison.py:1013](D:/Projects/opticalsystem/tools/zemax_parity/generate_workbench_comparison.py:1013)。

只要 `BuildAnalysisView` 没抛异常，工具就写 `captured`，报告再转换成“72/72 成功”。本次 72 个输出中至少 3 项实际只有状态提示：

- Non-Sequential Ray Trace：`No non-sequential source objects`。
- Non-Sequential Detector Viewer：`No detector objects`。
- Incoherent Irradiance：`Detector surface has no supported physical aperture`。

这些是正常的不可用/不适用状态，不是有效数值结果。不能简单规定“没曲线就失败”，因为报告类分析可仅有表格；应由核心结果提供独立于显示文字的 typed 状态，并让采集/比较统计区分有效结果、状态页面、错误和不适用。

### F10 · P2：镜头库预览异常处理覆盖不完整

位置：[LensLibraryService.cs:129](D:/Projects/opticalsystem/src/OptilandWorkbench.Application/Services/LensLibraryService.cs:129)。

现有代码只对特定英文前缀的 RealImageHeight 异常退回无光线布局。新的 `RayAimingException` 未进入该处理。主测试实际遍历 925 个打包镜头，169 个预览失败，日志中大量出现光阑瞄准不收敛。

应使用结构化异常/状态区分“镜片几何能显示但光线无效”和“工程本身不可读”，允许前者显示镜片并带失败说明。这个降级只能用于预览，数值分析仍应保留计算失败，不得掩盖追迹错误。

## 重复与多余部分

扫描没有发现可以整包删除的重复实现。唯一完全相同的 C# 文件是 App 与 Application 的 `Properties/AssemblyInfo.cs`，它们分别为两个程序集声明属性，不能当作重复文件删除。

| 位置 | 判断与建议 |
| --- | --- |
| [Tolerancing.Helpers.cs:29](D:/Projects/opticalsystem/src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Helpers.cs:29) 与 [Tolerancing.Parallel.cs:15](D:/Projects/opticalsystem/src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Parallel.cs:15) | 默认/配置公差构建、变量映射存在两套相似流程。一套捕获 CurrentOptic，一套接受 worker optic；两者有实际调用。建议共用显式接收 Optic 的工厂，避免维护时分歧。 |
| [Tolerancing.Helpers.cs:119](D:/Projects/opticalsystem/src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Tolerancing.Helpers.cs:119) | `EvaluateToleranceCriterion(ToleranceCriterion)` 私有包装重载未找到调用；有用的是接收 definitions 的重载。可作为定向删除候选。 |
| [MtfScanAnalysis.cs:1099](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs:1099) 与 [PsfAndMtfAnalyses.cs:508](D:/Projects/opticalsystem/src/OptilandWorkbench.Core/Analysis/Diffraction/PsfAndMtfAnalyses.cs:508) | MTF 数据类型/方波响应/插值选择重复，且有有界与无界插值差异。先定义行为再合并，优先随 F01 修复统一。 |
| ScalarBatchedNumericBackendAdapter 与 ManagedCpuBackend.BatchedIntersections | 最明显的相同 20 行窗口为 40 组，许多来自参数签名、校验和后备实现。标量适配器与 SIMD 路径各有用途，不应直接删除一套；可抽取共同校验/标量尾部。 |
| WorkspaceContracts 与 Core Analysis/NonSequential models | 存在重复枚举/DTO 形状，是分层边界。保留 DTO 边界，以映射完整性检查控制漂移。 |
| Through Focus MTF / Fourier Through Focus MTF | 都走 Fourier 路径，当前捕获对同组数据重复计算约 46.40 / 44.94 秒。可评估一个规范入口加兼容别名；须迁移保存的工作区/分析设置后再处理。 |
| MeritFunction.cs（3137 行）等大文件 | 属于维护复杂度，不以文件大直接判逻辑错误。优先围绕操作数类别、结果状态和数值合成拆分，避免只机械分文件。 |

扫描报告中的重复窗口数不是重复行数，不能相加当作可删除行数。

## 构建和测试结果

| 检查 | 本次结果 |
| --- | --- |
| 主解决方案默认 Debug 构建 | 0 错误、1 个 NU1900 警告：NuGet 漏洞数据源不可达；不是源码编译错误，也不是漏洞审计通过。 |
| 主测试 | 1194 项：1188 通过、5 失败、1 跳过。 |
| InitialStructureLab 解决方案构建 | 0 错误、0 警告。依赖使用本地 NuGet 缓存恢复，未进行在线漏洞审计。 |
| InitialStructureLab 测试 | 24 项：23 通过、1 失败。 |
| Python Zemax 对比工具测试 | 14 项通过；F08 反例显示其测试覆盖仍有空缺。 |
| 独立反例程序构建 | 0 错误、0 警告。 |

主测试失败的区分：

1. 镜头库预览测试：实际功能错误，见 F10。
2. `GlassCatalogTests.UnqualifiedLegacyGlassUsesCatalogPriorityAndUnknownNamesDoNotFallback`：旧断言期待未知玻璃导入立即抛异常，而当前导入将其保留为 `UnresolvedMaterial` 并在计算入口阻断。现有 `UnresolvedZemaxGlassTests` 已覆盖新行为；这次失败不能写成“未知玻璃被当成空气”。应统一新旧测试契约。
3. `LayeringArchitectureTests.GlobalNumericInputStyleKeepsSpinnerControlsAvailable`：旧源代码断言禁止隐藏 spinner，与当前新增输入样式相冲突。需要按已接受的 UI 设计统一断言。
4. `LayeringArchitectureTests.AppUiCardsUseSharedChromeTokens`：`ThemeRegistry.cs:165` 的 `new CornerRadius(6)` 不符合原共享 Chrome 契约，需要统一样式与架构约束。
5. `RayFanFooterTests` 的 OPD 分支：断言查找 `采样点数`，实际数据使用 `SampleCount`；属于标签/测试契约不一致，未据此判 OPD 数值错误。

唯一跳过项是 F06 的主赛德尔端到端对齐测试。

实验室失败：`FrozenBenchmarkSetMeetsTheL3AcceptedFamilyGate` 在 `03-portrait-85mm-f4-4e` 只找到 1 个 accepted family，冻结下限为 2。根因尚未收敛，不将它推断为某个优化算法错误，也不建议直接降低门槛。该用例中途断言终止，后续冻结案例的该项门禁未被本次完整验收。

日志：[主构建](D:/Projects/opticalsystem/artifacts/audit-2026-09-05-build.log)、[主测试](D:/Projects/opticalsystem/artifacts/audit-2026-09-05-test.log)、[实验室构建](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/lab-build.log)、[实验室测试](D:/Projects/opticalsystem/artifacts/audit-2026-09-05-lab.log)、[Python 测试](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/python-tests.log)。

## Zemax 对齐结论的边界

**基准完整性通过。** 源 SHA-256 为 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`，与当前重算来源匹配。基准 165 项中 148 项捕获、17 项不适用或失败；148 张图像中 106 张为 OpticStudio 原生截图、42 张为标明来源的后备图像。这仅说明基准清单/JSON/引用/图像完整，不证明当前算法精度。

**当前代码已重新运行采集目录的 72 项。** 72 个 JSON 均写出，其中至少 3 个是状态页面（F09）。采集器遍历其运行时目录，数量和输出索引已经不同于历史 69 项，不得复用旧索引说明作为当前目录事实。源镜头、旧设置清单和输出清单见 [current-manifest.json](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/recalculated/current-manifest.json)。

**现有对比算法只覆盖 29 个映射。** 它产生 25 个“高度一致”、4 个“接近”，这些名称是旧算法自身的分级，不是本次审计认可的整体精度结论。中位 NRMSE：MTF 约 5.16%、Ray Fan 约 2.93%（最差系列约 26.09%）、几何圈入能量约 3.84%、几何线/边扩展约 5.60%。F08 表明其坐标、方向和系列匹配门禁不足；赛德尔明确失败，也不在该 29 项表格中。

**未进行 GUI 图像一致性验收。** 本次生成的图像是 JSON 的 Matplotlib 离线重绘，绝不是实际 Avalonia 截图；没有据此对 GUI 与 Zemax 外观作一致性判断。自动报告中的“72/72 成功”等原始文字保留为工具行为证据，必须结合 F08/F09 阅读。[旧算法本次诊断输出](D:/Projects/opticalsystem/artifacts/audit-2026-09-05/recalculated/comparison.json)。

已明确披露的能力限制，如近似 GRIN、经验膜层/散射模型、偏振加权标量衍射、不支持的自由曲面/优化算法，不重复包装为本次新发现。非球面三阶赛德尔贡献仍属于需要补齐的实现范围；官方适用面型定义见 [Seidel Coefficients](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Seidel_Coefficients.html)。本次未以 Optiland 辅助 golden 替代 Zemax 精度结论。

## 建议处理顺序

1. 修复 F08/F09，使验证能够识别物理坐标错误、系列错配和无有效数据；保留原始 golden 与阈值。
2. 以本报告独立反例建立数值回归，修复 F01–F05；逐一排查共用消费者，避免权重重复或坐标约定混用。
3. 解决 F06，恢复严格的赛德尔端到端测试，并明确 F07 的方向适用范围。
4. 修复镜头预览和实验室门禁失败，统一新旧 UI/导入测试契约，再做重复路径收敛。
5. 在同镜头、同设置和固定数值后端下重新进行物理坐标比较；对尚无基准的倾斜像面、浮动光阑和复色相位案例补充 Zemax 捕获，另行完成真实 GUI 截图复核。
