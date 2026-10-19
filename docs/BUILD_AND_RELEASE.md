# 构建与发布

更新：2026-10-19。本文只说明当前命令和交付规则。最新验收见 [当前状态](CURRENT_STATUS.md)，历史构建验收原文已集中到 [历史账本](validation/BUILD_VERIFICATION_HISTORY_2026-10-13.json)，不作为当前通过结论或可直接执行的旧命令。

## 唯一产品入口

`OpticalSystemDesign.slnx` 是唯一产品解决方案，包含主程序、初始结构和镀膜内置模块，以及对应测试。模块 UI 在主程序进程内运行；不再维护独立 App 或解决方案，也不接受外部可执行路径。

普通构建与 CI 不探测、不启动 Zemax。外部比较工具、参考捕获、第三方源归档和测试数据不进入产品运行依赖。

## 本地构建

安装 [global.json](../global.json) 指定兼容范围的 .NET SDK（当前 10.0.300 / latestPatch），在仓库根目录运行：

```powershell
dotnet restore OpticalSystemDesign.slnx --locked-mode
dotnet build OpticalSystemDesign.slnx -c Debug --no-restore /m:1 /nr:false
dotnet build OpticalSystemDesign.slnx -c Release --no-restore /m:1 /nr:false
```

依赖有意变更时才使用 `dotnet restore OpticalSystemDesign.slnx --force-evaluate` 并审查锁文件。仓库通过 `Directory.Build.targets` 关闭 Avalonia 构建统计，不关闭 NuGet 安全审计。若运行中的产品锁住默认 App 输出，应正常关闭该特定程序再重建，不用另一个输出目录冒充默认产物更新。

## 测试

```powershell
dotnet test OpticalSystemDesign.slnx -c Release --no-build --no-restore /m:1 /nr:false
dotnet test labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/OpticalSystemDesign.InitialStructure.Tests.csproj -c Release --no-build --no-restore
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release --no-build --no-restore
```

后两条用于模块定向运行，不是单独产品构建。VSTest 需要本地套接字；受限环境中的权限或网络故障应如实报告，不能当作通过。全量/定向、Debug/Release、原生/Headless、内部/外部数值验收分别记录，不能相加。

CI 使用同一根解决方案，分别运行主测试、模块测试和比较工具的离线测试；长测试有 hang 诊断。最新流程与阈值以 [.github/workflows/ci.yml](../.github/workflows/ci.yml) 为准，真实 Zemax 捕获仍需对应许可和运行时。

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

软件中选择“帮助 → 关于”，同一窗口显示当前/最新正式版本及对应完整安装包名称；点击其中的“检查更新”手动查询，不再提供独立更新窗口、帮助菜单入口或命令。只有点击检查才向固定仓库 `VendettaMask/opticalsystem` 的 GitHub Releases API 发起只读、无凭据请求；不会启动或打开关于即检查，也不发送工程、处方或设置。正式通道拒绝草稿、预发布、不规范版本、不匹配仓库/标签的地址、重复或未完整上传的安装包。版本按数字及 SemVer 预发布规则比较，不按字符串排序，不提供降级。Windows 按当前进程架构匹配 `win-x64` / `win-arm64`；macOS 等平台可查看版本与发布页，但本阶段不提供安装包匹配。离线、超时、访问限制、没有公开正式版或缺少对应安装包的详情通过最新版本的工具提示及辅助功能帮助区分，不误报为“已是最新”。404 也可能是仓库不可访问。关于正文不显示仓库地址、常驻检查状态或安装声明。

“打开发布页”由用户主动打开浏览器，不执行安装包。在线安装接口 `IOnlineUpgradeInstaller` 已预留，但默认实现不可用且调用明确拒绝；界面不显示预留升级按钮。**本阶段没有后台联网、自动下载、自动安装、自动重启或在线升级完成的承诺。** 用户在发布页核对版本、架构及 SHA-256，下载完整包，保存工程并关闭程序后手动安装；所有必装模块整体升级。未签名包仍不能认证发布者身份；未来启用在线安装前须另行实现可信签名/载荷验证、用户授权、未保存工程保护、进程退出与失败恢复。

安装包正式分发地址为 [GitHub Releases](https://github.com/VendettaMask/opticalsystem/releases)。[发布工作流](../.github/workflows/release.yml) 在推送 `v<产品版本>` 标签后核对版本、构建完整解决方案、运行定向交付测试及原冻结门禁，再生成 x64/ARM64 完整未签名 Setup EXE、各自 SHA-256 和 release 元数据，最后只创建**发布草稿**。x64 对包副本运行完整性/内置模块探测；ARM64 在 x64 runner 上仅交叉打包，不宣称原生执行验收。维护者须另行核对正常 CI、资产和发布说明，手动发布草稿为正式版并标为 Latest，软件才会检测到。标签推送、草稿创建、公开发布均是独立外部操作，本地构建不会触发。

每个稳定发布及架构只上传一个生产安装器：`OpticalSystemDesign-<版本>-<RID>-Setup.exe` 或明确标注未签名的 `OpticalSystemDesign-<版本>-<RID>-Unsigned-Setup.exe`，并附同名 `.sha256`；`UnsignedTest` 不能作为升级安装包。不可把源码压缩包或工作流临时产物冒充 Setup。API 约定见 [GitHub Releases REST 文档](https://docs.github.com/en/rest/releases/releases)。

当前已按用户要求手动上传 [1.0.4 x64 测试预发布](https://github.com/VendettaMask/opticalsystem/releases/tag/v1.0.4-test.20261019)，含 `UnsignedTest-Setup.exe`、同名 SHA-256 与 `release.json`；不标 Latest，不进入正式更新检测。此包是含未提交修改的工作区构建，标签仅指向源码基线；GitHub 自动源码压缩包不能复现本包。测试包使用正常产品安装标识，安装可能升级已有同产品安装，应先保存并备份重要工程；本轮没有实际安装。软件内部仍显示 1.0.4，同版本正式包不会被判为更高版本，必要时应手动安装。实际身份与验证见[本轮账目](validation/VERSION_UPDATES_2026-10-19.json)。

## 安装、修复和验证

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

跨平台脚本默认自包含，发布 `osx-arm64`、`osx-x64`、`win-x64`、`win-arm64` 并检查全部模块；macOS 生成 `.app`。可用 `CONFIGURATION` 和 `SELF_CONTAINED` 配置。脚本支持不等于目标硬件验收，macOS 签名/公证和 ARM64 实机状态不可从 x64 结果推断。Linux CI 构建不代表已有 Linux 正式安装包。

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
