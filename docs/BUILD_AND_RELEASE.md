# 构建与发布

更新：2026-10-26。本文只说明当前命令和交付规则。最新验收见 [当前状态](CURRENT_STATUS.md)，历史构建验收原文已集中到 [历史账本](validation/BUILD_VERIFICATION_HISTORY_2026-10-13.json)，不作为当前通过结论或可直接执行的旧命令。

## 唯一产品入口

`OpticalSystemDesign.slnx` 是唯一产品解决方案，包含主程序、初始结构和镀膜内置模块，以及对应测试。模块 UI 在主程序进程内运行；不再维护独立 App 或解决方案，也不接受外部可执行路径。

普通构建与 CI 不探测、不启动 Zemax。外部比较工具、参考捕获、第三方源归档和测试数据不进入产品运行依赖。

代码风格在根 `.editorconfig` 统一维护，SDK 分析器由 `Directory.Build.props` 启用，CI 将警告当错误并执行现有格式门禁。当前明确启用重抛堆栈和 ValueTask 使用规则；样式建议与阻断诊断分别配置，不宣称全部潜在代码问题已通过静态证明。发布工作流的外部 Actions 固定到完整 40 位提交 SHA；`scripts/verify-release-actions.ps1` 在 CI 检查引用，升级时应从对应官方仓库核对提交后修改，不能退回浮动标签。此检查不执行发布。

## 本地构建

安装 [global.json](../global.json) 指定兼容范围的 .NET SDK（当前 10.0.300 / latestPatch），在仓库根目录运行：

```powershell
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Debug --no-restore /m:1 /nr:false
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false
```

依赖有意变更时才使用 `dotnet restore OpticalSystemDesign.slnx --force-evaluate` 并审查锁文件。仓库通过 `Directory.Build.targets` 关闭 Avalonia 构建统计，不关闭 NuGet 安全审计。若运行中的产品锁住默认 App 输出，应正常关闭该特定程序再重建，不用另一个输出目录冒充默认产物更新。

AutoLens 的 CUDA 瞄准、追迹及求导使用锁定的 ILGPU / ILGPU.Algorithms 1.5.3 C# 库，默认构建和发布自动包含 `ILGPU.dll`、`ILGPU.Algorithms.dll` 与 `Licenses/ILGPU-*.txt` 五份许可证/声明。发布脚本拒绝缺少这些文件的载荷。使用 GPU 需要机器上已有可用 NVIDIA CUDA 驱动，产品不安装驱动或另启外部计算程序；无 GPU 时自动模式仍可用 CPU。参见 [GPU 功能范围](AUTOLENS_GPU.md)。

## 测试

产品依赖门禁用固定 SDK 内置的 C# 编译器解析源码，检查字符串、导入和互操作引用，并检查条件编译的非活动分支。来源注释、本地参数名和光瞳坐标 `Py` 不当成运行依赖；`py` 启动器在进程/文件名上下文中检查。项目、脚本、JSON、程序集及资源的依赖检查保留。编译器引用仅在测试项目中，不增加产品运行依赖或 NuGet 包。

日常修改默认只运行与改动行为及已观察退化直接相关的筛选；只有明确风险或用户要求才扩大范围。以下完整解决方案命令用于需要全量验证时，不要求每次修改都执行。各次实际范围见[当前状态](CURRENT_STATUS.md)。

```powershell
dotnet test OpticalSystemDesign.slnx -c Release --no-build --no-restore /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-build --no-restore
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release --no-build --no-restore
```

后两条用于模块定向运行，不是单独产品构建。VSTest 需要本地套接字；受限环境中的权限或网络故障应如实报告，不能当作通过。全量/定向、Debug/Release、原生/Headless、内部/外部数值验收分别记录，不能相加。

CI 使用同一根解决方案，以统一的 `CONFIGURATION=Release` 编译和运行主测试、模块测试、比较工具离线测试及性能冒烟，避免默认 Debug 的计算耗时污染有界契约检查。主测试和比较工具的单测试无进展诊断阈值均为 30 分钟。比较工具显式包含与 SkiaSharp 同版本的 Linux 原生资产；三个系统仍由各自的 CI 任务验证。最新流程与阈值以 [.github/workflows/ci.yml](../.github/workflows/ci.yml) 为准，真实 Zemax 捕获仍需对应许可和运行时。

初始结构 CI 在每个平台分为普通契约、前六个调查案例、后六个调查案例三组。`scripts/test-initial-structure-partition.ps1` 从原测试数据生成筛选器，并核对每个调查案例的实际 TRX 结果；12 个原案例必须恰好各运行一次，不删除调查、不缩减评价或时间预算。单案例冻结预算为 30 分钟，因此模块无进展诊断阈值为 35 分钟，调查分组任务上限为 240 分钟；主测试无进展阈值为 30 分钟。比较工具测试即使主测试失败也独立执行。分组只解决执行安排，不代表长调查已通过。

普通契约分组仅允许结果核对脚本中逐名列出的五个显式 CUDA 硬件测试报告 `NotExecuted`，并单独记录为跳过；失败、中止、空结果、普通测试跳过和调查分组跳过仍拒绝。界面测试的隔离会话使用真实 Skia 几何/绘图后端，以验证圆角裁剪命中和位图；涉及异步布局的 UI 对象在同一隔离 UI 线程创建、等待及释放。运行配置、采样或测试夹具有意调整时，必须保留冻结参考、严格数值门槛和原始失败记录。2026-10-26 的修复边界见 [CI 排查报告](CI_REPAIR_2026-10-26.md)。

可用以下命令检查分组或定向执行（先完成对应配置构建）：

```powershell
./scripts/tests/test-initial-structure-partition.tests.ps1
./scripts/test-initial-structure-partition.ps1 -Partition survey-first -PlanOnly
./scripts/test-initial-structure-partition.ps1 -Partition contracts -Configuration Release -ResultsDirectory artifacts/validation/initial-contracts
```

## 冻结证据门禁

CI 在三个系统上同时核对工作区与 HEAD 的原始字节。历史清单保持原路径、2668 项和原汇总 SHA-256；产品更名后的四项证据通过[显式迁移表](../tools/validation/frozen-evidence-relocations.json)定位，绝不按旧目录前缀自动回退。三份夹具读取现目录，旧配置读取已保存的原字节归档，不以现配置的 JSON 语义相同代替字节校验。此表仅用于历史证据验证，不进入产品运行或旧名称兼容。

从仓库根目录执行与 CI 相同的门禁：

```powershell
./tools/validation/GitFrozenIntegrity.ps1 -RepositoryRoot . -ManifestPath ./artifacts/validation/n02-layer-diagnosis-20261009/frozen-after.json -ExpectedAggregateSha256 3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10 -RelocationMapPath tools/validation/frozen-evidence-relocations.json -ExpectedRelocationMapSha256 6eefb25cbb1c35a120f73e8bbb761aac16759e7b8491b4606475caec8dfd4f6e
```

迁移表必须匹配固定字节哈希、原清单身份和逐项原 SHA-256，并存在于同一 Git 校验来源。内容或换行变化、文件缺失、目标冲突、越界或链接、旧路径重现均拒绝；不带迁移表仍按原路径检查并失败。`-Source Index` 仅认证所选索引，不能宣称 HEAD 或远端 CI 通过。新增表尚未提交时，HEAD 门禁也必须失败；临时索引的候选复验及回归记录见[本轮账本](validation/FROZEN_EVIDENCE_RELOCATION_REPAIR_2026-10-19.json)。

## 启动桌面应用

Windows 使用 `Run-OpticalSystemDesign.cmd`，macOS 使用 `Run-OpticalSystemDesign.command`，或执行：

```powershell
dotnet run --project src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release --no-build
```

macOS 的 `.command` 启动脚本须保留可执行位（Git 文件模式 `100755`）。若 Finder 提示“无法执行，因为你没有正确的访问权限”，在仓库根目录执行 `chmod +x Run-OpticalSystemDesign.command` 后再双击；该脚本会还原依赖、清理并重建默认 Debug 输出，然后启动主程序。

脚本先还原，再清理、构建和启动；清理只处理构建输出，不删除工程、主题或工作区。两个模块均从主程序“实验室”菜单打开，无独立启动脚本。

## Windows 一键生成带安装向导的 EXE

```powershell
.\Build-Installer.cmd
.\Build-Installer.cmd -OutputRoot "D:\Releases\Optical System Design"
.\Build-Installer.cmd -InnoSetupCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
.\Build-Installer.cmd -Runtime win-arm64
.\Build-Installer.cmd -WhatIf
```

`Build-Exe.cmd` 是兼容入口。默认发布 Windows x64 Release 自包含未签名安装包，版本从 [ProductVersion.props](../ProductVersion.props) 读取（当前 1.0.4，尚未正式发布；已有明确标注的测试预发布），输出到 `artifacts/installers/installer-win-x64-<编号>/`，含 Setup EXE、SHA-256、`release.json` 与载荷目录。分发只需 Setup EXE；哈希不证明发布者身份。首次构建需要联网还原并准备编译器，目标用户安装不需要 .NET 或联网。

脚本必须打包主程序、五个内部模块库和镀膜授权文件；缺少任何一项则失败。安装没有模块选装。仅目录、快捷方式等普通安装设置可由用户选择，完成页启动默认不勾选。默认按当前用户安装，不要求管理员权限。

`build-installer.ps1` 调用 `publish-windows.ps1` 再编译 Inno Setup 脚本。优先使用指定或已安装的编译器，否则下载固定版本并核对固定哈希；来源和许可见 [编译器说明](../packaging/windows/inno/SOURCE.md)。不因非商业使用而删除第三方授权材料。

RID 专用锁文件放在项目 `obj` 下，不覆盖源锁文件。发布禁用裁剪、AOT 和单文件合并，保留原生依赖和授权；每次使用新输出目录，失败的 `.partial` 产物不当成成功包。自动化可设置 `OPTICAL_SYSTEM_DESIGN_PACKAGE_NO_PAUSE=1`。

此前标准 1.0.3 完整包已经生成；它不是当前 1.0.4 源码的交付验收。旧包和用户现有安装不覆盖。此前包身份与验收见 [统一交付记录](BUILT_IN_MODULES_2026-10-13.md)，当前范围见 [当前状态](CURRENT_STATUS.md)。

## 软件版本与 GitHub 分发

[ProductVersion.props](../ProductVersion.props) 是默认版本唯一来源，普通构建、启动画面、“关于”、更新窗口、Windows 发布及安装器均使用该值；macOS 发布脚本将同一值写入产物的两个 bundle 版本字段。安装脚本显式 `-AppVersion` 仅用于受控测试或明确指定版本，要求规范的三段稳定版本（每段 0..65534），并将同一版本传入编译。Windows 发布额外核对实际程序集的产品版本，不允许安装器和软件版本漂移。修改版本号不会自动发布。

软件中选择“帮助 → 关于”，同一窗口显示当前版本/构建、最新可用版本/构建及完整安装包名称。“包含测试版”默认勾选，通过固定仓库 `VendettaMask/opticalsystem` 的有界公开列表检查正式版和 `v<版本>-test.YYYYMMDD` 测试发布；取消勾选使用正式 Latest 接口。仅点击检查才发出只读、无凭据请求，不发送工程或设置，不在启动时联网。检查中显示不定进度，结束后将无更新、网络/限流/超时、签名缺失或可用更新直接显示在正文，工具提示和辅助功能同步；404 不误报已是最新。改变范围会清除已验证候选并要求重新检查。始终拒绝草稿、含糊元数据、不匹配仓库/标签/架构、重复或未完整上传的安装包。

手动 OTA 先比较数字版本，低版本拒绝；相同版本仅允许测试包的签名构建编号高于当前主程序集中的编号，同一/旧构建不重复提示。正式版使用原 v1 签名信封，测试版使用 v2 并绑定云端 `GITHUB_RUN_ID`；测试标签、`UnsignedTest` 文件名、架构、完整大小/SHA-256、源码和载荷身份均须一致。检查、下载和安装交接使用相同的版本/构建判断。只有可信候选启用“下载更新”；下载显示进度且可取消，完整验证前不执行安装。请求仅访问固定仓库和允许的 HTTPS GitHub 下载主机，限制重定向、大小、空闲及总时长。“打开发布页”仅打开浏览器；无可信签名可手动下载。没有自动检查或后台安装。

下载验证成功后，用户另行点击“安装更新并重启”。主项目或公差数据有未保存修改时，可选择“保存并继续”“不保存”或“取消”；不保存也可更新，关闭阶段不会再次强制保存或重复询问。选择取消，或选择保存后取消保存对话框/保存失败，均不安装，并保留当前编辑及已验证下载以便重试。确认对话框和所选保存对话框属于当前“关于”窗口。两个内置模块仍有各自的退出保护；正常关闭及工作区/恢复状态持久化完成后，才在真实窗口关闭事件准备更新辅助进程。将主程序运行所需的 EXE、DLL、运行配置和原生依赖复制到本次独立更新缓存并逐文件核对 SHA-256；复制数量、层级和总大小有界，不复制工程、示例和用户设置。辅助进程从缓存副本运行，票据绑定原安装目录与活动载荷，等待原进程正常退出并确认旧 EXE 可独占访问；不会强杀窗口，也不放宽安装器的占用保护。

只接受当前用户注册且身份完整的标准版本化安装，开发/便携/隔离测试版本显示不可用原因。安装前重新验证签名、版本/构建、架构、原安装身份和文件哈希并保持只读锁；重复构建、降级、过期或不匹配票据拒绝。安装器保留原目录和快捷方式，不传 `/DIR`、`/GROUP` 或强制关闭参数，不重启 Windows；同版本不同载荷显示“更新”，相同载荷显示“修复”，仍使用原验证与事务恢复流程。成功核对载荷发布 ID 后启动新版本。失败保留工程/设置和诊断，恢复操作始终指向原安装目录。尚无 Windows 发布者签名的包明确解释系统的“未知发布者”提示，OTA 校验不替代发布者证书。旧客户端须正常退出所有窗口及失败辅助进程后手动安装一次修复包；已有安装器和下载文件不会被源码修改。修复范围与验证见[OTA 交接报告](OTA_HANDOFF_REPAIR_2026-10-24.md)。

此前只检查稳定版的客户端，需要先手动安装一次支持测试通道/构建编号的新引导包；不会因修改云端规则就自动获得新解析能力。旧 `IOnlineUpgradeInstaller` 仅保留不可用兼容边界，生产流程使用 `OtaDownloader` 与 `WindowsOtaHandoff`。软件端真实升级/取消/失败交互与 ARM64 仍须实机验收，实际回归及安装包交付状态见[当前状态](CURRENT_STATUS.md)。

OTA 云端签名、密钥备份和客户端实机验收分别记账。正式与测试 EXE 都在发布前生成 RSA-PSS/SHA-256 元数据，公钥不匹配或密钥不可用时停止；测试包额外校验构建编号与本次工作流一致，仍为预发布且不标 Latest。私有 GitHub 仓库保存密码加密备份，日常发布不依赖维护者电脑；恢复只需 GitHub 权限、加密文件与单独保管的恢复密码，不使用机器绑定、明文私钥附件或永久依赖 Actions artifact。步骤及安全边界见[云端发布指南](GITHUB_CLOUD_RELEASE.md#ota-签名与纯云端密钥备份)，实际结果见[当前状态](CURRENT_STATUS.md)。

安装包分发地址为 [GitHub Releases](https://github.com/VendettaMask/opticalsystem/releases)。[发布工作流](../.github/workflows/release.yml) 在 GitHub 托管 Windows / Apple Silicon Mac runner 编译完整解决方案，运行版本、升级、安装、内置模块、自动初始结构界面的定向测试及冻结证据/文档门禁，再生成自包含 Windows Setup EXE 或 Apple Silicon DMG。镜头表头/结构导航、材料行显示、二维结构选择和三维几何/显示模式另以定向集合检查，记录在 `lens-viewers.trx`，不扩大为全套或实机 GUI 验收。x64 对实际载荷的隔离副本运行完整性/内置模块探测；Windows ARM64 在 x64 runner 上仅交叉打包，不宣称原生执行验收。全部所选架构通过后自动上传并公开发布；不再止于草稿。本地构建本身不会联网发布。

推送与产品版本一致的 `v<产品版本>` 标签自动构建 Windows x64/ARM64 和 Mac `osx-arm64` 并发布正式版；推送 `v<产品版本>-test.YYYYMMDD` 标签发布测试预发布、不标 Latest。维护者也可在 Actions 的 **Desktop build and publish → Run workflow** 选择分支、已有标签及 `all` 或单个平台，勾选 `replace_existing` 才允许覆盖已有附件。手动刷新不移动旧标签，发布说明记录实际源码提交。详细操作和边界见[云端发布指南](GITHUB_CLOUD_RELEASE.md)。

每个 Windows 架构上传一个 EXE，文件名以 `-Setup-<八位小写源码短码>.exe` 结尾，并与签名中的完整源码一致。Mac 仅上传一个 Apple Silicon DMG；目前云端未配置 Windows 发布者证书，文件名明确标注 `Unsigned` 或 `UnsignedTest`；测试包可在“包含测试版”范围内参与签名 OTA，但不成为正式版或 Latest。新客户端兼容旧的无短码名称，旧客户端需先手动更新一次以识别新规则。SHA-256 写在发布说明，元数据、检查结果和替换前附件保存在 Actions 的 30 天工作流产物中，不上传 `.sha256` 或 `release.json` 到 Releases。按架构替换旧附件，先备份，再上传临时名新 EXE 并核对 GitHub 的大小/完整 SHA-256，最后启用新短码下载名；切换失败尝试恢复旧下载名，未选平台的附件和有效签名保持。未知附件或不可变发布停止处理，不随意删除。GitHub 自动提供的 Source code ZIP/tar.gz 不是安装附件，也不代表手动刷新后的源码提交。API 约定见 [GitHub Releases REST 文档](https://docs.github.com/en/rest/releases/releases)。

2026-10-23 的 [1.0.4 x64 测试预发布](https://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.4-test.20261019)来自 [GitHub 构建 39888939610](https://github.com/VendettaMask/opticalsystem/actions/runs/39888939610)，源码 `fbbe454db9322b32492f28025341aed5a6183367`，包身份 `1.0.4-win-x64-820a153a9b73`。该引导包包含检查反馈与测试构建 OTA，公开 v2 签名已通过内置公钥独立核验；当时仅一个 EXE，服务端哈希与签名一致，旧包由发布任务备份。旧客户端必须先手动安装本包，再等待更高云端构建进行真实同版本 OTA 测试；同版本安装仍走原修复事务。云端通过不代替实装，详情见[最新交付账目](validation/OTA_TEST_CHANNEL_FEEDBACK_2026-10-23.json)，此前范围保存在[上一包账目](validation/EXE_RELEASE_2026-10-23.json)。

2026-10-24 已向同一测试发布追加 [Apple Silicon Mac DMG](https://github.com/VendettaMask/opticalsystem/releases/download/v1.0.4-test.20261019/OpticalSystemDesign-1.0.4-osx-arm64-AdHocTest.dmg)，源码 `c68b210d`，Windows 原包及签名保持不变。Mac 当前使用 ad-hoc 签名、尚未公证，包内运行时和两个必装模块已做原生无界面与磁盘映像复查；实际范围见[最新 Mac 交付账目](validation/MACOS_RELEASE_CLOUD_2026-10-24.json)。

## 安装、修复和验证

安装器按固定产品标识识别当前用户的已有安装：检测到后进入“更新”或“修复”，锁定原安装目录、开始菜单目录、无开始菜单选项和桌面快捷方式配置，跳过这些选择页；命令行 `/DIR`、`/GROUP`、`/NOICONS`、`/TASKS` 等也不能另建一份。同版本相同发布 ID 按修复处理，不同发布 ID 的重包显示更新；新版本原位升级，低版本拒绝。平铺安装没有 `release-state.ini` 时仍根据卸载注册版本拒绝降级。复制前持锁恢复未完成事务，再复核注册信息与状态版本。目录冲突、坏版本和无法确认的安装身份停止处理，不猜测新目录或删除旧目录。全用户同产品安装阻止当前用户包另建副本，不自动跨范围迁移。原始维护范围见[安装维护策略](WINDOWS_INSTALLATION_POLICY_2026-10-22.md)，后续更新见[OTA 交接修复](OTA_HANDOFF_REPAIR_2026-10-24.md)。

标准构建的 `ProductAppId` 保持不变；自定义隔离标识限制为字母、数字和连字符、最多 56 字符，与生产标识的已验证精确注册键范围一致，防止 Inno 对过长键名归一化后绕过维护识别。`release.json` 同时记录主安装脚本、恢复脚本和维护策略脚本的 SHA-256。新的策略只随重新构建的安装包交付，旧下载包与当前已安装程序不会因修改源码自动改变。

版本载荷放在 `versions/<release-id>`，校验清单与全部文件后才切换入口。持久事务支持异常中断后的恢复；同发布重装修复损坏或缺失文件；目录文件锁协调应用、安装、卸载和维护。安全清理保留当前、上一与正在启动版本，并保留未知、额外或修改文件。用户不需要一键版本回退，不提供回退入口。

卸载移除安装器管理的文件，但不递归清空安装目录或删除用户工程/设置。编辑随包示例前应另存到用户目录。同包修复不是用户数据备份。详细边界见 [Windows 安装与恢复](WINDOWS_INSTALLATION_UPGRADE_2026-10-12.md)。

包检查使用 `scripts/verify-built-in-package.ps1` 的可丢弃副本；安装矩阵使用 `scripts/verify-windows-upgrade.ps1` 的隔离产品标识，不得拿用户正式安装做故障注入。参数及保护条件以脚本声明为准。真实断电、磁盘故障、跨登录会话、ARM64 和完整人工桌面验收仍未完成。

## 跨平台与便携发布

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
```

```bash
bash scripts/publish-cross-platform.sh
```

Mac 请运行 `bash scripts/build-macos.sh --test-package`，生成自包含 `osx-arm64` DMG；不生成 Intel 版本。打开 DMG 后将 `.app` 拖入“应用程序”，无需 SDK。当前使用 ad-hoc 完整性签名，尚无 Developer ID 签名/公证；Mac 通过发布页手动更新，不使用 Windows OTA。该脚本包含标准 bundle 排布、arm64 检查、模块/字体许可校验、原生无界面执行及 DMG 只读挂载复查；临时目录失败保留，新构建不覆盖旧包。

`publish-cross-platform.sh` 在 Mac 转入上述 DMG 打包，在其他主机保留 Windows 便携发布。脚本支持和原生载荷检查不等于全部桌面验收，系统及签名边界见[云端发布指南](GITHUB_CLOUD_RELEASE.md#apple-silicon-mac-包)。Linux CI 构建不代表已有 Linux 正式安装包。

## 基准与教学样例

```powershell
dotnet run -c Release --project tools/OpticalSystemDesign.Benchmarks/OpticalSystemDesign.Benchmarks.csproj
dotnet run -c Release --project tools/OpticalSystemDesign.Benchmarks/OpticalSystemDesign.Benchmarks.csproj -- --non-sequential 1000000
dotnet run --project tools/OpticalSystemDesign.NonSequentialSamples/OpticalSystemDesign.NonSequentialSamples.csproj -- samples/non-sequential
```

基准用于同机同运行时比较，不是普通 CI 的硬阈值；`PsfMtfSampling` 历史名称实际测几何 MTF，不认证 FFT/Huygens。非序列样例生成会写目标目录，不应指向用户工程位置；教学说明见 [样例入口](../samples/non-sequential/README.md)。

## 文档维护与格式

文本遵循 [.editorconfig](../.editorconfig) 和 [.gitattributes](../.gitattributes)：一般 LF，Windows 批处理 CRLF。更新功能时同时修正相应指南；当前结果集中维护，不向无关页面复制历史测试计数。

```powershell
pwsh -File scripts/verify-documentation.ps1 -Strict -OutputPath .tmp/documentation-audit.json
git diff --check
```

文档检查不重跑产品测试、不请求外部网址，也不改写冻结证据。历史步骤和结果见各专题报告与 [构建历史账本](validation/BUILD_VERIFICATION_HISTORY_2026-10-13.json)。
