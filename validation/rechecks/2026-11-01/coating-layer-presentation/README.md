# 镀膜膜层表离屏验证

2026-11-01，最终版本 13/13 定向测试通过，日志为 `tests.log`。`pre-color-tests.log` 是加入色点前的上一轮 13 项记录，不与最终 13 项相加。

预览由真实 Avalonia/Skia 控件离屏渲染，不是 HTML 原型或图片修饰。卡片为 2 倍像素密度；完整窗口为离屏帧。输入是人为构造的 32 层布局夹具，不是优化结果。

| 范围 | 卡片 | 完整窗口 |
| --- | --- | --- |
| 浅色，1200 DIP，字号 13 | [膜层表](layer-card-Light-1200-13.png) | [窗口](layers-Light-1200-13.png) |
| 深色，1200 DIP，字号 13 | [膜层表](layer-card-Dark-1200-13.png) | [窗口](layers-Dark-1200-13.png) |
| 浅色，820 DIP，字号 18 | [膜层表](layer-card-Light-820-18.png) | [窗口](layers-Light-820-18.png) |

最终 App、测试程序集的独立构建日志为 `app-build.log` / `test-build.log`。完整依赖重建失败记录保留在 `dependency-build-failure.log`；默认输出被使用中的主程序锁定。构建和测试边界见[说明](../../../../docs/COATING_LAYER_PRESENTATION_2026-11-01.md)。

使用既有独立依赖的构建与测试命令：

```powershell
$buildRoot = 'D:\Projects\opticalsystem\artifacts\coating-table-20261101'
dotnet build src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false --no-restore --no-dependencies -m:1
dotnet build labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false --no-restore --no-dependencies -m:1
$env:COATING_UI_EVIDENCE = "$buildRoot\screenshots"
dotnet test labs/CoatingDesign/tests/OpticalSystemDesign.CoatingDesign.Tests/OpticalSystemDesign.CoatingDesign.Tests.csproj -c Release -p:ArtifactsPath=$buildRoot -p:RuntimeIdentifier=win-x64 -p:UseAppHost=false --no-build --no-restore -m:1 --filter 'FullyQualifiedName~LayerCellsRetainMaterialIdentity|FullyQualifiedName~ThicknessFormattingDoesNotRound|FullyQualifiedName~RealMouseWorkflowEditsLayers|FullyQualifiedName~SharedThemesSwitchWithout|FullyQualifiedName~SaveOpenExportButtons'
```
