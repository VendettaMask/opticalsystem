# Python Optiland 移除审计与实施记录

日期：2026-09-05。此阶段最初按用户要求仅作本地重构；用户于 2026-09-06 后续授权提交并同步到 `origin/main`。
最终验证与同步边界见 [Huygens 后续记录](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)，本页的历史捕获身份不改写。

本页保留移除阶段的历史验证。2026-09-06 后续数值错误修复及当前完整测试结果见 [数值修复记录](NUMERICAL_REPAIR_2026-09-06.md)；后续修复会有意改变受影响分析，不沿用本页移除阶段的数值不变断言。

## 修改前引用审计

审计起点为 `7a2d15e976b9a98dd173ce0749418adc357df9a3`，工作区无已有修改。先检查了 `src`、测试、工具、锁文件、解决方案、启动脚本、CI、README 和 docs，再实施删除。

- 未发现正式产品启动 Python、嵌入 Python.NET 或依赖 Optiland 包的计算后端。现有计算已经由 C# 实现；不能把本次描述成删除了一个实际运行的 Python 引擎。
- Core 的 `Serialization/PythonOptiland` 有 7 个专用读写、转换和 DTO 文件；`OpticJsonStore` 自动识别该字典格式。Application 的 `WorkbenchRuntime` 识别两种专用复合扩展名并分派保存；App 提供专用打开筛选器。
- Compatibility 项目只有继承 `WorkbenchRuntime` 的空壳 `OptilandConnector`，由测试引用。它没有独立计算能力，应连同项目引用移除。
- `EncircledEnergyAnalysis` 有仅被历史对照测试使用的 `optilandCompatibility` 参数和算法分支。删除该分支，保留正式产品的加权、多波长算法。辐射分析中名称带 Python 的采样函数实际上是 C# 六角环采样，仅调整名称。
- 测试混合了冻结数值参考和 Python JSON 往返验证；有限物距数值测试还借用生产 Python JSON 读取器构造模型。需要把输入转存为项目原生快照，断开这一依赖，再删除专用格式测试。
- `tools/python-reference` 是重新生成 Optiland 参考的离线脚本集合；构建和产品发布不需要它。删除生成器，保留有价值的已有数值参考并归入 `validation/history`，不再新增 Optiland 对照。
- Core 内嵌玻璃目录是独立静态材料数据，历史来源包含 Optiland 分发的 refractiveindex.info 数据；它不是运行时 Python 包，也不是测试夹具。保留原始字节以保证材料计算不变，删除重新通过 Optiland 生成的维护指引。
- `.NET` 数值后端、插件接口、玻璃文件库以及程序集、命名空间、资源键、设置路径中的 Optiland 历史名称不属于 Python 后端。
- `tools/zemax_parity` 中 Python 脚本用于产品之外的 Zemax 捕获和报告；CI 的 FreeCAD/Python 用于 STEP 外部验证，图标脚本用于离线素材维护。它们与 Python Optiland 无关，必须保持在产品依赖图之外。
- README、格式/兼容/架构/精度文档仍把 Python JSON 列为当前能力，完成计划还计划恢复导出。应删除这些能力和未来方向，统一为纯 C#/.NET 产品及 Zemax 2026 R1 主要外部精度基准。

## 已删除内容

| 范围 | 删除内容 |
| --- | --- |
| Core 格式 | `Serialization/PythonOptiland` 全部 7 个文件：Store、Reader 及其 Components、Writer 及其 Components、Conversion、Models；原生 `OpticJsonStore` 的外部字典自动探测 |
| Application / App | 专用格式识别 API、读写分派、文件类型和打开筛选器；不保留恢复开关或空壳注册。两种扩展名只出现在拒绝检查中，防止被通用 `.json` 接收 |
| Compatibility | `OptilandConnector`、空壳项目、锁文件、解决方案和测试项目引用；所有行为测试改用 `WorkbenchRuntime` |
| 专用算法 | `EncircledEnergyAnalysis` 的 `optilandCompatibility` 参数、分支及只服务于该分支的辅助函数；正式加权、多波长算法未改动 |
| 专用测试 | Python JSON 导入、导出、往返和失败契约；两份 Cooke/Tessar 专用字典夹具；旧兼容类型测试和 2 个历史 EE 分支用例，共减少 41 个用例 |
| 参考生成 | `tools/python-reference` 中全部 12 个生成器；顶点输入及已有数值资料迁入历史目录，不保留再生成工具 |
| 文档与计划 | `PYTHON_JSON_INTEROP.md`、`PYTHON_ANALYSIS_PARITY.md`、`PYTHON_PARITY_AUDIT.md` 及其当前能力/未来计划入口；完成计划中恢复导出和扩大 Python 互操作的工作包 |
| 冗余 | 删除专用测试留下的 JSON 比较/几何比较辅助函数；历史 ZMX 输入迁移后删除原位置的重复文件 |

审计未发现可删除的实际 Python 计算后端、运行时配置或 NuGet Python 依赖；本次没有虚构这些删除项。原有通用进程调用、原生 JSON 和其它格式基础设施均按用途核验。

## 保留内容与边界

- 保留 `INumericBackend`、`IBatchedNumericBackend`、`ManagedCpuBackend`、注册表及其 CPU/SIMD 实现；没有独立 `SimdCpuBackend` 类，SIMD 位于 ManagedCpuBackend 的批量路径。未来 GPU 后端仍可沿通用接口扩展。
- 保留光线追迹、分析、优化、公差、材料计算、STAROPT、ZMX、CODE V SEQ、OSLO LEN、通用顺序文本和项目自己的 JSON 快照。
- `validation/history/optiland-0.5.8` 保存原字节的 11 份数值 JSON、1 份 ZMX 和 1 份顶点文件。清单固定原路径和 SHA-256；1 个有限物距和 29 个组件输入仅转存为原生快照，没有重新计算外部数据。专用格式往返已删除，原有计算断言继续验证现有 C# 实现。
- 内嵌 `glass-catalog.json` 是独立的 refractiveindex.info 产品材料目录；另一份 `zemax-glass-catalogs.ogdb` 来自 AGF。两份资源保持原字节，不依赖历史夹具或外部 Python。C# 硬编码 Cooke/Tessar 示例处方继续保留，测试依赖产品示例，产品不读取测试参考。
- `tools/zemax_parity` 中 Python/pythonnet 只用于产品之外的 Zemax 捕获与报告；CI 的 FreeCAD Python 组件只用于 STEP 外部验收；图标脚本只用于离线维护。均不参与产品运行、发布内容或 src 依赖链。旧绘图缓存忽略规则用于避免误提交生成物，不提供再生成能力。
- 保留 `OptilandWorkbench` 程序集、命名空间、解决方案、目录、资源键、设置环境变量、原生玻璃库/插件接口、原生 `.optiland` 名称及 opaque 载荷标识。本次不做品牌/API 全局重命名。

每个仍包含 Python 或 Optiland 的文件，以及每处行号、列号和保留理由，见 [逐文件引用索引](RETAINED_PYTHON_OPTILAND_REFERENCES.md) 和 [完整机器清单](validation/RETAINED_PYTHON_OPTILAND_REFERENCES.json)。清单只排除自身的递归引用及忽略的生成物；历史捕获和明确标注日期的旧审计不改写为当前能力。

## 架构变化

```text
App → Application（Services / WorkbenchRuntime）→ Core
测试 → 产品程序集
测试 → validation/history（冻结、哈希验证）
外部 Zemax / STEP / 素材工具 → 离线验证或维护
```

Compatibility 已退出依赖图，App 仍不直接引用 Core。架构测试扫描产品源码、项目和包锁文件、打包与启动输入以及程序集引用/资源，阻止 Python 运行时、进程启动、pythonnet、已删除连接器、外部工具和测试资产回到产品路径。负向用例覆盖这些规则；普通光瞳坐标 `Py` 不属于进程启动器。

## 旧文件及源码兼容变化

1. `.optiland-python.json` 和 `.python-optiland.json`（大小写不敏感）在读取/保存前拒绝，旧文件不会被覆盖。改名为 `.json` 的旧字典也不满足原生快照模式，不能导入。没有保留迁移器；已有 STAROPT 或 Workbench 原生快照继续可用。
2. `PythonOptilandJsonStore` 及相关 DTO/转换器、`IsPythonOptilandJsonPath`、`OptilandConnector` 和 Compatibility 程序集已删除，旧源码引用需要迁移到 Runtime/Services 和原生格式。
3. `EncircledEnergyAnalysis` 不再接受 `optilandCompatibility` 参数；正式默认计算不变。
4. ZMX 格式标识从 `zemax-zmx-optiland-0.5.8` 改为 `zemax-zmx`。不可用探测器结果元数据键改为 `DetectorApertureRequirement`，对应中文标题改为“探测器孔径要求”。这些是元数据/显示契约变化，不是光学数值变化。

## 完整验证

2026-09-05 本次移除后的最终验证：

| 检查 | 结果 |
| --- | --- |
| 锁定依赖还原 | 主解决方案、独立实验室及 .NET 数值捕获工具通过；使用本地 NuGet 缓存和 `NuGetAudit=false`，未执行在线漏洞审计 |
| 完整主解决方案构建 | 默认 Debug 输出，0 警告、0 错误 |
| 完整主测试 | 1197/1197，0 失败、0 跳过；相对原 1214 个用例删除 41 个，新增架构/格式/历史完整性用例 24 个 |
| 独立实验室 | 完整解决方案构建 0 警告、0 错误；测试 24/24，0 跳过 |
| 外部 Zemax 报告工具 | unittest 28/28，通过；未调用 Optiland |
| 格式检查 | 主解决方案及实验室 `dotnet format --verify-no-changes --no-restore` 通过 |
| 差异检查 | `git diff --check` 通过 |
| 冻结历史数据 | 所有清单哈希通过；原生快照输入已纳入可审查文件，未被通用 `*.optic.json` 忽略规则遗漏 |
| 正式数值不变性 | 同一镜头、同一设置完整重算 72 项，1,786,839 个 JSON 数值精确相同，数值容差为 0；71 份完整结果相同，剩余 1 份只改变不可用探测器提示的两处标签 |
| Zemax 基准完整性 | 通过：165 项中 148 项捕获、17 项不适用或失败；106 张原生截图和 42 张明确标注的后备图 |

最初新增启动器守护曾误判字符串 `Py`，已收窄为进程启动语境并增加反例；表中 1197/1197 是修正后重新完整运行的结果，不是定向测试。验证日志在 `artifacts/python-removal-*.log`；主测试最终记录为 `artifacts/python-removal-tests/python-removal-main-verified.trx`。

没有执行新的 OpticStudio/ZOS-API 捕获、真实桌面 GUI 人工验收、安装包/跨平台发布或 FreeCAD 外部 STEP 作业；它们不计入本次构建和测试通过结论。

## Zemax 与数值结论

后续外部精度验证以已提交的 Zemax OpticStudio 2026 R1 基准为主要权威。本轮使用 `artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX`，SHA-256 为 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`。逐项采样和设置沿用基准目录中的 `comparison-reports/workbench-vs-zemax-2026-07-31/current-manifest.json`，SHA-256 为 `f3a96a8f6ba2b7aafcae619e0ef31aff4ce7fd1b006e5e4c6f8f8216a9065316`；单位沿用各分析的类型化轴元数据。没有修改镜头、设置、采样、材料常数或捕获文件，也不能将此设置称为通用 Zemax 默认值。

重算得到 69 项有效结果、1 项不可用、2 项不适用、0 项异常。与固定 Zemax 的 30 个映射中，29 项取得数据，26 项高度一致，3 项坐标范围不一致；1 项未比较，另排除 2 项定义不等价的映射。三项坐标范围差异是原有 Encircled Energy、Geometric Line Edge Spread 和 PSF 边界，本次不改变自动范围或以新默认值掩盖差异。

该外部比较沿用既有门槛：端点归一化误差 ≤5%、覆盖率 ≥95%；高度一致需中位 NRMSE ≤3% 且最差系列 ≤10%。这些报告门槛不是通用 Zemax 精度规格。与之独立的“本次重构数值不变”检查使用零容差，完整结果及输入/输出哈希见 [机器可读数值不变性证据](validation/PYTHON_REMOVAL_NUMERICAL_INVARIANCE_2026-09-05.json)。当前报告在 `artifacts/python-removal-recalculated/COMPARISON_REPORT.md`，其中 72 张页面图是离线重绘，不是 GUI 验收。

## 后续独立命名议题

可以另行评估 `OptilandWorkbench` 解决方案/程序集/命名空间/目录、`IOptilandPlugin` 和 `OptilandGlassCatalogStore`、资源键和设置目录/环境变量、启动脚本、硬编码示例名称，以及 `OptilandParityTests` 等历史测试类名。该议题需要资源、配置、插件、原生快照和外部源码迁移方案；不应借本次后端/格式删除隐式完成重命名。

本次仅提供本地可审查修改，没有提交、推送或创建 PR。

历史文件的冻结哈希使用源提交的原始 Git blob（LF），而不是旧 Windows 副本的 CRLF；已验证 13 个迁移输入与源提交逐字节相同，两个原生输入仅规范化外层换行且 JSON 值不变。这避免干净检出后的哈希误报，随后再次运行完整主测试。
