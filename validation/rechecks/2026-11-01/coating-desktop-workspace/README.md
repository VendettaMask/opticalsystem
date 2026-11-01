# 镀膜双窗格与菜单设置：离屏证据

2026-11-01，最终版本 25/25 桌面回归和 3/3 三语布局检查通过，共 28 个不同用例身份。范围和限制见[实现记录](../../../../docs/COATING_DESKTOP_WORKSPACE_2026-11-01.md)。

## 实际预览

| 范围 | 预览 |
| --- | --- |
| 中文浅色主窗口，含共享 Core 实算光谱 | [工作区](workspace-light.png) |
| 深色主窗口 | [工作区](workspace-dark.png) |
| 820 DIP / 字号 18，保持双窗格和表格横向滚动 | [工作区](workspace-large-font.png) |
| 设计目标设置 | [设置页](design-settings.png) |
| 介质与膜料 | [设置页](media-settings.png) |
| 优化与采样 | [设置页](optimization-settings.png) |
| 公差 | [设置页](tolerance-settings.png) |

另含英文深色和日文大字号的四页设置图。图片来自真实 Avalonia/Skia 控件离屏渲染，没有用图片处理或网页原型替代界面。主窗口的 32 层处方为布局夹具，其实际光谱不代表优化达标。

## 日志与边界

- `desktop-tests.log`：25 项桌面回归。包括菜单、设置入口、生成编辑、协议保持、五种主题、材料管理、保存重开、关闭与未保存保护。
- `settings-tests.log`：3 项中文/英文/日文用例，每项检查四页设置的编辑器可达、值与实验指纹保持。
- `app-build.log` / `desktop-build.log` / `scoped-build.log`：独立输出的 App 完整依赖构建及两个测试程序集构建成功。
- `root-build-failure.log`：完整主测试项目当时因 `SequentialExampleGroup.DisplayName` 缺失构建失败，未计通过；本次未修改该失败文件。

三语检查使用下面的临时项目，仅链接仓库原 `LaboratoryPresentationTests.cs` 和安全离屏宿主，不包含或屏蔽原测试文件中的镀膜检查。项目的集合辅助类只声明禁止并行的离屏测试集合。原生桌面输入、默认输出更新和全仓测试没有执行。

## 复现

将 `CoatingUiTests.csproj` 和 `Collection.cs` 复制到仓库的 `.tmp/coating-workspace-ui/`，从仓库根目录运行。两个测试项目共同引用当前主程序，使用同一独立输出根目录。首次需正常还原依赖；离屏测试宿主需要允许其进程启动。

```powershell
$buildRoot = 'D:\Projects\opticalsystem\artifacts\coating-table-20261101'
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -m:1
dotnet build labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -m:1
dotnet build .tmp/coating-workspace-ui/CoatingUiTests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false -m:1
$env:COATING_UI_EVIDENCE = "$buildRoot\workspace-screenshots"
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false --no-build --no-restore -m:1 --filter 'FullyQualifiedName~DesktopTests'
$env:OPTICAL_LAB_PRESENTATION_CAPTURE_DIR = "$buildRoot\settings-screenshots"
dotnet test .tmp/coating-workspace-ui/CoatingUiTests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false --no-build --no-restore -m:1 --filter 'DisplayName~coating'
```
