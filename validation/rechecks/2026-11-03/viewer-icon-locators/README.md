# 视图按钮稳定定位：2026-11-03

## 原云端失败与修复

[三平台运行 41619105284](https://github.com/VendettaMask/opticalsystem/actions/runs/41619105284) 使用源码 `10a885a16a2817ef9b33a35d91ea68a37d394863`，三平台均完成构建和前置发布检查，但在视图回归阶段各有 10 项失败：测试仍按 `LocalIcon.IconName == refresh-cw` 查找按钮，产品已使用 `action-recalculate`。安装包生成及发布被跳过。

给实际同步按钮增加稳定名称 `ViewerSynchronizeButton`，在 `ViewerConfigurationTests`、`ViewerSettingsInputThemeTests` 及同样依赖旧图标的 `ViewerSettingsButtonThemeTests` 中使用该名称。保留实际按钮、原事件处理、布局、输入、配置切换、主题和悬停断言，不增加回退选择器，不跳过测试或放宽阈值。ScientificIconTests 对备用图标目录的覆盖仍保留，那里不是查找 UI 按钮的定位器。

## 本地验证

在失败版本的独立稀疏检出中应用同一份修复并运行；未使用主工作区里其他任务尚未完成的 Core 修改。检出包含产品源码、必需的字体/工程资源和测试项目显式引用的固定夹具；初次构建发现稀疏路径遗漏一份已跟踪的 ZMX，补齐原文件后正常完成构建，没有修改项目或夹具。

```powershell
dotnet restore tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj --locked-mode
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ViewerSettingsInputThemeTests|FullyQualifiedName~ViewerConfigurationTests|FullyQualifiedName~ViewerSettingsButtonThemeTests" --logger "trx;LogFileName=viewer-icon-locators.trx" --results-directory validation/rechecks/2026-11-03/viewer-icon-locators
```

最终锁定还原成功，隔离检出的正常 Release 输出构建零警告/错误。**24 通过、0 失败、0 跳过**，测试耗时约 24 秒，原始 [TRX](viewer-icon-locators.trx) 保留。包括三种视图模式、中英日文/窄窗工具条、实际配置选择、参数表单布局及主题悬停。测试仅在后台 Avalonia Headless 会话运行；为允许测试运行器和本机 testhost 通信，实际测试在沙箱外执行。未操作或重启用户的前台软件。

此结果只覆盖本次修复与三组定向回归，不代表完整 65 项云端视图门禁、后续图纸/导出门禁、三平台安装包或全仓测试已经通过。新的云端构建和发布按实际 Actions 结果分别确认。
