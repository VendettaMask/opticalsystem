# 全部格式的离焦/非相干照度修复核对 — 2026-10-30

用户明确要求同时覆盖界面显示、普通图片及所有报告格式。此前修复已经位于共享层，本轮补齐真实跨入口验证，未另写 PDF、Word 或其他格式的绘图/孔径算法。

## 共享路径

- 离焦图：`ResponsivePlotLegendGrid.LegendWidth` 与 `ResponsiveAnalysisPlotHost.MinimumReadableWidth` 使用实际图例宽度；`UniformDiagramPage` 统一检查源图边界。
- 照度：`WorkbenchRuntime.Analysis.cs` 在共享应用分析入口启用 `UseSurfaceSamplingWindow`；仅当实际物理孔径为空时允许真实表面半口径采样，结果标注窗口来源。Core 默认严格要求物理孔径的通用 API 不变。
- 普通 PNG：`AnalysisPanel.PresentResult` → `BuildResultContent` → `CapturePlotImage`。
- 四种报告：`MarkdownReportExporter` → `ReportImageRenderer` → `AnalysisPanel.CreateReportPlot` → 同一个 `BuildResultContent`，随后由对应包装器内嵌或链接源 PNG。`ForPrint` 不选择另一套图形布局或计算算法。

## 本轮检查

新增 `AnalysisCrossFormatExportTests`，4 种格式各实际导出高斯 28 度的离焦点列图和非相干照度，共 8 项分析结果。每次分别通过普通分析面板保存 PNG 和正式报告绘图入口生成源图，在相同结果、时间戳和浅色主题下比较全部解码像素，4/4 通过。

每一组合均确认离焦 15 格、照度 9 格，照度每格均有非零值且注明“未添加物理孔径”，完整图签、外边框及 2404×1804 规格保持。源工程恢复状态不变。Word 媒体文件、HTML 内嵌图片和 Markdown 资源文件与实际源 PNG 一致；PDF 保留原始像素尺寸且不进行 JPEG 重编码。`verify-packages.py` 从实际输出重新提取图片，对四格式共 8 张图逐像素复核，记录见 `package-inspection.json`。

- `cross-format-passed.trx`、`final-test.log`：最终 4/4 通过，无失败/跳过。
- `default-app-build.log`：默认 App Release 构建，零警告/错误。
- `format-final.log`：只检查新增测试文件，空白格式验证退出 0。
- `exports/Word`、`exports/Pdf`、`exports/Markdown`、`exports/Html`：本轮实际诊断报告及原图资源；为自动验证产物，未将 Word 应用中的原生排版视为已验收。

初次 `build-and-test.log` 因其他后台任务占用 App 中间 PDB 未执行测试，未停止那个任务。改用独立测试中间/输出目录后，`cross-format-final.trx` 与 `cross-format-diagnostic.trx` 保留了新验证代码未将普通图片采集切回界面线程所致的失败；修正测试调度后最终 4 项通过。期间没有为使测试通过而修改产品绘图或孔径行为。这些失败重试不计入通过数。

本轮只补齐全部格式的定向契约，未重复前轮 80 项回归或全部 75 项分析；未进行新的 Zemax 数值对照、桌面操作、提交、推送或安装包发布。既有表格配色在 Word/PDF/HTML 中共用浅灰绿方案，Markdown 表格仍由阅读器主题着色。
