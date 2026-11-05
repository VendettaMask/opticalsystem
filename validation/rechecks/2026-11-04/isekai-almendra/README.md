# Isekai Almendra / English policy

2026-11-04 用户选定 Almendra，并确认异世界主题固定英文，字体与语言不可更改。

## 实现范围

- `ThemeDefinition` 声明 `LocksFontFamily` 与 `FixedLanguage`；仅 Isekai 开启，固定内嵌 Almendra / `en-US`。界面、数字资源、直接绘图字链和设置预览服从主题约束，旧 `FontFamily` 不会覆盖。
- 字体选择显示 Almendra 并禁用，语言菜单固定 English 并禁用，实际命令和语言服务也拒绝绕过。字号与同家族字形可调；已存字体与语言偏好保持，切回其他主题后恢复。
- 启动先应用主题再创建工作区；未附加的面板从应用解析主题，修复此前侧栏/表格保留启动字体的实际预览问题。
- 内嵌官方 Regular、Bold、Italic、BoldItalic 四个未经修改的文件，逐一验证官方 Git blob，大小和 SHA256 见[产品字体清单](../../../../src/OpticalSystemDesign.App/Assets/Fonts/Typography/manifest.json)。OFL 原许可随构建/标准发布复制，支持免费商用和随软件分发。用户输入的中文名称和字体缺失的符号保留补字，不改写工程内容。
- 补齐实际菜单/字体设置/分析工具栏、摘要和页脚中观察到的英文缺项；摘要只翻译已知标签，保留结果值。绘图交互提示只增加本地化调用，计算、曲线构造、坐标、交互和物理颜色均不改变。原始分析 DTO/报告文本不作批量改写。

## 验证方法与边界

使用默认 Release 输出目录，`--no-restore -p:BuildProjectReferences=false`，复用工作区已有 Core 和内置模块依赖；不是隔离的全仓重构建。构建日志为 [App](build-app.log) 和 [Tests](build-tests.log)。默认输出的 Almendra OFL 与源文件 SHA256 相同，字体清单全部九个字体文件匹配。

测试在 Skia/HarfBuzz Headless 宿主中运行，无前台应用、真实鼠标键盘或重启。最终单次 **31/31通过，零失败/跳过，1分5秒**，以 [almendra-complete.trx](almendra-complete.trx) 为准；较早的 `policy` / `final` / `verified` 为逐步扩充前的子集或前一版本，不累计测试数量。

检查实际的常规/粗体/斜体/粗斜体字形均为 Almendra 且无合成；旧自选字体不能覆盖界面/数值/绘图字链。验证显示设置禁用与偏好恢复、中文/日文偏好启动、实际 Ribbon 命令拒绝语言更改、13/18 DIP 和150%/200%显示缩放、主题往返与原文档恢复状态。保留普通字体与本地化、原生菜单关闭和 Ribbon 布局回归；真实 Huygens MTF 页用于实际显示检查，不作为新的光学精度认证。

## 实际离屏预览

- [工作台：13 DIP / 150%](previews/almendra-workbench-13.png)
- [工作台：18 DIP / 200%](previews/almendra-workbench-18.png)
- [实际 Huygens MTF 页](previews/almendra-analysis.png)
- [字体设置锁定](previews/almendra-locked-settings.png)
- [四种真实字形及数字控件](../isekai-popup-font/after/almendra-real-faces.png)

所有截图来自真实产品控件，不是生成的字体示意。未提交、推送、打包或重启用户程序；此次变更不构成新安装包交付、全仓认证或完整三语翻译审计。
