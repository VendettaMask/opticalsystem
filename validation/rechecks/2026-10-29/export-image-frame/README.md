# 导出图片统一外边框

日期：2026-10-29。范围：分析相机 PNG、二维/三维布局与实体模型相机 PNG，以及 Word/PDF/Markdown/HTML 设计报告中的全部布局、分析和分页文字图片。

## 实现

- 使用共用 `ExportImageFrame.Capture` 绘制完整图片外边框。细线为 1 DIP，沿用当前主题的坐标轴画刷，在浅色/深色背景下均保持可见。
- 原图四周各增加 1 DIP；图面、坐标、图例、图签与参数栏不被边线覆盖。1× 时输出宽高各增加 2 像素，其他比例按 DPI 取整。
- 对原来已排好的页面绘制，不移动控件、不改布局或画布视角；窗口、工具栏、设置层与焦点标记仍按各自原导出范围排除。保存选择器前冻结内容、取消保护和原子写入保留。
- 图像分配上限把边框尺寸计算在内：单边不超过 32768 像素，总像素不超过 33554432，分配前拒绝超限。
- 报告在生成源 PNG 时加入边框，四格式内嵌/引用的是同一张带框图片；不依赖特定阅读器的 CSS 或边线设置。公司标志本身不增加独立边框。
- 实体模型只增加导出整图外框，保持原来的紧凑参数栏和材质图例，不增加 Logo 图签。工作台显示不因导出边框而改变。

## 验证方法

所有测试均在后台通过 Avalonia Headless/Skia 离屏运行，不打开或控制用户桌面。只运行变更涉及的图片导出回归，没有运行全仓测试、跨平台原生交互或发布。没有重新逐页验收 Word/PDF 纸面排版。

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
$env:OPTICAL_SYSTEM_DESIGN_ANALYSIS_EXPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\export-image-frame-analysis'
$env:OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\export-image-frame-viewers'
$env:OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\export-image-frame-reports'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~AnalysisImageExportTests|FullyQualifiedName~ViewerImageExportTests|FullyQualifiedName~MarkdownReportImageTests.FullReportRendersRealLayoutsPlotsAndPaginatedTextWithVisiblePixels'
```

验证所有四边与四角连续、同色、不透明；浅色/深色的 1×、1.25×、2× 输出保留原图角落内容与父级/边界。原有图像内容检查跳过新增外框，防止仅有边框的空图片误判通过。图签像素比较去除新外框后再与原界面比较，继续验证图签完整与视角状态。

## 最终结果

默认 Release App/测试项目构建零警告、零错误，见[构建日志](build.log)。首轮 **61 项中 59 通过、2 失败**，见[原始 TRX](frame-initial.trx)、[日志](test-initial.log)。失败均为 125% 缩放时脱离视图的导出页按 1 倍逻辑布局取整，使右/下边缘落在半个像素上；导出页保留分数 DIP 边界后修正，没有放宽四边/四角同色不透明的断言。

修正后定向 **13/13** 通过，见[最终 TRX](frame-final.trx)、[日志](test-final.log)：深浅主题与 1×/1.25×/2× 的 6 项边界检查、二维/三维/实体模型 125% 的 3 项实际导出、四格式完整报告 4 项。125% 的三类视图实际导出作为新增回归保留。

两轮按用例名称取最新结果，**64 个不同用例的最新结果均通过**，并非 74 个独立用例；见[去重账目](latest-coverage.json)。整数比例及报告 1× 的输出边界不受最后的分数布局取整修正影响，未重复运行首轮已通过的全部相机交互用例。覆盖普通/矩阵/波前/基面分析、隐藏系列恢复、完整页脚和滚动导出、相机/缩放、取消与换文件快照、超限保护、四格式布局/分析/分页文字图片。

[最终格式检查](format-final.log)退出 0、无诊断，`git diff --check` 通过。

真实离屏预览：[报告光线像差图](ray-fan-framed.png)、[分析相机 PNG](analysis-framed.png)、[二维 PNG](2d-framed.png)、[三维 PNG](3d-framed.png)、[深色图](dark-framed.png)、[125% 三维 PNG](3d-125percent.png)。已检查外框完整且包住图签；报告二维/三维图片现为 1202×786，图像内容保留原 1200×784 大小。
