# 正蓝 × 雾蓝主题（2026-09-27）

2026-10-03 膜层编辑器使用共享 SurfaceCard 角色和 WindowTitle 字号。既有普通 Light 的侧栏标题/浮层圆角从局部 6 DIP 常量归入共享命名令牌，数值与布局不变；见[验证记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

本文保留配色各阶段的实测记录。当前按钮角色与侧栏布局见 [最新实现](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)；该阶段 Debug/Release 回归各 70/70、Skia 15/15；后续材料行复用和校验状态验证见 [UI 审查修复](UI_AUDIT_FIXES_2026-09-27.md)，完整验证范围见 [状态页](CURRENT_STATUS.md)。


## 技术与实现范围

已实现于现有 .NET 10 / Avalonia 12.1.0 Fluent / Dock 12.0.0.2 桌面工程。不是 Qt、Web 或 HTML 替换版。普通 Light 及系统跟随的 Light 使用本主题；Dark、Isekai、Pixel 保留自己的视觉包。没有收到独立 HTML 文件，本次依据需求中的精确 token、状态矩阵、截图及现有代码实施。

残留高亮有三处来源：Fluent/Dock 模板使用自己的状态资源；工具栏指针事件写入局部颜色，优先级高于主题；数值列的自定义 `CellTheme` 没有继承 Fluent 模板，丢失当前单元格/验证视觉。现分别在资源映射、状态选择器、继承原单元格模板的层级修复。系统跟随还会监听实际主题变化，同步兼容资源并移除上一主题残留键。

## 语义 token

唯一代码来源是 `src/OptilandWorkbench.App/Theming/BlueThemeTokens.cs`。`LightTheme` 映射当前安装版本的 Fluent/Dock 资源；`BlueThemeStyles` 处理模板上的组合状态和焦点。没有覆盖所有子元素颜色的强制规则，也不以显示文案选择样式。以下为完整色值：

| token | 色值 |
| --- | --- |
| `appBackground` | `#F2F5FB` |
| `surface` | `#FFFFFF` |
| `sidebar` | `#F5F7FC` |
| `headerBackground` | `#2F6FD1` |
| `headerHoverBackground` | `#2864C3` |
| `headerPressedBackground` | `#235AB3` |
| `titleText` | `#FFFFFF` |
| `headerMenuText` | `#F5F8FF` |
| `headerMuted` | `#D1DDF0` |
| `headerDivider` | `#4A82D8` |
| `tableHeader` | `#EBF0F9` |
| `textPrimary` | `#22334C` |
| `textSecondary` | `#5C6F8A` |
| `textDisabled` | `#9BA9BF` |
| `emphasisText` | `#2446F0` |
| `emphasisTextHover` | `#1C3ED8` |
| `emphasisTextPressed` | `#1734BB` |
| `divider` | `#DDE5F0` |
| `controlBorder` | `#8297B6` |
| `accent` | `#2F65CC` |
| `accentHover` | `#2554AF` |
| `accentPressed` | `#1D448F` |
| `primaryButtonBackground` | `#2F65CC` |
| `primaryButtonHoverBackground` | `#2554AF` |
| `primaryButtonPressedBackground` | `#1D448F` |
| `primaryButtonText` | `#FFFFFF` |
| `focusBorder` | `#2F65CC` |
| `hoverBackground` | `#EAF1FF` |
| `pressedBackground` | `#CDDDFA` |
| `pressedBorder` | `#5B80C0` |
| `selectedBackground` | `#DFEAFE` |
| `selectedHover` | `#D5E3FC` |
| `selectedPressed` | `#B9CFF5` |
| `materialRowBackground` | `#EAF1FF`（复用统一浅蓝色阶） |
| `materialRowHoverBackground` | `#D5E3FC`（复用统一浅蓝色阶） |
| `textSelectionBackground` | `#BED3FA` |
| `textSelectionForeground` | `#22334C` |
| `disabledBackground` | `#F0F3F8` |
| `error` | `#AD3F3B` |
| `errorBackground` | `#FBEDEC` |

## 已实现的状态与布局

- 普通按钮白底，悬停/按下为不同浅蓝；释放鼠标恢复悬停，移出恢复默认。主要按钮使用品牌蓝三个阶段；显示设置的“应用并保存”、优化向导“生成评价函数”、评价函数编辑器“执行优化”、公差运行“运行”/向导“确定”、变量滑块“应用”、库存匹配“开始匹配”和目录“搜索”均明确使用 `accent` 角色。取消、重置、导出保持次级。
- Toggle、复选/单选、普通页签保留原有勾选/选择模型；选中与 hover/pressed 组合各有资源。禁用覆盖这些组合，事件仍由原控件阻止。
- 顶部仍是分类 Tab 与单行工具栏；正蓝菜单的活动页使用已有选中背景、不显示下划线，工具栏命令中性图标，打开的下拉触发器保留蓝色活动边框。菜单内部白底、深色文字和浅蓝状态。
- 输入框保持白底和灰蓝边，聚焦为 2 DIP 品牌蓝，选区浅蓝配深字。错误红边优先，同时保留独立蓝色焦点外圈。ComboBox 选项增加勾选标记，沿用原生选择、键盘、Escape、外部点击和焦点逻辑，没有替换业务模型。
- 表格选中和失焦选中均保留浅蓝/深字；当前单元格使用独立 2 DIP 蓝边。数字右对齐、文本左对齐、编号居中；数值列继承原模板以保留编辑、取消和验证。
- 滚动条轨道/滑块/箭头、右键菜单、提示框、子窗口、状态栏使用同一资源层。macOS 普通亮模式主窗口已把标题栏和菜单栏统一为正蓝；交通灯仍是系统原生按钮，子窗口及其他平台保留原窗口装饰。
- 主区、普通按钮与输入白底；侧栏外沿雾蓝，分组内部连续白底，以间距和内收细线区分，标题悬停/按下底色两侧留白、圆角 6；普通卡片圆角 6，控件圆角 5，无投影。控件 32 DIP、表格行 36、表头 34；正文延续用户字体与默认 13 DIP，启用字体支持的等宽数字特性。
- 系统选项 Dock 当前默认 256 DIP、最小 240 DIP、最大 280 DIP；旧的过宽设置也被限制。参数采用标签/输入同行，侧栏和内部材料列表禁用横向滚动，纵向滚动条占位且不覆盖编辑器。640×480 下右侧镜头表仍独立横向滚动，不缩小文字；顶部工具栏保留按需横向滚动。详见 [紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)。

默认三位小数、“示例”中的 Cooke/Tessar 和二维镜片 2 DIP 黑线沿用前一阶段实现。本次主题没有改变业务默认值、字段次序、命令、数值计算或存储语义；物理波长、分析业务色和错误语义没有改成普通交互色。

## 历史阶段：标题栏与菜单栏连成深蓝

此前只修改菜单栏，macOS 标题栏仍为系统浅色，未达到参考图的整体效果。现由 `MainWindowTitleBar` 包装主窗口内容：开启 Avalonia 12.1.0 的 `ExtendClientAreaToDecorationsHint`，保留 `WindowDecorations.Full` 和原生交通灯，在 40 DIP 标题区域绘制同一个 `headerBackground`、细分隔线与居中 `headerText`。标题绑定 `Window.Title`，文件名及未保存标记实时同步；两侧对称预留 128 DIP，窄窗口省略长标题。

标题区域使用 `WindowDecorationProperties.ElementRole = TitleBar`，由平台处理拖动和双击缩放，不重写关闭、最小化、最大化逻辑。进入全屏时隐藏自绘标题占位，退出后恢复；系统在全屏鼠标触顶时短暂显示的原生控制层仍由 macOS 绘制。Dark/Isekai/Pixel 不启用此标题区域，跟随系统在实际 Light 时启用；Windows/Linux 尚未启用该定制，不能声称这些平台已实现相同标题效果。

固定版本的实现依据为 [Avalonia Window](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Controls/Window.cs) 与 [macOS 命中处理](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.0/src/Avalonia.Native/WindowImpl.cs)。当时原生运行确认连续顶区（当前色值已更新为上表正蓝）、标题居中、双击放大/恢复及全屏进入/退出后的标题恢复；截图直接展示在会话。自动化测试覆盖标题同步、原生窗口动作配置、主题往返、隐藏后的内容布局与全屏状态。

本次标题修正仅改动 `MainWindow.cs`、新增 `Theming/MainWindowTitleBar.cs` 和 `MainWindowTitleBarTests.cs`，并同步本文、README、GUI 工作流、UI 规范及主题包说明。记录目录为 `artifacts/validation/blue-titlebar-20260927/`；前一阶段截图和 167 项回归保留历史范围。

## 历史阶段：仅强调文字与箭头

新增 `EmphasisText` / `EmphasisTextHover` / `EmphasisTextPressed`，分别为 `#2446F0` / `#1C3ED8` / `#1734BB`。`BlueThemeStyles.EmphasizedText()` 仅设置文字前景和对应箭头笔刷；按钮填充、分隔线、选中底色、布局、焦点边框、禁用色与业务模型沿用既有实现。

范围为系统侧栏的六个分组标题及折叠箭头、当前活动 Ribbon/普通/Dock 页签标题、菜单当前选择/勾选/展开项的标题与子菜单箭头、当前展开或鼠标悬停的视场项标题及箭头。视场编辑卡没有独立选中模型，继续使用现有 `theme-emphasized` 状态（展开或悬停）；未展开且未悬停的视场项保持原色。视场参数摘要、波长编辑项、表格文字、工具栏命令图标和页签关闭图标不属于本次修改范围。

状态选择器按默认→悬停→按下排序，且仅匹配启用控件；真实鼠标释放后恢复悬停色、移出后恢复当前选择状态的默认色。禁用时回到原有禁用样式。Dock 标题数据模板的 `TextBlock` 受应用级文字样式影响，需要在标题 presenter 内局部指定前景，不能只改 presenter 的继承值。这一历史阶段曾将顶栏活动文字也设为亮蓝；当前正蓝版本已按顶部菜单的专用规则恢复近白文字，亮蓝强调限于浅色区域。

本轮代码限于 `BlueThemeTokens.cs`、`BlueThemeStyles.cs`、`SystemPropertiesPanel.cs` 和 `BlueThemeInteractionTests.cs`。新增两项实际控件输入回归，并补充原页签回归的前景断言；覆盖普通页签、Ribbon、Dock、菜单、分组与视场展开/收起，检查按下恢复、禁用、主题往返和非目标内容不变。证据保存在 `artifacts/validation/blue-emphasis-20260927/`。

## 配色阶段：正蓝顶栏与统一交互色阶

顶栏使用 `HeaderBackground #2F6FD1`、`HeaderHoverBackground #2864C3`、`HeaderPressedBackground #235AB3`。标题文字独立为 `TitleText #FFFFFF`，顶部菜单文字为 `HeaderMenuText #F5F8FF`，初版当前菜单下划线为 `HeaderSelectedUnderline #D2E4FF`（已在后续微调中取消并移除该 token），标题分隔线 `HeaderDivider #4A82D8`。顶栏类别沿用现有 Ribbon 选择模型，当前打开类别持续使用展开背景；未打开类别移入为悬停色、移出恢复默认，按下使用第三档颜色。非交互标题背景保持默认色，原生拖动和窗口动作不变。

浅色区域的分组标题、普通/Dock 活动页签、选中菜单及视场标题/箭头继续使用 `EmphasisText` 三态。顶栏虽然由 `TabItem` 实现，按区域使用近白菜单文字，不套用浅色区域的亮蓝文字规则。普通交互背景为 `#EAF1FF` / `#CDDDFA`；持续选中为 `#DFEAFE` / `#D5E3FC` / `#B9CFF5`。本轮补齐 Dock 页签（含失焦选中）、视场展开项、勾选/展开菜单项和打开工具栏按钮的组合状态；原有 ListBox 按下状态也避免被 hover 规则遮蔽。

视场项新增的 `field-editor-expanded` 类只表达既有展开状态供背景样式使用，不增加选中模型，也不改点击与展开/收起逻辑。普通按钮释放恢复悬停，移出恢复默认；已选页签/勾选项保持真实业务选择。禁用控件不匹配新的启用状态规则，沿用原有禁用背景和文字。

主要按钮使用独立 `PrimaryButtonBackground` / `PrimaryButtonHoverBackground` / `PrimaryButtonPressedBackground` / `PrimaryButtonText`，输入框和当前单元格使用独立 `FocusBorder #2F65CC`；文字选区使用 `TextSelectionBackground #BED3FA` 与 `TextSelectionForeground #22334C`。它们与一般品牌色及强调文字变量分开，即使某些数值相同也不混用语义。

本次仅修改 `BlueThemeTokens.cs`、`BlueThemeStyles.cs`、`LightTheme.cs`、`MainWindowTitleBar.cs` 的颜色/状态，以及 `SystemPropertiesPanel.cs` 的展开样式标记；更新两份交互测试。没有改布局、控件尺寸、光学默认值或计算。原生 macOS 主窗口已实际确认正蓝连续顶区、近白菜单、浅蓝当前页签和展开视场，并在会话展示截图。

测试新增一项顶栏的鼠标悬停/按下/释放/移出、类别切换、禁用和主题往返回归；扩展现有 Dock、菜单、视场、标题及焦点测试。打开的原生弹层接管鼠标输入后，宿主按钮的 hover/pressed 组合采用显式伪类检查；弹层打开/关闭和 Escape 仍使用实际输入事件。中间运行出现一次既有 Headless 会话 Dispose 集合关闭异常，复跑通过，没有修改产品或测试宿主来屏蔽异常。最终构建与回归证据目录为 `artifacts/validation/blue-palette-20260927/`。

## 侧栏微调：系统选项分组衔接（阶段记录）

下述尺寸不变及 14 项检查是分组衔接阶段的结果；随后紧凑侧栏已改变宽度、内容缩进和字段排布，当前以 [主要动作与紧凑侧栏](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md) 为准。

“系统孔径”与“视场”等外层分组现在使用连续白底，消除卡片之间露出的雾蓝横带。原来满宽的底边改为 1 DIP 内收分隔线，与内容左右边距对齐。标题的悬停/按下底色使用 6 DIP 圆角、左侧 4 DIP 和右侧 12 DIP 留白，右侧避开覆盖式滚动条；标题文字与箭头的位置、35 DIP 高度和整行点击区域保持不变。分组仍间隔 8 DIP，展开/折叠不额外改变间距。沿用已有 Surface、Divider 和交互色阶，没有增加一套颜色。

`SystemPropertiesPanel.cs` 只给外层分组和容器添加语义类及不接收输入的分隔线，`BlueThemeStyles.cs` 精确匹配普通 Light 提供上述绘制样式。内部视场卡片、输入尺寸、数值及展开业务不变；Dark/Isekai/Pixel 隐藏此分隔线，恢复各自既有边框与背景。

2026-09-27 本次默认 Debug/Release 解决方案构建均零警告、零错误；分组主题与蓝色交互定向回归在两种配置下分别 **14/14** 通过（同一测试集，不相加）。扩展现有分组测试，验证主题往返、展开/折叠、悬停、对齐、完整点击区域及 256 DIP 窄侧栏；没有新增测试项。独立进程使用真实应用主题与面板进行 Skia 2× 渲染，生成展开分组、悬停、窄侧栏和其他主题截图；这一渲染运行的 1 项测试已包含在上述 14 项内，不另计。加入窄窗口检查时发现测试需先处理排队的尺寸变更再读取布局，已在测试渲染辅助函数中修正，最终普通/Skia 两种测试宿主均通过；诊断日志保留。格式与差异检查通过。证据在 `artifacts/validation/sidebar-transition-20260927/`。

本轮截图是实际 Avalonia 控件的 Headless/Skia 渲染，使用测试已有 Cooke 数据，不是概念图，也不是原生桌面截图。当前桌面应用含未保存的 Tessar 文档，本轮未关闭或重启它；默认桌面二进制已更新，保存工作并重启后加载新样式。没有重新进行原生窗口失焦/全屏或跨显示器走查，也未重新运行下述 171 项配色阶段回归及全量光学验证。

## 材料行规则：材料非空行持续显示蓝色

镜头数据中，所有 `MaterialDisplay` 非空的行在普通 Light 下持续使用 `MaterialRowBackground #EAF1FF`，不依赖当前选中行；悬停为 `MaterialRowHoverBackground #D5E3FC`。选中仍为 `#DFEAFE`，选中加悬停为 `#D5E3FC`，当前单元格另有 2 DIP 蓝框；禁用和验证错误保持既有优先级。空材料/空气行不加材料底色，仍正常响应选中和悬停。两个材料 token 复用已有蓝色色阶，不在业务面板硬编码颜色。

此前 `LensEditorPanel` 已根据 `SurfaceEditorRow.HasOpticalMaterial` 添加 `glass-material-row`，并在行载入和材料提交时更新；问题是 Fluent 的 `BackgroundRectangle` 使用不透明白色，盖住了行的原有背景。本次在 `BlueThemeStyles.Tables()` 对真正绘制的模板背景设置材料状态，排除选中、禁用和错误组合，不改模型、材料解析、行选择或计算。所有普通 Light 材料行同时生效；其他主题保留原主题外观，切回 Light 立即恢复材料蓝色。Air 按现有业务在材料列显示为空，不会因存储字符串非空而误涂蓝。

此偏好已写入根 `AGENTS.md` 的 `Persistent UI preferences`，作为后续修改必须保留的项目记忆。新增 `LensMaterialRowThemeTests` 使用真实 Tessar 处方，确认第 1、3、6、7 行同时变蓝，并验证材料清空/恢复/取消、应用服务刷新、撤销、滚动后行复用、文件切换、主题往返、选中/悬停、当前单元格与禁用组合。

2026-09-27 默认 Debug/Release 解决方案构建均零警告、零错误；新增材料行回归与既有 13 项蓝色交互回归在两种配置下分别 **14/14** 通过。这是材料行验证集，与前面的 14 项侧栏验证范围不同，不相加，也不替代历史 171 项主题回归或光学全量基线。独立 Skia 2× 渲染中的 1 项已包含在这 14 项内，截图为实际 Avalonia 面板而非概念图或原生窗口截图。修改文件格式与差异检查通过；日志、TRX 和截图位于 `artifacts/validation/material-rows-20260927/`。本轮未重启含未保存内容的原生窗口。

## 当前微调：顶部菜单不显示选中下划线

按用户反馈，普通 Light 的顶部 Ribbon 分类菜单隐藏 Fluent 模板中的 `PART_SelectedPipe`，并删除不再使用的 `HeaderSelectedUnderline` token。当前类别继续使用已有深一档背景；悬停、按下、禁用、键盘焦点、文字和布局均沿用原实现。规则只匹配 `ribbon-tab`，普通文档/分析页签的选择指示保留；此偏好同步写入根 `AGENTS.md`。

2026-09-27 默认 Debug/Release 解决方案构建零警告、零错误；扩展既有顶部菜单交互回归，在两种配置下各 **1/1** 通过，覆盖点击切换、按下、禁用、键盘焦点、主题往返及普通页签指示保留，没有新增测试项。格式与差异检查通过。证据位于 `artifacts/validation/menu-no-underline-20260927/`。本轮用实际 Avalonia 模板验证，未重启含未保存内容的原生窗口，未新增原生截图，也未重跑前述材料行 14 项、侧栏 14 项或配色阶段 171 项回归。

## 正蓝配色阶段的验证证据与边界

2026-09-27 配色阶段默认 Debug 与 Release 解决方案构建均为零警告、零错误；同一组相关界面与 Dock 回归在两种配置下分别 **171/171** 通过，零失败、零跳过（不相加）。其中 13 项为主题交互/对比度/窄窗口及强调文字测试，另有 1 项标题栏/主题/全屏回归。修改文件的 `dotnet format whitespace --verify-no-changes` 和 `git diff --check` 通过。此前 138 项验证后补齐滚动条与窄窗口修复、工作区恢复覆盖形成 167 项；标题栏修正形成 168 项；强调文字阶段形成 170 项；本次新增顶栏状态回归，以 171 项为最终局部结果。新增 `BlueThemeInteractionTests` 使用真实 Avalonia 控件和输入事件，覆盖鼠标进入→按下→释放→离开、点击次数、组合选择状态、禁用阻止操作、Tab、方向键/Enter/Escape、编辑取消、错误焦点、菜单、滚动条、提示框、主题往返、窄窗口与 2× 渲染。基础正文、表格选中文字、正蓝顶栏各状态文字、主按钮和错误文字对比度检查不低于 4.5:1；焦点蓝对白底不低于 3:1。

| 检查 | 本轮证据 |
| --- | --- |
| 按钮/页签/复选/单选组合状态 | 自动输入事件、实际模板颜色与业务状态断言 |
| 下拉选择、方向键、Enter、Escape、禁用选项 | 自动化；macOS 原生下拉打开、方向键/Enter、Escape 和 Tab 补充走查 |
| 表格选择、单元格焦点、编辑/取消、右键命令 | 自动化回归；原生 Object 单元格编辑后 Escape 恢复 Object |
| 子窗口与窗口失焦/激活 | 原生显示格式窗口、Tab 到主要按钮、自动滚动；主窗口切出后选中仍浅蓝，重新激活正常 |
| 禁用/错误、提示框、滚动条状态 | 自动化组合状态验证；原生禁用撤销/重做及滚动后子窗口截图 |
| 640×480 窄窗口 | 实际 Dock/面板 Headless 输入与布局断言、Skia 截图；原生窗口拖拽未成功改变尺寸，不计为原生窄窗口验收 |
| 高 DPI | 原生 Retina 截图及 Headless 2× 渲染；未验证跨显示器切换和 125%/150% 分数缩放 |

原生 macOS 主窗口、失焦窗口和显示格式子窗口截图已通过桌面工具直接展示在本次会话。原生工具未将独立菜单弹窗包含在主窗口截图内；弹层状态以自动化断言为证，没有声称原生弹层像素验收完成。Windows/Linux 原生窗口、所有浮动 Dock 组合、操作系统实时明暗切换及每一条业务弹窗没有逐一人工走查。

本次正蓝版本的最终构建日志、171 项 Debug/Release TRX、测试筛选范围及格式检查日志位于 `artifacts/validation/blue-palette-20260927/`。

此前强调文字微调的构建日志、170 项 Debug/Release TRX、测试筛选范围及格式检查日志位于 `artifacts/validation/blue-emphasis-20260927/`。本轮原生截图已展示分组标题/箭头、活动镜头页签和展开视场标题；菜单弹层的像素及短暂按下色以实际控件自动输入断言为证，不声称已做原生逐帧捕获。

此前标题修正的构建日志、168 项 Debug/Release TRX、测试筛选范围及格式检查日志位于 `artifacts/validation/blue-titlebar-20260927/`。

前一阶段验证目录为 `artifacts/validation/blue-theme-20260927/`：

- `light-workspace.png`、`light-workspace-narrow.png`、`light-workspace-2x.png`：实际 App 面板的 Skia 渲染，不是概念图或原生桌面截图。渲染宿主使用正式新建文档服务及真实数据，工具栏动作在宿主中为空回调，因此不将该渲染作为业务命令验收。
- `light-controls.png`、`light-controls-after-theme-switch.png`：实际控件渲染与主题往返结果。
- `blue-ui-debug.trx`、`blue-ui-release.trx`、`blue-interaction.trx`：自动化回归明细；较早失败或中间结果只作诊断。
- `upstream/`：本轮核对的固定版本 Fluent 控件模板，辅助确认实际资源键，不用于运行时加载。

原生新建文档确认 2 个表面、1 个视场、1 个波长，入瞳 14、均匀、角度；Object/Image、无限、厚度 100/“-”、空材料、None 膜层各保持独立语义，状态栏 EFFL 0、F/# 0、APER 14、TOTR 101。未根据截图创建或更改产品样例。

这是主题与界面验证，不是新的全量光学验证：既有正式 1311/1311、比较工具 104/104 和各实验室记录保持各自历史范围。本次没有重新采集 Zemax、执行外部数值比较或给出新的精度结论。早先 144/144、108/108 及本轮中间 138/138 不与最终局部回归相加。

## 主要代码位置

- `Theming/BlueThemeTokens.cs`、`LightTheme.cs`、`BlueThemeStyles.cs`：统一色值、模板资源、组合状态。
- `Theming/ThemePalette.cs`、`ThemeChrome.cs`、`ThemeRegistry.cs`、`ThemeApplicationService.cs`、`Services/ThemeResourceBindings.cs`：主题契约、动态切换及系统跟随。
- `App.cs`、`Shell/MainWindow.Shell.cs`、`DisplaySettingsWindow.cs`：样式注册、正蓝顶栏、工具栏状态及主要按钮。
- `MainWindow.cs`、`Theming/MainWindowTitleBar.cs`、`tests/OptilandWorkbench.Tests/MainWindowTitleBarTests.cs`：macOS 正蓝标题区域与原生窗口行为回归。
- `Panels/SystemPropertiesPanel.cs`、`Panels/LensEditorPanel.cs`、`Services/WorkspaceDockFactory.cs`、`Controls/UiDensity.cs`：侧栏、表格模板继承、对齐和响应式密度。
- `tests/OptilandWorkbench.Tests/BlueThemeInteractionTests.cs` 及既有主题/布局测试：交互和尺寸回归。

## 镀膜实验室统一 · 2026-09-28

实验室移除局部 Light 资源映射，源码链接本主题实现与 ThemeRegistry/ThemeApplicationService；执行动作为 accent，次要动作中性，禁用原因复用 ControlAvailability。光谱使用共享 PlotBackground/PlotText 成对资源，修复暗夜轴字不可见；窄窗口滚动条不覆盖字段和工具按钮。Pixel 字体 URI 使用当前程序集名以支持共享源码，正式 App 原路径不变。验证见[镀膜记录](../validation/coating/README.md)。
