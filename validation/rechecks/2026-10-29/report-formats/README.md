# 设计报告三种导出格式

日期：2026-10-29。范围：数据库中的设计报告设置、格式写入、报告测试和使用文档。

## 实现

- 格式下拉框依次提供 Word（`.docx`）、PDF（`.pdf`）、Markdown（`.md`），只生成所选格式，保存设置或成功导出后记住选择。旧设置默认 Markdown。
- 顶部入口只打开或显示已有窗口，改格式不执行任务；运行中锁定格式和内容设置，仍可取消。
- 同一快照、同一次分析计算组装 `DesignReportContent` 标题/段落/表格/原始数据/图片块，三个写入器直接消费，不解析用户文本为文档标记，也没有第二套光学计算。
- Word 是标准 OOXML ZIP 包，正文、标题和表格可编辑，表头可跨页重复，PNG 内嵌。使用框架自带 ZIP/XML，不增加产品包依赖或 Office 生成进程。
- PDF 复用现有 SkiaSharp 和 Noto 字形，A4 自动换行、分页、表头重复，超高行可分片，图片等比无损内嵌；最多 1000 页。显式指定 PDF 元数据的无损质量 101，避免结构体零值导致 JPEG 低质量重编码。CFF 字体以自包含 Type3 字形程序写入，使用源 UTF-8 文本簇生成正确的 ToUnicode/ActualText，保留原中文复制/检索含义。
- Markdown 保持图片相对链接和 `_assets` 目录。Word/PDF 是可独立分享的单个文件，临时图片清理。所有格式都使用有界暂存和原子发布，不覆盖旧报告；取消或文档包装失败只清理本次文件。

## 自动验证

最终默认 Release App/测试项目构建零警告、零错误，见[构建日志](build-final.log)。报告相关 **27/27 通过**，见[最终 TRX](report-formats-lossless.trx)和[日志](test-lossless.log)：

- `MarkdownReportExportTests` 9 项：原快照、参数/链接、配置、失败继续、默认设置和三格式取消保护。
- `MarkdownReportImageTests` 8 项：三种语言分别操作三格式的真实入口，确认选择不执行、重复点击不执行、开始后才生成、运行中不重复、重新打开保留格式；三格式真实 Skia 布局/图表截图、设置窗口和共享进度。
- `DesignReportFormatTests` 10 项：Word 包结构/图片与原文、PDF 字形和图片、旧文件/原工程保护、两格式包装失败清理、非法格式零创建、设置复制/序列化、长表格及超高行、PDF 中文 ActualText 映射。

后台命令使用 `DOTNET_PROCESSOR_COUNT=2`、低优先级；构建用单进程，测试在独立后台环境运行，以避开受限 testhost 连接问题。格式检查见[最终日志](format-lossless.log)，退出 0；`git diff --check` 通过。没有运行完整测试库。

```powershell
dotnet build tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
$env:OPTICAL_SYSTEM_DESIGN_REPORT_CAPTURE_DIR = 'D:\optical system design\opticalsystem\tmp\report-formats-lossless-qa'
dotnet test tests/OpticalSystemDesign.Tests/OpticalSystemDesign.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter 'FullyQualifiedName~MarkdownReport|FullyQualifiedName~DesignReportFormat'
```

## 文件和页面检查

测试样例与页面原图保存在忽略的 `tmp/report-formats-lossless-qa/`，由上述测试可重建。完整样例包含真实二维/三维布局、常用四种分析、赛德尔系数和波前图。Word 的包结构、内嵌图片和文本由 ZIP/XML、python-docx 及真实 Word 分别检查；PDF 用独立 MuPDF 读取原文字，Poppler 渲染页面，再检查页面布局和文本边界。

机器检查与样例哈希见[检查账目](inspection-final/document-inspection.json)，完整 PDF/Word 排版各 24 页，长表格 PDF/Word 分别 5/6 页，页面文字均在边界内，末行和长行尾部原字符检查通过。PDF 内嵌的 9 张图逐张与源 PNG 解码像素完全一致。原始提取文字和全部页面概览同目录保存，[设置界面](export-window.png)保留格式选择与显式开始。PDF 图片中的分析轴/数据来自原图，不作为可选择正文检查。

Word 排版使用任务创建的独立、不可见 Word 16.0 实例，只打开任务样例、禁用宏与提示、不加入最近文档，完成后关闭该实例。没有使用用户已有 Word 进程、显示窗口或注入鼠标键盘。这个 Word 实例只用于验证，产品导出仍不依赖 Word 安装。原 `render_docx.py` 因本机没有 LibreOffice 不能使用，保留诊断后改用后台原生 Word 排版；不把该失败计为通过。

## 中间记录与边界

- 首次编译的一个 XML 构造括号错误、一个测试风格警告已修复，原构建日志保留在 `tmp/report-formats-*.log`，最终构建干净。
- 首批 [25 项记录](report-formats.trx)为 24 通过、1 失败：测试假定一定存在 `/FontFile`，实际 Skia 为 CFF 输出 Type3 字形程序，改为核验真实自包含字形结构；未删除字体检查。
- [长表格扩展后的 27 项记录](report-formats-final.trx)全部通过，但独立文字检查随后发现普通绘字反向映射会返回同形 CJK 部首，不能据此宣称原字符一致。已改用带源 UTF-8 簇的文本 run，追加 `/ActualText` 原字符断言，并完成最终 27 项回归。
- pypdf/pdfminer 的普通提取不消费该 PDF 的 ActualText，因此保留其原始输出作为诊断；最终文字检查采用支持 ActualText 的 MuPDF，严格比较原字符，不用 NFKC 放宽匹配。见[Skia 文本存储接口](https://api.skia.org/classSkTextBlobBuilder.html)和[SkiaSharp 文本 run](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.sktextblobbuilder.allocatehorizontaltextrun)。临时检查依赖没有加入产品或系统 Python。
- 页面检查发现 PDF 图片因默认元数据结构体的质量零值被有损编码；已改为无损 101，增加禁用 JPEG 重编码的回归，并逐张核对解码像素与原 PNG 完全一致。质量参数含义见[Skia PDF 元数据](https://api.skia.org/structSkPDF_1_1Metadata.html)。
- 验证范围限于 Windows Release、Avalonia Headless/Skia 与后台文档排版。没有全仓测试、跨平台原生交互、外部光学数值认证、真实桌面操作或安装包发布。
