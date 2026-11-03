# 异世界羊皮纸绘图材质 — 2026-11-03

用户确认羊皮纸与钢笔幻想概念后，授权仅修改异世界主题，重点为分析、2D 和 3D。以下截图均来自产品实际 C#/Avalonia/Skia 绘制及共享 Core 的 Cooke 示例结果，不是生成式概念图。

## 实现范围

- 注册表仅为 Isekai 提供 `DrawingMaterial`；其他主题没有绘图材质覆盖。
- 暖纸色、确定性的矢量纤维、墨线细框、植物角饰、星盘和指南针。画刷、笔与 UI 线程内的矢量几何/纹理缓存复用，无动画定时器或随机闪烁。
- 分析图纸内局部资源使文字、坐标、图签及原公司矢量标志适合纸底。菜单、输入、数据页和已批准的功能图标仍使用原资源。
- 星盘只使用实际图例下方的剩余区域，空间不足时不画。装饰不命中指针、不改变逻辑内容顺序、不参与原图尺寸/字体/数据排布。
- 2D/3D 场景在实际光学几何前绘制纸面；实体模型保留材质图例、实际像面和紧凑信息栏。光线、镜片材质、标量色图和实际几何未重绘/替换为概念图。
- 实时与导出共用绘制。脱离可视树的导出网格显式先放纸面后放光路，避免依赖合成器的 Z 排序；图纸进入/离开材质主题时刷新自绘画布，保留交互状态。报告仍支持原强制亮色打印路径。

## 实际截图

| 页面 | 源图/导出 | 离屏界面 |
| --- | --- | --- |
| 惠更斯 MTF | [完整源图](previews/Huygens-MTF.png) | 同一绘图路径 |
| 点列图 | [完整源图](previews/Spot-Diagram.png) | 同一绘图路径 |
| 波前面 | [完整源图](previews/Wavefront-Map.png) | 同一绘图路径 |
| 二维光路 | [PNG](previews/TwoDimensional-OpticalLayout.png) | [界面](previews/TwoDimensional-OpticalLayout-live.png) |
| 三维布局 | [PNG](previews/ThreeDimensional-OpticalLayout.png) | [界面](previews/ThreeDimensional-OpticalLayout-live.png) |
| 实体模型 | [PNG](previews/ThreeDimensional-SolidModel.png) | [界面](previews/ThreeDimensional-SolidModel-live.png) |

图像为 1× 核对输出，保留源图/当前场景尺寸；用户默认画布尺寸、独立 DPI 与报告物理大小没有改动。

## 验证

默认 Release 输出构建（含 App 及两个内置模块依赖），零警告/错误。最终 [isekai-compositor-verified.trx](isekai-compositor-verified.trx)：**85 执行、85 通过、0 失败、0 跳过**，约 21 秒。重复运行不累计为新的通过用例。

覆盖 `IsekaiDrawingMaterialTests`（6 项）、`UniformDiagramExportTests`、`DiagramViewportFitTests`、`AnalysisLegendLayoutTests`、`MtfPlotTitleTests`、`AnalysisFrameDpiTests`、`ThemeResourceTests`、`ThemeRuntimeTests`。

新增真实场景/分析检查核对：

- 惠更斯 MTF、点列图、波前面和三种光路视图的纸面像素；实际离屏合成帧也验证主题切换后的纸面。
- Light/Dark/Lumen/Pixel → Isekai → 原主题的图像像素完全恢复；常规主题源图文本/布局保持，Pixel 仍保留其独立字体。
- 原始曲线对象、工程恢复状态、场景对象及相机方向不变；图例复选框和光线显隐仍改变实际输出并可恢复。
- 窗口缩小不改变源图像素，报告捕获保留材质；现有导出尺寸、DPI、完整外框、选择性曲线和 MTF 图签回归通过。

首次扩大回归记录 [isekai-regression.trx](isekai-regression.trx) 保留装饰子项逻辑顺序问题及旧图标色断言；[isekai-final.trx](isekai-final.trx) 保留脱离可视树的导出遮盖问题；[isekai-verified.trx](isekai-verified.trx) 是这些问题修正后的 85/85，中间状态不替代最终合成帧验证。图标旧断言改为已批准的 `IsekaiArcane.Signal`，产品图标及其颜色未改。

所有验证仅使用后台 Headless/Skia；未控制/重启用户窗口、安装软件或发布包。未重跑全量光学精度矩阵，不据此宣称全仓/全平台验收。初次沙箱内测试因测试运行器本机 TCP 绑定受限而中止，后续以允许本机通信的后台测试方式完成。

`git diff --check` 通过；本机没有 `pwsh`，未执行完整 PowerShell 文档审计。四份更新说明的 911 个本地链接目标检查通过，范围见 [document-links.json](document-links.json)，不包含全文重复段落/生命周期检查。

复现（仓库根目录）：

```sh
OPTICAL_SYSTEM_DESIGN_ISEKAI_CAPTURE_DIR="$PWD/validation/rechecks/2026-11-03/isekai-parchment/previews" \
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~IsekaiDrawingMaterialTests|FullyQualifiedName~UniformDiagramExportTests|FullyQualifiedName~DiagramViewportFitTests|FullyQualifiedName~AnalysisLegendLayoutTests|FullyQualifiedName~MtfPlotTitleTests|FullyQualifiedName~AnalysisFrameDpiTests|FullyQualifiedName~ThemeResourceTests|FullyQualifiedName~ThemeRuntimeTests' \
  --logger 'trx;LogFileName=isekai-compositor-verified.trx' \
  --results-directory validation/rechecks/2026-11-03/isekai-parchment \
  /m:1 /nr:false /p:UseSharedCompilation=false
```
