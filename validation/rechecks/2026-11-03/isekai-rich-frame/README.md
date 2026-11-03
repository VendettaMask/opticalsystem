# 异世界完整书封与精细材质 — 2026-11-03

用户指出上一版缺少概念图的完整边框，且暖羊皮纸、钢笔边饰与星盘都过于简单。本轮替换第一版程序化细纹/角花，增加真实材质层次与完整魔导书书封。只作用于 Isekai 的分析图、2D/3D 光路和实体模型；以下页面来自产品实际 C#/Avalonia/Skia 以及共享 Core 的 Cooke 示例，装饰素材本身使用内置图像生成工具制作。

## 已实现

- 深色皮革书封、旧金双压线、黄铜角帽与圆钉、连续钢笔花叶及小符文、上下星芒徽记；自然斑驳羊皮纸和带细刻度/雕花底座的黄铜星盘。
- 三张原始 PNG 嵌入 App；[素材、提示词与哈希](../../../../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)随仓库维护。资源每个 UI 线程只解码一次，绘制装饰时局部开启高质量插值；未改变科学图像插值、物理曲线或光线颜色。
- 外框分成八片，中央不绘制。每侧 88 DIP 衬边在原图纸外部，普通源图仍为 1200×900、图签 160 DIP；1× 完整图为 1376×1076。默认导出画布仍为 1920×1080，完整外框居中适配；适应窗口与 100% 滚动包含完整边框。
- 星盘只在图例剩余空间足够时出现，或作为光路背景右下角装饰；装饰不命中指针。实际光路、曲线、科学色图、字体、品牌轮廓、图例筛选及相机由原代码保留。
- 报告复用内部源图，但外部装饰衬边不计入报告图幅；仍使用原细框、原比例和既定物理尺寸，避免文字被厚衬边挤小。强制亮色打印路径保持。
- Light、Dark、Lumen、Pixel 没有材质覆盖；菜单与参数操作区、已批准的语义图标不因本次修订改变。

## 实际截图

| 页面 | 完整 1× 源图与外框 | 真实离屏界面 | 默认 PNG 画布 |
| --- | --- | --- | --- |
| 惠更斯 MTF | [图](previews/Huygens-MTF.png) | [界面](previews/Huygens-MTF-live.png) | [1920×1080](previews/Huygens-MTF-1080p.png) |
| 点列图 | [图](previews/Spot-Diagram.png) | [界面](previews/Spot-Diagram-live.png) | [1920×1080](previews/Spot-Diagram-1080p.png) |
| 波前面 | [图](previews/Wavefront-Map.png) | [界面](previews/Wavefront-Map-live.png) | [1920×1080](previews/Wavefront-Map-1080p.png) |
| 二维光路 | [图](previews/TwoDimensional-OpticalLayout.png) | [界面](previews/TwoDimensional-OpticalLayout-live.png) | 同一捕获路径 |
| 三维光路 | [图](previews/ThreeDimensional-OpticalLayout.png) | [界面](previews/ThreeDimensional-OpticalLayout-live.png) | 同一捕获路径 |
| 实体模型 | [图](previews/ThreeDimensional-SolidModel.png) | [界面](previews/ThreeDimensional-SolidModel-live.png) | 同一捕获路径 |

## 验证范围

最终 [rich-frame-final.trx](rich-frame-final.trx)：**86 执行、86 通过、0 失败、0 跳过**，约 29 秒。默认 Release App、内置模块依赖与测试构建完成，无警告/错误；7 项 Isekai 材质/边框用例包含在该总数内，重复运行不累计。

沿用并扩展 `IsekaiDrawingMaterialTests`，核对真实分析与三种光路、其他四主题往返像素恢复、源图几何/字体/曲线身份、图例与光线显隐、相机/工程状态、窄窗、完整外框的 fit/100% 和报告原比例。新增一项像素对齐的四角标记/单像素交叉线夹具，严格逐行比较带框前后的全部源像素，验证外框不裁切、不覆盖、不重采样源内容。

相关回归包含 `UniformDiagramExportTests`、`DiagramViewportFitTests`、`AnalysisLegendLayoutTests`、`MtfPlotTitleTests`、`AnalysisFrameDpiTests`、`ThemeResourceTests`、`ThemeRuntimeTests`。不重跑全量光学精度矩阵，不据此宣称全仓或全平台验收。全部后台 Headless/Skia；未打开、控制或重启用户的应用，没有打包发布。

阶段记录：[最初材质接入 6/6](material-first.trx)。[首轮源图检查](rich-frame-source-check.trx)为 82/85；[像素诊断](rich-frame-pixel-check.trx)为 3/6。失败来自新加的“任意矢量内容平移后像素逐字节相同”假设：Skia 对浮点边界的覆盖率依赖平移，MTF/波前的差异仅 30 个通道，点列约 9 千通道，最大差 22/255；布局、字体与曲线对象检查均通过。此断言改为像素对齐标记的严格全像素比较，真实图继续检查源几何/字体、数据对象和无裁切布局；没有修改光学计算或原数值验收阈值。

首版[薄框材质记录](../isekai-parchment/README.md)保留其阶段范围，不与本次结果相加。

`git diff --check` 通过；6 份相关 Markdown 的 928 个本地链接目标存在性检查通过，结果保存于 `document-links.json`。本机没有 `pwsh`，未执行完整 PowerShell 文档审计；该检查不涵盖全文重复段落或生命周期规则。

复现（仓库根目录）：

```sh
OPTICAL_SYSTEM_DESIGN_ISEKAI_CAPTURE_DIR="$PWD/validation/rechecks/2026-11-03/isekai-rich-frame/previews" \
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~IsekaiDrawingMaterialTests|FullyQualifiedName~UniformDiagramExportTests|FullyQualifiedName~DiagramViewportFitTests|FullyQualifiedName~AnalysisLegendLayoutTests|FullyQualifiedName~MtfPlotTitleTests|FullyQualifiedName~AnalysisFrameDpiTests|FullyQualifiedName~ThemeResourceTests|FullyQualifiedName~ThemeRuntimeTests' \
  --logger 'trx;LogFileName=rich-frame-final.trx' \
  --results-directory validation/rechecks/2026-11-03/isekai-rich-frame \
  /m:1 /nr:false /p:UseSharedCompilation=false
```
