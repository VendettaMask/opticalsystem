# 2026-09-06 能量分析独立复验

这些是 Zemax OpticStudio 2026 R1（26.1 SP0 / API 260127）的冻结原始捕获，只由测试读取，不参与产品运行。
不是通用 Zemax 默认设置，也不替换已提交的 `123456.ZMX` 主基准。

| 输入 | SHA-256 | 捕获 |
|---|---|---|
| `primary.ZMX`，原文件 `123456.ZMX` | `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b` | 几何圈入能量、几何线/边缘扩散、对比度损失及原生相位数组 |
| `ms-l7.ZMX`，原文件 `[MS-L7](10X大NA大视场).ZMX` | `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8` | 扩展源圈入能量，最大距离分别为 5 / 20 µm |
| `source-image.IMA` | `37da6aae0e3408906b79656890741fa85c28c8a6039f15a889f74a014923ce10` | 仓库自有 3 × 3 等权均匀方形面积源 |

主基准两项均为配置 1、第一波长 0.42 µm、像面、非偏振、最大距离 5 µm、32 光瞳间隔。
圈入能量覆盖全部 5 个 RealImageHeight 视场；线/边缘扩散使用第一视场，比较 native Y 位移的 LSF / ERF 两列。
返回圈入能量 396 点；线/边缘扩散 101 点，原始四列全部保留。

主基准 Contrast Loss Map 使用第一视场、第一波长、13 × 13 光瞳、Frequency 0（本分析的 5% 截止频率）、
Normalize false、ShowOPD true。原生导出的相位数组与未偏移光瞳的波前一致；
官方 GUI 指示器所定义的两偏移光线平均 OPD 保留为另一种量，不纳入该数组的数值一致性声明。

扩展源捕获为 MS-L7 配置 1、第一视场、第一波长 0.4861327 µm、像面、质心参考、非偏振、
FieldSize 0.1 mm、RaysX1000 100、RemoveVignettingFactors true、MultiplyByDiffractionLimit false，源旋转 0。
每个 IMA 像素为面积发射源。原始三档 5 / 10 / 20 µm 捕获共同证明累计分箱的显示约定：
显示结点 `R*i/99` 对应 `CDF(R*(i-1)/99)`；插值后各返回 396 点，最后端点不输出。
10 µm 捕获保留在相邻 `ms-l7-analysis-expansion-2026-09-06/extended-source-encircled-energy-c1`，没有重写。
此约定只用于显式的兼容绘图输出，不移动追迹光线，不拟合强度或坐标比例；通用 Core CDF 保持直接半径定义。

Workbench 对照使用 C#、相同源图、7 × 7 像素内积分点、100000 条请求光线和确定性 Sobol 光瞳。
它不是 native 随机样本的逐条重放，剩余采样误差仍受原有能量容差检验。单位是 µm 与无量纲能量分数。
完整容差保存在 `comparison-settings.json`，各项读回设置、版本、模型及原始文本保存在各捕获目录。
manifest 为所有资产提供 SHA-256；原始绝对路径用于溯源，测试从冻结副本解析输入。

复验测试：`CapturedEnergyRepairTests`。当前数值、全量构建和测试状态见
[`docs/ZEMAX_ENERGY_REPAIR_2026-09-06.md`](../../../../docs/ZEMAX_ENERGY_REPAIR_2026-09-06.md)。
