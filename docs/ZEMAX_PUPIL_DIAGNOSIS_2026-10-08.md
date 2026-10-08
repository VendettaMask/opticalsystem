# 波前瞄准、带符号渐晕与 RA 网格诊断 · 2026-10-08

最新状态见[确认缺陷修复与完整重算](CONFIRMED_ISSUE_REPAIR_2026-10-08.md)及[待解决问题分类](OPEN_ISSUES_2026-10-08.md)。负向单侧 Y 渐晕已取得新原生证据并修复，混合/非对称场表保留捕获的最近行行为。本文下方的 FFT、零离焦、波前图和渐晕未修复描述保留诊断时点；RA 波前原生节点仍待认证。

这是修复前的诊断记录，其数值和结论范围保持原样。后续波前图瞄准传递、FFT 与零离焦辅助路径已完成相应修复，分别见[波前阶段](WAVEFRONT_AIMING_REPAIR_2026-10-08.md)与[计算路径阶段](CALCULATION_PATH_REPAIR_2026-10-08.md)；不以本文旧待办覆盖当前结论。

诊断轮仅定位问题，不修改产品计算规则、原生参考或容差。**已确认波前图的显示选项错误控制了光线瞄准；已复现负 Y 场表的渐晕不连续；RA 波前节点的原生一致性仍待捕获。** 三项不是同一种“采样不足”。

## 1. 已确认：波前图将出瞳形状开关当成瞄准开关

位置：`src/OptilandWorkbench.Core/Analysis/Wavefront/WavefrontAnalyses.cs` 的 `WavefrontAnalysis.GenerateData`。均匀采样分支把 `_useExitPupilShape` 传给 `aimAtStop`，而不是使用 `Optic.RayAimingEnabled`。非均匀分支调用的 `GenerateChiefRay` 内部固定不瞄准，也需要在下一次修复中检查。

[OpticStudio 2026 R1 Wavefront Map](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Wavefront_Map.html) 将 Use Exit Pupil Shape 定义为按出瞳的 X/Y F 数呈现光瞳形状，不是启用停面瞄准。当前实现却改变实际光线发射与光程，让相同归一化光瞳坐标代表不同真实光线。增加采样密度不能消除这个定义错误。

### 数值复现

采用冻结的三份官方镜头，每份轴上/主波长和边场/短波共六组 OPD 原生光扇，每组两个方向各 41 点。直接比较全部 82 条精确节点记录，不插值、不去倾斜、不拟合活塞或比例；全部节点有效，源/快照/原生哈希先后核验。只切换正式共享 `WavefrontEngine` 的瞄准参数。

| 镜头 / 捕获设置 | 系统瞄准 | 强制瞄准最大误差 waves | 遵循系统设置最大误差 waves |
| --- | --- | ---: | ---: |
| Cooke / 轴上 550 nm | 关闭 | 0.010271390677794 | 1.552293849e-10 |
| Cooke / 20°、480 nm | 关闭 | 364.929953861164 | 2.624171760e-5 |
| Double Gauss / 轴上 587.6 nm | 关闭 | 0.021963776529312 | 2.422235745e-10 |
| Double Gauss / 14°、486.1 nm | 关闭 | 384.154817243149 | 4.031837608e-5 |
| Relay / 轴上 587.5618 nm | 开启 | 7.668519486e-9 | 7.668519486e-9 |
| Relay / 2°、486.1327 nm | 开启 | 6.317280772e-7 | 6.317280772e-7 |

Relay 是开启瞄准的对照，两条路径本来相同，现有有限共轭残差未消失。

另执行实际 `WavefrontAnalysis` 的 64×64 图，按产品自己的未舍入光程元数据恢复显示前带符号 OPD。每组与原生光扇共有 6 条精确共节点记录（5 个独立位置，中心在两个方向各一次），没有把全部 82 个光扇节点说成二维图节点。这些实际图值与强制瞄准引擎路径的最大差均小于 9e-13 waves；Cooke / Double Gauss 的轴上、边场错误也在实际图共节点上复现。

已确认的是**当前产品波前图的光线定义错误**。没有重新获取原生完整 Wavefront Map，也未重新归类原 132 项矩阵。轴上误差与历史矩阵波前图残差相符，但不能以 OPD 共节点检查替代完整二维原生对照。

### 相关路径与推论边界

`DiffractionEngine.GenerateDefocusedWavefront(..., defocusMillimeters: 0)` 也固定 `aimAtStop: true`，同样的六组 82 点控制复现上表强制瞄准误差，零离焦未消除问题。

FFT PSF 的 `ComputeFftPsf` 使用 `cellCenteredPupil || aimAtStop` 作为实际瞄准条件，使“单元中心采样”改变了瞄准。需要拆分节点、系统瞄准、F 数标尺和高 NA 失败处理，再用原生 PSF 验证，不能直接断言它解释所有 PSF 差异。

Huygens 主入口已按传入的瞄准参数运行。本轮没有证据把现有 Huygens、包围能量和照度的全部残差归因于上述波前图问题。

## 2. 已复现内部缺陷：负 Y 场表退回最近行

位置：`src/OptilandWorkbench.Core/Raytrace/RayGenerator.cs` 的 `ResolveVignetting`。插值分支额外要求全部 `field.Y >= 0`；只要场表含负 Y，就转入最近行选择。

将相同 Tessar 原始 ZMX 的当前正式导入模型复制为独立镜像场表，只令 Y 与 VDY 变号、压缩系数不变。镜像后系数不再平滑变化，在行中点突然切换：

| 归一化视场绝对值 | 正 Y 表 VCY | 镜像负 Y 表 VCY |
| --- | ---: | ---: |
| 0.1999999999 | 0.022217988952782 | 0 |
| 0.2000000001 | 0.022217988997218 | 0.0888719559 |
| 0.2666666667 | 0.039498647066667 | 0.0888719559 |

最大视场 25°；在约 5° 的行中点，视场只变动 5e-9°，负 Y 表的 VCY 却跳变 0.0888719559。这会突变光瞳映射，改变像点、接纳掩码和积分，不等同于圆孔边缘的离散采样误差。

[官方 Vignetting Factors](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Vignetting_Factors.html) 描述全部沿 Y 的表使用相邻行插值，一般含 X 的表才选最近行。当前分支的不连续已由代码和内部控制确认；负 Y、跨正负 Y 的原生精确插值公式尚未捕获。不能直接将正 Y 平方径向公式推广到混合符号、重复半径和非对称系统。

## 3. 已量化敏感性：RA 波前与光斑节点不同

独立波前视场的 `Fields/FieldSweepAnalyses.cs` 和其他波前 RMS 的 `RmsScanAnalyses.cs` 使用 `UniformGrid` 端点；RA 光斑使用 `ApertureSampler.GenerateRectangularArray` 单元中心。同为 RA64，圆内候选分别为 3096、3228，光线集合不相同。

相同当前 Tessar 导入模型、25°、589 nm、移除渐晕因子、chief 去活塞，复用正式波前引擎与统计：

| RA 密度 | 端点有效 / 候选 | 单元中心有效 / 候选 | 端点 RMS waves | 单元中心 RMS waves | 相对差（端点减中心） |
| --- | ---: | ---: | ---: | ---: | ---: |
| 32 | 554 / 740 | 584 / 812 | 1.367143614 | 1.357135213 | +0.737465% |
| 64 | 2280 / 3096 | 2346 / 3228 | 1.370261358 | 1.364460857 | +0.425113% |
| 128 | 9248 / 12644 | 9416 / 12892 | 1.364118942 | 1.366928737 | -0.205555% |
| 256 | 37300 / 51040 | 37596 / 51468 | 1.365341888 | 1.365059666 | +0.020675% |

实际 `RmsWavefrontVsFieldAnalysis` 四个密度的边缘结果均与端点控制相同，差小于 1e-10 waves。本轮重新导入 ZMX，不使用旧快照的自动 STOP 硬孔径；RA256 单元中心接纳 37596 条，与此前原生光线接纳证据一致。

这是同一正式算法对不同节点集合的内部敏感性实验，不是四份新原生 RA 波前曲线。差异随密度变号，不能据高密度一次接近认定节点正确或积分收敛。已有原生 RA 光斑节点证据不能自动认证 RA 波前；[RMS 官方说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html)要求孔径截断下采用较密 RA，但未给出该分析精确端点位置。

## 下一步优先级

1. 修复波前图：分开系统瞄准与出瞳显示形状，覆盖瞄准开/关、均匀/非均匀采样，加入 Cooke、Double Gauss、Relay 正式回归。
2. 清点 FFT 与零离焦辅助路径的固定瞄准，先验证物理波前，再对照原生 PSF/MTF；保留高 NA 无法正常发射时的明确失败契约。
3. 捕获负 Y、混合符号 Y 的原生发射光线，确认插值变量与镜像语义后补齐渐晕。
4. 捕获 RA 波前 32/64/128/256、chief/centroid、渐晕开/关，判定节点后扩展高密度收敛。多波长联合参考、偏振介质边界和其他衍射/照度差异仍单列。

## 证据与本轮验证范围

- [诊断源码](../tools/diagnostics/PupilComparison20261008/Program.cs)只调用正式 Core 光学接口，不进入产品或初始结构实验室。
- [最终结果 run-03](../artifacts/validation/pupil-comparison-20261008/run-03/summary.json)：六组共 492 条原生光扇记录、36 条实际图共节点记录、8 个镜像场表位置、4 个 RA 内部控制，不能相加为新原生分析数量。11 个源/快照/原生/清单文件哈希核验不变。run-02 在正确新导入模型上的重复数值相同；run-01 使用旧快照，其 RA 值不作最终控制。
- 正式工具已尝试新捕获，[尝试记录](../artifacts/validation/pupil-comparison-20261008/native-attempt-01/run-summary.json)明确报 `ZOS-API not found`：原生执行 0、数值比较 0，属于环境缺失，不是光学数值失败或新增 Pass；本机许可状态未检测。
- 独立诊断项目 Release 构建零警告、零错误，最终执行成功；最终再核验 11 个证据文件及诊断源码指纹，与上一正确模型运行的瞄准、渐晕数值一致。
- 本轮未修改产品计算、冻结主参考、原生容差或 Word 报告，只新增独立诊断、结果和文档。没有新增正式测试项或重跑全量，既有发布门禁与六文件 132 项分类保持此前记录。
