# Double Gauss 28° 四格式报告实算样例

日期：2026-10-29。用户请求：使用高斯 28° 镜头跑一次报告导出测试，将报告放到桌面查看。本记录是实际产品计算与导出样例，不是 OpticStudio 数值对比、镜头像质验收或全仓测试。

## 来源与范围

- 来源：`src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx`。源文件名称 `DOUBLE GAUSS`，三个视场为 0°、10°、14°，最大半视场 14°，全视场 28°。
- SHA-256：`f994005d633d4af742402469c9e4e2e0c289a57aed3ae876fb3231d1d3f95460`，生成与交付后复核均未改变。
- 软件快照：EFL 99.50067904919332 mm，F/# 2.9853189033661365，总长 132.988419345 mm，入瞳 33.33 mm，13 面、3 视场、3 波长。
- 通过正式 `WorkbenchApplication` 导入并捕获同一报告快照，使用产品 `MarkdownReportExporter`、`ReportImageRenderer` 和各格式写入器。没有手工重绘报告图或修改镜头/报告内容。
- 二维布局、三维布局、标准点列、光线像差、傅里叶 MTF、场曲/畸变、波前图、赛德尔系数：每种格式均 **8 成功、0 失败**。四种格式重复同一案例，不累计为 32 个不同的光学测试。

## 交付

桌面文件夹：`C:\Users\19851\Desktop\高斯28度镜头报告_20261029_160554`。

包含 `高斯28度光学设计报告.pdf`、`.docx`、`.html`、`.md`，以及 Markdown 使用的原名 `_assets` 图片目录。交付时只重命名文件，未改变内容；四份文件和配套 PNG 均逐个校验哈希一致，见[交付账目](delivery.json)。

## 检查结果

- 生成快照与全部图片账目：[generation.json](generation.json)、[生成日志](generation.log)。每种格式 9 张 PNG，全部检查四边与四角连续、不透明，保留图面与图签。
- PDF 41 页，Word 在独立不可见 Word 16.0 实例中排版为 42 页；两者所有页面均渲染为 PNG 并逐页复核，文字页面边界越界为 0。PDF 的 9 张内嵌图片与源 PNG 解码像素一致；Word 有 9 张内嵌图片、21 个原生表格。见[检查结果](inspection.json)、[Word 排版日志](word-native-render.log)。
- HTML 10 张内嵌图片（含公司标志）全部加载，目录链接有效，桌面与窄屏无整页横向溢出，0 外部请求。Markdown 的 9 个图片链接均存在，目录链接有效。见[浏览器检查](browser-inspection.json)。
- 保留真实软件排版供用户检查：Word 的执行汇总表最后一行续至第 11 页，完整参数附录从第 12 页开始；PDF 正文为前 10 页，附录从第 11 页开始。窄处方列中精确数值会换行，没有手工调整本次样例以掩盖这些输出特征。
- 打包 DOCX 渲染器因本机没有 LibreOffice 而不可用，见[日志](docx-packaged-render-unavailable.log)；已使用独立、不可见的 Word 实例完成替代排版检查，没有复用或操作用户已有 Word 文档。网页检查使用独立无界面浏览器。
- 临时生成器构建成功、0 错误；离线 NuGet 漏洞源不可访问产生 2 条 NU1900 警告，见[构建日志](build.log)。报告生成均成功，没有改变产品依赖或进行前台原生界面验收。

预览：[PDF 二维布局](pdf-layout-preview.png)、[PDF 光线像差](pdf-ray-fan-preview.png)、[Word 二维布局](word-layout-preview.png)、[HTML 布局](html-layout-preview.png)。完整检查页保留在工作区 `tmp/gauss28-report-sample/qa`，不复制到用户桌面。
