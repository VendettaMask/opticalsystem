# UI 符合性审计

日期：2026-08-04。最后整改复核：2026-08-30。

范围：`src/OptilandWorkbench.App` 全部桌面 UI 源码、主题资源、公共控件、分析结果页、窗口、面板和现有 UI 文档。

原始结论：项目当时已有主题资源、分析图契约和部分公共 chrome，但固定尺寸和可访问性规则仍分散。2026-08-24 已完成 P1-01 和 P1-02 的基础整改：关键 Dock 文档支持断点重排，自绘交互画布具备自动化 Peer 和键盘路径；其余条目按下文状态维护。

## 扫描结果

本轮使用源码静态扫描和人工复核，没有依赖运行截图结论。

| 项 | 结果 |
| --- | ---: |
| App UI C# 文件 | 86 |
| Panel 文件 | 20 |
| 自绘 Control 文件 | 14 |
| Window 类型约 | 12 |
| `Color.FromRgb/Argb` 或 `Colors` | 362 |
| `Brushes.` | 62 |
| 显式 `FontSize =` | 80，均为命名 token、图表契约或变量 |
| 普通 UI `FontSize` 数字字面量 | 0 |
| 固定/最小/最大宽度数值 | 176 |
| 固定/最小/最大高度数值 | 126 |
| `Viewbox` | 0 |
| `BindThemeResource` | 127 |
| `AutomationProperties` / `AutomationPeer` | 已覆盖 Ribbon、分析参数、Viewer 设置和 4 个自绘交互画布 |

正向结果：

- `Viewbox` 已清零，标准点列图字体被异常放大的直接风险已移除。
- 普通 UI 的数字字面量字号已清零，`DisplayTypography` 提供命名字号 token，并由 `AppUiFontSizesUseTypographyTokens` 防回归测试保护。
- 主题资源和动态绑定已经覆盖大量常规区域。
- 分析图波长颜色、fan 图按视场成组、Dock 浮动页回收、空宿主过滤等近期契约已经落地。
- 分析结果底部区和品牌资产已经有统一方向，但仍需要继续收敛到公共组件。

## P1 问题

### P1-01：窄 Dock 下仍有大量固定最小宽度

状态：已整改。

证据：

- `src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs`：材料库存在 520、980、1040 等大最小宽度。
- `src/OptilandWorkbench.App/Panels/MaterialAnalysisPanel.cs`：面板最小宽度 520。
- `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs`：参数页存在 780 到 960 的宽度约束。
- `src/OptilandWorkbench.App/Panels/ViewerPanel.cs`：设置网格使用 `Auto,150,Auto,150` 固定列。

影响：Dock 分栏、浮动窗口回收和小屏恢复时，面板会强制横向空间，导致内容裁切、滚动异常或挤压中央工作区。

整改：把普通页面改为响应式 `Grid`/`WrapPanel`/滚动布局；大宽度只作为对话框初始尺寸或最小产品窗口尺寸，不用于 Dock 内部文档内容。

处理结果：

- 主窗口最小和恢复宽度下限由 1100/980 调整为 640px；Ribbon 自身保留横向滚动。
- 材料库、设计镜头库、商用镜头目录和材料分析使用 `ResponsiveTwoPaneGrid`，宽布局左右并列，窄布局上下重排。
- Viewer 设置字段与复选项改为 `WrapPanel` 自动换行；分析设置卡改为可伸缩列、垂直滚动和换行操作区。
- 上述 Dock 页面已清除 500px 及以上的固定 `MinWidth`，并由 `DockDocumentsDoNotRestoreLargeFixedMinimumWidths` 防回归测试保护。

### P1-02：可访问性基础缺失

状态：基础整改已完成。

证据：

- 全项目未发现 `AutomationProperties` 或 `AutomationPeer`。
- 全项目未发现标准 `Label` 绑定输入控件。
- 自绘控件如 `AnalysisPlotControl`、`DrawingPreviewControl`、`OpticSceneControl`、`WavefrontSurfaceControl` 主要依赖指针、滚轮、拖拽和双击。

影响：键盘用户、屏幕阅读器、自动化测试和可访问性检查都无法稳定理解界面。图表缩放、复位、拖拽等功能对非鼠标路径不可达。

整改：先为所有命令按钮、输入项、菜单项、图表画布补自动化名称；再给自绘画布补键盘缩放、平移、复位和焦点状态。

处理结果：

- `AnalysisPlotControl`、`DrawingPreviewControl`、`OpticSceneControl`、`WavefrontSurfaceControl` 均可聚焦，发布自动化名称、帮助文本和自定义 `ControlAutomationPeer`。
- 四类画布统一支持 `Home` 重置、`+/-` 缩放和方向键导航；3D 场景与波前表面使用方向键旋转、`Shift+方向键` 平移。
- Ribbon 按钮/菜单项、分析参数输入、文件浏览按钮、Viewer 设置和图表导出命令已发布稳定自动化名称；分析参数同时发布稳定 Automation ID。
- Headless Avalonia 测试会实际创建四个自动化 Peer；键盘映射由独立合同测试覆盖。后续新增的纯图标控件仍必须在各自改动中设置名称。

### P1-03：字体体系分散

状态：已整改。

处理结果：

- `DisplayTypography` 已新增 `SplashTitle`、`PageTitle`、`SectionTitle`、`CardTitle`、`Body`、`CompactBody`、`Caption` 等命名字号 token。
- `SystemPropertiesPanel`、`MaterialAnalysisPanel`、`MaterialDatabasePanels`、优化/公差向导、`AnalysisPanel.Results`、Ribbon、状态栏、启动页和关于页的普通 UI 字号已迁移到 token。
- 普通 UI 不再出现 `FontSize = 10/11/12/13/14/16/22/29` 等数字字面量。
- `LayeringArchitectureTests.AppUiFontSizesUseTypographyTokens` 会扫描 `src/OptilandWorkbench.App`，阻止后续继续写散点字号。

保留边界：自绘图表继续使用图表专用字号和 `DisplayTypography.Scale`；导出文档、工程图和标准版式仍可使用独立渲染尺寸。

### P1-04：主题颜色局部硬编码

状态：已整改。

原始证据：

- `src/OptilandWorkbench.App/Windows/ImageFileViewerWindow.cs` 使用 `Brushes.LightGray` 和 `Brushes.White` 直接绘制边框/画布。
- `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs` 对评价函数行使用硬编码前景色。
- `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs` 中仍有局部行色、方向色和注释色。
- `src/OptilandWorkbench.App/Panels/MeritOperandRowPalette.cs` 有集中但未纳入主题/对比度验证的业务色。

影响：暗夜和异世界主题可能出现低对比、白块、灰块或业务颜色和状态颜色混淆。用户刚才指出的“页面偏灰、图是白色”属于同类问题。

整改结果：
- `ImageFileViewerWindow` 的边框、画布和状态文字改为主题资源，不再直接使用 `Brushes.LightGray` 或 `Brushes.White`。
- `DisplaySettingsWindow` 的校验错误、预览边框和说明文字改为 `TextError`、`Border`、`TextMuted`。
- `AnalysisPanel.Results` 的实光线/近轴光线表格行色改为主题资源，并覆盖明亮、暗夜、异世界三套主题。
- 基点/焦平面/主平面/节平面等分析注释色集中到 `AnalysisSemanticColors`。
- `OptimizationPanel` 不再写死评价函数行前景色；`MeritOperandRowPalette` 统一输出明/暗主题背景和前景组合。
- 新增对比度测试，分析表格行色和评价函数业务行色必须达到 4.5:1。

保留边界：物理波长色、光线/图表数据色、制造图纸输出色和算法色标仍是允许的工程/物理语义例外，但必须集中命名，不能作为普通 UI 色使用。

## P2 问题

### P2-01：卡片、阴影和圆角重复实现

状态：已整改。

原始证据：

- `SettingsPanelChrome` 已存在公共卡片样式。
- `MainWindow.Shell.cs`、`MaterialDatabasePanels.cs`、`LensEditorPanel.cs`、`ViewerPanel.cs` 等仍有不同阴影和圆角。
- 当前圆角值包括 4、5、6、7、8、9、18、20 等。

影响：同类设置卡片在不同面板看起来像不同产品模块，后续主题调整成本高。

整改结果：

- `SettingsPanelChrome` 现在提供 `CardCornerRadius = 8`、`ControlCornerRadius = 5`、`CardShadow`、`ApplySurfaceCardStyle` 和 `ApplyControlFrameStyle`。
- `MaterialDatabasePanels`、`ViewerPanel`、`TolerancingPanel`、`DisplaySettingsWindow`、`SystemPropertiesPanel`、`LensEditorPanel`、`MainWindow.Shell`、`AnalysisPanel.Export` 等普通 UI 已迁移到公共入口。
- 普通卡片阴影不再散落在各面板；`LayeringArchitectureTests.AppUiCardsUseSharedChromeTokens` 会阻止新增局部卡片阴影和 6/7/9 这类分裂圆角。
- 保留的 4/18/20 圆角只用于图例数据标记、Dock 文档 pill 和 Splash 装饰，不作为普通卡片/控件样式。

### P2-02：控件高度和密度没有统一规则

状态：已整改。

证据：

- 全局按钮/输入高度约 29，但各面板存在 30、32、34、35、36、42 等局部高度。
- `DataGrid` 行高/表头高度在镜头、优化、制造等面板不一致。

影响：横向对齐差，底部区和工具栏高度看起来不稳定。

整改结果：

- 新增 `UiDensity`，统一普通控件30、紧凑表格行28、表头30和工具栏点击区32四类尺寸；
- `App` 的 Button、TextBox、ComboBox、NumericUpDown 和 DataGrid 默认样式改用命名令牌；
- 镜头、优化、非序列对象和可制造性表格不再保存各自的数字行高；
- `AppUiTableDensityUsesSharedTokens` 扫描 App 源码，阻止新增 `RowHeight` / `ColumnHeaderHeight` 数字字面量。

### P2-03：`NumericUpDown` 全局关闭 spinner

状态：已整改。

证据：`src/OptilandWorkbench.App/App.cs` 对 `NumericUpDown` 全局设置 `ShowButtonSpinner=false`。

影响：光学设计中的半径、厚度、视场、波长、焦移等参数需要微调。全局关闭 spinner 会降低调参效率，也让“紧凑表格输入”和“普通参数输入”无法区分。

整改结果：`App.cs` 不再全局设置 `NumericUpDown.ShowButtonSpinner=false`，普通参数输入恢复微调按钮；密集表格、紧凑工具条或明确只读场景仍可在局部构造器关闭。`LayeringArchitectureTests.GlobalNumericInputStyleKeepsSpinnerControlsAvailable` 阻止全局禁用规则回归。

### P2-04：长耗时操作缺少统一进度与取消体验

状态：已整改。

原始证据：

- `AnalysisPanel`、`OptimizationPanel`、`TolerancingPanel` 内部已有取消令牌或运行状态，但 UI 主要显示状态文本。
- 全项目只有启动页明确使用 `ProgressBar`。

影响：耗时分析、优化、公差运行时，用户难以判断是否卡死，也缺少显式取消路径。

整改结果：

- 新增 `OperationStatusBar` 公共运行状态条，统一表达执行中、已同步、结果过期、失败和空闲状态。
- 运行超过 500 ms 才显示不确定进度，避免短任务闪烁；运行超过 2 秒且不可取消时显示不可取消说明。
- `AnalysisPanel`、`OptimizationPanel`、`TolerancingPanel` 已接入公共状态条，并把现有 `CancellationTokenSource.Cancel()` 连接到显式“取消”按钮。
- `LayeringArchitectureTests.LongRunningPanelsUseSharedOperationStatusBar` 阻止长耗时页面继续新增局部 `ProgressBar` 或只写状态文本。

### P2-05：查看器和分析设置覆盖层需要继续响应式优化

状态：已整改。

证据：`ViewerPanel` 的设置区域仍使用固定列宽；分析设置已从横向 `WrapPanel` 收敛为两列浮层，但列宽仍是参考样式固定值。

影响：窄窗口或 Dock 分栏时仍可能遮挡场景。

整改结果：查看器设置与复选项使用 `ResponsiveSettingsGrid`，在400px断点把两列等宽布局重排为单列，不再给每个设置单元写死190px宽度。分析设置使用可伸缩星号列、滚动容器和可换行命令区；两类浮层继续共用半透明主题表面和公共卡片 chrome。Headless 测试覆盖宽窄布局切换及控件行列位置。

### P2-06：视觉质量缺少自动化验收

状态：基础整改已完成。

证据：

- 现有主题测试验证资源存在和部分亮度关系，但没有覆盖 WCAG 对比度。
- GUI 契约测试主要覆盖分析布局和数据语义，没有全局 UI 样式快照、DPI、键盘和主题矩阵。

影响：局部修复容易再次破坏其它分析页或主题。

整改结果：三套主题必须定义全部命名画刷；分析业务行色和评价函数行色必须达到4.5:1；分析页脚统一为共享高度；矩阵、fan和点列布局测试阻止文字 `Viewbox` 回归；卡片、字号、表格密度、NumericUpDown spinner、长任务状态条和大最小宽度均有静态或 Headless 契约。真实多DPI整窗截图仍属于发布前人工/GUI捕获矩阵，不以源码测试冒充像素验收。

## P3 问题

### P3-01：界面语言仍有少量不统一

状态：边界已规范化。

证据：启动页等位置仍有英文副标题；部分分析术语中英文混排未标注规则。

影响：不阻塞使用，但会降低产品完整感。

整改结果：`UI_DESIGN_SPEC.md` 明确保留 FFT、PSF、MTF、RMS、Zernike、Jones、Y-Ybar、第三方格式名和产品品牌；普通命令、状态和说明使用中文。主题存储值等内部稳定键不作为可见文案审计对象。

### P3-02：旧 UI 走查文档承担了过多角色

状态：已整改。

证据：`docs/UI_DESIGN_REVIEW.md` 同时包含历史发现、修复记录、当前契约和计划事项。

影响：后续开发难以判断什么是当前必须遵守的规范，什么是已经过期的历史问题。

整改结果：`UI_DESIGN_SPEC.md` 是现行强制规范，本文只维护当前整改状态，`UI_DESIGN_REVIEW.md` 文件头明确限定为历史走查、用户反馈和分析 UI 契约演进；新增实现不得从历史问题段落推断当前行为。

## 建议整改顺序

1. 收敛分析结果页底部区、品牌区、页签和图表背景：这是用户近期反复验证的区域，收益最高。
2. 移除 Dock 文档区内部的大固定宽高：避免平铺、分栏和小屏继续出问题。
3. 补可访问性名称和键盘路径：先覆盖按钮、输入和自绘图表。
4. 统一卡片、阴影、圆角、控件高度和表格密度。
5. 扩展 UI 样式契约测试，把“白灰背景割裂、品牌灰块、底部高度不一致、普通 UI 散点字号”变成自动检查。

## 当前判定

本审计列出的 P1/P2 工程问题已完成基础整改，并由公共资源、公共组件和定向契约保护。2026-08-30增量验证为桌面应用构建`0`警告、`0`错误，以及13项可访问性、响应式布局和架构测试通过（10项 Headless UI、3项分层契约）；镜头库定向验证另有6项通过。遵照当前任务约束未运行全量测试。后续新增页面仍须接受多DPI、三主题和窄Dock的发布前视觉复核，这属于持续质量门禁，不再作为本轮遗留功能。
