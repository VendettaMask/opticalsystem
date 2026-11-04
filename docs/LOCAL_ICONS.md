# 本地矢量图标库

全项目设计与评审方法见[UI规范](UI_DESIGN_SPEC.md#全项目设计语言与工作方法)，功能造型仍以[图标语义规范](ICON_SEMANTIC_DESIGN.md)为准。光域后续纯投影HUD目标与当前已交付1.3材质分开记录；本轮方法同步没有重画图形或重新导出Word/网页、图库、SVG包，不改变资源版本及历史验证结论。

2026-11-03 起，全程序功能图标统一使用 **Optical Scientific Outline 1.3**：32×32 源网格、1.65 线宽、圆端点和圆连接，以及 `structure`、`signal`、`secondary` 三种信息角色。普通、黑洞及像素保持基础图形；光域采用科技HUD，异世界属于剑与魔法，使用魔导书、卷轴、法阵与炼金器具，各有221款独立主题SVG。工具栏、菜单、页面操作、命令搜索和内置实验室共用原生矢量渲染；主题切换时按当前主题重新取色，主题图形按独立资源身份缓存，几何按绘制线程缓存。

实际 332 处入口及切换状态现在使用 221 款语义图形：196 款依据名称及对象/动作新绘，25 款保留已确认的公差 6、帮助 4、窗口管理 6 和分析分类 9 原稿。同义操作共享图形，不同分析和对象拆分专用设计。另保留 1,748 个固定 `lucide-static 1.25.0` 规范化通用 SVG 为备用，合计 1,969 个资源名；其 ISC 来源保持，不冒称本轮原创。运行时无需图标字体、JavaScript、额外 SVG 包或网络。

本轮交付为[按功能语义重绘的完整图库](assets/icons/index.html)、[HUD与魔法主题SVG包](assets/icons/scientific-outline-1.3.zip)和[完整黑洞主题总图](assets/icons/overview/semantic-icons-Dark.svg)：221 款、五主题 1,105 份透明 SVG，附每项设计含义及全部功能对应表。7 个视角方向也已由对应新 SVG 绘制，保留原方向命令与强调状态。干涉图、几何图像分析、帮助的旧缺失名称均已更正为专用图形。系统/控件模板符号及品牌标志不计作业务 SVG。前轮 142 种图形清点（重绘前本机基线）是重绘前的引用基线，已由本套替代。

九项 MTF 已分别采用 `mtf-{fourier|huygens|geometric}-{frequency|focus|field}`。下降响应表示频率传递，穿焦峰表示离焦扫描，场点扇与场边响应表示随视场；频谱采样、次波圆弧和几何光线区分方法。不得再用普通折线图或扫描框共用全部入口。详见[语义设计规范](ICON_SEMANTIC_DESIGN.md)和[本轮验证](../validation/rechecks/2026-11-03/semantic-icon-redraw/README.md)。

仓库文件：

- `Assets/Icons/lucide-icon-nodes.json`：完整节点目录；
- `Assets/Icons/lucide-package.json`：固定上游元数据；
- `Assets/Icons/LUCIDE-LICENSE.txt`：ISC 许可和 Feather 署名；
- `Assets/Icons/Scientific/Approved/*.svg`：25 个确认的原创稿；
- `Assets/Icons/Scientific/Designed/*.svg`：196 个按实际功能新绘的专用稿；
- `Assets/Icons/Scientific/Designed/manifest.json`：实际 221 款及 72 个分析命令的设计元数据；
- `Assets/Icons/Scientific/Themes/{Lumen,Isekai}/*.svg`：各221款独立主题造型；
- `Assets/Icons/Scientific/Themes/manifest.json`：批准试稿、语法及材质；
- `Assets/Icons/Scientific/Glyphs/*.svg`：1,748 个规范化备用图标；
- `Assets/Icons/Scientific/catalog.json`：版本和来源元数据；
- `Controls/ScientificIconLibrary.cs`：有限内嵌目录、按需 SVG 加载、原生几何缓存；
- `Controls/LocalIcon.cs`：通用图标控件、禁用和强调前景适配；
- `Theming/ScientificIconPalette.cs` 与 `ThemeRegistry`：每个主题的角色配色；
- `scripts/build-scientific-icons.py`：离线、确定性的通用 SVG 制作脚本，不参与产品运行。
- `scripts/semantic_icon_designs.py`、`build-semantic-icons.py`、`export-semantic-icons.py`：本轮专用路径、绑定计划及图库导出。

熟悉的工具栏命令可使用纯图标，但必须提供 Tooltip：

```csharp
var resetButton = new Button
{
    Content = new LocalIcon { IconName = "rotate-ccw", Width = 18, Height = 18 }
};
ToolTip.SetTip(resetButton, "重置视图");
```

需要可见文字时使用：

```csharp
var saveButton = new Button
{
    Content = new LocalIconLabel("save", "保存")
};
```

新增图标名前先调用 `LocalIconLibrary.Contains`。该入口识别专用语义名和备用目录；`LocalIconLibrary.Names` 保留原始 Lucide 目录，完整可解析资源目录使用 `ScientificIconLibrary.Names`，实际使用集以专用 manifest 和图库为准。未知名称显示同风格的 `circle-question-mark`，不会回退到旧像素图。业务代码不得按主题替换 `IconName`，也不得直接读取某个主题包。

## 主题配色

五个具体主题均注册 `ScientificOutline:<主题身份>` 图标包，画刷由各自 `IconPalette` 提供。普通、黑洞及像素共享基础几何；光域和异世界分别解析独立主题 SVG，不能仅换色。跟随系统解析为实际 Light 或 Dark。图标不改变文字、Chrome、主题身份或用户已选设置。

| 主题 | 结构 | 主信息 | 辅助 |
| --- | --- | --- | --- |
| 普通 Light | #8298A8 | #398FF0 | #28ADAF |
| 黑洞暗夜 Dark | TextMuted #A5978B | Accent #F0AE79 | PlotZeroLine #D5B89C |
| 光域 Lumen HUD | 钢色渐变 / #6C8D9D | #78DCFF | #C4F5FF，参考点金色 |
| 异世界 Isekai | #A18B5F | #EACC8B | #A6B48B |
| 像素 Pixel | TextSecondary | TextAccent | TextSuccess |

源图标为透明背景，不烘焙下拉箭头、控件选中背景或标签。禁用图标使用主题禁用色；白色或主题强调按钮前景、错误/警告/成功前景仍遵循宿主的可读性和状态语义。三维视角立方体沿用实际面的含义，轮廓和选中面按同一主题角色取色。光学曲线的波长、系列、材料标记和物面/光阑/像面角色色不属于功能图标配色。

背景对比度判断包含实际祖先控件、边框和面板。对不透明的Pad线性渐变，将图标中心转换到该背景坐标中取色，避免浅蓝窗口渐变被误判成普通白底；对比不足时仍沿用宿主可读前景回退，不改变SVG几何或其他主题图标包。

## 确认的专用语义与分析分类

公差编辑/查看分别使用表格加铅笔/眼睛；公差分析表示限值和响应；直方图使用相邻分箱；良率使用合格占比；报告使用文档和统计摘要。用户指南、操作数帮助、恢复未保存工程、关于分别采用手册、分类查询、历史恢复和信息符号。窗口管理使用确认的布局、分析克隆、更新锁定、多页关闭、默认布局保存和恢复语义。不要将这些专用命令名全局替换为有其他含义的通用名称。

2026-11-02 确认的九个分析分类图标在普通、黑洞及像素中保留基础几何，原始来源位于 [Assets/Icons/Analysis](../src/OpticalSystemDesign.App/Assets/Icons/Analysis/README.md)，活动目录中有同源的 `analysis-*` 矢量。光域和异世界使用对应的独立主题图稿；全部主题保持原显示尺寸、分类含义和菜单动作。Dark 使用已确认的黑洞暖色。

九类依次为光线迹点、报告、像差分析、波前、点扩散函数、MTF曲线、RMS、圈入能量和扩展图像分析；MTF为下降曲线，RMS为U形曲线，PSF为峰与衍射环。入口仍是原生下拉菜单，图标在左、文字在右，只有一个箭头；报告、波前、圈入能量后设置细分隔线。非序列模式、下拉项及其他工具栏同样使用新矢量包。

SVG 随标准 App 内嵌发布。解析器保留角色、组透明度和虚线，原生几何延迟到绘制环境初始化后建立并缓存，避免命令目录查询依赖图形平台。实际五主题、运行时切换、禁用状态及工作台验证见[本轮接入记录](../validation/rechecks/2026-11-03/theme-scientific-icons/README.md)。原九类接入的历史范围见[2026-11-02 记录](../validation/rechecks/2026-11-02/analysis-toolbar-svg/README.md)。

原主题1989组图标/尺寸像素基线保持；独立主题及完整图库验证见[主题身份接入](../validation/rechecks/2026-11-03/icon-theme-identity/README.md)。旧1.2图包保留当时资源，持续更新的图库以当前版本为准。

## 制作和来源保留

新增或改图须遵循[语义与主题造型规范](ICON_SEMANTIC_DESIGN.md#后续改图与文档同步)，同步五主题资源、完整图库与图包、原 Word/网页规范以及当前记录。光域的科技 HUD 和异世界的剑与魔法为长期设计要求，古典炼金器具属于异世界语汇。

新功能专用稿在 `semantic_icon_designs.py` 中定义并输出到 `Scientific/Designed`，写明实际含义、保持角色类名及 32×32 坐标；`Approved` 保留已有确认稿，不把新稿冒称已确认。新增图元必须同步原生解析验证。备用通用稿使用 `python scripts/build-scientific-icons.py` 从固定节点目录重建；脚本正确处理 SVG 弧线的紧凑标志位，统一坐标和角色，不访问网络，不修改专用路径。

以下 Game-icons 和 Farm-Fresh 文件保留原来源、许可与历史验证，已不再注册为活动功能图标包；不能将它们的署名转移到新原创稿。

`Assets/Icons/game-icons-isekai.json` 记录每个语义的上游作者目录、SVG 文件、路径数据及固定提交号；`scripts/import-game-icons.ps1` 可从官方仓库重新导入。资源按 CC BY 3.0 分发，完整署名和许可位于 `Assets/Icons/GAME-ICONS-LICENSE.txt`。

2026-08-30 语义复核后，网格、列表树、画中画分别使用 `divided-square.svg`、`checkbox-tree.svg`、`window.svg`；搜索、放大、缩小分别使用 `magnifying-glass.svg`、`expand.svg`、`contract.svg`；新建文件与文字输入分别使用 `scroll-quill.svg`、`quill-ink.svg`；包搜索与扫描搜索分别使用 `archive-research.svg`、`radar-sweep.svg`。这些易混淆操作必须保持不同上游来源，相关契约测试同时验证来源和实际渲染。

更新上游目录必须使用明确固定版本，保留许可和来源元数据，并执行针对目录、实际绘制与运行时主题切换的检查。Farm-Fresh 3.92 的历史资源按 CC BY 3.0 US 分发，许可与署名副本为 `FARM-FRESH-ICONS-LICENSE.txt`；禁止使用浮动版本或运行时联网获取图标。
