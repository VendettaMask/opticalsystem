# 用户指南阅读界面调整（2026-10-27）

本次按用户确认调整帮助页面的阅读布局。章节内容、计算公式定义和计算引擎保持原有行为；没有扩展或重新验证 PDF 功能。

## 已实现

- 左侧目录宽 248 DIP，降低分组文字的视觉权重，保留选中提示、搜索和分类；长标题换行，不横向裁切。
- 正文最大宽度从 1040 改为 720 DIP。标题 30 DIP，小节标题 18 DIP，正文 15 DIP、行高 27 DIP。摘要、分隔线和小节间距形成清晰层次。
- 达到 1120 DIP 时，在右侧显示 176 DIP 的“本章内容”文字目录；较窄时将同一目录移入正文顶部的折叠区。点击条目跳转小节；切换宽度保留当前章节。
- 窄于 760 DIP 时，在目录和正文间切换。保留阅读字号，超宽公式只在自身区域横向滚动；宽屏公式真正居中。
- 截图使用轻边框、下方图注与放大入口，保留适应窗口和 100% 查看。流程示意仍位于章末并默认折叠。
- 小节链接、前后章和相关计算章节链接弱化按钮边框，长链接可换行；沿用原生键盘焦点反馈与主题资源。右上现有操作收进“⋯”，未改变导出器。

## 验证

默认 Release 输出参与构建。最终 `UserGuidePanelTests` **10/10 通过**，不是全仓测试：

- 搜索、分类、空结果、前后章、命令覆盖和实际点击计算章节链接；
- 中、英、日三语，浅色、深色主题，1440 / 1200 / 520 DIP 阅读预览；
- 实际调整 1440 → 1000 → 360 → 1440 DIP，检查小节目录迁移、跳转、章节保留、公式居中与局部滚动；
- 目录文字边界不超过实际视口；正文不产生横向溢出；
- 截图放大、适应窗口、100% 以及全部 54 张嵌入截图解码。

全程采用后台离屏渲染，没有打开原生桌面窗口或发送键鼠输入。代码格式检查通过；2668 份冻结参考在 HEAD 与工作目录均保持一致。截图只证明阅读界面呈现，不构成光学精度认证。

早期构建发现不存在的主题资源常量和测试缺少命名空间，已修正。第二轮 9/10：新增测试误认为 520 DIP 必须使近轴公式溢出，而该公式实际能放下；改用真实产生溢出的 360 DIP 验证，不缩小字号或放宽断言。预览同时发现公式未居中及目录长标题裁切，均修正后复核。第四轮和最终轮各 10/10，通过数不相加。原始失败记录保留在同一证据目录。

## 实际预览与记录

- [资源与模块正文](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-resources.png)
- [近轴公式排版](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-calculation.png)
- [操作教程](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-zh-CN-Light-1440.png)
- [窄屏](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-zh-CN-Light-520.png)
- [深色](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-zh-CN-Dark-1200.png)
- [英文](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-en-US-Light-1200.png) / [日文](../validation/rechecks/2026-10-27/user-guide-reader-redesign/previews/guide-ja-JP-Light-1200.png)
- [最终测试](../validation/rechecks/2026-10-27/user-guide-reader-redesign/reader-final.trx)、[构建日志](../validation/rechecks/2026-10-27/user-guide-reader-redesign/reader-final.log)、[冻结核对](../validation/rechecks/2026-10-27/user-guide-reader-redesign/frozen-integrity.json)

使用说明见[用户指南](USER_GUIDE.md)。
