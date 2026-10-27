# 初始结构（产品内置模块）

更新：2026-10-27。通过 **实验室 → AI 初始结构** 打开，与镀膜设计同在主程序进程内运行，随标准安装包交付。产品保持纯 C#；相同的基础光学计算使用共享 Core，AutoLens 专用的绿色参考、detach 与 epsilon RMS 损失统计留在实验室。

后续镜片增删与自主搜索按[自主镜头拓扑设计实施计划](../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)推进。`AutonomousTopologyV1` 已完成 M0–M5：稳定 ID、Core 编译/参数绑定、固定拓扑优化、真实增删恢复、有限多候选 CPU 搜索、硬追迹配额、独立归档和取消续跑，以及原始快照渐进删除、真实拆片/并片、目录材料和显式偶次面型分支。M3 两个短程终点未完整达标；[M4](../../validation/rechecks/2026-10-26/autonomous-search-m4/README.md)与[M5](../../validation/rechecks/2026-10-26/autonomous-expansion-m5/README.md)使用明确的小型流程目标，不证明原任务优化成功。M5 新计划设置 `Options.Expansion = new()` 开启扩展，空值保持 M4 行为和旧检查点校验；过渡片冻结全部变量，只给本片临时厚度例外，实体删除后恢复原约束独立验收。M6 已接入显式 CPU 的内置桌面入口，并完成受控短程对照，范围见下文；下述 `UpstreamE58`、桌面自动优先 GPU 默认及已停止的旧训练保持独立。

自主设计入口为 **实验室 → AI 初始结构 → 自主拓扑设计**，同进程独立编辑状态，不改变原 AutoLens 默认预设。可导入受支持的无限远 STAROPT、`.optic.json` 或 `.topology.json`；有限物距等不支持的输入明确拒绝。未编辑计划保存时保留原精确数值与身份。加载设计计划（`.topology-plan.json`）或带校验的自主记录（`.autonomous.json`）不执行光学计算；开始或继续均需显式操作。自主记录位于 `OpticalSystemDesign/Labs/InitialStructure/autonomous/runs`，按阶段原子保存。记录载入后锁定原目标和预算；修改目标应新建计划，不修改历史记录。停止、关闭运行窗口先保存；保存失败保留窗口和错误，避免显示保存成功。复制记录只接受新文件名，不覆盖既有记录。

界面展示真实候选/父方案、操作阶段和失败、函数/梯度/追迹实耗、预约与归档，以及实际面位置、材料、曲率、非球面系数和各视场物理验收。过渡片不发布；未达标候选仍可明确标识后导出，STAROPT 导出通过共享 Core 回读校验。中英日与窄窗口使用离屏控件验证；按用户要求，后续测试只在后台运行，不控制用户桌面。原生交互尝试被 Escape 中止，不计为原生验收。详见[界面验证](../../validation/rechecks/2026-10-27/autonomous-desktop-m6/README.md)。

[M6 的16次同预算探索](../../validation/rechecks/2026-10-27/autonomous-comparison-m6/README.md)在两个相近三片任务、两个种子上比较 A 固定片数、B 增删无恢复、C 增删有恢复、D 加渐进删除。四组各有2/4案例保留达标处方，均来自本来已达标的M2任务；未优化起点全部未达标。最少合格片数仍为3，D两次过渡验收拒绝，未证明成功减片或统计优势。相同预算上限、训练权限和独立终验下实际消耗不同，不能把候选数或训练损失当作设计成功率。未恢复旧长训练，未发布安装包。

后续[减片修复复验](../../validation/rechecks/2026-10-27/topology-reduction-repair/README.md)确认M6恢复后的合格二片被后续补偿破坏。新自主示例默认`Options.CheckStageFeasibility=true`、局部/全目标阶段`RequireDescent=true`：恢复/局部补偿后先按原完整Core门槛独立验收，已达标结束分支并归档，未达标再补偿；过渡仅可推进，不发布。每次验收预约并实际计费，不借用终验预留。下降保护拒绝同一固定目标下上升的有限提议，仍计费；旧记录两个开关空值保持原行为，继续旧记录不自动升级策略。可通过新任务导入已有处方，或打开修复对照中保存的新计划使用新策略。

本轮同预算上限、两种子C/D的16次组合对照中，原策略最少达标3片、仅下降保护2片、阶段验收1片；合格单片RMS43.401/44.137µm，四份导出重新导入仍通过。仅证明这一个50mm、F/12.5、半视场5°任务的配对结果，困难恢复/渐进失败保留，未认证手机F/1.8或普遍最优性。该阶段仅曲率/显式面型；当前新计划已接入下述有界轴向补偿与敏感度局部选参；阶段内每一步的短暂可行点也未逐次密集复验。

[更低F数/大视场复验](../../validation/rechecks/2026-10-27/representative-reduction/README.md)随后覆盖五份F/2.0–4.93、14°–35°半视场的3/4/6片物理副本。原输入与物理化改动留痕，72次包含40次原分配、24次同总预算分配控制、8次合格四片闭环。原五份先在几何、最大光斑、通光或验收预算处失败，不能算作五个合格镜头减片失败；合格四片实际删到三片后RMS35.642µm仍有21°边界失光15/99，未通过。最少合格片数保持4，0个合格减片结果；4次预算预约不足的后续分支保留。较大视场密集验收成本限制探索范围，低F数任务还需要先通过原严格基准的合格起点。本轮未改生产算法，所有运行仅后台低优先级CPU，不操作桌面；以前F/12.5、5°的单片结论不作普遍保证。

[21°边缘失光修复](../../validation/rechecks/2026-10-27/topology-edge-loss-repair/README.md)进一步确认SF15后表面真实遮挡。新自主示例默认显式开启`Local/Full.Sampling.IncludeBoundaryClearance=true`，在原Gaussian统计外加入33条中心/边界的Core可微余量约束；`Options.ProtectCompensationRayBudget=true`使Local后的可选密集检查无法预约完整额度时零计费跳过该次检查，继续Full并保留原独立终验及预留。旧null计划/记录不自动升级，Core/UpstreamE58不改。两批共16次同上限短程对照、60项后台CPU/离屏回归完成；完整补偿后的三片仍在21°失15/99，RMS35.504µm不能代替通光门槛，最少合格片数保持4。口径/光阑/像面/目标/变量权限未放宽，失败STAROPT及预约不足的后续分支均保留。
[通光优先及局部选参复验](../../validation/rechecks/2026-10-27/topology-transmission-priority/README.md)新增显式实验`FixedTopologyOptions.TransmissionFirst=true`、局部`ActiveParameterLimit=2`及`TransmissionPolicyVersion=3`；该阶段仅显式开启，当前新示例已开启这些策略。局部在同次计费Core Jacobian中选通光敏感列和独立补偿列，有符号约束保留已满足门槛的切向，通光下降且其他训练门槛不恶化才接受，实际步长拒绝折半/接受恢复。单次预算、参数类型和独立物理门槛保持；旧普通null策略不变，未标版本的新策略原型只读查看/导出，不能静默以当前模型续跑。已提供显式v3新计划并验证与留存v3局部终点逐值一致，计划导出不执行训练。

40次三批短程均保留：边界失光15→9，有符号组合RMS43.593µm、仅21°边界门槛失败；最终回溯组合仍9条且RMS51.284µm失败，0个合格减片，继续保留合格四片。训练1×8在同一10.5°视场约48.48µm、原完整积分为51.159µm，内部余量不能替代密集验收。70项后台CPU/离屏回归及native回读完成，无桌面操作/长训练或预算追加。该阶段随后转入下述训练积分校准及受限轴向修复，不能独立声称已解决失光。
[训练积分精度与剩余失光复验](../../validation/rechecks/2026-10-27/topology-sampling-precision/README.md)随后通过收敛校准，将显式短程协议1×8改为4×16：同定义RMS相对10×32的最大误差从5.312628µm降至3.57e-7µm，纯采样配对不增加总预算，正式RMS51.284→48.583µm，但仍9条失光。高精度固定曲率修复后仍9条，再单独在原制造约束中开放第二片前后位置各±0.25mm；这次三片接续成功：全35视场960/960积分、99/99检查通过，RMS48.999µm、最大半径92.793µm、焦距/F数均在原±2%内。光阑、像面、口径/材料/半视场21°和验收不改。

合格三片已保存STAROPT并回读完整验收，终点积分差4.56e-9µm；成功单次初验/训练/终验合计1,077,444追迹、14梯度，低于原总额。源准备成本和14项实验分别记账，不称为从四片开始一次预算内减片；部分邻域诊断因配额截断明确留痕。后台30项定向回归完成。当前新自主示例已接入该有界轴向策略，旧计划保持原行为；Core及UpstreamE58/MOBILE_CONFIG不变。

[F/3.34、21°门槛修复](../../validation/rechecks/2026-10-27/wide21-gates-repair/README.md)已修复训练最大未包含独立检查的统计缺口：共享Core沿用积分质心取Gaussian/检查最大，检查不进入RMS。新自主示例默认`Sampling.IncludeBoundaryMaximum=true`与显式`TransmissionPolicyVersion=4`；v4联合分步恢复、逐行保护和有界4096轮投影，支持取消及身份隔离。既有v3计划保持400轮及原行为，加载/继续不自动升级。仅修正最大统计不足以消除通光和畸变缺口。

本处方原起点12对照中联合4控制得到合格四片；保留10条失光终点的独立固定接续用2更新/3梯度、950,170追迹通过原门槛：RMS47.414336µm、最大149.893620µm、畸变2.999853937%、35视场960/960与99/99。口径/视场/原16参数边界和门槛不变，仍是四片，未得合格减片；畸变余量小，未作稳健性认证。所有失败/native、源准备和接续成本分开保存，86项相关后台CPU/离屏及默认Release通过。

[高精度与轴向补偿接入](../../validation/rechecks/2026-10-27/topology-integrated-compensation/README.md)该阶段将4×16训练、v3通光优先和局部敏感列选择接入；当前新例v4及检查最大见上。编辑器可显式切换高精度训练、镜面轴向微调及移动上限；已载入且未编辑的计划保留原身份，编辑预算也保留自定义采样及原门槛精确值，不自动重写为新配置。

`Options.AxialCompensation`为可空的独立计划字段，默认每次结构试验起点±0.25mm、尺度0.1mm；空`ElementIds`选择所有未保护的保留镜片，也可在计划中限定指定镜片。前/后位置独立，故可同时改变厚度与空气隙，但仍受原Core制造与结构门槛约束。恢复前持久化`Pending.AxialAnchors`，默认Recovery只恢复曲率，Local/Full可选这些位置；显式`UseAxialVariablesInRecovery=true`也可让恢复使用同一窗口。换阶段、渐进删除或停止续跑不重新居中。参考面、光阑、像面、口径、视场、材料与正在渐进移除的镜片不由该策略改变。窗口以每次结构试验的起点计，不是整个多试验搜索累计的全局位移承诺。旧null计划、记录与AutoLens身份保持独立，加载不运行。

本轮40短程中，同一合格四片的轴向4控制在原单次预算内均4→3通过，仅曲率4控制仍9条失光；三片RMS49.142089µm、最大半径96.982404µm、全35视场960/960与99/99，4份native回读通过。新换4份F/3.17–3.61/18°–30°的32次尚无合格减片，21°14→10仍失败、18°未评完/瞄准不可用、28°较优增片仍失败，均保留。80/80相关后台CPU/离屏回归及默认Release通过；1次60秒配置实记61.459秒保留超时，不宣称所有墙钟严格过线。源准备、固定三片贯通与额外审计分开记账，见上述报告。

[指定合格四片4→3恢复修复](../../validation/rechecks/2026-10-27/four-to-three-recovery/README.md)已在原总配额内获得合格三片：RMS49.292572µm、最大136.488028µm、畸变1.472027483%、焦距25.012716mm/F3.402845，35场全部960/960积分与99/99检查，制造门槛通过。源为上轮固定接续的合格四片；最终直接删除两种子均通过，渐进两次失败，之前24次失败全部保留。每次161函数/29光线梯度/1,356,386追迹，含四片初验及三片终验，不拼接先前诊断预算。

新修复均通过计划显式启用，普通新示例和旧计划未自动升级：`Recovery.UseAutomaticMatrixDerivatives`用共享Core固定平面ABCD导数替换差分，`Recovery.RestorePupilMagnification`另恢复固定光阑的父逆F数；后者必须同时开启自动导数。`Options.ProtectPostRecoveryRayBudget`在完整可选密集检查额度不足时延后，继续Local，最终验收照常。`Options.UseAxialVariablesInRecovery`沿用原冻结轴向权限；`Options.CompensateConic`单独开放锥常数，不自动开启Profile分支或多项式。可空字段省略null，旧身份与续跑不改；恢复矩阵导数计入函数/矩阵/Jacobian，光线梯度计数定义保持，瞳调用另列。

成功需要六个[-2,2]锥常数，属于额外面型自由度；原曲率/轴向界限不变。仅修复恢复/预算仍失败，锥常数配原Full8也以RMS50.472853µm失败；最终Full上限99利用原40万阶段/200万全局剩余额度，实际13接受更新后被阶段追迹截断。不能称与原8更新同段分配、原纯球面权限或普遍可减片。94/94定向后台回归、native回读及默认Release通过，全部28次配额/输入/权限核验。RMS/F数余量小，未作容差认证；全过程后台两处理器低优先级，无原生桌面操作或完整精修。

新实验使用 `AutoLensManagedV1` 入口的显式 `UpstreamE58` 策略，算法后缀 `autolens-upstream-e58-v2`、优化状态 schema 2。默认预设来自已实际跑通的原版 MOBILE_CONFIG：5 mm、F/1.8、全视场 90°、六片、独立前置光阑、20000 mm 物距、r⁴–r¹⁸、种子 42。另保留 README 的五项本地目标适配。载入不自动运行，也不代表成功处方。[逐项对齐及保留差异](../../docs/AUTOLENS_UPSTREAM_ALIGNMENT_2026-10-24.md)、[预设来源](../../docs/AUTOLENS_OFFICIAL_EXAMPLES.md)、[当前状态](../../docs/CURRENT_STATUS.md)分别说明范围。

系统页管理目标与 RGB 波长；目标页管理初始化总长/后焦、允许玻璃及独立验收门槛；搜索页管理种子数、资源、面型、材料优化和完整阶段计划。旧二元形式、QUICK 筛选、起始半径和移动光阑搜索控制已移除。平板祖先仅为旧存储格式的来源证明，实际随机起点另存 `TrainingStartOptic`。

阶段交接的原版 JSON 舍入约定在实验室执行，首面归零后由舍入面间距累计重建所有面和像面；见[交接修正](../../docs/AUTOLENS_HANDOFF_REPAIR_2026-10-25.md)。流程按原版随机初始化、半到全口径课程、固定输入光线求导、detach 绿色参考/权重、线性约束、直接 Adam、目录投影及固定玻璃精修推进。默认执行 2001 + 3001 次更新。预算不足保存坐标、矩、缓存与完整阶段进度，不压缩课程、不做下降线搜索、不接原生 target polish，也不用旧 incumbent 替换训练终点。默认使用原版 61 项 CDGM 表及对应真实色散，显式允许玻璃限制仍有效。

完成终点才进入产品原有的独立物理验收：21 视场、10×32 面积积分瞳、独立边界/中心检查，以及焦距、F 数、制造、光斑与通光硬门槛。训练掩码和连续材料不能代替验收，未完成训练不发布临时投影副本。

设备选择为自动优先 CUDA、GPU、CPU；均使用共享 Core 双精度。有限物距的方向瞄准使用 Core CPU，单独计为 `CpuAimingRays`；固定输入四输出方向导数采用 `cuda-fixed-input-forward-v1`，按光线和参数在 CUDA 成批计算，沿正式 Core 交点与折射方向传播。历史隐式瞄准/相对厚度保留六输出记录反传并复用内存。正式传播、完成的有效光线导数、批次和回退原因分别计数。[GPU 边界](../../docs/AUTOLENS_GPU.md)说明 2026-10-26 相同处方、完整默认单步规模的提速及数值检查；这不是完整训练时间或最终像质比较。训练随机流未逐位复现 CUDA Philox，Core double 与原版 float32 仍不同，另一套 CAMERA_CONFIG 拓扑尚未完整复现。

运行记录位于本地应用数据 `OpticalSystemDesign/Labs/InitialStructure/flat-start/runs`，采用带校验的 `.family.json` 文件。剩余预算和配额允许时恢复同一状态；追加细化使用独立预算。文件按批次原子保存，恢复工作先预约配额，进程丢失的未知工作保持计费；未派发取消为已知零工作。旧 v1、原生及混合记录保持原身份，可查看/导出，不能通过当前桌面恢复或细化。导出候选经 Core STAROPT 回读验证，不修改正式工作台。

```powershell
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -m:1
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj --filter FullyQualifiedName~AutoLensReferenceTests -m:1
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release
```

构建和测试不需要原版 Python 环境；冻结规则夹具只作测试输入。参考训练要求三个有效 RGB 波长、绿色为主波长，所有采样覆盖都保存为规格身份。资源上限为 100000 评价、24 小时、128 起点、4 并行任务及 64 MiB 搜索文件。[模块计算边界](../../docs/INITIAL_STRUCTURE_LAB_PLAN.md)与[授权声明](../../third_party/autolens/AutoLens-NOTICE.txt)保留；“AI”不代表预训练网络或自主设计智能体。本轮未发布安装包。
