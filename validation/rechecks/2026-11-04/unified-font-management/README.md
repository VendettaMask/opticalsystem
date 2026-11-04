# 全产品统一字体管理（2026-11-04）

用户要求“全部统一字体管理”。本轮将此前审计出的工程图、设计报告PDF、指南PDF、Word及HTML独立字体入口接入公共策略，所有主题的弹层也使用共享动态界面资源。当前行为见[字体说明](../../../../docs/TYPOGRAPHY.md)。

## 实现范围

- `ProductFonts`及Registry集中管理六种角色、真实字体文件/字重/斜体、许可、缓存和系统自选字体解析；`DisplayTypography`决定主题、用户选择和补字。`DocumentTypography`为一次输出捕获稳定策略，供后台渲染使用。
- 界面/数值/科学分析继续使用公共Avalonia字链；菜单、Flyout、右键、提示和下拉项共用界面动态资源。普通主题保留DM Sans/思源Medium/JetBrains，Pixel保留原像素主字形。
- 元件/系统工程图（预览、PNG、PDF）、设计报告PDF的正文/表格/页眉页脚、指南PDF、Word及两种HTML使用同一便携字体政策。普通文档默认Noto、代码JetBrains；Isekai主字形Almendra，CJK补Noto，公式STIX Two Math。纸面point字号、图框、几何和数据沿用原规则。
- Word内嵌字体表/odttf及许可；测试独立反混淆并逐文件核对SHA256。HTML内嵌font-face/data字体及许可，CSP允许本页字体。中文整字体使示例Word约14.6MB、HTML约24.2MB，这是当前完整内嵌的体积。
- 系统字体选择也在中央解析，只有可用、允许文档嵌入的独立文件参与便携输出；受限、缺失或TTC统一回退到内嵌字链。随包新增STIX为OFL1.1，原文件与许可哈希在字体manifest/本目录收据中保留。官方raw下载成功，Git blob为本地计算身份；API限流不算独立远端blob核验。
- 启动回归发现新增数据保护会跳过镜头表的绑定分类标签。只将固定面角色/面型显式标为可本地化，用户注释/名称/材料/数值仍保留原文；真实窗口英文及Almendra字形验证通过。

## 检查与记录

默认Release App与Tests按`--no-restore -p:BuildProjectReferences=false`增量构建，复用已有Core/内置模块产物；本轮不声明完整解决方案重建。

- `unified-fonts.trx`：新增初版8/8通过。
- `unified-font-regressions.trx`：初轮101过/9失败，原失败记录保留。图片测试将默认Auto当固定4:3；5种全报告夹具同样误认为1440×1080；PDF测试期望1440但实际输入为1202/2404；两项启动测试找不到英文面型标签。
- 图片夹具现在显式使用Standard并恢复设置；PDF断言按真实输入宽度2404，仍检查原图/完整纸面尺寸和无损行为。后续`unified-font-final.trx`为108过/2失败；`startup-diagnostic.trx`保留真实可见中文的诊断，修复分类标签后`startup-correction.trx`为2/2通过。
- `unified-font-accepted.trx`：最终广范围定向回归 **112/112通过，零失败/跳过**，包含新增10项字体/字形/实际输出检查，以及普通字体、Isekai字体锁/语言/启动/多级弹层、报告各格式/公式/原图、工程图和指南相关回归；不累计前置重叠结果。
- `unified-popup-accepted.trx`：补充1/1通过，同一个已打开弹层依次切换Light/Dark/Pixel/Lumen/Isekai/Light，核对实际字体资源及绘制字形。与上一批无重复，合计 **113个不同用例通过**。初版`unified-popup-defaults.trx`误把可变DM Sans实际名称“DM Sans 9pt”要求为“DM Sans”，现按同仓既有字形测试的命名实例规则检查。最后两次构建均零警告/错误；一次其他testhost占用默认Tests输出的MSB3026/3027/3021记录保留在`build-tests-output-lock.log`，自然释放后重建成功，未结束其他进程。
- `audit_pdfs.py`及`pdf-font-audit.json`：6份实际PDF逐字体检查。Skia对Noto使用内嵌Type3字形轮廓，Almendra/JetBrains/STIX使用字体文件，均带ToUnicode。检查50.000、0.5876、0.012345、选定中文及λ/ρ可提取；未声称任意汉字逐码点往返。实测“长”与部首⻓共享字形时，Skia映射提取为U+2ED3，该别名明确记录；没有为通过检查改动报告源文或数值。
- Poppler重新渲染6份PDF为`artifacts/*-pdf-page.png`，并人工复核正文、公式、页眉页脚、中文补字和工程图，未见缺字方框或裁切。原生工程图预览另存`*-engineering.png`。指南中的历史截图属于固定图像，不因当前字体策略重绘。

实际样本：[Isekai报告](artifacts/Isekai-pdf-page.png)、[普通指南](artifacts/Light-guide-pdf-page.png)、[Isekai工程图](artifacts/Isekai-engineering-pdf-page.png)。测试源为`UnifiedFontManagementTests`；所有证据在后台Headless/Skia/Poppler生成，没有操作或重启用户程序。

## 边界

纯文本、CSV、Markdown、固定轮廓Logo/公式SVG、已有截图、操作系统原生标题栏/文件对话框不属于可切换的产品字体家族。Word已验证包结构、字体反混淆及原文；此环境没有LibreOffice，未启动原生Word或浏览器做分页/打印验收。系统字体读取/嵌入路径不是对所有第三方字体的兼容认证。字体调整也不证明全部界面文案翻译完毕。

字体与导出定向通过不等于全仓测试、全部光学精度、发布/安装或macOS原生GUI认证。未改Core光学公式/验收门槛，未提交、推送或触发远端打包；同时存在的其他聊天修改保留。

`tested-identities.json`记录本次字体源码/资产、最终输出及构建产物SHA256；其中112项批次与补充弹层批次的Tests DLL分别标识，不将后加测试的编译产物冒认为前一批的原始身份。`audit_delivery.py`核对10项manifest身份及输出许可副本，生成此收据。
