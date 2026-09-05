# 打包镜头库

## 运行时模型

桌面应用不下载、同步、解压或转换镜头文件。发布前生成只读原生库：

```text
LensLibrary/
  index.json
  projects/*.staropt
  StockCatalogs/
    Daheng Optics.json
    Edmund Optics.json
    Newport.json
    Sigma Koki.json
    Thorlabs.json
```

应用启动时读取版本 2 的 `index.json`，参数和二维预览只打开 `.staropt`。镜头库页面位于“数据库 > 镜头库”，与材料库分离。选中条目只更新预览，不改变当前设计；双击条目打开打包工程并激活镜头编辑器。搜索同时覆盖镜头名、来源、镜头类型、应用场景和设计单位。页面在有限工作区内让镜头列表和元数据各自滚动，并为光学预览保留有限画布；925 条目录不会再把右侧详情推到列表末尾，窄 Dock 下列表和详情按当前视口上下分配空间。

实像高系统若无法生成预览光线，仍显示镜头几何，不因光线预览失败而放弃整个条目。

## 库存镜头查看与匹配

“数据库 > 库存镜头查看”是独立于设计镜头库的厂商元件目录。当前产品范围明确限制为五家：Thorlabs（索雷博）、Edmund Optics（爱特蒙特）、Daheng Optics（大恒光电）、Newport 和 Sigma Koki；其他本机 ZMF 厂商文件不读取、不显示，也不参与匹配。查看器在后台读取库存 JSON，应用服务会缓存已过滤排序的库存目录，首次打开不会在 UI 线程同步解析 16 MB 目录；筛选结果完整计数，但表格先绑定前 500 个可见行，避免一次性渲染 16,289 条记录造成停顿。查看器可按厂商、有效焦距、入瞳直径 EPD、形状代码、曲面代码、元件数以及料号/名称筛选，并显示厂商页面、EFL、EPD、元件数和目录分类。形状使用 Zemax 的 `? / E / B / P / M`，曲面使用 `S / G / A / T`。厂商、形状、曲面、元件数和范围开关在改变时立即筛选；搜索词与范围数值采用 220 ms 延迟刷新，“搜索”用于立即执行当前条件，“重置”一次性清除全部条件。系统浏览器或本地模型载入失败时，错误显示在页面状态文本中，不抛出为 UI 未处理异常。

“数据库 > 库存镜头匹配”参考 [Ansys Zemax Stock Lens Matching](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Stock_Lens_Matching.html) 的候选筛选思路，但使用当前软件实际拥有的数据实现：目标值来自当前系统的一阶有效焦距和入瞳直径；用户选择五家厂商、结果数、EFL 公差、EPD 公差和光焦度方向约束；候选先经过条件过滤，再按归一化 EFL/EPD 综合偏差排序。页面打开时只刷新目标参数，用户点击“开始匹配”后才在后台读取目录和执行匹配扫描；用户切换系统或重新开始匹配时，旧结果会被取消/丢弃，不会覆盖新状态。当前目标范围是“所有面（当前系统）”，完整系统没有唯一的单镜片形状，因此形状匹配不会被伪造为已生效。

2026-09-01 起，桌面运行时不再发现或解析当前用户的 `Documents/Zemax/Stockcat`。五家厂商共 16,289 条目录头元数据已转换为版本 1 的应用自有明文 JSON，并在 `StockCatalogs` 下按厂商名字分别保存，随应用和源码版本同步；任意电脑运行同一版本都会看到同一库存目录，不需要安装 Zemax。目录只保存料号、元件数、形状、表面类别、EFL、EPD、厂商主页和来源说明，不包含 ZMF 处方正文、价格或实时库存。

原有 6 条经厂商官方页面核验的记录已在转换时按厂商和料号合并，保留更丰富的名称、产品页和规格字段。所有库存条目默认不能“载入模型”；只有取得明确许可、转换为 STAROPT 并通过校验的模型才允许载入，不能依据目录规格反推并冒充厂商处方。

ZMF 解析仅保留在离线维护工具 `OptilandWorkbench.LensLibraryBuilder` 中。维护人员显式提供来源目录并更新同步资源：

```powershell
dotnet run --project tools\OptilandWorkbench.LensLibraryBuilder -- `
  --stock-catalog <ZMF来源目录> `
  src\OptilandWorkbench.App\Assets\LensLibrary\StockCatalogs
```

转换器要求五家源目录齐全，限制文件和记录大小，跳过处方正文，并分别原子替换五个厂商 JSON 文件。转换得到的数据仍继承其来源授权；改成自有 JSON 格式不代表获得重新分发许可，发布前必须审核来源许可。

匹配页返回的是真实目录候选和可复算的偏差，不是自动替换。由于当前合法可读的 ZMF 目录头不包含可授权的光学处方、中心厚度和空气补偿数据，本阶段不执行镜片替换、空气厚度补偿、组合保存或再优化，也不提供看似可点击但没有真实作用的控件。只有将来取得明确许可的本地处方模型后，这些能力才可以接入。

## 离线构建

维护工具 `tools/OptilandWorkbench.LensLibraryBuilder`：

1. 在临时目录安全解压 ZIP；
2. 扫描本地 ZMX；
3. 使用 Workbench 玻璃数据库解析材料；
4. 导入全部支持的配置；
5. 写入带校验的 `.staropt` 和带来源审计信息的索引。

```bash
dotnet run \
  --project tools/OptilandWorkbench.LensLibraryBuilder \
  -- \
  tools/lens-library-public-sources.json \
  src/OptilandWorkbench.App/Assets/LensLibrary
```

当前打包库共 925 项：56 个独立显微物镜、5 个工业示例和 864 个可转换公开 Zemax 设计。显微类别只允许独立物镜；筒镜、聚光镜、Fourier 成像链和完整显微系统排除。

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
dotnet run --project tools\OptilandWorkbench.LensLibraryBuilder -- `
  --reindex src\OptilandWorkbench.App\Assets\LensLibrary
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

2026-08-03 使用最新导入代码重新处理失败项后，1,050 个 ZMX 清单项中已有 864 个成功、180 个保留明确失败报告、6 个重复内容跳过。失败条目仍保留在下载语料中，不会静默近似。

2026-08-04 的评价函数修正不会追溯重写全部 925 个打包 STAROPT。当前定向夹具 `[MS-L7](10X大NA大视场).ZMX` 的 103 行评价函数已按源顺序导入；124 个 Zemax 顺序操作数已有定义级可执行路径，覆盖 `TRAR`、`TTHI/TGTH`、`REAR/RANG`、基础数学与行约束（含 `DIVB/PROB/OSUM/QSUM/EQUA` 定义级语义）、常见厚度/边厚/曲率/圆锥/半口径、`WLEN/INDX`、`MNIN/MXIN/MNAB/MXAB`、`POWR`、若干一阶量以及 `CTGT`、`PMAG`、`PETZ`、`MXEG` 和 `GOTO/ENDX/OOFF/SKIN/SKIS/USYM`。注册表按 2026 R1 实测扩展到 383 个顺序兼容代码；`DIMX` 等尚未完整实现参数语义的操作数禁用只读保留。新增执行路径仍需 Zemax/ZOS-API golden 对照后才可称为完整兼容。需要让既有库条目获得该导入结果时，应使用同一来源 ZMX 重新运行离线转换并审核索引差异。

2026-08-16 使用当前代码在隔离暂存库中重试了全部 1,050 个公开 ZMX：798 个转换成功、246 个失败、6 个重复跳过。与 2026-08-03 基线相比没有新增成功项，反而有 66 个旧成功项被当前更严格的实像高求解、光线瞄准或快照引用校验拒绝；正式库没有用失败结果覆盖它们，而是保留此前已验证的 STAROPT。5 个工业样例和当前仍有来源文件的 `[MS-L7]` 已重新转换；其余 55 个书籍配套 ZMX 在本机来源目录缺失，因此同样保留原打包结果。最终库仍为 925 项，其中 804 项由本次代码成功重算，121 项明确保留旧的已验证结果；不能把它描述成 925 项全部由当前导入器生成。

## 2026-09-05 预览失败处理

全部 925 个镜头预览回归通过。类型化的光阑瞄准/实像高瞄准异常在预览服务中降级为无光线几何场景，警告经 SceneDto 传到界面；其他异常仍向上传递。这不代表相应处方的数值分析已经可计算，也不更新历史库的导入来源和生成日期。详见 [项目修复与验证](PROJECT_REPAIR_2026-09-05.md)。
