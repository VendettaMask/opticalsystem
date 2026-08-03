# UI 设计走查记录

初始走查日期：2026-07-29

当前状态更新：2026-08-04

范围：`OptilandWorkbench.App` 桌面端整体 UI、Dock 工作区和分析结果展示。
说明：P1/P2/P3 段落保留初始走查发现；带日期的更新和“当前实现状态”描述现行行为。分析图默认不在普通 UI 重构中顺手改动，但用户已明确要求的布局、分组、配色和文字修正应按当前契约记录并回归验证。

## 走查方法

- 以主窗口、Ribbon、Dock 工作区、系统属性、镜头编辑器、分析、优化、公差、多配置、材料和查看器面板为主。
- 初始结论来自源码级 UI 结构走查；后续更新结合实现契约、自动化测试和用户提供的界面截图。没有实际验证的视觉结论仍应标记为风险，不能写成已完成。
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

2026-07-31 更新：已将主题调色板扩展为主文字、次文字、弱化、禁用、强调、强调底色文字以及警告/错误/成功状态文字层级；全局 TextBlock、Label、DataGrid 选中行和主要自绘分析/场景控件改为动态资源。原有布局、Ribbon 排列以及波长/视场等分析语义色保持不变。其余面板中仍存在的业务专用硬编码颜色应继续按组件逐步迁移。

2026-07-31 异世界主题：新增独立的 IsekaiTheme.cs，以黑曜石/深皮革底色、旧金强调色、羊皮纸文字层级和奥术蓝场景点缀呈现剑与魔法风格。该主题只替换颜色与绘图资源，不改布局、控件尺寸、Ribbon 排列、现有文案、分析语义色或普通/暗夜主题资源。

2026-07-31 主题补全：系统属性折叠卡片、镜头编辑、优化、可制造性、材料库、查看器分隔线和分析报告标题区改用动态主题资源；可制造性错误/警告/通过行新增随主题切换的语义底色，优化操作数的 Zemax 行分类色保持不变。Window、Button、输入控件、列表和 DataGrid 表面不做全局颜色覆盖，继续由 Avalonia Fluent 主题控制。

2026-07-31 明亮主题修复：撤回对 Window、Button、输入控件、列表和 DataGrid 表面的全局颜色覆盖，明亮模式重新使用 Avalonia Fluent 原生控件底色；Light 调色板数值、暗夜/异世界主题、布局和分析颜色均保持原状。

2026-07-31 文案恢复：撤回 1b088115 中未经明确授权的分析显示名称汉化，恢复 FFT、PSF、MTF、RMS、Huygens、Zernike、Jones、Y-Ybar 和 vs. 等既有产品术语。重复入口清理和布局修复保留；后续任何术语翻译或重命名必须有明确的 UI 文案需求。

2026-08-02 设置样式统一：二维/三维视图与全部分析页面通过独立的 `SettingsPanelChrome` 共用设置齿轮、圆角卡片、边框和阴影。明亮主题新增纯白设置卡片表面，避免原 `Surface`（250,250,252）与分析 `SubtleSurface`（242,242,247）造成的灰度不一致；暗夜和异世界主题继续使用各自主题表面色。后续复核发现 Fluent `Button` 的默认灰色背景覆盖了外层白色卡片，因此共享设置按钮现在也直接绑定 `SettingsSurface`；headless 主题测试验证明亮主题下按钮实际背景为 `#FFFFFF`。该变更只调整视觉样式，不修改参数排列、按钮、展开方式、默认值或计算行为。

2026-08-02 窗口布局修复：批量停靠、关闭、浮动、平铺和层叠现在统一剔除不含文档或工具内容的空浮动宿主；会话保存不再写入空宿主，恢复旧会话时也会过滤历史空壳，因此不会再次出现 `Window / No documents open`。Ribbon 将“浮动全部”明确为“全部独立浮动”，说明每个页面各自使用一个原生窗口；“全部停靠/单一 Pane”改为“保留分栏停靠/合并单窗格”，锁定与解锁合并为一个状态切换按钮，“恢复默认布局”也区分为系统初始布局与已保存默认布局。最终交互语义是：只有“全部独立浮动”在软件外创建原生窗口；平铺和层叠会显式把所有页面移回主文档区，切换 Dock 内部 MDI 后分别执行平铺或层叠；合并同样先回收所有页面，再切回单一标签窗格。该变更不复制业务文档，稳定文档 ID 的复用规则保持不变。

2026-08-03 报告入口修正：分析 Ribbon 原“系统报告”按钮改为更短的“报告”下拉按钮，菜单按“表面数据报告、系统数据报告、分类数据报告 / 系统数据摘要、基面数据”排列并保留中间分隔线；基面数据从“光线迹点”迁回报告组。此次并非只替换文案：表面数据报告输出逐面处方表，系统数据报告输出系统/孔径/视场/波长/近轴分类表，分类数据报告按表面角色、材料和几何类型汇总；旧“一阶量、处方报告”名称仅用于兼容既有会话。

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

已于 2026-07-29 按明确需求在系统选项中加入“材料库”折叠区。界面使用“当前玻璃库 / 可用玻璃库”双列表以及加入、移出、优先级上移和优先级下移操作；当前列表顺序参与未限定厂商玻璃名的解析，并进入撤销、STAROPT 快照及 ZMX `GCAT` 往返。至少保留一个当前目录，避免空选择产生隐式回退。

### P2：分析设置布局窄屏表现弱，且结果导航存在例外

分析面板默认参数区是 `WrapPanel`，见 `src/OptilandWorkbench.App/Panels/AnalysisPanel.cs:28`；部分 Zemax 风格设置页则是固定列宽 Grid，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:361`、`src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Parameters.cs:376`。两套布局在不同分析之间切换时，用户会感到设置项位置和阅读顺序不稳定。

结果页大多数使用“绘图 / 数据 / 文本”三 tab，见 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs:87` 到 `src/OptilandWorkbench.App/Panels/Analysis/AnalysisPanel.Results.cs:94`。这是合理方向，但特殊报告类视图会走不同构建分支，可能造成有些分析不像同一个产品体系。

分析图的坐标含义、曲线分组、单位、图例和矩阵维度属于产品契约。未获得明确需求时仍保持冻结；2026-08-03 已按明确反馈完成的展示修正如下。

#### 当前分析结果 UI 契约

| 分析 | 当前布局与文字规则 |
| --- | --- |
| 标准点列图 | 按视场分组，自动规划平衡网格；点列坐标是像面 X/Y 截距。 |
| 离焦点列图 | 视场为行、五个离焦位置为列；每个小图为 `120×120` 方形，隐藏重复轴标题和刻度文字，只保留统一视场标签、离焦列值和离焦说明。不得把 X/Y 两轴都显示成“像高”。 |
| 全视场点列图 | 单图外层默认方形，X/Y 数据等比例；图例位于独立区域，不参与方形尺寸计算。按波长着色时按波长分组，按视场着色时真正按视场拆分系列。 |
| 普通矩阵点列图 | 视场为行、波长为列；小图为 `140×140` 方形，隐藏重复轴标题，以最小绘图边距扩大点列区域。 |
| 结构矩阵点列图 | 视场为行、结构为列；每个“视场 × 结构”单元叠加全部所选波长，蓝/绿/红波长图例可分别开关。不得把三个波长继续拆成三个格子。 |
| 光迹图 | 单图外层默认方形；按视场或波长着色时，图例标签、颜色和过滤分组保持一致。 |
| 干涉图 | 作为二维瞳面空间分布，单图外层默认正方形，X/Y 数据等比例；不得随结果区宽高比拉伸成矩形。 |
| 色焦移 | 保持普通曲线图的自适应宽屏布局；横轴为焦移、纵轴为波长，不套用二维空间图的方形视口。 |
| 光线像差图 | 每个视场的 `P_y` 与 `P_x` 紧邻组成组合卡片；多个视场形成平衡网格，末行居中，组合卡片内子图保持接近正方形。 |
| 瞳孔像差图 | 与光线像差图使用相同的视场配对布局；所有视场、波长和 X/Y 子图共用全局对称纵轴 `±scale`，说明区只保留日期、最大缩放、波长和像面。 |
| 其它多子图 | 使用各自的自动铺满或固定矩阵契约；结果区不提供手动“方形子图”开关。 |

波长色由真实波长值选择稳定的工程约定色，不按系列序号循环。显示采用高饱和、高辨识度风格：Fraunhofer F/d/C 线 `486.1 nm`、`587.6 nm`、`656.3 nm` 分别使用亮蓝 `#008CFF`、亮绿 `#00C853`、鲜红 `#FF3430`。d 线绿色是工程曲线约定，真实 `587.6 nm` 单色光外观偏黄，二者不得混为一谈。不可见的紫外/红外使用蓝灰中性色；视场、结构、频率等离散类别仍使用主题分类色；渐晕红是状态色，不属于波长映射。

上述布局主要由 `AnalysisPresentationKind`、`AnalysisPlotOptions.DefaultSquareViewport`、`AnalysisPanel.Plots.cs` 和 `AnalysisPlotControl` 实现。轴标题即使清空，也必须同步清除不再适用的 `AnalysisAxisQuantity/AnalysisAxisUnit`，否则格式化器会重新生成轴文字并挤占小图空间。

2026-08-04 更新：独立“畸变”分析入口已移除；场曲和畸变在同一个组合页面中并列展示。旧 `Distortion`/“畸变”名称只用于会话迁移并自动转到“场曲/畸变”，不再创建第二套页面。干涉图默认使用方形外层视口并保持二维数据等比例；色焦移保持普通曲线图的自适应布局。

### P2：优化面板状态色和编辑行为过重

已于 2026-07-29 修正评价函数表的单一状态色：编辑器现在按 Zemax 操作数类型应用稳定的浅色行色，参考系统中出现的 `TTHI`、`OPLT`、`EFFL`、`PMAG`、`CONS`、`DIVI`、`REAR`、`PETZ`、`MNCA`、`MNEA`、`MNCG`、`MNEG`、`MXCG`、`MXEG` 和 `DMFS` 均有对应色；`BLNK` 保持白色，同族操作数使用一致色系，未知类型使用中性回退色。错误状态优先覆盖为红色。

2026-08-04 修正 ZMX 评价函数漏行：参考镜头的 103 行评价函数现在按 Zemax 原顺序导入，不再只显示约 29 行；63 行 `TRAR` 保留完整参数并进入现有计算路径，九种尚未实现计算的约束/数学操作数按八个 Zemax 参数槽位显示为只读记录。这里的目标是先保证“看见的行、顺序和源参数一致”；只读行不参与 Workbench 评价函数数值，不能据此外观宣称与 Zemax 优化器等价。

选中行不再使用全局强调色覆盖整行，而是保留类型底色、恢复深色文字并使用强调色边框，因此颜色语义在键盘或鼠标选择后仍然可见。实现见 `src/OptilandWorkbench.App/Panels/MeritOperandRowPalette.cs` 和 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs`。

尚未实现的边界是 ZOS-API `IMFERow.RowColor` 的 `Color1`–`Color16` 自定义覆盖、逐行“无颜色”以及全局 `Color Rows` 偏好开关的持久化；这些能力不得仅凭当前默认色外观标记为完成。

工具栏用两个 `WrapPanel` 承载新增、删除、上移、下移、向导、刷新、运行等动作，见 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs:80` 和 `src/OptilandWorkbench.App/Panels/OptimizationPanel.cs:94`。动作多但层级不明显，用户不容易区分“编辑评价函数”和“运行优化”的主次。

2026-07-29 的 Ribbon 更新在面板之外增加了“手动调整 / 自动优化 / 全局优化”三级入口，覆盖快速聚焦、快速调整、变量滑块、评价函数编辑器、向导、执行优化、批量变量操作、差分进化全局搜索和基于 basin hopping 的锤形搜索。玻璃替换模板当前明确为玻璃目录与评价函数的人工替换工作流，不表示已经实现 Zemax Glass Expert 等价算法。

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

分析图属于与 Zemax 一致性核对的一部分，不应在普通 UI 改版中顺手调整。2026-08-03 已明确要求并完成的点列图、fan 图、瞳孔像差和波长色修正，以“当前分析结果 UI 契约”为准。后续涉及 `AnalysisPanel.Plots.cs`、`AnalysisPlotControl`、波前/PSF/MTF/圈入能量等图形控件时，应先确认：

- 是否改变了图的坐标含义、曲线分组、单位、图例、色标或矩阵排布。
- 是否会影响与 Zemax 截图/结果对比。
- 用户是否明确授权修改图形表现形式，以及文档中的当前契约是否需要同步更新。

如果只是修复入口、设置项、主题 token、布局容器或面板导航，也应避免改变分析图的视觉输出。

## 2026-07-31 异世界 Ribbon 剑形装饰

异世界主题新增独立的 `IsekaiRibbonChrome` 矢量渲染层，在既有 Ribbon 页签背后绘制深色皮革/黑钢底纹、旧金框线、铆钉、剑柄、蓝色宝石与贯穿式剑刃。渲染层只在 `ActualThemeVariant == IsekaiTheme.Variant` 时输出，并设置为不参与命中测试，因此明亮、暗夜主题以及 Ribbon 的鼠标/键盘行为不受影响。

本次不修改 Ribbon 页签和命令的名称、语言、顺序、尺寸、分组或点击逻辑；现有 `TabControl` 仅与装饰层叠放，布局仍由原控件完成。装饰采用代码矢量绘制而非固定分辨率位图，以避免高 DPI 和窗口宽度变化时的拉伸失真。
