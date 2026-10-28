# 当前无焦精度复核与证据维护

## 完整原请求复算（2026-10-28）

本次对四份原始无焦文件、五个配置的既有150项请求逐一重新执行Workbench计算，再与原OpticStudio 2026 R1捕获比较；另重算20项独立固定角采样控制。未启动新的Zemax采集，未改参考数组、canonical request、光学公式或比较门槛。150项覆盖18类分析，不代表所有目录入口、任意采样/视场/偏振设置或全95文件。

唯一机器账目为[afocal-comparison.json](afocal-comparison.json)，其中包含逐请求前后结论、原容差、误差、输入指纹和输出存储定位。主矩阵、补充测试及最终瞳孔审计使用同一Core SHA-256 `5fe37b4116bd56863529b57b2164151e7dce2b9c03abde2e8d0315f02084cb17`，对应复核开始时选定的主线Core，源提交为`3fabccb2c9a234965cbeb9502a1db174ed0ffe23`。共享工作区在计算期间新增了其它任务的Core改动，本次没有认证这些未完成改动；曾混入另一构建的首轮瞳孔诊断已排除，最终按上述二进制重算。旧固定网格阶段没有保存二进制身份文件，此处如实留空，不补造历史SHA。

### 逐镜头与以前对照

| 原文件 / 配置 | 请求数 | 以前 Pass / Difference / Incomparable | 本次 Pass / Difference / Incomparable |
| --- | ---: | --- | --- |
| Afocal Riflescope / 1 | 30 | 26 / 1 / 3 | 26 / 1 / 3 |
| Beam Expander / 1 | 22 | 21 / 0 / 1 | 21 / 0 / 1 |
| Binocular System / 1 | 30 | 25 / 1 / 4 | 25 / 1 / 4 |
| Binocular System / 2 | 30 | 25 / 1 / 4 | 25 / 1 / 4 |
| Apochromat prism / 1 | 38 | 37 / 0 / 1 | 37 / 0 / 1 |
| 原150项合计 | 150 | 134 / 3 / 13 | 134 / 3 / 13 |

两个阶段均无Close，本次无超时、无比较执行错误，全部150项结论不变，旧Pass零回退。两项相对照度Workbench明确返回不可用（退出码3），已归入缺参考/不可比，不能称为150份有效数值结果。20项固定角控制全部Pass，与旧结论一致；它们属于另一组请求，不把通过数加进原150项。结论一致不等于输出逐位相同：本次与旧矩阵最大的指标NRMSE变化约`6.997e-11`。

### 18类分析的实际残差

以下仅统计原150项；“最大差”是已对齐、有效数值分量的最大绝对差，不能为不可比项目推断强度精度。NRMSE为原工具定义，按参考最大绝对值或绝对门限中的较大值归一化。不同单位不可横向当成统一百分比。

| 分析 | Pass / Difference / Incomparable | 最大绝对差 | 最大NRMSE |
| --- | --- | --- | ---: |
| 逐面光线 | 20 / 0 / 0 | 位置`6.28219e-8 mm`；方向/法线`4.04282e-9`；入射角`2.22777e-8°` | `1.44765e-3`，来自近零分量 |
| 光线扇形 | 20 / 0 / 0 | `8.88613e-8°` | `2.79081e-6` |
| 点列图 | 20 / 0 / 0 | `1.41824e-6 mrad` | `2.42246e-6` |
| 光程差OPD | 20 / 0 / 0 | `0.0391731 wave` | `9.27943e-4` |
| 波前图 | 5 / 0 / 0 | `4.95689e-6 wave` | `1.05301e-5` |
| 场曲/畸变 | 5 / 0 / 0 | `4.90461e-9 D` / `4.56877e-6`个百分点 | `1.28368e-5` |
| 一阶量 | 3 / 0 / 2 | 长度`1.08629e-5 mm`；无量纲量`1.08627e-6` | `3.30062e-12`，含大幅值近轴量 |
| FFT MTF | 5 / 0 / 0 | `0.0111766`调制度 | `3.16066e-3` |
| Huygens MTF | 5 / 0 / 0 | `4.36472e-8`调制度 | `2.90824e-8` |
| Huygens离焦MTF | 5 / 0 / 0 | `0.00278356`调制度 | `2.03307e-3` |
| FFT PSF | 2 / 0 / 3 | `0.233718`，捕获线性强度尺度 | `5.00743e-3` |
| Huygens PSF | 2 / 0 / 3 | `1.08940e-8`，捕获线性强度尺度 | `1.70562e-9` |
| FFT PSF截面 | 5 / 0 / 0 | `0.00119466` | `3.51861e-4` |
| Huygens PSF截面 | 5 / 0 / 0 | `8.32962e-5` | `2.31320e-5` |
| 几何圈入能量 | 2 / 3 / 0 | `0.191227`能量比例，即19.1227个百分点 | `7.71874e-2` |
| 衍射圈入能量 | 5 / 0 / 0 | `0.00117233`能量比例 | `8.22978e-4` |
| RMS随视场 | 5 / 0 / 0 | `0.00221811 mrad` | `6.54394e-4` |
| 相对照度 | 0 / 0 / 5 | 无有效原生数值依据 | — |

### 总通过数之外的精度问题

**扩束镜FFT PSF的峰值/幅度差异应继续处理。** 自动采样峰值为Core `0.7662612705`、Zemax `0.9999794964`，中心差`0.2337182259`，约为原生峰值的23.37%；固定角采样控制也存在Core `0.7603254372`、Zemax `0.9999776444`，差`0.2396522072`，约23.97%。这两项原来和现在都按原NRMSE规则通过，不能由此声称强度或能量尺度已一致。形状高度相关也不能替代幅度认证；本次只确认残差，没有把原因归结为已证明的归一化或瞳孔缺陷。

原[比较器](../../../../tools/OpticalSystemDesign.ZemaxComparison/Metrics/ComparisonMetrics.cs)在覆盖率合格后，允许“全部点满足绝对+相对误差”**或**“多点NRMSE满足门槛”判Pass。新增逐点审计检查了只有一个有效quantity门槛、且已对齐的180条指标序列：21条序列仅由NRMSE通过，涉及13个请求，其中11个请求的总结果原本为Pass。未检查所有多quantity请求，因此这是至少11项，不能称为全矩阵精确总数。它们仍保留原Pass分类，但局部残差必须同时读：除扩束镜PSF，还包括瞄准镜FFT MTF最大`0.0111766`、OPD最大`0.0391731 wave`、扩束镜Huygens离焦MTF最大`0.00278356`等。此次没有通过改门槛、归一化峰值或删除异常点来消除差异。

三项内置质心几何能量仍为Difference：瞄准镜最大19.1227个百分点，双筒两配置各16.0602个百分点。独立真实角质心控制通过，不能替换这些内置曲线。六项自动FFT/Huygens PSF仍因网格轴不一致而不可比：瞄准镜轴差分别`1.095180862e-6 / 2.826273192e-7 mrad`，双筒配置1为`1.934071230e-6 / 4.991151561e-7 mrad`，配置2为`1.934093841e-6 / 4.991209909e-7 mrad`。没有重采样或拟合间距。其余不可比为相对照度5项、mixed NSC一阶量不适用2项。

双筒逐节点审计已按同一Core重跑：两个配置各有699个有效节点；配置1集合相同，以原生中心复场为参考的主值相位RMS差`4.01521e-6 wave`；配置2仍有2个仅Core、2个仅原生节点，共同有效节点相位RMS差`6.08355e-4 wave`。这不是解除相位缠绕后的全瞳OPD误差。原生独占节点的归一化坐标是按捕获像素和当前映射解释的值，不冒充直接采集的原生发射坐标。诊断中的`freshProfileMaxDifference=0`只比较两份已冻结的原生截面，不是本轮新采集Zemax结果。

### 补充控制与证据复用

本次补充18项原生控制测试通过：13组OPD/169点、3组独立角质心（13视场、10361条原始原生光线）以及2配置场曲操作数。另20项正式PSF归一化测试通过，其中包括明确断言6项Incomparable；它们不是20个新增光学Pass。这38次断言不与此前227项测试或150/20项光学请求累计覆盖。有焦1159+52完整集合未在这一步重跑；此前同日55+5项有焦定向检查见下方保留阶段。

新数值、匹配CSV、两份TRX、二进制统一记录及采集脚本只保存在[本次差异证据包](afocal-current-evidence.zip)。所有168份可用Core数值输出均来自本次执行；其中134份与[旧密封证据包](../../2026-10-27/afocal-fft-energy/evidence-current.zip)中的成员逐字节相同，仅在新账目引用其成员和SHA，另34份写入新包。489份匹配值CSV同样按字节身份引用或新存，没有再复制原生数组、PNG或整套旧报告。ZIP写入后逐成员读回验证，归档SHA/成员清单见机器账目。存储复用不代表跳过计算。

在仓库根目录可用同一诊断重建独立输出（不修改旧证据）：

```powershell
$work = 'artifacts/validation/afocal-current-comparison-20261028'
dotnet build tools/diagnostics/AfocalReferenceFocus20261027/AfocalReferenceFocus20261027.csproj -c Release --no-restore --output "$work/runner"
dotnet "$work/runner/AfocalReferenceFocus20261027.dll" prepare-existing artifacts/validation/afocal-fft-energy-20261027/final-150-v2 artifacts/validation/afocal-replay-new/original-150
dotnet "$work/runner/AfocalReferenceFocus20261027.dll" workbench artifacts/validation/afocal-recompare-20261026 artifacts/validation/afocal-replay-new/original-150
dotnet "$work/runner/AfocalReferenceFocus20261027.dll" compare artifacts/validation/afocal-recompare-20261026 artifacts/validation/afocal-replay-new/original-150
```

固定控制把原输入目录换成`final-fixed-v2`，并选另一个全新输出目录。`prepare-existing`要求旧`items.json`、快照及原生捕获实际可读，保留请求对象并记录输入SHA；这些本机生成目录不等于可从Git单独下载的完整运行环境。若从新代码重新构建，先记录其实际Core SHA，不把它冒充本次固定二进制。完整汇总/逐点脚本保存在ZIP的`audit/`下，需要本机生成工作目录；新账目提供已密封输出的独立定位与哈希。

一次逐点统计脚本误用了PowerShell保留变量，随后一次内联重试解析失败，均未影响光学工作进程；修复后从完成的匹配CSV重做审计。首次混合二进制瞳孔诊断和旧固定运行身份缺失导致的打包中断也已保留说明，均不作为通过证据。会话没有DeepSeek MCP工具，未向外部审查方发送代码。

## 首次定向检查（此前阶段）

2026-10-28，Windows/.NET 10.0.300，源提交 `3fabccb2c9a234965cbeb9502a1db174ed0ffe23`。全部后台执行，没有启动/操作桌面程序，没有重新采集 Zemax、修改产品计算或比较门槛。

### 首次定向检查的实际结果

| 检查 | 通过 / 失败 / 跳过 | 范围 |
| --- | --- | --- |
| 无焦相关 | 167 / 0 / 0 | 四个原始无焦文件、双筒两配置；冻结原生参考、正式 PSF 契约、FFT/能量/离焦、解析物理及诊断守卫 |
| 有焦原生参考相关 | 55 / 0 / 0 | 能量 8、RA256 采样 13、RMS 视场采样 18、离轴 Huygens 1、原物镜精度 15；含完整性/契约断言 |
| 有焦出瞳解析回归 | 5 / 0 / 0 | 原平板、曲面、刚体位移、三维瞳孔及交会测试，直接编译原测试文件 |

三个 TRX 按测试身份去重为 **227 个不同用例通过**，不是 227 项 Zemax 数值对比。无焦集合中包含断言已知不可比结果、拒绝错误契约及只检查探针有限值的测试。测试持续时间分别约 22 秒、2 分 56 秒、2 秒，不与以前的执行次数累计。

测试和共享 Core Release 输出的 SHA-256 全部为 `5fe37b4116bd56863529b57b2164151e7dce2b9c03abde2e8d0315f02084cb17`。原比较策略 SHA-256 为 `30c27e7976710867d0edc6dd4099edac54ca9e070245dea679b3afdd88fd28f5`，未改变。原 **2668 份冻结证据的 HEAD 与工作副本均逐字节匹配**，聚合身份 `3a5fde98e1c881700f00be1c62d6ab4ff6f4feacc90454046ed787e80bf28c10`。

### 首次定向检查的精度边界

1. **六项自动 PSF 网格仍不可比，本轮已重现。** 原自动 PSF 共 10 项：4 Pass / 6 Incomparable；独立固定角网格 PSF 10 项全部 Pass。瞄准镜的 FFT/Huygens 最大轴差分别为 `1.095180862e-6 / 2.826273192e-7 mrad`；双筒配置 1 为 `1.934071230e-6 / 4.991151561e-7 mrad`，配置 2 为 `1.934093841e-6 / 4.991209909e-7 mrad`。没有放宽坐标门槛、拟合尺度或重采样。
2. **三项原生内置质心曲线 Difference 和双筒配置 2 的节点差异仍未关闭。** 本轮重新通过独立真实角质心、FFT 截面和衍射能量相关测试，但没有重跑内置几何能量的完整矩阵或复数场逐节点审计。原生/内部参考语义与棱镜边界解释的证据缺口见[正式比较报告](../../../../docs/AFOCAL_PSF_CONTRACT_REPAIR_2026-10-28.md)、[原生边界报告](../../../../docs/AFOCAL_NATIVE_EDGE_CONTRACT_2026-10-28.md)。不能用测试通过替换原 Difference，或宣称所有瞳孔节点一致。
3. **惠更斯仍有有限采样误差。** 生产无焦 Huygens 对独立均匀圆瞳 Airy 解，N=32/64 的全窗口最大绝对差为 `0.02371247249 / 0.01146785959`（峰值为 1，约 2.37% / 1.15%）。加密时减小，但仍是百分比量级；沿用原离散误差预算，不能称为普遍高精度认证。生产 FFT/PSF-MTF 的相同圆瞳检查在 N=32/64/128 的最大绝对差为 `0.00574685 / 0.00258841 / 0.000465401`。这属于限定圆瞳的独立采样检查，并非上述实际镜头与 Zemax 的误差。
4. **五项相对照度缺有效原生参考，两项 mixed NSC 一阶量不适用，仍不计通过。** 没有新数据就不能判断这些项的数值精度。偏振、大角度和任意复杂瞳孔不因本轮通过而扩大认证范围。

在本轮选定范围内未观察到新回退。原完整无焦矩阵 **134 Pass / 0 Close / 3 Difference / 13 Incomparable** 及有焦 **1159+52** 保护结果继续作为其原阶段的历史记录；本轮没有重跑上述完整集合，也不是全 95 文件认证。

### 首次定向检查的复现与证据

根目录执行（Release）：

```powershell
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Afocal' --logger trx
dotnet test tests/OpticalSystemDesign.ZemaxComparison.Tests/OpticalSystemDesign.ZemaxComparison.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~HuygensOffAxisDensityTests|FullyQualifiedName~CapturedEnergyRepairTests|FullyQualifiedName~CapturedRa256SamplingTests|FullyQualifiedName~CapturedRmsFieldSamplingTests|FullyQualifiedName~ObjectivesNativePrecisionTests' --logger trx
dotnet test tools/diagnostics/AfocalPsfContract20261028/FocusedPupilRegression.csproj -c Release --no-restore --filter 'FullyQualifiedName~ThreeDimensionalStopImagePreservesPhysicalWavefrontAndRetainsCurvedDepth|FullyQualifiedName~ConjugateFitRecoversKnownThreeDimensionalPointAndReportsSkewInsteadOfInventingExactFocus|FullyQualifiedName~PlaneParallelPlateStopImageMatchesIndependentConjugateAndTiltedDetectorProjection|FullyQualifiedName~GeometricPupilMovesRigidlyWithoutChangingItsShapeOrPhysicalWavefront|FullyQualifiedName~PostStopCurvedInterfaceHasAnalyticAxialConjugateAndNonlinearLateralPupilDistortion' --logger trx
```

三份 [TRX](tests/afocal.trx)、[有焦 TRX](tests/focal-native.trx)、[解析 TRX](tests/pupil-focal.trx) 和各自日志随目录保存。完整性见 [frozen-head-final.json](frozen-head-final.json)；[verification.json](verification.json) 包含二进制/策略身份、逐请求网格差和独立物理测量。已有本次 Release 输出时可执行 [Verify.ps1](Verify.ps1) 重新核验该账目，脚本不执行光学计算。

两次未成功启动/调用不计入通过：第一次受限测试进程无输出且未生成 TRX，取消后后台重启；第一次完整性调用给 relocation-map 传入绝对路径，被检查器拒绝，改用要求的仓库相对路径后重新完整核验。原日志保留为 `afocal-startup-incomplete.log`、`frozen-integrity.log`，没有删除失败或替换原参考。

## 结论与文件重复性核查（此前维护阶段）

首次整理只核查记录。[文件/运行重叠账目](file-duplication-audit.json)保存整理前范围：695个无焦相关文件共220,984,051字节，23组非空文件逐字节相同，可重复字节合计1,956,485，约占0.89%；另有一组空日志。这是限定范围、整文件哈希检查，没有展开ZIP，也没有扫描所有产品或AutoLens记录。后续实际副本收敛及必要回归见下节，不改写首次核查身份。

可取得的14份旧无焦TRX包含220次结果记录，但只有90个不同testId；其中85个也出现在本轮167项无焦测试中。该统计只说明重复执行，包含失败及有焦控制，不计算新增光学覆盖，也不代表已检查Library中的全部记录。比如旧`contract-final.trx`与`github-fixture-reuse.trx`的40个身份都在本轮集合中，不能再加成80个新用例。

当前结论的维护分工和阶段关系统一在[数值验证指南](../../../../docs/NUMERICAL_PARITY.md)。已收敛状态页的重复广播、问题页的旧全量表述，并修正旧派生夹具说明。后续复查只增加新的执行记录及必要差异，不另复制参考数组或重复发布同一结论。

## 实际副本收敛与防止再引入

[去重账目](deduplication.json)记录23组内容的127个物理别名移除，保留23份原字节内容和全部150条原逻辑身份。减少1,956,485字节重复载荷，空日志保留各自运行上下文。原695条审计路径中现有568份物理文件，非空重复组为0；这不是全仓或ZIP内部去重。所有旧归档、原2668文件清单、原生数组数值及比较策略保持。

[唯一映射](../../../evidence-aliases.json)声明源路径、别名、字节长度和SHA。比较测试构建改为调用[还原工具](../../../../tools/validation/EvidenceAliases.ps1)，在生成目录恢复原夹具树；不需要给每份共享数据写另一份JSON副本。旧诊断若直接读取历史物理路径，应先按[工具说明](../../../../tools/validation/README.md#无焦证据副本收敛)生成独立历史视图，不向当前证据目录回填重复文件。

本机Windows完成：全新输出目录的167项无焦测试通过（0失败/跳过），9项别名与路径/内容守卫通过，300份夹具与清理前快照逐字节匹配，127条历史路径还原匹配，原2668文件HEAD/工作区完整性通过。末次新增的目标祖先链接检查也通过9项守卫及300份夹具复验；没有为此重跑有焦全矩阵或把重复执行加进227项覆盖数。物理路径、内容损坏、重复回填及新未登记重复由守卫拒绝；三平台CI已接入，尚不声称远端执行通过。

详细TRX、存储检查、原夹具哈希快照及还原/完整性结果统一保存在[单份去重证据包](dedup-evidence.zip)，各成员的长度与SHA列在去重账目。原始运行文件另留在本机`artifacts/validation/precision-check-20261028`，没有把还原后的重复目录提交回仓库。此次共享工作区存在另一任务的应用/初始结构修改，本账目仅声明上述去重范围。当前会话未发现DeepSeek MCP工具，未调用外部审查或发送代码。
