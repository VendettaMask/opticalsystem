# 构建与发布

更新：2026-10-15。本文只说明当前命令和交付规则。最新验收见 [当前状态](CURRENT_STATUS.md)，历史构建验收原文已集中到 [历史账本](validation/BUILD_VERIFICATION_HISTORY_2026-10-13.json)，不作为当前通过结论或可直接执行的旧命令。

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

`Build-Exe.cmd` 是兼容入口。默认发布 Windows x64 Release 自包含未签名 1.0.3 安装包，输出到 `artifacts/installers/installer-win-x64-<编号>/`，含 Setup EXE、SHA-256、`release.json` 与载荷目录。分发只需 Setup EXE；哈希不证明发布者身份。首次构建需要联网还原并准备编译器，目标用户安装不需要 .NET 或联网。

脚本必须打包主程序、五个内部模块库和镀膜授权文件；缺少任何一项则失败。安装没有模块选装。仅目录、快捷方式等普通安装设置可由用户选择，完成页启动默认不勾选。默认按当前用户安装，不要求管理员权限。

`build-installer.ps1` 调用 `publish-windows.ps1` 再编译 Inno Setup 脚本。优先使用指定或已安装的编译器，否则下载固定版本并核对固定哈希；来源和许可见 [编译器说明](../packaging/windows/inno/SOURCE.md)。不因非商业使用而删除第三方授权材料。

RID 专用锁文件放在项目 `obj` 下，不覆盖源锁文件。发布禁用裁剪、AOT 和单文件合并，保留原生依赖和授权；每次使用新输出目录，失败的 `.partial` 产物不当成成功包。自动化可设置 `OPTICAL_SYSTEM_DESIGN_PACKAGE_NO_PAUSE=1`。

当前标准 1.0.3 完整包已经生成，不再是“只有测试包”；旧正式 1.0.2 文件和用户现有安装未覆盖。当前包身份与验收见 [统一交付记录](BUILT_IN_MODULES_2026-10-13.md)。

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
