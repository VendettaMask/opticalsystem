# 帮助阅读界面定向验证（2026-10-27）

范围与预览见[调整记录](../../../../docs/USER_GUIDE_READER_DESIGN_2026-10-27.md)。

- `reader-final.trx` / `reader-final.log`：最终后台离屏定向 10/10，通过默认 Release 输出构建；未运行全仓测试。
- `previews/`：七张实际帮助控件离屏预览，涵盖教程、资源、公式、三语和主题/宽度。
- `frozen-integrity.json`：2668 份冻结参考一致。
- `verification.json`：本轮范围与文件哈希汇总。
- `documentation.json` / `documentation.log`：文档检查记录。
- `reader-first.log`、`reader-third.log`：中间构建失败；`reader-second.trx`：9/10；`reader-fourth.trx`：10/10。均保留，不与最终通过数累加。

测试筛选为 `FullyQualifiedName~UserGuidePanelTests`，采用真实 Skia 后台渲染；没有原生桌面窗口或输入，没有计算算法、公式定义、PDF 导出器或精度阈值变更。
