# 项目审计修复与验证（2026-09-05）

> 历史记录：本文描述当时的代码、命令和测试计数。后续 Python Optiland 格式、连接器及专用兼容分支已移除；当前边界和验证见 [移除审计](PYTHON_OPTILAND_REMOVAL.md)。历史命令中的已删除 API/测试名称不能直接用于当前版本。

本次修复对应 [全项目审计](PROJECT_AUDIT_2026-09-05.md) 的 F01–F10、已失败的主项目/实验室回归，以及修复过程中复现的相邻问题。起点为 `cedddf31fcabe5c855e97c9349fd211a1331e64f` 加已有工作区修改；已有镜头编辑器、主题、报告和 Windows 打包变更一并集成验证。没有改写 Zemax 捕获资料、Python golden 或实验室冻结的家族下限。

## 修复内容

| 问题 | 最终行为与回归依据 |
| --- | --- |
| F01：复色 MTF 丢失相位 | Fourier、离焦和几何路径在共同物理频率上加权复数 OTF 后求模。FFT 在变换前恢复 PSF 物理原点；否则数组中心产生交替符号，插值会制造虚假抵消。PSF/波前使用共同主波长参考中心，快速离焦的重新采样也传递该参考。MTF 的模、实部和虚部共用计算，频率截断保留方向频率轴。反例覆盖相反相位抵消、位移点像的解析 DFT、离焦复数分量及截断插值。 |
| F02：浮动光阑遗漏前组倍率 | 从物面至光阑的旁轴映射反算入瞳，有限/无限共轭的边缘光线均到达实际光阑边缘；Cooke 入瞳直径为 12.102557103672 mm。缺失或奇异光阑不伪造尺寸。 |
| F03/F04：光斑统计及质心权重错误 | 统计使用有限正光线强度乘波长权重，全波长质心使用同一权重；零权重波长不参与 RMS/圈入能量统计。原始显示光线保留独立强度，避免重复乘权重；逐波长消除横向色差仍为显式选项。 |
| F05：像空间使用全局坐标 | 点、方向余弦与离焦投影统一到所选面局部坐标。回归覆盖像面平移和绕 Z 轴旋转。 |
| F06/F07：赛德尔主光线与视场方向 | 实像高/近轴像高在旁轴报告中按同一线性共轭定义求解，移除遗漏物方传播的反向主光线路径；视场模长包含 X 分量。恢复 `123456.ZMX` 波长 2 四张逐面和累计表的 1e−6 端到端比较，纯 X/Y 对称视场系数相同。 |
| F08：对比工具可能误报精度通过 | 通过类型元数据换算单位，按真实物理坐标插值，仅比较交集并报告覆盖率；缺少必需系列/网格即失败。禁止按数值选择翻转/转置、按数组序号拉伸或复用最后一组数据。系列坐标可排序，重复坐标被拒绝；20 cycles/mm 映射到捕获资料中明确的 20 cycles/mm 组。最差系列参与门禁。 |
| F09：状态页误算为成功 | `AnalysisOutcome` 区分 Success、Unavailable、NotApplicable，并携带原因贯穿 Core、Runtime、DTO、包装分析及采集清单。重算目录数量动态统计。采集复用要求源 SHA-256、Core/Application 程序集指纹和全部设置相同。 |
| F10：镜头库预览被瞄准失败阻断 | 瞄准失败采用类型化异常。预览仅在这类失败时显示无光线的几何场景并发布可见警告；计算入口继续报告失败。全部 925 个镜头预览回归通过。 |
| 相邻问题 | Huygens MTF 视场轴发布 NormalizedField/Dimensionless，并显式映射 DTO；普通视场曲线按坐标排序。光线光扇图传递系统 Ray Aiming 开关，回归与独立单光线追迹对照。 |
| 实验室冻结门禁 | 差分进化候选原用 density 1，而父候选和局部优化用 density 2，导致不可比较的目标值。搜索阶段统一 density 2，验收仍单独使用 density 4，算法身份升级为 3。十个冻结规格按原门槛通过。 |
| UI/导入契约 | 未知玻璃保留为 UnresolvedMaterial 并在计算入口阻断；旧测试与该行为统一。数值框样式契约、共享文档标签圆角和 OPD 采样点数标签同步现有界面。 |

删去了 MTF 重复插值、旧归一化坐标/网格拉伸辅助函数，以及串行公差重复的变量工厂。串行和工作线程公差变量共用参数化工厂。DTO 隔离层、不同后端实现、兼容别名和实验室工程边界保留。

Optiland 辅助点列和离焦夹具缺少新的质心权重信息：其回归比较同一共享锚点下的跨波长相对坐标，仍使用原 2e−8 光线坐标容差；绝对加权质心另由独立回归验证。旧夹具未改写，不再把其旧参考原点当作 Zemax 质心定义。

## 最终验证

| 检查 | 结果 |
| --- | --- |
| 主解决方案默认 Debug 构建 | 0 警告、0 错误；默认 App 输出已更新 |
| 主测试 | 1214/1214 通过，0 失败、0 跳过；包含 925 镜头预览回归 |
| 独立实验室默认 Debug 构建与全量测试 | 0 警告、0 错误；24/24 通过，0 跳过 |
| Python 对比工具测试 | 28/28 通过 |
| 主解决方案与实验室格式检查 | `dotnet format --no-restore --verify-no-changes` 均通过 |
| 依赖 | 主解决方案以本地缓存、锁文件和 `NuGetAudit=false` 还原；不将离线还原描述为在线漏洞审计通过 |

验证环境为 Windows / .NET SDK 10.0.300。此前 1207/1207 是补充最后七个回归之前的中间结果，当前以 1214/1214 为准。没有重新构建或安装 Windows Setup EXE，也未在本机运行 Linux/macOS 的 CI 任务。

本地证据位于 `artifacts/repair-2026-09-05/`，属于忽略的可重建运行产物。日志不作为源代码提交；本文和测试保存可复现结论。

## Zemax 对齐边界

权威仍为 `123456.ZMX / OpticStudio 2026 R1` 的固定文件和捕获设置，源 SHA-256 为 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。完整性检查通过：165 项中 148 项捕获、17 项不适用或失败，148 张图像含 106 张原生截图和 42 张标注的后备图像。完整性、当前重算、数值对比和 GUI 截图验收是四种不同证据。

赛德尔主基准四张表已端到端通过，旁轴主光线物方斜率约 +0.418772303751，像方斜率约 +0.085656480929，佩兹伐半径约 −376.28503335 mm。原始累计 S1/S2/S3/S4/S5 为 0.000625、0.000374、−0.000173、0.001830、0.076620；CL/CT 为 0.000002、−0.000233。以上只限定此文件和所选波长，不泛化为全部面型。

最终重算 72 项：69 项有效结果、1 项不可用、2 项不适用、0 项异常。30 个历史映射条目中，29 项取得数据，26 项通过高度一致门禁，3 项坐标范围不一致；独立 Distortion 已从目录退场，1 项明确未比较，另有 2 个非等价映射排除。赛德尔另有严格端到端测试，不计入该映射数量。

| 自动范围下未通过坐标门禁的分析 | 中位数值 NRMSE（仅交集） | 最低物理覆盖率 | 边界 |
| --- | ---: | ---: | --- |
| Encircled Energy | 0.3385% | 87.94% | 自动半径范围不同；交集内能量曲线误差小，但不足以验收完整捕获范围。 |
| Geometric Line Edge Spread | 3.0481% | 44.56% | 当前自动范围约 ±0.8912 µm，捕获为 ±2 µm；显式范围补充验证见下文。 |
| PSF | 1.0600% | 45.18% | 当前通用 FFT 网格与捕获的 FFT PSF 采样范围不同；显式像面间距补充验证见下文。 |

上述三项的自动范围仍与捕获输出不同，交集内低误差不足以宣称自动范围验收通过；同坐标数值结果由下面的独立补充验证判定。

光线光扇图在传递系统 Ray Aiming 后，最差系列 NRMSE 约 1.90e−8；其原约 26.09% 差异已解决。

[机器可读验证摘要](validation/PROJECT_REPAIR_2026-09-05.json) 随仓库提交，保留程序集指纹、测试数量和全部映射分类；完整本地报告位于 `artifacts/repair-2026-09-05/verified-recalculated/COMPARISON_REPORT.md`。

对比阈值为：物理坐标端点归一化误差不超过 5%，物理覆盖率至少 95%；高度一致需中位 NRMSE ≤ 3% 且最差系列 ≤ 10%，接近需中位 ≤ 10% 且最差 ≤ 25%。P90 仅描述分布。与旧报告只看中位/P90 的门禁不同，旧分类不能直接当作本轮结果。

本轮没有新的 OpticStudio/ZOS-API 捕获，也没有真实 Avalonia/Zemax GUI 图像一致性验收；报告页面是 JSON 的离线重绘。非球面三阶赛德尔贡献、近似膜层/散射、GRIN 和其他已明确披露的未实现能力不因本次回归通过而成为完整功能。

### 显式捕获坐标范围的补充验证

为区分自动取图范围与同一物理坐标上的计算差异，在相同镜头、波长、视场及采样数量下，只将三项输出范围显式设置为捕获数据的坐标元数据：

| 分析 | 显式参数 | 中位 NRMSE | 最差 NRMSE | 物理覆盖率 |
| --- | --- | ---: | ---: | ---: |
| Encircled Energy | `MaximumDistanceMicrometers=4.987373737373738` | 0.3387% | 0.3907% | 100.00% |
| Geometric Line Edge Spread | `MaximumRadiusMicrometers=2.0` | 1.4254% | 2.2047% | 100.00% |
| PSF | `ImageDeltaMicrometers=0.3946521566267988` | 1.1178% | 1.1178% | 99.21% |

参数来自捕获曲线的端点和网格 `dx`，没有按数值误差拟合。补充组 **29/29 可比映射高度一致，0 个数值/坐标门禁失败**；缺失的独立 Distortion 与两个非等价映射仍然不计为通过。这只验证所列显式设置，不证明两端自动绘图范围算法相同。PSF 映射按峰值归一化比较形状，不验收绝对辐照度或 Strehl。

补充运行重算 3 项，按源哈希、程序集指纹及全部设置检查复用其余 69 项；不是再次从头重算全部 72 项。原自动范围结果保持不变。可重建完整补充报告位于 `artifacts/repair-2026-09-05/explicit-range-recalculated/COMPARISON_REPORT.md`，参数保存于 [显式范围设置](validation/CAPTURED_RANGE_OVERRIDES_2026-09-05.json)，机器摘要也单独记录该组结果。

复现时，将上述三个参数覆盖到固定基准的 Workbench 设置清单对应 `analyses[].settings`，写入基准目录之外的新设置文件，再用本页采集/比较命令换用该文件和独立输出目录；保留基准镜头和其余设置。若从已有输出目录复用，采集器还会校验完整输入与代码指纹。

## 复现

```powershell
dotnet restore OptilandWorkbench.slnx --locked-mode
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
dotnet test labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx /m:1 /nr:false
python -m unittest discover -s tools/zemax_parity/tests
python tools/zemax_parity/verify_baseline.py artifacts/zemax/123456-zemax-2026-r1-baseline
dotnet run --project tools/OptilandWorkbench.AccuracyCapture -- artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json artifacts/repair-2026-09-05/verified-recalculated
python tools/zemax_parity/generate_workbench_comparison.py artifacts/zemax/123456-zemax-2026-r1-baseline artifacts/repair-2026-09-05/verified-recalculated artifacts/zemax/123456-zemax-2026-r1-baseline/comparison-reports/workbench-vs-zemax-2026-07-31
```
