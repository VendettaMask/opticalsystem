# UI 设计走查记录

日期：2026-07-29  
范围：`OptilandWorkbench.App` 桌面端整体 UI 静态走查。  
约束：本轮只记录明显 UI/UX 问题，不修改界面代码；分析图的绘制表现形式保持冻结，后续除非明确要求，不把分析图样式作为 UI 改版对象。

## 走查方法

- 以主窗口、Ribbon、Dock 工作区、系统属性、镜头编辑器、分析、优化、公差、多配置、材料和查看器面板为主。
- 本轮是源码级 UI 结构走查，没有启动桌面应用做截图回归，因此以下结论优先标记“明显设计风险”和“可预期可用性问题”。
- 严重度含义：
  - P1：会明显影响主要工作流、误导入口、造成不可预期操作或大面积主题/布局问题。
  - P2：中等可用性问题，常见屏幕或复杂数据下体验会明显变差。
  - P3：一致性、可读性、可访问性和打磨问题。

## 全局问题

### P1：Ribbon 信息架构过载，且入口命名不够稳定

Ribbon 当前承载大量分析、设置、工具入口，分析组里还存在重复入口和中英混排名称。例如 `analysis-wavefront` 同时出现在像差分析和波前相关入口中，分析菜单也同时包含 `FFT PSF Cross Section`、`Image Simulation`、`Geometric Image Analysis`、`Fourier MTF vs Field` 等英文/混排名称，见 `src/OptilandWorkbench.App/MainWindow.cs:101`、`src/OptilandWorkbench.App/MainWindow.cs:108`、`src/OptilandWorkbench.App/MainWindow.cs:115`、`src/OptilandWorkbench.App/MainWindow.cs:128`。

Ribbon 视觉上又使用固定高度和固定按钮尺寸：主 Ribbon 高度为 144，按钮宽 78、高 66，按钮内部内容宽 66，见 `src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:184`、`src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:274`、`src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:400`。长标题会依赖换行挤进小块区域，扫描效率和可读性都比较差。

另外主窗口默认选中 Ribbon 第 2 个 tab，见 `src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:113`。如果第 2 个 tab 是“设置”，用户打开软件后看到的是设置导向，而不是镜头编辑、分析或文件工作流导向。

### P1：全局控件样式过强，可能覆盖局部语义

`App.cs` 对 Button、TextBox、ComboBox、NumericUpDown、DataGrid 选中行等做了全局样式覆盖。尤其是 NumericUpDown 全局关闭 spinner，见 `src/OptilandWorkbench.App/App.cs:124`。光学设计里大量数值参数需要小步进调整，关闭 spinner 会降低精细调参效率，也和“数值输入应提供 stepper/slider/input”的常规工具 UI 预期不一致。

DataGrid 选中行全局设为白字和强调色背景，见 `src/OptilandWorkbench.App/App.cs:147`。这会和优化、公差、玻璃库等表格里的状态色冲突，导致“选中状态”和“业务状态”混在一起。

### P1：主题资源和硬编码颜色混用，暗色/高对比一致性风险大

应用已经定义了浅色/深色主题 token，见 `src/OptilandWorkbench.App/App.cs:220` 和 `src/OptilandWorkbench.App/App.cs:231`。但很多面板仍直接写 RGB 颜色，例如 Ribbon 图标 hover/退出颜色写死为品牌蓝，见 `src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:412`、`src/OptilandWorkbench.App/Shell/MainWindow.Shell.cs:520`；系统属性 section header 也大量直接按 `IsDarkTheme` 分支设置颜色，见 `src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs:319` 到 `src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs:330`。

结果是同一控件族在不同面板里会呈现不同的 hover、border、选中和 disabled 状态。后续如果调主题，很容易出现某些面板漏改。

### P1：关键设置经常默认隐藏，但操作会自动触发

分析面板的设置 host 默认隐藏，见 `src/OptilandWorkbench.App/Panels/AnalysisPanel.cs:112`；镜头编辑器的“表面属性与组件”也默认隐藏，见 `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:137`。这降低了关键参数的可发现性。

同时分析设置里存在“自动应用”行为，设置关闭时也可能刷新分析，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:405` 到 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:423`。对耗时分析来说，用户调整下拉框或数值框时自动重算，容易造成卡顿和“还没改完就开始跑”的体验。

### P2：窗口和面板大量使用固定尺寸，响应式能力不足

主窗口最小宽度为 1100，见 `src/OptilandWorkbench.App/MainWindow.cs:325`。分析设置采用 `MinWidth = 780`、`MaxWidth = 960` 的参考样式布局，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:376`。材料库面板最小宽度有 1040 和 980，见 `src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs:102`、`src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs:677`。

这些固定尺寸在大屏上可接受，但当用户把面板停靠到窄列、使用小屏或缩放比例较高时，容易出现横向滚动、挤压、按钮换行失控。

### P2：Dock 工作区默认结构偏“工程师可用”，但导航模型不够显性

默认 Dock 布局左侧只有系统选项，中央是镜头编辑器文档，见 `src/OptilandWorkbench.App/Services/WorkspaceDockFactory.cs:90` 到 `src/OptilandWorkbench.App/Services/WorkspaceDockFactory.cs:120`。分析、优化、公差等作为文档打开，这种模型灵活，但高度依赖 Ribbon 入口；如果用户错过 Ribbon，缺少一个稳定的工作流导航或最近/常用面板入口。

会话布局读取直接反序列化 JSON，见 `src/OptilandWorkbench.App/Services/WorkspaceSessionStore.cs:237`、`src/OptilandWorkbench.App/Services/WorkspaceSessionStore.cs:249`、`src/OptilandWorkbench.App/Services/WorkspaceSessionStore.cs:316`。从 UI 角度看，异常或过大的布局文件可能导致启动期恢复失败、卡顿或回退体验不稳定。

## 面板级问题

### P1：公差面板打开即创建默认操作数

`TolerancingPanel` 构造完成后直接调用 `AddOperand()`，见 `src/OptilandWorkbench.App/Panels/TolerancingPanel.cs:144`。这意味着用户只是打开面板，就会看到或生成一个默认公差操作数。对工程软件来说，打开面板不应暗含编辑动作，否则保存、撤销和用户心理模型都会变得不清晰。

### P2：镜头编辑器密度高，但高级组件入口低可见

镜头表格使用大量固定列宽，例如多个列通过 `DataGridLength(width)`、112、96、106、240 等固定尺寸构建，见 `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:341`、`src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:370`、`src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:499`。密集表格符合光学设计软件习惯，但目前缺少明显的列管理、常用列/高级列分层或横向滚动提示。

组件编辑器使用 `WrapPanel`，见 `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:77`，并由 240px 宽按钮打开，见 `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:125`。打开后几何、孔径、光栅、薄透镜等参数容易混排，且不同表面类型下不相关字段也容易形成视觉噪音。

### P2：系统属性把基础光学数据折叠得过深

系统属性默认显示多个折叠 section，其中“波长”“环境”“高级”都是后置区块，见 `src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs:80` 到 `src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs:82`。波长对绝大多数分析是基础输入，折叠过深会让新用户误以为只需要孔径和视场。

环境区内还有“当前仅保存环境参数，暂不启用温度补偿计算”的说明文字，见 `src/OptilandWorkbench.App/Panels/SystemPropertiesPanel.cs:563`。这类状态很重要，但现在放在普通说明文本中，容易被忽略，也容易让用户误会环境设置已经参与全部计算。

### P2：分析设置布局窄屏表现弱，且结果导航存在例外

分析面板默认参数区是 `WrapPanel`，见 `src/OptilandWorkbench.App/Panels/AnalysisPanel.cs:28`；部分 Zemax 风格设置页则是固定列宽 Grid，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:361`、`src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:376`。两套布局在不同分析之间切换时，用户会感到设置项位置和阅读顺序不稳定。

结果页大多数使用“绘图 / 数据 / 文本”三 tab，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs:87` 到 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs:94`。这是合理方向，但特殊报告类视图会走不同构建分支，可能造成有些分析不像同一个产品体系。

注意：分析图本身的坐标、图例、曲线、矩阵排布、色标等表现形式本轮只记录风险，不建议改动。当前要求是先保证与 Zemax 的逻辑、结果和图形含义一致，非经明确授权不修改绘图表现。

### P2：优化面板状态色和编辑行为过重

优化面板的评价函数表使用固定状态底色：目标行浅绿、指令行粉色、错误行红色等，见 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs:176`。这些颜色不完全走主题 token，暗色主题下会显得突兀，也可能和选中行颜色冲突。

工具栏用两个 `WrapPanel` 承载新增、删除、上移、下移、向导、刷新、运行等动作，见 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs:80` 和 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs:94`。动作多但层级不明显，用户不容易区分“编辑评价函数”和“运行优化”的主次。

### P2：多配置面板过于简化，容易误导功能边界

多配置面板工具栏只暴露新增、激活、选择表面和应用厚度，见 `src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs:50` 到 `src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs:62`。表格列也主要是名称、表面数、总长、有效焦距等摘要，见 `src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs:101` 到 `src/OptilandWorkbench.App/Panels/MultiConfigurationPanel.cs:106`。

这会让用户以为多配置只能编辑厚度，而不是一个可管理多变量/多操作数的系统级能力。入口本身没有错，但表达的能力边界偏窄。

### P2：查看器设置浮层可能遮挡主场景

3D/2D 查看器把设置区作为覆盖层隐藏/显示，见 `src/OptilandWorkbench.App/Panels/ViewerPanel.cs:227`。浮层适合快速调参，但在小屏、复杂光路或放大查看局部时会遮挡视图。分隔线等局部颜色也有硬编码，见 `src/OptilandWorkbench.App/Panels/ViewerPanel.cs:318`。

查看器设置里同时包含渲染模式、光线范围、颜色、比例尺、口径、帧和箭头等选项。它们分别属于“追迹输入”和“显示偏好”，混在一起会增加理解成本。

### P3：材料/制造相关面板宽度和说明文本偏硬

材料库和材料分析面板有较大的最小宽度与固定控件宽度，见 `src/OptilandWorkbench.App/Panels/MaterialDatabasePanels.cs:102`、`src/OptilandWorkbench.App/Panels/MaterialAnalysisPanel.cs:33`。在 Dock 文档区域变窄时，容易强制横向空间。

材料分析中存在显式操作说明文字，例如滚轮缩放等提示，见 `src/OptilandWorkbench.App/Panels/MaterialAnalysisPanel.cs:108` 附近。少量提示可以接受，但如果大量出现，会让专业工具界面显得像教程页，并占用数据区域。

### P3：若干小控件语义不够自解释

镜头编辑器中无限值相关控件使用“∞”作为 checkbox 内容，且 NumericUpDown 局部也关闭 spinner，见 `src/OptilandWorkbench.App/Panels/LensEditorPanel.cs:602`。如果没有 tooltip 或相邻解释，用户不一定能立刻判断它控制的是“无限焦距”“无限周期”还是“禁用数值”。

## 建议优先级

1. 先处理 P1：Ribbon 信息架构、关键入口命名、全局控件样式、主题硬编码、公差面板打开即编辑、分析自动应用的可控性。
2. 再处理响应式基础：减少固定宽度/高度，给窄 Dock 和高 DPI 场景做布局预算。
3. 然后处理核心工作流面板：镜头编辑器列管理、系统属性基础区块、优化/公差工具栏主次、多配置能力表达。
4. 最后做一致性打磨：tooltip、状态文本层级、暗色主题、空状态、错误状态和辅助说明。

## 分析图冻结说明

分析图目前属于与 Zemax 一致性核对的一部分，不应在普通 UI 改版中顺手调整。后续涉及 `AnalysisPanel.Plots.cs`、`AnalysisPlotControl`、波前/PSF/MTF/圈入能量等图形控件时，应先确认：

- 是否改变了图的坐标含义、曲线分组、单位、图例、色标或矩阵排布。
- 是否会影响与 Zemax 截图/结果对比。
- 用户是否明确授权修改图形表现形式。

如果只是修复入口、设置项、主题 token、布局容器或面板导航，也应避免改变分析图的视觉输出。
