# 异世界圆角旧金边框 — 2026-11-03

用户在图内花饰位置确认后补充一张分析页参考，要求兼顾此类边框。当前增加圆角旧金包角、双层内框、顶部星芒，以及分析页侧边纸内细线装饰；保留上一张参考的图内花叶位置。

## 范围

- 新圆角皮革/旧金素材替代带横排符文的窄框，保持 28 DIP 外围尺寸；实际 Border 与 PNG 均采用圆角外轮廓，不在透明角外填成方框。
- 绘图区与图签增添主题自有双细线内框。分析两侧 18 DIP 纸内窄边绘制完整的缓存矢量藤蔓，不通过裁切大型花朵制造侧边碎片。
- 黄铜星盘仅在图例下方真实剩余空间足够时绘制，不添加占位、不遮盖复选框；2D/3D 保留此前四角百合、天体星盘、炼金瓶与晶簇。
- 图纸仍为 1200×900、图签 160 DIP；1× 带框图 1256×956，默认 PNG 画布 1920×1080。报告仍使用原源图比例/物理文字尺寸，内部细框/花饰随源图共享。
- 图标、其他主题、坐标布局、数值/曲线/物理配色、相机和图例筛选保持。装饰不接收指针，不用生成图替代真实光学结果。
- 圆角边框由内置 `image_gen` 制作，提示词、源图路径和哈希见[素材记录](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)。侧边藤蔓为 C# 矢量路径，按 UI 线程缓存；不增加绘制时的解析或图像解码。

## 实际渲染

| 页面 | 源图/导出 | 离屏界面 |
| --- | --- | --- |
| 惠更斯 MTF | [图](previews/Huygens-MTF.png) / [1080P](previews/Huygens-MTF-1080p.png) | [界面](previews/Huygens-MTF-live.png) |
| 点列图 | [图](previews/Spot-Diagram.png) | [界面](previews/Spot-Diagram-live.png) |
| 波前面 | [图](previews/Wavefront-Map.png) | [界面](previews/Wavefront-Map-live.png) |
| 二维光路 | [图](previews/TwoDimensional-OpticalLayout.png) | [界面](previews/TwoDimensional-OpticalLayout-live.png) |
| 三维光路 | [图](previews/ThreeDimensional-OpticalLayout.png) | [界面](previews/ThreeDimensional-OpticalLayout-live.png) |
| 实体模型 | [图](previews/ThreeDimensional-SolidModel.png) | [界面](previews/ThreeDimensional-SolidModel-live.png) |

截图来自实际产品的 C#/Avalonia/Skia 与共享 Core 结果，参考图中的曲线、数字、品牌和控件没有拷入产品。

## 验证范围

默认 Release App、两个内置模块依赖及测试构建完成，无警告/错误。最终 [rounded-final.trx](rounded-final.trx)：**86 执行、86 通过、0 失败、0 跳过**，约 28 秒。

覆盖 `IsekaiDrawingMaterialTests`（7 项）、`UniformDiagramExportTests`、`DiagramViewportFitTests`、`AnalysisLegendLayoutTests`、`MtfPlotTitleTests`、`AnalysisFrameDpiTests`、`ThemeResourceTests`、`ThemeRuntimeTests`。本轮在原源像素边界验证中加验四个外角透明、顶部边框连续不透明；源图内边缘/单像素标记仍逐行完整保留。既有检查继续覆盖其他四主题往返、内部花饰像素、源几何/字体/数据、相机、图例、fit/100% 及报告比例。

[rounded-border.trx](rounded-border.trx) 为首轮 86/86 阶段检查；目检发现侧边裁切碎片后，已改为完整藤蔓并重新验证，重复运行不累计。此前[图内花饰阶段](../isekai-inner-ornament/README.md)保留原范围。

仅后台 Headless/Skia，没有打开、控制或重启用户应用，没有打包发布。没有重跑完整光学精度矩阵，不据此宣称全仓/原生桌面验收。

`git diff --check` 通过；6 份相关 Markdown 的 935 个本地链接目标检查通过，记录为 `document-links.json`。本机无 `pwsh`，未执行完整 PowerShell 文档审计。

复现（仓库根目录）：

```sh
OPTICAL_SYSTEM_DESIGN_ISEKAI_CAPTURE_DIR="$PWD/validation/rechecks/2026-11-03/isekai-rounded-border/previews" \
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~IsekaiDrawingMaterialTests|FullyQualifiedName~UniformDiagramExportTests|FullyQualifiedName~DiagramViewportFitTests|FullyQualifiedName~AnalysisLegendLayoutTests|FullyQualifiedName~MtfPlotTitleTests|FullyQualifiedName~AnalysisFrameDpiTests|FullyQualifiedName~ThemeResourceTests|FullyQualifiedName~ThemeRuntimeTests' \
  --logger 'trx;LogFileName=rounded-final.trx' \
  --results-directory validation/rechecks/2026-11-03/isekai-rounded-border \
  /m:1 /nr:false /p:UseSharedCompilation=false
```
