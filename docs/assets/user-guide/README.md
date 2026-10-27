# 用户指南真实截图

日期：2026-10-27。18 种界面 × 简体中文、English、日本語，共 54 张 PNG，由当前产品控件的 Avalonia Headless + Skia 在 96 DPI 离屏渲染。未启动原生桌面窗口、抢焦点或注入桌面输入。截图使用隔离工程与临时设置目录，不保存用户工程或修改用户设置。

覆盖工作台、文件入口、二维图、实体模型、点列、傅里叶 MTF、波前、优化、公差表、公差设置、镜头库、工程图、相机、镀膜、初始结构、装调、非序列对象与探测器结果。分析图运行实际计算；相机使用显式“理想光学对照”。顺序教程用内置演示处方展示操作，不作为独立镜头精度认证或新增数值基线。非序列示例为点光源及矩形探测器。

应用构建将三个语言目录的 PNG 嵌入指南资源，不依赖运行时磁盘图片。每个语言的 `capture.json` 记录捕获范围；哈希清单记录实际图片字节。重新生成后必须再构建一次，才能将新图片嵌入程序集。

2026-10-27 制图入口统一后，三语 `drawing.png` 和 `lens-library.png` 单独重新捕获，工具栏只显示“光学制图”，图纸页内选择标准。工程图截图展示 ISO 单片图。每种语言另捕获 `drawing-legacy.png` 验证同一页切换老国标；这三张额外图片仅保存在证据归档，不嵌入指南。各语言 `drawing-capture.json` 和 manifest 的 `drawingCaptureEvidence` 记录本次范围，其余图片保持原捕获范围。

随后按原标准修正老国标的右上角要求表与原表字段，重捕获同样三语范围；最新 `drawingCaptureEvidence` 指向该次核验。ISO 图纸本身通过修改前后的预览字节及 PDF 渲染像素对照保持一致；指南图片里的工作台状态不作为图纸字节对照。

光学零件图标注复核后再次同步上述六张图片及三份捕获记录，`drawingCaptureEvidence`现指向`gb1991-semantics`阶段。老国标半径、Ra/Rq、孔径和非球面说明的完整核对见[清单](../../LEGACY_GB1991_OPTICAL_PARTS_REVIEW.md)；老国标额外截图仍只放入证据包。此轮四张PNG有变化、两张逐字节保持，重新构建后验证嵌入资源。

## 后台重新捕获

在仓库根目录的 PowerShell 中执行：

```powershell
$env:OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_DIR = Join-Path $PWD 'docs/assets/user-guide'
$env:DOTNET_PROCESSOR_COUNT = '2'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --filter FullyQualifiedName~UserGuideCaptureTests /m:1 /nr:false
```

普通测试不设置此变量时，三项批量捕获跳过。阅读布局和图片加载回归始终运行；可设置 `OPTICAL_SYSTEM_DESIGN_GUIDE_READER_CAPTURE_DIR` 另存指南自身的宽屏、窄屏、深色和三语检查图。界面截图不作为光学精度证据。

只刷新受制图入口影响的镜头库、工程图及老国标切换证据时，再设置 `$env:OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_SCOPE = 'drawing'`。清除此变量后恢复完整捕获。
