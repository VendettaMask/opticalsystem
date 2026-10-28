# 无焦 PSF 正式比较契约修复（2026-10-28）

基于 `9ee9cd547688a8876287d8420635b1ddd20e2873`，独立分支 `fix/afocal-precision-20261028`。本轮修复比较工具，**没有修改 Core 光学公式或关闭原三项质心差异、六项自动网格不可比**。原始参考、模型、请求及比较门槛保持。没有调用 Zemax、访问用户电脑、运行 AutoLens、全库测试、提交推送或发布。

## 已证实并修复的缺陷

- 正式入口此前整体阻止无焦 PSF/Huygens PSF，已有专项角域契约未接入。正式原生网格解码又将所有非波前网格标为 ImageHeight/Micrometer，无法与 Core 的 IncidentAngle/Milliradian 比较。
- 原生派发直接使用历史字段 `ImageDeltaMicrometers`，但它在无焦 Core 中实际表示 mrad，原生 API 要求模型声明的角单位。例如 0.05 mrad 在 ArcMinute 模型应派发 0.17188733853924698，而不是 0.05；已有固定角捕获的属性读回验证该换算。仅转换派发副本，不改 canonical request 或指纹。
- 接通限定的线性、标量、单视场/波长、最终面、主光线参考契约；核验模型角单位、原生选择器、采样、配置、旋转、归一化和角间距。偏振、全波长、质心参考、负间距请求在此适配器明确拒绝，不代表 Core 不支持这些模式。
- 配置中的零 PSF 间距现在明确允许自动采样。对坐标不一致的无焦 PSF，正式 Compare 保留 Incomparable 和原因；不再仅向上抛出异常，不产生虚假的强度比较度量。原 `ComparisonMetrics.Align` 及其坐标门槛未改。

## 实际验证

源参考是 10 月 27 日归档 `evidence-current.zip`。新夹具仅逐字节复制20个PSF请求的 native data/model/captured-settings，清单记录原成员路径与 SHA-256；没有生成或替换原生数组。测试从原镜头源导入并重算当前 Core，再通过正式 ResultNormalizer 和 Compare。

| 集合 | 结果 | 与原结论关系 |
| --- | --- | --- |
| 原150项内的10个自动PSF | 4 Pass / 6 Incomparable | 全部保持 |
| 独立固定角网格中的10个PSF | 10 Pass | 全部保持；不加入150项通过数 |
| 首批新契约测试 | 23失败 | 修复前的单位标注与派发换算失败，TRX保留 |
| 修复后新契约初验 | 23通过 | 尚未加入所有拒绝条件控制 |
| 无焦相关回归 | 142通过 | 既有104项和当时新增38项 |
| 最终契约回归 | 40通过 | 含新增2项零间距配置控制；重复项不累计 |

比较链路阶段不同测试共 **144通过，零失败/跳过**；后续新增8项独立验证见[独立报告](AFOCAL_INDEPENDENT_PHYSICS_2026-10-28.md)，当前记录去重152项。最初15项基线也保留，但不重复累计。Release构建由上述定点测试完成；没有构建桌面安装包或重跑完整150项。全局策略 SHA-256 仍为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`。验证文件和逐请求输出见[证据目录](../validation/rechecks/2026-10-28/afocal-psf-contract/README.md)。

六项自动网格重现了原严格坐标门槛失败，最大轴坐标差如下（mrad）：

| 配置 | FFT PSF | Huygens PSF |
| --- | --- | --- |
| Afocal Riflescope / 1 | 1.09518e-6 | 2.82627e-7 |
| Binocular / 1 | 1.93407e-6 | 4.99115e-7 |
| Binocular / 2 | 1.93409e-6 | 4.99121e-7 |

固定角网格控制通过支持继续区分自动出瞳尺度口径与传播误差，但不能单凭它断言根因或将六项改为 Pass。没有拟合尺度、重采样、平移、翻转或峰值归一化来消除差异。

## 仍未关闭

原完整150项仍引用10月27日 **134 Pass / 0 Close / 3 Difference / 13 Incomparable**，不是本轮重新执行的矩阵。三项几何质心图表差异保持；独立角质心光线回归通过不能替换内置图表。另用既有 `binocular-audit` 入口重放两配置的冻结复数场：配置1双方699点全部对应，相位RMS差4.01522e-6波；配置2双方699点，但仍各有2个独有节点，相位RMS差6.08355e-4波，与历史残留一致。未改变掩码或声明逐节点一致。输出字段 `freshProfileMaxDifference` 沿用旧工具名称，本轮比的是两份已冻结截面，不是新原生捕获。

五项相对照度没有有效原生参考、双筒两项 mixed NSC 一阶量不适用，继续分别保留；没有构造参考或近轴等效清零。原生实时派发没有执行，本轮只验证派发换算和冻结读回的一致性。

继续定位数值差异所缺的最小外部依据：同原处方/配置/版本/波长下，自动PSF的完整精度出瞳尺度及角间距定义；双筒配置2对应差异瞳孔节点的复数场/逐光线坐标与接纳定义；质心图需能解释参考切换行为的原生证据。现有资料不足以证明应该改哪条产品光学公式，因此本轮没有猜测性调整。相对照度则需有效原生输出及全部设置/诊断；不要求或执行用户电脑上的操作。

## 有界诊断收尾：已隔离的范围与最小外部证据

本次仅检查冻结数据并重放双筒两配置节点审计，不重新执行150项。机器记录见证据目录 `closure-diagnosis.json`、`binocular-nodes-final.json` 和 `scope-test-audit.json`。

1. **六项自动网格已隔离为间距尺度差，尚不能决定哪方尺度定义应改。** 归档中两轴的样本数量、方向和零点索引一致：FFT为63，Huygens为16；同配置FFT/Huygens的相对步长差一致。瞄准镜约+3.42725e-7，双筒配置1/2约−5.63265e-7/−5.63272e-7。单位解码、整格平移和网格尺寸差不能解释该残留。测量步长比仅用于诊断，没有用于修正数组或拟合产品尺度。
   最小外部需求：原 `Afocal Riflescope.ZMX` 配置1、`Binocular_System.ZMX` 配置1/2，OpticStudio 2026 R1原构建，Field=1、Wavelength=2、最终面、Ray Aiming及原材料/环境保持；FFT PSF 64→128，Huygens PSF 32→32，ImageDelta=0，Linear、Normalize=false、UsePolarization=false、Rotation=0、Huygens UseCentroid=false。先提供完整精度的X/Y出瞳尺度、出瞳位置和坐标框架/尺度定义，以及三组 `model.json`、六组 `captured-settings.json`、原始 `minX/minY/dx/dy/nx/ny` 与诊断消息。原数组已有，无需无条件重复整套采集。只有元数据与旧捕获不一致时再重导该单项数组。此证据区分近轴/真实投影或有限笔形光束定义、版本/设置漂移与真正计算误差。

2. **三项质心差异定位在内置曲线的参考语义，独立角质心积分并未失败。** 本次逐字读取三组冻结导出：Centroid与ChiefRay的完整 `dataSeries` 相等，最大差为0；捕获属性确实分别为两种ReferTo。沿用独立10361条原生光线、13场曲线通过证据，不以主光线结果冒充质心。尚不能判断是原生图表的特定契约还是采集/版本行为。
   最小外部需求：同三个配置、Wavelength=2、Field=0（全部定义视场：瞄准镜0/1.75/2.5度，双筒0/±0.75/±1.5度），Geometric Encircled Energy、SampleSize=S_32x32、RadiusMaximum=5（瞄准镜ArcMinute，双筒Milliradian）、UseHuygensPSF=false、UsePolarization=false、ScatterRays=false、ShowDiffractionLimit=false；只切换ReferTo=Centroid/ChiefRay。需原生版本号、属性读回、API曲线及文本导出的参考坐标/单位，优先取得该版本关于无焦参考中心的权威定义或诊断。若要进一步排除捕获复用，最小采用瞄准镜的Field=2单场，从两个独立分析实例各导出一次；单纯再次重复已知全部曲线不能区分根因。不必重采现有10361条光线。

3. **配置2的节点差异限于四个轴端点，并非散布的丢光。** 64×64反演瞳孔中，Core独有(column,row)=(32,17)/(32,47)，原生独有=(17,32)/(47,32)。Core上下点对应归一瞳孔(0,±1)；左右点按Core网格解释为(±1.0000049793749568,0)，位于其请求圆外，尚未进入实际光线追迹。这个归一坐标是Core解释，不是已测得的原生发射坐标。配置1无独有节点。不能据此推断原生真实光线被挡，也不能擅自交换X/Y或删点。
   最小外部需求：双筒配置2，配置1为控制；Field=1、Wavelength=2、FFT SampleSize=PsfS_32x32、OutputSize=PsfS_64x64、ImageDelta=0、Rotation=CW0、Normalize=false、UsePolarization=false，Real/Imaginary各一份，保留原设置。现有复数场不用重采，首先需要原生FFT网格X/Y尺度/方向/接纳掩码及上述四个索引的实际归一发射坐标。若API不提供掩码，取得对应真实出瞳坐标和中心/四轴端点的显式单光线结果：入瞳坐标、逐面位置/方向、error/vignette、最终OPD/参考平面。目的是区分FFT采样边界/轴约定与物理遮挡；没有上述映射时，任意翻转或旋转匹配不能作为修复依据。

以上是证据缺口，不是访问被拒绝。本任务不运行原生Zemax，也不操作用户电脑。五项相对照度仍缺有效原生输出：如继续验证，沿原五配置各自冻结请求仅导出数值结果、完整属性/选择器和原生错误/诊断；当前无数值不能判定精度。双筒两项mixed NSC一阶量属于已知不适用，不要求采集“通用近轴等效”，不应以补数据为名清零。

最终diff只涉及比较工具、离线诊断输出、测试夹具/测试和文档；`src/`、`labs/`、比较策略及旧参考未改。144指最新去重测试身份（含参数）全部通过，不是144个光学比较通过；其中测试明确断言6项自动网格仍不可比。节点审计是额外两个配置的诊断，不累计为测试通过数。

## 复现命令

在仓库指定 .NET 10.0.300 下：

```sh
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --filter 'FullyQualifiedName~Afocal' --logger trx
```

夹具制备脚本为 `tools/diagnostics/AfocalPsfContract20261028/Prepare-Fixture.py`，仅用于离线复制归档原字节，已有目标时拒绝覆盖；不参与产品计算。

## 继续处理：冻结字段核验与可执行外部探针

本节细化并缩小上文的采集需求，未改变原结论。`existing-fields-final.json` 记录原成员哈希及逐行索引。原生 System Data 实际已有 UTF-16 中文出瞳文本：瞄准镜直径4.026658、位置−8.232425e−05；双筒直径3.750945、位置1.247598。它们是舍入文本，不是内部FFT完整精度的X/Y尺度，不能拿来替换Core值或拟合残差。

双筒两配置现有中心和四个精确圆端点共10条光线，input索引与原生number逐一对应，全部error=0、vignette=0、intensity=1。因此无需重复采集这些端点；也不能将FFT两对节点差异直接解释为真实光线遮挡。原生FFT像素映射仍未导出，Core解释的±1.000004979不等于原生发射坐标。

发现并修复一个额外诊断缺陷：`NativePointComparisonCapture` 的imageGeometryOnly路径实际使用OPDMode.None，却在全局元数据写CurrentAndChief。新输出按实际模式标注，逐面也带opdMode，单独明确imageGeometryOpdMode=None；完整逐面捕获最后一面仍保持CurrentAndChief契约。历史原始数据不改，本次端点核验排除其OPD列。此修复防止误用诊断数据，不代表三项质心差异已解决。

新增[可运行采集器及说明](../tools/diagnostics/AfocalPsfContract20261028/README.md)：默认只准备，不启动原生程序；显式Windows `--run` 才编译原生host并执行11个独立、有超时的任务。包括3个原生瞳孔操作数/参数模式探针、6个Field=2独立实例Centroid/ChiefRay控制、2个13点边界探针。使用冻结处方哈希，保存原始错误，不保存镜头，不替换基准。若原生操作数不公开FFT内部映射，仍需该版本的映射定义；不把近轴操作数强称为mixed NSC的通用物理等效。5项相对照度缺参考和2项一阶不适用保持。

离线准备验证通过：11任务、10端点索引、5份瞳孔文本及全部源哈希/配置/波长/选择器断言。首轮准备曾因未筛除两项无关配置失败，记录在工作区，已限定为3个目标配置后通过。另定点重跑契约和原生无焦回归80项全部通过（Debug，47秒），覆盖原144去重测试中的80项，不能加成224。没有新增光学Pass。原生host的新探针和元数据修正未在专有API环境编译/执行：这是明确外部验证边界，不是权限拒绝。Core、AutoLens、原参考及原门槛均未修改。

后续已授权修改共享Core的阶段见[出瞳交会修复报告](AFOCAL_PUPIL_CONDITIONING_2026-10-28.md)：仅修复FitLines数值相消，125项相关回归通过，原生残留与双筒探针敏感性未关闭。本报告前文“未改Core”描述的是当时阶段。
