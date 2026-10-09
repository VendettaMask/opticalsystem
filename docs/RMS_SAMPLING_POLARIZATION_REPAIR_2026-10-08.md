# RMS 采样参数与偏振链路修正 · 2026-10-08

本次修复的是实际生效的采样密度、桌面设置含义和波前 RMS 的偏振强度链路。它不是一次新的完整 Zemax 数值认证，也没有改写冻结的原生数据、容差或此前的对比报告。

后续同日[继续对比诊断](ZEMAX_PUPIL_DIAGNOSIS_2026-10-08.md)已复现波前图的出瞳形状 / 瞄准误耦合、负 Y 场表渐晕跳变，并量化两套 RA 节点敏感性。该轮没有修改产品计算，未完成 RA 波前或负 Y 原生捕获，不冲销本页未完成项。

再后续的[波前图系统瞄准修复](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)已拆开波前图显示选项与系统瞄准。本页全量计数保留为该修复前历史范围；RA 波前节点、带符号渐晕及衍射路径未随之完成。

历史[波前阶段验收](WAVEFRONT_ACCEPTANCE_2026-10-08.md)为正式 **4430/4/4434**、比较工具 **178/2/180**（通过/失败/总数），零跳过。本页计数保留各自阶段；当前实现与验收见[2026年10月9日发射坐标修复与当前验收](RAY_LAUNCH_COORDINATE_REPAIR_2026-10-09.md)，整体发布门禁仍未关闭。

## 已实现

### 1. 按方法解释密度，不再静默限制 RA 到 32

五个 RMS 入口（视场、波长、离焦、二维场图、独立波前视场）统一验证密度：GQ 表示径向环数，共享 Core 支持 1–32；RA 表示每边点数，支持 1–1024。非法密度明确抛出范围错误。此前独立波前、波长、离焦和场图构造器对两种方法都限制到 32，导致 RA64/128/256 没有按请求执行。

OpticStudio 2026 R1 的 RMS vs. Field 文档将 GQ 上限规定为 20。本程序的 32 是共享 Core 的扩展计算范围，**不是 Zemax 上限或默认设置**。做原生对照应使用其允许范围并匹配全部设置。字段间隔数仍按已有捕获契约生成 `Field Density + 1` 个扫描点；本次没有更改扫描方向、最大视场和通用构造器默认值。

所有 RMS 扫描在开始生成光瞳/追迹前估算总光线工作量：候选光线数上界 × 扫描评价次数 × 波长数 × 表面数 × 必要的重复追迹次数。上界为既有的 100,000,000；超限明确报错，请求不会降密度。单束偏振波前仍受 1,000,000 条光线的既有保护。支持输入 RA1024 不代表任意视场数、波长数和表面数的组合都能执行。

### 2. 桌面设置与保存

设置标签随方法切换为“GQ 径向环数”或“RA 每边点数”，输入上限分别为 32、1024；已保存的 RA128/256 不会在打开设置时降到 32。交互切换到 GQ 时，如果现值超过 32，输入框显示调整后的 32。Core/API 对非法密度直接拒绝，不会这样自动调整。

五个入口都公开 `GaussianAzimuthalSamples`（GQ 角向点数），一般默认保持 6、范围 1–72；RA 下仅禁用该输入并显示原因，切回 GQ 后恢复，不禁用外围标签，也不清除保存值。保留现有 `NumRings` / `RayDensity` 设置键以兼容旧设置文件。此前比较工具的显式 `2 × GQ 径向阶数` 属于该批捕获的配置，不被提升为所有分析的默认值。

独立“RMS Wavefront vs Field”也提供“使用偏振”。两条波前视场入口均将实际开关传入正式计算，并发布 `UsePolarization`、`PupilWeighting`、`PupilSampleCount` 与实际 `RayDensity`。

### 3. 偏振计算真正影响权重

此前“RMS vs Field”的波前分支只在结果元数据记录偏振开关，未传给波前引擎；波前统计也只使用几何权重。另一个问题是波前引擎的旧实数矩阵标记没有接入现有正式 Fresnel/Jones 强度输运。

现在启用偏振时，共享波前引擎复用正式 `SequentialRayTracer.DiagnoseApertures` 的完整强度链。只有通过全部实体孔径的结果才能成为波前样本，诊断延续到孔径外的光线永不接纳。输运遵循系统的非偏振或 Jones 输入、已支持的膜层与材料限制，膜层衰减只计一次。

波前 RMS 在开启偏振时使用“几何积分权重 × 透过强度”。chief 参考去除加权活塞；centroid 参考去除加权最佳拟合活塞及两个光瞳倾斜。多波长方差按波长权重和该波长的有效透过光瞳权重合成。关闭偏振时保留原来的几何积分权重；`WavefrontStatistics.Measure` 的一般调用也保持默认几何口径，避免把其他操作数改成强度加权。

这仍是标量 OPD / 强度加权 RMS，**没有加入偏振相位像差或矢量 Huygens PSF**。多波长仍保留既有主波长参考球及逐波长去活塞/倾斜流程，不宣称完成原生全光瞳联合参考拟合认证。

## 支持边界与未完成项

- 正式 Jones 输运仍拒绝尚未实现的吸收介质接口复折射率功率运输、GRIN 连续偏振、非标准交互或不供给物理偏振振幅的膜层等组合。开启波前 RMS 偏振遇到这些情况会明确报错，不会伪装为无偏振结果。内置 Cooke 的真实目录玻璃含吸收数据，因此该模型的裸吸收接口目前属于拒绝范围；新增偏振成功用例使用明确命名的无吸收测试处方，不能算作原镜头认证。
- RA 光斑继续使用已经原生逐点验证的单元中心网格。RA 波前仍保留原有端点网格；本次只恢复真实请求的密度，**没有**凭 FFT 波前图文档推断 RMS RA 的精确节点。新增高密度波前测试是内部契约测试，不是 Zemax 节点校准。
- 保留渐晕因子的正 Y 视场平方径向插值保持已有原生证据。官方文档描述的带符号 Y / 对称视场插值范围更宽，但没有给出精确公式；本次未把正 Y 公式外推到该范围，一般场表仍保持现有最近行行为，需补原生控制。
- RA512/1024 原生收敛控制、Relay 瞄准残差、衍射差异、其余 21 份官方镜头和发布门禁仍未完成。此前六文件 132 项和独立 RMS 23 项的分类属于 2026-10-07 的外部结果，本次没有重新捕获或执行那套完整矩阵，也没有覆盖用户现有 Word 对比报告。

## 验证

本节保留 RMS 实现阶段的执行范围和计数。最终后续完整验收见[计算路径修复报告](CALCULATION_PATH_REPAIR_2026-10-08.md)，待解决项见[分类问题清单](OPEN_ISSUES_2026-10-08.md)。本节引用的 RMS 测试输出随本次同步另存原始字节归档到 `artifacts/validation/rms-sampling-repair-20261008/formal` 和 `tool`，文件名不变；原测试输出不删除。

新增 `RmsSamplingRepairTests` 与 `RmsSamplingPanelTests` 共 25 项：真实 RA64/128/256 波前计算与独立网格枚举、chief/centroid 两种参考、两条应用入口参数贯通、相邻 RMS 扫描密度、非法输入/总预算、实际 Jones 输入和透过强度、独立加权方差/尺度不变性、未实现介质明确拒绝、五个设置面板的保存和方法切换。

Debug/Release 相关回归各 **222/222** 通过，新增 25 项在两配置均通过，零失败、零跳过；集合重叠，不能相加。范围包括新用例、既有 Zemax RMS 波前/矩形光斑/GQ 原生冻结曲线、波前操作数、渐晕与正式偏振回归。默认正式 Debug/Release 构建零警告、零错误；最终 Release 桌面输出也已单独构建更新。

其中包含冻结原生曲线的 `ZemaxRmsWavefrontVsFieldParityTests`、`RectangularRmsParityTests`、`GaussianPupilParityTests` 三类采样回归 **29/29** 通过；这属于已有参考的当前重算，不是新建原生捕获。结果保存在测试输出中的 `rms-sampling-related-debug.trx` / `rms-sampling-related-release.trx`；主全量及工具全量分别使用 `rms-sampling-full-release.trx` / `rms-sampling-tools-release.trx`。

正式完整 Release **4407 通过 / 2 失败 / 共 4409 项**，比较工具完整 Release **178 通过 / 2 失败 / 共 180 项**，均零跳过。发布门禁仍未通过；没有修改容差、冻结参考或以定向通过替换完整失败计数。完整 Debug 和实验室全量本轮未重跑。

| 失败范围 | 失败身份与观察 |
| --- | --- |
| 正式既有历史参考 | `FieldDefinitionParityTests.FieldDefinitionsMatchFrozenReferenceInitialAndFinalRealRays`：finite_angle 期望 0.058589983242791625，实际 0.060191701479492175；与此前相同。 |
| 正式界面会话关闭 | `PanelContentLayoutTests.DrawingReadsMaterialValuesAndCombinesThemWithEditedTolerances`，Iso10110 / cemented=true：`SafeHeadlessUnitTestSession.Dispose` 的 NullReferenceException；栈未显示光学计算或产品断言失败。 |
| 工具既有 Huygens 截面 | NRMSE 0.0037256670590166724，高于冻结上界 0.0037256669899999998。 |
| 工具既有衍射包围能量 | curve:1 NRMSE 0.011467280942848471，高于冻结上界 0.01146706362185577。 |

图纸六种标准/胶合组合与五个新增 RMS 设置用例在隔离 Release 复跑中 **11/11** 通过（其中图纸 **6/6**）。新增设置用例随后明确关闭自动应用并阻止首次挂载的后台分析，仅测试参数控件，避免取消中的光学任务和设置保存进入短生命周期的 UI 测试。正式产品代码没有为此改动图纸或会话异常过滤器；不能据一次隔离通过宣称全量 UI 稳定性已修复。全量记录发生在这一测试隔离调整前；最后的两配置相关回归验证最终代码与测试，完整全量没有再次重跑。隔离记录为 `rms-sampling-drawing-isolation-release.trx`。

## 官方文档依据

- [OpticStudio 2026 R1 — RMS vs. Field](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html)：GQ / RA 密度含义、GQ 20 上限、参考点、偏振开关及孔径截断下的 GQ 限制。
- [Polarization — System Explorer](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Polarization_System_Explorer.html)：系统输入状态与常规顺序分析的强度加权；矢量 Huygens 是不同能力范围。
- [Vignetting Factors](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Vignetting_Factors.html)：缩放/平移后旋转，以及场表插值适用范围；没有据此推定未经捕获验证的具体插值公式。

实现位于正式 Core 的 `RmsScanAnalyses.cs`、`Fields/FieldSweepAnalyses.cs`、`WavefrontEngine.cs`、`WavefrontStatistics.cs`；应用参数和桌面控件仅负责设置传递，不包含第二套光学公式。
