# 构建与发布

2026-09-07 当前工程验证：正式完整主测试 1289 通过、5 失败（共 1294），工具测试 104/104 通过，均无跳过。5 项失败已在本轮修改前的 Core 二进制上复现相同数值，当前不能宣称完整测试全通过；详细证据及 P5 状态见 [孔径诊断修复记录](INITIAL_STRUCTURE_P5_APERTURE_2026-09-07.md)。下文 2026-09-06 的测试数和精度结论是该阶段历史记录。

2026-09-08 独立实验室 S1 验证：锁定还原、默认 Debug/Release 构建、完整格式及差异检查通过，完整 Release 测试 **150/150** 通过。Debug 定向测试 **35/35** 通过，Debug 全量曾主动中断，未完成；正式完整测试本阶段未重跑，以上 5 项既有失败仍未解决。配置、命令与证据见 [S1 实施记录](INITIAL_STRUCTURE_S1_SOLVER_2026-09-08.md)。该通用求解器尚未接入光学搜索或正式产品。

## 文档同步规则

每项已完成代码修改必须在同一任务中更新相关文档。文档必须区分已实现、计划和仅兼容行为。测试数量或验证日期变化时，所有引用该基线的文档必须同步；代码、测试、文档和最终报告必须一致。

## 本地构建

解决方案新增独立 `OptilandWorkbench.ZemaxComparison` 和其离线测试项目；普通构建/CI 不启动 Zemax，也不需要其程序集。真实捕获时才使用安装目录的 .NET Framework 编译器和 ZOS-API DLL 构建隔离工作进程。发布 App 不包含验证工具。命令及锁定还原说明见 [工具 README](../tools/OptilandWorkbench.ZemaxComparison/README.md)。

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --locked-mode
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
```

仓库通过 `Directory.Build.targets` 关闭 Avalonia BuildServices 的构建统计目标；这不是编译能力的一部分，关闭后受限本地环境不需要写入用户 AppData/Home 目录即可构建 App 和测试程序集。

有意修改依赖时再更新锁文件：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --force-evaluate
```

## 测试

```bash
dotnet test OptilandWorkbench.slnx --no-build --no-restore /m:1 /nr:false
```

VSTest 会打开本地套接字；受限沙箱可能需要额外权限。普通修改优先运行相关定向子集，只有跨模块、高风险或发布验证才要求全量测试。

普通 CI 的 Linux、macOS、Windows 主测试作业均单独运行 `OptilandWorkbench.ZemaxComparison.Tests`；这些测试只使用固定原始夹具和普通子进程，不探测或启动 OpticStudio。其 hang 诊断阈值为 `3m`。许可证集成验证只在明确运行比较工具的 Zemax 机器上执行。

CI 中正式产品与 Initial Structure Lab 测试均启用 hang 诊断：测试进程长时间无响应时会产生日志/转储线索，而不是无限等待或让后续结果失真。主测试 hang 阈值为 `12m`，Initial Structure Lab 为 `8m`；这个阈值覆盖已知 4 分钟级长测试在较慢 CI 机器上的波动，不把接近完成的慢测试误判成挂死。

## 换行符

仓库文本默认使用 LF；Windows 批处理脚本保留 CRLF。`.editorconfig` 和 `.gitattributes` 必须保持一致。Windows 开发者建议使用仓库属性控制换行，而不是依赖全局 `core.autocrlf=true`。

## 性能基准

```bash
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj -- --non-sequential 1000000
```

第一条基准覆盖 10,000 和 100,000 条顺序光线、20 个表面、不同历史保留模式、PSF/MTF 采样和 Monte Carlo。第二条是独立非序列百万光线 STARRDB 流式写入基准，记录吞吐、托管堆、峰值工作集和数据库大小。输出均为 CSV；性能结果用于同机同运行时比较，不是普通 CI 硬阈值。

## 非序列教学样例

```bash
dotnet run --project tools/OptilandWorkbench.NonSequentialSamples/OptilandWorkbench.NonSequentialSamples.csproj -- samples/non-sequential
```

生成器使用固定对象GUID和随机种子，逐个追迹验证场景、能量平衡及建议路径筛选，再分别原子写入12个STAROPT工程、6张光源效果SVG和`index.json`。样例清单、课堂步骤与预期结果见[`samples/non-sequential/README.md`](../samples/non-sequential/README.md)。

## 启动桌面应用

独立顶层“实验室 → AI 初始结构”提供独立初始结构实验室的进程启动入口。实验室仍须通过 `labs/InitialStructure/OptilandWorkbench.InitialStructureLab.slnx` 单独构建，正式解决方案和安装包不包含其实验程序集。开发目录的入口使用与主程序相同配置的实验室输出；独立部署和路径配置见 [实验室 README](../labs/InitialStructure/README.md#run)。

- Windows：`Run-Optiland.cmd`
- macOS：`Run-Optiland.command`

脚本依次执行 `dotnet restore`、`dotnet clean`、`dotnet build --no-restore` 和 `dotnet run --no-build`。还原放在清理之前，因为 `dotnet clean` 同样会解析现有 NuGet 资产文件；全局包缓存缺失时，这个顺序会先补齐依赖，避免清理阶段触发 `NETSDK1064`。清理只涉及项目构建输出，不删除 `%APPDATA%/OptilandWorkbench` 或 macOS 对应用户目录中的工程、主题和会话数据。

终端等价命令：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## 发布目标

### Windows 一键生成带安装向导的 EXE

在仓库根目录双击 `Build-Installer.cmd`（原入口 `Build-Exe.cmd` 等效）。默认生成 Windows x64 Release **Setup 安装程序**，不再只是便携目录。需要安装 `global.json` 指定的 .NET SDK（当前为 `10.0.300`，允许同功能带更新补丁）；首次打包需联网还原 NuGet、Windows 运行时和准备安装包编译器。

```text
artifacts/installers/OpticalSystemDesign-win-x64-<时间戳>-<唯一编号>/
  OpticalSystemDesign-1.0.0-win-x64-Setup.exe
  OpticalSystemDesign-1.0.0-win-x64-Setup.exe.sha256
  payload/app-<唯一编号>/
```

分发时只需提供 `*-Setup.exe`；SHA-256 文件可用于核验，不需要携带 `payload`。安装程序包含 .NET 运行时与全部资源，目标电脑不必另装 .NET；安装时无需联网。安装包使用 Inno Setup 的现代向导，提供简体中文/英文、欢迎页、安装目录、开始菜单目录、可选桌面快捷方式、安装进度和完成页。完成页的启动程序选项默认不勾选。

默认按当前用户安装至 `%LOCALAPPDATA%\Programs\S.T.A.R. Labs\Optical System Design`，不要求管理员权限；在 Windows“设置 > 应用”或开始菜单卸载。安装目录与快捷方式选项在重复安装时保留。升级/卸载前请正常退出应用；安装程序不会自动关闭正在运行且可能有未保存内容的窗口。

安装包与厂商镜头 JSON 均不压缩，厂商资源仍分别保存于 `LensLibrary/StockCatalogs/<厂商名>.json`。卸载使用已安装文件日志，不递归清空安装目录，也不删除用户设置、会话或另外创建的工程文件。**随安装包分发的文件归安装程序管理**，修改过的内置示例/目录仍可能在重复安装时被覆盖、卸载时被移除；编辑后应另存到用户工程目录。

`scripts/build-installer.ps1` 先调用 `scripts/publish-windows.ps1` 发布，再编译 `packaging/windows/OpticalSystemDesign.iss`。优先使用指定路径或本机已安装的 Inno Setup；否则从官方 GitHub 发布下载固定 `6.7.3`，验证固定 SHA-256 后，以便携模式准备到忽略版本控制的 `artifacts/tools/inno-setup-6.7.3`，不注册系统安装、快捷方式或文件关联。语言文件、授权和来源见 `packaging/windows/inno/`；商用前应查阅上游授权与商业许可政策。

可选命令（相对输出路径按仓库根目录解析）：

```powershell
# 自定义输出目录，支持路径包含空格；建议保持路径较短
.\Build-Installer.cmd -OutputRoot "D:\Releases\Optical System Design"
# 使用已准备好的编译器（Inno Setup 6.3+），不触发编译器下载
.\Build-Installer.cmd -InnoSetupCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
# Windows ARM64（脚本支持，尚未在 ARM64 机器上验证安装/运行）
.\Build-Installer.cmd -Runtime win-arm64
# 只预览，不下载工具、不执行还原、不创建输出目录
.\Build-Installer.cmd -WhatIf
# 仍需便携版时，直接使用原发布脚本
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
```

自动化可直接调用 PowerShell 脚本，或者为 CMD 入口设置 `OPTILAND_PACKAGE_NO_PAUSE=1`。脚本先验证已提交的平台无关锁文件，再把 RID 专用锁文件写入各项目 `obj` 目录，不覆盖源代码中的 `packages.lock.json`。发布关闭裁剪、AOT 和单文件合并，保留 Avalonia/Skia 原生依赖、外部资源及授权说明。每次使用新目录；失败输出保留 `.partial` 后缀，不删除旧发布包和用户数据。安装包内部暂存目录使用短名称以避免 Inno Setup 6 的源文件路径长度限制。成功前检查 EXE、运行时、品牌/授权资源和厂商目录，编译后生成安装包 SHA-256。

便携版仍输出到 `artifacts/windows/OpticalSystemDesign-win-x64-<时间戳>-<唯一编号>/`，运行其中的 `OptilandWorkbench.App.exe`，分发时必须复制**整个目录**。Windows 打包专用 `WindowsPackage=true` 使 App 使用 `WinExe` 子系统，启动时不附带控制台；日常构建和其他平台不受影响。安装程序与应用目前均未签名，Windows 可能提示未知发布者；不要绕过组织安全策略。

2026-09-04 安装包验证：实际生成 `win-x64` Setup；使用独立测试 AppId 在项目临时目录完成安装、同版本重复安装和卸载，退出码均为 `0`。安装后的 **1178 个载荷文件**逐一哈希一致，授权文件存在，卸载后测试安装注册与应用 EXE 已移除，另行创建的工程文件保留。测试没有启动产品、覆盖正式安装或创建用户快捷方式；这是静默安装链路验证，不等同于安装向导逐页操作或目标电脑上的完整 GUI 验收。`WindowsPackagingTests` **7/7** 定向通过（便携发布契约、中文安装/安全卸载契约、编译器来源/哈希校验、含空格路径预览、非法 RID、缺失指定编译器），CMD 入口 `-WhatIf` 通过；未执行全量测试，不替代下文历史全量基线。

最终重建的 Setup 约 **260.4 MiB**，SHA-256 核对通过；与上述安装烟测载荷相比，仅补充了 `README.txt` 中对随包文件的卸载/覆盖说明，全部二进制及镜头资源哈希保持一致。隔离测试安装已正常卸载，临时安装目录内仅保留测试自建工程。

同日早先便携版验证：约 258 MiB，PE 头确认 x64 / Windows GUI，`runtimeconfig.json` 声明随包携带 .NET 10.0.8，931 个镜头库文件逐一哈希核对与源资源一致。这是早先的便携输出验证记录，不是当前默认入口的产物类型。

### 跨平台发布

一次发布主要平台：

```bash
bash scripts/publish-cross-platform.sh
```

自包含发布：

```bash
SELF_CONTAINED=true bash scripts/publish-cross-platform.sh
```

手工命令示例：

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-arm64 --self-contained false
```

输出位于：

```text
src/OptilandWorkbench.App/bin/Release/net10.0/<runtime>/publish
```

macOS 脚本还会生成 `Optical System Design.app`，声明 `.staropt` 文档类型并转发 Finder 打开的工程路径。

## 当前验证基线

2026-09-06 Huygens MTF 后处理修复后的正式解决方案 Release 默认输出构建 `0` 警告、`0` 错误，完整主测试 `1233/1233`、工具测试 `104/104`，合计 `1337` 项通过，零失败、零跳过；锁定依赖从本地缓存还原，格式检查和 git diff --check 通过。MS-L7 重新执行全部 72 项，结果为 44 Pass、6 Close、2 Difference、17 Incomparable、3 Skipped，0 执行错误；主基准独立复验三项 Huygens MTF 均 Pass。新增 14 项回归包含原生 PSF 后处理重建，不能代替全链条精度结论。视场 MTF 的一处分量由 Pass 变为 Close，局部退步及未解决误差监控预算已明确记录，正式数值容差不变。未执行在线漏洞审计、独立实验室、旧外部报告工具、GUI 截图或安装包实测。该阶段证据见 `ZEMAX_HUYGENS_REPAIR_2026-09-06.md`，以下更早的移除审计仅作历史记录。详见 [移除审计与验证](PYTHON_OPTILAND_REMOVAL.md)。

## 历史验证记录

以下保留各日期当时的定向和全量结果，不作为当前数量：

- 2026-08-28变更前历史基线为正式产品严格构建 `0` 警告、`0` 错误、全量回归 `837/837`；该数量已经由下文 2026-09-03 的 `1015/1015` 完整基线取代，不作为当前结果；
- 2026-08-29可靠性加固前，相关桌面可访问性、分析 GUI、镜头编辑器和 Dock 契约子集记录为 `95/95`；本轮修改后未把该旧数量沿用为当前结果；
- 2026-08-29可靠性加固前，非序列定向记录为 `80/80`、独立智能初始结构实验室定向记录为 `9/9`；本轮新增数据库、资源预算和路径表达式回归后，这两个数字只作为历史记录，不代表当前测试总数；
- 最近一次架构、可访问性和响应式布局定向结果为 `14/14`；
- 2 项新增 Avalonia 首帧/主题回归通过相关 16 项定向子集；
- 4 项新增 Dock 空宿主、会话和锁定回归通过 `12/12` 窗口布局子集；
- 配对 fan 布局契约覆盖光线像差图和瞳孔像差图：同一视场的 `P_y/P_x` 位于同一卡片，卡片按视场数平衡排列，结果区不再暴露手动方形开关；
- 方形单图契约覆盖全视场点列图、光迹图和干涉图：X/Y 数据等比例，外层视口默认保持正方形；色焦移保留普通曲线图的自适应布局；
- 平铺/层叠修复复用了现有测试，验证浮动页自动回收到主文档区并进入内部 MDI；合并命令验证回收后恢复标签模式；
- 报告菜单、72 项 Core 分析目录、顺序 70 项/非序列 2 项模式隔离、三类真实报告输出以及独立“畸变”入口退场由对应定向契约测试覆盖；
- 最近一次相关目录子集为 `3/3`，同时覆盖旧 `Distortion`/“畸变”名称迁移、组合页畸变曲线、干涉图方形视口和色焦移非方形契约；
- Zemax 评价函数导入子集为 `6/6`，覆盖实际 `[MS-L7]` 的 103 行源顺序、`TRAR` 参数映射、只读兼容槽位、快照校验和编辑往返不裁剪；
- 最近 App 项目构建结果为 0 警告、0 错误。
- 2026-08-29可靠性加固后，正式解决方案（含 Core、Application、App、测试程序集和工具项目）与独立实验室解决方案均在默认输出目录构建为 `0` 警告、`0` 错误；此前正式产品组合子集为 `14/14`、实验室不可变发布子集为 `3/3`。本轮最终新增/高风险正式产品筛选为 `13/13`，实验室持久化与规格边界筛选为 `4/4`，限定静态分析规则无残余诊断。只运行相关定向测试，不运行正式全量测试，详细边界见[可靠性与资源边界加固](RELIABILITY_HARDENING_2026-08-29.md)。
- 同轮正式产品分别通过可靠性主子集 `11/11`、非序列光源采样 `3/3`、分析 GUI 参数与轴元数据 `3/3`、限额读取/响应式守护/STAROPT/会话 `5/5`、可访问性与响应式布局 `5/5`；这些是独立筛选结果，不是新的全量基线。
- 实验室资源预算最初单项为 `1/1`，预算与持久化往返组合子集为 `2/2`，唯一运行 ID、不可变目录发布和取消清理子集为 `3/3`，本轮防篡改/大小写冲突/发布/规格边界筛选为 `4/4`；筛选重叠，不累计为全量数量。
- 2026-08-29 对正式解决方案执行 NuGet.org 直接与传递依赖漏洞查询，当前未报告已知易受攻击包；后续发布仍应重新查询。
- 2026-08-29 能力真实性修复后，评价函数/优化器目录、ZMX 只读行、快照校验和应用优化入口定向子集通过 `8/8`；未运行正式全量测试。
- CI 现将正式解决方案与独立 Initial Structure Lab 的构建、三平台测试拆成独立 job：主测试失败不会跳过 Lab 测试，Lab 构建成功也不再被解释为 Lab 测试已运行。两套测试分别输出 TRX 并以独立 artifact 上传；仓库质量任务分别验证两套解决方案格式。固定种子格式模糊回归覆盖 STAROPT、STARMESH、STARRDB 和 ZMX，当前定向结果 `6/6`。
- STEP CAD CI 分为“生成 fixture”和“FreeCAD/OpenCascade 第三方导入验证”两个 job。生成出的 STEP fixture 始终上传；验证 job 固定在 `ubuntu-22.04`，安装固定版本 `FreeCAD 0.19.2+dfsg1-3ubuntu1`，通过 GitHub Actions 缓存复用固定 FreeCAD `.deb` 包，并上传 apt、FreeCAD 版本、`.deb` SHA-256 和 OpenCascade 验证日志。若验证环境安装失败，红灯表示第三方验证环境未建立，不等同于 STEP 输出已被证明错误。完全脱离 apt 镜像变化仍需要后续维护预构建容器或随仓库托管的校验运行时。
- 独立 CI 性能烟测覆盖 10,000 条顺序末面追迹、2,000 条几何 MTF、20 次公差 Monte Carlo 和 10,000 条非序列 STARRDB；本机当前约 `0.8 s`、累计分配约 `705 MiB`，闸门上限为 `2 min`、`2 GiB` 和 `128 MiB` 数据库。该宽松闸门用于捕获数量级退化，不替代正式性能基准。
- CI 在提交、拉取请求、手动运行和每周计划任务中执行直接与传递 NuGet 漏洞查询；在线查询结果具有时效性。
- 2026-08-30最终收口重新构建正式产品和独立实验室，均为`0`警告、`0`错误；两套`dotnet format --verify-no-changes`、启动/发布脚本语法和`git diff --check`通过。评价函数/优化器、当时的333代码注册、公差逆向/元件/非球面、非序列探测器及其类型化物理轴、兼容程序集、Application/App分层、可访问性、响应式布局和主题的高风险组合筛选为`56/56`；受限文件与固定种子格式模糊组合为`14/14`；实验室冻结基准、密集验收、断点续算、STAROPT导出和响应式源码关键路径为`5/5`。这些仍是历史定向结果。
- 同日通过 NuGet.org 重新查询正式解决方案和独立实验室的直接与传递依赖，所有项目均未报告已知易受攻击包；性能烟测在本机约`0.79 s`完成，累计分配约`705.5 MB`，10,000射线STARRDB为`877,339 bytes`，均在CI宽松闸门内。
- 2026-08-31 后续高风险加固覆盖非序列材料快照、文档图验证复杂度、不可变网格资产、STL 流式/可取消导入与预计工作集预算，以及优化框架严格有限数/维度校验。正式产品完整主测试 `935/935` 通过，独立 Initial Structure Lab 测试 `21/21` 通过。
- 2026-08-31 文档切换事务、优化迭代/变量回滚、非序列结果代次/探测器重建和运行时字号刷新修复后，正式解决方案默认输出构建为 `0` 警告、`0` 错误；文档与保存 `22/22`、优化相关 `23/23`、非序列文档/追迹/杂散光 `88/88`、主题运行时 `6/6`、分层架构 `15/15` 定向筛选通过。筛选存在交集，不能相加；按用户要求未运行完整测试，不替代上条 `935/935` 完整基线。
- 2026-09-01 设置保存事务、非序列锁外观察者通知与大页码分页、相位/扩展光源输入边界、自绘只读图辅助功能、窄视口约束和 Initial Structure Lab 算法版本 2 修复后，正式解决方案和独立实验室解决方案默认输出构建均为 `0` 警告、`0` 错误。正式产品相关定向测试 `7/7`、实验室定向测试 `4/4` 通过；按用户要求未运行完整测试，不替代 2026-08-31 的正式产品 `935/935` 和实验室 `21/21` 完整基线。
- 2026-09-01 修复 CI 的 STEP 验证 job 在 job 级环境变量中使用不可用 `runner` 上下文的问题；日志目录改用该阶段允许的 `github.workspace`，不改变 STEP 生成、固定 FreeCAD 版本或 OpenCascade 验证门槛。GitHub 上连续零秒失败的运行均未创建任何 job，属于工作流解析失败，不代表这些提交已执行或未通过代码测试。修复后的工作流通过 YAML 解析、`actionlint 1.7.12` 和 shell 脚本语法校验。
- 同日工作流恢复执行后，Linux 主测试实际运行 `972` 项并发现 2 个非序列教学清单仍使用分裂父分支重复发布时期的旧计数。Fresnel 与全反射光管的确定性分支基准分别更新为 `1200` 和 `1195`，12 个教学工程重新载入/追迹子集 `13/13` 通过；STAROPT 工程内容和能量基准未改变。
- 2026-09-03 Zemax 顺序评价函数继续收敛后，正式主测试 `1006/1006` 通过，解决方案 Release 构建 `0` 错误；构建仅有 NuGet 漏洞数据源 SSL 警告。目录保持 383 个顺序兼容代码、114 个已连接计算引擎的代码；`[MS-L7]` 103 行的源哈希/顺序和 400 余个活动参数槽已锁定，除兼容只读 `DIMX` 外，全部 82 个当前可执行数值行与本机 OpticStudio 2026 R1 golden 对齐。此次补齐 63 个高 NA `TRAR`、`RANG/SINE`、`PMAG/DIVI`、`REAR` 与边厚范围行，并修正 ray aiming、零号面分类型语义、所选波长近轴像面和相邻表面各自半口径边厚；完整 Zemax 等价仍限于已捕获系统和设置。

- 2026-09-03 后续独立审核发现评价函数缓存混用物面/像面、RMS 主光线默认面解析错误及单光线/多光线瞄准设置不一致。本批修复按实际目标面及瞄准设置区分采样缓存，补齐 RMS、Moore–Elliott 和波前相关默认像面路径。新增 9 个回归用例覆盖独立与批量求值、两种行序、默认与显式像面、非连续面号及瞄准开关；正式全量主测试 `1015/1015` 通过，零跳过。锁定还原成功，Release 构建为 `0` 警告、`0` 错误，两个改动源文件的格式检查及 `git diff --check` 通过。上条 `1006/1006` 为前一批历史结果；固定 `[MS-L7]` 的 82 行 golden 容差和数值未调整。本轮不执行后续计划阶段。
- 2026-09-04 新增“帮助 > 操作数帮助”可停靠文档，并从优化服务的当前操作数目录发布定义、参数、支持状态和实际计算说明。搜索、可计算/兼容保留筛选、Dock 类型契约、Headless 宽窄布局与字体令牌守护组合测试 `5/5` 通过，相邻评价函数描述符与工作区会话子集 `7/7` 通过；正式解决方案 Debug 构建为 `0` 警告、`0` 错误。按要求未在本机运行完整测试，不替代 2026-09-03 的 `1015/1015` 本机全量基线；此变更不调整任何操作数数值算法或 Zemax golden。
- 2026-09-04 继续 Zemax 顺序评价函数计划，新增 `DIVB`、`PROB`、`OSUM`、`QSUM` 和 `EQUA` 五个通用数学操作数的定义级执行路径，该批结束时为 383 个顺序兼容代码、119 个已连接计算引擎代码。ZMX 导入、参数描述符、行序求值、错误报告、STAROPT 快照往返、帮助说明和行色归类已同步；定向测试 `ZemaxImportTests|MeritOperandRowPaletteTests` 为 `78/78` 通过，`OperandHelpTests` 为 `4/4` 通过，解决方案 Debug 构建 `0` 警告、`0` 错误，`dotnet format --verify-no-changes` 和 `git diff --check` 通过。当前环境未找到可用 OpticStudio/ZOS-API 运行时，因此本批新增语义尚未形成 Zemax golden 数值闭环，也不替代 2026-09-03 的 `1015/1015` 本机全量基线。
- 2026-09-04 后续继续同一计划，新增 `MNIN`、`MXIN`、`MNAB`、`MXAB` 和 `POWR` 五个常见玻璃/表面功率操作数的定义级执行路径，注册表当前为 383 个顺序兼容代码、124 个已连接计算引擎代码。`MNIN/MXIN` 按 `Surf1..Surf2` 范围约束玻璃 d 线 Nd，`MNAB/MXAB` 约束玻璃 Vd，空气/真空/反射空间不参与；`POWR` 按标准折射面 `(n_after − n_before) / Radius` 计算表面光焦度，平面返回 0，非标准面或反射面报告错误。ZMX 导入、参数描述符、错误路径、STAROPT 快照往返、帮助说明和行色归类已同步；定向测试 `ZemaxImportTests|MeritOperandRowPaletteTests|OperandHelpTests` 为 `84/84` 通过，解决方案 Debug 构建 `0` 警告、`0` 错误，`dotnet format --verify-no-changes` 和 `git diff --check` 通过。当前环境未找到可用 OpticStudio/ZOS-API 运行时，因此本批新增语义尚未形成 Zemax golden 数值闭环，也不替代 2026-09-03 的 `1015/1015` 本机全量基线。

- 2026-09-04 远端同步前复核：ZMX 切趾、实际 266 nm 扩束文件、缺失玻璃保留与底部提示、文档编辑/保存和相邻追迹路径的定向回归 `280/280` 通过；制图模板、架构约束、操作数行色及帮助的补充定向回归 `56/56` 通过。桌面默认输出目录构建 `0` 警告、`0` 错误；扩束布局预览随仓库保存至 `artifacts/validation/beam-expander-266nm-6x-layout.png`。这些结果不是全量测试或新的 Zemax 数值基线，具体修复边界见 [ZMX 切趾与布局修复记录](ZEMAX_APODIZATION_LAYOUT_FIX.md)。

- 2026-09-05 镜头数据移除“添加 / 删除”工具栏，新增右键“下插入、上插入、删除”。默认 App 项目已重建为 `0` 警告、`0` 错误，旧进程不再占用默认输出；插入/删除与相邻结构回归 `21/21`，此前待跑的切趾、数值框、表面属性和文档标签界面回归 `19/19`，组合筛选 `40/40` 通过。独立 Skia 右键子集 `5/5` 再次通过，并复核菜单截图，重复用例不累加。未运行全量测试、打包或新的 Zemax 数值对标；不替代历史完整基线。界面截图测试不得与使用模拟字体的 Headless 测试混用渲染后端，应另起测试进程，避免 `HeadlessPlatformTypeface`/`SkiaTypeface` 混用错误。详细边界见 [镜头数据右键操作](UI_DESIGN_REVIEW.md#2026-09-05-镜头数据右键插入与删除)。

- 2026-09-05 普通主题默认操作按钮改为淡蓝底，悬停/按下依次加深；其他主题及专用按钮样式保留。默认 App 构建 `0` 警告、`0` 错误，状态、交互、动态控件、主题隔离及相邻布局定向检查 `8/8` 通过；独立 Skia 真实鼠标用例 `1/1` 再次通过并核对三种状态截图。未运行全量测试或打包，不修改历史全量基线。详见 [操作按钮状态](UI_DESIGN_REVIEW.md#2026-09-05-普通主题操作按钮蓝色状态)。

- 2026-09-05 半径求解入口改为单元格右侧标记区，支持固定、变量和前序面曲率拾取。默认 App Debug 构建 `0` 警告、`0` 错误；计算/服务、撤销保存、插入、界面和优化组合定向检查 **43/43** 通过，0 跳过。独立 Skia 界面复跑 **3/3** 通过，并复核变量/拾取截图；重复用例不累加。本轮没有运行全量测试、发布或打包，不更新历史全量数量，也不构成新的 Zemax 实机数值对标。详细边界见[曲率半径求解入口](RADIUS_SOLVE_EDITOR.md)。

2026-09-05 审计修复后已补跑上述完整验证；安装包构建和 Windows 安装/卸载实测不包含在本次数值与源码回归中。

- 2026-09-05 表面属性标题简化为“展开/收起 + 表面 N 属性 + 上一面/下一面”圆形按钮横条。默认 App Debug 构建 `0` 警告、`0` 错误；属性布局/导航、半径求解和右键行操作定向组合 **14/14** 通过，0 跳过，独立 Skia 属性面板 **6/6** 重复验证通过并复核紧凑横条截图。没有运行全量测试或打包，不更新历史全量数量；[属性面板文档](SURFACE_PROPERTIES_EDITOR.md) 记录功能与验证边界。

Optiland 0.5.8 历史资料只保留在 validation/history，禁止重新生成或新增对照。锁定还原、构建和产品发布不依赖 Python、pythonnet、tools/python-reference 或历史测试数据。Zemax 捕获/报告与 FreeCAD STEP 检查属于产品之外的验证工具，使用它们不意味着产品运行需要 Python。

- 2026-09-07 修复 Zemax `MEMA` 机械半直径导入：该值不再由相邻 `DIAM` 推算，并贯通 STAROPT 快照、处方显示、布局机械边界、制造数据、CAD 网格和 ZMX 再导出；未提供 `MEMA` 的文件继续随该面净半直径变化。包含单位换算、独立 `DIAM/MEMA`、保存往返及相关既有功能的定向回归 `136/136` 通过，格式检查通过；此结果不是新的全量测试基线。
- 2026-09-07 对齐 Zemax 顺序布局的有效口径语义：布局仍按系统入瞳采样，固定 `DIAM` 的有光焦度表面和光阑面建立圆形截光口径，`APMN` 保持环形遮拦，`MEMA` 仅控制实体外形。新增用例验证入瞳边缘光线在 `DIAM` 渐晕、机械外缘不放大通光束、自动 `DIAM` 不被错误用作额外光阑及平面光阑截光。默认目录相关导入、布局和追迹定向回归 `126/126` 通过，整个解决方案 Debug 构建 `0` 警告、`0` 错误。本次不更新历史全量测试基线。
- 2026-09-07 修复导入固定 `DIAM` 后标准点列图的艾里斑计算：几何点列仍按有效口径排除渐晕光线；用于艾里斑的 Working F/# 探测按 Zemax 规则忽略表面口径，并在边缘探测光线失败时缩小瞳孔采样后外推。实际 `zemax-123456.ZMX` 与独立 `DIAM/MEMA` 回归均已覆盖；默认目录相关点列、艾里斑、PSF、无焦和 Zemax 导入测试 `139/139` 通过，整个解决方案 Debug 构建 `0` 警告、`0` 错误，格式检查与 `git diff --check` 通过。本次不更新历史全量测试基线。
- 2026-09-07 镜头数据的曲率、厚度和净口径统一为 Zemax 风格的单元格求解入口：固定曲率/厚度为空白、变量 `V`、拾取 `P`；自动净口径为空白、用户固定 `U`、拾取 `P`。移除独立“T 变量”、净口径“固定”列及表面属性页的重复开关，右键数值单元格打开对应设置。厚度与净口径拾取已连接计算、撤销、保存、表面重编号、多配置和优化链路，ZMX `DIAM` 的 `0/1/2` 可导入为自动/用户固定/拾取。默认目录统一求解及相邻高风险回归 `207/207` 通过，整个解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误；格式和差异检查通过。本次不更新历史全量测试基线，详见[镜头数据求解入口](RADIUS_SOLVE_EDITOR.md)。
- 2026-09-07 修复全视场像差在单一轴上视场中把极小默认宽度除以零最大视场、生成越界归一化坐标的问题。全视场采样现在按径向归一化单位圆裁剪边界外点，零宽度轴上网格只计算中心点，绘图坐标围绕所选视场中心显示。全视场像差、视场定义与相邻波前图定向回归 `22/22` 通过，整个解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误；格式和差异检查通过。本次不更新历史全量测试基线。
- 2026-09-07 使用用户实际 `zmax_38668.ZMX` 复现 12 个默认分析入口中 11 个共享 Working F/# 异常。将忽略表面口径的尺度探测统一到公共实现，同时保留真实波前/偏振光瞳截光、整体瞄准和无通光样本的失败状态；MTF 继承源 PSF 视场尺度，高采样 PSF 归一化避免整数溢出。原文件入口、斯涅尔定律/解析圆孔 MTF、相邻高 NA/无焦/衍射及已提交 Zemax 数值基准定向回归 `86/86` 通过；默认目录完整解决方案 Debug 与 Release 构建均为 `0` 警告、`0` 错误，格式和差异检查通过。实际 Avalonia MTF 面板截图成功。原文件新一轮 Zemax 捕获因本机缺少 ZOS-API 未完成，不能计为实机一致性通过。本次不更新历史全量测试基线，详见 [38668 公共计算修复](ZEMAX_38668_WORKING_F_NUMBER.md)。
- 2026-09-07 增加“实验室 → AI 初始结构”的独立进程入口。正式 App 仅含路径定位与启动代码，正式 Core/Application/App 不引用实验室程序集；实验算法、App、数据、测试和解决方案继续独立。正式启动器/架构定向检查 `33/33`、实验室隔离/界面源契约 `2/2` 通过；Windows 独立程序窗口启动检查通过。正式及实验室解决方案分别在默认 Debug/Release 输出构建为 `0` 警告、`0` 错误，正式解决方案格式检查与差异检查通过。未修改默认安装包、实验算法或历史全量测试基线。
- 2026-09-07 独立实验室 P0/P1：计划先保存、12 个完整目标规格先冻结，再实现严格零曲率启动引擎。新路径定向 `18/18`、实验室全量 `42/42`，均无失败/跳过；独立解决方案默认 Debug/Release 构建及格式、差异检查通过。原十个算法基准未改。此时只验证轴上、主波长、30% 入瞳启动，完整规格及新界面未完成，正式源码和安装包不变。见 [P0/P1 实施记录](INITIAL_STRUCTURE_P1_2026-09-07.md)。
- 2026-09-07 独立实验室 P2：先将 P1 截距指标迁移到正式 Core 点列接口（启动算法 v2），再实现完整口径／视场／光谱的单家族引擎。实验室全量 `63/63`、正式相关定向 `46/46`，均无失败／跳过；正式和实验室默认 Debug/Release 构建均为 `0` 警告、`0` 错误。12 个冻结规格各测一个固定家族，4 个达标、8 个明确报告差距，规格和容差未改。新引擎尚未接入新桌面工作流，多种子发布协议及新候选外部对照仍未完成；不改写正式全量数值基线。见 [P2 实施记录](INITIAL_STRUCTURE_P2_2026-09-07.md)。
- 2026-09-07 独立实验室 P3：新增真实目录玻璃、离散表面光阑、片数家族和有父来源的细化分支，全部光学评价仍调用正式 Core。原子检查点预扣预算，正常完成后结算，未知中断工作保守计费；串行／并行及完成批次恢复一致性通过。实验室全量 `91/91`，最终去重调整后的相关子集 `16/16`（包含于 91 项），均无失败／跳过；独立实验室最终默认 Debug/Release 构建均为 `0` 警告、`0` 错误，格式及差异检查通过。12 个冻结规格按原种子 101 和各 10,000 次预算运行，4 个找到多个达标家族，8 个仍有差距；不表示发布协议通过，也未全面改善 P2 的像质结果。本次未修改正式 Core、未重跑正式测试，旧桌面 v3 尚未接入新搜索。见 [P3 实施记录](INITIAL_STRUCTURE_P3_2026-09-07.md)。
- 2026-09-07 独立实验室 P4：新界面接入严格平板多家族搜索；二维面形与光线全部来自正式 Core，新增目标／处方／视场表、A/B 同尺度比较、保留预算恢复、独立追加细化和停止后导出。参数修改清空旧图，损坏记录显示错误，无效数值不再失焦回退后误用旧参数。实验室全量 `104/104`，约 11 分 19 秒；最后输入修正后的相关 `14/14` 在最终 Release 重放（属于 104 项），均无失败／跳过。默认 Debug/Release 构建 `0` 警告、`0` 错误，格式与差异检查通过；1240/600/480 px 实际 Avalonia 交互渲染及独立 Windows 程序启动／关闭检查通过。原生人工跨平台 UI 验收、60 次发布协议和新候选外部数值对照未完成；正式源码、冻结规格和安装包未改，未重跑正式数值测试。见 [P4 实施记录](INITIAL_STRUCTURE_P4_2026-09-07.md)。
