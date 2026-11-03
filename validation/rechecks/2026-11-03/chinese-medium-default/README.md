# 中文 Medium 正式接入

2026-11-03。用户选定此前思源黑体 Medium 的实际预览并授权接入。

普通主题中文正文使用未经修改的 Adobe Source Han Sans SC Medium（500）文件，替换原 Cjk 资源集合中的 Regular。原 Bold 保留，沿用既有标题强调；不提高窗口或控件的全局字重。英文 DM Sans 与数字 JetBrains Mono 原文件、复合字体角色、现有字重、用户自选字体、像素默认字体及工程图字体保持。仍使用原字号和布局，不改光学计算。

## 字体身份

- Medium：16,546,328 字节，SHA256 `1df61d31687d04fd2f928a3bb6ca6cd61f0e988cc267cf317f32406edbb49f70`，Git blob `304b3e6cc2eb1b0c694d53fba21107fe83116cea`。
- [Adobe 官方身份回执](official-font.json)核对上游文件名、字节数和 Git 对象；与用户已选预览使用的文件一致。[五个字体的本地完整性核对](font-integrity.json)对应[产品来源清单](../../../../src/OpticalSystemDesign.App/Assets/Fonts/Typography/manifest.json)。
- 继续内嵌字体，使用现有完整 Source Han Sans SIL OFL 1.1 许可；不依赖系统安装字体或运行时联网。

## 验证

正常默认 Release App/测试构建完成。[最终测试](final-typography.trx)、[日志](final-tests.log)：**27 通过、0 失败、0 跳过**。过滤范围为 `ProductTypographyTests`、`LensHeaderTypographyTests`、`LensEditorLayoutTests`、`ReferenceWorkbenchLayoutTests`。四种普通主题使用真实 Skia/HarfBuzz 字形，确认普通中文为 Medium 且无合成、英文/数字仍是原常规字重，数字仍等宽；保留原有字体覆盖、标题、像素字体和工作台布局检查。

首轮 [21 项记录](typography.trx)、[日志](tests.log)为 20 通过、1 失败：本轮新增的“所有 Bold 字体均不合成”断言还检查了既有 JetBrains Mono Bold，后者报告 Bold simulation。未改变英文或数字字体；将新增无合成断言限定为本轮中文接入，原有中英数家族/字重检查保留。最终实际主窗口确认中文 Medium 与原 Bold 都没有合成。记录不将数值字体既有模拟状态认定为本轮修复。

以下为正式 MainWindow 的离屏实绘，无字体/文本 Run 覆盖，使用同一 Double Gauss 示例。三份旁置 JSON 都确认处方恢复状态未变、未保存用户设置，并记录实际字形和 App SHA256。

- [1600×1000，13 DIP](workbench-1600x1000-13.png)，[回执](workbench-1600x1000-13.json)。
- [900×700，13 DIP](workbench-900x700-13.png)，[回执](workbench-900x700-13.json)。
- [1024×800，18 DIP](workbench-1024x800-18.png)，[回执](workbench-1024x800-18.json)。

三张截图均已目检。截图工具仅存在临时目录，不进入产品；读取正式 App，未启动原生桌面窗口。测试 App 和截图 App 的身份在 [验证回执](verification.json)中记录。本次没有重跑完整数值矩阵、打包发布或同步远端。
