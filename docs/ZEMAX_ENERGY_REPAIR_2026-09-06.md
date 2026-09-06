# MS-L7 能量计算与相位对照修复（2026-09-06）

本文是能量/相位阶段的历史记录，1323 项测试及下文误差属于当时版本。当前完整测试为 1337 项，Huygens 的后续修复、局部退步与全部 72 项重新计算结果见 [最新验证](ZEMAX_HUYGENS_REPAIR_2026-09-06.md)。

本轮在全分析扩展后继续修复数值差异，当时仅保留本地，后续提交同步授权见本页开头的最新验证链接。生产计算保持纯 C#/.NET，
没有增加 Python Optiland 后端或互操作；通用数值后端与历史程序集、命名空间继续保留。
既有扩展阶段的 40 Pass / 7 Close / 5 Difference 见 [历史扩展记录](ZEMAX_ANALYSIS_EXPANSION_2026-09-06.md)。

## 已修复的三项

| 分析 | 修复前最大 NRMSE | 修复后最大 NRMSE | 原有容差结论 |
|---|---:|---:|---|
| 几何圈入能量 | 0.007663014411 | 7.6931e-17 | Close → Pass |
| 几何线/边缘扩散 | 0.028192685283 | 3.5137e-16 | Difference → Pass |
| 扩展光源圈入能量 | 0.013431183657 | 0.000937756661 | Difference → Pass |

数值是比例，NRMSE 0.000937756661 约为 0.094%。没有改变容差、原始数组或源镜头文件。

几何采样中的 N 是光瞳间隔数，实际有 N+1 个轴向结点，圆边界也参与统计。
32 个间隔对应 33 × 33 网格中的 797 个圆内点；旧实现误用了 32 个结点。
几何能量的 100 个累计结点经过自然三次插值、范围约束和单调约束，输出 396 点并省略最右端点。
线/边缘扩散的直方图宽度为 `2R/N`，显示坐标步长为 `2R/(N-1)`；边缘累计值在区间中心取值，
应包含当前区间的一半能量。旧实现用了显示坐标步长分箱，并累计了整个当前区间。

扩展源的显示分箱另有一格偏移。用独立的 5 / 10 / 20 µm 范围捕获后，
共同结点能量完全相同，证明显示结点 `R*i/99` 使用 `CDF(R*(i-1)/99)`。
该规则实现于输出层，不平移光线、源图或像面，不拟合缩放参数。输出元数据记录结点数、显示点数和评价半径偏移。
同样采用 100 结点、396 显示点。源面/光瞳采样没有逐条匹配，仍存在误差，不能称为浮点级一致。

独立主基准 `123456.ZMX` 的全部 5 个几何能量视场，以及第一视场的线/边缘扩散，
也与新实时捕获逐点吻合，最大绝对差不超过 4e-15。
两个新增扩展源范围均通过原有能量容差，且三档分箱关系有独立断言。
这些是特定镜头、软件版本和设置的证据，不是通用 Zemax 默认规则声明。

## 相位对照的物理量修正

Contrast Loss Map 的已捕获相位数组与未偏移光瞳的波前相位一致，
但官方 GUI 指示器定义为两条偏移光线的平均 OPD。此前比较工具把这两种量直接相比，产生错误的相位差异结论。
现在 Core 保留原有平均 OPD 指示器与损失公式，同时发布独立、有类型和单位的原光瞳相位序列；
工具将该独立序列与原生数组对照。增加物理测试：原光瞳相位不随自相关方向/频率改变，而平均 OPD 指示器仍可以改变。
有效平均 OPD 与损失计算不依赖额外光线是否有效，关闭相位显示时也不额外追迹该光线。
结构化辅助序列保留在未舍入数据输出中，摘要表与文字报告不将其 CLR 类型名当作指标值显示。

MS-L7 与独立 `123456.ZMX` 捕获都验证了原光瞳相位数组关系。该项明确标记 PartiallyComparable：
比较两张损失图和原生导出的相位，不声称已经验证 GUI 的平均相位指示器。
主基准的损失最大 NRMSE 为 1.71e-7，原相位正弦/余弦最大 NRMSE 为 5.41e-8。
MS-L7 的原相位分量最大 NRMSE 为 2.1671e-6，损失仍为 1.4397e-6；六个分量均通过原有容差。
这属于比较契约修复，不是通过更改正确的产品公式去贴合原生数组。

## 架构与兼容边界

- Core `EncircledEnergyAnalysis` / `ExtendedSourceEncircledEnergyAnalysis` 的 `ZemaxCompatibleOutput` 默认 false，保留直接 CDF 与调用者请求的输出点数。Application 和比较工具显式选择兼容绘图预设。
- 几何线/边缘扩散修正了采样与积分定义；末区间仍有能量时，区间中心的累计值小于 1。旧测试的强制末点等于 1 已替换成能量守恒断言。
- 新增 `EnergyPlotSampling` 共享纯 C# 插值输出，不依赖验证工具或资产；依赖仍为 tools → Application → Core，测试 → 冻结 validation。
- STAROPT、ZMX、SEQ、LEN 和通用 JSON 格式入口没有变动；本轮改变受影响分析的点数和数值，脚本应读取输出元数据，不能硬编码旧点数。
- 没有恢复 Python 专用接口、运行探测、配置开关或 JSON 格式。历史命名重命名仍是独立议题。

Python Optiland 的删除清单和保留边界见 [移除审计](PYTHON_OPTILAND_REMOVAL.md)；
仍出现 Python / Optiland 的逐文件、逐处引用与保留理由见 [引用索引](RETAINED_PYTHON_OPTILAND_REFERENCES.md)。

## 验证记录

完整测试中还复现了查看器跨线程访问静态 Pen.Brush 的异常。`OpticSceneControl` 的共享资源已改为
不可变画笔、画刷和渐变；保留颜色、线宽与虚线定义，删除两个未使用的背景画刷。
新增从工作线程读取共享绘图资源的回归，并保留实际主题/悬停渲染测试。这是界面资源生命周期修复，不改变数值计算。

当前 MS-L7 的 72 项为 **44 Pass、6 Close、2 Difference、17 Incomparable、3 Skipped，0 Error**。
实际比较 52 项；另枚举 105 项 Zemax-only，未执行，不计入上述 72 项。
原先 54 项待适配目前为 34 Pass、6 Close、2 Difference、4 API 限制、3 物理定义差异、5 图像契约未完成。
Contrast Loss 的 Pass 仅限已声明的部分可比契约。

- 强制锁定依赖还原通过：`--locked-mode --force --source C:/Users/19851/.nuget/packages -p:NuGetAudit=false`；离线缓存还原，未运行在线漏洞审计。
- 完整解决方案 Release 默认输出构建：0 警告、0 错误。
- 完整解决方案测试：**1219 主测试 + 104 工具测试 = 1323**，零失败、零跳过；包含架构、其它文件格式、主基准、能量/相位及冻结捕获守卫。
- 完整 `dotnet format --verify-no-changes` 与 `git diff --check` 通过；42 个新增捕获/说明文件的 manifest 哈希一致。
- 全分析运行期间 70 次 Workbench 执行的计算程序集指纹一致。之后仅调整结构化辅助序列的摘要显示，并修复查看器静态绘图资源的线程归属，未改变 BuildAnalysisData 或 Core 数值；最终完整测试在该显示调整之后执行。
- 原有十项 MS-L7 契约继续通过。主基准本轮独立复验几何能量、线/边缘扩散、相位/损失三项；没有将其描述成主基准全 72 项重捕获。
- 未执行 GUI 截图精度复核、安装包安装/卸载、独立实验室、旧外部报告工具；旧中途停止的对比仅作诊断，不计入本次完成结果。

[完整运行报告](../artifacts/zemax-comparisons/ms-l7-energy-phase-repair-2026-09-06-final/COMPARISON_REPORT.md)、
[持久化验证清单与全部逐项误差](validation/ZEMAX_ENERGY_REPAIR_2026-09-06.json)。
独立捕获与文件哈希见
[`validation/zemax/2026-r1/energy-repair-2026-09-06`](../validation/zemax/2026-r1/energy-repair-2026-09-06/README.md)。

## 尚未解决的范围

衍射圈入能量、Huygens 离焦 MTF 仍有 Difference；
场 MTF、相对照度、Huygens PSF 剖面、Jones 光瞳和一阶出口瞳直径仍有 Close。
本轮没有通过降低容差把它们变成 Pass。另有 4 项点列布局 API 输出限制、
3 项物理定义不一致和 5 项图像/光源适配未完成；测试通过不代表这些项目已对齐。
后续需要分别验证衍射径向积分、离焦传播与相位参考，并实现共同源、相干性和探测器契约。

以下取每项 NRMSE 最大的比较分量；NRMSE 是比例，绝对误差不能跨不同物理量直接比较。

| 分析 | 结论 | 最大 NRMSE | 该分量最大绝对误差 | 单位 |
|---|---|---:|---:|---|
| Diffraction Encircled Energy | Difference | 0.013875843 | 0.050537607 | 无量纲 |
| Huygens Through Focus MTF | Difference | 0.032689150 | 0.094583405 | 无量纲 |
| Fourier MTF vs Field | Close | 0.004099609 | 0.009403880 | 无量纲 |
| Huygens MTF vs Field | Close | 0.007883622 | 0.014692832 | 无量纲 |
| Relative Illumination | Close | 0.008994666 | 0.018440785 | 无量纲 |
| Huygens PSF Cross Section | Close | 0.003725667 | 0.005391581 | 无量纲 |
| Jones Pupil | Close | 0.004526624 | 0.007171899 | 无量纲 |
| System Data Report（出口瞳直径） | Close | 2.454289e-6 | 2.529541e-5 | mm |

官方资料用于核对物理定义和设置含义；上面的离散结点规则由冻结的 2026 R1 捕获验证：
[衍射圈入能量说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html)、
[扩展源说明](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Extended_Source.html)、
[对比度损失与 GUI 平均 OPD 指示器](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v251/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Contrast_Loss_Map.html)。
