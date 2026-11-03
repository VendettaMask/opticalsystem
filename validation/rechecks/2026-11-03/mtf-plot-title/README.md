# MTF 绘图区标题统一

2026年11月3日，将 MTF 分析名称统一放在原有底部标题栏，去掉单图绘图区上方重复的分析名。原因是原始配置不同：FFT、几何 MTF 默认没有顶部标题，而惠更斯显式设置了 `Huygens MTF`，离焦与随视场扫描也带有各自标题。

## 实现范围

应用契约末尾增加 `AnalysisPresentationKind.Mtf`，保持既有枚举值身份不变；分析目录为 FFT、Huygens、Geometric、Sampled 以及三类离焦、三类随视场分析提供同一类型。界面按类型归一单图选项，不解析中英文名称，也不改动 Core 中的标题元数据。

`BuildSinglePlot` 首次建图和创建可勾选图例时使用同一份归一后的选项，避免后一步重新带回标题。已有底部标题栏保留分析名称；结构、视场等多子图识别标题保留。系列、坐标范围、单位、设置、数值结果及原始 DTO 不变。

界面与 `ReportImageRenderer` 共用 `BuildResultContent` 绘图入口，因此后续 PNG 和报告插图应用同一布局。用户提供的历史报告图片属于问题参照，当前验证为新绘图输出。

## 验证

默认 Release App 和内置模块依赖构建完成，**35/35 项定向检查通过**，无跳过。原始结果见 [mtf-title.trx](mtf-title.trx)。

- 10 个 MTF 曲线族全部使用新分类；每条曲线独立勾选、全部隐藏及恢复后，顶部标题不重新出现，原坐标和视口保持。
- 有、无勾选图例的单图均覆盖标题归一；带 MTF 字样的其他类型仍保留自己的绘图标题，证明规则不依赖名字。
- MTF 多结构子图保留“结构 1”“结构 2”标题。
- 实际 Double Gauss 的 FFT、Huygens、Geometric 三类结果均保留底部标题，系列和除显示标题/内置图例外的绘图选项逐项保持。
- 三类分析在两个窗口尺寸下导出的 2404×1804 原始图全像素一致，并与报告绘图出口全像素一致；镜头恢复状态在验证前后相同。

实际输出位于 [previews](previews)：[FFT](previews/MTF.png)、[Huygens](previews/Huygens-MTF.png)、[Geometric](previews/Geometric-MTF.png)。这些布局验证样例显式使用单波长、32 级采样和 100 cycles/mm 的频率上限；与问题参照图的采样和频率范围不同，不用于重新判定光学数值精度。完整图例回归预览位于 `legend-previews`。

所有绘制均在后台离屏完成。没有操作用户桌面或重启其应用。

复现：

```powershell
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MtfPlotTitleTests|FullyQualifiedName~AnalysisLegendLayoutTests'
```
