# MS-L7 全分析数值对比扩展（2026-09-06）

本文是扩展阶段的历史记录：40/7/5 和 1311 项测试属于当时版本。当前结果为 44 Pass、6 Close、2 Difference，完整测试 1337 项；详见 [Huygens 后处理后续修复](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。下文相位差异后来确认为导出数组与 GUI 平均相位指示器的物理量混用，不再作为当前数值误差。

本轮针对用户确认的 `[MS-L7](10X大NA大视场).ZMX`，源 SHA-256 为
`8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`。
当时按用户要求仅保留本地；2026-09-06 后续已授权提交同步，见 [最新记录](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。本文记录实现和实测边界，不能视为全分析精度认证。

主要外部精度权威仍为仓库已提交的 Zemax OpticStudio 2026 R1 `123456.ZMX`。
MS-L7 是新增镜头覆盖，不替换主基准。实测软件为 26.1 SP0、API 260127、有效 Enterprise 许可证；
17 面、1 配置，ObjectHeight 视场 0/-10.6/-15 mm，三波长 0.4861327/0.5875618/0.6562725 µm，
主波长为第二波长，Ray Aiming 开启。每项配置、单位、采样、参考方式和容差随请求保存，均非通用 Zemax 默认值。

## 54 项的处理边界

- **42 项新增数值契约**，加上原有 10 项，共 52 项可执行数值比较。实现并不等于通过。
- **4 项点列布局**：全视场、矩阵、配置矩阵和离焦点列。实际采集显式设置，审计全部 IAR 数值、RGB、散点、光线、点列统计和文本通道。没有点云时不得用标准点列的 RMS 数值冒充布局对照。
- **3 项物理定义不一致**：渐晕图是编辑器渐晕系数而非实际通光比例；Foucault 是带人工边缘的波前梯度近似；部分相干图像是源图与模拟图的强度混合而非相干传播。这些保留原始能力捕获，标为 PhysicalDefinitionMismatch，不声称已完成精度适配。
- **5 项图像/光源契约未完成**：Image Simulation、Geometric Image、Geometric Bitmap、Light Source、Extended Diffraction Image。执行 native capability inspection，仍标 AdapterNotImplemented。共同源、光谱、探测器和辐射量定义尚未闭合，不能因同名或通道存在给出一致性结论。

五项未完成的图像契约和仍存在的数值差异是明确未解决项；本轮没有把它们隐藏成已通过。
Image Simulation 额外尝试本地 BMP 输出，Geometric Image 使用 GreyScale 探测栅格输出；
没有结构化数组不自动等于整个 API 不支持。检查使用 Reset 后保存的实际设置，标明 inspectionOnly，绝不与 Workbench 的另一套默认源图直接比较。

## 修复内容

| 路径 | 修复及保留边界 |
|---|---|
| Seidel / 色差 / 像差 | 所选波长的旁轴光瞳归一化；四张未舍入系数表；色焦移相对主波长旁轴焦点；衍射焦深 4λF²；Standard/Fringe 基底、带符号场像差和独立分量参考 |
| 光线、轴向与场扫描 | 转发系统光阑瞄准；像面局部坐标与出射方向；单光线入射侧法线；两类角度扫描与同输入列表的 native IBatchRayTrace 比较，并逐条检查有效状态 |
| RMS | 修正 Gaussian 模式误用六角环；角向采样成为显式设置。Core 默认仍为 6，本轮工具明确使用 12，并独立检查 12/24/48 角向收敛，不把未公开的 native 内部规则说成已知默认值 |
| MTF / PSF 剖面 | 场扫描遵循连续视场密度；离焦同时更新间隔和局部坐标并失效缓存；几何 OTF 瞄准及非偏振权重；FFT 场 MTF 使用光瞳自相关，剖面保留周期端点并按明确采样插值 |
| 能量 | 几何统计不重复施加表面透射；扩展源使用自有均匀方形 IMA，源文件在运行前冻结并校验 SHA-256；没有拟合原点或强度比例 |
| Jones | 初始偏振基正交于入射光线；Y 输入电场投影到像面局部 X/Y；体吸收按振幅 Beer–Lambert 处理。衍射路径避免重复吸收，不声称已验证完整复 Jones 矩阵 |
| 比较工具 | Windows 指标文件名清除冒号，避免 NTFS alternate data streams；原始数据、普通 CSV 和图像分离；单位依据 typed axes；MTF 频率统计不套到离焦/视场轴，能量统计不套到相位网格；逐项异常不终止后续分析 |

ZOS-API 26.1 的部分 RMS 枚举显示名与保存报告的物理参考和采样数不符，工具同时核对最终 selector、物理报告、范围和数目。
通用 Core 默认值与 Application 产品预设保持边界。本轮数值错误修复有意改变受影响结果，不能沿用此前纯 Python 移除阶段的“数值不变”声明。
生产计算仍为纯 C#/.NET；依赖方向为独立 tools → Application/Core，src 不读取 native 工具或回归资产。

## 完整验证结果

本轮完成适配扩展和一批计算错误修复，**没有完成全部分析的数值对齐**。
MS-L7 的 72 项：40 Pass、7 Close、5 Difference、17 Incomparable、3 Skipped，0 Error。
其中 52 项有实际数值比较；17 项不可比由 4 项 API 无输出、3 项物理定义差异、5 项图像适配未完成、5 项 Workbench-only 组成；3 项跳过为非序列两项和 Classified Data。
另枚举 105 项 Zemax-only，未执行，不能混入 72 项通过率。
原先 54 项待适配现为 **30 Pass、7 Close、5 Difference、4 API 限制、3 物理定义不一致、5 适配未完成**。

完整当前运行：[MS-L7 报告](../artifacts/zemax-comparisons/ms-l7-analysis-expansion-2026-09-06-x/COMPARISON_REPORT.md)。
原有十项契约在主基准 `123456.ZMX` 上重新实时执行，**10/10 Pass**；[主基准复验](../artifacts/zemax-comparisons/123456-analysis-expansion-2026-09-06-y/COMPARISON_REPORT.md)。
这不代表主基准全部 72 项都已重新实时采集。主基准目录与两支原镜头未修改。
两个运行各自保留其程序集哈希，MS-L7 全部 70 次 Workbench 执行的计算程序集指纹一致。
最终代码还通过 42 项扩展捕获的离线重算；后续物理轴拒绝校验不改变此 ObjectHeight 镜头数值。

- 锁定依赖还原通过：本地 NuGet 缓存，`--locked-mode --force --source C:/Users/19851/.nuget/packages -p:NuGetAudit=false`。
- 完整解决方案 Release 默认输出构建：0 警告、0 错误。
- 完整解决方案测试：主测试 **1215/1215**，独立比较工具 **96/96**，共 **1311**，零失败、零跳过。包含架构/文件格式守卫、主基准回归、42 项扩展捕获和新物理/文件输出检查。
- `dotnet format whitespace --verify-no-changes` 与 `git diff --check` 通过。
- 42 项数值捕获及 12 项能力捕获冻结到 validation，manifest 校验所有文件；更早的 26 项捕获没有重写。
- 未运行在线漏洞审计、独立实验室、旧外部报告工具或 GUI 截图复核；图像能力检测与自动数值绘图不算 GUI/截图精度验证。

完整命令、版本/源/配置哈希、72 项请求与误差见 [可审查验证清单](validation/ZEMAX_ANALYSIS_EXPANSION_2026-09-06.json)。
历史失败诊断保留在 ignored artifacts；不放宽公共数值容差。
部分回归测试专门限制已知误差不能恶化，这种测试通过 **不等于数值 Pass**，报告仍显示 Close/Difference。

## 未通过的数值项目

下表取每项 NRMSE 最大的比较分量，并列出该分量的最大绝对误差。NRMSE 是比例，不是百分数。
System Data 为单标量绝对/相对门槛；其它项目同时检查物理覆盖。不同物理量不能横向比较绝对误差大小。

| 分析 | 结论 | 最大 NRMSE | 同分量最大绝对误差 | 单位 |
|---|---|---:|---:|---|
| Encircled Energy | Close | 0.0076630144 | 0.048529065 | Dimensionless |
| Diffraction Encircled Energy | Difference | 0.013875843 | 0.050537607 | Dimensionless |
| Geometric Line Edge Spread | Difference | 0.028192685 | 0.14595104 | Dimensionless |
| Extended Source Encircled Energy | Difference | 0.013431184 | 0.028601856 | Dimensionless |
| Huygens Through Focus MTF | Difference | 0.03268915 | 0.094583405 | Dimensionless |
| Fourier MTF vs Field | Close | 0.0040996092 | 0.0094038796 | Dimensionless |
| Huygens MTF vs Field | Close | 0.0078836219 | 0.014692832 | Dimensionless |
| Relative Illumination | Close | 0.0089946664 | 0.018440785 | Dimensionless |
| Huygens PSF Cross Section | Close | 0.003725667 | 0.0053915815 | Dimensionless |
| Contrast Loss Map | Difference | 0.013852809 | 0.048307327 | Dimensionless |
| Jones Pupil | Close | 0.0045266239 | 0.007171899 | Dimensionless |
| System Data Report | Close | 2.4542887E-06 | 2.5295411E-05 | Millimeter |

Contrast Loss 的两张损失网格已 Pass（NRMSE 约 1.44e-6），未通过的是 OPD 相位的正弦/余弦分量，不是把整个损失图都算错。
Jones 仅验证 Y 输入的像面 Ex/Ey 幅值，尚不验证另一输入、复相位或完整矩阵。
Huygens 加密采样诊断也保留：离焦 MTF 在 pupil 64/image 128 下 NRMSE 约 0.02595，仍未达到门槛；不能靠选择一次较好采样宣布已经对齐。

## 尚需完成的工作

1. Huygens 离焦/PSF/场 MTF：对齐焦移后的参考球、有限像面窗口及 OTF 取样规则，并同时保持两支镜头回归。已排除通过改用直接频率求值即可普遍修复的假设。
2. 能量和几何线扩散：检查光瞳积分、有限窗口、直方图/累积分布与面积源子采样的差异；仍以固定 native 数据及预先记录容差判断。
3. Contrast 相位、Jones 和相对照度：分别核对相位参考、局部电场/透射和有效入瞳积分；不得做经验相位平移或强度比例拟合。
4. 五项图像契约：先提交共用源资产与哈希，明确单色/复色、物方尺寸、探测器像素间隔、PSF/光线采样、归一化和单位，再接通 BMP/数值网格；现有 capability capture 只证明接口输出能力。
5. 三项物理模型差异须先修正定义或传播模型。四项点列布局不得拿另一分析的标量替代缺失点云。
## Reference definitions

- [Seidel coefficient definitions, 2026 R1](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v261/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Seidel_Coefficients.html).
- [Chromatic focal shift](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Chromatic_Focal_Shift.html): primary paraxial focus reference; confirmed with the installed 2026 R1 capture.
- [Full Field Aberration](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Full_Field_Aberration.html): Standard basis and component-wise display references; confirmed with the installed capture.
- [RMS vs Field](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/RMS_vs_Field.html): physical reference and orientation semantics. These older readable help pages supplement, rather than replace, versioned 2026 R1 measurements.
