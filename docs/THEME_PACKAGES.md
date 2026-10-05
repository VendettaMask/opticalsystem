# 主题包开发规范

2026-10-03 膜层编辑器使用共享 SurfaceCard 角色和 WindowTitle 字号。既有普通 Light 的侧栏标题/浮层圆角从局部 6 DIP 常量归入共享命名令牌，数值与布局不变；见[验证记录](COATING_LAYER_CONSTRAINTS_2026-10-03.md)。

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

`Light` 和 `Dark` 保留原有图标几何。普通亮模式采用“正蓝 × 雾蓝”，普通卡片圆角为 6、无投影，侧栏分组内部连续白底，使用内收底部分隔线和两侧留白的小圆角标题悬停底色；暗夜主题保留原有 Chrome。`BlueThemeTokens` 是背景、文字、边框等语义色值的唯一来源；`LightTheme` 映射 Fluent/Dock 实际资源，`BlueThemeStyles` 在原模板层修复状态组合、焦点及禁用优先级，业务面板不判断主题名。详见[实现与验收](BLUE_THEME_2026-09-27.md)。顶部 Ribbon 已统一为紧凑分类页签与横向工具栏，所有主题共用布局资源：最小高度 72、命令最小尺寸 56×36、20px 图标、自动宽度和单行文字；不再使用像素主题独有的固定宽度大图标命令布局。异世界主题使用从 Game-icons.net 官方仓库筛选并内嵌的 `GameIconsFantasy` SVG 路径目录，包含 86 个语义映射并覆盖当前界面全部图标用法；旧金 Chrome 和装饰层不会修改命令 ID 或文案。像素风格改用 CC BY 3.0 授权的 FatCow Farm-Fresh 3.92 彩色 32×32 PNG 目录，并以 OFL 授权的 `Fusion Pixel 10px Mono zh_hans` 矢量 TTF 提供中文像素字；亮蓝标题带、珊瑚红活动页签、向日葵黄选中行、绿色复选状态、连续浅蓝紧凑 Ribbon 及其悬停硬框只由像素主题资源表达，不改动其他主题布局。

`MainWindowTitleBar` 为 macOS 普通 Light 主窗口提供正蓝标题区域，沿用相同语义 token，并保留原生交通灯及平台标题命中处理；动态标题、全屏和主题切换独立于光学业务。其他主题及 Windows/Linux 保留原窗口装饰。

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

## 独立镀膜实验室的主题范围

镀膜实验室现在源码链接正式 `ThemeRegistry`、资源、样式、标题栏和相同图标/字体资产，支持四个具体主题及跟随系统。Pixel 字体资源 URI 从所在程序集生成，正式 App 的路径保持原值。实验室启动时只读继承主题、字体、字号/字形和数字格式；实验室内主题切换不写主程序设置，也不持续监听主程序。独立五主题控件测试已执行，不借用主程序截图作为实验室证据。见[使用与验证](../labs/CoatingDesign/README.md)。
