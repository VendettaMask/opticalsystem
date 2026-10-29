# 三维布局完整图签，实体模型保持原状

日期：2026-10-29。范围：三维布局底部图签、相机 PNG、四格式设计报告的三维布局图片，以及实体模型的保护性回归。

## 最终行为

- 二维图与三维布局复用 `PlotCaption.ForLayoutScene`，包含图名、生成时间、当前结构、焦距/F 数/总长，正式公司 SVG 标志、版本和工程文件名/名称。图签最小高度 132 DIP，窄窗上下排列，长名称增高。
- 三维非序列布局保留对象数、显示光线数、Z 范围、布局结果准备/一致/过期隐藏/过期显示状态；已有过期光线警告横幅不变。
- 相机 PNG 绘制实际已排好的完整图签，保留当前相机和平移/缩放。打开保存选择器前固定图像与名称，保留取消、隐藏底部框架、分配上限和原子写入。
- Word、PDF、Markdown、HTML 的二维与三维布局 PNG 均使用完整图签及同一捕获时间。
- 只对三维布局限制悬浮视角工具条的可用高度，空间不足时滚动，避免换行按钮盖住图签；按钮可滚动到末项，保留字体、视角、方向指示与比例尺。
- 实体模型保留紧凑参数栏、玻璃材质图例、原有导航布局和导出结构，不增加 Logo 图签。

## 验证方法与范围

只运行后台测试与 Avalonia Headless/Skia 离屏绘图，没有启动或控制前台应用，没有执行发布。未运行全仓测试，未重新逐页验收 Word/PDF 纸面排版或进行跨平台原生交互。

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
$env:OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\viewer-3d-caption-captures'
$env:OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\viewer-3d-caption-reports'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~ViewerImageExportTests|FullyQualifiedName~ViewerConfigurationTests|FullyQualifiedName~Viewer3DGeometryTests.ViewButtonsShowLabelsAndTrackTheActualCamera|FullyQualifiedName~Viewer3DGeometryTests.LayoutIsWireframeAndShadingControlsBelongToSolidModel|FullyQualifiedName~MarkdownReportImageTests.FullReportRendersRealLayoutsPlotsAndPaginatedTextWithVisiblePixels'
```

初次检查发现小窗口的视角工具条换行后越过画布下沿，已增加限高滚动。原相机交互测试假定画布中心没有悬浮按钮，新图签减少画布高度后该点会命中视角按钮；现使用工具条侧边的实际画布区域验证自由旋转，不改变视角匹配断言。初次 69 项运行中的 2 项失败保留于[日志](test-initial.log)和[TRX](caption-initial.trx)，未计为通过。

## 最终结果

默认 Release App/测试项目构建零警告、零错误，见[构建日志](build.log)。最终定向 **69/69** 通过，零失败、零跳过，见[最终 TRX](caption-final.trx)和[日志](test-final.log)：

| 范围 | 用例数 |
| --- | ---: |
| 二维/三维完整图签与 PNG、深浅主题/三语言/大字体/长名称、非序列四种结果状态、取消/换文件快照/2× 比例/分配保护，实体模型无图签且保留完整材质图例 | 34 |
| 各视图独立配置选择、参数与工具栏适配 | 21 |
| 三维布局与实体模型的显示边界、键盘/鼠标/预设/高亮与适配 | 10 |
| 四格式完整报告，二维与三维 PNG 均检查页脚公司标志区域 | 4 |

[最终格式检查](format-final.log)退出 0、无诊断，`git diff --check` 通过。

已复核真实离屏图片：[三维界面](3d-viewer-preview.png)、[窄而矮窗口及导航滚动](3d-viewer-narrow.png)、[当前视角 PNG](3d-export-preview.png)、[深色英文](3d-caption-dark.png)、[大字体](3d-caption-large-font.png)、[报告三维图](3d-report-preview.png)、[实体模型原参数栏和材质图例](solid-export-unchanged.png)。图签保持完整；实体模型没有新增公司标志图签。报告布局 PNG 为 1200×784。
