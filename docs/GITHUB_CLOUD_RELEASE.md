# GitHub 云端构建与发布

源码、依赖锁文件、Windows 安装器、Apple Silicon Mac DMG 及全部打包脚本均随仓库维护，不需要维护者电脑持续开机。入口是 [Desktop build and publish](https://github.com/VendettaMask/opticalsystem/actions/workflows/release.yml)。该流程生成完整安装包及两端 OTA 元数据；软件中的下载与安装均由用户发起。

2026-10-27 较早批次已从 `a31fb444` 完成三平台构建与公开替换，原测试标签不动。该阶段的下载、Windows 签名与 Mac 布局崩溃修复见[原批次账目](validation/CLOUD_RELEASE_2026-10-27.json)；后续 Mac OTA 接入、当前实际安装包及最新验证范围以[当前状态](CURRENT_STATUS.md)为准。

## 一键更新当前测试包

1. 将要交付的代码同步到 GitHub `main`，在 Actions 打开上述工作流，点击 **Run workflow**。
2. 选择 `main`，`release_tag` 保持 `v1.0.4-test.20261019`，平台选择 `all` 构建全部；仅更新 Windows x64 时选 `win-x64`，仅补充 Mac 时选择 `osx-arm64`。
3. 替换所选平台的已有包时勾选 `replace_existing`；新增尚不存在的 Mac 包无需覆盖开关。需要仓库写权限。
4. 等所有步骤成功，再从原测试发布页下载对应的 EXE 或 DMG。失败记录在 Actions 查看，不把失败的流程当成新版交付。

手动刷新旧发布不会移动标签。包内与发布说明中的源码提交才是本包实际来源；该标签的自动 Source code 压缩包仍是标签原始代码。操作入口和权限说明见 [GitHub 手动运行文档](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)。

项目增加或调整 `ProjectReference` 时也必须同步受影响的 `packages.lock.json`：在维护端执行 `dotnet restore OpticalSystemDesign.slnx --force-evaluate`，检查差异，再以 `dotnet restore OpticalSystemDesign.slnx --locked-mode` 和完整 Release 构建复验后提交。不要在云发布流程中关闭锁定还原。2026-10-27 首次发布因 Persistence → Engine 引用未同步而被此门禁阻断，修复范围见[阶段记录](../validation/rechecks/2026-10-27/cloud-release/README.md)；当前实际交付结果以[状态页](CURRENT_STATUS.md)为准。

Windows EXE 文件名末尾显示本次实际构建源码的八位小写提交短码，例如 `OpticalSystemDesign-1.0.4-win-x64-UnsignedTest-Setup-6edad153.exe`，发布标题也显示同一短码。打包记录、签名中的完整源码和文件名短码必须一致；每次源码变更会产生不同的 EXE 下载地址，测试标签与发布页地址保持不动。分平台源码仍以正文逐项记录为准，更新 Windows 不代表同时重建 Mac。

新客户端同时识别带短码和原有无短码的文件名；原有客户端的文件名规则不会自动改变，需要从发布页手动覆盖更新一次，之后可正常使用在线更新。新名称也须通过原有公钥、完整大小/SHA-256、架构、版本和构建编号校验，不以短码代替签名。

手动流程只重建所选平台，已有其他平台的附件及有效 Windows OTA 签名保持不变，不表示它们也来自本轮源码；要求全部平台统一来源时选择 `all` 或使用新版本标签。正文分别记录新包的源码，保留其他平台原记录。

## 发布新版本

先修改 `ProductVersion.props`，更新 `packaging/windows/RELEASE_NOTES.md` 并完成检查，再推送代码和匹配的版本标签。`v1.0.5` 会构建 Windows x64、Windows ARM64 和 Mac Apple Silicon 并自动公开为正式版；`v1.0.5-test.20261022` 会公开为测试预发布、不标 Latest。标签和产品版本不一致直接失败。推送普通分支只运行通常的 CI，不发布安装包。

首次公开前创建草稿暂存已验证的安装包，所有所选平台通过后才公开。刷新已有发布时按所选架构查找旧附件，兼容旧文件名与此前短码，先保存旧附件副本，再以临时名称上传完整新包并核对服务端 SHA-256，随后切换到新的带短码下载名并移除同架构旧附件。其他平台的附件、正文来源及有效签名保留，每个 Windows 架构只留一个 EXE。GitHub 没有多附件原子事务；短暂的临时名和切换失败仍可能出现，错误时会尝试恢复旧名称，旧文件和备份不提前删除。发现未知附件或不可变发布时停止，需要维护者检查。

## 下载区与记录

### OTA 签名与纯云端密钥备份

Windows 正式版与测试版在修改 Release 前都必须验证 RSA-4096 签名密钥与产品内置公钥相同，签名版本、架构、完整 EXE 大小/SHA-256、源码提交和载荷发布 ID；签名元数据放在发布正文的隐藏标记，不增加 Release 附件。正式版保留兼容的 v1 信封，测试版使用 v2，额外签名云端 `GITHUB_RUN_ID` 构建编号；该编号也写入主程序集及打包记录，发布时必须与本次工作流一致。测试预发布仍不标 Latest、不伪装正式版，但新版客户端默认“包含测试版”，可按更高版本或同版本更高构建编号 OTA；未勾选时仍只检查正式版。重跑同一个 Actions run 不产生更高编号，要交付新的测试构建应重新 Run workflow。此签名不是 Windows Authenticode，不能消除未知发布者提醒。

正常签名只读取主仓库的 `OPTICAL_OTA_SIGNING_KEY` Actions Secret。经用户批准建立的私有仓库 `VendettaMask/opticalsystem-ota-vault` 提供独立 Backup/Verify 工作流：恢复密码仅通过该私有仓库的 `OPTICAL_OTA_RECOVERY_PASSWORD` Secret 输入，不使用聊天、普通变量或可见的工作流输入。Backup 生成 AES-256-GCM、PBKDF2-HMAC-SHA256（600000 次）的加密文件并提交到私有仓库，再下载并解密核验；Verify 不读取原始签名 Secret，仅用已提交的加密文件和密码恢复并验证同一签名身份。备份不绑定电脑或 Windows 账户，也不依赖会过期的 Actions 产物。只需维护者保管一个独立强密码；软件用户不需要它。

若主签名 Secret 丢失，可从私有仓库网页复制完整加密 JSON 到主仓库的 `OPTICAL_OTA_ENCRYPTED_BACKUP` Secret，再将同一个密码填入主仓库 `OPTICAL_OTA_RECOVERY_PASSWORD` Secret。发布脚本在原始签名 Secret 缺失时从这两个 Secret 在内存中解密，仍必须匹配同一公钥；原 Secret 存在但损坏时直接拒绝，不静默使用备用路径。不需要个人访问令牌、旧电脑或把明文私钥取出。密码修改不是自动重加密；已有备份密码不匹配时停止并保留原文件。全部基于 GitHub 不代表跨平台灾备：失去账户/私有仓库访问权限时，仅恢复密码不足以找回文件。

签名基础设施、客户端交付与真实安装分别记账；旧稳定通道客户端不会因云端规则改变就认识测试包，须先手动安装新引导包。客户端、真实安装和主仓库恢复发布的实际状态查看[当前状态](CURRENT_STATUS.md)。私有仓库操作说明的公开模板见[维护说明](../packaging/ota-vault/README.md)，早期备份验证见[云端密钥账目](validation/OTA_CLOUD_KEY_VAULT_2026-10-22.json)。

- Releases 只附加 Windows EXE 和 Apple Silicon DMG；SHA-256 放在发布正文，构建元数据、测试结果、旧附件备份放在 Actions 工作流产物内，保留 30 天。仓库可见性和 GitHub 访问权限同样适用于这些产物，不应放秘密。
- 自动生成的 Source code ZIP / tar.gz 是 GitHub 固定提供的源码入口，不是额外上传的安装文件。
- 发布用本次运行的短期 `GITHUB_TOKEN`，只在发布任务授予仓库内容写权限，不需要在代码中保存个人令牌。
- 当前包未作 Windows Authenticode 发布者签名；OTA 内置公钥验签不替代 Windows 证书。云端打包不会消除未知发布者提醒，也不会自动下载、安装或关闭用户软件。

## 验证范围

Windows 在 `windows-latest`、Mac 在 Apple Silicon `macos-15` runner 构建完整解决方案，但只运行与交付、安装、界面和原生预设相关的定向测试，不把它称为全仓库测试。分析页可读排布/图注、图例、字体和报告契约为发布门禁，完整页面渲染与 PNG 导出分别在独立 Skia 进程执行，避免与绘图桩共享字形缓存；相关图片和 TRX 留在 Actions 验证产物中。冻结证据与文档校验保留，Windows x64 包执行内置模块完整性检查，Windows ARM64 仍仅交叉打包；Mac ARM64 在原生 runner 执行包内程序与磁盘映像复查。AutoLens 目标预设的存在和界面通过不等于设计达标；此前 24 次固定 CPU 运行仍为 0/24 达标。

流程源码见[工作流](../.github/workflows/release.yml)与[安全替换脚本](../scripts/publish-github-exe.ps1)，实际执行和交付结果以[当前状态](CURRENT_STATUS.md)为准。

镜头结构导航由 [test-lens-navigation-release.ps1](../scripts/test-lens-navigation-release.ps1) 动态发现全部用例，再按测试方法启动独立测试进程，避免完整 Dock 工作区与其他方法共用原生 Skia 状态。每组必须与发现的用例数相同、全部通过且运行完整结束，失败、漏跑或跳过直接拒绝；TRX 与 `lens-navigation-isolation.json` 随平台检查产物保留。图表页脚构建测试也须在独立真实绘图会话中创建矢量品牌控件，不能依赖其他测试留下的渲染平台。

固定图纸的窄窗口门禁保留 1200×900 原始尺寸，通过滚动将视角按钮带到视口中部，并有界等待滚动后的命中几何就绪，再验证真实鼠标点击及键盘切换。等待不会重试错位点击，也不直接触发 Click 事件；目标一直被遮挡仍须失败。设置表单的紧凑排布直接约束表单宽度，不能以缩小窗口来要求整张图纸重排。文字超出固定图框时，门禁验证可见提示及导出拒绝；另用可读字号检查完整交互，不绕过提示层或缩小产品字体。

手动输入 `validation_only=true` 时仍执行所选平台全部验证，但不生成安装包、不上传 `cloud-package-*`、不执行发布任务；默认值为 false，正常交付流程不变。经用户于 2026-10-27 明确授权，失败的 Mac 云任务仅保留临时构建机上 `testhost` / `dotnet` 的系统崩溃报告，最多五份、每份不超过 20 MiB，随已有检查工件按 30 天保留；不读取维护者本机报告，不采集进程内存转储。导航步骤同时启用 .NET `DOTNET_EnableCrashReportOnly=1`，只生成 JSON 线程/调用栈报告，不生成 dump，并保留不含处方数据的测试阶段标记。该开关的无转储语义依据 [.NET runtime 实现](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/debug/createdump/createdumpmain.cpp)。

仅验证的 Mac 任务还可通过 [后台原生栈观察器](../scripts/test-macos-navigation-backtrace.py) 在已知导航阶段附加 LLDB。目标 PID 必须由该测试写入且进程命令包含 `testhost.dll`，仅输出线程调用栈和栈指针寄存器，完成后分离调试器；不保存内存、不改测试断言，常规发布不附加调试器。该脚本拒绝在用户本机运行。

LLDB 使用官方 `-k` 故障后命令打印栈，避免普通 `-o` 命令在异常处停止而漏记；临时 runner 单独安装固定版本 `dotnet-sos` 10.0.750501，仅执行不含参数/局部变量的 `clrstack`。诊断工具不进入产品依赖或安装包，也不在本机安装。依据 [LLDB 命令行说明](https://lldb.llvm.org/man/lldb.html)和 [Microsoft SOS 说明](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-sos)。

Mac 工作区路径散列已改用固定参数的 UTF-8 `realpath`，保留实际文件系统大小写与不存在路径的后备行为。旧声明将可变参数 `fcntl(F_GETPATH)` 当作固定参数调用，在 Darwin ARM64 上不符合调用约定，有写坏调用栈的风险。约定见 [Apple ARM64 说明](https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms)，实际文件名恢复见 [Apple libc realpath 实现](https://github.com/apple-oss-distributions/Libc/blob/main/stdlib/FreeBSD/realpath.c)。Mac 云门禁增加全部 `WorkspaceSessionTests`，包括真实 Unicode 文件连续保存/读取及原有文件系统大小写语义检查；原生回归结果以状态页为准。

2026-10-26 已从 `45cce15b` 完成 Windows x64 云端重建与安全替换，EXE 末尾为 `-Setup-45cce15b.exe`。首次带码任务的依赖误报与旧界面夹具已修复，随后全部发布步骤通过；公开附件的源码、文件哈希、更新签名和下载均已核对。Mac 附件及来源、原测试标签保持，Windows 新名称使用新的下载地址；旧客户端需手动覆盖安装一次。实际运行、跳过范围与载荷证据见[本轮交付账目](validation/WINDOWS_CODED_EXE_RELEASE_2026-10-26.json)，没有替换用户本机安装。

2026-10-24 曾从 `11de8bce` 分别重新构建并替换 Windows x64 与 Apple Silicon Mac 的现有测试包，新增分析排布、完整渲染和 PNG 导出门禁，当时保留原标签及下载地址。两平台的原阶段执行、去重测试、包内检查和签名核对见[该阶段交付账目](validation/ANALYSIS_UI_CLOUD_2026-10-24.json)。

2026-10-24 的 Windows OTA 交接修复已执行云端重新打包并替换原测试 EXE，同时保留 Mac 附件与原标签。该流程还检查从完整自包含包准备的辅助进程缓存副本；具体源码、真实测试和公开下载核对见[本轮交付账目](validation/OTA_CLOUD_RELEASE_2026-10-24.json)。旧客户端仍需要正常退出后手动安装一次新完整包。

2026-10-22 已[实际跑通 x64 手动触发后的全自动构建与发布](https://github.com/VendettaMask/opticalsystem/actions/runs/39715666744)，没有操作用户桌面或本机安装。定向测试 97/97 和 23/23、包检查 14/14；首次发布的列表解析失败及其修复也保存在[交付账目](validation/GITHUB_CLOUD_RELEASE_2026-10-22.json)。本次没有实际触发新版本标签、执行 ARM64 包或重新认证完整光学矩阵。

## Apple Silicon Mac 包

Mac 仅生成 `osx-arm64` 的自包含 DMG，包含 .NET、两个必装模块、字体与许可。用户打开磁盘映像，将 `.app` 拖到“应用程序”；不要求安装 SDK。最低系统声明为 macOS 14，实际检查范围以账目为准；.NET 支持范围见 [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/core/install/macos)。

构建使用标准 Resources/Frameworks 目录和内部相对链接，检查原生代码均为 arm64，逐项 ad-hoc 签名后校验整个 bundle。生成 DMG 后只读挂载，检查整个 bundle 不包含独立模块启动程序，再运行包内 `--verify-installation` 并比较完整文件清单，确认运行时、初始结构和镀膜模块仍在主进程可用，不读取或写入用户设置。这是原生无界面载荷检查，不是完整 GUI 验收。

当前无 Apple Developer ID 证书和公证凭据，文件名标记 `AdHoc` / `AdHocTest`，不宣称已公证。首次打开可能需要用户在系统设置中批准；不自动解除隔离或关闭安全保护。带更新器的 Mac 版本在“帮助 → 关于”检查更新，下载并验证后再安装和重启。早期版本需先手动安装一次带更新器的完整 DMG。打包入口见 [build-macos.sh](../scripts/build-macos.sh)、[载荷检查](../scripts/verify-macos-package.sh)和[安装说明](../packaging/macos/README.txt)。

Mac 使用固定版本和 SHA-256 的 Sparkle 2.10.1。沿用现有三个 OTA Secrets，通过 HKDF-SHA256 从既有 RSA 私钥派生独立 Ed25519 签名种子，公钥固定在包内；不新增 Secret，不改变 Windows 公钥或原签名信封，现有加密备份可恢复同一 Mac 身份。派生种子只在权限受限的云端临时目录出现，使用后清理，不上传到工件、Git 或发布页。

最终发布作业在 Apple Silicon Mac 执行：先验证根密钥和 Mac 包身份，签署 DMG 与 Appcast，再调用原附件发布器上传并核对三平台安装文件。仅在实际 Mac 公开附件的名称、大小及完整 SHA-256 与已签名条目一致后，更新远端 main 的 `updates/macos/appcast.xml`，并逐字读回。更新文件 SHA 同时用于并发保护；清单被其他作业修改时停止，不覆盖。Windows-only 刷新不改 Mac 清单。

Mac Appcast 的地址固定为 GitHub raw main 文件；稳定版使用默认通道，测试版使用 `test` 通道。客户端要求清单及完整更新包的 Ed25519 签名，在解包前验签。`CFBundleVersion` 由发布工作流的递增 run number 和 attempt 映射为符合数字位数限制的三段版本；显示版本仍来自 `ProductVersion.props`。不要重置发布工作流的编号或手工降低已签名清单的构建号。详细接口与验证范围见 [Mac OTA](MAC_OTA.md)。
