# 产品字体

桌面默认采用以下内嵌方案，无需安装系统字体或联网加载；2026-11-03 按确认的实际预览将中文正文改为思源黑体 Medium，英文和数字保持原样。

| 内容 | 字体 |
| --- | --- |
| 普通主题菜单、导航、标签、标题、图表文字中的拉丁字符 | DM Sans |
| 中文和中文标点 | Source Han Sans SC Medium（思源黑体 SC，中等字重） |
| 镜头数值单元格、NumericUpDown、状态栏指标、数值报告和固定列文本 | JetBrains Mono；中文回退思源黑体 SC Medium |
| 像素主题默认界面及数值 | 既有 Fusion Pixel 10px Mono zh_hans |
| 异世界主题界面、数值、分析图、工程图和文档正文/代码 | 固定 Almendra；界面固定 English，中文名称和缺失符号使用注册的补字链 |
| 普通主题标准工程图、PDF/Word/HTML报告正文、指南PDF | 统一注册的 Noto Sans CJK SC 默认文档角色；显式自选字体经公共策略解析 |
| 普通主题文档代码/固定列内容 | JetBrains Mono |
| 原生文档公式及缺失数学符号 | 内嵌 STIX Two Math；必要时补 Noto 中文字形 |

DM Sans 使用上游可变字体；思源黑体 SC 包含 Medium、Bold 原文件，JetBrains Mono 保留 Regular、Bold 原文件。普通中文正文直接选择真实 Medium（500）字形，不做合成加粗；现有标题沿用原来的强调字重。未提高窗口或控件的全局 FontWeight，因此英文和数字的家族、字重与原有强调层级保持。默认基准仍为 13 DIP，字号 token、用户缩放、控件高度和侧栏宽度约束继续生效。源文件未裁剪或改名修改内部字体身份。

“设置 → 格式与字体”中的“产品默认（推荐）”采用上表随主题切换的角色组合，保存时仍用空 `FontFamily` 表示默认。异世界以外，明确指定的字体同时覆盖普通与数值角色。字体列表提供内嵌家族和系统字体，暂不可用的已存字体名称也保留，避免打开设置就丢失选择。恢复默认重新启用产品方案。预览跟随所选主题，字号单位为 DIP。

2026-11-04 用户确认异世界主题强制使用 **Almendra + English**。主题注册项声明字体家族锁与固定语言，运行时界面/数值资源、直接绘图字链和预览均优先服从主题，已有自选字体不能覆盖。该主题下字体家族框显示 Almendra 并禁用，语言菜单固定 English 并禁用，命令入口与本地化服务也拒绝其他语言；字号和同家族字形仍可调整。原字体与语言偏好保留，切换到其他主题时恢复。字体包含上游未经修改的 Regular、Bold、Italic、BoldItalic 四种真实字形；中文用户名称和不在 Almendra 内的符号可补字，不翻译或改写工程内容。随后确认的“全部统一字体管理”将工程图、报告及指南PDF接入相同主题策略，异世界下这些正文也使用Almendra，原生数学公式保留专用数学角色。

本次没有把思源宋体设为默认或加入安装包。长篇文档正文的宋体方案保留为后续独立排版选择。纯文本和 CSV 本身没有字体信息，数值格式和光学数据不因显示字体变化而改变。

实现入口为 `ProductFonts`、`DisplayTypography` 和主题应用服务；窗口共享界面字体动态资源，数值控件使用 `numeric-text` 样式或数值字体动态资源。家族分别放入 Latin、Cjk、Numeric、Almendra、Math 资源目录，字符回退由复合字体显式指定，避免同目录字体集合自行跨角色选字；像素主题的主集合直接限定到原 TTF 文件。现有控件在主题/显示设置更新时会重新解析资源，显示设置分别预览界面与数值两种角色。

异世界的固定字体家族选择框和Language按钮采用明确的中性灰禁用外观，包含文字、图标和箭头；它们没有下拉选择或切换行为。样式仅在异世界锁定策略生效时使用，切出主题恢复原显示和可用状态，字形、字号及其他控件保持各自原状态。

原生菜单项、MenuFlyout/Flyout呈现器、右键菜单、提示与ComboBox下拉选项在所有主题下共用界面字体动态资源，覆盖Fluent弹层默认Inter。异世界使用Almendra，其他主题使用各自默认或用户自选字体；已打开弹层随资源更新。最初异世界专属修复的真实字形和100%/150%/200%截图保留在[历史弹层记录](../validation/rechecks/2026-11-04/isekai-popup-font/README.md)，当前规则已扩大为全部主题统一管理。

## 公共策略与各输出端

主界面、数值角色与主要科学分析绘图控件通过`ProductFonts`、`DisplayTypography`和主题注册表管理；曲线坐标/标注、波前三维、Foucault、Seidel、二维/三维布局，以及镀膜光谱/堆栈绘图均使用共享字链。普通分析PNG和报告中的分析插图复用分析绘制，不另指定一套图中文字字体。

`ProductFontRole`集中定义Interface、Numeric、Engineering、Document、Code、Math六种用途。`ProductFonts.Registry`登记字体文件、真实字形、字节/Skia字形缓存、授权和自选系统字体解析；`DisplayTypography`统一决定主题、用户选择与回退顺序。`DocumentTypography`在每次导出/工程图渲染开始时捕获策略，供后台线程使用，同一次输出不会中途读到另一主题。

工程制图（元件/系统、预览/PNG/PDF）、设计报告PDF正文/表格/页眉页脚、用户指南PDF、Word正文/代码/公式和两种HTML报告均已接入。工程图和PDF的测量、换行、绘制使用同一字链，不再私自加载Noto或临时调用本机缺字匹配。普通主题文档保留Noto默认、代码保留JetBrains；异世界文档/代码/工程图的主字体受Almendra锁定。Math角色始终使用STIX Two Math，避免依赖Cambria Math或用不含公式字形的装饰字体绘制公式。字体家族管理统一，不合并原有DIP/纸面point字号、页框和几何规则。

HTML内嵌`@font-face`、OFL和三类角色CSS变量，CSP允许本页字体data资源；Word以标准OOXML字体表和odttf嵌入原字体及许可；PDF由Skia写入字体子集或Type3字形轮廓并保留Unicode映射。完整内嵌中文字体会增加Word/HTML文件大小，这是当前离线自包含输出的行为。普通主题的显式系统字体由中央解析器读取可用、允许文档嵌入的独立字体文件；不可用、受限或字体集合TTC改用统一内嵌回退，不由各导出端各自猜测。软件随包字体均保留开源授权，未新增需购买的商业字体。

镜头表的面角色/面型是固定UI分类，显式走共享本地化；用户注释、名称、材料和计算数据保持原文。公司字标和已转轮廓的公式SVG是固定图形，不存在可切换字体；既有截图也是固定像素。系统原生标题栏/文件对话框的系统绘制文字由操作系统管理；纯文本/CSV/Markdown正文由打开它的软件选择字体。上述边界不再构成产品内部的第二套字体配置。验证范围、PDF文字提取的既有字形别名限制和原生Word排版边界见[统一管理验证](../validation/rechecks/2026-11-04/unified-font-management/README.md)。

## 来源与分发

- [DM Sans 官方源](https://github.com/google/fonts/tree/main/ofl/dmsans)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/DM-Sans-OFL.txt)。
- [思源黑体官方源](https://github.com/adobe-fonts/source-han-sans)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/Source-Han-Sans-OFL.txt)。
- [JetBrains Mono 官方源](https://github.com/JetBrains/JetBrainsMono)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/JetBrains-Mono-OFL.txt)。
- [Almendra 官方分发](https://github.com/google/fonts/tree/main/ofl/almendra)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/Almendra-OFL.txt)。
- [STIX Two Math 官方分发](https://github.com/google/fonts/tree/main/ofl/stixtwomath)，[上游项目](https://github.com/stipub/stixfonts)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/STIX-Two-Math-OFL.txt)。

十个Typography字体原文件的大小、SHA256、Git blob身份与下载地址保存在[来源清单](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/manifest.json)。STIX通过官方raw地址取得并记录本地SHA256和计算所得Git blob，未将API限流当作远端独立校验成功。全部附带 SIL OFL 1.1 及原版权声明；构建和标准发布复制五个Typography许可文件，既有像素和Noto许可继续保留。当前构建、回归和真实渲染范围见[当前状态](CURRENT_STATUS.md)。
