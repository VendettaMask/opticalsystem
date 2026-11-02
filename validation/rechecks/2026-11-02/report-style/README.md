# 四格式工程报告统一版式与 Zernike 公式

本轮落实用户确认的完整 HTML 样稿：HTML、PDF、Word、Markdown 共用封面、目录、系统概要、编号整图与原生数值表。前三者采用石墨文字、钢蓝辅助色、浅灰表头和淡灰交替行；Markdown 使用相同语义结构与配套 SVG/PNG，外观由阅读器主题决定。没有改变光学拟合、追迹或物理验收标准。

## 完整镜头样例

- 源文件：`Double Gauss 28 degree field.zmx`，SHA-256：`F994005D633D4AF742402469C9E4E2E0C289A57AED3AE876FB3231D1D3F95460`。
- 捕获时间见 [snapshot.json](snapshot.json)。67 项分析加二维/三维布局共 **69 项全部完成**；一次真实计算后，四种容器复用同一批冻结数值与源图。后续修正排版仅重新封装，没有重复计算所有分析。
- 按用户要求排除图像模拟、几何图像、几何位图、部分相干、扩展图像、光源分析六项，清单见 [selection.json](selection.json)。产品仍按用户选择生成报告，不强制禁用这些分析。
- 62 张不同的完整源 PNG 均为 **2404×1804**，含完整整图边框、图例及 Logo 图签。封面重复展示二维布局，因此 HTML/PDF/Word 有 63 次整图放置；Word 包只保存 62 张不同原图，Markdown 使用 62 张图与一个 SVG 标志。
- HTML/PDF/Word 默认正文均为 **80 页**。Word/PDF 的详细采样记录改为明确勾选的附录；没有将数千行采样自动追加至纸面正文。HTML 的计算记录窗口及 Markdown 折叠附录保留完整设置、来源和原始输出。
- 保持“不导出完整处方”的过滤规则、当前分析参数、配置与冻结快照，不保存或修改源镜头。

## Zernike

Fringe、Standard、Annular 均包含 37 项，正文合计 **111 个公式**，按原 n/m 和项号排序，并保留 G12 系数。Fringe/Standard 使用各自实际归一化；Annular 从拟合使用的径向递推展开，注明实际遮拦比和变量 `u = (2ρ² − 1 − ε²)/(1 − ε²)`。只有公式显示精度整理，未修改拟合或波前统计。独立解析显示表达式与 Core 基底求值进行多点对照，并检查已知低阶公式。

Word 实际打开确认 **111 个原生 OMML 方程**，包含真正的上标与根式。PDF 使用嵌入字体和定位绘制的普通数字显示高次上标，避免字体缺少 ⁵、⁶ 等字形时出现方框。HTML 为真正上标，Markdown 使用可读数学符号。

## 验证

- 最终 **63/63 个不同定向用例通过**，见 [TRX](report-style-data-preservation-63.trx)。覆盖四容器、XSS/离线资源、取消及原子清理、源快照安全、配置、处方过滤、空附录、实际图像像素、图表背后数值表与全部指标的记录保留、Markdown 正文范围、设置窗口与开始导出、Word/PDF 详细附录、公式定义/归一化和 PDF 上标字形。
- 初轮旧版断言仍期待不同目录结构和文字截图，已随新需求修正；原失败记录 [第一轮](report-style-first.trx)、[第二轮](report-style-second.trx) 保留，不叠加重复通过数量。
- 浏览器检查 1600、1100、390 像素视口、完整目录、最后一页导航、原图预览、111 个公式、原始记录窗口及 A4 打印边界，见 [HTML 检查](html-inspection.json)。Windows HTML 换行的 CSP 哈希按浏览器规范化，固定阅读脚本可以执行，用户内容不参与脚本。
- HTML/Word/Markdown 的原图文件字节一致；产品 PDF 解码后 RGB 像素与原 PNG 全部一致，不使用 JPEG。PDF/Word 图宽统一为 **523.28 pt，约 184.60 mm**，保留完整比例。见 [容器检查](artifact-inspection.json) 与 [源图清单](source-image-manifest.json)。
- 先尝试标准 DOCX 渲染脚本；本机没有 LibreOffice，改用独立且不可见的 Microsoft Word 16 实际分页和渲染。确认 80 页、63 次完整整图放置与 111 个可编辑公式，见 [Word 记录](word-render.json) 和 [Word 渲染检查](word-render-inspection.json)。内部 Word 转出的 QA PDF 可能被 Word 重采样，不作为产品 PDF 或原 PNG 完整性证据。
- 其他任务曾占用默认测试 DLL，且镀膜模块处于并行编辑阶段；测试使用独立目录，包含全部最终报告修改；最后一轮重新构建实际 App 依赖并全部通过，没有终止其他任务。最后正常默认 Release App 与内置模块依赖构建 **0 警告、0 错误**，见 [构建日志](default-app-build.log)。未替其他任务修改镀膜源码。

## 产物与范围

默认导出文件、大小和 SHA-256 见 [artifacts.json](artifacts.json)，桌面目录见 [交付收据](desktop-delivery.json)。大文件保留在 `output/report-style-gauss28-20261102/Reports/`，本目录保留审计 JSON、选取的实际渲染页与源码哈希。源镜头保持不变。

这是当前镜头的报告生成、数据保留与显示验证，不是新一轮商业软件数值对照，也不升级历史 Zemax/Optiland 精度结论。本轮没有全仓测试、前台桌面操作、安装、发布、提交或推送。
