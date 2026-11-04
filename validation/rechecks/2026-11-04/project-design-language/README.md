# 全项目设计语言：skill方法同步

用户要求先学习界面设计skill，再将方法同步到整个项目已有的设计语言文件。本轮已完整阅读并采用下列来源的方法，按Avalonia桌面工作流、原生交互及用户确认的主题/字体/科学内容边界改写，没有复制外部网页模板或增加产品依赖。

| 来源 | 用于本项目的部分 |
| --- | --- |
| [Anthropic frontend-design](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md) | 从实际领域出发，明确视觉主体，设计后对照要求并自检 |
| [Dammyjay93 interface-design](https://github.com/Dammyjay93/interface-design/blob/main/.claude/skills/interface-design/SKILL.md) | 工具界面的层级、语义令牌、原生组件/状态复用及可观察的视觉检查 |

两份skill已通过内置skill-installer安装到个人技能目录，可在后续新一轮调用。项目规则不依赖该目录，其他开发者不安装skill也能遵守。[来源文件身份](skill-sources.json)仅记录本次读取日期、公开地址与SHA256，不另复制整套外部skill。

## 仓库落点

- [UI设计规范](../../../../docs/UI_DESIGN_SPEC.md#全项目设计语言与工作方法)为唯一的全项目设计语言与流程入口，覆盖主窗口、Dock、编辑/分析/查看器及各内置实验室。
- [AGENTS](../../../../AGENTS.md)要求后续界面任务读取这些现有规范，不另建竞争的`.interface-design/system.md`，也不引入外部skill的网页默认样式、额外审批或全仓验收门槛。
- [文档索引](../../../../docs/README.md)增加设计入口；[走查记录](../../../../docs/UI_DESIGN_REVIEW.md)记录确认、否决及后续实际检查。
- [主题包](../../../../docs/THEME_PACKAGES.md)、[图标语义](../../../../docs/ICON_SEMANTIC_DESIGN.md)和[图标资源指南](../../../../docs/LOCAL_ICONS.md)引用同一方法，明确光域新目标与现有1.3资源的区别。
- [当前状态](../../../../docs/CURRENT_STATUS.md)独立记录本轮文档范围，不合并或增加产品通过数。

Light/Dark/Pixel/Isekai各自已确认的身份保持；光域新方向是纯投影HUD、无金属/实体舱板/写实环境。此前几张效果图均未获用户确认，不能当作批准母版。本轮没有重绘图标、更新1.3图包或重新导出原Word/网页图标规范；这些仍是实际交付资源的说明，不能改标为已经实现新版外观。

## 检查与边界

使用仓库既有`verify-documentation.ps1 -Strict -AuditDate 2026-11-04`检查Markdown/HTML本地链接与锚点、代码围栏、现行正文重复和文档生命周期；最终结果见[文档检查](documentation-check.json)及[日志](documentation-check.log)。`git diff --check`核对本轮编辑入口的空白问题；[文件校验](evidence-hashes.json)记录此范围的文档身份。

这是文档与后续协作方法同步，没有改动运行代码、字体/布局/配色资源、图形/导出、光学算法或参考容差。未运行产品构建或光学测试，没有重启用户程序、提交、推送或发布；静态文档检查不等于界面已改好、原生视觉验收或精度认证。
