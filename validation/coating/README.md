# 镀膜实验室验收记录 · 2026-09-28

2026-10-05 同步复验：正式及镀膜默认 Debug/Release 构建零警告、零错误；最终累计 Release **3500/3500**，装调/玻璃库相邻双配置各 **25/25**，镀膜完整双配置各 **44/44**。累计 Debug 的 3500 项保留 2026-10-04 记录；不相加各测试集合，也不表示全仓或跨平台发布验收。见 [同步范围与证据](../../docs/PROJECT_SYNC_2026-10-05.md)。以下保留各功能阶段的实现日期和验证范围。

## 2026-10-02 共享求解器与正式追迹连接

`coherent-scattering/2` 保持既有功率接口，新增复振幅。最终默认 Debug/Release 镀膜实验室物理与工作流各 **25/25**，包含冻结的 120 组 tmmcore 0.4.2 功率对照；参考文件逐字节等于 HEAD，没有重新生成。正式主程序阶段各 169/169，最终严格快照定向各 42/42，见[本轮记录](../../docs/COHERENT_COATING_TRANSPORT_2026-10-02.md)及[机器摘要](../../artifacts/validation/coherent-coating-transport-20261002/verification.json)。这不是重跑实验室完整 44 项、桌面走查或 Zemax 镀膜等价验收；实验室 UI 应用桥接仍未完成。

下方 2026-09-28 与更早结果是历史阶段范围，保留当时的源码哈希和未完成清单。


## 当前日常设计增强验收

[2026-09-28 机器摘要](verification-20260928.json)记录本轮源码/证据哈希、测试与示例。原 2026-09-27 摘要保持冻结，已不能拿其源码哈希核对新增实现。

| 范围 | 实际结果 | 覆盖边界 |
| --- | --- | --- |
| 镀膜完整 Release / Debug | 各 **44/44**，零失败/跳过 | 包含原物理/取消/过期结果/文件用例，以及材料表、旧格式哈希、双/三腔、多起点固定层、相关公差逐次重算、五主题、材料对话框和最小窗口 |
| 正式相关 Release | **226/226**，零失败/跳过 | 原入口、材料、优化、分析契约，加共享主题资源/运行时/按钮；默认 App 输出已更新，非正式全量 |
| 固定上游与物理 | 原 120 组 tmmcore 对照和独立解析测试均通过 | 共享 Core 求解没有变动；不新增 Zemax 或 Optiland 历史比较 |
| 日常三例 | **3/3** 保存重开光谱逐项一致 | 6 起点、种子 1234、每起点最多 80 迭代；真实 k 不变，双腔例另存 |
| Headless + Skia | 五种主题选择、1200×900 / 820×900 / 820×640 通过 | 实际鼠标/控件、材料表编辑、导出文件和渲染；最小窗口使用滚动，不缩字体 |
| 原生 macOS | Release 原生窗口生成、优化、保存、切换示例、原生选择器重开复算通过 | AR 最大 R 1.098132%；另走查主题切换和材料窗口。不是跨平台或安装包验收 |

本轮发现并修复显示问题：暗夜绘图区轴字与背景未成对使用共享 Plot 资源；窄窗口滚动条覆盖控件。多次桌面测试还暴露出静态可变曲线画刷跨 Dispatcher 所属线程的问题，现使用不可变画刷，保留相同物理曲线颜色。最终 PNG 在 `artifacts/validation/coating-20260928/`，包含 `theme-0-820.png` 至 `theme-4-820.png`、`ar-820-640.png`、`narrow-820-640.png`、`material-editor.png`。原生截图/AX 操作记录在任务工具输出中，原生保存文件为该目录 `native-verified.coating.json`。不要把 Headless PNG 称为原生截图。

原生初次 shell 启动受沙箱限制（Avalonia RenderTimer -6661），旧脚本 app bundle 的 CUA 定位也超时；使用默认 Release 构建的真实 apphost 放入临时 `.app` 后完成走查。临时包不是签名/公证发布包。测试宿主需要本机通信权限，已在允许范围内完成默认输出验证，没有以备用输出冒充完成。末轮还遇到并行评价函数代码的临时缺失方法引用；保留该任务改动，待工作区修复后重新执行默认构建。

### 日常示例的真实差距

- AR：8 层，最大 R **0.090309738%**，达标；550 次局部优化器函数评价。
- HR：24 层，最低 R **95.739782353%**，未达到 98%；14419 次评价。
- 双腔 NBF：17 层，峰值 **549.959761706 nm**、FWHM **19.961338111 nm**、峰值 T **69.299966785%**；左右 OD **0.949596873 / 1.084292371**。中心与右截止达标；FWHM、峰值 T 和左截止未达标；9934 次评价。

评价次数为累计局部优化器函数评价，模板排序与密集单波长求解另行发生。三例均采样收敛。每例另有 5 次正态 1σ=1% 同材料厚度扰动、1σ=0.1° 角度扰动的公差演示；仅演示流程，不宣称生产良率置信度。三例公差演示分别为 5/5、0/5、0/5 通过。完整结果见 [daily/verification.json](../../labs/CoatingDesign/examples/daily/verification.json) 和各例 `tolerance.json`。

### 本轮命令

```sh
dotnet test labs/CoatingDesign/tests/OptilandWorkbench.CoatingDesign.Tests/OptilandWorkbench.CoatingDesign.Tests.csproj -c Release --no-restore /m:1 /nr:false
dotnet test labs/CoatingDesign/tests/OptilandWorkbench.CoatingDesign.Tests/OptilandWorkbench.CoatingDesign.Tests.csproj -c Debug --no-restore /m:1 /nr:false
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App -c Release --no-build -- --batch-daily-examples artifacts/coating-daily-examples
```

正式 226 项筛选包含 `CoatingDesignLabLauncherTests`、`InitialStructureLabLauncherTests`、`OptilandParityTests`（既有 C# 契约）、`MaterialAnalysisTests`、`DielectricGlassMaterialTests`、`OptimizationRouteTests`、`AnalysisGuiContractTests`、`LensMaterialRowThemeTests`、`ThemeResourceTests`、`ThemeRuntimeTests`、`StandardActionButtonThemeTests`。TRX 均在 `artifacts/validation/coating-20260928/`。

未覆盖：Windows/Linux 原生窗口、签名安装包、正式全量；各向异性、非相干基板背面、粗糙散射、n/k 制造误差与补偿、任意目标多腔解析综合、针插入、完整 TFStudio 文件兼容、正式追迹桥接和生产控制。有限结构搜索并不证明未达标目标物理无解。

## 2026-09-27 冻结历史

以下保留旧版数值与验收过程，**其中 29/29、198/198、Light 限制、原生未完成和多腔未实现等只描述当时版本**；当前能力以上文为准。

[机器验收摘要与源码哈希](verification-20260927.json)由最终 TRX 和示例输出生成。

本目录记录新增镀膜功能的验证，不替代正式主测试全量、初始结构实验室或 Zemax 外部验收基线。测试通过与设计指标达标分别记录。实现和启动见 [实验室 README](../../labs/CoatingDesign/README.md)。

## 来源与可复现输入

- TFStudio 固定提交 `8b942b3f956728a23cea2b35023c8b5d8de17428`（package 1.8.1），锁文件实际引用 tmmcore 0.4.2。
- 归档、哈希、MIT 原文和 CC0 材料原文件见 [来源说明](../../third_party/coating-reference/README.md)。产品不加载上游应用或 JavaScript。
- `generate-reference.mjs` 直接调用固定 tmmcore 包的 JavaScript 实现，生成 [120 组冻结参考](tmmcore-0.4.2.json)。涵盖裸界面、单层减反、周期高反、吸收和厚膜，4 个角度、S/P、3 个波长。
- `import-materials.mjs` 将固定 SiO₂/Ta₂O₅ YAML 的全部 n/k 行转为材料表，仅把 μm 转 nm。重新生成与原嵌入表逐值相等。

```sh
mkdir -p .tmp/coating-upstream/tmmcore
tar -xzf third_party/coating-reference/tmmcore-0.4.2.tgz -C .tmp/coating-upstream/tmmcore
node validation/coating/generate-reference.mjs
node validation/coating/import-materials.mjs
```

Node 只用于维护固定参考文件和材料数据；正常构建、运行和 C# 测试不需要 Node。

## 已执行验证

| 范围 | 结果 | 含义与边界 |
| --- | --- | --- |
| 镀膜实验室完整 Release | **29/29**，零失败/跳过 | 物理、工作流、持久化、Avalonia 控件测试 |
| 镀膜实验室完整 Debug | **29/29**，零失败/跳过 | 同一测试集，不与 Release 累加 |
| 正式产品相关 Release 回归 | **198/198**，零失败/跳过 | 两种实验室入口、材料、OptilandParityTests 既有 C# 契约、优化入口、分析 UI 契约及材料行主题；不是正式全量 |
| 三例完整优化与保存重开 | **3/3 光谱逐项一致** | 每例真实计算和独立复验；只有减反例全部指标达标 |
| 上游移植一致性 | **120/120** | 每个 R、T、A 与固定 tmmcore 的绝对误差均 <2×10⁻¹¹；包含在上述一项测试中 |
| Avalonia 交互与渲染 | 1200/820 DIP 两种宽度通过 | 真实控件鼠标输入、膜层操作、计算/优化、输入失效、切换示例、保存/打开/导出；Headless+Skia 渲染 |
| 原生 macOS 自动化 | **未完成** | 独立 .NET 桌面进程成功启动；CUA 原生应用检查服务超时，不能以此宣称原生截图/交互通过 |

独立物理检查包括 Fresnel 裸界面、Brewster P 反射为零、理想四分之一波单层减反、周期高反解析解、零厚度恒等、S/P 与非偏振平均、无吸收 R+T、吸收基板与不透明膜极限、全反射及无效输入拒绝。稳定性测试包含 **2000 层**和 **100000 nm 吸收膜**；T 下溢时 ln(T) 仍为有限值，OD 不由显示截断制造。

窄带案例另用 **24001 点**独立密集网格校核，FWHM 差 ≤0.002 nm、峰值 T 差 ≤10⁻⁶。此为采样收敛检查，物理正确性另外使用解析解验证。验证并不声称任意极窄/多峰结构均已覆盖。

工作流验证固定膜层、厚度范围、无法满足的目标、实际算法版本/停止原因、优化中取消、忽略取消的旧任务被代次拒绝、输入材料数组隔离、损坏/过期实验和缺失材料系数拒绝、取消保存不破坏旧文件、公差固定种子重现。GUI 检查还发现并修复 Avalonia 延迟 TextChanged 引起的旧结果风险，输入变化现同步失效。

## 示例实际结果

| 示例 | 结果 | 运行记录 |
| --- | --- | --- |
| 减反 | 2 层，500–600 nm 最大 R 1.098131941%，目标 ≤2%，达标 | DLS `/2`，梯度阈值停止，6 迭代、26 次评价；801 点复验 |
| 高反 | 32 层，520–580 nm 最低 R 95.683242128%，目标 ≥98%，未达标 | DLS `/2`，80 次迭代上限、2683 次评价；801 点复验 |
| 窄带 | 11 层，峰值 549.970735786 nm；FWHM 27.220207772 nm；峰值 T 72.694745358%；右截止最低 OD 0.949845746，未全部达标 | DLS `/2`，80 次迭代上限、1001 次评价；1297 点复验 |

原目标、材料 k、约束和停止原因全部保留。窄带中心位置与左截止 OD 达标，带宽、峰值透过率和右截止 OD 未达标；没有将停止当作收敛验收，也不能由本次局部搜索失败推断物理无解。完整精度记录位于 [examples/verification.json](../../labs/CoatingDesign/examples/verification.json)。

## 复跑命令

```sh
dotnet restore labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx --locked-mode
dotnet build OptilandWorkbench.slnx -c Release --no-restore /m:1 /nr:false
dotnet build labs/CoatingDesign/OptilandWorkbench.CoatingDesign.slnx -c Release --no-restore /m:1 /nr:false
dotnet test labs/CoatingDesign/tests/OptilandWorkbench.CoatingDesign.Tests/OptilandWorkbench.CoatingDesign.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=coating-final-release.trx'
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~CoatingDesignLabLauncherTests|FullyQualifiedName~InitialStructureLabLauncherTests|FullyQualifiedName~OptilandParityTests|FullyQualifiedName~MaterialAnalysisTests|FullyQualifiedName~DielectricGlassMaterialTests|FullyQualifiedName~OptimizationRouteTests|FullyQualifiedName~AnalysisGuiContractTests|FullyQualifiedName~LensMaterialRowThemeTests' --logger 'trx;LogFileName=coating-formal-regression.trx'
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App -c Release --no-build -- --batch-examples artifacts/coating-examples
```

Debug 使用相同实验室命令替换配置。要保存实际渲染截图，在实验室测试前设置 `COATING_UI_EVIDENCE` 为证据目录。TRX 位于对应测试项目 `TestResults`；本机截图位于 `artifacts/validation/coating-20260927`，属于可再生证据。

本次 NuGet 依赖由本地缓存锁定还原，离线还原使用 `-p:NuGetAudit=false`，未执行在线漏洞审计。锁定还原、VSTest 与格式工具需要本地套接字/命名管道，受限沙箱下先失败、获执行权限后重跑成功；一次 Debug 测试在构建尚未完成时没有加载程序集，已等待构建完成后重跑 29/29。独立解决方案现显式列出共享 Core/Application，避免 Release 误映射到 Debug 引用。主程序和镀膜实验室默认 Debug/Release 输出构建均为零警告、零错误；本次新增 C# 文件格式和差异检查通过。

未执行 Windows/Linux 原生验收、安装包验收、正式全量、初始结构实验室全量、新 Zemax 捕获或新的 Optiland 历史比较。本次不会覆盖仓库其他任务报告的既有全量失败或发布门禁。非相干背面、各向异性、粗糙散射、多腔综合、生产控制和主程序光线追迹集成仍是后续工作。

## 文档同步复核

2026-09-27 后续“更新所有文档”仅同步说明并只读核对既有证据：机器摘要中的 18 个源码 SHA-256 与当时文件一致，镀膜 Debug/Release 两份 TRX 各 29 通过，正式相关 TRX 为 198 通过；三份示例继续为减反达标、高反/窄带未全部达标且保存重开一致。未重跑构建/测试、未替换参考数据或改变任何目标。当前能力导航见[文档索引](../../docs/README.md)及[状态范围](../../docs/CURRENT_STATUS.md)。

文档校验：本次同步的 43 份相关文档中，1062 个本地文件/目录链接均可解析；差异空白检查通过。历史数值报告和上游许可证原文保持不变。

## Git 同步前复验

2026-09-27 再次完成独立镀膜解决方案默认 Release 构建（零警告、零错误）与完整 29/29 回归（零失败、零跳过）。这是已有用例复验，不新增物理或像质验收范围；本次未重新生成三例或上游数据。记录见 `artifacts/validation/project-sync-20260927/sync-coating-release.trx`。

本轮文档核对：17 份相关文档的 937 个本地链接可解析；源码及证据哈希在机器摘要中。材料管理窗口继承主窗口的字体、字号与字形，额外以 16 DIP 字号的真实控件交互检查。

原生走查使用的临时包二进制哈希单独记录；其后的材料窗口字体继承和不可变画刷修正由最终 Debug/Release 控件测试覆盖，不将两种证据混为一次原生全量验收。
