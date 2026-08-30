# 主题包开发规范

状态：已实现架构规范。

应用主题由 `ThemeRegistry` 统一注册。业务窗口、Ribbon、分析页面和面板只声明颜色、图标和边框的语义，不得根据 `Light`、`Dark`、`Isekai` 等主题名编写显示分支。

## 当前主题包

| 设置值 | 显示名称 | 基础外观 | 图标包 | Chrome/装饰 |
| --- | --- | --- | --- | --- |
| `Light` | 普通模式 | Avalonia Light | `StandardLucide` | 现有圆角、边框和阴影 |
| `Dark` | 暗夜模式 | Avalonia Dark | `StandardLucide` | 现有圆角、边框和阴影 |
| `Isekai` | 异世界 | Dark 派生 | `GameIconsFantasy` | 旧金锐角框、工作区/视口/对话框双框、皮革剑刃 Ribbon |
| `System` | 跟随系统 | 由系统决定 Light/Dark | 对应实际明暗主题 | 对应实际明暗主题 |

`Light` 和 `Dark` 的图标几何、卡片圆角、控件圆角、边框厚度和 Ribbon 阴影保持改造前参数。异世界主题使用从 Game-icons.net 官方仓库筛选并内嵌的 `GameIconsFantasy` SVG 路径目录，包含 86 个语义映射并覆盖当前界面全部图标用法；旧金 Chrome 和装饰层不会修改命令 ID、文案、结构边框厚度、布局尺寸或输入命中区域。

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

`System` 是选择代理，不拥有第四套视觉资源。它请求 `ThemeVariant.Default`，实际颜色、图标、Chrome 和装饰由控件的 `ActualThemeVariant` 解析到明亮或暗夜主题，避免系统模式维护一份会过期的复制色板。

## 图标规则

业务代码继续使用稳定语义名，例如 `save`、`settings`、`telescope`。`LocalIcon` 根据 `ActualThemeVariant` 调用 `ThemeIconResolver`：

- 普通和暗夜主题直接返回固定版本 Lucide 定义，确保现有显示不变；
- 异世界主题由 `GameIconsFantasy` 包直接加载 Game-icons.net 上游填充式 SVG 路径；每项都记录作者和原始文件，未映射语义回退到同套 `help.svg`；
- 未知语义统一回退到 `circle-question-mark`，不得显示空白；
- 主题切换会使现有 `LocalIcon` 失效重绘，不要求重启。

新增主题可以复用现有图标包，也可以注册独立包。若声称具有独立图标，必须让全部已知语义名可解析，并对缺失项提供显式回退测试。

## Chrome 角色

`ThemeChromeRole` 当前包括 Ribbon、工作区、设置卡片、普通表面卡片、控件框、状态栏、对话框和视口。公共组件通过 `ThemeChrome.Apply` 绑定角色资源；按钮、输入框和 Dock 外壳按钮也从 `ControlFrame` 动态资源读取圆角/边框。只负责装饰的绘制层使用 `ThemeChromeOverlay`。

约束：

- 装饰层必须 `IsHitTestVisible = false`；
- 装饰层的期望尺寸必须为零，不能改变布局测量；
- 对话框在包装现有内容前必须先从 `Window` 逻辑树解除内容，禁止把同一 `ScrollViewer` 同时挂到两个逻辑父级；
- 悬停、展开等瞬时状态通过样式类选择动态主题资源，禁止在状态更新中销毁并重建资源绑定；同一属性需要业务状态色时，`ThemeChrome.Apply` 必须跳过该属性的角色绑定，避免覆盖绑定触发跨线程释放；
- 各主题同一角色的结构边框厚度必须一致，差异化双线、符文和纹理只能画在覆盖层；
- 普通主题的角色参数必须保持现有值；
- 科学绘图区、工程图和导出版式不自动套用幻想装饰；
- 业务代码不得直接实例化某个具体主题的装饰器。

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
- Fluent 与 Dock 的内部模板只覆盖已确认需要的资源键和按钮样式，不复制整套第三方模板，避免上游升级分叉。
- 当前不实现运行时插件发现；新增主题采用编译期注册，保证资源完整性和测试可重复。
