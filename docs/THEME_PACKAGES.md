# 主题包开发规范

共享卡片角色、窗口标题字号和圆角令牌按 [UI 规范](UI_DESIGN_SPEC.md) 使用；主题页不重复各功能批次的实现与测试摘要。

状态：已实现架构规范。

应用主题由 `ThemeRegistry` 统一注册。业务窗口、Ribbon、分析页面和面板只声明颜色、图标和边框的语义，不得根据 `Light`、`Dark`、`Isekai`、`Pixel` 等主题名编写显示分支。

## 当前主题包

| 设置值 | 显示名称 | 基础外观 | 图标包 | Chrome/装饰 |
| --- | --- | --- | --- | --- |
| `Light` | 普通模式 | Avalonia Light | `StandardLucide` | 正蓝菜单、白色主区、雾蓝侧栏与表头、统一蓝色交互，无投影 |
| `Dark` | 暗夜模式 | Avalonia Dark | `StandardLucide` | 现有圆角、边框和阴影 |
| `Isekai` | 异世界 | Dark 派生 | `GameIconsFantasy` | 旧金锐角框、工作区/视口/对话框双框、皮革剑刃 Ribbon |
| `Pixel` | 像素风格 | Light 派生 | `FarmFresh32` | 明亮 8-bit 色板、中文像素字、彩色 32×32 图标、方角硬边与紧凑点阵 Ribbon |
| `System` | 跟随系统 | 由系统决定 Light/Dark | 对应实际明暗主题 | 对应实际明暗主题 |

`Light` 和 `Dark` 保留原有图标几何。普通亮模式采用确认的参考图工作台：正蓝渐变顶栏、白色编辑区、雾蓝背景、独立白色圆角侧栏分组（圆角 6、间距 6、无投影），选中文档页签白底蓝字，蓝色选中边从下沿移至右侧（2 DIP），保留原生模板与圆角。原连续白底内收分隔线已移除，嵌套视场/波长卡片恢复完整细轮廓；暗夜主题保留自己的 Chrome。`BlueThemeTokens` 集中语义色值，`LightTheme` 映射 Fluent/Dock 实际资源及当前布局资源，`BlueThemeStyles` 修复原模板的组合状态、焦点及禁用优先级；业务面板不判断主题名。顶部 Ribbon 仍为紧凑分类页签与横向工具栏，各主题共用最小高度 72、命令最小尺寸 56×36、18 DIP 图标、6 DIP 图文间距和 22 DIP 居中短分隔线。Light 工具栏及状态栏的浅渐变分别通过 `RibbonPageBackground`、`StatusBarBackground` 语义资源提供，其他主题回落到自身 Surface/SubtleSurface；背景渐变为不可变资源，避免跨界面线程共享可变画刷。布局细节见 [UI 规范](UI_DESIGN_SPEC.md)，历史记录见 [主题报告](BLUE_THEME_2026-09-27.md)。

异世界主题使用从 Game-icons.net 官方仓库筛选并内嵌的 `GameIconsFantasy` SVG 路径目录，包含 86 个语义映射并覆盖当前界面全部图标用法；旧金 Chrome 和装饰层不会修改命令 ID 或文案。像素风格改用 CC BY 3.0 授权的 FatCow Farm-Fresh 3.92 彩色 32×32 PNG 目录，并以 OFL 授权的 `Fusion Pixel 10px Mono zh_hans` 矢量 TTF 提供中文像素字；亮蓝标题带、珊瑚红活动页签、向日葵黄选中行、绿色复选状态、连续浅蓝紧凑 Ribbon 及其悬停硬框只由像素主题资源表达，不改动其他主题布局。

Ribbon 下沿通过 `ThemeChromeRole.Ribbon` 提供主题圆角，Light/Dark 为仅底部 8 DIP，Isekai/Pixel 保留锐角/方角。`ThemeLayoutResources.RibbonWorkspaceMargin` 在所有主题提供下方 8 DIP 间隔；外露区域绑定工作区背景，内容随圆角裁切。业务布局不判断主题名称，顺序和非序列模式共用框架。

普通 Light 的文档区全宽分隔使用原内容框顶部 1 DIP 的 `Divider` 淡线，不使用 Dock 的额外蓝色激活色带；`BlueThemeStyles` 将该原生色带宿主限制为零高并裁切，不依赖会被 Dock 自行更新的可见性，也不增加覆盖层或修改全局强调色资源。页签右侧蓝色选中边及其他主题的激活反馈保持独立。

`MainWindowTitleBar` 在 Windows/macOS 的扩展客户区提供实时标题与紧凑搜索，普通 Light 使用正蓝渐变，其他主题使用自己的资源；保留原生窗口按钮及平台标题命中处理，动态标题、全屏和主题切换独立于光学业务。Linux 保留原窗口装饰，快捷键搜索仍可使用命令面板。

## 主题包组成

每个可选主题通过一个 `ThemeDefinition` 声明：

- 稳定设置值和中文显示名称；
- `RequestedThemeVariant`、是否跟随系统以及是否属于暗色视觉；
- 具体主题的完整 `ThemePalette`；
- 强调色应用器；
- 实际 `IThemeIconPack`；
- `ThemeChromeProfile`；
- 可选的 `IThemeDecorationRenderer`。

`App` 只遍历注册中心建立资源字典；`ThemeApplicationService` 在 UI 线程中先准备 Fluent/Dock 兼容强调资源，再发布主题变体切换。显示设置窗口直接读取注册中心，并在不改变全局主题的独立样例中预览所选色板和 Chrome。新增主题不得修改 `MainWindow`、`DisplaySettingsWindow` 或业务面板的主题 switch。

`System` 是选择代理，不拥有独立视觉资源。它请求 `ThemeVariant.Default`，`ThemeApplicationService` 监听实际主题变化并同步 Fluent/Dock 根资源；实际颜色、图标、Chrome 和装饰由控件的 `ActualThemeVariant` 解析到明亮或暗夜主题，避免系统模式维护一份会过期的复制色板。

## 图标规则

业务代码继续使用稳定语义名，例如 `save`、`settings`、`telescope`。`LocalIcon` 根据 `ActualThemeVariant` 调用 `ThemeIconResolver`：

- 普通和暗夜主题直接返回固定版本 Lucide 定义，确保现有显示不变；
- 异世界主题由 `GameIconsFantasy` 包直接加载 Game-icons.net 上游填充式 SVG 路径；每项都记录作者和原始文件，未映射语义回退到同套 `help.svg`；
- 像素风格由 `FarmFresh32` 包加载固定 3.92 版彩色 PNG 并使用最近邻插值保持像素边缘，目录逐项记录原始文件名，未知语义回退到同一包的 `question.png`；许可副本为 `Assets/Icons/FARM-FRESH-ICONS-LICENSE.txt`；

像素字体固定使用 Fusion Pixel Font `2026.07.20` 的 `fusion-pixel-font-10px-monospaced-ttf` 发布包，下载包 SHA256 为 `3d2719a7720d405167b494b7fa1c8e721bfbda59183cccc696ef5d43b0be5945`。应用内嵌 `zh_hans` 矢量 TTF；用户显式选择显示字体时仍以用户设置优先。字体许可副本为 `Assets/Fonts/FUSION-PIXEL-FONT-OFL.txt`。
- 未知语义统一回退到 `circle-question-mark`，不得显示空白；
- 主题切换会使现有 `LocalIcon` 失效重绘，不要求重启。

新增主题可以复用现有图标包，也可以注册独立包。若声称具有独立图标，必须让全部已知语义名可解析，并对缺失项提供显式回退测试。

## Chrome 角色

`ThemeChromeRole` 当前包括 Ribbon、工作区、设置卡片、普通表面卡片、控件框、状态栏、对话框和视口。公共组件通过 `ThemeChrome.Apply` 绑定角色资源；按钮、输入框和 Dock 外壳按钮也从 `ControlFrame` 动态资源读取圆角、边框和阴影。折叠标题、选中行等状态通过独立语义画刷绑定，使像素主题可以使用蓝色标题与黄色选择，普通亮模式使用白色标题与浅蓝选择，暗夜和异世界保留各自的强调语义。只负责装饰的绘制层使用 `ThemeChromeOverlay`。

约束：

- 装饰层必须 `IsHitTestVisible = false`；
- 装饰层的期望尺寸必须为零，不能改变布局测量；
- 对话框在包装现有内容前必须先从 `Window` 逻辑树解除内容，禁止把同一 `ScrollViewer` 同时挂到两个逻辑父级；
- 悬停、展开等瞬时状态通过样式类选择动态主题资源，禁止在状态更新中销毁并重建资源绑定；同一属性需要业务状态色时，`ThemeChrome.Apply` 必须跳过该属性的角色绑定，避免覆盖绑定触发跨线程释放；
- 公共 Chrome 角色使用稳定的布局契约；普通亮模式的侧栏分组有明确的扁平分隔样式，差异化双线、符文和纹理只画在覆盖层；
- 修改其他主题时，普通主题的圆角、边框厚度和无投影参数必须保持现有值；
- 科学绘图区、工程图和导出版式不自动套用幻想装饰；
- 业务代码不得直接实例化某个具体主题的装饰器。

## 按钮角色、可用性和布局边界

执行动作通过现有 `accent` 类声明主次层级，颜色继续由 `PrimaryButtonBackground`、`PrimaryButtonHoverBackground`、`PrimaryButtonPressedBackground`、`PrimaryButtonText` 解析；普通按钮常态白底。不要以全局替换或固定 hover/pressed 色制造持久选中。取消、重置、导出等维持次级；实际采用主动作的页面见 [按钮范围](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)。

`ControlAvailability.Set/Explain` 复用业务可用条件，设置禁用时可见的 ToolTip 与无障碍 HelpText；启用后清除失效条件。不禁用整组容器，也不降低正常标签/说明的透明度。原条件未变，未实现功能仍不可用。

侧栏 256 DIP 默认、240–280 DIP 范围属于 `UiDensity`/Dock 布局契约，不是主题色资源；所有主题共用紧凑表单和仅纵向滚动，保留各主题自己的颜色与 Chrome。

## 新增主题步骤

1. 创建调色板、强调色应用器、图标包和 Chrome 配置。
2. 在 `ThemeRegistry` 增加一个 `ThemeDefinition`。
3. 为全部 `ThemeResourceBindings` 和 `ThemeChromeRole` 提供资源。
4. 增加图标完整性、对比度、切换重绘和装饰零布局测试。
5. 更新本文、UI 设计规范和设计走查记录。

不得通过在各面板增加 `if (theme == ...)` 完成新主题。

## 明确边界

- 公司标识和启动页属于全局品牌资产，不因界面主题改写品牌色；启动页不套主题对话框装饰。
- 波长色、光线色、工程图标准线色、算法固定色标和导出版式属于物理/工程语义，不改成主题强调色。
- 二维布局的镜片曲面、外轮廓和连接边固定为 2 DIP 黑色实线，不随主题变色；填充和选中提示保留原有语义。`SceneSurface`、`SceneLensEdge` 资源键保留兼容声明，当前二维镜片轮廓不再消费它们。三维与实体模型的轮廓绘制不受此调整影响。
- Fluent 与 Dock 的内部模板只覆盖已确认需要的资源键和按钮样式，不复制整套第三方模板，避免上游升级分叉。
- 当前不实现运行时插件发现；新增主题采用编译期注册，保证资源完整性和测试可重复。

主题切换时，`ThemeApplicationService` 记录并恢复上一个主题临时写入根字典的资源，只保留新主题声明的覆盖键，避免普通、像素和暗夜切换后残留前一套控件底色。

## 内置镀膜模块的主题范围

镀膜 UI 属于主程序程序集，直接消费统一 `ThemeRegistry`、动态资源、样式、标题栏和图标/字体资产，不再通过独立项目链接源码或读取另一份设置 JSON。显示设置由主程序宿主管理，主题切换沿用产品的共享资源更新；实验编辑状态与镜头状态仍分开。尺寸和控件规则见 [UI 规范](UI_DESIGN_SPEC.md#镀膜实验室界面适用范围)，当前交付与测试范围见 [状态页](CURRENT_STATUS.md)。
