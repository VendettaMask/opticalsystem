# 主要动作与紧凑侧栏

本页为该批次的实现与运行证据。跨模块状态及不同测试集的适用范围见 [当前状态](CURRENT_STATUS.md)，全仓文档见 [索引](README.md)。


2026-09-27；实际工程为 .NET 10 / Avalonia 12.1.0 / Dock 12.0.0.2。本次修改生产控件，不是 HTML 概念预览，不调整光学计算、字段含义或数值默认值。

## 已实现：按钮角色

沿用 Fluent `accent` 角色和现有集中语义 token，不新增各页面私有颜色。普通 Light 下主按钮默认 `#2F65CC`、悬停 `#2554AF`、按下 `#1D448F`，图标和文字白色；松开恢复悬停，移出恢复默认，保留键盘焦点。

| 页面 | 主要动作 |
| --- | --- |
| 优化向导 | 生成评价函数 |
| 评价函数编辑器 | 执行优化 |
| 公差运行 | 运行 |
| 公差向导 | 确定 |
| 优化变量滑块 | 应用 |
| 库存镜头匹配 | 开始匹配 |
| 库存镜头查看 | 搜索 |

取消、关闭、重置、恢复初值、导出、厂商页面等保持次级；公差向导同功能的“应用”保留次级以避免两个并列主按钮。位置、尺寸、快捷键、点击事件和业务可用性条件不变。其他主题继续解析各自原有的 accent 资源。

`ControlAvailability` 将现有禁用条件同步到禁用时也可显示的提示和无障碍 HelpText，重新启用时清除过期禁用原因。覆盖优化评价/采样选项、公差反求/补偿、无内部表面的滑块、库存目录范围筛选和没有可打开地址/模型的候选。向导兼容占位按钮明确说明当前不支持或应在编辑器操作；没有将占位功能实现为可用功能。

向导对比度条件不满足时，在原摘要中显示空间频率及方向权重要求；空系统滑块在原状态区域说明需要可调整的内部表面。说明文字继续使用正常正文/次要正文色，不给整个容器叠加禁用或透明度。

## 已实现：侧栏宽度与表单

- 宽度语义集中在 `UiDensity`：默认 256 DIP，最小 240 DIP，最大 280 DIP。旧的过宽 `LeftPaneWidth` 和布局应用被限制在此范围，ToolDock 最大宽度阻止宽屏中按比例持续扩大。保留工作区面板划分和分隔器。
- 系统选项及内部材料库列表禁用横向滚动；保留纵向滚动。外层纵向滚动条实际占宽，不覆盖输入框。
- 2026-10-05 隐藏内部当前/可用玻璃库滚动条，保留滚轮和键盘滚动并解决卡片右侧遮挡；本次双配置 21 项局部回归及实际渲染见 [修复记录](GLASS_CATALOG_SIDEBAR_2026-10-05.md)。下方保留本页原批次验证范围。
- 参数采用 70 DIP 标签列加弹性输入列，行内间距 6 DIP，标签按需换行并保留完整提示。收紧分组及视场/波长卡片内容缩进，移除下拉框不适合窄侧栏的固定最小宽度。
- “视场数据/添加视场”等原位标题操作允许换行；分组、字段顺序、字号、控件高度、展开状态和业务事件保留。
- 镜头数据表仍可独立横向滚动，本次取消的是侧栏横向滚动。

## 变更文件

- `Controls/ControlAvailability.cs`：复用现有禁用条件的提示与无障碍说明。
- `Panels/OptimizationWizardWindow.cs`、`OptimizationPanel.cs`、`OptimizationVariableSliderWindow.cs`、`TolerancingRunWindow.cs`、`ToleranceWizardWindow.cs`、`StockLensMatchingPanel.cs`、`CommercialLensCatalogPanel.cs`：明确主动作及禁用原因。
- `Controls/UiDensity.cs`、`Services/AppSettings.cs`、`WorkspaceDockFactory.cs`、`PanelManager.cs`、`Panels/SystemPropertiesPanel.cs`：紧凑侧栏和有限宽度内的参数行。
- `PrimaryActionPresentationTests.cs`、`CompactSidebarTests.cs`：生产控件回归与可选 Skia 截图。
- `BlueThemeInteractionTests.cs`、`SystemPropertiesPanelSectionThemeTests.cs`：将旧横向滚动/宽度断言同步为新的布局契约。

## 验证记录

- 默认 Debug / Release 全解决方案均构建成功，各 0 警告、0 错误。
- Debug / Release 相关回归各 70/70 通过（按钮呈现、紧凑侧栏、分组主题、蓝色交互、容器布局、库存目录/匹配、工作区模型），不是全仓测试总数。
- 原生 macOS 独立验证实例实际重启新构建：确认侧栏收窄、展开视场后仅纵向滚动、标签与输入左右对齐，以及优化向导主按钮蓝底白字和次级按钮区别；空系统滑块禁用控件清晰，条件说明为正常深色文字。原生截图在任务中展示。
- 独立 Skia 实际控件截图回归 15/15 通过；覆盖七个主动作页面、向导/滑块禁用说明、240/280 DIP 展开侧栏、1024/1600 DIP 整体工作区。截图是实际 Avalonia 控件的离屏渲染，不是原生窗口截屏或概念图。记录位于 `artifacts/validation/primary-actions-20260927/`，截图位于其 `screenshots/` 子目录；`git diff --check` 通过。
- 最初曾遇到测试审批服务额度失败，后续用户要求继续后服务恢复，已完成以上 Debug 回归。初次目录测试关闭过早造成跨线程回调，测试现等待目录加载完成再关闭，无需改动生产匹配逻辑。
- 未做 Windows/Linux 原生交互验证，也未执行外部厂商链接、PDF 导出或完整光学数值基线重算。本次不更改仓库全量数值测试基线。

### 可复核截图

- [1024 DIP 工作区](../artifacts/validation/primary-actions-20260927/screenshots/workspace-sidebar-1024.png)
- [240 DIP 展开侧栏](../artifacts/validation/primary-actions-20260927/screenshots/sidebar-expanded-240.png)
- [优化向导](../artifacts/validation/primary-actions-20260927/screenshots/wizard.png)
- [公差运行](../artifacts/validation/primary-actions-20260927/screenshots/tolerance-run.png)
- [滑块禁用说明](../artifacts/validation/primary-actions-20260927/screenshots/slider-disabled.png)

## Git 同步前复验

2026-09-27 在默认 Release 输出重新构建并运行同一 70 项 UI 回归，70/70 通过；本次未重做 Skia 截图或原生桌面操作。原截图和历史记录保留，新增 TRX 见 `artifacts/validation/project-sync-20260927/sync-ui-release-completed.trx`。
