# 用户指南公式与 PDF 补充

日期：2026-10-27。补充上一阶段原理总览，未修改 Core 数值公式或放宽任何精度门槛。

## 已实现

- 计算原理扩展为 27 章、39 组公式。实际产品分析目录的 76 个条目均有章节对应，参考页提供直接跳转。文件查看、场景显示、清空与数据库筛选分别说明是否产生新计算。
- 普通字符公式改为分式、根式、积分、上下标及矩阵排版；维护时由固定工具生成自包含 SVG。界面和原生 C# PDF 共同消费矢量轮廓，没有在线渲染、运行时 JavaScript、外部 TeX 或 PNG 公式。
- 导出当前章节、全部计算原理或完整指南，固定点击时的语言及内容。嵌入正文所需字体，保留截图与矢量品牌/公式，支持取消和原子替换。章节与公式尽量同页，截图按剩余空间排版。
- 明确了未加权 Zernike QR、扫描最大值相对照度、FFT OPD 符号、采样 MTF 光瞳剪切、局部对比损失、Jones 多层膜限制，以及装调归一化几何预览等容易误解的定义。

## 验证

最终指南与数学/PDF 定向 **14/14** 通过，默认 Release 输出参与构建。覆盖全部定义/引用、所有矢量轮廓、三语导出、宽窄/深色布局、三种范围、后台线程导出、导出前及导出中取消均保留原文件。完整指南测试使用最小测试命令集合，不能将其章节数当成生产注册命令数。

随后增加参考页的实际点击检查，发现保留搜索条件时 `TextChanged` 延后触发会使跳转仍停在参考页；改为先同步重建目录再定位目标。指南 **9/9** 复验通过，这些是重复执行的既有用例，不与14项相加。

- [测试结果](../validation/rechecks/2026-10-27/user-guide/math-pdf-complete.trx)、[76 项对应证据](../validation/rechecks/2026-10-27/user-guide/pdf-audit/coverage.json)。
- [三语 PDF 文字与逐页渲染检查](../validation/rechecks/2026-10-27/user-guide/pdf-review/inspection.json)、[实际三种范围结果](../validation/rechecks/2026-10-27/user-guide/pdf-audit/all-scopes.json)。
- [公式界面预览](../validation/rechecks/2026-10-27/user-guide/math-reader/guide-calculation.png)、[公式源及哈希](../src/OpticalSystemDesign.App/Assets/UserGuideMath/manifest.json)。
- [冻结参考完整性](../validation/rechecks/2026-10-27/user-guide/math-frozen-integrity.json)。未运行全仓、未启动原生窗口或操作桌面，未提交、推送或发布安装包。
- [导航复验](../validation/rechecks/2026-10-27/user-guide/math-navigation-fixed.trx)、[文档检查](../validation/rechecks/2026-10-27/user-guide/math-documentation-fixed.json)、[检查器18/18回归](../validation/rechecks/2026-10-27/user-guide/documentation-regressions.json)。

PDF 正文可选择；Skia 的嵌入 CJK 字体存在同字形偏旁和中点字符的文本映射，提取检查按 Unicode 兼容规范化及相同字形别名核对标题。数学部分保留矢量轮廓，普通 PDF 文本提取不会将轮廓还原成 LaTeX；源公式在[数学附录](COMPUTATION_FORMULAS.md)提供。

首轮只读编译碰到并行工程图改动尚未写完的引用错误，后来默认构建与测试通过，失败日志保留。早期 PDF 排版的公式跨页问题已调整，首轮文件不作为最终交付。

全仓文档检查曾把临时目录中的工程图参考 PDF 当 UTF-8 Markdown 扫描，产生过量正则工作；已停止本任务对应的检查进程，未关闭用户程序。检查器现仅对 PDF/Word/ODT 保留库存与哈希，文本文件继续执行原链接、围栏与重复检查，未修改参考文档。新增二进制回归与原17项全部通过，严格文档检查无断链及围栏问题。

## 使用与边界

[计算指南](COMPUTATION_GUIDE.md)解释定义、单位、采样及支持范围；[逐项对应表](COMPUTATION_COVERAGE.md)是指南覆盖证据。76/76 关联和 PDF 检查不意味着任意镜头、面型或参数都通过外部数值认证，原有 Zemax 指定捕获范围保持。
