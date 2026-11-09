# 打包镜头库

当前操作数统计与最新构建/测试范围统一见[当前状态](CURRENT_STATUS.md)。下方各批次计数保留对应历史阶段，不代表最新总数。

## 运行时模型

商业镜头目录保存使用 `.stock-catalog-index` 记录当前有效的厂家 JSON；移除厂家只将旧文件移出有效集合，保留原文件，不再扫描删除目录内其他 JSON。写入前检查全部目标，同名文件不能解析为对应厂家目录时拒绝保存并保留原文件。已有无索引目录仍按旧方式读取厂家 JSON；各文件采用原子写入，整个目录不是一次事务。详见[数据保护修复](PRIORITY_RELIABILITY_REPAIR_2026-10-26.md)。

镜头库原生文件路径检查既拒绝词法越界，也拒绝库内目录联接、符号链接和链接文件，避免索引中的相对路径跳到库外。

桌面应用不下载、同步、解压或转换镜头文件。发布前生成只读原生库：

```text
LensLibrary/
  index.json
  projects/*.staropt
  StockCatalogs/
    Daheng Optics.json
    Edmund Optics.json
    JCOPTIX.json
    Newport.json
    Sigma Koki.json
    Thorlabs.json
```

应用启动时读取版本 2 的 `index.json`，参数和二维预览只打开 `.staropt`。镜头库页面位于“数据库 > 镜头库”，与材料库分离。选中条目只更新预览，不改变当前设计；双击条目打开打包工程并激活镜头编辑器。搜索同时覆盖镜头名、来源、镜头类型、应用场景和设计单位。页面在有限工作区内让镜头列表和元数据各自滚动，并为光学预览保留有限画布；925 条目录不会再把右侧详情推到列表末尾，窄 Dock 下列表和详情按当前视口上下分配空间。

实像高系统若无法生成预览光线，仍显示镜头几何，不因光线预览失败而放弃整个条目。

MiniVolt 收集臂与激发臂的 ZMX 引用 `WATER_AK.ZTG`。公开数据提供处方及设置文件，材料表需另行取得。镜头库与同名示例使用 20 °C、998.2067 kg/m³ 的普通液态水替代模型，按 IAPWS R9-97 式 (1) 生成 210–1100 nm 折射率表，并在处方中记录来源。收集臂在 590 nm 取 1.33332727，激发臂在 532 nm 取 1.33538038。表中仅包含实折射率。二维图将水浸层填为青绿色。[MiniVolt 原始数据](https://zenodo.org/records/17187705) · [IAPWS R9-97](https://iapws.org/technical-guidance/release/Rindex)

Dan Reiley 的 14 个旧定焦工程已将有歧义的 H-F1、H-ZF3、H-QK3L、H-LAK52、H-LAF50B 和 H-ZLAF66 固定为 CDGM 2026-09 同牌号的色散数据。变焦工程 US07492524-1 的三个配置用 CDGM H-ZLAF55D 替代现行库中缺失的 H-ZLAF55A，两者玻璃代码为 835427。替代后的项目和同名示例均保存完整材料模型，镜头库索引从更新后的活动配置计算。原始 ZMX 文件列表位于 Dan Reiley 的[定焦目录](https://sites.google.com/site/danreiley/photographic-lenses-prime)和[变焦目录](https://sites.google.com/site/danreiley/photographic-lenses-zoom)。历史 H-ZLAF55A 玻璃代码见 [Edmund Optics 对照表](https://www.edmundoptics.com/ViewDocument/edmund-optics-glass-types.pdf)。

## 库存镜头查看与匹配

“数据库 > 库存镜头查看”是独立于设计镜头库的厂商元件目录。目录收录 Thorlabs（索雷博）、Edmund Optics（爱特蒙特）、Daheng Optics（大恒光电）、Newport、Sigma Koki 和 JCOPTIX（晶萃光学）。查看器在后台读取厂商目录和已安装的单文件模型库。应用服务会缓存已过滤排序的目录，表格先绑定前 500 个可见行，并显示完整的筛选结果计数。查看器可按厂商、有效焦距、入瞳直径 EPD、形状代码、曲面代码、元件数以及料号/名称筛选，并显示厂商页面、EFL、EPD、元件数和目录分类。形状代码使用 `? / E / B / P / M`，曲面使用 `S / G / A / T`。厂商、形状、曲面、元件数和范围开关在改变时立即筛选；搜索词与范围数值采用 220 ms 延迟刷新，“搜索”用于立即执行当前条件，“重置”一次性清除全部条件。系统浏览器或本地模型载入失败时，错误显示在页面状态文本中，不抛出为 UI 未处理异常。

“数据库 > 库存镜头匹配”使用当前系统的一阶有效焦距和入瞳直径作为目标值。用户选择参与匹配的厂商、结果数、EFL 公差、EPD 公差和光焦度方向约束；候选先经过条件过滤，再按归一化 EFL/EPD 综合偏差排序。页面打开时只刷新目标参数，用户点击“开始匹配”后才在后台读取目录和执行匹配扫描；用户切换系统或重新开始匹配时，旧结果会被取消/丢弃，不会覆盖新状态。当前目标范围是“所有面（当前系统）”，完整系统没有唯一的单镜片形状，因此形状匹配不会被伪造为已生效。

2026-09-01 起，库存目录随应用发布。五家厂商共 16,289 条目录头元数据已转换为版本 1 的应用自有明文 JSON，并在 `StockCatalogs` 下按厂商名字分别保存，随应用和源码版本同步；任意电脑运行同一版本都会看到同一库存目录。目录只保存料号、元件数、形状、表面类别、EFL、EPD、厂商主页和来源说明，不包含 ZMF 处方正文、价格或实时库存。

原有 6 条经厂商官方页面核验的记录已在转换时按厂商和料号合并，保留更丰富的名称、产品页和规格字段。应用支持载入从厂商原始处方转换并校验的 STAROPT。随软件分发处方须取得明确许可。

ZMF 解析仅保留在离线维护工具 `OpticalSystemDesign.LensLibraryBuilder` 中。维护人员显式提供来源目录并更新同步资源：

```powershell
dotnet run --project tools\OpticalSystemDesign.LensLibraryBuilder -- `
  --stock-catalog <ZMF来源目录> `
  src\OpticalSystemDesign.App\Assets\LensLibrary\StockCatalogs
```

转换器要求五家源目录齐全，限制文件和记录大小，跳过处方正文，并分别原子替换五个厂商 JSON 文件。转换得到的数据仍继承其来源授权；改成自有 JSON 格式不代表获得重新分发许可，发布前必须审核来源许可。

匹配页返回的是真实目录候选和可复算的偏差，不是自动替换。库存查看的“载入模型”会打开独立设计。

### 晶萃光学的本机模型

首批目录包含 7 个晶萃料号，共 6 种光学处方。OLC140116M 与 OLC140116 的官网 ZMX 内容相同，两个料号分别对应套筒安装版与裸片。焦距和镜片外径取自商品规格，入瞳直径取自原厂 ZMX 的 ENPD。非球面 OLS140123-T3M 的原厂 ZMX 使用 THORB 膜系标记，已核对几何处方和一阶参数，膜系光谱尚未验证。 [未镀膜平凸系列](https://www.jcoptix.com/product/161/776#OLC140116M) · [T3 非球面系列](https://www.jcoptix.com/product/198/663#OLS140123-T3M)

模型离线转换和重读校验后，安装到 `%LOCALAPPDATA%\OpticalSystemDesign\StockLensModels\JCOPTIX`。文件名见 `StockCatalogs/JCOPTIX.json` 的 `LocalModelPath`。启动新版应用后，从“数据库 > 库存镜头查看”选择 JCOPTIX 和目标料号，再点“载入模型”。换电脑时，可从各料号的厂商页面下载 ZMX，用下文的单文件转换工具指定临时示例库及镜头库，再把校验后的 STAROPT 按目录声明的名称放到本机模型目录。安装包收录料号、规格和官网链接。原厂 ZMX 与转换模型保存在本机，厂商的再分发许可尚未确认。

### Thorlabs AC127 消色差双胶合镜头

`AC127-050` 和 `AC127-075` 的 A、B、C 三种镀膜及各自的 `-ML` 镜座版共 12 个料号，从 Thorlabs 产品页按料号取得原厂光学处方。`AC127-050-C-ML` 使用原厂 `19628-S02.zar` 内的 ZMX，其余料号使用各自产品页上的 ZMX。`THORLABS.ZMF` 目录记录提供 EFL 和 EPD，原厂 ZMX 提供曲率、厚度、玻璃、孔径和设计波长。原厂膜系定义保留在原始来源中，STAROPT 模型用于几何光线追迹。[AC127-075-B 产品页](https://www.thorlabs.com/item/AC127-075-B) · [原厂 ZMX](https://thin01mstroc282prod.dxcloud.episerver.net/globalassets/items/a/ac/ac1/ac127-075-b/7401-s03.zmx?v=0116101807)

首批安装的 12 个料号现已并入下方的单文件库存库。目录条目的数据来源记录了对应的厂商原始下载地址。

### Thorlabs 单文件库存库

`Thorlabs.stocklens` 包含 Thorlabs 2026 年 5 月版 `THORLABS_MAY_2026.ZMF` 中全部 4,217 份 STAROPT 处方，以及 6 份从原厂产品页取得的历史镜座版处方。其余 847 个旧料号保留目录记录。库存查看器从库中按需读取所选处方，将它作为独立文档打开。编辑后可另存工程。原厂膜系文件随厂商 ZIP 提供，库内模型使用几何光线追迹。[Thorlabs 完整 Zemax 目录](https://www.thorlabs.com/software-pages/zemax)

应用可从系统资源目录 `LensLibrary/StockCatalogs` 或本机 `%LOCALAPPDATA%\OpticalSystemDesign\StockLensModels` 读取 `Thorlabs.stocklens`。本机文件优先。原先的 12 个 AC127 料号也在单文件中。维护时用下列命令转换官方 ZIP 解出的 ZMF，输出文件名与厂商名一致。[原厂 ZIP](https://thin01mstroc282prod.dxcloud.episerver.net/contentassets/0ee24bcce2224cc6ae338b2bdaa9e212/zemaxcatalog.zip?v=0511073732)

```powershell
dotnet run --project tools\OpticalSystemDesign.LensLibraryBuilder -- `
  --stock-model-pack <THORLABS_MAY_2026.ZMF 路径> `
  src\OpticalSystemDesign.App\Assets\LensLibrary <输出目录>\Thorlabs.stocklens
```

末尾可另传已有本机模型的根目录，将目录条目声明的历史料号模型一并归档。

本次原厂 ZIP 的两次下载内容相同，ZIP 校验通过。实测 ZIP SHA-256 为 `a7c0cfe71195a7a9dc11753040d92f75153642e83186ba674ace76d93a25897f`，与网页资源记录中的 SHA-256 不同。内部 `THORLABS_MAY_2026.ZMF` 的 SHA-256 为 `64417a32ff0d20a7f43134cac1275a9c951c0d6476af8a9651a712ca6e68137b`。梯度折射率处方可打开，二维布局的 GRIN 曲线路径仍待接入。

## 库存页面的当前交互与窄窗口行为

库存查看的“搜索”和库存匹配的“开始匹配”是蓝色主动作；重置、厂商页面、数据表和模型载入保持次级。禁用的范围框说明须先勾选筛选项，外链/模型按钮说明等待目录、未选条目或缺少有效地址/模型等实际条件；完整系统形状匹配仍禁用并说明原因。

匹配结果区的说明和“厂商页面”按钮按自然高度测量，表格使用剩余高度并局部裁剪，关闭重开和 MDI 平铺不再向上覆盖表头。顶部参数只在短窗口中滚动，不改变原面板划分。EFL/EPD 的 mm、偏差的 % 独立占位，长标题省略时有完整提示。详见 [容器修复](PANEL_CONTENT_LAYOUT_2026-09-27.md) 与 [主动作验证](PRIMARY_ACTIONS_AND_COMPACT_SIDEBAR_2026-09-27.md)。

## 离线构建

维护工具 `tools/OpticalSystemDesign.LensLibraryBuilder`：

1. 在临时目录安全解压 ZIP；
2. 扫描本地 ZMX；
3. 使用 Workbench 玻璃数据库解析材料；
4. 导入全部支持的配置；
5. 写入带校验的 `.staropt` 和带来源审计信息的索引。

```bash
dotnet run \
  --project tools/OpticalSystemDesign.LensLibraryBuilder \
  -- \
  tools/lens-library-public-sources.json \
  src/OpticalSystemDesign.App/Assets/LensLibrary
```

当前打包库共 925 项：56 个独立显微物镜、5 个工业示例和 864 个可转换公开 ZMX 设计。显微类别只允许独立物镜；筒镜、聚光镜、Fourier 成像链和完整显微系统排除。

## 索引元数据

每个镜头条目包含以下可检索或可显示字段：

- 光学规格：有效焦距、F/#、NA、最大视场及其定义、工作距离及其口径、波长范围；
- 结构规格：镜片数、表面数、系统总长、最大清口径；
- 分类信息：镜头类型、应用场景、设计单位；
- 来源审计：来源名称、来源地址、许可证、源文件名、导入时间和导入器程序集版本。

计算口径必须保留在索引中，不能只保存一个无解释数值：

- 源文件以 NA 定义系统孔径时，记录“物方定义”；其他系统只有在 F/# 有效时，按空气中 `sin(atan(1 / (2 × F/#)))` 记录“像方空气近轴估算”，不能冒充源文件给出的物方 NA；
- 有限物距系统优先记录物面到首个实体光学面的“物方工作距离”；无限物距系统记录最后实体光学面到像面的“像方后工作距离”；无法可靠判定时记录“未提供”；
- 镜片数按主波长下折射率大于 1.0001 的连续实体材料段计数，胶合件中的不同玻璃分别计数；纯反射或特殊模型允许为 0；
- 最大清口径为物面和像面之间所有顺序表面的最大半口径的两倍，不代表镜筒机械外径；
- 系统总长沿用顺序模型的 `TotalTrack`，不把它改写成机械筒长。

版本 1 的历史索引没有逐条导入时刻和导入器版本。迁移时这些字段明确记录为“历史条目未记录/历史版本（未记录）”，不得用文件修改时间或迁移时间伪造原始导入时间。今后的单文件导入和全量构建会写入真实导入时间及导入器程序集版本。

## 来源与 Git 策略

下载和展开的数据位于忽略目录：

```text
local-data/lens-library/originals/user-zmx/public/
```

仓库内测试样例位于相邻 `user-zmx/project/`。只有审核后的 `index.json`、`.staropt` 与 `StockCatalogs/<厂商>.json` 随应用打包。来源许可必须记录；不支持的结构离线构建时明确失败，不能近似替代。

## 单文件转换与安装

Windows 可把 `.zmx` 拖到 `Convert-Zemax-Lens.cmd`，或执行：

```powershell
.\Convert-Zemax-Lens.cmd "D:\lenses\example.zmx"
```

工具会导入支持配置、写入并重读 STAROPT 校验、发布到 `samples/lenses` 和打包镜头库，并按稳定 ID 更新索引。相同来源重新导入会更新条目，不创建重复项。全部输出先暂存；发布时在目标目录的同一父目录准备完整替换副本，保留 `StockCatalogs` 等非生成器管理内容，再把旧库改名为事务备份并激活新库。新库激活失败会立即恢复旧目录，故障测试覆盖“旧库已经移入备份、新库尚未激活”的窗口；只有回滚本身也遭遇文件系统故障时才保留显式备份路径供人工恢复。

可通过 `--name`、`--category`、`--source-name`、`--source-url`、`--license`、`--lens-type`、`--application` 和 `--design-organization` 提供元数据；未给出类型和用途时按分类写入保守值，设计单位未知时写入“未注明”。运行 `--help` 查看完整参数。

已有 STAROPT 库升级索引或重新计算元数据时，可执行：

```powershell
dotnet run --project tools\OpticalSystemDesign.LensLibraryBuilder -- `
  --reindex src\OpticalSystemDesign.App\Assets\LensLibrary
```

重建过程先完整读取所有原生工程，全部成功后才原子替换 `index.json`，不会修改 `.staropt`。该操作是重新提取元数据，不等同于重新导入源 ZMX，因此不会填写历史条目缺失的导入时间。

## 公开语料同步

`tools/Sync-Public-ZemaxCorpus.ps1` 从 Figshare、Zenodo 和已知 Mendeley 数据集同步声明开放许可的真实 ZMX，并记录来源、MD5/SHA-256 和许可。

`tools/Sync-DanReileyLensExchange.ps1` 镜像 Dan Reiley Lens Design Exchange 的公开目录，并记录文件 ID、原名、哈希、重复关系和失败。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-Public-ZemaxCorpus.ps1

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Sync-DanReileyLensExchange.ps1
```

批量转换：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Import-Public-ZemaxCorpus.ps1
```

正式库全量更新时，应先用 `lens-library-release.json` 在暂存目录生成项目/工业基础库，再使用最新导入器和 `-RetrySuccessful` 重算全部公开清单。构建器支持系统临时目录与输出目录位于不同 Windows 卷。脚本提供 `-ExamplesDirectory`、`-LibraryDirectory` 和 `-ConversionReportPath`，可把转换结果完全隔离到暂存目录；完成索引、项目数量和 STAROPT 重读校验后再替换正式库，避免长时间批处理在中断时留下半更新资源。未指定这些参数时仍使用原有正式目录。

2026-08-03 使用最新导入代码重新处理失败项后，1,050 个 ZMX 清单项中已有 864 个成功、184 个保留明确失败报告、6 个重复内容跳过。失败条目仍保留在下载语料中，不会静默近似。

2026-08-04 的导入修正不会追溯重写全部历史打包 STAROPT；下列镜头数量和转换成绩只属于相应日期。操作数的当前可执行数量、禁用/兼容范围与原生认证缺口统一见 [操作数支持](ZEMAX_OPERAND_SUPPORT.md)，不再在镜头库文档中维护一份会漂移的旧统计。需要使既有条目采用新导入规则时，使用相同来源 ZMX 重新离线转换并审核索引差异，保留原失败和来源。

2026-08-16 使用当前代码在隔离暂存库中重试了全部 1,050 个公开 ZMX：798 个转换成功、246 个失败、6 个重复跳过。与 2026-08-03 基线相比没有新增成功项，反而有 66 个旧成功项被当前更严格的实像高求解、光线瞄准或快照引用校验拒绝；正式库没有用失败结果覆盖它们，而是保留此前已验证的 STAROPT。5 个工业样例和当前仍有来源文件的 `[MS-L7]` 已重新转换；其余 55 个书籍配套 ZMX 在本机来源目录缺失，因此同样保留原打包结果。最终库仍为 925 项，其中 804 项由本次代码成功重算，121 项明确保留旧的已验证结果；不能把它描述成 925 项全部由当前导入器生成。

## 2026-09-05 预览失败处理

全部 925 个镜头预览回归通过。类型化的光阑瞄准/实像高瞄准异常在预览服务中降级为无光线几何场景，警告经 SceneDto 传到界面；其他异常仍向上传递。这不代表相应处方的数值分析已经可计算，也不更新历史库的导入来源和生成日期。详见 [项目修复与验证](PROJECT_REPAIR_2026-09-05.md)。
