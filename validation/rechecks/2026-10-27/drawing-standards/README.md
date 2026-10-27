# ISO 与老国标制图入口精简

日期：2026-10-27。范围仅为移除 GB/T 13323—2009，保留 ISO 10110 与老国标 GB/T 13323—1991；不改变共享 Core、镜头数据或保留标准的图纸计算。

已移除 2009 版的按钮、注册命令、帮助标题、枚举值、嵌入 XML 模板、表格渲染及只由该表格使用的字段。保留 ISO 与老国标各自的文档 ID、预览、PDF、材料公差和剖面符号。旧 2009 页面在显式恢复时按 ISO 打开并更新标题，保留其原停靠 ID 与其他设置；输入保存对象不被就地修改。字符串形式和旧数值枚举值均纳入恢复检查。

## 验证

全部使用默认 Release 输出、两个逻辑处理器、BelowNormal 后台执行。界面通过 Avalonia Headless + Skia 离屏绘制；无原生桌面窗口和桌面输入。

| 记录 | 通过 / 总数 | 范围 |
| --- | --- | --- |
| [iso-and-legacy-verified.trx](iso-and-legacy-verified.trx) | 35 / 35 | 制造图、模板、材料、单片/胶合预览与 PDF、窄宽布局、独立文档、五种旧设置恢复、三语实际入口及初次截图 |
| [guide-images-verified.trx](guide-images-verified.trx) | 3 / 3 | 同三语捕获用例复跑，更新工程图/镜头库六张截图；不重复计入不同用例数 |
| [guide-reader-verified.trx](guide-reader-verified.trx) | 9 / 9 | 六张更新图片重新嵌入后，指南阅读、三语资源加载、宽窄布局及命令覆盖 |

不同通过用例共 44 项。截图及每种语言的实际捕获范围见 [captures.zip](captures.zip)；对应六张图片已同步到 `docs/assets/user-guide`，其哈希更新到原 manifest。实际工具栏及已注册制图命令均只有 ISO 和 1991 两项。新旧两套制图仍由同一产品渲染器输出。

初次默认并行构建提前终止、未报告编译错误；切换单节点并禁用构建服务器后，默认输出构建完成。首次受限测试进程随后出现 testhost 连接超时，执行 0 项；见保留的 [connection-aborted.trx](connection-aborted.trx)，不计通过。后续后台授权运行完成相同范围及图片加载验证。未运行全仓测试或扩大数值认证范围，未提交、推送或发布安装包。

源文件、结果和截图哈希见 [verification.json](verification.json)。三语局部捕获可用现有 `UserGuideCaptureTests`，设置 `OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_DIR` 与 `OPTICAL_SYSTEM_DESIGN_GUIDE_CAPTURE_SCOPE=drawing`；取消 scope 后仍执行原完整捕获。
