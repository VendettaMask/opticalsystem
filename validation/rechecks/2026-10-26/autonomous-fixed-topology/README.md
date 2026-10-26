# AutonomousTopologyV1：M2 固定拓扑优化

日期：2026-10-26。对应[计划 v1.2](../../../../docs/AUTONOMOUS_TOPOLOGY_V1_IMPLEMENTATION_PLAN.md)的 M2。当前交付为内置模块库接口，尚无自主增删恢复、搜索调度或界面。没有恢复旧完整训练或精修。

默认 Release 输出下，Core **30/30**、模块 **41/41**，合计 **71 项定向通过、0 失败、0 跳过**。分别见 [Core TRX](core-complete.trx)、[Core 日志](core-complete.log)、[模块 TRX](module-accounted.trx)、[模块日志](module-accounted.log)和[机器汇总/二进制 SHA256](summary.json)。其中 M2 新模块用例 16 项、Core 新能力 5 项，其余为相关既有回归；不将中间重复运行相加。默认 App 输出也已更新，各测试及 App 使用的 Core 二进制指纹一致。

## 交付接口与定义

- `FixedTopologyObjective`：通过稳定 ID 参数绑定使用任意支持片数，不假设固定族面号。固定每一条残差和 Jacobian 行；变量、边界、目标、结构、材料、归一化和采样身份不可混用。
- `FixedTopologyOptimizer`：复用现有自动梯度 Adam，显式有界更新；无效提案拒绝并保留上一有效处方、丢弃该提案的矩。无变化返回 `Stalled`，不冒充收敛达标；端点独立验收决定是否达标。
- `TopologyValidator`：新建实际处方，以 Core CPU 独立复核原目标。保存原始目标、结构约束和指纹，不读取训练损失决定合格。
- Core 新能力：二维显式视场、实际独立光阑的隐式求导、入口瞳径和连续逆 F 数；当前像面的一阶主光线像高尺度、非相邻参考行间距以及共享多色质心。保留旧 EPD/Upstream 路径。

残差含光焦度、连续逆 F 数、中心/边缘厚度、中心/边缘空气间隙、机械与光学总长、后工作空间、斜率/曲面有效域、多色 RMS/最大半径、主光线畸变、逐光线净空及共同质心 XY。调用方指定物理变量边界和尺度；材料固定为真实目录玻璃。本阶段要求启用光阑瞄准，不能静默改写任务的设置。

畸变采用 **主波长主光线相对当前像面近轴映射的偏差比例**，Core `DistortionMetrics.ParaxialChiefFractionAtField` 与相应自动梯度同定义。它区别于已有 Grid Distortion 的实光线拟合参考，也不使用目标焦距再次惩罚倍率误差。光斑采用多色共同质心，保留横向色差，不将各波长分别居中。

最终扫描每个有效定义视场从轴上到该点的 21 个位置并去重，Gaussian **10×32** 面积积分瞳，**32 个边界点加中心**，轮廓 **64 个径向点**。本组默认任务为 21 视场；多定义点/方向可能更多，超过 101 点在训练前拒绝。分别检查总光线和各波长的几何有效比例、全部边界检查、像质及制造门槛；过渡片不允许合格。它不是包含镀膜损耗的能量通光率。

## 相同设置下的初末物理结果

冻结输入源与参数见 [M2 协议](../../../../labs/InitialStructure/benchmarks/autonomous-topology-v1/fixed-topology-m2.json)。无限远、0–5°、486.1/587.6/656.3 nm，主波长 587.6 nm，独立前置光阑半径 2 mm。目标为 EFL 50 mm、F/12.5，二者容差 2%；最差 RMS ≤50 µm、最大半径 ≤150 µm、上述主光线畸变 ≤3%、每波长及总有效光线 ≥98%，全部边界检查通过，并满足结构/制造限制。

三种基准**显式允许像面位置参与优化**，不改变原 M0 文件的保护标记。每个光学面只释放曲率，另释放像面；20 次 Adam 更新、学习率 0.02，训练为 3 视场/方向、Gaussian 1×8、8 个轮廓径向点。初始和最终物理表均使用上面的同一密集终验，未用不同采样的训练量替代。

| 片数 | EFL 初始→最终 / mm | 最终 F 数 | 最差 RMS 初始→最终 / µm | 最终最大半径 / µm | 最终最大畸变 | 最终合格 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| [2](optimization-2.json) | 214.4791→77.2900 | 19.3225 | 940.7474→319.1984 | 455.1693 | 0.01344% | 否 |
| [3](optimization-3.json) | 49.0692→49.5777 | 12.3944 | 151.2890→43.4785 | 65.9421 | 0.19590% | 是 |
| [4](optimization-4.json) | 39.9799→47.8769 | 11.9692 | 15.6651→119.8710 | 173.8278 | 0.23797% | 否 |

三者密集积分瞳及每波长有效比例均为 100%，全部边界/中心检查通过。两片仍有焦距、F 数和像质问题；四片的综合训练目标下降，却牺牲了原先较小的光斑，焦距/F 数和像质仍未达标。**不能以目标下降替代质量或成功结论。** 四片只是预先插入新片后的固定输入，不构成 M3 的近轴恢复闭环。

三片的通过仅限本轮目标、可动像面权限和设置，不推广为固定像面版本、原版 MOBILE_CONFIG、其他种子或外部认证。另有 [固定像面和受保护镜片的三步真实回归](protected-stage.json)，验证其内容及位置不变，并未声称那项也达标。

## 全参数、裁光与边界

[全部参数差分](all-parameter-gradients.json)覆盖三片处方 **27 个变量**：所有前后面轴向位置、曲率、圆锥系数，首面全部 r4–r18，以及像面位置。r2 固定为零。**340 个固定残差×27 = 9180 个 Jacobian 项**，中心差分步长 1e-4；活动制造约束取最小边缘厚度 0.4 mm，活动畸变门槛取 0.05%。最大归一化差：残差约 **5.25e-8**，完整目标梯度约 **6.03e-7**，均通过 5e-5 门槛。这次诊断的 55 次评价/求导请求及 **8250 条实际追迹**单独记录，不混入 20 步阶段预算。

[裁光例](clipping.json)把一面有效半口径缩为 0.1 mm，保持实际机械口径。训练继续返回相同行序的未裁光诊断坐标和正净空惩罚，另对其全部 7 个选定变量作差分；正式 Core 验收保留实际丢光和空指标，判不合格。残差没有因丢光而删除或缩短。

[临界折射](critical-refraction.json)使用实际 N-BK7 色散和接近恰好临界的玻璃出射面，Core 拒绝奇异导数；结果无目标值、无伪造零梯度、保留固定失败布局和已消耗追迹，外层返回明确失败。[轮廓无效域](domain-failure.json)同样拒绝。正常前置/内部/负瞳放大率及二维视场，另在 Core 层与正式光线和全部选定参数差分核对。

## 预算、取消和 CUDA

每个 20 步阶段为 **22 次评价尝试 = 初始 1 + 更新 20 + 独立终验 1**，21 次完整梯度，实际追迹 **47670** 条。初始密集物理对照是基准工具的额外工作，单独记 **44520** 条；两者合计每个基准 **92190** 条。RayCount 是实际提交光线的计数，包含瞄准、失败及诊断并排除命中缓存的计算，不是交点总数，也不能把不同片数的一步当成相同成本。

[三次真实拒步](rejected-steps.json)保持上一合法处方，累计 5 次评价尝试、4 次梯度请求，其中仅 1 次得到完整梯度；三次非法轴向提案没有实际追迹，未虚构光线工作。最后一个评价槽始终预留独立验收。[取消](cancellation.json)、[求导中途取消](partial-gradient-cancellation.json)分别核对未派发零工作、已完成更新及中途实际消耗；取消/超时不再启动昂贵终验，无最终验收就不发布为合格。无自由度明确返回 `Stalled`。

[CUDA 两步阶段](cuda-stage.json)在实际设备上运行 **225 条导数光线、9 批**，终点目标与 CPU 的绝对差约 **3.55e-15**，并记录 GPU 名义传播和回退数据；独立终验保持 Core CPU。失败/取消请求若拿不到完整 GPU 导数计数，预算仍保留已派发请求和已测追迹，GPU 总数标为 null，不当作零或成功 GPU 工作。

当前硬上限是评价/梯度请求、更新次数、墙钟及工作集；没有硬光线配额、持久 Adam 检查点或精确恢复。本阶段不能恢复旧 Upstream 状态，M4 再实现完整预算调度和恢复协议。

## 复现

仓库根目录，默认输出：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~PhysicalStopDerivativeTests|FullyQualifiedName~OpticalDesignDerivativeTests|FullyQualifiedName~SampledSpotMetricTests|FullyQualifiedName~AxialSurfaceSeparationTests' --logger 'trx;LogFileName=core-complete.trx' --results-directory validation/rechecks/2026-10-26/autonomous-fixed-topology --nologo
$env:OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M2_EVIDENCE = Join-Path (Get-Location) 'validation/rechecks/2026-10-26/autonomous-fixed-topology'
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~FixedTopologyOptimizerTests|FullyQualifiedName~AutonomousTopologyFoundationTests|FullyQualifiedName~AutoLensReferenceTests.ReferenceDefaultIsPinnedMobileProtocolAndCatalog|FullyQualifiedName~AutoLensReferenceTests.UpstreamFrozenLinearBoundsProfilesMaterialsAndGradientsAgree|FullyQualifiedName~AutoLensReferenceTests.UpstreamFrozenAdamMomentsAndBothSchedulesAgreeIncludingNonFiniteSanitization' --logger 'trx;LogFileName=module-accounted.trx' --results-directory validation/rechecks/2026-10-26/autonomous-fixed-topology --nologo
```

## 失败历史与限制

所有失败日志/TRX 保留。早期问题包括 ApertureKind 命名空间、常量瞳径零梯度未补齐维数、混用带瞄准辅助维数的近轴量、零光焦度下正式 F 数未定义、ImmutableArray 比较方式，以及新增像高接口误算像面之后的占位厚度。相应修复已通过最终定向范围，小角度实际主光线/逐面近轴追迹也已核对；旧通用焦距、瞄准和物理口径规则未为这些测试改动。

`gradient-active-first` 保留极端缩放诊断：畸变门槛 0.001%、目标量约 1.73e9、差分步长 1e-5 时，一个 r6 参数的完整目标差分归一化差约 **3.17e-4**，未通过 5e-5 门槛。它不能直接证明自动梯度有同量级错误，也没有被算作通过。最终全参数结论限定为上文明确的目标尺度和可分辨扰动；没有验证所有极端尺度或把换用诊断设置称为消除该数值边界。

有限共轭、胶合、偏心/倾斜及未建模特征仍按 M1 契约拒绝。采样是有限离散检验，不是连续全口径数学证明。没有完整训练/精修、全仓测试、原生界面验收、新 Zemax/原版外部比较或同预算 A/B/C/D。M3 的完整近轴矩阵、实际增删恢复与补偿为下一阶段。
