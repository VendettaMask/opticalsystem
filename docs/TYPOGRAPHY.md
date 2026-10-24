# 产品字体

2026-10-24 起，桌面默认采用以下内嵌方案，无需安装系统字体或联网加载。

| 内容 | 字体 |
| --- | --- |
| 普通主题菜单、导航、标签、标题、图表文字中的拉丁字符 | DM Sans |
| 中文和中文标点 | Source Han Sans SC（思源黑体 SC） |
| 镜头数值单元格、NumericUpDown、状态栏指标、数值报告和固定列文本 | JetBrains Mono；中文回退思源黑体 SC |
| 像素主题默认界面及数值 | 既有 Fusion Pixel 10px Mono zh_hans |
| 标准工程图与图纸导出 | 既有独立 Noto Sans CJK 字体链 |

DM Sans 使用上游可变字体；思源黑体 SC 与 JetBrains Mono 各包含 Regular、Bold 原文件。普通正文保留常规字重，现有标题沿用其强调字重。默认基准仍为 13 DIP，字号 token、用户缩放、控件高度和侧栏宽度约束继续生效。源文件未裁剪或改名修改内部字体身份。

“设置 → 格式与字体”中的“产品默认（推荐）”采用上表随主题切换的角色组合，保存时仍用空 `FontFamily` 表示默认。此前没有指定字体的设置自动使用新方案；明确指定的字体保留，并同时覆盖普通与数值角色。字体列表提供三种内嵌家族和系统字体，暂不可用的已存字体名称也保留，避免打开设置就丢失选择。恢复默认重新启用产品方案。预览跟随所选主题，字号单位为 DIP。

本次没有把思源宋体设为默认或加入安装包。长篇文档正文的宋体方案保留为后续独立排版选择。纯文本和 CSV 本身没有字体信息，数值格式和光学数据不因显示字体变化而改变。

实现入口为 `ProductFonts`、`DisplayTypography` 和主题应用服务；窗口共享界面字体动态资源，数值控件使用 `numeric-text` 样式或数值字体动态资源。家族分别放入 Latin、Cjk、Numeric 资源目录，字符回退由复合字体显式指定，避免同目录字体集合自行跨角色选字；像素主题的集合直接限定到原 TTF 文件。现有控件在主题/显示设置更新时会重新解析资源，显示设置分别预览界面与数值两种角色。

## 来源与分发

- [DM Sans 官方源](https://github.com/google/fonts/tree/main/ofl/dmsans)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/DM-Sans-OFL.txt)。
- [思源黑体官方源](https://github.com/adobe-fonts/source-han-sans)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/Source-Han-Sans-OFL.txt)。
- [JetBrains Mono 官方源](https://github.com/JetBrains/JetBrainsMono)，[许可副本](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/JetBrains-Mono-OFL.txt)。

五个字体原文件的大小、SHA256、官方 Git blob 与下载地址保存在[来源清单](../src/OpticalSystemDesign.App/Assets/Fonts/Typography/manifest.json)。全部附带 SIL OFL 1.1 及原版权声明；构建和标准发布复制三个许可文件到 `Assets/Fonts/Typography`。既有像素、工程图字体和其他第三方许可继续各自保留。当前构建、回归和真实渲染范围见[当前状态](CURRENT_STATUS.md)。
