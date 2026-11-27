# 云端打包依赖锁修复

2026-11-27，基于 `bdc0f8bf44b08f9f60b378f3328e99880400b523` 的[三平台仅打包任务 45925161856](https://github.com/VendettaMask/opticalsystem/actions/runs/45925161856)在还原阶段失败，未生成安装包。Windows x64、Windows ARM64 和 Apple Silicon Mac 的日志均报告相同的 `NU1004` 依赖锁不一致。

镀膜分析接入后，Application 新增 CoatingDesign.Engine 引用，而以下项目的锁定依赖图仍是旧记录：

- `tests/OpticalSystemDesign.ZemaxComparison.Tests/packages.lock.json`
- `tools/OpticalSystemDesign.ZemaxComparison/packages.lock.json`
- `labs/InitialStructure/tests/OpticalSystemDesign.InitialStructure.Tests/packages.lock.json`

执行完整解决方案的 `dotnet restore OpticalSystemDesign.slnx --force-evaluate` 后，同时移除了镀膜引擎和 `CoatingRecovery20261102` 诊断项目锁文件中已不再依赖的 Application／SkiaSharp 传递记录。本次提交只更新五份锁文件与说明，不调整产品源码或包版本。

已用 `dotnet restore OpticalSystemDesign.slnx --locked-mode` 验证修复；完整 Release 构建采用云端同样的 `/m:1 /nr:false -p:ContinuousIntegrationBuild=true` 参数。具体命令和结果见[验证账目](verification.json)，三平台原始失败与本地验证回执见[归档](evidence.zip)。本地构建工作区还包含其他正在进行的修改，云端重试将只检出本次锁文件修复提交，云端结果单独判定。

原流程对产品源码使用固定提交检出，仅生成三平台自包含安装包并存入 Actions 30 天。重试需要更新该固定源码提交并启动新任务；直接重跑旧任务仍会使用旧锁文件。
