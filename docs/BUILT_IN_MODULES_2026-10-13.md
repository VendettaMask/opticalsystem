# 初始结构与镀膜设计：统一产品交付（2026-10-13）

## 固定产品约束

按用户要求，两个实验室不是独立软件，而是 Optical System Design 的必装内置模块。标准安装一次包含全部功能，不提供模块勾选、不要求补装、不提示寻找外部程序。独立进程方案已撤销，不属于可保留架构。

主程序通过“实验室 → AI 初始结构 / 光学镀膜设计”创建同进程、同程序集的受主窗口管理的工作区。重复打开复用原窗口；退出主程序统一等待/关闭。未保存工程的保存、放弃、取消保护保留，这不是模块安装提示。

## 实现与维护入口

- UI 已移到 `src/OptilandWorkbench.App/Laboratories/InitialStructure` 与 `CoatingDesign`，使用主程序主题、字体、进度状态与可用性说明。
- `IntegratedLaboratories` 管理窗口；`ProductModules` 在启动和安装验证时确认两个内置窗口及五个内部模块库。缺失模块必须受控退出，不能回退搜索开发机目录。
- `src/OptilandWorkbench.Modules` 是内部类库，转换共享 Core 结果到 Application 的场景/曲线 DTO；App 仍不直接引用 Core。数值公式、搜索算法和冻结目标没有改变。
- `labs/` 只保留领域库、持久化、测试、基准工具和历史证据。原两个 App 项目、独立解决方案、启动器和 `Run-CoatingDesign` 脚本已移除，历史可从 Git 恢复；用户文件没有删除。
- 根解决方案统一构建 19 个项目。CI 使用根解决方案还原、构建、格式检查和依赖审计，并运行两个内置模块的测试；不再维护独立产品构建。

安装与修复必需的内部类库：`OptilandWorkbench.Modules.dll`、`OptilandWorkbench.InitialStructure.Contracts.dll`、`OptilandWorkbench.InitialStructure.Engine.dll`、`OptilandWorkbench.InitialStructure.Persistence.dll`、`OptilandWorkbench.CoatingDesign.Engine.dll`。镀膜的两份 MIT 许可及材料来源声明随主程序发布到 `Licenses/`。

```powershell
dotnet build OptilandWorkbench.slnx -c Release
dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release
./scripts/build-installer.ps1
```

标准 Windows 包自带 .NET；终端命令仅用于开发和打包，不要求普通安装用户安装 SDK。`publish-windows.ps1` 验证必需文件并禁止旧模块 EXE；安装清单覆盖所有模块，因此升级、修复和卸载共用既有事务与用户数据保护。跨平台发布脚本同样验证模块类库，但这不等于跨平台实机验收。

镀膜示例批处理并入唯一主程序：`--batch-coating-examples <目录>` 与 `--batch-coating-daily-examples <目录>`。初始结构记录仍使用原用户目录，镀膜实验仍保持原 `.coating.json` 格式和编辑状态，不隐式改写当前镜头。

## 验证范围

本次以默认输出构建，根解决方案 Debug/Release 均为零警告、零错误，锁定还原通过；本次迁移/新增文件的空白格式检查和 `git diff --check` 通过。保留失败尝试及后续修正的 TRX。模块集成回归、原生发布包检查、真实隔离安装检查分别计数，不能相加为“全仓通过”。

已验证：主程序集成/架构/安装相关 Debug/Release 各 **100/100**；初始结构界面/显示/契约相关各 **38/38**；镀膜完整两配置各 **54/54**（新增 7 个关闭保存保护案例）；最终标准发布包 **12/12**（**1187** 个清单文件逐个哈希一致，五个模块缺失均受控返回 `75`）；同代码 x64 隔离真实安装矩阵 **82/82**，包括五个模块逐项修复和卸载后的用户工程/设置保留。以上最终集合零失败、零跳过。原生测试安装已卸载，测试工程与原始日志保留。冻结工作区及 Git 原始证据 **2668/2668** 保持不变。

图形复核覆盖浅色/深色镀膜、宽窗口和初始结构窄窗口。自动主题检查同时验证四个具体主题的实际语义资源，以及 Headless 下“跟随系统”未解析资源时的共享可见回退；坐标前景必须不透明且不同于背景。首次把系统模式当作总有具体资源字典的断言失败记录保留，不属于光学误差，未跳过该主题。

### 最终标准安装包

[OpticalSystemDesign-1.0.3-win-x64-Unsigned-Setup.exe](../artifacts/installers/installer-win-x64-4f1511f0/OpticalSystemDesign-1.0.3-win-x64-Unsigned-Setup.exe)

- 发布身份：`1.0.3-win-x64-332991feffd8`，大小 `274571666` 字节。
- SHA-256：`7f39c67441fecaac59f39824b951e675082866ba82d9ae9ab531f2b18bbf7e58`。
- 非测试标准包，默认未签名，不含故障注入开关；一次安装全部模块。
- 实际安装矩阵使用隔离测试发布 `1.0.3-win-x64-7c21a88be8d5`；主程序与五个模块 DLL 和上述标准包逐项哈希一致，只隔离产品标识及测试开关。
- 此前本轮中间包 `fb4eeb19`、`8540c875` 保留为修正过程证据，不是推荐交付版本。旧正式 1.0.2 包没有被覆盖，用户现有正式安装没有被替换；本轮未提交或推送。

包检查记录：`.tmp/module-package-verified-final/`；安装记录：`.tmp/module-installation-verified-final/`；机器清单见[交付证据账本](validation/BUILT_IN_MODULES_2026-10-13.json)。

证据目录：`artifacts/validation/integrated-product-20261013/`。原生包检查使用 `scripts/verify-built-in-package.ps1` 的可丢弃副本，不改发布原件；实际安装使用独立 `OpticalSystemDesign-UpgradeValidation-*` 标识和仓库测试目录，不覆盖用户正式安装。

回归包含同进程受控窗口、共享 Core 场景逐值一致、主题坐标文字/背景可见、重复打开/关闭、保存取消/失败/保存期间继续编辑、缺失模块的受控退出，以及五个模块逐项同包修复。首轮曾发现旧 UI 不符合主程序分层/字体/进度/颜色规范、源契约尚指向旧转换位置、适配库缺失导致提前 JIT 崩溃及主题资源漏传当前主题；均保留初次证据并修复，没有放松光学容差或架构要求。

## 不冒充已解决的全量问题

本次交付集成不宣称整个项目达到完整发布门禁。集成前 2026-10-13 全仓审计的原始证据在 `artifacts/validation/product-audit-20261013/`：

- 主测试完整 Release：4809 通过 / 1 失败 / 4810；失败为 `AuditReliabilityFixTests.OptimizationPreparationDoesNotBlockReadsAndRejectsConcurrentEdits(method: "quick")` 的 5 秒启动等待，相关隔离重跑 3/3 通过，尚未据此关闭并发稳定性问题。
- 初始结构完整 Release：258 通过 / 2 失败 / 260；`JointGeometryRestartTests` 的 `trial-0078, 0.45` 光斑阈值与 `SecantLeastSquaresTests` 的案例 9 完整目标验收仍有失败。本次没有修改求解器、目标或容差来消除它们。
- 该次镀膜完整 47/47、比较工具 182/182 属于集成前审计；本次新增关闭保护后以本次镀膜结果为准。
- 集成前格式检查报告 37 文件、511 个空白诊断；本次只规范迁移/新增代码，不宣称全仓格式债务已清零。

真实断电、磁盘故障、第二 Windows 登录会话、ARM64 机器及完整人工桌面验收仍未完成。Headless 控件渲染和 x64 原生非 GUI 启动不能替代这些验收。非商业未签名交付继续遵循用户决定；一键版本回退不纳入范围。未新增 Zemax 原生捕获，也不恢复已排除的独立 Cooke/Tessar 调查。
