# 主题包开发规范

共享卡片角色、窗口标题字号和圆角令牌按 [UI 规范](UI_DESIGN_SPEC.md) 使用；主题页不重复各功能批次的实现与测试摘要。

状态：已实现架构规范。

应用主题由 `ThemeRegistry` 统一注册。业务窗口、Ribbon、分析页面和面板只声明颜色、图标和边框的语义，不得根据 `Light`、`Dark`、`Isekai`、`Pixel` 等主题名编写显示分支。

## 当前主题包

### 光域（Lumen）

“光域”现已作为独立主题包注册，设置值为 `Lumen`，可从“设置 → 显示格式设置 → 界面主题”选择；默认仍为普通模式，不自动改写用户选择。实现集中在 [LumenTheme](../src/OpticalSystemDesign.App/Theming/LumenTheme.cs)、[状态样式](../src/OpticalSystemDesign.App/Theming/LumenThemeStyles.cs)及注册表，不在业务面板中增加主题分支。

主题采用近黑灰青底、灰白文字、暖金主操作、灰蓝选择态，以及无阴影的方角细边框。侧栏、菜单、输入框、下拉项、表格、Dock 页签和对话框共享资源；使用统一 `ScientificThemeIconPack` 图标及用户字体。材料行始终保留蓝色语义，普通/悬停/选中分别绘制实际 Fluent 行背景，焦点和禁用状态独立处理。没有装饰计数圆环、假进度或假性能读数。

科学画布使用暗底和银灰镜片填充，2 DIP 黑色透镜轮廓、物理光线/波长色及标准导出版式不变。主题不复制光学计算，不改变默认镜头数据/二维文档布局；此前预览中的三维主视觉、外围读数和自定义上下分栏不是正式产品启动布局。

可运行真实 C# 交互预览，直接应用注册资源而不使用旧预览的临时调色板和材料行覆盖；“控件与对话框”按钮打开原生交互示例，示例不计算、不保存设置：

```powershell
dotnet run --project tools/previews/FuiThemePreview/FuiThemePreview.csproj -c Release -- --lumen --engineering
```

追加 `--capture artifacts/validation/lumen-theme-20261019/lumen.png` 可离屏验证工作台并输出原生控件示例图。新主题已进入源码和正常 Debug/Release 输出；后续源码同步见[当前状态](CURRENT_STATUS.md)，不重打安装包或修改远端发布附件。主题实现阶段核对范围及保留失败见[主题交付账目](validation/LUMEN_THEME_2026-10-19.json)。

### 独立 FUI C# 预览（未注册为产品主题）

不带 `--lumen` 的 [C# 工具](../tools/previews/FuiThemePreview/Program.cs)仍保留较早的“光域 03”独立布局试验；这一临时调色板/布局本身**不是正式主题，不进入安装包，不改写用户主题/工程/工作区设置**，与上面的已注册 `Lumen` 主题包区分。独立进程中的[空间仪表窗口](../tools/previews/FuiThemePreview/FuiPreviewWindow.cs)保留暖金/灰白三维线框，增加与暗场统一的二维光路剖面；周边显示同工程实际读数，下方保留 12 列原生镜头编辑器，侧栏复用 `SystemPropertiesPanel`。支持编辑处方、拖动旋转/滚轮缩放三维结构、二维平移/缩放、复位当前图、辅助线开关、重建模型及调整上下分栏，关闭即丢弃修改。顶部菜单用于外观预览，不接入正式文件/保存命令。

右侧“光学参数”已移除表示镜片数量的装饰圆环与刻度，改用“结构：3 片 / 6 面”一行文字；数量来自当前场景，处方刷新或构建失败时清空旧数量。焦距、F 数与参考折射率保持实际读数。该调整仅涉及预览，验证范围见[紧凑读数账目](validation/FUI_CSHARP_PREVIEW_COMPACT_READOUTS_2026-10-19.json)。

新增的 [C# 三维展示控件](../tools/previews/FuiThemePreview/FuiSpatialStage.cs)只投影共享 Core 返回的 `Scene3Dto` 网格、轮廓与材料信息，不生成光学曲面、不计算光线或像质；环线/网格/引导标注是明确标示的 UI 结构辅助，不是工程标准线或性能图。右侧焦距、F 数、参考折射率与镜组计数均来自当前 Core 场景，不绘制假进度或假达标结果。处方变化后先清除旧模型与读数，只有二维和三维均匹配当前修订的结果才能显示。新的[二维仪表壳](../tools/previews/FuiThemePreview/FuiEngineeringView.cs)直接复用产品 `OpticSceneControl`，不另写二维几何、光线映射或物理配色；仅在预览局部资源中使用暗底/银灰镜片填充，并保留可切换的浅底、2 DIP 黑色镜片轮廓、原物理光线颜色、原生平移缩放和比例尺。工具栏移至图外，原生标注展示实际镜组表面编号、材料与像面，光线采用控件已有的 0.85 DIP 细线设置。材料行在真实 Fluent 填充层保持全行蓝色。预览显式采用空间/剖面与底部处方分栏，**不改变产品启动时镜头数据/二维视图的默认文档布局**。调色板反射适配仅在工具中使用，不构成运行时主题插件接口。

```powershell
dotnet restore tools/previews/FuiThemePreview/FuiThemePreview.csproj --locked-mode
dotnet run --project tools/previews/FuiThemePreview/FuiThemePreview.csproj -c Release
dotnet run --project tools/previews/FuiThemePreview/FuiThemePreview.csproj -c Release -- --engineering
```

`--engineering` 直接打开二维页。可用 `-- --engineering --capture artifacts/validation/fui-csharp-preview-v3-20261019/section.png` 在独立 Headless/Skia 进程中做真实控件渲染自检并退出。自检等待实际场景修订完成，执行处方编辑/还原、二维视图切换、光线/标注/明暗底色切换及配色恢复/应用，不以固定延迟代替更新成功。GUI 预览不保存设置；仅 `--capture` 显式写入目标 PNG、自检 JSON 或失败日志。此工具不加入产品解决方案或依赖树。当前核对范围见[二维修订账目](validation/FUI_CSHARP_PREVIEW_V3_2026-10-19.json)；[第二版](validation/FUI_CSHARP_PREVIEW_V2_2026-10-19.json)及[第一版记录](validation/FUI_CSHARP_PREVIEW_2026-10-19.json)保留原始范围，不代表当前外观。

### 正式注册的主题

| 设置值 | 显示名称 | 基础外观 | 图标包 | Chrome/装饰 |
| --- | --- | --- | --- | --- |
| `Light` | 普通模式 | Avalonia Light | `ScientificThemeIconPack` | 窗口级浅蓝渐变、玻璃反光及细亮边、半透明侧栏分组、白色数据区、12 DIP 圆角停靠区 |
| `Dark` | 暗夜模式 | Avalonia Dark | `ScientificThemeIconPack` | 现有圆角、边框和阴影 |
| `Isekai` | 异世界 | Dark 派生，图纸局部羊皮纸 | `ScientificThemeIconPack` | 皮革剑刃 Ribbon、旧金操作框；图纸内角钢笔百合/藤蔓、天体星盘、炼金瓶与晶簇，外侧窄皮革金边 |
| `Pixel` | 像素风格 | Light 派生 | `ScientificThemeIconPack` | 明亮 8-bit 色板、中文像素字、主题色科学轮廓图标、方角硬边与紧凑点阵 Ribbon |
| `Lumen` | 光域 | Dark 派生 | `ScientificThemeIconPack` | 灰青暗场、暖金主操作、灰蓝选择态、蓝色材料行、方角细边框，无装饰圆环或阴影 |
| `System` | 跟随系统 | 由系统决定 Light/Dark | 对应实际明暗主题 | 对应实际明暗主题 |

各主题共用 Optical Scientific Outline 1.3 的功能语义与网格；普通/黑洞/像素保持基础图形，光域与异世界各有221款独立主题SVG。光域使用钢铁侠式科技 HUD，异世界属于剑与魔法，古典炼金器具是其中的功能语汇。图形来源与当前目录见[本地图标说明](LOCAL_ICONS.md)。普通亮模式采用确认的玻璃工作台：窗口级浅蓝渐变及反光顶栏、白色编辑区、半透明玻璃侧栏分组（圆角 6、间距 6、细亮边与浅阴影），选中文档页签白底蓝字，蓝色选中边从下沿移至右侧（2 DIP），保留原生模板与圆角。原连续白底内收分隔线已移除，嵌套视场/波长卡片恢复完整细轮廓；暗夜主题保留自己的 Chrome。`BlueThemeTokens` 集中底色与比例，`LightGlassMaterial` 集中不可变的玻璃反光、透明面及亮边，`LightTheme` 映射 Fluent/Dock 实际资源及当前布局资源，`BlueThemeStyles` 修复原模板的组合状态、焦点及禁用优先级；业务面板不判断主题名。顶部 Ribbon 仍为紧凑分类页签与横向工具栏，各主题共用最小高度 72、命令最小尺寸 56×36、18 DIP 图标、6 DIP 图文间距和 22 DIP 居中短分隔线。Light 标题与工具栏共用 `WorkbenchBackdrop` 的窗口级蓝色渐变，材质反光在原背景层叠加，工具页保持透明；状态栏使用 `StatusBarBackground`，其他主题回落到自身 Surface/SubtleSurface；背景渐变为不可变资源，避免跨界面线程共享可变画刷。布局细节见 [UI 规范](UI_DESIGN_SPEC.md)，历史记录见 [主题报告](BLUE_THEME_2026-09-27.md)。

异世界采用旧铜/浅金/灰绿的魔导器具风格，像素保留原科学轮廓及海军蓝/红/绿角色色。异世界的旧金 Chrome 与装饰层、像素主题的中文像素字体及方角色板保持独立；像素字仍由 OFL 授权的 `Fusion Pixel 10px Mono zh_hans` 提供。旧 GameIconsFantasy/FarmFresh32 目录属于历史来源，当前运行图标以注册表和[本地图标说明](LOCAL_ICONS.md)为准；不改变命令 ID、文案或布局。

异世界绘图材质通过注册表的可选 `DrawingMaterial` 接入，具体实现为 `IsekaiDrawingMaterial`。根据用户两张附图，现嵌入圆角旧金窄框、羊皮纸、透明内角花饰和分析用黄铜星盘四张素材。花叶保留在纸内，上角百合/藤蔓/天体星盘与下角炼金瓶/晶簇沿用；第二张参考补充圆角包角、顶部星芒、双层细线内框及分析页纸内侧边花饰。图例下方仅在实际剩余空间足够时出现黄铜星盘，不增加图例占位。解码和角部画刷按 UI 线程缓存，装饰局部高质量插值，不改变科学数据采样。完整提示词、尺寸与哈希见[素材记录](../src/OpticalSystemDesign.App/Assets/Themes/Isekai/README.md)。菜单、参数和数据页仍使用原深色资源，功能图标保持独立 SVG。

外框分片绘制并独立保留中心徽记，28 DIP 窄边只增加源图外围尺寸；实时及 PNG 外轮廓均为圆角，透明角外不补成矩形。普通源图仍为 1200×900 与 160 DIP 图签，内部花饰绘制在数据后方；2D/3D 使用较清晰墨色，分析和图签降低浓度，不挪动坐标、文字或曲线。实时适应窗口、100% 滚动及 PNG 包含完整窄框；默认 PNG 仍为 1920×1080。报告复用含内部花饰和双细框的原源图及物理文字大小，不纳入外部皮革边，原强制亮色打印选项有效。装饰不接收指针、不随相机移动，不充当镜片/光线/数据。原字体、品牌矢量、其他主题及实体模型紧凑信息栏保留。见[当前验证](../validation/rechecks/2026-11-03/isekai-rounded-border/README.md)；[图内花饰阶段](../validation/rechecks/2026-11-03/isekai-inner-ornament/README.md)、[宽花框阶段](../validation/rechecks/2026-11-03/isekai-rich-frame/README.md)与[首版](../validation/rechecks/2026-11-03/isekai-parchment/README.md)保留历史范围。

Ribbon 下沿通过 `ThemeChromeRole.Ribbon` 提供主题圆角，Light/Dark 为仅底部 8 DIP，Isekai/Pixel 保留锐角/方角。`ThemeLayoutResources.RibbonWorkspaceMargin` 在所有主题提供下方 8 DIP 间隔；外露区域绑定工作区背景，内容随圆角裁切。业务布局不判断主题名称，顺序和非序列模式共用框架。

普通 Light 的文档区全宽分隔保留原内容框顶部 1 DIP 细线，玻璃样式使用柔和亮边画刷，不使用 Dock 的额外蓝色激活色带；`BlueThemeStyles` 将该原生色带宿主限制为零高并裁切，不依赖会被 Dock 自行更新的可见性，也不增加覆盖层或修改全局强调色资源。页签右侧蓝色选中边及其他主题的激活反馈保持独立。

`MainWindowTitleBar` 在 Windows/macOS 的扩展客户区提供实时标题与紧凑搜索，普通 Light 使用窗口级浅蓝渐变及玻璃反光，其他主题使用自己的资源；保留原生窗口按钮及平台标题命中处理，动态标题、全屏和主题切换独立于光学业务。Linux 保留原窗口装饰，快捷键搜索仍可使用命令面板。

普通亮模式的工具栏下拉按钮在悬停、按下和菜单展开时，由原生 `RootBorder` 单独绘制 1 DIP 轮廓；内容呈现层保持透明且不重复绘制边框，与普通工具栏按钮的细框一致。键盘焦点提示继续使用既有原生焦点装饰，不以加粗外框区分下拉按钮。其他主题和非工具栏下拉控件保持各自的原有样式。

## 主题包组成

异世界本次确认的概念稿、图内花饰参考和分析页边框参考原图保存在[参考原稿目录](assets/themes/isekai/references/README.md)，与实际运行素材和验证截图分开标明来源。

每个可选主题通过一个 `ThemeDefinition` 声明：

- 稳定设置值和中文显示名称；
- `RequestedThemeVariant`、是否跟随系统以及是否属于暗色视觉；
- 具体主题的完整 `ThemePalette`；
- 强调色应用器；
- 实际 `IThemeIconPack`；
- `ThemeChromeProfile`；
- 可选的 `IThemeDecorationRenderer` 以及绘图用 `IThemeDrawingMaterial`。

`App` 只遍历注册中心建立资源字典；`ThemeApplicationService` 在 UI 线程中先准备 Fluent/Dock 兼容强调资源，再发布主题变体切换。显示设置窗口直接读取注册中心，并在不改变全局主题的独立样例中预览所选色板和 Chrome。新增主题不得修改 `MainWindow`、`DisplaySettingsWindow` 或业务面板的主题 switch。

`System` 是选择代理，不拥有独立视觉资源。它请求 `ThemeVariant.Default`，`ThemeApplicationService` 监听实际主题变化并同步 Fluent/Dock 根资源；实际颜色、图标、Chrome 和装饰由控件的 `ActualThemeVariant` 解析到明亮或暗夜主题，避免系统模式维护一份会过期的复制色板。

## 图标规则

业务代码继续使用稳定语义名，例如 `save`、`settings`、`telescope`。`LocalIcon` 根据 `ActualThemeVariant` 调用 `ThemeIconResolver`：

- 五个具体主题统一注册 `ScientificThemeIconPack`，使用 32×32、1.65 圆端点线条和结构/主信息/辅助角色；实际使用集为221款语义图形，通用 Lucide 目录仅作备用。
- 普通、黑洞暗夜和像素采用基础图形，分别使用蓝青、暖灰橙砂金、海军蓝红绿角色色；像素字体及方角 Chrome 保持。
- 光域使用独立 Lumen 图稿：枪灰钢色结构、冰蓝青光、切角面板、分段仪表环和精密刻线。局部钢色渐变、信号边光及高亮限于图标，禁用/强调单色状态关闭材质；不在界面中增加假读数或假进度。
- 异世界使用独立 Isekai 图稿：以剑与魔法为世界观，魔导书表示指南，卷轴表示报告，账册羽笔表示编辑，法阵和符文刻线表现光瞳/波前等对象。古典炼金器具是这个世界中的专业器具，使用旧铜、浅金、灰绿角色色。各功能保持自己的识别特征，不能只给通用图换色或套相同徽章。
- `StandardLucide`、`GameIconsFantasy` 与 `FarmFresh32` 已不作为活动功能图标包；原资源的来源、作者与许可证继续保留，不能转移为新原创稿的署名。
- 未知语义统一回退到 `circle-question-mark`，不得显示空白；主题切换使现有图标重绘，不要求重启。

像素字体固定使用 Fusion Pixel Font `2026.07.20` 的 `fusion-pixel-font-10px-monospaced-ttf` 发布包，下载包 SHA256 为 `3d2719a7720d405167b494b7fa1c8e721bfbda59183cccc696ef5d43b0be5945`。应用内嵌 `zh_hans` 矢量 TTF，并以完整文件 URI 限定资源集合，防止新增普通主题字体进入像素字形的候选集合；用户显式选择显示字体时仍以用户设置优先。字体许可副本为 `Assets/Fonts/FUSION-PIXEL-FONT-OFL.txt`。普通主题的内嵌字体及数值角色见[字体方案](TYPOGRAPHY.md)。
新增主题可以复用现有图标包，也可以注册独立包。若声称具有独立图标，必须让全部已知语义名可解析，并对缺失项提供显式回退测试。

后续改图按[语义与主题造型规范](ICON_SEMANTIC_DESIGN.md)核对对象、动作和方法，同时更新原 Word/网页规范、图库与 SVG 包、当前记录和长期偏好；修正已过时的正文规则。原生验证沿用[主题接入记录](../validation/rechecks/2026-11-03/icon-theme-identity/README.md)的范围，文档修改不新增产品通过数。

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
