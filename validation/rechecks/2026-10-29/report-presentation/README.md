# HTML 和四格式报告排版

日期：2026-10-29。范围：报告导出窗口、共享内容组织、Word/PDF/Markdown 排版和新增 HTML 输出。

## 最终行为

- 格式选择增加 HTML，旧枚举值和默认 Markdown 保留。保存设置或成功导出后记住选择，修改格式或点击顶部入口不启动任务。
- 共享内容按“标题/工程名、报告概览、目录、配置结构与图表、执行结果、完整计算记录附录”组织。正文图表先于原始 JSON，完整参数、原始报告、计算来源和指纹均保留。大于 80 行的表在正文明确展示前 12 行；超过 8 项的指标也标明范围，完整值在附录。
- Word 标题/副标题黑色、深蓝表头、隔行底色、居中编号图注、页眉、PAGE/NUMPAGES 页脚和内部目录书签。PDF 同一纸面风格，主配置和附录新页，图与标题/图注保持邻近。保留无损 PNG 和中文 ActualText。
- HTML 单文件内嵌 SVG 公司标志、PNG 图片和样式，不含脚本或外部资源。桌面侧边目录，窄屏重排，宽表局部滚动，打印目录和浅色表头；文本均编码，CSP 限制资源。Markdown 保留通用标题、章节链接、图注、表格与 `details` 附录，主题颜色由阅读器决定。
- 布局图保留实际场景和比例尺，去掉与报告重复的图片内标题；使用原主题的实色背景，避免透明像素在 PDF 中发生颜色往返差异。分析图的完整图例、页脚和原物理数据保持。

## 自动验证

默认 Release App/测试项目构建零警告、零错误，见[构建日志](build-final.log)。完整定向 **35/35** 通过，见[TRX](report-presentation-final.trx)、[日志](test-final.log)。透明背景收尾后只复跑四格式真实图像/完整报告 **4/4**，见[图像 TRX](report-images-final.trx)、[日志](test-images-final.log)，它们与前 35 项重叠，不累计为 39 项。

覆盖四格式选择/记忆、显式开始和重复点击、快照/原文件保护、取消清理、失败继续、包装失败、设置兼容、完整数据附录位置、目录目标、HTML 编码与内容安全策略、真实布局/图表图片、Word 包、PDF 字形和无损图片、长表格及超高行。未运行全测试库。

最终 [格式检查](format-verified.log)退出 0、无诊断，`git diff --check` 通过。

```powershell
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~MarkdownReport|FullyQualifiedName~DesignReportFormat'
# 背景收尾后的定向复验与最终样例：
$env:OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\report-presentation-verified'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~MarkdownReportImageTests.FullReportRendersRealLayoutsPlotsAndPaginatedTextWithVisiblePixels'
```

所有计算与测试均后台、双处理器低优先级运行。浏览器与 Word 仅为独立离屏排版验证，不是产品导出依赖。

## 文件与视觉检查

最终样例在忽略的 `tmp/report-presentation-verified/`。完整 PDF 33 页，Word 后台排版 34 页，HTML 打印 35 页；三者第一张图均位于第 3 页，完整处方 JSON 始于第 10 页附录，见[结构账目](document-structure-verified.json)。PDF/Word 页面文字均在页面边界内，中文原文及末尾记录检查通过；PDF 内嵌九张图逐张匹配原 PNG 解码像素。见[文件/页面账目](inspection-verified/document-inspection.json)，各页概览保存在同目录。

Word 使用任务创建的独立不可见 Word 16.0 实例，只打开样例、禁用宏、不加入最近文档，完成后关闭该实例；没有操作用户已有窗口。长表格仍正常跨页，Word 原生页码显示为“当前页 / 总页数”。PDF 使用 Poppler 渲染、MuPDF 严格提取原文；没有用归一化放宽原字符检查。

HTML 使用独立 headless Edge 检查 1440×1050 与 390×844 视口、A4 打印和资源加载，10 个内嵌图片（9 张报告图和 1 个公司标志）全部加载，零外部请求、目录断链或整页横向溢出。Markdown 经独立阅读器预览，目录目标完整；见[浏览器账目](browser-verified/browser-inspection.json)。

预览：

- [HTML 桌面概览](browser-verified/html-desktop-cover.png)
- [HTML 图表](browser-verified/html-desktop-figure.png)
- [HTML 窄屏](browser-verified/html-mobile.png)
- [Markdown 阅读器预览](browser-verified/markdown-cover.png)

这些是软件测试样例，不能当作用户当前工程报告或新的光学数值认证。

## 中间记录和边界

初轮和最终 35 项均通过；样例视觉复核后统一黑色副标题、窄屏两列表格和打印目录。浏览器辅助脚本曾使用 Windows 路径直接作 ESM URL 而退出，已改用文件 URL；原[日志](browser-initial.log)保留，仅验证脚本受影响。

去掉布局图重复标题时，直接捕获场景的透明像素在 PDF 的预乘颜色转换中出现逐像素差异；原[检查失败](inspection.log)保留。恢复同主题的不透明背景后重跑四格式图像，并验证九张图片的像素完全一致，没有删除或放宽该检查。

验证限于本机 Windows Release、Light 样例、后台 Word 与 headless Edge。未进行真实桌面交互、Linux/macOS 原生验收、全仓测试、额外数值认证或安装包发布。Markdown 的字体、颜色和折叠呈现由所用阅读器支持程度决定。
