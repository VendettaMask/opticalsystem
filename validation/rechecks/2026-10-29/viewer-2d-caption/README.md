# 二维视图完整图签

日期：2026-10-29。范围：工作台二维视图、相机 PNG、四格式报告中的二维布局图，以及共用图签的既有分析页。

## 实现范围

- 二维视图以完整图签替换单行参数栏：图名、生成时间、结构编号、有效焦距、F 数、总长；右侧正式 SVG 公司标志、产品版本、工程文件名和工程名称。
- 从分析图提取 `PlotCaption`，复用原品牌、分区、字体与 132 DIP 最小高度。窄窗口上下排列，长工程名称自动增高；二维画布可收缩，避免最小高度挤入图签。
- 相机 PNG 直接绘制点击时已排好的完整图签，保留主题、语言、字体与生成时间，底部起点按输出像素对齐；打开保存选择器前固定图像和文件名。保留隐藏底框设置、取消、过期保护、缩放和平移。
- 报告二维布局使用同一图签构造器与捕获时间，四格式保留完整图签。三维布局、实体模型的参数栏及光学计算不在本次功能范围内。
- 相机 PNG 增加与分析 PNG 同量级的像素分配上限，并继续使用有界原子写入。

## 验证方法

所有验证均使用后台测试和 Avalonia Headless/Skia 离屏绘制，没有启动或操作前台桌面窗口，没有执行发布。没有运行全仓测试。

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
$env:OPTICAL_SYSTEM_DESIGN_VIEWER_EXPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\viewer-caption-captures'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~ViewerImageExportTests'
```

初次离屏检查发现：重建脱离主题树的图签丢失画刷；直接绘制完整可视图签已解决。随后检查发现小窗口原 240 DIP 最小画布与图签重叠，以及分数高度引起文字边缘重采样；改为可收缩二维画布及像素对齐。早期失败日志与最终复验分开保留，不把失败运行计为通过。

## 最终结果

默认 Release App/测试项目构建零警告、零错误，见[构建日志](build.log)。定向 **76/76** 通过，零失败、零跳过，见[最终 TRX](caption-final.trx)和[最终日志](test-final.log)：

| 范围 | 用例数 |
| --- | ---: |
| 相机图片、完整图签、主题/三语言/大字体、长名称、窄窗防重叠、隐藏图框、2× 像素比例、保存取消、换文件快照、超大图片分配保护 | 23 |
| 二维/三维/实体模型独立配置选择与工具栏适配 | 21 |
| 场曲、光线扇形、干涉图、共享高度与各绘图族长图签适配 | 28 |
| Word、PDF、Markdown、HTML 完整报告及真实二维图签 PNG | 4 |

最终图签图片与离屏界面比较采用亮度而非逐 RGB 通道：可视画刷使用灰度抗锯齿，原窗口使用 LCD 彩色字缘。要求完整不透明背景、保留图签大小与全部标识/参数，并限制亮度差大于 32 的像素低于 1%；另检查文字在图签内、区域不重叠、隐藏行为和保存快照。早期逐 RGB 比较造成的失败保留于中间日志，最终验证不依赖提高颜色通道差异门槛。

[格式验证](format-final.log)退出 0、无诊断，`git diff --check` 通过。

已检查真实离屏图：[宽窗口](2d-viewer-preview.png)、[420×420 窄窗口](2d-viewer-narrow.png)、[深色英文 PNG](2d-caption-dark.png)、[24 号字体 PNG](2d-caption-large-font.png)、[报告二维布局图](2d-report-preview.png)。报告图保留完整 1200×784 图面；四格式测试均检查场景下方的公司标志区域。未重新验收 Word/PDF 的每页纸面排版、原生桌面交互、跨平台或安装发布。
