# Isekai 绘图材质素材

## 字体与语言

2026-11-04确认本主题强制使用内嵌开源Almendra与English，字体家族和语言不可更改；字号与同家族字形保留调整。常规、粗体、斜体和粗斜体来自上游原文件，OFL随软件分发；原有其他主题偏好不覆盖，中文用户名称和缺失符号保留补字。随后“全部统一字体管理”将菜单/弹层、科学分析图、工程图及PDF/Word/HTML/指南正文接入共享角色策略，本主题正文/代码/工程图的主字体均为Almendra，公式使用内嵌STIX Two Math。科学源值、物理配色、图形几何和既有纸面字号保留。具体资源、授权和实现约定见[字体说明](../../../../../docs/TYPOGRAPHY.md)。

## 现行：中世纪磨毛羊皮图纸

分析图纸居中适应后露出的上下/左右空隙，现由结果容器复用同一`PaperBrush`补齐。取消原`#F4EBDD`纯色底板，不增加边距或缩放，不改图纸源PNG/报告、原生页签、正文/数据及其他主题。见[羊皮补齐验证](../../../../../validation/rechecks/2026-11-04/isekai-paper-gaps/README.md)。

分析两栏已确认使用同纸页眉/手稿页脚：直接复用原纸面材质，以深褐文字和原造型图标承载功能，细笔分隔，底部原生页索引用墨线与点标记当前页；保留原栏端淡印花纹。用户撤回后续灰色/羽毛笔误解改版，两张候选从未被代码/资源清单引用，现已移出产品素材目录至[未采用候选档案](../../../../../artifacts/design-candidates/2026-11-04/isekai-gray-not-applied/README.md)。悬停/禁用/焦点、状态语义及原生深色菜单保持可读；纸张PNG、纸面绘制和导出源图不变。[确认版收尾验收](../../../../../validation/rechecks/2026-11-04/isekai-approved-final/README.md)与[原确认画面](../../../../../validation/rechecks/2026-11-04/isekai-manuscript-bands/README.md)记录当前版本；[上一轮皮革带](../../../../../validation/rechecks/2026-11-04/isekai-folio-rails/README.md)保留历史范围，下列羊皮图纸本体仍有效。

用户先否决网上素材和过于正式的画框，再明确整齐卷边也不足，要有“中世纪磨毛边的图纸”感觉。现行以自有生成的粗纤维羊皮图纸为主：不规则毛边、磨损缺口、半透明散纤维、压痕与旧折痕。恢复此前生成的压纹棕皮，暖象牙/棕/旧金配色保持。金色仅作小配饰；正式雕金画框、珠边和硬铭牌不再运行加载。

| 现行材质 | 来源 | 用途 |
| --- | --- | --- |
| [frayed-vellum.png](frayed-vellum.png) | 内置image_gen；原羊皮纸作配色参考 | 完整粗纤维纸；外部28 DIP取真实透明磨毛边，纸面取中心区域，纤维/折痕保持比例 |
| [crafted-leather.png](crafted-leather.png) | 恢复此前自有生成原PNG | 主程序标题/菜单的压纹棕皮；分析两栏当前改用纸面 |
| [scroll-control-skins.png](scroll-control-skins.png) | 内置image_gen | 保留程序文档页签等四状态底板；分析两栏不再贴皮革/纸牌 |
| [ribbon-small-accents.svg](ribbon-small-accents.svg) | 用户2026-11-04确认的小细节预览，原创矢量 | 主Ribbon右侧真实留白的淡金枝叶/小星盘，以及按原生选中项定位的细角线 |

主程序顶栏采用用户确认的“只加小细节”范围，保留原皮革、布局、文字/图标与配色；全羊皮纸顶部及重排候选未接入。SVG中的枝叶/星盘几何直接缓存绘制，最大130 DIP宽，随实际右端留白等比缩小、贴边定位，留白不足66×18 DIP时省略；选中项角线跟随原生TabItem边界，底部刻痕只使用实际空闲内边距。均为不接收输入的装饰层。

长剑保留原有金属/皮革/宝石色，改为短握柄、对称弯护手、收尖双面剑身；整把剑只绘在测得的菜单与命令间隙内。握柄/护手等比缩放，只有剑身长度随栏宽改变，避免旧固定42 DIP高度导致护手被选中项遮断或进入按钮区。正常主Ribbon不使用画廊独立控件的缺省槽位。当前接入与实际宽窄/高DPI检查见[本轮验证](../../../../../validation/rechecks/2026-11-04/isekai-ribbon-details/README.md)。

完整提示词、输入角色与原生成位置见[frayed-generation.json](frayed-generation.json)、[scroll-generation.json](scroll-generation.json)及[crafter原始生成记录](crafted-generation.json)。其中`scroll-vellum-binding.png`为被用户继续否决的整齐卷边候选，未嵌入；`scroll-control-skins.png`仍作原生底板。精确字节哈希、尺寸和透明数据见[frayed-assets.json](frayed-assets.json)。素材由内置工具生成，无CLI/API回退，原PNG字节与alpha保留。

完整框住真实设置/同步/文本复制与导出/PNG/线宽/适应窗口、设置弹层、结果和底部绘图/数据/文本。实际文字、选择、焦点、关闭叉、键盘与命令不烘焙到素材中。仅Isekai更换材质，其他主题沿用原布局/像素。文档选中页签用深褐文字，避免浅纸上白字。生成素材只画背景，所有曲线/数据仍来自真实C#共享Core。

源图默认自动按各绘图区的实际宽高选择4:3（1200×900）或16:9（1600×900），保留两种手动比例，图签160 DIP、28 DIP外部边距及逻辑字号保持；仅改变横向布局，科学数据和相机不变。纸面背景`UniformToFill`，避免将整页纹理压成图签里的横向木纹；图签文字不改变。完整透明毛边替代规整矩形边线，外部alpha不强制全不透明。原2D/3D内部角饰组合保留；分析小配饰仍按类型元数据选择并避让真实文字。PNG不导出程序操作栏/页签，报告保留所选源图比例与既定纸面图框；宽版在相同纸面宽度下的文字相应较小。[当前真实截图及验证](../../../../../validation/rechecks/2026-11-04/isekai-frayed-vellum/README.md)。

运行嵌入为9张PNG及新增的一份`ribbon-small-accents.svg`：上述3张PNG，加原`inner-botanical.png`、`analysis-accessory-atlas.png`、`analysis-folio-atlas.png`、`book-navigation-ornament.png`、`crafted-paper-corners.png`和`crafted-crest-trims.png`。网络皮革及其授权、正式边框/铭牌、原羊皮纸和较早窄框保留来源，但不嵌入或加载。先前输出目录中可能有历史许可证，不表示现行包使用该材质。

## 历史：网络皮革和正式雕金方案

以下为前一阶段素材记录，阶段性“当前”措辞仅属于当时版本；现行以上节为准。

当前版本按用户 2026-11-04 确认的[统一棕金母版](../../../../../docs/assets/themes/isekai/references/04-approved-gold-master-20261104.png)实现：暖象牙纸、棕色皮革、旧金及深褐墨色共用，不建立绿色、紫色或酒红色子主题，只按分析类型更换小配饰。此前[三张原稿与来源](../../../../../docs/assets/themes/isekai/references/README.md)继续保留，花饰位于纸内、28 DIP 窄边使用圆角包角、星芒与双层细线。

当前连续书封包含真实设置/同步/复制/导出文本/PNG/线宽/适应窗口操作、设置弹层、结果和绘图/数据/文本原生页签。标题/菜单/Ribbon、文档页签使用同套棕皮和雕金材质。后续绘图版式设置支持4:3和16:9，连续书封按所选源图比例适配。文字、命令、关闭叉、箭头、焦点和键盘行为继续来自真实控件，素材不包含这些内容。

本轮先准备完整素材后接入。棕皮采用 **Rob Tuytel / Poly Haven 的 CC0 Brown Leather**；[来源和原字节校验](POLY-HAVEN-NOTICE.txt)、[完整CC0授权](POLY-HAVEN-CC0-1.0.txt)随构建/发布输出。官方原JPEG保持不变，棕色叠层在程序绘制时处理。未调用运行时网络/API。网上另筛选[Kenney Fantasy UI Borders](https://kenney.nl/assets/fantasy-ui-borders)、[Buch Golden UI原版](https://opengameart.org/content/golden-ui)和[Karen Arnold金色角花](https://www.publicdomainpictures.net/en/view-image.php?image=233048&picture=corner-ornament-golden)；未将这些候选混入产品。雕金、花叶及铭牌由内置image_gen按批准母版生成，保留透明PNG原字节；没有使用CLI/API图像生成回退。生成的皮革候选未接入，改用真实扫描皮革。

| 本轮运行材质 | 原始像素 | 绘制用途 |
| --- | --- | --- |
| [brown-leather-cc0.jpg](brown-leather-cc0.jpg) | 1024×1024 JPEG | 原始皮革底纹，320 DIP平铺加棕色叠层 |
| [crafted-gold-binding.png](crafted-gold-binding.png) | 1448×1086 RGBA | 28 DIP金框、分离包角、细珠边及标题/菜单/操作栏边轨 |
| [crafted-paper-corners.png](crafted-paper-corners.png) | 1448×1086 RGBA | 四个完整分析纸内花角，按实际透明边界取图，替代旧半图碎片 |
| [crafted-control-skins.png](crafted-control-skins.png) | 1448×1086 RGBA | 棕皮、金色、象牙纸、悬停棕四种空铭牌，作用于真实控件底板 |
| [crafted-crest-trims.png](crafted-crest-trims.png) | 1448×1086 RGBA | 中央罗盘星芒和花铆钉；图集中另保留分隔饰和藤条供后续使用 |

完整提示词及输入身份见[crafted-generation.json](crafted-generation.json)，尺寸、SHA-256、透明比例、精确源矩形见[crafted-assets.json](crafted-assets.json)。原配饰完整提示词见[accessory-prompts.json](accessory-prompts.json)，元数据见[accessory-assets.json](accessory-assets.json)。更早窄框/内角提示词见[rounded-frame-prompt.json](rounded-frame-prompt.json)、[inner-prompts.json](inner-prompts.json)，纸纹与历史生成见[prompts.json](prompts.json)。程序保持一个固定控件树，主题切换只更换材质和边距，不重建结果页。绘制和解码按UI线程缓存；底板使用ImageBrush源矩形记录，避免Avalonia 12的DrawingGroup不支持DrawBitmap的问题。

| 沿用/保留资源 | 原始像素 | 用途 | SHA-256 |
| --- | --- | --- | --- |
| [rounded-gold-binding.png](rounded-gold-binding.png) | 1448×1086 RGBA | 前一版圆角窄框，保留历史来源；现行外框使用crafted-gold-binding | `d17f4ce78488777400f29734a6c0c07dfea3485feb74afe7bf99d7078c6e615c` |
| [inner-botanical.png](inner-botanical.png) | 1598×984 RGBA | 纸内百合/藤蔓、天体星盘、炼金瓶、晶簇 | `62502bc8626eb9b1070fb1a6c31f6ab6e4e092bfda38cf8d1a2031f1629cd7ae` |
| [vellum.png](vellum.png) | 1448×1086 RGB | 轻色中心纸纹，渲染时降低纹理浓度 | `4a8b61331cb9e1c2a19bd92bc9fdff96b11355c7d1e721fe417d84c704fd5f5b` |
| [analysis-accessory-atlas.png](analysis-accessory-atlas.png) | 1024×1536 RGBA | 2×2 配饰：天球仪、蒸馏器、符文水晶、卷轴羽笔 | `b40c461b0bb8e321ae148abd9ae5e74d1b8c1ad1730e57b513c418324ac3e656` |
| [analysis-folio-atlas.png](analysis-folio-atlas.png) | 1448×1086 RGBA | 2×2 书册：星图、草药、法阵、镜片图册及对应小物件 | `ce632cf8514675fe047adfa7fdf28455159f7a0f27d7891e0a72c505b08be2c5` |
| [book-navigation-ornament.png](book-navigation-ornament.png) | 2172×724 RGBA | 底栏留白的太阳测角盘、花叶、地球仪，无按钮/文字 | `fffa75a9922fe358ef27ea6fd2be953922ed012193acaa8e745da129cd6c4b98` |

配饰由 AnalysisArtwork 使用类型化元数据选择，界面名称/译文不参与判断：

| 分析族 | 小配饰 |
| --- | --- |
| MTF、RMS 等普通曲线 | 星盘、星图书册与羽毛笔 |
| FFT/Huygens PSF、Foucault、EnergyFraction 纵轴的能量结果 | 蒸馏器、草药书册、药瓶与研钵 |
| 波前、光程差、干涉图 | 符文水晶、法阵书册与法杖 |
| 点列、光扇、像差、场曲 | 卷轴羽笔、镜片图册、放大镜与分规 |

图例配饰仅在实际图例下方的留白中绘制；没有这种空位的画面仍可在摘要空位使用对应书册。书册按各行真实文本边界找空白矩形，放不下就省略，不为装饰扩张图例或压缩科学绘图。素材只包含装饰，不包含真实曲线、读数、产品控件或公司标志。科学内容继续由 C# 与共享 Core 绘制，公司标志保留 Assets/Brand/CompanyLogo.svg 原轮廓；解码和画刷按 UI 线程缓存。

纸纹以 0.3 浓度叠在暖象牙底色上。2D/3D 内角花饰保持 0.9，分析为 0.32，图签背景角饰为 0.22；两侧纸内窄边仍为 0.55。源图可选1200×900或1600×900，160 DIP图签和配置逻辑字体保持。1×无损PNG包含28 DIP窄框，分别为1256×956或1656×956，默认配置画布为 1920×1080；程序页签和工具栏不导出。报告保留所选源图比例与既定纸面图框；宽版在相同纸面宽度下的文字相应较小。实时程序中内层源图只绘一次，由外层书封同时包住图像和页签；100% 滚动范围对应原源图。

当前入口为Theming/IsekaiCraftsmanship.cs、IsekaiDrawingMaterial.cs、IsekaiThemeStyles.cs、AnalysisArtwork.cs及Controls/AnalysisBookWorkspace.cs、AnalysisResultTabs.cs。2D/3D内部角饰继续使用原组合；分析花角使用新完整图集。[当前实际截图与验证](../../../../../validation/rechecks/2026-11-04/isekai-crafted-workbench/README.md)记录真实产品行为，[前一版验证](../../../../../validation/rechecks/2026-11-04/isekai-unified-book/README.md)保留历史范围；生成参考图不作为产品完成或光学精度证据。

历史资源 [armillary.png](armillary.png)、[grimoire-frame.png](grimoire-frame.png) 和 [leather-binding.png](leather-binding.png) 保留来源，均不再嵌入或运行加载。此前[圆角框](../../../../../validation/rechecks/2026-11-03/isekai-rounded-border/README.md)、[图内花饰](../../../../../validation/rechecks/2026-11-03/isekai-inner-ornament/README.md)及[宽花框阶段](../../../../../validation/rechecks/2026-11-03/isekai-rich-frame/README.md)保留当时的验证范围。
