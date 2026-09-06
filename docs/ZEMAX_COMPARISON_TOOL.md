# 独立 C# Zemax 比较工具审计

以下为 2026-09-05 工具实现及首轮捕获的历史记录。当前实现已扩展为 52 项数值契约，并补充点列/图像接口能力审计；最新结果与未解决项见 [MS-L7 全分析扩展](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md)。此前 [十项数值修复与复验](NUMERICAL_REPAIR_2026-09-06.md) 和首轮生成物保留，不覆盖历史失败证据。当时仅保留本地，2026-09-06 后续已授权提交同步，见 [最新记录](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。

## 修改前引用与执行路径审计

- 已重新阅读根 `AGENTS.md`。正式产品仍为纯 C#/.NET；既有历史数据及已提交 Zemax 基准不改写。
- `tools/zemax_parity/zosapi_capture_baseline.py` 提供 `AnalysisIDM` 枚举、原始 DataSeries/DataGrid、CFG、文本和原生 ZPL 截图流程。新工具用 C# 类型化 ZOS-API 适配器复用这些契约；不运行旧脚本。
- `generate_workbench_comparison.py` 的既有 30 组映射和坐标门禁已审查。若干转换只适用于固定 `123456.ZMX`，例如 4.5 mm 视场范围，不可用于任意镜头。相同物理单位换算、T/S 顺序、偶数波前网格和非等价参考球排除原则予以保留；没有继承基于旧中文系列名的映射。
- `tools/OptilandWorkbench.AccuracyCapture` 已通过 `WorkbenchRuntime.BuildAnalysisView` 重算真实结果，但 UI 行文本经过格式化。新增 `BuildAnalysisData` 复用同一私有分析工厂，直接返回未舍入的 `AnalysisData`；`BuildAnalysisView` 也调用此入口，没有另一套构造器或默认值。
- Core `AnalysisCatalog` 为 72 项；Application 规范描述符还有用于展示的别名。注册表只接受 Core 的稳定键，并检查每项的 Application 描述符。原生 `AnalysisIDM` 无对应项通过同一注册表的 `NativeOnly` 枚举。
- 已检查 `artifacts/zemax`、精度验证、基准配置边界、分析参考文档，以及现有 Huygens PSF、波前和其它 Zemax parity/golden 测试。没有删除或覆盖旧捕获资料。
- 安装的 ZOS-API 引用了 .NET Framework/WPF/remoting。捕获由本地编译的独立 C# Framework 进程执行，主工具为 .NET 10。生产 `src` 不引用该工具、Framework host 或 ZOS-API。

## 已实施边界

新增工具、独立离线测试项目和 PowerShell 入口；完整目录/CLI/配置/运行/报告说明见 [工具 README](../tools/OptilandWorkbench.ZemaxComparison/README.md)。

首轮注册表审计全部 72 项，当时有 10 个数值适配器、56 个待适配候选和 6 个无验证等价项。该数量是历史状态，不代表当前覆盖。`AdapterNotImplemented` 用于区分“本工具尚未实现”和“ZOS-API 本身不支持”；后续新增 `PhysicalDefinitionMismatch` 标记现有两侧物理定义不同，均不得算作数值通过。

所有结果带输入与配置哈希、实际软件版本、采样、单位和请求指纹。默认值属于工具的 CapturedSettings，结论只适用于本次镜头与这些设置。数值比较、不可比较、未实现、许可证/API 错误和原生截图各自计数。未实现的 Zernike/复杂场/图像适配器不会因为已有结果模型类而被标记完成。

2026-09-05 工具实现阶段的生产依赖变化仅为 Application 公开原有分析工厂的未舍入输出；当时 Core 光线追迹、优化、公差、材料和分析算法未改变。工具依赖 Application/Core 与独立 SkiaSharp 绘图包，依赖方向始终是 `tools -> src`。

## 实测中确认的接口细节

- 本机 2026 R1 的 `IAR_SpotDataResultMatrix` RMS/GEO 输出在 MM 镜头下已经是微米，与原生文本一致，不能再次乘 1000。
- 单色扇形数据保留其它波长的空列；空列保持缺失，不冒充零值。T/S 的原生列/面板顺序依据明确输出契约，中文标题不参与调度。
- Workbench Pupil Aberration 的现有工厂输出全部视场/波长；工具按稳定索引提取选定组合，不篡改处方或复制算法。
- `IA_.ToFile` 导出文本。原生截图复用 ZPL 窗口导出，并补上显式 CFG 参数；实测 MTF JPEG 已生成、校验并人工查看。
- Workbench 波前显示减去了自身最小值。规范化通过其原始光程差均值与波长恢复有符号 OPD，记录转换量，不根据 Zemax 数值拟合 piston。
- 首轮 FFT PSF 存在物理像素原点不同的情况，当时保留双侧网格并标记 Incomparable。后续修复产品采样和坐标后重新比较；工具仍禁止拟合平移或重新归一化。

## 验证记录

锁定依赖还原和默认 Debug 完整解决方案构建通过，`0` 警告、`0` 错误；完整解决方案测试为主测试 `1197/1197` 加工具 `45/45`，零失败、零跳过。最后的 CLI/配置拒绝边界和独立保存路径修正后，再次构建完整解决方案并运行工具 `45/45`。最后这次是定向复验，没有将其描述为完整测试。完整测试日志在 `artifacts/zemax-comparison-dev/final-full-tests.log`，最新定向日志为 `final-tool-tests.log`。

工具测试覆盖 CLI、配置、完整注册表、单位、插值、矩阵方向与掩码、误差指标、报告、防覆盖、哈希、冻结 Zemax 原始夹具、取消/超时隔离及生产反向依赖检查。CI 的三个主测试平台已加入该测试项目，不启动 OpticStudio。依赖以本机缓存执行 `--locked-mode --force -p:NuGetAudit=false`，本次未执行在线漏洞审计。

已在本机 Enterprise API 许可证下使用仓库 `123456.ZMX` 副本完成六项真实集成，并扩展为十个适配器探测。它们是开发验证，不是用户指定的 MS-L7 首轮，也不是旧基准的替换。开发报告在 `artifacts/zemax-comparison-dev`，不提交生成物。

首轮记录中的短路径 `C:\Users\19851\Desktop\MS-L7.ZMX` 当时不存在，因此实际使用的 `[MS-L7](10X大NA大视场).ZMX` 最初标记为候选。用户已于 2026-09-06 明确指定使用这个完整文件名；重新核对 SHA-256 为 `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`，与最终全分析报告及仓库同名镜头一致，输入身份现已确认。旧捕获路径和失败记录保持原样；当前结果见 [修复后报告](NUMERICAL_REPAIR_2026-09-06.md)。

单项失败集成：`artifacts/zemax-comparison-dev/single-failure-integration` 在候选镜头上先完成 First Order，再以显式不存在的视场拒绝 MTF；返回 `2`，先前双侧原始/规范化数据、指标、报告及源哈希检查均保留。Ctrl+C 集成：`artifacts/zemax-comparison-dev/ctrl-c-exit-code` 使用固定 `123456.ZMX`，在一阶捕获完成后取消 Huygens PSF；报告记录退出码 `4`、已完成的一阶结果及未变的源文件。通过本次 PTY/PowerShell 宿主发送 Ctrl+C 时宿主返回 `1`，因此不将它描述为外部 shell 返回 `4` 的成功验收；工具自身取消分支和返回码计算另由离线测试覆盖。

`--overwrite` 现在要求源/配置哈希相同，且在获得独占运行锁后把此前生成的整套证据移动到 `previous-run-<UTC>`，保留无关文件；子集重跑不会展示旧图。两侧规范化文件独立保存，即使另一侧计算失败也保留成功一侧。普通数值重绘 PNG 与原生 MTF JPEG 均已查看，原生文本窗口未生成截图时如实记为不可用。

## 新增文件与依赖

- `tools/OptilandWorkbench.ZemaxComparison`：项目/锁文件、CLI、注册表、请求与结果模型、配置、主运行器、Workbench 执行器、Framework ZOS host、进程/截图隔离、规范化、指标、绘图、报告及 README。
- `tests/OptilandWorkbench.ZemaxComparison.Tests`：独立项目、锁文件与 45 个离线用例；链接既有固定 Huygens PSF 原始夹具，没有复制或替换旧基准。
- `scripts/compare-zemax.ps1`：环境检查、锁定构建和参数转发。
- `tools/OptilandWorkbench.AccuracyCapture/README.md`、本审计和验证清单：说明旧入口复用及新工具边界。
- 修改解决方案与 CI 以构建/测试独立工具；产品 Application 仅公开原工厂的 `BuildAnalysisData`。验证方向为 `ZemaxComparison -> Application -> Core`，正式产品不反向引用 ZOS-API 或工具资产。

## 2026-09-05 候选 MS-L7 结果（历史）

完整报告：[COMPARISON_REPORT.md](../artifacts/zemax-comparisons/ms-l7-candidate-final-2026-09-05/COMPARISON_REPORT.md)。报告目录为 `artifacts/zemax-comparisons/ms-l7-candidate-final-2026-09-05`，生成物不提交。可提交的 [验证清单](validation/ZEMAX_COMPARISON_RUN_2026-09-05.json) 包含完整 72 项映射、新增文件清单、构建/测试证据、源/配置/工具/报告哈希和保留限制。

实际软件为 OpticStudio 2026 R1，API 版本 `260127`、SP0、有效 EnterpriseEdition 许可证。输入为 17 面、1 个配置的顺序镜头。最终工具 Release 程序集哈希为 `a7d43198ee670515092ff48a4bf21abd31ab12977225aa7a53a889773027b4dd`；配置哈希为 `5e648bf1f14ff2daeea7834089469648cd89b6d52bfca8fc54bc4e861bd315af`。源文件及副本完整性均通过。

| 指标 | 数量 |
| --- | ---: |
| 总矩阵 | 177：72 个规范项 + 105 个原生独有枚举项 |
| Workbench 有效捕获 | 61；另有 1 个不可用输出仍保存原始/规范化结构 |
| Zemax 数值捕获 | 10，原始与规范化文件全部保存 |
| 可比较 | 7 |
| Pass / Close / Difference | 6 / 0 / 1 |
| Incomparable / Skipped / Error | 54 / 108 / 8 |
| 原生截图 | 9；First Order 文本窗口没有生成图像，单独记录不可用 |

退出码为 `2`，原因是 8 项 Workbench 异常，不是工具成功退出后声称一致。它们均在工作 F 数路径报告边缘光线未到达像面：Encircled Energy、Fourier MTF vs Field、Huygens PSF、Huygens PSF Cross Section、MTF、Contrast Loss Map、Partially Coherent Image Analysis、Extended Diffraction Image Analysis。按本次不得改变算法的边界保留异常，不在报告层修补。FFT PSF 为物理像素原点不一致，保留网格并判为 Incomparable。

只有 7 项有可排名的数值；不能为凑足十项给失败或不可比较项编造误差。以下按每项最大 NRMSE 排序，NRMSE 定义及各物理量容差见报告：

| 分析 | 状态 | 最大 NRMSE |
| --- | --- | ---: |
| Spot Diagram | Difference | 0.06775840233 |
| Huygens MTF | Pass | 0.002781379552 |
| Pupil Aberration | Pass | 0.001249565132 |
| Optical Path Difference | Pass | 8.511590969e-7 |
| Wavefront | Pass | 7.269620321e-7 |
| Ray Fan | Pass | 2.935602430e-9 |
| First Order | Pass | 1.162763721e-16 |

点列 RMS 半径：Workbench `0.7977275282012398 µm`，Zemax `0.7471048941977483 µm`，绝对差 `0.05062263400349154 µm`，相对差约 `6.776%`。这是本次设置下的实际差异，不推广到其它视场/波长或镜头。

实际复跑命令（每次省略 `--output` 自动生成新目录）：

```powershell
dotnet run -c Release --project tools/OptilandWorkbench.ZemaxComparison -- --input "C:\Users\19851\Desktop\[MS-L7](10X大NA大视场).ZMX" --all --capture-screenshots --keep-raw
scripts/compare-zemax.ps1 -InputFile "D:\Optics\AnotherLens.ZMX" -OutputDirectory "D:\Optics\comparison-results" -Configuration "tools\OptilandWorkbench.ZemaxComparison\comparison-settings.json"
```

锁定还原、完整构建、完整测试及格式验证已经执行；最后 `git diff --check` 通过。旧实验室和旧外部报告工具的同轮验证记录单独保留，不冒充本次新工具的许可证集成测试。

## 官方接口依据

原生分析和结果读取使用 [ZOS-API 分析执行说明](https://optics.ansys.com/hc/en-us/articles/42661767157907-Basic-method-of-performing-system-analysis-in-ZOS-API)。参考球与 Remove Tilt 的含义见 [Wavefront Map](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html)。显式截图设置依据 [OPENANALYSISWINDOW 的 CFG 参数](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/OPENANALYSISWINDOW.html)。这些官方定义和本机 2026 R1 实测记录分开列示，旧版本文档不冒充本机版本的验证结果。
