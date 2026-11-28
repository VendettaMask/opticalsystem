# 非序列直接导入流程

2026-11-23，Windows x64，.NET 10 Release。按用户要求，支持的场景直接完整载入；无法导入的文件只显示简短提示和原文件名。

删除原导入检查窗口和应用确认步骤。示例菜单、对象编辑器、文件导入，以及非序列模式下的文件打开共用直接导入。后台读取完成后使用原有整场景事务应用，导入支持撤销。失败、取消文件选择及读取时关闭宿主窗口均保留原场景。内部完整记录及诊断继续由读取器和维护批查工具提供。

22 项定向检查全部通过：

- `NonSequentialImportFlowTests`：10 项，包含三种文件选择入口的成功/取消、全场景直接导入及撤销、部分对象可映射但整文件不可用时的原场景保持、文件不存在以及宿主关闭取消。
- `NonSequentialExampleTests`：9 项，包含 106 份原始输入哈希及导入状态、全部菜单条目身份，以及三主题成功/失败的真实主窗口交互。
- `NonSequentialDesignWindowTests`：3 项，检查其余设计/物理窗口和实验重放。

默认 Release 主程序/测试依赖构建零警告、零错误。三语目录检查 4356 条翻译、3943 处 UI 引用，零缺失、零错误。全部界面验证均离屏完成，未操作用户桌面或运行完整精度矩阵。导入器的几何和物理支持沿用[资源接入的逐文件结果](../nsc-objects-layout/files.md)：本轮调整交互，不改变文件支持数量。

## 实际画面

| 主题与语言 | 显示比例 | 直接载入后的主窗口 | 无法导入提示 |
| --- | --- | --- | --- |
| Light 中文 | 100% | [主窗口](Light-imported.png) | [短提示](Light-unavailable.png) |
| Dark 日文 | 150% | [主窗口](Dark-imported.png) | [短提示](Dark-unavailable.png) |
| Isekai 英文 | 200% | [主窗口](Isekai-imported.png) | [短提示](Isekai-unavailable.png) |

提示仅含结果、文件名和关闭按钮；主窗口已完整列出 Color Fringes 的五个对象，没有检查弹窗。其余既有对象属性编辑器的显示保持原实现。

[机器回执](results.json)记录源身份、实际筛选和证据哈希。`functional.trx.gz` 保存最终 22 项结果；`build.log.gz`、`localization-check.log.gz` 保存构建和翻译检查输出。原资源检查及完整诊断由[资源接入回执](../nsc-objects-layout/README.md)提供。
