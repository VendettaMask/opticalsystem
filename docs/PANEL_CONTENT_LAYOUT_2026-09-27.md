# 库存匹配与制图容器修复

后续 [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md) 的 Debug/Release 70 项回归包含本页 8 项容器用例。下方 86/87 是当时扩大到架构检查的历史结果；70 项未纳入该失败用例，不能解释为它已修复。


本次在 Avalonia 12.1.0 / Dock 12.0.0.2 的原有面板内修复测量、裁剪及文本适配，不替换模型、匹配算法、制图输出或事件，不调整现有面板划分。

## 已实现

- 库存匹配：移除结果表格的 `MinHeight = 360`。原表格在有限星号行中被强制拉高并居中，向上绘制到说明及“厂商页面”区域；修复前的真实控件回归测得表顶 307 DIP、按钮底 319 DIP，原生桌面也复现了覆盖。现按剩余高度布局，结果边框局部裁剪，说明和按钮正常参与 Auto 行测量。
- `ScrollableHeaderGrid` 保持原来的顶部区和下方内容区。顶部保留自然高度，只在短视口中限制其测量并滚动，为下方保留最多 180 DIP（小窗口为高度的 40%）。库存厂商组和目标摘要在有限宽度中测量，按原顺序换行。
- 公共 Fluent 表头：原模板隐藏排序图标后仍占用 32 DIP，窄列“排名”“启用”等被截断。`DataGridHeaderLayout` 只把该模板的排序槽改为按内容测量，保留排序、键盘焦点、列宽拖动、背景及数据模型。表头与标准 `CellTextBlock` 的长文字采用省略号，并提示原始完整内容；自定义单元格编辑器不受该规则影响。
- 库存匹配和库存目录：EFL、EPD 的 `mm` 以及相对偏差的 `%` 独立测量，列名优先收缩，单位保留；短标签通过完整提示解释，保留原 Header、绑定及列顺序。
- 三套光学制图：长参数用短名称和完整提示，如“厚度上偏差”对应“中心厚度上偏差 (mm)”。单位放在标签第二行并按内容增高，仍使用原 118 DIP 标签列、340 DIP 宽屏设置面板、900 DIP 断点和原窄屏分栏比例。Logo 操作只在自身字段内换行。
- 制图及顶部区滚动条关闭悬浮覆盖，按实际宽度参与测量，避免背景不透明的悬浮滚动条遮住编辑器右边框；预览移除会溢出有限分栏的固定最小高度，保留现有预览操作与业务值。

## 文件

- `src/OptilandWorkbench.App/Controls/ScrollableHeaderGrid.cs`
- `src/OptilandWorkbench.App/Controls/CompactLabel.cs`
- `src/OptilandWorkbench.App/Controls/DataGridHeaderLayout.cs`
- `src/OptilandWorkbench.App/App.cs`
- `src/OptilandWorkbench.App/Panels/StockLensMatchingPanel.cs`
- `src/OptilandWorkbench.App/Panels/CommercialLensCatalogPanel.cs`
- `src/OptilandWorkbench.App/Panels/OpticalDrawingPanel.cs`
- `tests/OptilandWorkbench.Tests/PanelContentLayoutTests.cs`

## 验证边界与证据

新增 8 项实际 Avalonia 控件用例覆盖：库存页分离/重挂及窗口隐藏/重显；关闭后连续三次重开并在标签/平铺间切换；1180 × 660 和 720 × 500 视口、1/2 倍缩放；设置区滚动；三套制图在 1200/720 DIP 宽度下的标签、单位、输入框、滚动条和 Tab；亮/暗主题表头空间、真实点击排序及长文字提示。

独立进程启用 Skia 的同组截图回归 **8/8** 通过。截图由实际生产控件和真实字体渲染，属于 Headless + Skia 运行截图，不是概念图，也不是原生桌面截图。代表文件：

- `artifacts/validation/panel-content-layout-20260927/captures/stock-third-opening.png`
- `artifacts/validation/panel-content-layout-20260927/captures/stock-720.png`
- `artifacts/validation/panel-content-layout-20260927/captures/drawing-Iso10110-1200.png`
- `artifacts/validation/panel-content-layout-20260927/captures/drawing-GbT13323_2009-720.png`
- `artifacts/validation/panel-content-layout-20260927/captures/table-Light.png`

原生 macOS 专用验证实例已复现旧覆盖；更新后实际执行“关闭其他页 → 数据库 → 库存镜头匹配”，确认再次打开时结果说明、按钮、表头分离。另检查了制图标签、完整输入框右边框、参数点击与 Tab 切换。截图由会话中的计算机工具返回。没有更改另一个正在运行的用户工程。

2026-09-27，默认 Debug、Release 解决方案构建均成功，**0 警告、0 错误**，默认可运行输出已更新。两种配置的相同相关测试集各 **86 通过 / 1 失败 / 共 87 项**，零跳过；不把两种配置相加。其中本轮新增的 8 项全部通过。

唯一失败为 `LayeringArchitectureTests.AppUiCardsUseSharedChromeTokens`：`Theming/BlueThemeStyles.cs:292` 和 `Theming/LightTheme.cs:43` 使用字面量 `new CornerRadius(6)`。这是此前 [商业代码审计](COMMERCIAL_CODE_AUDIT_2026-09-27.md) 已记录的失败，本轮未修改这两个主题文件；不把本次结果表述为全部测试通过。

相关测试范围为 `PanelContentLayoutTests`、`AccessibilityAndResponsiveLayoutTests`、`CommercialLensCatalogPanelTests`、`StockLensMatcherTests`、`ManufacturingDrawingTests`、`WorkspaceMdiInteractionTests`、`BlueThemeInteractionTests`、`LensEditorLayoutTests`、`SurfacePropertiesPanelTests`、`LayeringArchitectureTests`。8 个修改/新增的 C# 文件格式验证与 `git diff --check` 通过。日志、Debug/Release TRX 和截图位于 `artifacts/validation/panel-content-layout-20260927/`。

本轮为局部界面回归，不更新商业审计的全量数值基线，不代表已走查所有业务表格；未打开外部厂商链接，未执行新的 Zemax 数值对照或 PDF 导出人工验收。
