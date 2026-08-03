# 构建与发布

## 文档同步规则

每项已完成代码修改必须在同一任务中更新相关文档。文档必须区分已实现、计划和仅兼容行为。测试数量或验证日期变化时，所有引用该基线的文档必须同步；代码、测试、文档和最终报告必须一致。

## 本地构建

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --locked-mode
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
```

有意修改依赖时再更新锁文件：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx --force-evaluate
```

## 测试

```bash
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

VSTest 会打开本地套接字；受限沙箱可能需要额外权限。普通修改优先运行相关定向子集，只有跨模块、高风险或发布验证才要求全量测试。

## 性能基准

```bash
dotnet run -c Release --project tools/OptilandWorkbench.Benchmarks/OptilandWorkbench.Benchmarks.csproj
```

基准覆盖 10,000 和 100,000 条光线、20 个表面、不同历史保留模式、PSF/MTF 采样和 Monte Carlo。输出为 CSV；耗时用于同机同运行时比较，不是 CI 硬阈值。

## 启动桌面应用

- Windows：`Run-Optiland.cmd`
- macOS：`Run-Optiland.command`

脚本依次执行 `dotnet clean`、`dotnet build` 和 `dotnet run --no-build`。清理只涉及项目构建输出，不删除 `%APPDATA%/OptilandWorkbench` 或 macOS 对应用户目录中的工程、主题和会话数据。

终端等价命令：

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## 发布目标

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

截至 2026-08-03：

- 仓库包含 `662` 项回归测试；
- 当前全量基线为 `662/662`；
- 2 项新增 Avalonia 首帧/主题回归通过相关 16 项定向子集；
- 4 项新增 Dock 空宿主、会话和锁定回归通过 `12/12` 窗口布局子集；
- 平铺/层叠修复复用了现有测试，验证浮动页自动回收到主文档区并进入内部 MDI；合并命令验证回收后恢复标签模式；
- 最近 App 项目构建结果为 0 警告、0 错误。

完整发布前仍应重新运行锁定还原、解决方案构建和全量测试，不得把定向验证表述成新的全量基线。

Python 基准夹具只在有意更新固定的 `optiland==0.5.8` 契约时重新生成；生成后必须审核差异并运行全量测试。
